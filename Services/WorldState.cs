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

    // Established romantic partnerships — stored as canonical (lesser, greater) string pairs.
    private readonly HashSet<(string, string)> _romances = new();

    // Personality profiles — set before the simulation loop begins.
    private readonly Dictionary<string, PersonalityProfile> _personalities = new();

    // Group system — shared across all agents.
    public IGroupSystem Groups { get; } = new GroupSystem();

    public ISurvivalSystem      Survival      { get; }
    public IMoodSystem          Mood          { get; }
    public IMemorySystem        Memory        { get; }
    public IItemSystem          Items         { get; }
    public IForagingSystem      Foraging      { get; }
    public ICommunicationSystem Communication { get; }
    public IAnimalSystem        Animals       { get; }
    public ICraftingSystem      Crafting      { get; }
    public IPresenceSystem      Presence      { get; }
    public IDayNightSystem      DayNight      { get; }
    public IWeatherSystem       Weather       { get; }

    public DayPhase CurrentPhase => DayNight.GetPhase(CurrentRound);
    public bool     IsNight      => DayNight.IsNight(CurrentRound);

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
        IPresenceSystem      presence,
        IDayNightSystem?     dayNight = null,
        IWeatherSystem?      weather  = null)
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
        DayNight      = dayNight ?? new DayNightSystem();
        Weather       = weather  ?? new WeatherSystem();

        Survival.Attach(this);
        Mood.Attach(this);
        Memory.Attach(this);
        Items.Attach(this);
        Foraging.Attach(this);
        Communication.Attach(this);
        Animals.Attach(this);
        Crafting.Attach(this);
        Presence.Attach(this);
        DayNight.Attach(this);
        Weather.Attach(this);
        Groups.Attach(this);
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
        // Pre-register MaxHealth from personality profile (profile must be set before this call)
        float maxHp = GetPersonality(agentName).MaxHealth;
        Survival.InitializeAgentHealth(agentName, maxHp);
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
                bool romantic = AreRomantic(name, agentName);
                bool protects = GetPersonality(name).HasFlag("protects_others");
                float moodHit   = romantic ? -50f : (protects ? -28f : -20f);
                float stressHit = romantic ? +35f : (protects ? +22f : +15f);
                m.AdjustMood(moodHit);
                m.AdjustStress(stressHit);
                string tag = romantic ? "  [romantic partner]" : (protects ? "  [protects_others]" : "");
                _devLog.Add($"[{name}] witnessed death of {agentName} → mood {moodHit:+0;-0}  stress {stressHit:+0}{tag}");
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
    public void TickDayNight(string agentName) => DayNight.TickAgentNight(agentName);

    /// Advances weather state once per round (call before any agent turns).
    public void TickWeather() => Weather.TickWeather();

    /// Applies per-turn outdoor warmth drain from current weather to a single agent.
    public void TickWeatherEffects(string agentName)
    {
        float drain = Weather.OutdoorWarmthDrain;
        if (drain <= 0f) return;

        if (!_agentLocations.TryGetValue(agentName, out var pos)) return;
        bool indoors = DayNightSystem.IsIndoors(GetCell(pos.x, pos.y).Terrain);
        if (indoors) return;

        Survival.AddHunger(agentName, -drain);
        LogDev($"[{agentName}] weather ({Weather.Label}) outdoors → hunger -{drain:F1}");
    }

    /// Fires the dawn boost for all living agents if this round crosses Night→Dawn.
    public void TickDawnBoost()
    {
        if (!DayNight.TryFireDawnBoost(CurrentRound)) return;

        foreach (var agentName in AgentNames)
        {
            if (Mood.Has(agentName))
            {
                var m = Mood.GetMood(agentName);
                m.AdjustMood(+8f);
                m.AdjustStress(-5f);
                m.AdjustHope(+4f);   // survived another night
                LogDev($"[{agentName}] dawn break → mood +8  stress -5  hope +4");
            }
            Memory.AddMemory(agentName, "The sky began to lighten. The worst of the night is over.");
            // Log the dawn announcement at each living agent's position so it reaches the event stream.
            var pos = GetAgentPosition(agentName);
            if (pos.x >= 0) LogAt(pos.x, pos.y, "Dawn: the first light of morning creeps across the city.");
        }
    }

    // ── Personality ──────────────────────────────────────────────────────────

    public void SetPersonality(string agentName, PersonalityProfile profile) =>
        _personalities[agentName] = profile;

    public PersonalityProfile GetPersonality(string agentName) =>
        _personalities.TryGetValue(agentName, out var p) ? p : PersonalityProfile.Default;

    // ── Romance ──────────────────────────────────────────────────────────────

    /// Canonical key so (A,B) and (B,A) map to the same slot.
    private static (string, string) RomancePair(string a, string b) =>
        string.Compare(a, b, StringComparison.Ordinal) <= 0 ? (a, b) : (b, a);

    public bool AreRomantic(string a, string b) =>
        _romances.Contains(RomancePair(a, b));

    /// Forms a romance between a and b if both agents have the open_to_romance flag,
    /// both trust each other at ≥ 85, and no romance already exists.
    public void TryFormRomance(string a, string b)
    {
        var pair = RomancePair(a, b);
        if (_romances.Contains(pair)) return;
        if (!GetPersonality(a).IsOpenToRomance) return;
        if (!GetPersonality(b).IsOpenToRomance) return;

        float trustAtoB = Mood.Has(a) ? Mood.GetMood(a).GetTrust(b) : 0f;
        float trustBtoA = Mood.Has(b) ? Mood.GetMood(b).GetTrust(a) : 0f;
        if (trustAtoB < 85f || trustBtoA < 85f) return;

        _romances.Add(pair);

        // Mutual emotional lift when the bond forms
        if (Mood.Has(a)) { var m = Mood.GetMood(a); m.AdjustMood(+12f); m.AdjustStress(-8f); }
        if (Mood.Has(b)) { var m = Mood.GetMood(b); m.AdjustMood(+12f); m.AdjustStress(-8f); }

        var posA = GetAgentPosition(a);
        var posB = GetAgentPosition(b);
        string notice = $"{a} and {b} have grown deeply close. Something has changed between them.";
        if (posA.x >= 0) LogAt(posA.x, posA.y, notice);
        if (posB.x >= 0 && posB != posA) LogAt(posB.x, posB.y, notice);
        Memory.AddMemory(a, $"Something shifted between you and {b}. You can't quite name it, but it's real.");
        Memory.AddMemory(b, $"Something shifted between you and {a}. You can't quite name it, but it's real.");
        LogDev($"[romance formed] {a} ↔ {b}  (trust A→B:{trustAtoB:F0}  B→A:{trustBtoA:F0})");
    }

    // ── Forwarders (back-compat) ─────────────────────────────────────────────

    public float GetHunger(string agentName) => Survival.GetHunger(agentName);
    public float GetThirst(string agentName) => Survival.GetThirst(agentName);
    public float GetHealth(string agentName)    => Survival.GetHealth(agentName);
    public float GetMaxHealth(string agentName) => Survival.GetMaxHealth(agentName);
    public void  AddHealth(string agentName, float delta) => Survival.AddHealth(agentName, delta);
    public bool  TickMeters(string agentName) => Survival.TickMeters(agentName);
    public bool  IsDead(string agentName) => Survival.IsDead(agentName);
    public float GetStamina(string agentName)  => Survival.GetStamina(agentName);
    public bool  IsExhausted(string agentName) => Survival.IsExhausted(agentName);
    public void  RecordActivity(string agentName, bool wasActive) => Survival.RecordActivity(agentName, wasActive);
    public int   GetIdleTurns(string agentName) => Survival.GetIdleTurns(agentName);

    public void TickStamina(string agentName)
    {
        var pos = GetAgentPosition(agentName);
        if (pos.x < 0) return;
        bool isStationary    = Presence.IsStationary(agentName);
        bool isIndoors       = DayNightSystem.IsIndoors(GetCell(pos.x, pos.y).Terrain);
        bool isRestingIndoors = isStationary && isIndoors && IsNight;
        Survival.TickStamina(agentName, isStationary, isRestingIndoors);
    }

    public string? TryForage(string agentName) => Foraging.TryForage(agentName);

    public void InitializeItems()   => Items.InitializeItems();
    public void InitializeAnimals() => Animals.InitializeAnimals();
    public void TickAnimals()       => Animals.TickAnimals();
    public void TickRespawn()       { Items.TickRespawn(); Animals.TickRespawn(); }
    public bool HasItemsAt(int x, int y) => Items.HasItemsAt(x, y);
    public IReadOnlyList<ItemInstance> GetItemsAt(int x, int y) => Items.GetItemsAt(x, y);
    public IReadOnlyList<ItemInstance> GetInventory(string agentName) => Items.GetInventory(agentName);
    public string? TryPickUp(string agentName, string id) => Items.TryPickUp(agentName, id);
    public string? TryDrop(string agentName, string id) => Items.TryDrop(agentName, id);
    public string TryUse(string agentName, string id) => Items.TryUse(agentName, id);
    public string? TryGive(string from, string id, string to) => Items.TryGive(from, id, to);
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
    public string? TryFeedAnimal(string agent, string id)                   => Animals.TryFeedAnimal(agent, id);

    public string? TryCraft(string agentName, string recipeId)             => Crafting.TryCraft(agentName, recipeId);
    public string? TryPlaceTrap(string agentName, string id)               => Items.TryPlaceTrap(agentName, id);

    public const int    TapPressureRounds = 48;
    public const string FountainName      = "Riverside Fountain";
    public const float MoveHungerCost    = 0.5f;  // hunger lost per cell walked
    public const float MoveThirstCost    = 1.0f;  // thirst lost per cell walked (exertion dehydrates faster)
    public const float MoveFatigueCost   = 2.5f;  // stamina lost per cell walked (was 5 — too aggressive)

    /// Returns true if the agent can currently use tap water at their location.
    /// Sets failReason to a user-facing message when false.
    private bool TapIsAvailable(string agentName, out string failReason)
    {
        if (!_agentLocations.TryGetValue(agentName, out var pos))
            { failReason = "agent not found"; return false; }
        var terrain = _map.GetCell(pos.x, pos.y).Terrain;
        if (terrain != TerrainType.Apartment && terrain != TerrainType.Storefront)
            { failReason = "no tap — must be inside an apartment building or a storefront/shop (bathrooms have sinks)"; return false; }
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
        if (!_agentLocations.TryGetValue(agentName, out _))
            return "agent not found";
        bool tapOk      = TapIsAvailable(agentName, out _);
        bool fountainOk = FountainIsAvailable(agentName, out _);
        bool riverOk    = RiverIsAvailable(agentName, out _);
        if (!tapOk && !fountainOk && !riverOk)
            return "no water source here — fill at an apartment tap, the Riverside Fountain, or the Irongate River";
        return Items.TryFill(agentName, instanceIdStr);
    }

    public static readonly HashSet<string> CookingTools = ["fire_steel", "camping_stove"];

    public string? TryFish(string agentName)
    {
        if (!_agentLocations.TryGetValue(agentName, out var pos)) return "agent not found";
        if (_map.GetCell(pos.x, pos.y).Terrain != TerrainType.River)
            return "must be at the river to fish";

        var hook = Items.GetInventory(agentName)
            .FirstOrDefault(i => i.DefinitionId == "fishing_hook" && i.UsesRemaining > 0);
        if (hook == null)
            return "need a Fishing Hook in your inventory to fish here";

        Items.ConsumeOneUse(agentName, hook.InstanceId.ToString());

        var rng = new Random();
        if (rng.NextDouble() > 0.65)
        {
            LogAt(pos.x, pos.y, $"{agentName} casts a line but catches nothing this time.");
            return "fishes — no catch";
        }

        bool added = Items.TryAddItemInstance(agentName, new ItemInstance("raw_river_fish"));
        if (!added) Items.PlaceItemAt("raw_river_fish", pos.x, pos.y);
        string dest = added ? "inventory" : "ground";
        LogAt(pos.x, pos.y, $"{agentName} catches a raw river fish! (placed in {dest})");
        if (Mood.Has(agentName)) { var m = Mood.GetMood(agentName); m.AdjustMood(+6f); m.AdjustStress(-5f); }
        LogDev($"[{agentName}] fish → caught raw_river_fish  mood +6  stress -5");
        Memory.AddMemory(agentName, $"Caught a raw river fish at ({pos.x},{pos.y}).");
        return "fishes — catches a raw river fish";
    }

    public string? TryCook(string agentName, string instanceIdStr)
    {
        if (!_agentLocations.TryGetValue(agentName, out var pos)) return "agent not found";

        var inventory = Items.GetInventory(agentName);
        var tool = inventory.FirstOrDefault(i => CookingTools.Contains(i.DefinitionId));
        if (tool == null)
            return "need a Fire Steel or Camping Stove in your inventory to cook";

        var target = inventory.FirstOrDefault(i =>
            string.Equals(i.InstanceId.ToString(), instanceIdStr, StringComparison.OrdinalIgnoreCase));
        if (target == null)
            return $"item {instanceIdStr} not found in inventory";

        var def = target.Definition;
        if (!def.IsCookable || string.IsNullOrEmpty(def.CookResult))
            return $"{def.Name} cannot be cooked";

        Items.TryConsume(agentName, target.InstanceId.ToString());
        bool added = Items.TryAddItemInstance(agentName, new ItemInstance(def.CookResult));
        if (!added) Items.PlaceItemAt(def.CookResult, pos.x, pos.y);
        var resultDef = ItemRegistry.Get(def.CookResult);
        string dest = added ? "inventory" : "ground";

        // Both fire_steel (50 uses) and camping_stove (10 uses) deplete on each cook.
        Items.ConsumeOneUse(agentName, tool.InstanceId.ToString());

        LogAt(pos.x, pos.y, $"{agentName} cooks {def.Name} → {resultDef.Name} (placed in {dest}).");
        if (Mood.Has(agentName)) { var m = Mood.GetMood(agentName); m.AdjustMood(+5f); m.AdjustStress(-3f); }
        LogDev($"[{agentName}] cook {def.Id} → {def.CookResult}  mood +5  stress -3");
        Memory.AddMemory(agentName, $"Cooked {def.Name} at ({pos.x},{pos.y}).");
        return $"cooks {def.Name} → {resultDef.Name}";
    }

    public string? TryPurify(string agentName, string instanceIdStr)
    {
        if (!_agentLocations.TryGetValue(agentName, out var pos)) return "agent not found";

        var inventory = Items.GetInventory(agentName);
        var tablet = inventory.FirstOrDefault(i =>
            i.DefinitionId == "purification_tablet" && i.UsesRemaining > 0);
        if (tablet == null)
            return "need a Purification Tablet with charges in your inventory to purify water";

        var target = inventory.FirstOrDefault(i =>
            string.Equals(i.InstanceId.ToString(), instanceIdStr, StringComparison.OrdinalIgnoreCase));
        if (target == null)
            return $"item {instanceIdStr} not found in inventory";

        var def = target.Definition;
        if (string.IsNullOrEmpty(def.PurifyResult))
            return $"{def.Name} cannot be purified — fill a container first, then purify it";

        Items.ConsumeOneUse(agentName, tablet.InstanceId.ToString());
        Items.TryConsume(agentName, target.InstanceId.ToString());
        bool added2 = Items.TryAddItemInstance(agentName, new ItemInstance(def.PurifyResult));
        if (!added2) Items.PlaceItemAt(def.PurifyResult, pos.x, pos.y);
        var resultDef2 = ItemRegistry.Get(def.PurifyResult);
        string dest2 = added2 ? "inventory" : "ground";

        LogAt(pos.x, pos.y, $"{agentName} purifies water → {resultDef2.Name} (placed in {dest2}).");
        LogDev($"[{agentName}] purify {def.Id} → {def.PurifyResult}");
        Memory.AddMemory(agentName, $"Purified water at ({pos.x},{pos.y}).");
        return $"purifies water → {resultDef2.Name}";
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

    /// Returns every entry in the cell log (used by the context renderer for the RECENT EVENTS block).
    public IReadOnlyList<string> GetAllCellLog(int x, int y) =>
        _locationLogs.TryGetValue((x, y), out var logs) ? logs.ToList() : Array.Empty<string>();

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
                    // Scale mood/trust gain from speech by the listener's Sociability.
                    // Very social listeners (+) respond warmly; introverts (−) barely register it.
                    var listenerPersonality = GetPersonality(name);
                    float socFactor = listenerPersonality.Sociability / 50f; // 0→0, 50→1, 100→2
                    // People Reader: trust and mood gains from hearing speech are 50% more effective.
                    float readerMult = listenerPersonality.IsPeopleReader ? 1.5f : 1.0f;
                    // distrusts_strangers: barely extends trust to people they don't know yet.
                    float distrustsMultiplier = (listenerPersonality.HasFlag("distrusts_strangers") && !KnowsName(name, agentName)) ? 0.25f : 1.0f;
                    float moodGain  = Math.Max(0f, 2f * socFactor * readerMult);
                    float stressRel = Math.Max(0f, 1f * socFactor * readerMult);
                    float trustGain = 3f * socFactor * readerMult * distrustsMultiplier;
                    var listenerMood = Mood.GetMood(name);
                    listenerMood.AdjustMood(moodGain);
                    listenerMood.AdjustStress(-stressRel);
                    listenerMood.AdjustTrust(agentName, trustGain);
                    string readerNote    = readerMult > 1          ? "  [people reader]"      : "";
                    string distrustNote = distrustsMultiplier < 1f ? "  [distrusts strangers]" : "";
                    _devLog.Add($"[{name}] heard {agentName} → mood +{moodGain:F1}  stress -{stressRel:F1}  trust[{agentName}] +{trustGain:F1}  (soc={listenerPersonality.Sociability}){readerNote}{distrustNote}");
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
        Survival.DrainStamina(agentName, MoveFatigueCost);
        LogDev($"[{agentName}] moved → hunger -{MoveHungerCost}  thirst -{MoveThirstCost}  stamina -{MoveFatigueCost}");

        return true;
    }

    /// Returns agents within earshot of <paramref name="agentName"/>, excluding self.
    /// Outdoors (street, park, forest, river): anyone within 1 cell.
    /// Indoors (apartment, storefront, industrial): same cell AND same floor only.
    public IReadOnlyList<(string name, int x, int y)> GetVisibleAgents(string agentName)
    {
        if (!_agentLocations.TryGetValue(agentName, out var pos)) return Array.Empty<(string, int, int)>();
        var cell = _map.GetCell(pos.x, pos.y);
        bool isIndoor = cell.Terrain is TerrainType.Apartment or TerrainType.Storefront or TerrainType.Industrial;
        if (isIndoor)
        {
            int myFloor = GetAgentFloor(agentName);
            return _agentLocations
                .Where(kv => kv.Key != agentName && kv.Value == pos && GetAgentFloor(kv.Key) == myFloor)
                .Select(kv => (kv.Key, kv.Value.x, kv.Value.y))
                .ToList();
        }
        // Wind storm / thunderstorm: outdoor voices can only be heard in the same cell.
        int radius = Weather.ReducesSpeechRange ? 0 : 1;
        return GetAgentsInRadius(pos.x, pos.y, radius).Where(a => a.name != agentName).ToList();
    }

    public string? TryDepositToGroupStash(string agentName, string instanceIdStr)
        => Groups.TryDeposit(agentName, instanceIdStr);

    public string? TryWithdrawFromGroupStash(string agentName, string instanceIdStr)
        => Groups.TryWithdraw(agentName, instanceIdStr);

    // ── Context generation ───────────────────────────────────────────────────
    // Rendering is delegated to AgentContextBuilder; WorldState only holds state.

    public string GetContext(string agentName) => AgentContextBuilder.Build(this, agentName);

    // -- Internal helpers --------------------------------------------------------

    public static string CompassDirection(int fromX, int fromY, int toX, int toY)
    {
        int dx = toX - fromX, dy = toY - fromY;
        if (dx == 0 && dy == 0) return "here";
        double deg = (Math.Atan2(dy, dx) * 180.0 / Math.PI + 360.0) % 360.0;
        return deg switch
        {
            < 22.5  or >= 337.5 => "E",
            < 67.5              => "SE",
            < 112.5             => "S",
            < 157.5             => "SW",
            < 202.5             => "W",
            < 247.5             => "NW",
            < 292.5             => "N",
            _                   => "NE",
        };
    }

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
