using System.Text;
using MultiAgentSimWeb.Models;
using MultiAgentSimWeb.Services.Systems;

namespace MultiAgentSimWeb.Services;

public class WorldState
{
    public string Situation { get; }
    private readonly MapGrid _map;
    private readonly Dictionary<string, (int x, int y)>        _agentLocations = new();
    private readonly Dictionary<string, int>                    _agentFloors    = new();
    private readonly Dictionary<(int x, int y), List<string>>  _locationLogs   = new();
    private readonly Dictionary<string, Queue<(int x, int y)>> _agentTrails    = new();
    private readonly List<string>   _devLog        = new();

    // Social knowledge: observer -> set of other agents whose NAME the observer has
    // learned. Everyone starts as strangers; a name is learned only when its owner
    // states it in speech nearby (self-introduction).
    private readonly Dictionary<string, HashSet<string>> _knownNames = new();

    public ISurvivalSystem      Survival      { get; }
    public IMoodSystem          Mood          { get; }
    public IMemorySystem        Memory        { get; }
    public IItemSystem          Items         { get; }
    public IForagingSystem      Foraging      { get; }
    public ICommunicationSystem Communication { get; }
    public IAnimalSystem        Animals       { get; }
    public ICraftingSystem      Crafting      { get; }
    public IPresenceSystem      Presence      { get; }

    public int      MapWidth     => _map.Width;
    public int      MapHeight    => _map.Height;
    public GridCell GetCell(int x, int y) => _map.GetCell(x, y);

    public int CurrentRound { get; set; } = 0;

    // Power went out at 23:47. Each round = 1 hour.
    public string CurrentTime
    {
        get
        {
            int totalMinutes = 23 * 60 + 47 + (CurrentRound - 1) * 60;
            int h = (totalMinutes / 60) % 24;
            int m = totalMinutes % 60;
            string ampm = h < 12 ? "AM" : "PM";
            int h12 = h % 12 == 0 ? 12 : h % 12;
            return $"{h12}:{m:D2} {ampm}";
        }
    }
    public bool     IsInBounds(int x, int y) => _map.IsInBounds(x, y);

    public WorldState(string situation, MapGrid map,
        ISurvivalSystem      survival,
        IMoodSystem          mood,
        IMemorySystem        memory,
        IItemSystem          items,
        IForagingSystem      foraging,
        ICommunicationSystem communication,
        IAnimalSystem        animals,
        ICraftingSystem      crafting,
        IPresenceSystem      presence)
    {
        Situation     = situation;
        _map          = map;
        Survival      = survival;
        Mood          = mood;
        Memory        = memory;
        Items         = items;
        Foraging      = foraging;
        Communication = communication;
        Animals       = animals;
        Crafting      = crafting;
        Presence      = presence;

        Survival.Attach(this);
        Mood.Attach(this);
        Memory.Attach(this);
        Items.Attach(this);
        Foraging.Attach(this);
        Communication.Attach(this);
        Animals.Attach(this);
        Crafting.Attach(this);
        Presence.Attach(this);
    }

    /// Convenience constructor with default system implementations.
    public WorldState(string situation, MapGrid map)
        : this(situation, map,
               new SurvivalSystem(),
               new MoodSystem(),
               new MemorySystem(),
               new ItemSystem(),
               new ForagingSystem(),
               new CommunicationSystem(),
               new AnimalSystem(),
               new CraftingSystem(),
               new PresenceSystem())
    { }

    // ── Agent lifecycle ──────────────────────────────────────────────────────

    public void InitializeAgent(string agentName, int x, int y)
    {
        _agentLocations[agentName] = (x, y);
        _agentFloors[agentName] = 1;
        _agentTrails[agentName] = new Queue<(int, int)>();
        Survival.InitializeAgent(agentName);
        Mood.InitializeAgent(agentName);
        Memory.InitializeAgent(agentName);
        Items.InitializeAgent(agentName);
        Communication.InitializeAgent(agentName);
        _knownNames[agentName] = new HashSet<string>();
        GetOrCreateLog((x, y));
    }

    // ── Social identity (strangers until introduced) ─────────────────────────

    /// True if <paramref name="observer"/> knows <paramref name="target"/>'s name.
    /// You always know your own name.
    public bool KnowsName(string observer, string target) =>
        observer == target ||
        (_knownNames.TryGetValue(observer, out var known) && known.Contains(target));

    /// Records that <paramref name="observer"/> has learned <paramref name="target"/>'s name.
    public void LearnName(string observer, string target)
    {
        if (observer == target) return;
        if (!_knownNames.TryGetValue(observer, out var known))
            _knownNames[observer] = known = new HashSet<string>();
        known.Add(target);
    }

    /// How <paramref name="observer"/> refers to <paramref name="target"/>: their real
    /// name once learned, otherwise an anonymous descriptor.
    public string DescribeAgent(string observer, string target) =>
        KnowsName(observer, target) ? target : "an unknown person";

    /// True if <paramref name="speech"/> spoken by <paramref name="speaker"/> reveals
    /// the speaker's own name (a self-introduction), so listeners can learn it.
    private static bool SpeechRevealsName(string speaker, string speech)
    {
        if (string.IsNullOrWhiteSpace(speech)) return false;
        // Match the full name or the first name as a standalone word, case-insensitive.
        foreach (var token in new[] { speaker, speaker.Split(' ')[0] })
        {
            if (token.Length < 2) continue;
            int idx = speech.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                bool leftOk  = idx == 0 || !char.IsLetter(speech[idx - 1]);
                int end = idx + token.Length;
                bool rightOk = end >= speech.Length || !char.IsLetter(speech[end]);
                if (leftOk && rightOk) return true;
                idx = speech.IndexOf(token, idx + 1, StringComparison.OrdinalIgnoreCase);
            }
        }
        return false;
    }

    /// Drops all inventory to current cell and removes agent from all tracking.
    public void KillAgent(string agentName)
    {
        if (!_agentLocations.TryGetValue(agentName, out var pos)) return;

        // Emotional impact on survivors within 2 cells
        var deathCause = Survival.DeathCause(agentName);
        foreach (var (name, _, _) in GetAgentsInRadius(pos.x, pos.y, 2).Where(a => a.name != agentName))
        {
            if (Mood.Has(name))
            {
                var m = Mood.GetMood(name);
                m.AdjustMood(-20f);
                m.AdjustStress(+15f);
                _devLog.Add($"[{name}] witnessed death of {agentName} → mood -20  stress +15");
            }
            Memory.AddMemory(name, $"Witnessed {DescribeAgent(name, agentName)} die of {deathCause} nearby.");
        }

        Items.DropInventoryAt(agentName, pos.x, pos.y);
        GetOrCreateLog(pos).Add($"{agentName} has perished here.");

        _agentLocations.Remove(agentName);
        _agentFloors.Remove(agentName);
        _agentTrails.Remove(agentName);
        Presence.RemoveAgent(agentName);
        Survival.RemoveAgent(agentName);
        Mood.RemoveAgent(agentName);
        Memory.RemoveAgent(agentName);
        Items.RemoveAgent(agentName);
        Communication.RemoveAgent(agentName);
    }

    public void TickPresence(string agentName) => Presence.TickPresence(agentName);

    // ── Forwarders (back-compat) ─────────────────────────────────────────────

    public float GetHunger(string agentName) => Survival.GetHunger(agentName);
    public float GetThirst(string agentName) => Survival.GetThirst(agentName);
    public bool  TickMeters(string agentName) => Survival.TickMeters(agentName);
    public bool  IsDead(string agentName) => Survival.IsDead(agentName);

    public string? TryForage(string agentName) => Foraging.TryForage(agentName);

    public void InitializeItems()   => Items.InitializeItems();
    public void InitializeAnimals() => Animals.InitializeAnimals();
    public void TickAnimals()       => Animals.TickAnimals();
    public void TickRespawn()       { Items.TickRespawn(); Animals.TickRespawn(); }
    public bool HasItemsAt(int x, int y) => Items.HasItemsAt(x, y);
    public IReadOnlyList<ItemInstance> GetItemsAt(int x, int y) => Items.GetItemsAt(x, y);
    public IReadOnlyList<ItemInstance> GetInventory(string agentName) => Items.GetInventory(agentName);
    public bool TryPickUp(string agentName, string id) => Items.TryPickUp(agentName, id);
    public bool TryDrop(string agentName, string id) => Items.TryDrop(agentName, id);
    public string TryUse(string agentName, string id) => Items.TryUse(agentName, id);
    public bool TryGive(string from, string id, string to) => Items.TryGive(from, id, to);
    public (bool consumed, bool success, IReadOnlyList<string> yielded) TryDeconstruct(string agentName, string id) =>
        Items.TryDeconstruct(agentName, id);

    public AgentMood   GetMood(string agentName) => Mood.GetMood(agentName);
    public void        TickMood(string agentName) => Mood.TickMood(agentName);
    public AgentMemory GetMemory(string agentName) => Memory.GetMemory(agentName);
    public void        AddMemory(string agentName, string entry) => Memory.AddMemory(agentName, entry);

    public void                      QueueDirectMessage(DirectMessage msg) => Communication.QueueDirectMessage(msg);
    public bool                      HasPendingMessages(string agentName) => Communication.HasPendingMessages(agentName);
    public IReadOnlyList<DirectMessage> DrainPendingMessages(string agentName) => Communication.DrainPendingMessages(agentName);

    public IReadOnlyList<Animal> GetAllAnimals()                            => Animals.AllAnimals;
    public IReadOnlyList<Animal> GetAnimalsAt(int x, int y)                => Animals.GetAnimalsAt(x, y);
    public IReadOnlyList<Animal> GetAnimalsInRadius(int cx, int cy, int r)  => Animals.GetAnimalsInRadius(cx, cy, r);
    public string? TryAttackAnimal(string agent, string id)                 => Animals.TryAttackAnimal(agent, id);
    public string? TryTrapAnimal(string agent, string id)                   => Animals.TryTrapAnimal(agent, id);
    public string? TryScareAnimal(string agent, string id)                  => Animals.TryScareAnimal(agent, id);

    public string? TryCraft(string agentName, string recipeId)             => Crafting.TryCraft(agentName, recipeId);
    public string? TryPlaceTrap(string agentName, string id)               => Items.TryPlaceTrap(agentName, id);

    private const int    TapPressureRounds = 48;
    private const string FountainName      = "Riverside Fountain";
    private const float MoveHungerCost    = 0.5f;  // hunger lost per cell walked
    private const float MoveThirstCost    = 1.0f;  // thirst lost per cell walked (exertion dehydrates faster)

    /// Returns true if the agent can currently use tap water at their location.
    /// Sets failReason to a user-facing message when false.
    private bool TapIsAvailable(string agentName, out string failReason)
    {
        if (!_agentLocations.TryGetValue(agentName, out var pos))
            { failReason = "agent not found"; return false; }
        if (_map.GetCell(pos.x, pos.y).Terrain != TerrainType.Apartment)
            { failReason = "no tap — must be inside an apartment building"; return false; }
        if (CurrentRound > TapPressureRounds)
            { failReason = "the taps have run dry — no water pressure"; return false; }
        failReason = "";
        return true;
    }

    /// Returns true if the agent is standing on any River terrain cell.
    private bool RiverIsAvailable(string agentName, out string failReason)
    {
        if (!_agentLocations.TryGetValue(agentName, out var pos))
            { failReason = "agent not found"; return false; }
        if (_map.GetCell(pos.x, pos.y).Terrain != TerrainType.River)
            { failReason = "no river here — must be standing at the Irongate River or its banks"; return false; }
        failReason = "";
        return true;
    }

    /// Returns true if the agent is standing at the Riverside Fountain.
    private bool FountainIsAvailable(string agentName, out string failReason)
    {
        if (!_agentLocations.TryGetValue(agentName, out var pos))
            { failReason = "agent not found"; return false; }
        if (_map.GetCell(pos.x, pos.y).BuildingName != FountainName)
            { failReason = "no fountain here — must be at the Riverside Fountain"; return false; }
        failReason = "";
        return true;
    }

    public string? TryDrinkTap(string agentName)
    {
        if (!TapIsAvailable(agentName, out var fail)) return fail;
        var pos = _agentLocations[agentName];
        float restore = CurrentRound <= 36 ? 30f : 20f;
        Survival.AddThirst(agentName, restore);
        Memory.AddMemory(agentName, $"Drank from a tap/sink at ({pos.x},{pos.y}). (+{restore:F0} thirst)");
        LogDev($"[{agentName}] drink_tap → thirst +{restore:F0}");
        return $"drinks from the tap (+{restore:F0} thirst)";
    }

    public string? TryDrinkRiver(string agentName)
    {
        if (!RiverIsAvailable(agentName, out var fail)) return fail;
        var pos = _agentLocations[agentName];
        const float restore = 25f;
        Survival.AddThirst(agentName, restore);
        Memory.AddMemory(agentName, $"Drank from the river at ({pos.x},{pos.y}). (+{restore:F0} thirst)");
        LogDev($"[{agentName}] drink_river → thirst +{restore:F0}");
        return $"drinks from the river (+{restore:F0} thirst)";
    }

    public string? TryDrinkFountain(string agentName)
    {
        if (!FountainIsAvailable(agentName, out var fail)) return fail;
        var pos = _agentLocations[agentName];
        const float restore = 20f;
        Survival.AddThirst(agentName, restore);
        Memory.AddMemory(agentName, $"Drank from the Riverside Fountain at ({pos.x},{pos.y}). (+{restore:F0} thirst)");
        LogDev($"[{agentName}] drink_fountain → thirst +{restore:F0}");
        return $"drinks from the fountain (+{restore:F0} thirst)";
    }

    /// Fills a container item with water (tap, fountain, or river).
    public string? TryFillContainer(string agentName, string instanceIdStr)
    {
        bool tapOk      = TapIsAvailable(agentName, out _);
        bool fountainOk = FountainIsAvailable(agentName, out _);
        bool riverOk    = RiverIsAvailable(agentName, out _);
        if (!tapOk && !fountainOk && !riverOk)
            return "no water source here — fill at an apartment tap, the Riverside Fountain, or the Irongate River";
        return Items.TryFill(agentName, instanceIdStr);
    }
    public IReadOnlyList<ItemInstance> GetPlacedTrapsAt(int x, int y)      => Items.GetPlacedTrapsAt(x, y);

    public IReadOnlyList<SimEvent> DrainAnimalEvents() => Animals.DrainEvents();

    // ── Position queries ─────────────────────────────────────────────────────

    public (int x, int y) GetAgentPosition(string agentName) =>
        _agentLocations.TryGetValue(agentName, out var pos) ? pos : (-1, -1);

    public IReadOnlyList<string> GetAgentsAtPosition(int x, int y) =>
        _agentLocations.Where(kv => kv.Value == (x, y)).Select(kv => kv.Key).ToList();

    public IReadOnlyList<string> GetCellLog(int x, int y) =>
        _locationLogs.TryGetValue((x, y), out var logs) ? logs.TakeLast(2).ToList() : Array.Empty<string>();

    public IReadOnlyList<(string name, int x, int y)> GetAgentsInRadius(int cx, int cy, int radius = 1) =>
        _agentLocations
            .Where(kv => Math.Abs(kv.Value.x - cx) <= radius && Math.Abs(kv.Value.y - cy) <= radius)
            .Select(kv => (kv.Key, kv.Value.x, kv.Value.y))
            .ToList();

    public IReadOnlyCollection<string> AgentNames => _agentLocations.Keys;

    public IReadOnlyList<(int x, int y)> GetAgentTrail(string agentName) =>
        _agentTrails.TryGetValue(agentName, out var t) ? t.ToList() : Array.Empty<(int, int)>();

    public int  GetAgentFloor(string agentName) => _agentFloors.TryGetValue(agentName, out var f) ? f : 1;
    public void SetAgentFloor(string agentName, int floor) => _agentFloors[agentName] = floor;

    // ── Shared infrastructure for systems ────────────────────────────────────

    public void LogAt(int x, int y, string entry) => GetOrCreateLog((x, y)).Add(entry);
    public void LogDev(string msg) => _devLog.Add(msg);

    public IReadOnlyList<string> DrainDevLog()
    {
        var copy = _devLog.ToList();
        _devLog.Clear();
        return copy;
    }

    // ── Event logging (cross-system: speech ripples to listeners) ────────────

    public void AddEvent(string agentName, AgentAction action)
    {
        if (!_agentLocations.TryGetValue(agentName, out var pos)) return;
        var log = GetOrCreateLog(pos);

        if (!string.IsNullOrWhiteSpace(action.Speech))
        {
            log.Add($"{agentName} says: \"{action.Speech}\"");

            if (Mood.Has(agentName))
            {
                Mood.GetMood(agentName).AdjustMood(+2f);
                _devLog.Add($"[{agentName}] spoke → mood +2");
            }

            // If the speaker stated their own name, nearby listeners learn it.
            bool introduces = SpeechRevealsName(agentName, action.Speech);

            foreach (var (name, _, _) in GetAgentsInRadius(pos.x, pos.y, 1).Where(a => a.name != agentName))
            {
                if (introduces && !KnowsName(name, agentName))
                {
                    LearnName(name, agentName);
                    _devLog.Add($"[{name}] learned that the stranger is {agentName}");
                }

                if (Mood.Has(name))
                {
                    var listenerMood = Mood.GetMood(name);
                    listenerMood.AdjustMood(+2f);
                    listenerMood.AdjustStress(-1f);
                    listenerMood.AdjustTrust(agentName, +3f);
                    _devLog.Add($"[{name}] heard {agentName} → mood +2  stress -1  trust[{agentName}] +3");
                }

                var speakerLabel = DescribeAgent(name, agentName);
                Memory.AddMemory(name, $"Heard {speakerLabel} say: \"{action.Speech}\".");
            }
        }

        if (!string.IsNullOrWhiteSpace(action.Action) && action.Action != "nothing")
            log.Add($"{agentName} does: {action.Action}");
    }

    // ── Movement ─────────────────────────────────────────────────────────────

    public bool MoveAgent(string agentName, string direction)
    {
        if (string.IsNullOrWhiteSpace(direction)) return false;
        if (!_agentLocations.TryGetValue(agentName, out var pos)) return false;

        var (ox, oy) = pos;
        var (dx, dy) = direction.ToUpperInvariant() switch
        {
            "N" => ( 0, -1),
            "S" => ( 0,  1),
            "E" => ( 1,  0),
            "W" => (-1,  0),
            _   => ( 0,  0)
        };

        if (dx == 0 && dy == 0) return false;

        int nx = ox + dx, ny = oy + dy;
        if (!_map.IsInBounds(nx, ny)) return false;

        var fromDir = direction.ToUpperInvariant() switch
        {
            "N" => "south", "S" => "north", "E" => "west", "W" => "east", _ => "unknown"
        };

        GetOrCreateLog((ox, oy)).Add($"{agentName} moves {direction.ToUpperInvariant()}.");
        _agentLocations[agentName] = (nx, ny);
        if (!_agentTrails.TryGetValue(agentName, out var trail))
            _agentTrails[agentName] = trail = new Queue<(int, int)>();
        trail.Enqueue((nx, ny));
        if (trail.Count > 6) trail.Dequeue();
        GetOrCreateLog((nx, ny)).Add($"{agentName} arrives from the {fromDir}.");
        var destCell = _map.GetCell(nx, ny);
        string destLabel = destCell.BuildingName != null
            ? $"{destCell.BuildingName} ({destCell.DisplayName})"
            : destCell.DisplayName;
        Memory.AddMemory(agentName, $"Moved to {destLabel} at ({nx},{ny}).");

        // Moving costs energy — small penalty on top of the per-turn decay.
        Survival.AddHunger(agentName, -MoveHungerCost);
        Survival.AddThirst(agentName, -MoveThirstCost);
        LogDev($"[{agentName}] moved → hunger -{MoveHungerCost}  thirst -{MoveThirstCost}");

        return true;
    }

    // ── Context generation ───────────────────────────────────────────────────

    public string GetContext(string agentName)
    {
        if (!_agentLocations.TryGetValue(agentName, out var pos))
            return "ERROR: agent not initialized";

        var (cx, cy) = pos;
        var cell = _map.GetCell(cx, cy);
        var sb = new StringBuilder();

        sb.AppendLine($"SITUATION:\n{Situation}");
        sb.AppendLine();
        sb.AppendLine($"TIME: {CurrentTime} (approximately {CurrentRound} hour{(CurrentRound == 1 ? "" : "s")} since the blackout)");
        sb.AppendLine($"YOUR POSITION: ({cx}, {cy})");
        string terrainLabel = cell.BuildingName != null
            ? $"{cell.BuildingName} ({cell.DisplayName})"
            : cell.DisplayName;
        sb.AppendLine($"TERRAIN: {terrainLabel} — {cell.Description}");
        if (cell.Floors > 1)
        {
            int agentFloor = GetAgentFloor(agentName);
            sb.AppendLine($"FLOOR: You are on floor {agentFloor} of {cell.Floors}. Use move_floor: \"up\" or \"down\" to change floors.");
        }
        if (cell.Terrain == TerrainType.Apartment && CurrentRound <= TapPressureRounds)
        {
            string tapNote = CurrentRound <= 36
                ? "TAP WATER: Taps and sinks still have pressure. Set drink_tap: true to drink (+30 thirst)."
                : "TAP WATER: Water pressure is weakening — may not last much longer. Set drink_tap: true to drink (+20 thirst).";
            sb.AppendLine(tapNote);
        }

        if (cell.BuildingName == FountainName)
        {
            sb.AppendLine("FOUNTAIN: A stone fountain — underground pressure keeps the water flowing. Set drink_fountain: true to drink (+20 thirst). You can also fill containers here (use item_action \"fill\").");
        }

        if (cell.Terrain == TerrainType.River)
        {
            sb.AppendLine("RIVER: The Irongate River flows here — clean enough to drink. Set drink_river: true to drink (+25 thirst). You can also fill containers here (use item_action \"fill\"). The river never runs dry.");
        }

        if (cell.Terrain == TerrainType.Forest)
        {
            sb.AppendLine("FOREST: Dense woodland. Set scavenge: true to forage for wild berries and mushrooms. Animals are present — foxes are harmless, but deer can be hunted for meat.");
        }

        // Shelter status
        var shelterPos = Presence.GetShelter(agentName);
        bool hasShelter = shelterPos.x >= 0;
        bool atShelter  = hasShelter && shelterPos == pos;
        if (atShelter)
        {
            sb.AppendLine("YOUR SHELTER: You are at your base. Familiarity eases your stress slightly each turn you stay here.");
        }
        else if (hasShelter)
        {
            var sc = _map.GetCell(shelterPos.x, shelterPos.y);
            int dist = Math.Abs(shelterPos.x - cx) + Math.Abs(shelterPos.y - cy);
            string sName = sc.BuildingName ?? sc.DisplayName;
            sb.AppendLine($"YOUR SHELTER: {sName} ({shelterPos.x},{shelterPos.y}) — {dist} step{(dist == 1 ? "" : "s")} away. Items you left there may still be waiting.");
        }

        sb.AppendLine();

        float hunger = Survival.GetHunger(agentName);
        float thirst = Survival.GetThirst(agentName);
        sb.AppendLine($"HUNGER: {hunger:F0}/100 ({Survival.HungerLabel(hunger)})");
        sb.AppendLine($"THIRST: {thirst:F0}/100 ({Survival.ThirstLabel(thirst)})");
        if (Survival.IsCritical(agentName))
            sb.AppendLine("WARNING: You are in danger of dying from starvation or dehydration. Find food or water NOW.");
        sb.AppendLine();

        if (Mood.Has(agentName))
        {
            var ctxMood = Mood.GetMood(agentName);
            sb.AppendLine($"EMOTIONAL STATE:");
            sb.AppendLine($"  Mood: {ctxMood.MoodLabel} ({ctxMood.Mood:+0;-0;0})  |  Stress: {ctxMood.StressLabel} ({ctxMood.Stress:F0})");
            // Only people whose name you've learned — you have no opinion of strangers
            // you haven't met.
            var knownPeers = _agentLocations.Keys
                .Where(n => n != agentName && KnowsName(agentName, n))
                .ToList();
            if (knownPeers.Count > 0)
            {
                sb.AppendLine("  Attitudes toward people you've met:");
                foreach (var peer in knownPeers)
                {
                    float t = ctxMood.GetTrust(peer);
                    var mp = Presence.GetMeetingPoint(agentName, peer);
                    string mpNote = mp.Item1 >= 0
                        ? $"  [last met: {_map.GetCell(mp.Item1, mp.Item2).BuildingName ?? _map.GetCell(mp.Item1, mp.Item2).DisplayName} ({mp.Item1},{mp.Item2})]"
                        : "";
                    sb.AppendLine($"    {peer} — {AgentMood.TrustLabel(t)} ({t:+0;-0;0}){mpNote}");
                }
            }
            sb.AppendLine("  Your emotional state is real. Let it shape your tone, choices, and how you treat others.");
            sb.AppendLine();
        }

        var ctxMemory = Memory.GetMemory(agentName);
        if (ctxMemory.Recent.Count > 0)
        {
            sb.AppendLine(ctxMemory.Format());
            sb.AppendLine();
        }

        sb.AppendLine("NEARBY (within 1 cell):");

        (int dx, int dy, string label)[] dirs =
        [
            ( 0, -1, "North"), ( 0,  1, "South"),
            ( 1,  0, "East"),  (-1,  0, "West"),
            ( 1, -1, "NE"),    (-1, -1, "NW"),
            ( 1,  1, "SE"),    (-1,  1, "SW"),
        ];

        foreach (var (dx, dy, label) in dirs)
        {
            int nx = cx + dx, ny = cy + dy;
            if (_map.IsInBounds(nx, ny))
            {
                var nc = _map.GetCell(nx, ny);
                string ncLabel = nc.BuildingName != null ? $"{nc.BuildingName} ({nc.DisplayName})" : nc.DisplayName;
                sb.AppendLine($"  {label} ({nx},{ny}): {ncLabel}");
            }
            else
            {
                sb.AppendLine($"  {label}: [edge of map]");
            }
        }

        sb.AppendLine();
        var nearby = GetAgentsInRadius(cx, cy, 1).Where(a => a.name != agentName).ToList();
        if (nearby.Count > 0)
        {
            sb.AppendLine("OTHERS NEARBY:");
            foreach (var (name, nx, ny) in nearby)
            {
                int dist = Math.Max(Math.Abs(nx - cx), Math.Abs(ny - cy));
                var who = DescribeAgent(agentName, name);
                sb.AppendLine($"  {who} is at ({nx},{ny}) [{dist} cell{(dist == 1 ? "" : "s")} away]");
            }
        }
        else
        {
            sb.AppendLine("OTHERS NEARBY: No one nearby.");
        }

        // Direct message: you can only address someone privately if you know their name.
        // To reach a stranger, speak generally (everyone nearby hears it) and introduce yourself.
        sb.AppendLine();
        var knownNearby = nearby.Where(a => KnowsName(agentName, a.name)).ToList();
        if (knownNearby.Count > 0)
        {
            sb.AppendLine("DIRECT MESSAGE — to address someone privately, copy their name exactly into address_agent:");
            foreach (var (name, _, _) in knownNearby)
                sb.AppendLine($"  \"{name}\"");
        }
        else if (nearby.Count > 0)
        {
            sb.AppendLine("DIRECT MESSAGE: You don't know anyone nearby by name yet — speak generally to be heard, and say your name to introduce yourself. Leave address_agent empty.");
        }
        else
        {
            sb.AppendLine("DIRECT MESSAGE: No one within range — leave address_agent empty.");
        }

        sb.AppendLine();
        var exits = new List<string>();
        if (_map.IsInBounds(cx, cy - 1)) exits.Add("N");
        if (_map.IsInBounds(cx, cy + 1)) exits.Add("S");
        if (_map.IsInBounds(cx + 1, cy)) exits.Add("E");
        if (_map.IsInBounds(cx - 1, cy)) exits.Add("W");
        sb.AppendLine($"EXITS: {(exits.Count > 0 ? string.Join(", ", exits) : "none")}");

        if (Foraging.CanForage(cell.Terrain))
            sb.AppendLine("SCAVENGE: Set \"scavenge\": true to search this building for supplies. Resolves at your final position AFTER any movement this turn.");
        else
            sb.AppendLine("SCAVENGE: Not available here — must be inside an Apartment or Storefront. Move into a building first, then scavenge next turn.");

        sb.AppendLine();
        var inv = Items.GetInventory(agentName);
        int capacity = Items.GetCarryCapacity(agentName);
        var containers = inv.Where(i => i.Definition.CarryCapacity > 0).ToList();
        string containerNote = containers.Count > 0
            ? " — " + string.Join(", ", containers.Select(c => $"{c.Definition.Name}: +{c.Definition.CarryCapacity}"))
            : "";
        sb.AppendLine($"YOUR INVENTORY ({inv.Count}/{capacity} slots{containerNote}):");
        if (inv.Count > 0)
            foreach (var it in inv)
                sb.AppendLine($"  [{it.InstanceId}] {it.DisplayName} — {it.Definition.Description}");
        else
            sb.AppendLine("  (empty)");
        if (Items.IsInventoryFull(agentName))
            sb.AppendLine("  ⚠ INVENTORY FULL — drop or use an item before picking up more. Find a bag or backpack to expand capacity.");

        sb.AppendLine();
        var cellItemList = Items.GetItemsAt(cx, cy);
        sb.AppendLine("ITEMS HERE (at your position):");
        if (cellItemList.Count > 0)
            foreach (var it in cellItemList)
                sb.AppendLine($"  [{it.InstanceId}] {it.DisplayName} — {it.Definition.Description}");
        else
            sb.AppendLine("  (none)");

        var armedTraps = Items.GetPlacedTrapsAt(cx, cy);
        if (armedTraps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("TRAPS ARMED HERE:");
            foreach (var t in armedTraps)
                sb.AppendLine($"  [{t.InstanceId}] {t.DisplayName} — armed and waiting");
            sb.AppendLine("  (Small animals that wander here will be caught. Pick up with pick_up to retrieve.)");
        }

        sb.AppendLine();
        sb.AppendLine("ITEM ACTIONS: pick_up, drop, use, give, deconstruct, craft, fill, place_trap");
        sb.AppendLine("  Set \"item_action\" to one of the above (or \"none\"), \"item_target_id\" to the item's ID string,");
        sb.AppendLine("  \"item_give_to\" to the recipient agent's exact name (for \"give\" only).");
        sb.AppendLine("  For \"craft\": set \"craft_recipe_id\" to the recipe ID (see CRAFTING below); item_target_id unused.");
        sb.AppendLine("  For \"place_trap\": set item_target_id to the Improved Trap's instance ID to arm it here.");
        sb.AppendLine("  You may only do one item action per turn.");

        var available = RecipeRegistry.GetAvailable(Items.GetInventory(agentName));
        sb.AppendLine();
        sb.AppendLine("CRAFTING (recipes you can make right now with your current inventory):");
        if (available.Count > 0)
        {
            foreach (var r in available)
            {
                var ingNames = string.Join(" + ", r.Ingredients.Select(id => ItemRegistry.Get(id).Name));
                sb.AppendLine($"  [{r.Id}] {r.Name} — needs: {ingNames} — {r.Description}");
            }
        }
        else
        {
            sb.AppendLine("  (none available — collect components to unlock recipes)");
        }

        sb.AppendLine();
        sb.AppendLine("CRAFTING TIPS:");
        sb.AppendLine("  WEAPONS: Shiv (+20 dmg) and Crude Knife (+12 dmg) boost every animal attack automatically — no equip needed.");
        sb.AppendLine("  TRAPS: Craft an Improved Trap, then use place_trap to arm it at your location.");
        sb.AppendLine("         It stays active after you leave. Small animals that wander onto it are caught (85% chance).");
        sb.AppendLine("         Retrieve it with pick_up. Cook carcasses with a Lighter for better hunger restore.");

        sb.AppendLine();
        var cellAnimals = Animals.GetAnimalsAt(cx, cy);
        sb.AppendLine("ANIMALS IN YOUR CELL:");
        if (cellAnimals.Count > 0)
        {
            foreach (var a in cellAnimals)
            {
                string sizeTag = a.Size == AnimalSize.Large ? "[LARGE — DANGEROUS]" : "[small]";
                sb.AppendLine($"  {sizeTag} {a.DisplayName} [id:{a.Id}] — HP:{a.Health:F0}/{a.MaxHealth:F0} — {a.Description}");
            }
        }
        else
        {
            sb.AppendLine("  (none)");
        }

        sb.AppendLine();
        var nearbyAnimals = Animals.GetAnimalsInRadius(cx, cy, 3)
            .Where(a => !(a.X == cx && a.Y == cy))
            .OrderBy(a => Math.Max(Math.Abs(a.X - cx), Math.Abs(a.Y - cy)))
            .ToList();
        sb.AppendLine("ANIMALS NEARBY (within 3 cells):");
        if (nearbyAnimals.Count > 0)
        {
            foreach (var a in nearbyAnimals)
            {
                int dist = Math.Max(Math.Abs(a.X - cx), Math.Abs(a.Y - cy));
                string sizeTag = a.Size == AnimalSize.Large ? "[LARGE]" : "[small]";
                string stateStr = a.State switch
                {
                    AnimalState.Hunting => " — HUNTING",
                    AnimalState.Fleeing => " — fleeing",
                    _ => ""
                };
                sb.AppendLine($"  {sizeTag} {a.DisplayName} at ({a.X},{a.Y}) [{dist} cell{(dist == 1 ? "" : "s")} away]{stateStr}");
            }
        }
        else
        {
            sb.AppendLine("  (none nearby)");
        }

        sb.AppendLine();
        sb.AppendLine("ANIMAL ACTIONS: attack, trap, scare, none");
        sb.AppendLine("  Set \"animal_action\" to one of the above, \"animal_target_id\" to the animal's [id:...] string.");
        sb.AppendLine("  attack — strike an animal IN YOUR CELL. Risky against large ones (they counter-attack).");
        sb.AppendLine("  trap   — catch a SMALL animal in your cell using a Wire Bundle from your inventory.");
        sb.AppendLine("  scare  — attempt to frighten a LARGE animal within 2 cells away. May backfire.");
        sb.AppendLine("  none   — take no animal action (default).");
        sb.AppendLine("  You may only do one animal action per turn, combined with a normal item action if desired.");

        sb.AppendLine();
        sb.AppendLine("RECENT EVENTS IN YOUR AREA:");
        var allEntries = new List<string>();
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int lx = cx + dx, ly = cy + dy;
                if (_map.IsInBounds(lx, ly) &&
                    _locationLogs.TryGetValue((lx, ly), out var log) &&
                    log.Count > 0)
                    allEntries.AddRange(log);
            }
        sb.AppendLine(allEntries.Count > 0
            ? string.Join("\n", allEntries)
            : "(Nothing has happened nearby yet.)");

        sb.AppendLine();
        sb.Append("MOVE OPTIONS: \"N\", \"S\", \"E\", or \"W\" to move one step, or \"\" to stay.");

        return sb.ToString();
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    private List<string> GetOrCreateLog((int x, int y) pos)
    {
        if (!_locationLogs.TryGetValue(pos, out var log))
        {
            log = new List<string>();
            _locationLogs[pos] = log;
        }
        if (log.Count > 20) log.RemoveAt(0);
        return log;
    }
}
