using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public class DayNightSystem : IDayNightSystem
{
    private WorldState _world = null!;
    private int _lastDawnBoostRound = -1;

    // Items that count as light sources (any with remaining uses).
    private static readonly HashSet<string> LightSourceIds =
        ["flashlight", "candle", "lighter", "matches", "improvised_lantern", "glow_stick", "filled_oil_lamp", "torch"];

    // Items that provide warmth against the cold.
    private static readonly HashSet<string> WarmthItemIds =
        ["blanket", "winter_coat", "leather_wrap", "sleeping_bag", "fur_vest"];

    // Terrain types treated as sheltered indoors.
    private static readonly HashSet<TerrainType> IndoorTerrains =
        [TerrainType.Apartment, TerrainType.Storefront, TerrainType.Industrial];

    public void Attach(WorldState world) => _world = world;

    // ── Phase computation ────────────────────────────────────────────────────

    /// <summary>
    /// Derives the DayPhase from a round number.
    /// Round 1 starts at 11:47 PM; each round = 1 hour.
    /// Hour bands: Night 22-23 / 0-4 | Dawn 5-7 | Day 8-17 | Dusk 18-21
    /// </summary>
    public DayPhase GetPhase(int round)
    {
        int totalMinutes = 23 * 60 + 47 + (round - 1) * 60;
        int hourOfDay    = (totalMinutes / 60) % 24;
        return hourOfDay switch
        {
            >= 5  and < 8  => DayPhase.Dawn,
            >= 8  and < 18 => DayPhase.Day,
            >= 18 and < 22 => DayPhase.Dusk,
            _              => DayPhase.Night,
        };
    }

    public bool IsNight(int round) => GetPhase(round) == DayPhase.Night;

    // ── Dawn boost ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true exactly once per Night→Dawn transition.
    /// Called from WorldState.TickDawnBoost() at the start of each round.
    /// </summary>
    public bool TryFireDawnBoost(int round)
    {
        if (GetPhase(round) != DayPhase.Dawn) return false;
        if (round <= 1)                        return false;
        if (GetPhase(round - 1) != DayPhase.Night) return false;
        if (_lastDawnBoostRound == round)      return false;
        _lastDawnBoostRound = round;
        return true;
    }

    // ── Inventory helpers ────────────────────────────────────────────────────

    public bool HasLightSource(string agentName)
    {
        var inv = _world.Items.GetInventory(agentName);
        // MaxUses == 0 means the item is consumed on first use (no charges to track).
        // Such items are still valid light sources while they remain in the inventory.
        return inv.Any(i => LightSourceIds.Contains(i.DefinitionId) &&
                            (i.Definition.MaxUses == 0 || i.UsesRemaining > 0));
    }

    public bool HasWarmth(string agentName)
    {
        var inv = _world.Items.GetInventory(agentName);
        return inv.Any(i => WarmthItemIds.Contains(i.DefinitionId));
    }

    public static bool IsIndoors(TerrainType terrain) => IndoorTerrains.Contains(terrain);

    // ── Per-agent night tick ──────────────────────────────────────────────────

    public void TickAgentNight(string agentName)
    {
        if (!IsNight(_world.CurrentRound)) return;

        var pos = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return;

        var terrain = _world.GetCell(pos.x, pos.y).Terrain;
        bool indoors = IsIndoors(terrain);

        var p = _world.GetPersonality(agentName);

        // ── Cold drain — outdoors at night without warmth (night_owl is hardy; still takes cold drain) ──
        if (!indoors && !HasWarmth(agentName))
        {
            _world.Survival.AddHunger(agentName, -1f);
            if (_world.Mood.Has(agentName))
                _world.Mood.GetMood(agentName).AdjustTrauma(+1f);
            _world.LogDev($"[{agentName}] cold night outdoors → hunger -1  trauma +1");
        }

        // ── Rest bonus — indoors and stationary (claustrophobic agents can't settle indoors) ──────────
        if (indoors && _world.Presence.IsStationary(agentName))
        {
            if (p.HasFlag("claustrophobic"))
            {
                // Can't rest — walls close in
                if (_world.Mood.Has(agentName))
                    _world.Mood.GetMood(agentName).AdjustStress(+3f);
                _world.LogDev($"[{agentName}] claustrophobic indoors at night — can't rest → stress +3");
            }
            else
            {
                _world.Survival.AddHealth(agentName, 2f);
                if (_world.Mood.Has(agentName))
                    _world.Mood.GetMood(agentName).AdjustStress(-5f);
                _world.LogDev($"[{agentName}] resting indoors at night → health +2  stress -5");
            }
        }
    }
}
