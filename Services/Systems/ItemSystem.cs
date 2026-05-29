using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public class ItemSystem : IItemSystem
{
    private readonly Dictionary<(int x, int y), List<ItemInstance>> _cellItems        = new();
    private readonly Dictionary<string, List<ItemInstance>>         _agentInventories = new();
    private readonly Dictionary<(int x, int y), List<ItemInstance>> _placedTraps      = new();

    // Maps item InstanceId → agent who deliberately dropped it (cleared on death drops so
    // corpse loot is always fair game). Used to detect witnessed theft.
    private readonly Dictionary<Guid, string> _droppedBy = new();

    private readonly Random _rng = new();
    private WorldState _world = null!;
    private int _respawnTick = 0;
    private const int BaseCarryCapacity = 5;

    private static readonly string[] _respawnApartment =
    [
        "canned_food", "water_bottle", "crackers", "chocolate_bar",
        "painkillers", "lighter", "candle", "pocket_knife", "blanket",
        "plastic_bag", "satchel", "instant_coffee", "honey_jar",
        "cereal_box", "dried_pasta", "antiseptic", "bandage_roll",
        "book", "playing_cards", "photo_album", "winter_coat",
        "batteries", "rope", "hand_sanitizer", "glass_bottle",
        "tin_can", "mason_jar", "cooking_pot", "water_jug", "bucket",
        "oil_lamp", "cooking_oil", "sleeping_bag"
    ];

    private static readonly string[] _respawnStorefront =
    [
        "canned_food", "canned_food", "water_bottle", "water_bottle",
        "sports_drink", "instant_noodles", "protein_bar", "granola_bar",
        "candle", "matches", "flashlight", "plastic_bag", "glow_stick",
        "caffeine_pills", "energy_drink"
    ];

    private static readonly string[] _respawnForest =
    [
        "wild_berries", "wild_berries", "mushrooms", "mushrooms",
        "wild_berries", "mushrooms", "wild_berries", "sleeping_bag"
    ];

    private static readonly string[] _respawnRiver =
    [
        "fishing_hook", "purification_tablet"
    ];

    public void Attach(WorldState world) => _world = world;

    public void InitializeAgent(string agentName) =>
        _agentInventories[agentName] = new List<ItemInstance>();

    public void RemoveAgent(string agentName) =>
        _agentInventories.Remove(agentName);

    public void InitializeItems()
    {
        // ── The Meridian (13,13) — central apartment block ───────────────────
        // Kitchen
        PlaceItem("water_bottle",     13, 13);
        PlaceItem("canned_food",      12, 13);
        PlaceItem("peanut_butter",    12, 13);
        PlaceItem("honey_jar",        13, 12);
        PlaceItem("instant_coffee",   13, 12);
        PlaceItem("cereal_box",       14, 13);
        PlaceItem("crackers",         14, 12);
        PlaceItem("dried_pasta",      11, 13);
        PlaceItem("cooking_oil",      11, 13);
        PlaceItem("chocolate_bar",    14, 14);
        PlaceItem("alcohol_bottle",   11, 14);
        // Bathroom
        PlaceItem("first_aid_kit",    13, 13);
        PlaceItem("painkillers",      12, 13);
        PlaceItem("antiseptic",       13, 14);
        PlaceItem("bandage_roll",     13, 14);
        PlaceItem("hand_sanitizer",   12, 14);
        // Bedroom / living room
        PlaceItem("blanket",          14, 12);
        PlaceItem("winter_coat",      14, 12);
        PlaceItem("photo_album",      13, 11);
        PlaceItem("book",             13, 11);
        PlaceItem("playing_cards",    12, 12);
        PlaceItem("stuffed_animal",   11, 12);
        PlaceItem("flashlight",       11, 14);
        PlaceItem("candle",           12, 11);
        PlaceItem("lighter",          13, 14);
        // Utility closet / toolbox
        PlaceItem("wire_bundle",      14, 13);
        PlaceItem("hammer",           14, 13);
        PlaceItem("scissors",         14, 11);
        PlaceItem("rope",             15, 13);
        PlaceItem("duct_tape",        15, 13);
        PlaceItem("pocket_knife",     13, 15);
        PlaceItem("batteries",        12, 15);
        PlaceItem("glass_bottle",     11, 11);
        PlaceItem("ham_radio",        12, 12);
        PlaceItem("prescription_meds", 14, 14);

        // ── Rosewood Apartments (13,4) ────────────────────────────────────────
        PlaceItem("canned_food",      13,  4);
        PlaceItem("water_bottle",     13,  4);
        PlaceItem("instant_coffee",   13,  4);
        PlaceItem("cereal_box",       12,  4);
        PlaceItem("peanut_butter",    12,  4);
        PlaceItem("crackers",         14,  4);
        PlaceItem("blanket",          13,  5);
        PlaceItem("winter_coat",      13,  5);
        PlaceItem("painkillers",      12,  5);
        PlaceItem("antiseptic",       14,  5);
        PlaceItem("flashlight",       13,  3);
        PlaceItem("candle",           12,  3);
        PlaceItem("pocket_knife",     14,  3);
        PlaceItem("book",             13,  6);
        PlaceItem("photo_album",      12,  6);
        PlaceItem("batteries",        14,  6);
        PlaceItem("rope",             11,  4);
        PlaceItem("satchel",          11,  4);

        // ── Calloway Flats (4,13) ─────────────────────────────────────────────
        PlaceItem("canned_food",       4, 13);
        PlaceItem("canned_meat",       4, 13);
        PlaceItem("honey_jar",         4, 12);
        PlaceItem("dried_pasta",       4, 12);
        PlaceItem("water_bottle",      5, 13);
        PlaceItem("sports_drink",      5, 13);
        PlaceItem("blanket",           3, 13);
        PlaceItem("painkillers",       4, 14);
        PlaceItem("bandage_roll",      4, 14);
        PlaceItem("flashlight",        3, 14);
        PlaceItem("lighter",           5, 14);
        PlaceItem("hammer",            4, 11);
        PlaceItem("scissors",          5, 11);
        PlaceItem("playing_cards",     3, 12);
        PlaceItem("book",              4, 15);
        PlaceItem("alcohol_bottle",    5, 15);
        PlaceItem("plastic_bag",       4, 13);
        PlaceItem("glass_bottle",      3, 11);

        // ── Hargrove Towers (22,22) ───────────────────────────────────────────
        PlaceItem("canned_food",      22, 22);
        PlaceItem("canned_meat",      22, 22);
        PlaceItem("instant_coffee",   22, 21);
        PlaceItem("chocolate_bar",    22, 21);
        PlaceItem("granola_bar",      23, 22);
        PlaceItem("water_bottle",     21, 22);
        PlaceItem("blanket",          22, 23);
        PlaceItem("winter_coat",      22, 23);
        PlaceItem("first_aid_kit",    23, 23);
        PlaceItem("prescription_meds", 21, 23);
        PlaceItem("hand_sanitizer",   22, 24);
        PlaceItem("candle",           23, 24);
        PlaceItem("matches",          21, 24);
        PlaceItem("caffeine_pills",   22, 24);  // Hargrove medicine cabinet
        PlaceItem("pocket_knife",     23, 21);
        PlaceItem("rope",             21, 21);
        PlaceItem("batteries",        24, 22);
        PlaceItem("stuffed_animal",   24, 23);
        PlaceItem("photo_album",      23, 20);
        PlaceItem("duffel_bag",       22, 20);

        // ── Sleeping bags — one per apartment block, found in bedroom closets ─────
        PlaceItem("sleeping_bag",     15, 12);  // The Meridian — storage closet
        PlaceItem("sleeping_bag",     14,  3);  // Rosewood — spare bedroom
        PlaceItem("sleeping_bag",      3, 14);  // Calloway — hallway cupboard
        PlaceItem("sleeping_bag",     24, 20);  // Hargrove — top-floor unit
        PlaceItem("sleeping_bag",     34, 11);  // Eastside Flats — camping gear left by tenants

        // ── Oil lamps — one per apartment block, found on shelves ───────────────
        PlaceItem("oil_lamp",         11, 11);  // The Meridian living room shelf
        PlaceItem("oil_lamp",         11,  5);  // Rosewood hallway
        PlaceItem("oil_lamp",          3, 15);  // Calloway kitchen counter
        PlaceItem("oil_lamp",         24, 24);  // Hargrove dining room

        // ── Liquid containers — scattered across apartment blocks ─────────────
        // The Meridian
        PlaceItem("cooking_pot",  13, 13);  // kitchen
        PlaceItem("mason_jar",    12, 11);  // pantry shelf
        PlaceItem("water_jug",    14, 15);  // storage cupboard
        PlaceItem("tin_can",      11, 12);  // recycling
        PlaceItem("bucket",       15, 14);  // utility closet
        // Rosewood
        PlaceItem("cooking_pot",  13,  5);
        PlaceItem("mason_jar",    12,  3);
        PlaceItem("tin_can",      14,  6);
        PlaceItem("water_jug",    11,  5);
        // Calloway
        PlaceItem("cooking_pot",   5, 12);
        PlaceItem("mason_jar",     3, 15);
        PlaceItem("bucket",        5, 15);
        PlaceItem("tin_can",       4, 11);
        // Hargrove
        PlaceItem("cooking_pot",  22, 21);
        PlaceItem("water_jug",    21, 20);
        PlaceItem("mason_jar",    23, 25);
        PlaceItem("bucket",       24, 21);
        PlaceItem("tin_can",      21, 25);

        // ── Sub-zone landmark seeds ───────────────────────────────────────────
        // Pak's Stockroom (19-26, 6-8) — preserved food cache in the back
        PlaceItem("canned_food",       21,  7);
        PlaceItem("canned_food",       22,  7);
        PlaceItem("canned_food",       23,  7);
        PlaceItem("water_bottle",      21,  6);
        PlaceItem("water_bottle",      24,  7);
        PlaceItem("instant_noodles",   22,  6);
        PlaceItem("peanut_butter",     20,  7);
        PlaceItem("granola_bar",       25,  7);
        PlaceItem("protein_bar",       24,  6);
        PlaceItem("honey_jar",         23,  6);
        PlaceItem("crackers",          20,  6);
        PlaceItem("caffeine_pills",    25,  6);  // Pak's counter display

        // Neon Diner Kitchen (19-26, 15-17) — commercial kitchen scraps
        PlaceItem("matches",           21, 16);
        PlaceItem("matches",           23, 16);
        PlaceItem("cooking_oil",       22, 15);
        PlaceItem("mason_jar",         20, 16);
        PlaceItem("bucket",            25, 16);
        PlaceItem("canned_food",       24, 15);
        PlaceItem("alcohol_bottle",    21, 17);  // industrial cleaning alcohol
        PlaceItem("instant_coffee",    20, 15);

        // Elm St. Dispensary (10-17, 23-26) — prescription back-room
        PlaceItem("prescription_meds", 13, 24);
        PlaceItem("prescription_meds", 14, 24);
        PlaceItem("prescription_meds", 15, 25);
        PlaceItem("painkillers",       12, 24);
        PlaceItem("painkillers",       13, 25);
        PlaceItem("antiseptic",        14, 25);
        PlaceItem("antiseptic",        12, 25);
        PlaceItem("bandage_roll",      15, 24);
        PlaceItem("first_aid_kit",     13, 26);
        PlaceItem("caffeine_pills",    14, 26);  // OTC shelf

        // Hendricks Loading Dock (1-4, 23-26) — heavy equipment and tool crates
        PlaceItem("crowbar",           2, 24);
        PlaceItem("scrap_metal",       3, 24);
        PlaceItem("wire_bundle",       2, 25);
        PlaceItem("rope",              3, 25);
        PlaceItem("hammer",            2, 26);
        PlaceItem("duct_tape",         3, 23);
        PlaceItem("battery_pack",      4, 24);
        // Industrial-exclusive finds
        PlaceItem("bolt_cutters",      1, 25);
        PlaceItem("bolt_cutters",      4, 26);
        PlaceItem("propane_tank",      2, 23);
        PlaceItem("propane_tank",      3, 26);
        PlaceItem("camping_stove",     1, 24);  // emergency kit in warehouse break room
        PlaceItem("cargo_straps",      4, 23);
        PlaceItem("cargo_straps",      3, 24);
        PlaceItem("glow_stick",        1, 26);  // safety kit by the loading bay
        PlaceItem("oil_lamp",          2, 23);  // break room shelf

        // Hendricks Cold Storage (5-8, 23-26) — frozen food and water reserves
        PlaceItem("canned_food",         6, 24);
        PlaceItem("canned_food",         7, 24);
        PlaceItem("canned_meat",         6, 25);
        PlaceItem("canned_meat",         7, 25);
        PlaceItem("filled_cooking_pot",  5, 24);  // pipes burst — water pooled here
        PlaceItem("filled_water_jug",    8, 24);
        PlaceItem("blanket",             6, 26);  // cold storage workers left gear

        // The Meridian Lobby (10-17, 15-17) — stripped entrance hall
        PlaceItem("satchel",           13, 16);  // abandoned bag by someone who fled
        PlaceItem("plastic_bag",       12, 16);

        // Riverside Fountain (3-6, 3-6) — containers left by earlier visitors
        PlaceItem("water_jug",         4,  4);
        PlaceItem("bucket",            5,  5);
        PlaceItem("tin_can",           3,  4);

        // ── General Hospital (28-35, 1-8) ─────────────────────────────────────
        PlaceItem("first_aid_kit",      31,  4);
        PlaceItem("first_aid_kit",      30,  4);
        PlaceItem("prescription_meds",  31,  3);
        PlaceItem("prescription_meds",  32,  3);
        PlaceItem("prescription_meds",  33,  5);
        PlaceItem("painkillers",        30,  3);
        PlaceItem("painkillers",        31,  5);
        PlaceItem("painkillers",        32,  5);
        PlaceItem("antiseptic",         29,  4);
        PlaceItem("antiseptic",         31,  6);
        PlaceItem("bandage_roll",       30,  5);
        PlaceItem("bandage_roll",       33,  4);
        PlaceItem("bandage_roll",       34,  4);
        PlaceItem("hand_sanitizer",     29,  5);
        PlaceItem("hand_sanitizer",     32,  4);
        PlaceItem("alcohol_bottle",     30,  6);  // surgical alcohol
        PlaceItem("canned_food",        29,  3);
        PlaceItem("water_bottle",       30,  3);
        PlaceItem("blanket",            31,  7);
        PlaceItem("blanket",            32,  7);
        PlaceItem("flashlight",         29,  6);
        PlaceItem("batteries",          34,  5);
        PlaceItem("caffeine_pills",     30,  6);  // nurse station drawer
        PlaceItem("water_jug",          33,  3);  // IV bags long dry
        PlaceItem("bucket",             34,  3);
        // Hospital-exclusive high-tier medical items
        PlaceItem("antibiotics",        32,  6);
        PlaceItem("antibiotics",        33,  6);
        PlaceItem("morphine",           34,  6);  // crash cart
        PlaceItem("surgical_kit",       29,  7);
        PlaceItem("surgical_kit",       33,  7);
        // Emergency lighting in hospital corridors
        PlaceItem("glow_stick",         30,  7);
        PlaceItem("glow_stick",         34,  4);  // nurse station emergency kit

        // ── Eastside Flats (28-35, 10-17) ─────────────────────────────────────
        PlaceItem("canned_food",       31, 13);
        PlaceItem("canned_meat",       30, 13);
        PlaceItem("water_bottle",      31, 12);
        PlaceItem("crackers",          32, 13);
        PlaceItem("peanut_butter",     29, 13);
        PlaceItem("blanket",           31, 14);
        PlaceItem("winter_coat",       30, 14);
        PlaceItem("painkillers",       32, 12);
        PlaceItem("antiseptic",        31, 11);
        PlaceItem("flashlight",        30, 11);
        PlaceItem("candle",            33, 13);
        PlaceItem("lighter",           33, 12);
        PlaceItem("book",              29, 12);
        PlaceItem("playing_cards",     29, 14);
        PlaceItem("hammer",            34, 13);
        PlaceItem("rope",              34, 14);
        PlaceItem("cooking_pot",       31, 15);
        PlaceItem("mason_jar",         30, 15);
        PlaceItem("satchel",           32, 14);

        // ── Greenwood Forest (1-8, 28-35) ────────────────────────────────────
        PlaceItem("wild_berries",       3, 30);
        PlaceItem("wild_berries",       5, 31);
        PlaceItem("wild_berries",       2, 33);
        PlaceItem("mushrooms",          4, 32);
        PlaceItem("mushrooms",          6, 30);
        PlaceItem("mushrooms",          3, 34);
        PlaceItem("rope",               4, 29);   // left behind by campers
        PlaceItem("pocket_knife",       2, 31);
        PlaceItem("matches",            5, 33);
        PlaceItem("blanket",            4, 35);   // emergency bivouac gear
        PlaceItem("winter_coat",        3, 32);
        PlaceItem("wood_axe",           6, 29);   // woodsman's axe at the forest edge
        PlaceItem("foraging_knife",     3, 33);   // left by a hunter
        PlaceItem("fire_steel",         5, 35);   // emergency kit — fire starting
        PlaceItem("sleeping_bag",       6, 33);   // abandoned campsite

        // ── Birchwood Forest (10-17, 28-35) ──────────────────────────────────
        PlaceItem("wild_berries",      12, 30);
        PlaceItem("wild_berries",      14, 32);
        PlaceItem("mushrooms",         11, 31);
        PlaceItem("mushrooms",         15, 33);
        PlaceItem("candle",            13, 29);   // left at a trail marker
        PlaceItem("lighter",           12, 34);
        PlaceItem("rope",              14, 30);
        PlaceItem("water_jug",         13, 33);   // left by a hiker
        PlaceItem("foraging_knife",    15, 31);   // lost by a hiker
        PlaceItem("fire_steel",        11, 35);
        PlaceItem("sleeping_bag",      14, 34);   // hiker's camp — still rolled up

        // ── River areas (Irongate River, River Bend, The Delta) ───────────────
        // Empty containers left at the water's edge
        PlaceItem("bucket",            31, 22);
        PlaceItem("water_jug",         30, 21);
        PlaceItem("tin_can",           33, 24);
        PlaceItem("mason_jar",         21, 30);   // River Bend
        PlaceItem("bucket",            22, 31);
        PlaceItem("tin_can",           31, 31);   // The Delta
        PlaceItem("water_jug",         29, 32);
        PlaceItem("flashlight",        32, 20);   // dropped by someone who came to drink
        PlaceItem("rope",              20, 32);   // tied to a tree stump near the bank
        // Fishing and purification — stashed or carried by earlier survivors
        PlaceItem("fishing_hook",      30, 22);   // tackle box left at the bank
        PlaceItem("fishing_hook",      21, 32);   // River Bend — someone was living here
        PlaceItem("purification_tablet", 33, 21); // emergency kit
        PlaceItem("purification_tablet", 22, 30); // River Bend hiker supplies

        // ── Carry containers — worth finding across the city ──────────────────
        PlaceItem("plastic_bag",  13, 13);
        PlaceItem("backpack",     14, 14);
        PlaceItem("hiking_pack",  19, 19);  // Hendricks Warehouse — best reward
        PlaceItem("duffel_bag",   31,  4);  // hospital emergency bag

        // ── Social/barter items — scattered lightly across the city ───────────
        PlaceItem("cigarettes",    9, 13);   // street corner
        PlaceItem("cigarettes",   22, 9);    // storefront shelf
        PlaceItem("cigarettes",   19, 22);   // apartment kitchen drawer
        PlaceItem("jewelry",       4,  9);   // Rosewood bedroom
        PlaceItem("jewelry",      22, 14);   // Hargrove dresser
        PlaceItem("jewelry",      31, 14);   // Eastside Flats
        PlaceItem("cash",         13,  9);   // Rosewood desk
        PlaceItem("cash",          9, 22);   // street wallet
        PlaceItem("cash",         22, 19);   // Pak's Stockroom till

        // ── Fire steel — a few across apartments and storefronts ──────────────
        PlaceItem("fire_steel",   13, 22);   // Hargrove utility drawer
        PlaceItem("fire_steel",   22,  4);   // Rosewood camping gear
        PlaceItem("fire_steel",    4, 22);   // Calloway tool box
        PlaceItem("fire_steel",   31, 13);   // Eastside Flats

        // ── Scatter across streets/storefronts ───────────────────────────────
        var streetItems = new[]
        {
            "scrap_metal", "crowbar", "duct_tape", "battery_pack", "wire_bundle",
            "lighter", "matches", "candle", "energy_drink", "glass_bottle",
            "cigarettes", "cash"
        };
        var storeItems = new[]
        {
            "canned_food", "canned_food", "water_bottle", "water_bottle",
            "sports_drink", "instant_noodles", "peanut_butter", "granola_bar",
            "protein_bar", "crackers", "canned_meat", "chocolate_bar",
            "prescription_meds", "painkillers", "flashlight", "candle",
            "matches", "instant_coffee", "honey_jar", "batteries",
            "cigarettes", "fire_steel", "caffeine_pills", "energy_drink"
        };
        var apartmentItems = new[]
        {
            "canned_food", "water_bottle", "crackers", "alcohol_bottle",
            "blanket", "lighter", "candle", "duct_tape", "pocket_knife",
            "prescription_meds", "painkillers", "chocolate_bar", "peanut_butter",
            "instant_coffee", "honey_jar", "cereal_box", "dried_pasta",
            "antiseptic", "bandage_roll", "book", "playing_cards",
            "photo_album", "winter_coat", "batteries", "rope",
            "hand_sanitizer", "glass_bottle", "stuffed_animal", "hammer",
            "tin_can", "mason_jar", "cooking_pot", "water_jug", "bucket",
            "cigarettes", "jewelry", "cash", "fire_steel",
            "oil_lamp", "cooking_oil", "sleeping_bag"
        };
        var forestItems = new[]
        {
            "wild_berries", "wild_berries", "mushrooms", "mushrooms",
            "wild_berries", "mushrooms", "rope", "pocket_knife", "blanket",
            "foraging_knife", "fire_steel", "sleeping_bag"
        };

        int placed = 0, attempts = 0;
        while (placed < 16 && attempts < 800)
        {
            attempts++;
            int rx = _rng.Next(_world.MapWidth);
            int ry = _rng.Next(_world.MapHeight);
            var t = _world.GetCell(rx, ry).Terrain;

            string[]? pool = t switch
            {
                TerrainType.Street     => streetItems,
                TerrainType.Storefront => storeItems,
                TerrainType.Apartment  => apartmentItems,
                TerrainType.Park       => streetItems,
                TerrainType.Forest     => forestItems,
                _                      => null
            };

            if (pool is null) continue;

            // Don't crowd the starting block
            if (Math.Max(Math.Abs(rx - 13), Math.Abs(ry - 13)) < 3) continue;

            PlaceItem(pool[_rng.Next(pool.Length)], rx, ry);
            placed++;
        }
    }

    public void PlaceItemAt(string definitionId, int x, int y) => PlaceItem(definitionId, x, y);

    private void PlaceItem(string defId, int x, int y)
    {
        if (!_cellItems.TryGetValue((x, y), out var list))
        {
            list = new List<ItemInstance>();
            _cellItems[(x, y)] = list;
        }
        list.Add(new ItemInstance(defId));
    }

    public bool HasItemsAt(int x, int y) =>
        _cellItems.TryGetValue((x, y), out var l) && l.Count > 0;

    public IReadOnlyList<ItemInstance> GetItemsAt(int x, int y) =>
        _cellItems.TryGetValue((x, y), out var l) ? l : [];

    public IReadOnlyList<ItemInstance> GetInventory(string agentName) =>
        _agentInventories.TryGetValue(agentName, out var inv) ? inv : [];

    public string? TryPickUp(string agentName, string instanceIdStr)
    {
        var pos = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return null;

        // Check placed traps first — agents can retrieve them before they fire
        if (_placedTraps.TryGetValue(pos, out var trapList))
        {
            var trap = trapList.FirstOrDefault(i => i.InstanceId.ToString() == instanceIdStr);
            if (trap != null)
            {
                if (IsInventoryFull(agentName)) return null;
                trapList.Remove(trap);
                _agentInventories[agentName].Add(trap);
                _world.LogAt(pos.x, pos.y, $"{agentName} retrieves the armed trap.");
                if (_world.Mood.Has(agentName))
                    _world.Mood.GetMood(agentName).AdjustMood(+2f);
                _world.Memory.AddMemory(agentName, $"Retrieved an armed trap from ({pos.x},{pos.y}).");
                return trap.DisplayName;
            }
        }

        if (!_cellItems.TryGetValue(pos, out var cellList)) return null;

        var item = cellList.FirstOrDefault(i => i.InstanceId.ToString() == instanceIdStr);
        if (item is null) return null;

        if (IsInventoryFull(agentName))
        {
            _world.LogDev($"[{agentName}] pick_up blocked — inventory full ({GetCarryCapacity(agentName)} slots)");
            return null;
        }

        cellList.Remove(item);
        _agentInventories[agentName].Add(item);
        _world.LogAt(pos.x, pos.y, $"{agentName} picks up {item.DisplayName}.");
        if (_world.Mood.Has(agentName))
        {
            var m = _world.Mood.GetMood(agentName);
            m.AdjustMood(+4f); m.AdjustStress(-2f);
        }
        _world.LogDev($"[{agentName}] pick_up {item.DisplayName} → mood +4  stress -2");
        _world.Memory.AddMemory(agentName, $"Picked up {item.DisplayName} at ({pos.x},{pos.y}).");

        // Theft detection: if the original dropper is still nearby and watching, penalise trust.
        if (_droppedBy.TryGetValue(item.InstanceId, out var dropper) && dropper != agentName)
        {
            _droppedBy.Remove(item.InstanceId);
            var visible = _world.GetVisibleAgents(agentName);
            bool dropperWatching = visible.Any(a => a.name == dropper);
            if (dropperWatching && _world.Mood.Has(dropper))
            {
                _world.GetMood(dropper).AdjustTrust(agentName, -18f);
                _world.GetMood(dropper).AdjustStress(+8f);
                _world.Memory.AddMemory(dropper,
                    $"{_world.DescribeAgent(dropper, agentName)} took my {item.DisplayName} while I was watching.");
                _world.LogDev($"[{dropper}] witnessed theft of {item.DisplayName} by {agentName} → trust[{agentName}] -18  stress +8");
            }
            // Bystanders who know both parties also lose some trust in the thief.
            foreach (var (witness, _, _) in visible)
            {
                if (witness == agentName || witness == dropper) continue;
                if (!_world.KnowsName(witness, agentName) || !_world.KnowsName(witness, dropper)) continue;
                if (!_world.Mood.Has(witness)) continue;
                _world.GetMood(witness).AdjustTrust(agentName, -6f);
                _world.Memory.AddMemory(witness,
                    $"Saw {agentName} take {item.DisplayName} from {dropper}.");
                _world.LogDev($"[{witness}] witnessed theft → trust[{agentName}] -6");
            }
        }
        else
        {
            _droppedBy.Remove(item.InstanceId);
        }

        return item.DisplayName;
    }

    public string? TryDrop(string agentName, string instanceIdStr)
    {
        var pos = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return null;
        var inv = _agentInventories[agentName];
        var item = inv.FirstOrDefault(i => i.InstanceId.ToString() == instanceIdStr);
        if (item is null) return null;

        inv.Remove(item);
        if (!_cellItems.TryGetValue(pos, out var cellList))
        {
            cellList = new List<ItemInstance>();
            _cellItems[pos] = cellList;
        }
        cellList.Add(item);
        _droppedBy[item.InstanceId] = agentName; // track ownership for theft detection
        _world.LogAt(pos.x, pos.y, $"{agentName} drops {item.DisplayName}.");
        return item.DisplayName;
    }

    public string TryUse(string agentName, string instanceIdStr)
    {
        var pos = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return "";
        var inv = _agentInventories[agentName];
        var item = inv.FirstOrDefault(i => i.InstanceId.ToString() == instanceIdStr);
        if (item is null || !item.Definition.IsUsable) return "";

        var def = item.Definition;

        if (def.HungerRestore > 0)
        {
            _world.Survival.AddHunger(agentName, def.HungerRestore);

            // Eating-while-starving: witnesses whose own hunger is critically low lose
            // trust in the eater for not sharing. protects_others penalise harder;
            // self_reliant witnesses care less.
            var visible = _world.GetVisibleAgents(agentName);
            foreach (var (witnessName, _, _) in visible)
            {
                if (!_world.Mood.Has(witnessName)) continue;
                float witnessHunger = _world.GetHunger(witnessName);
                if (witnessHunger >= 20f) continue;           // only triggered when witness is starving

                var witnessPersonality = _world.GetPersonality(witnessName);
                float penalty = witnessPersonality.HasFlag("protects_others") ? -14f
                              : witnessPersonality.HasFlag("self_reliant")    ?  -5f
                              :                                                  -9f;

                _world.GetMood(witnessName).AdjustTrust(agentName, penalty);
                _world.GetMood(witnessName).AdjustStress(+4f);
                _world.Memory.AddMemory(witnessName,
                    $"{_world.DescribeAgent(witnessName, agentName)} ate {def.Name} while I was starving right next to them.");
                _world.LogDev($"[{witnessName}] trust[{agentName}] {penalty:+0;-0}  stress +4  (ate while witness starving)");
            }
        }
        if (def.ThirstRestore > 0) _world.Survival.AddThirst(agentName, def.ThirstRestore);
        if (def.HealthRestore > 0)
        {
            float fieldMedicMult = _world.GetPersonality(agentName).IsFieldMedic ? 1.5f : 1.0f;
            _world.Survival.AddHealth(agentName, def.HealthRestore * fieldMedicMult);
        }

        // Consume: multi-use items lose one charge; single-use items are removed entirely.
        if (def.MaxUses > 0)
        {
            item.UsesRemaining--;
            if (item.UsesRemaining <= 0)
                inv.Remove(item);
        }
        else
        {
            inv.Remove(item);
        }

        var effect = def.UseEffect;
        string usesNote = def.MaxUses > 0 && item.UsesRemaining > 0
            ? $" ({item.UsesRemaining} uses left)" : "";
        _world.LogAt(pos.x, pos.y, $"{agentName} uses {def.Name}{usesNote}. {effect}");
        if (_world.Mood.Has(agentName))
        {
            var m = _world.Mood.GetMood(agentName);
            m.AdjustMood(def.MoodDelta);
            m.AdjustStress(def.StressDelta);
        }
        var restoreNote = "";
        if (def.HungerRestore > 0) restoreNote += $"  hunger +{def.HungerRestore:F0}";
        if (def.ThirstRestore > 0) restoreNote += $"  thirst +{def.ThirstRestore:F0}";
        if (def.HealthRestore > 0)
        {
            float fmMult = _world.GetPersonality(agentName).IsFieldMedic ? 1.5f : 1.0f;
            restoreNote += $"  health +{def.HealthRestore * fmMult:F0}{(fmMult > 1 ? " [field medic]" : "")}";
        }
        _world.LogDev($"[{agentName}] use {def.Name} → mood {def.MoodDelta:+0;-0}  stress {def.StressDelta:+0;-0}{restoreNote}");
        _world.Memory.AddMemory(agentName, $"Used {def.Name} — {effect}.");
        return effect;
    }

    public string? TryGive(string fromAgent, string instanceIdStr, string toAgent)
    {
        var fromPos = _world.GetAgentPosition(fromAgent);
        var toPos   = _world.GetAgentPosition(toAgent);
        if (fromPos.x < 0 || toPos.x < 0) return null;
        // Must be within the same earshot as conversations (visible to each other).
        if (!_world.GetVisibleAgents(fromAgent).Any(a => a.name == toAgent)) return null;

        var fromInv = _agentInventories[fromAgent];
        var item = fromInv.FirstOrDefault(i => i.InstanceId.ToString() == instanceIdStr);
        if (item is null) return null;

        fromInv.Remove(item);
        _agentInventories[toAgent].Add(item);
        _world.LogAt(fromPos.x, fromPos.y, $"{fromAgent} gives {item.DisplayName} to {toAgent}.");
        var giverPersonality = _world.GetPersonality(fromAgent);
        bool silverTongue = giverPersonality.IsSilverTongue;
        if (_world.Mood.Has(fromAgent))
        {
            var gm = _world.Mood.GetMood(fromAgent);
            bool hoardsFood    = giverPersonality.HasFlag("hoards_food");
            bool isFood        = item.Definition.HungerRestore > 0;
            bool protectsOthers = giverPersonality.HasFlag("protects_others");

            if (hoardsFood && isFood)
            {
                // Hoarding agents give food reluctantly — stress and regret
                gm.AdjustMood(-3f); gm.AdjustStress(+5f);
                _world.LogDev($"[{fromAgent}] hoards_food — gave food reluctantly → mood -3  stress +5");
            }
            else
            {
                float protectBonus = protectsOthers ? 4f : 0f;
                gm.AdjustMood(+5f + protectBonus);
                if (protectBonus > 0)
                    _world.LogDev($"[{fromAgent}] protects_others — giving boost → mood +{5f + protectBonus:F0}");
            }
            gm.AdjustTrust(toAgent, +5f);
        }
        if (_world.Mood.Has(toAgent))
        {
            float trustGain = silverTongue ? 20f : 10f;
            var rm = _world.Mood.GetMood(toAgent);
            if (_world.GetPersonality(toAgent).HasFlag("self_reliant"))
            {
                // Doesn't want charity — trust gain is halved, no mood boost
                rm.AdjustTrust(fromAgent, trustGain * 0.5f);
                _world.LogDev($"[{toAgent}] self_reliant — received {item.DisplayName} reluctantly → trust[{fromAgent}] +{trustGain * 0.5f:F0}  (no mood boost)");
            }
            else
            {
                rm.AdjustMood(+8f); rm.AdjustStress(-3f); rm.AdjustTrust(fromAgent, trustGain);
                _world.LogDev($"[{toAgent}] receive {item.DisplayName} → mood +8  stress -3  trust[{fromAgent}] +{(silverTongue ? 20 : 10)}{(silverTongue ? " [silver tongue]" : "")}");
            }
        }
        _world.LogDev($"[{fromAgent}] give {item.DisplayName} → trust[{toAgent}] +5{(silverTongue ? "  [silver tongue]" : "")}");
        _world.Memory.AddMemory(fromAgent, $"Gave {item.DisplayName} to {toAgent}.");
        _world.Memory.AddMemory(toAgent, $"Received {item.DisplayName} from {fromAgent}.");
        return item.DisplayName;
    }

    public (bool consumed, bool success, IReadOnlyList<string> yielded) TryDeconstruct(string agentName, string instanceIdStr)
    {
        var pos = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return (false, false, []);

        var inv = _agentInventories[agentName];
        var item = inv.FirstOrDefault(i => i.InstanceId.ToString() == instanceIdStr);
        if (item is null || !item.Definition.IsDeconstructable)
            return (false, false, []);

        inv.Remove(item);

        if (_rng.NextDouble() < item.Definition.DeconstructChance)
        {
            var yields = item.Definition.DeconstructYields;
            foreach (var yieldId in yields)
                inv.Add(new ItemInstance(yieldId));

            var yieldNames = string.Join(", ", yields.Select(y => ItemRegistry.Get(y).Name));
            _world.LogAt(pos.x, pos.y, $"{agentName} deconstructs {item.DisplayName} → {yieldNames}.");
            if (_world.Mood.Has(agentName))
                _world.Mood.GetMood(agentName).AdjustMood(+8f);
            _world.LogDev($"[{agentName}] deconstruct {item.DisplayName} success → mood +8");
            _world.Memory.AddMemory(agentName, $"Deconstructed {item.DisplayName}, yielded {yieldNames}.");
            return (true, true, yields);
        }
        else
        {
            _world.LogAt(pos.x, pos.y, $"{agentName} tries to deconstruct {item.DisplayName} but it crumbles to nothing.");
            if (_world.Mood.Has(agentName))
            {
                var m = _world.Mood.GetMood(agentName);
                m.AdjustMood(-5f); m.AdjustStress(+5f);
            }
            _world.LogDev($"[{agentName}] deconstruct {item.DisplayName} failed → mood -5  stress +5");
            return (true, false, []);
        }
    }

    public bool TryConsume(string agentName, string instanceIdStr)
    {
        if (!_agentInventories.TryGetValue(agentName, out var inv)) return false;
        var item = inv.FirstOrDefault(i => i.InstanceId.ToString() == instanceIdStr);
        if (item is null) return false;
        inv.Remove(item);
        return true;
    }

    public ItemInstance? TryRemoveFromInventory(string agentName, string instanceIdStr)
    {
        if (!_agentInventories.TryGetValue(agentName, out var inv)) return null;
        var item = inv.FirstOrDefault(i => i.InstanceId.ToString() == instanceIdStr);
        if (item is null) return null;
        inv.Remove(item);
        return item;
    }

    public bool TryAddItemInstance(string agentName, ItemInstance item)
    {
        if (!_agentInventories.TryGetValue(agentName, out var inv)) return false;
        if (inv.Count >= GetCarryCapacity(agentName)) return false;
        inv.Add(item);
        return true;
    }

    public void DropInventoryAt(string agentName, int x, int y)
    {
        if (!_agentInventories.TryGetValue(agentName, out var inv) || inv.Count == 0) return;
        if (!_cellItems.TryGetValue((x, y), out var cellList))
        {
            cellList = new List<ItemInstance>();
            _cellItems[(x, y)] = cellList;
        }
        // Death drops: clear ownership so corpse loot is fair game.
        foreach (var item in inv)
            _droppedBy.Remove(item.InstanceId);
        cellList.AddRange(inv);
        inv.Clear();
    }

    public void AddToInventory(string agentName, string definitionId)
    {
        if (!_agentInventories.TryGetValue(agentName, out var inv)) return;
        inv.Add(new ItemInstance(definitionId));
    }

    public bool ConsumeOneUse(string agentName, string instanceIdStr)
    {
        if (!_agentInventories.TryGetValue(agentName, out var inv)) return false;
        var item = inv.FirstOrDefault(i => i.InstanceId.ToString() == instanceIdStr);
        if (item is null) return false;

        if (item.Definition.MaxUses > 0)
        {
            item.UsesRemaining--;
            if (item.UsesRemaining <= 0) inv.Remove(item);
        }
        else
        {
            inv.Remove(item);
        }
        return true;
    }

    public string? TryFill(string agentName, string instanceIdStr)
    {
        var pos = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return null;

        var inv  = _agentInventories[agentName];
        var item = inv.FirstOrDefault(i => i.InstanceId.ToString() == instanceIdStr);
        if (item is null) return null;

        var fillId = item.Definition.FillResult;
        if (string.IsNullOrEmpty(fillId)) return null;

        // Replace the empty container with its filled counterpart.
        int idx = inv.IndexOf(item);
        inv[idx] = new ItemInstance(fillId);

        var filledName = ItemRegistry.Get(fillId).Name;
        _world.LogAt(pos.x, pos.y, $"{agentName} fills {item.DisplayName} with water -> {filledName}.");
        if (_world.Mood.Has(agentName))
        {
            var m = _world.Mood.GetMood(agentName);
            m.AdjustMood(+5f); m.AdjustStress(-4f);
        }
        _world.LogDev($"[{agentName}] fill {item.DisplayName} -> {filledName}  mood +5  stress -4");
        _world.Memory.AddMemory(agentName, $"Filled {item.DisplayName} with water -> {filledName} at ({pos.x},{pos.y}).");
        return $"fills {item.DisplayName} with water -> {filledName}";
    }

    public string? TryPlaceTrap(string agentName, string instanceIdStr)
    {
        var pos = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return null;

        var inv  = _agentInventories[agentName];
        var item = inv.FirstOrDefault(i =>
            i.InstanceId.ToString() == instanceIdStr && i.DefinitionId == "improved_trap");
        if (item is null) return null;

        inv.Remove(item);
        if (!_placedTraps.TryGetValue(pos, out var traps))
        {
            traps = new List<ItemInstance>();
            _placedTraps[pos] = traps;
        }
        traps.Add(item);

        _world.LogAt(pos.x, pos.y, $"{agentName} arms a trap here.");
        _world.Memory.AddMemory(agentName, $"Placed an improved trap at ({pos.x},{pos.y}).");
        if (_world.Mood.Has(agentName))
            _world.Mood.GetMood(agentName).AdjustMood(+3f);
        return "places an armed trap";
    }

    public IReadOnlyList<ItemInstance> GetPlacedTrapsAt(int x, int y) =>
        _placedTraps.TryGetValue((x, y), out var t) ? t : [];

    public ItemInstance? TakeTopTrapAt(int x, int y)
    {
        if (!_placedTraps.TryGetValue((x, y), out var traps) || traps.Count == 0) return null;
        var trap = traps[0];
        traps.RemoveAt(0);
        return trap;
    }

    public float GetWeaponBonus(string agentName)
    {
        if (!_agentInventories.TryGetValue(agentName, out var inv)) return 0f;
        return inv.Count == 0 ? 0f : inv.Max(i => i.Definition.AttackBonus);
    }

    public int GetCarryCapacity(string agentName)
    {
        if (!_agentInventories.TryGetValue(agentName, out var inv)) return BaseCarryCapacity;
        return BaseCarryCapacity + inv.Sum(i => i.Definition.CarryCapacity);
    }

    public bool IsInventoryFull(string agentName) =>
        _agentInventories.TryGetValue(agentName, out var inv) &&
        inv.Count >= GetCarryCapacity(agentName);

    public void TickRespawn()
    {
        _respawnTick++;
        if (_respawnTick % 5 != 0) return;

        int placed = 0, attempts = 0;
        while (placed < 3 && attempts < 300)
        {
            attempts++;
            int rx = _rng.Next(_world.MapWidth);
            int ry = _rng.Next(_world.MapHeight);
            var t = _world.GetCell(rx, ry).Terrain;

            string[]? pool = t switch
            {
                TerrainType.Apartment  => _respawnApartment,
                TerrainType.Storefront => _respawnStorefront,
                TerrainType.Forest     => _respawnForest,
                TerrainType.River      => _respawnRiver,
                _                      => null
            };
            if (pool is null) continue;
            if (HasItemsAt(rx, ry)) continue;

            PlaceItem(pool[_rng.Next(pool.Length)], rx, ry);
            placed++;
        }

        if (placed > 0)
            _world.LogDev($"[respawn] {placed} item(s) appeared in the city.");
    }
}
