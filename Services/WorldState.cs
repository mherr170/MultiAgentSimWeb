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
                LogDev($"[{agentName}] dawn break → mood +8  stress -5");
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
    private const float MoveFatigueCost   = 2.5f;  // stamina lost per cell walked (was 5 — too aggressive)

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
        if (!_agentLocations.TryGetValue(agentName, out _))
            return "agent not found";
        bool tapOk      = TapIsAvailable(agentName, out _);
        bool fountainOk = FountainIsAvailable(agentName, out _);
        bool riverOk    = RiverIsAvailable(agentName, out _);
        if (!tapOk && !fountainOk && !riverOk)
            return "no water source here — fill at an apartment tap, the Riverside Fountain, or the Irongate River";
        return Items.TryFill(agentName, instanceIdStr);
    }

    private static readonly HashSet<string> CookingTools = ["fire_steel", "camping_stove"];

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

    public string GetContext(string agentName)
    {
        if (!_agentLocations.TryGetValue(agentName, out var pos))
            return "ERROR: agent not initialized";

        var (cx, cy) = pos;
        var cell = _map.GetCell(cx, cy);
        var sb = new StringBuilder();
        var inv = Items.GetInventory(agentName);

        sb.AppendLine($"SITUATION:\n{Situation}");
        sb.AppendLine();
        sb.AppendLine($"TIME: {CurrentTime} (approximately {CurrentRound} hour{(CurrentRound == 1 ? "" : "s")} since the blackout)");

        // Day/Night context block
        var phase = CurrentPhase;
        string phaseLabel = phase switch
        {
            DayPhase.Night => "Night",
            DayPhase.Dawn  => "Dawn",
            DayPhase.Day   => "Day",
            DayPhase.Dusk  => "Dusk",
            _              => ""
        };
        sb.AppendLine($"DAY PHASE: {phaseLabel}");

        if (phase == DayPhase.Night)
        {
            var nightPos = GetAgentPosition(agentName);
            bool nightIndoors = nightPos.x >= 0 &&
                DayNightSystem.IsIndoors(GetCell(nightPos.x, nightPos.y).Terrain);
            if (!nightIndoors)
            {
                sb.AppendLine("DARKNESS: The city is pitch black. Animals are bolder tonight — large predators hunt more aggressively.");
                bool hasWarmth = DayNight.HasWarmth(agentName);
                if (!hasWarmth)
                    sb.AppendLine("COLD: You have no warmth (blanket or winter coat). The cold is draining your energy — you lose extra hunger each turn.");
                else
                    sb.AppendLine("WARMTH: Your gear keeps out the cold.");
                bool hasLight = DayNight.HasLightSource(agentName);
                if (!hasLight)
                    sb.AppendLine("LIGHT: No light source — scavenging is harder in the dark. A flashlight, candle, or lighter would help.");
                else
                    sb.AppendLine("LIGHT: Your light source helps you navigate — but it also makes you visible to predators at greater distance.");
            }
            else
            {
                sb.AppendLine("SHELTERED: You are indoors for the night. No cold drain.");
                if (Presence.IsStationary(agentName))
                    sb.AppendLine("REST: You are resting — staying still indoors tonight slowly restores health and reduces stress (+2 health, -5 stress per turn).");
                else
                    sb.AppendLine("REST: Stay in one place indoors to rest and recover health and stress.");
            }
        }
        else if (phase == DayPhase.Dawn)
        {
            sb.AppendLine("DAWN: The sky is lightening. The night's dangers are fading — the city is waking up.");
        }
        else if (phase == DayPhase.Day)
        {
            sb.AppendLine("DAYLIGHT: Full visibility. Best conditions for movement, scavenging, and exploring new areas.");
        }
        else if (phase == DayPhase.Dusk)
        {
            sb.AppendLine("DUSK: Light is fading. Animals will be bolder soon — consider finding shelter before full dark.");
        }

        sb.AppendLine(Weather.GetContextBlock(agentName));

        sb.AppendLine($"YOUR POSITION: ({cx}, {cy})");
        string terrainLabel = cell.BuildingName != null
            ? $"{cell.BuildingName} ({cell.DisplayName})"
            : cell.DisplayName;
        sb.AppendLine($"TERRAIN: {terrainLabel} — {cell.Description}");

        // Personality context — remind the agent who they are and surface flag-driven notes
        var personality = GetPersonality(agentName);
        if (!string.IsNullOrWhiteSpace(personality.Blurb))
        {
            sb.AppendLine();
            sb.AppendLine($"CHARACTER: {personality.Blurb}");
            foreach (var flag in personality.Flags)
            {
                string? flagNote = flag switch
                {
                    "hoards_food"         => "NOTE (personality): You have a strong instinct to stockpile food and water rather than consume them early. Scarcity is real and you've seen what happens when supplies run out.",
                    "distrusts_strangers" => "NOTE (personality): Your gut tells you not to trust people who haven't proven themselves. Strangers are potential threats until demonstrated otherwise.",
                    "fears_dark"          => "NOTE (personality): The darkness and silence of the blackout are getting to you more than you'd like to admit. Being alone in the dark is difficult.",
                    "protects_others"     => "NOTE (personality): Despite everything, you feel a pull toward protecting people who are weaker or more scared than you — even at cost to yourself.",
                    "prone_to_panic"      => "NOTE (personality): Under extreme pressure your emotions can spiral. High stress makes you less rational and more reactive — watch for it.",
                    "night_owl"           => "NOTE (personality): The night energises you. The dark city feels more alive to you than it should — you're more at ease out here than most.",
                    "claustrophobic"      => "NOTE (personality): Enclosed spaces put you on edge. Staying indoors for too long builds pressure inside you that's hard to shake.",
                    "self_reliant"        => "NOTE (personality): You're used to solving your own problems. Help offered freely makes you more uncomfortable than grateful — it implies debt.",
                    "paranoid"            => "NOTE (personality): You struggle to fully trust people you don't know. Unknown faces nearby keep you watchful and wired regardless of whether the threat is real.",
                    "risk_taker"          => "NOTE (personality): You've always leaned into danger rather than away from it. High-stakes moments sharpen you instead of paralyzing you.",
                    "open_to_romance"     => "NOTE (personality): You're not closing yourself off emotionally, even now. If something real develops with someone you trust deeply, you won't run from it.",
                    _                     => null
                };
                if (flagNote != null) sb.AppendLine(flagNote);
            }
            sb.AppendLine();
        }

        if (cell.Floors > 1)
        {
            int agentFloor = GetAgentFloor(agentName);
            sb.AppendLine($"FLOOR: You are on floor {agentFloor} of {cell.Floors}. Use move_floor: \"up\" or \"down\" to change floors.");
        }
        if (cell.Terrain == TerrainType.Apartment && CurrentRound <= TapPressureRounds)
        {
            string tapNote = CurrentRound <= 36
                ? "TAP WATER: Taps and sinks still have pressure. Set drink_tap: true to drink (+30 thirst)."
                : "TAP WATER: Water pressure is weakening -- may not last much longer. Set drink_tap: true to drink (+20 thirst).";
            sb.AppendLine(tapNote);
        }

        if (cell.BuildingName == FountainName)
        {
            sb.AppendLine("FOUNTAIN: A stone fountain -- underground pressure keeps the water flowing. Set drink_fountain: true to drink (+20 thirst). You can also fill containers here (use item_action \"fill\").");
        }

        if (cell.Terrain == TerrainType.River)
        {
            sb.AppendLine("RIVER: The Irongate River flows here -- clean enough to drink. Set drink_river: true to drink (+25 thirst). You can also fill containers here (use item_action \"fill\"). The river never runs dry.");
            bool hasFishingHook = inv.Any(i => i.DefinitionId == "fishing_hook" && i.UsesRemaining > 0);
            if (hasFishingHook)
                sb.AppendLine("FISHING: You have a Fishing Hook. Set fish: true to cast a line (65% catch chance). Each cast uses one hook charge. Raw fish must be cooked before eating.");
            else
                sb.AppendLine("FISHING: You need a Fishing Hook to fish here. Search near riverbanks to find one.");
            bool hasPurifyTablet = inv.Any(i => i.DefinitionId == "purification_tablet" && i.UsesRemaining > 0);
            bool hasFilledContainer = inv.Any(i => !string.IsNullOrEmpty(i.Definition.PurifyResult));
            if (hasPurifyTablet && hasFilledContainer)
                sb.AppendLine("PURIFY: You have a Purification Tablet and a filled container. Use item_action \"purify\" on the container's ID to produce safe stored water (+35 thirst when drunk, stores indefinitely).");
        }

        if (cell.Terrain == TerrainType.Forest)
        {
            bool hasForagingKnife = inv.Any(i => i.DefinitionId == "foraging_knife");
            string knifeNote = hasForagingKnife
                ? " Your Foraging Knife grants bonus rolls for extra berries and mushrooms."
                : " A Foraging Knife (found deeper in the forest or on hunters) would improve yields.";
            sb.AppendLine($"FOREST: Dense woodland. Set scavenge: true to forage for wild berries and mushrooms.{knifeNote} Animals are present -- foxes are harmless, but deer can be hunted for meat.");
        }

        bool hasCookingTool = inv.Any(i => CookingTools.Contains(i.DefinitionId));
        var cookableItems = inv.Where(i => i.Definition.IsCookable && !string.IsNullOrEmpty(i.Definition.CookResult)).ToList();
        if (hasCookingTool && cookableItems.Count > 0)
        {
            sb.AppendLine("COOKING: You have a cooking tool and raw food. Use item_action \"cook\" with the raw item's ID to cook it:");
            foreach (var ci in cookableItems)
                sb.AppendLine($"  [{ci.InstanceId}] {ci.Definition.Name} => {ItemRegistry.Get(ci.Definition.CookResult).Name}");
        }
        else if (cookableItems.Count > 0)
        {
            sb.AppendLine("COOKING: You have raw food but no cooking tool (Fire Steel or Camping Stove needed to cook).");
        }

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
            sb.AppendLine($"YOUR SHELTER: {sName} ({shelterPos.x},{shelterPos.y}) -- {dist} step{(dist == 1 ? "" : "s")} away. Items you left there may still be waiting.");
        }

        sb.AppendLine();

        float hunger = Survival.GetHunger(agentName);
        float thirst = Survival.GetThirst(agentName);
        float health = Survival.GetHealth(agentName);
        float maxHp  = Survival.GetMaxHealth(agentName);
        float stamina = Survival.GetStamina(agentName);
        sb.AppendLine($"HUNGER: {hunger:F0}/100 ({Survival.HungerLabel(hunger)})");
        sb.AppendLine($"THIRST: {thirst:F0}/100 ({Survival.ThirstLabel(thirst)})");
        sb.AppendLine($"HEALTH: {health:F0}/{maxHp:F0} ({Survival.HealthLabel(health)})");
        sb.AppendLine($"STAMINA: {stamina:F0}/100 ({Survival.StaminaLabel(stamina)})");
        if (Survival.IsCritical(agentName))
            sb.AppendLine("WARNING: You are in danger of dying from starvation or dehydration. Find food or water NOW.");
        if (Survival.IsHealthCritical(agentName))
            sb.AppendLine("WARNING: You are critically injured. Find and use medical supplies (first_aid_kit, bandage_roll, antiseptic, surgical_kit) immediately.");
        if (Survival.IsExhausted(agentName))
            sb.AppendLine("WARNING: You are exhausted. Rest by staying indoors at night to recover stamina. Scavenging is less effective while exhausted.");
        int idleTurns = Survival.GetIdleTurns(agentName);
        if (idleTurns >= 3)
            sb.AppendLine($"NOTE: You have been idle for {idleTurns} turn{(idleTurns == 1 ? "" : "s")} without purpose. Restlessness is setting in — move, scavenge, or find someone to talk to.");
        sb.AppendLine();

        if (Mood.Has(agentName))
        {
            var ctxMood = Mood.GetMood(agentName);
            sb.AppendLine($"EMOTIONAL STATE:");
            sb.AppendLine($"  Mood: {ctxMood.MoodLabel} ({ctxMood.Mood:+0;-0;0})  |  Stress: {ctxMood.StressLabel} ({ctxMood.Stress:F0})");
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
                    string romanceNote = AreRomantic(agentName, peer) ? " [romantic partner]" : "";
                    sb.AppendLine($"    {peer} -- {AgentMood.TrustLabel(t)} ({t:+0;-0;0}){romanceNote}{mpNote}");
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
        var nearby = GetVisibleAgents(agentName);
        if (nearby.Count > 0)
        {
            sb.AppendLine("OTHERS NEARBY:");
            foreach (var (name, nx, ny) in nearby)
            {
                int dist = Math.Max(Math.Abs(nx - cx), Math.Abs(ny - cy));
                string distNote = dist == 0 ? "same cell" : $"{dist} cell{(dist == 1 ? "" : "s")} away";
                var who = DescribeAgent(agentName, name);
                string relationNote = "";
                if (KnowsName(agentName, name) && Mood.Has(agentName))
                {
                    float t = Mood.GetMood(agentName).GetTrust(name);
                    if (AreRomantic(agentName, name))
                        relationNote = " [romantic partner]";
                    else if (t > 70f)
                        relationNote = " [close friend]";
                    else if (t < -30f)
                        relationNote = " [hostile]";
                }
                sb.AppendLine($"  {who} is at ({nx},{ny}) [{distNote}]{relationNote}");
            }
        }
        else
        {
            sb.AppendLine("OTHERS NEARBY: No one nearby.");
        }

        var agentGroup    = Groups.GetGroup(agentName);
        var pendingInvite = Groups.GetPendingInvite(agentName);

        if (agentGroup != null)
        {
            sb.AppendLine();
            sb.AppendLine($"GROUP -- \"{agentGroup.Name}\":");

            var memberParts = new List<string>();
            foreach (var m in agentGroup.Members)
            {
                if (m == agentName) { memberParts.Add($"{m} (you)"); continue; }
                var mp = GetAgentPosition(m);
                if (mp == (-1, -1)) { memberParts.Add($"{m} (dead)"); continue; }
                int mdist = Math.Max(Math.Abs(mp.x - cx), Math.Abs(mp.y - cy));
                string prox = mdist == 0 ? "same cell" : $"{mdist} cell{(mdist == 1 ? "" : "s")} away";
                memberParts.Add($"{m} ({prox})");
            }
            sb.AppendLine($"  Members: {string.Join(", ", memberParts)}");

            if (agentGroup.Waypoint is { } wp)
            {
                int wdist = Math.Abs(wp.x - cx) + Math.Abs(wp.y - cy);
                var wCell = _map.GetCell(wp.x, wp.y);
                string wName = wCell.BuildingName ?? wCell.DisplayName;
                string wDir  = CompassDirection(cx, cy, wp.x, wp.y);
                sb.AppendLine($"  Waypoint: \"{wp.Description}\" -- {wName} ({wp.x},{wp.y}), {wdist} step{(wdist == 1 ? "" : "s")} {wDir} (set by {wp.SetBy})");
            }

            if (agentGroup.StashLocation is { } sl)
            {
                var sc    = _map.GetCell(sl.x, sl.y);
                string sn = sc.BuildingName ?? sc.DisplayName;
                bool atStash = sl.x == cx && sl.y == cy;
                if (atStash)
                {
                    sb.AppendLine($"  GROUP STASH -- you are here ({sl.x},{sl.y}) -- {agentGroup.Stash.Count} item{(agentGroup.Stash.Count == 1 ? "" : "s")}:");
                    if (agentGroup.Stash.Count > 0)
                        foreach (var si in agentGroup.Stash)
                            sb.AppendLine($"    [{si.InstanceId}] {si.DisplayName} -- {si.Definition.Description}");
                    else
                        sb.AppendLine("    (empty)");
                    sb.AppendLine("    Use item_action \"deposit\" (item_target_id = inventory item ID) to add, \"withdraw\" (item_target_id = stash item ID) to take.");
                }
                else
                {
                    int sdist = Math.Abs(sl.x - cx) + Math.Abs(sl.y - cy);
                    string sDir = CompassDirection(cx, cy, sl.x, sl.y);
                    sb.AppendLine($"  Group stash: {agentGroup.Stash.Count} item{(agentGroup.Stash.Count == 1 ? "" : "s")} at {sn} ({sl.x},{sl.y}) -- {sdist} step{(sdist == 1 ? "" : "s")} {sDir}");
                }
            }
            else
            {
                sb.AppendLine("  Group stash: not established -- use item_action \"deposit\" anywhere to create it at your current cell.");
            }

            if (agentGroup.ActiveVote is { } vote)
            {
                sb.AppendLine();
                int yes2    = vote.Votes.Values.Count(v => v == "yes");
                int no2     = vote.Votes.Values.Count(v => v == "no");
                int pending2 = agentGroup.Members.Count(m => GetAgentPosition(m) != (-1, -1)) - vote.Votes.Count;
                sb.AppendLine($"  ACTIVE VOTE: \"{vote.Question}\" (proposed by {vote.Proposer})");
                sb.AppendLine($"    {yes2} yes / {no2} no / {Math.Max(0, pending2)} pending");
                if (vote.Votes.TryGetValue(agentName, out var myVote))
                    sb.AppendLine($"    You voted: \"{myVote}\"");
                else
                    sb.AppendLine("    You have not voted yet -- set group_vote to \"yes\" or \"no\" this turn.");
            }

            sb.AppendLine("  (Leave group: set leave_group: true. Set meeting point: set group_set_waypoint to a short label.)");
        }
        else if (pendingInvite is { } invite)
        {
            sb.AppendLine();
            sb.AppendLine($"GROUP INVITE: {invite.Inviter} has invited you to join \"{invite.GroupName}\".");
            sb.AppendLine("  Set accept_group_invite: true to join, or leave it false to decline (invite expires after this round).");
        }

        sb.AppendLine();
        var knownNearby = nearby.Where(a => KnowsName(agentName, a.name)).ToList();
        if (knownNearby.Count > 0)
        {
            sb.AppendLine("DIRECT MESSAGE -- to address someone privately, copy their name exactly into address_agent:");
            foreach (var (name, _, _) in knownNearby)
                sb.AppendLine($"  \"{name}\"");
        }
        else if (nearby.Count > 0)
        {
            sb.AppendLine("DIRECT MESSAGE: You don't know anyone nearby by name yet -- speak generally to be heard, and say your name to introduce yourself. Leave address_agent empty.");
        }
        else
        {
            sb.AppendLine("DIRECT MESSAGE: No one within range -- leave address_agent empty.");
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
            sb.AppendLine("SCAVENGE: Not available here -- must be inside an Apartment or Storefront. Move into a building first, then scavenge next turn.");

        sb.AppendLine();
        int capacity = Items.GetCarryCapacity(agentName);
        var containers = inv.Where(i => i.Definition.CarryCapacity > 0).ToList();
        string containerNote = containers.Count > 0
            ? " -- " + string.Join(", ", containers.Select(c => $"{c.Definition.Name}: +{c.Definition.CarryCapacity}"))
            : "";
        sb.AppendLine($"YOUR INVENTORY ({inv.Count}/{capacity} slots{containerNote}):");
        if (inv.Count > 0)
            foreach (var it in inv)
                sb.AppendLine($"  [{it.InstanceId}] {it.DisplayName} -- {it.Definition.Description}");
        else
            sb.AppendLine("  (empty)");
        if (Items.IsInventoryFull(agentName))
            sb.AppendLine("  WARNING: INVENTORY FULL -- drop or use an item before picking up more. Find a bag or backpack to expand capacity.");

        sb.AppendLine();
        var cellItemList = Items.GetItemsAt(cx, cy);
        sb.AppendLine("ITEMS HERE (at your position):");
        if (cellItemList.Count > 0)
            foreach (var it in cellItemList)
                sb.AppendLine($"  [{it.InstanceId}] {it.DisplayName} -- {it.Definition.Description}");
        else
            sb.AppendLine("  (none)");

        var armedTraps = Items.GetPlacedTrapsAt(cx, cy);
        if (armedTraps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("TRAPS ARMED HERE:");
            foreach (var t in armedTraps)
                sb.AppendLine($"  [{t.InstanceId}] {t.DisplayName} -- armed and waiting");
            sb.AppendLine("  (Small animals that wander here will be caught. Pick up with pick_up to retrieve.)");
        }

        sb.AppendLine();
        sb.AppendLine("ITEM ACTIONS: pick_up, drop, use, give, deconstruct, craft, fill, place_trap, cook, purify");
        sb.AppendLine("  Set \"item_action\" to one of the above (or \"none\"), \"item_target_id\" to the item's ID string,");
        sb.AppendLine("  \"item_give_to\" to the recipient agent's exact name (for \"give\" only).");
        sb.AppendLine("  For \"craft\": set \"craft_recipe_id\" to the recipe ID (see CRAFTING below); item_target_id unused.");
        sb.AppendLine("  For \"place_trap\": set item_target_id to the Improved Trap's instance ID to arm it here.");
        sb.AppendLine("  For \"cook\": set item_target_id to a raw/cookable item's ID. Requires Fire Steel or Camping Stove in inventory.");
        sb.AppendLine("  For \"purify\": set item_target_id to a filled container's ID. Requires Purification Tablet in inventory.");
        sb.AppendLine("  You may only do one item action per turn.");

        var available = RecipeRegistry.GetAvailable(Items.GetInventory(agentName));
        sb.AppendLine();
        sb.AppendLine("CRAFTING (recipes you can make right now with your current inventory):");
        if (available.Count > 0)
        {
            foreach (var r in available)
            {
                var ingNames = string.Join(" + ", r.Ingredients.Select(id => ItemRegistry.Get(id).Name));
                sb.AppendLine($"  [{r.Id}] {r.Name} -- needs: {ingNames} -- {r.Description}");
            }
        }
        else
        {
            sb.AppendLine("  (none available -- collect components to unlock recipes)");
        }

        sb.AppendLine();
        sb.AppendLine("CRAFTING TIPS:");
        sb.AppendLine("  WEAPONS: Shiv (+20 dmg) and Crude Knife (+12 dmg) boost every animal attack automatically -- no equip needed.");
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
                string sizeTag = a.Size == AnimalSize.Large ? "[LARGE -- DANGEROUS]" : "[small]";
                sb.AppendLine($"  {sizeTag} {a.DisplayName} [id:{a.Id}] -- HP:{a.Health:F0}/{a.MaxHealth:F0} -- {a.Description}");
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
                    AnimalState.Hunting => " -- HUNTING",
                    AnimalState.Fleeing => " -- fleeing",
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
        sb.AppendLine("  attack -- strike an animal IN YOUR CELL. Risky against large ones (they counter-attack).");
        sb.AppendLine("  trap   -- catch a SMALL animal in your cell using a Wire Bundle from your inventory.");
        sb.AppendLine("  scare  -- attempt to frighten a LARGE animal within 2 cells away. May backfire.");
        sb.AppendLine("  none   -- take no animal action (default).");
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

    // -- Internal helpers --------------------------------------------------------

    private static string CompassDirection(int fromX, int fromY, int toX, int toY)
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
