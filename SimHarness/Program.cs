using MultiAgentSimWeb.Models;
using MultiAgentSimWeb.Services;

// ── Resolve agents.md relative to the solution root ──────────────────────────
var agentsFile = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../Content/agents.md"));

if (!File.Exists(agentsFile))
{
    Console.Error.WriteLine($"ERROR: agents.md not found at {agentsFile}");
    return;
}

var profiles  = AgentProfileService.LoadFromFile(agentsFile);
var agentDefs = profiles.Agents.Take(6).ToList();

Console.WriteLine($"Loaded {agentDefs.Count} agents from agents.md");
Console.WriteLine($"Situation: {profiles.Situation[..Math.Min(80, profiles.Situation.Length)]}...\n");

// ── Scenarios ────────────────────────────────────────────────────────────────

RunScenario(
    name:        "1. Idle to Death",
    description: "All agents do nothing every turn. Shows raw survival timer.",
    maxRounds:   60,
    agentDefs:   agentDefs,
    profiles:    profiles,
    script:      (_, _, _) => new AgentAction(),
    trackPrompts: false);

RunScenario(
    name:        "2. Smart Survival",
    description: "Agents drink tap when thirsty, scavenge when hungry, stay inside.",
    maxRounds:   80,
    agentDefs:   agentDefs,
    profiles:    profiles,
    script:      SmartSurvivalScript,
    trackPrompts: false);

RunScenario(
    name:        "3. Prompt Size Over Time",
    description: "Mixed activity for 10 rounds — tracks how large the LLM prompt grows.",
    maxRounds:   10,
    agentDefs:   agentDefs,
    profiles:    profiles,
    script:      SmartSurvivalScript,
    trackPrompts: true);

// ── Scenario runner ───────────────────────────────────────────────────────────

static void RunScenario(
    string name,
    string description,
    int maxRounds,
    List<AgentDefinition> agentDefs,
    AgentProfiles profiles,
    Func<WorldState, string, int, AgentAction> script,
    bool trackPrompts)
{
    Header(name, description);

    var world = BuildWorld(profiles.Situation, agentDefs);
    world.InitializeItems();

    var alive      = agentDefs.Select(a => a.Name).ToList();
    var deathRound = new Dictionary<string, int>();
    var promptLog  = new Dictionary<string, List<int>>();

    if (trackPrompts)
        foreach (var a in alive)
            promptLog[a] = new List<int>();

    for (int round = 1; round <= maxRounds && alive.Count > 0; round++)
    {
        world.CurrentRound = round;
        Console.WriteLine($"── Round {round} ".PadRight(60, '─'));

        foreach (var agent in alive.ToList())
        {
            if (trackPrompts)
                promptLog[agent].Add(AgentContextBuilder.Build(world, agent).Length);

            var action    = script(world, agent, round);
            var narrative = ActionResolver.NarrativeEvents(agent, "#fff", action).ToList();
            var events    = ActionResolver.Resolve(world, agent, "#fff", action, round).ToList();

            if (narrative.Count == 0 && events.Count == 0)
            {
                Console.WriteLine($"  {agent,-22} (idle)");
            }
            else
            {
                foreach (var ev in narrative.Where(e => e.Type is "thought" or "speech"))
                    Console.WriteLine($"  {agent,-22} {ev.Label,-6} {ev.Content}");
                foreach (var ev in events)
                    Console.WriteLine($"  {agent,-22} [{ev.Type,-12}] {ev.Content}");
            }

            world.RecordActivity(agent, action.IsActive);
        }

        // Tick meters and kill agents whose meters hit zero
        foreach (var agent in alive.ToList())
        {
            bool fatal = world.TickMeters(agent);
            if (fatal)
            {
                float h = world.GetHunger(agent), t = world.GetThirst(agent), hp = world.GetHealth(agent);
                string cause = hp <= 0 ? "health" : t <= 0 ? "thirst" : "hunger";
                Console.WriteLine($"\n  *** {agent} DIED (round {round}, cause: {cause}) ***\n");
                deathRound[agent] = round;
                world.KillAgent(agent);
                alive.Remove(agent);
                promptLog.Remove(agent);
            }
        }

        // Survival stats table
        if (alive.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  {"Agent",-22} {"Hunger",7} {"Thirst",7} {"Health",7} {"Stam",7}");
            Console.WriteLine($"  {"─────",-22} {"──────",7} {"──────",7} {"──────",7} {"────",7}");
            foreach (var agent in alive)
            {
                string warn = world.GetThirst(agent) < 20 || world.GetHunger(agent) < 20 ? " !" : "";
                Console.WriteLine(
                    $"  {agent,-22} {world.GetHunger(agent),7:F0} {world.GetThirst(agent),7:F0}" +
                    $" {world.GetHealth(agent),7:F0} {world.GetStamina(agent),7:F0}{warn}");
            }
            Console.WriteLine();
        }
    }

    // ── Summary ───────────────────────────────────────────────────────────────
    Console.WriteLine(new string('─', 60));
    Console.WriteLine("SUMMARY");
    Console.WriteLine(new string('─', 60));
    foreach (var def in agentDefs)
    {
        if (deathRound.TryGetValue(def.Name, out int dr))
            Console.WriteLine($"  {def.Name,-22} died round {dr}");
        else
            Console.WriteLine($"  {def.Name,-22} survived all {maxRounds} rounds");
    }

    // ── Prompt size table ─────────────────────────────────────────────────────
    if (trackPrompts && promptLog.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("PROMPT SIZE (chars) PER ROUND:");
        int totalRounds = promptLog.Values.Max(v => v.Count);

        Console.Write($"  {"Agent",-22}");
        for (int r = 1; r <= totalRounds; r++) Console.Write($"  R{r,-5}");
        Console.WriteLine();

        foreach (var (agent, sizes) in promptLog)
        {
            Console.Write($"  {agent,-22}");
            foreach (var sz in sizes) Console.Write($"  {sz,-6}");
            Console.WriteLine();
        }

        // Also print token estimates (rough: 1 token ≈ 4 chars)
        Console.WriteLine();
        Console.WriteLine("ESTIMATED TOKENS (÷4):");
        Console.Write($"  {"Agent",-22}");
        for (int r = 1; r <= totalRounds; r++) Console.Write($"  R{r,-5}");
        Console.WriteLine();
        foreach (var (agent, sizes) in promptLog)
        {
            Console.Write($"  {agent,-22}");
            foreach (var sz in sizes) Console.Write($"  {sz / 4,-6}");
            Console.WriteLine();
        }
    }

    Console.WriteLine();
}

// ── Action scripts ────────────────────────────────────────────────────────────

static AgentAction SmartSurvivalScript(WorldState world, string agent, int round)
{
    var (x, y)   = world.GetAgentPosition(agent);
    var cell     = world.GetCell(x, y);
    bool indoor  = cell.Terrain is TerrainType.Apartment or TerrainType.Storefront;
    float thirst = world.GetThirst(agent);
    float hunger = world.GetHunger(agent);
    var inv      = world.GetInventory(agent);

    // Drink from tap if thirsty and indoors (while pressure holds)
    if (thirst < 65f && indoor && round <= WorldState.TapPressureRounds)
        return new AgentAction { DrinkTap = true };

    // Use a drink from inventory
    if (thirst < 50f)
    {
        var drink = inv.FirstOrDefault(i => i.Definition.ThirstRestore > 0 && i.Definition.IsUsable);
        if (drink != null)
            return new AgentAction { ItemAction = "use", ItemTargetId = drink.InstanceId.ToString() };
    }

    // Eat from inventory
    if (hunger < 50f)
    {
        var food = inv.FirstOrDefault(i => i.Definition.HungerRestore > 0 && i.Definition.IsUsable);
        if (food != null)
            return new AgentAction { ItemAction = "use", ItemTargetId = food.InstanceId.ToString() };
    }

    // Pick up useful ground items
    if (!world.Items.IsInventoryFull(agent))
    {
        var pickup = world.GetItemsAt(x, y)
            .FirstOrDefault(i => i.Definition.IsUsable || i.Definition.HungerRestore > 0 || i.Definition.ThirstRestore > 0);
        if (pickup != null)
            return new AgentAction { ItemAction = "pick_up", ItemTargetId = pickup.InstanceId.ToString() };
    }

    // Scavenge if indoors
    if (indoor)
        return new AgentAction { Scavenge = true };

    // Move toward the central apartment block (13,13)
    string dir = x < 13 ? "E" : x > 13 ? "W" : y > 13 ? "N" : "S";
    return new AgentAction { MoveTo = dir };
}

// ── World setup ───────────────────────────────────────────────────────────────

static WorldState BuildWorld(string situation, List<AgentDefinition> defs)
{
    var world = new WorldState(situation, MapGrid.CreateDefault());
    var (sx, sy) = MapGrid.DefaultStartPosition;

    foreach (var def in defs)
    {
        world.InitializeAgent(def.Name, sx, sy);
        if (def.Profile is { } p)
        {
            world.SetPersonality(def.Name, p);
            world.Survival.InitializeAgentHealth(def.Name, p.MaxHealth);
        }
    }

    return world;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

static void Header(string name, string description)
{
    var bar = new string('═', 62);
    Console.WriteLine();
    Console.WriteLine($"╔{bar}╗");
    Console.WriteLine($"║  {name,-60}║");
    Console.WriteLine($"║  {description,-60}║");
    Console.WriteLine($"╚{bar}╝");
    Console.WriteLine();
}
