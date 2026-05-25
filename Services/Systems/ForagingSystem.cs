using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public class ForagingSystem : IForagingSystem
{
    private readonly Random _rng = new();
    private WorldState _world = null!;

    public void Attach(WorldState world) => _world = world;

    public bool CanForage(TerrainType terrain) =>
        terrain == TerrainType.Apartment || terrain == TerrainType.Storefront || terrain == TerrainType.Forest;

    public string? ScavengeHint(TerrainType terrain) => terrain switch
    {
        TerrainType.Apartment  => "Set \"scavenge\": true to search this building for supplies. Resolves at your final position AFTER any movement this turn.",
        TerrainType.Storefront => "Set \"scavenge\": true to search this building for supplies. Resolves at your final position AFTER any movement this turn.",
        TerrainType.Forest     => "Set \"scavenge\": true to forage this woodland for wild berries and mushrooms. Resolves at your final position AFTER any movement this turn.",
        _                      => null
    };

    public string? TryForage(string agentName)
    {
        var pos = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return null;

        var terrain = _world.GetCell(pos.x, pos.y).Terrain;
        if (!CanForage(terrain)) return null;

        if (terrain == TerrainType.Forest)
            return TryForageForest(agentName, pos);

        bool isStore = terrain == TerrainType.Storefront;
        var results  = new List<string>();

        // Resourcefulness bonus: ±10% on all rolls
        var personality = _world.GetPersonality(agentName);
        float resBonus = personality.ScavengeBonus;

        // Urban Forager: +15% in city buildings (stacks with Resourcefulness)
        float urbanBonus = personality.IsUrbanForager ? 0.15f : 0f;

        // Night penalty: -20% on all rolls unless agent has a light source
        float nightMod = (_world.IsNight && !_world.DayNight.HasLightSource(agentName)) ? -0.20f : 0f;

        // Weather penalty: rain and storms impair building scavenging (wet, dark, chaotic)
        float weatherMod = _world.Weather.ForagePenalty;

        // Exhaustion penalty: -10% on all rolls when stamina is depleted
        float exhaustMod = _world.IsExhausted(agentName) ? -0.10f : 0f;

        // Food: 70% in stores (grocery, deli, pharmacy snacks), 55% in apartments (pantry)
        if (_rng.NextDouble() < (isStore ? 0.70 : 0.55) + resBonus + urbanBonus + nightMod + weatherMod + exhaustMod)
        {
            float gain = 15f + (float)(_rng.NextDouble() * 20f);
            _world.Survival.AddHunger(agentName, gain);
            string found = isStore ? "packaged food" : "canned goods";
            results.Add($"{found} (+{gain:F0} hunger)");
        }

        // Water: 45% in apartments (taps, stored bottles), 30% in stores (bottled drinks)
        if (_rng.NextDouble() < (isStore ? 0.30 : 0.45) + resBonus + urbanBonus + nightMod + weatherMod + exhaustMod)
        {
            float gain = 15f + (float)(_rng.NextDouble() * 20f);
            _world.Survival.AddThirst(agentName, gain);
            results.Add($"bottled water (+{gain:F0} thirst)");
        }

        // Survivor Grit: one reroll on an empty scavenge (nightMod + weatherMod + exhaustMod still apply)
        if (results.Count == 0 && personality.IsSurvivorGrit)
        {
            _world.LogDev($"[{agentName}] survivor_grit reroll triggered");
            if (_rng.NextDouble() < (isStore ? 0.55 : 0.45) + resBonus + urbanBonus + nightMod + weatherMod + exhaustMod)
            {
                float gain = 10f + (float)(_rng.NextDouble() * 15f);
                _world.Survival.AddHunger(agentName, gain);
                string found = isStore ? "forgotten supplies" : "overlooked canned goods";
                results.Add($"{found} (+{gain:F0} hunger) [survivor's instinct]");
            }
        }

        if (results.Count == 0)
        {
            _world.LogAt(pos.x, pos.y, $"{agentName} searches the {(isStore ? "store" : "apartment")} but finds nothing useful.");
            return "scavenges — found nothing";
        }

        var found2 = string.Join(" and ", results);
        _world.LogAt(pos.x, pos.y, $"{agentName} scavenges and finds {found2}.");
        if (_world.Mood.Has(agentName))
        {
            var m = _world.Mood.GetMood(agentName);
            m.AdjustMood(+8f); m.AdjustStress(-6f);
        }
        _world.LogDev($"[{agentName}] scavenge success → mood +8  stress -6{(personality.IsUrbanForager ? "  [urban forager]" : "")}");
        _world.Memory.AddMemory(agentName, $"Scavenged at ({pos.x},{pos.y}) and found {found2}.");
        return $"scavenges — finds {found2}";
    }

    private string TryForageForest(string agentName, (int x, int y) pos)
    {
        var results = new List<string>();

        // Foraging Knife grants a bonus second independent roll for each find type
        bool hasKnife = _world.Items.GetInventory(agentName)
            .Any(i => i.DefinitionId == "foraging_knife");

        // Resourcefulness bonus: ±10% on all rolls
        var personality = _world.GetPersonality(agentName);
        float resBonus = personality.ScavengeBonus;
        bool isFieldNaturalist = personality.IsFieldNaturalist;

        // Field Naturalist: +25% to all rolls (stacks with Resourcefulness)
        float naturalistBonus = isFieldNaturalist ? 0.25f : 0f;

        // Night penalty: -20% unless agent has a light source
        float nightMod = (_world.IsNight && !_world.DayNight.HasLightSource(agentName)) ? -0.20f : 0f;

        // Weather penalty: rain and storms make foraging harder even in forests
        float weatherMod = _world.Weather.ForagePenalty;

        // Exhaustion penalty: -10% on all rolls when stamina is depleted
        float exhaustMod = _world.IsExhausted(agentName) ? -0.10f : 0f;

        // Berries: 65% base chance; knife adds a second 50% roll
        if (_rng.NextDouble() < 0.65 + resBonus + naturalistBonus + nightMod + weatherMod + exhaustMod)
        {
            float gain = 12f + (float)(_rng.NextDouble() * 18f);
            _world.Survival.AddHunger(agentName, gain);
            results.Add($"wild berries (+{gain:F0} hunger)");
        }
        if (hasKnife && _rng.NextDouble() < 0.50)
        {
            float gain = 8f + (float)(_rng.NextDouble() * 12f);
            _world.Survival.AddHunger(agentName, gain);
            results.Add($"more wild berries (+{gain:F0} hunger)");
        }

        // Mushrooms: 50% base chance; knife adds a second 40% roll
        if (_rng.NextDouble() < 0.50 + resBonus + naturalistBonus + nightMod + weatherMod + exhaustMod)
        {
            float gain = 8f + (float)(_rng.NextDouble() * 12f);
            _world.Survival.AddHunger(agentName, gain);
            results.Add($"mushrooms (+{gain:F0} hunger)");
        }
        if (hasKnife && _rng.NextDouble() < 0.40)
        {
            float gain = 6f + (float)(_rng.NextDouble() * 10f);
            _world.Survival.AddHunger(agentName, gain);
            results.Add($"more mushrooms (+{gain:F0} hunger)");
        }

        // Field Naturalist: guaranteed minimum find — always spots something edible
        if (results.Count == 0 && isFieldNaturalist)
        {
            float gain = 6f + (float)(_rng.NextDouble() * 10f);
            _world.Survival.AddHunger(agentName, gain);
            results.Add($"edible plants (+{gain:F0} hunger) [naturalist's eye]");
            _world.LogDev($"[{agentName}] field_naturalist guaranteed find triggered");
        }

        if (results.Count == 0)
        {
            _world.LogAt(pos.x, pos.y, $"{agentName} searches the undergrowth but finds nothing worth eating right now.");
            return "forages the forest — found nothing";
        }

        var found = string.Join(" and ", results);
        string knifeNote = hasKnife ? " (foraging knife)" : "";
        string naturalistNote = isFieldNaturalist ? " (field naturalist)" : "";
        _world.LogAt(pos.x, pos.y, $"{agentName} forages{knifeNote}{naturalistNote} and finds {found}.");
        if (_world.Mood.Has(agentName))
        {
            var m = _world.Mood.GetMood(agentName);
            m.AdjustMood(+5f); m.AdjustStress(-4f);
        }
        _world.LogDev($"[{agentName}] forest forage → mood +5  stress -4{(hasKnife ? "  [knife bonus]" : "")}{(isFieldNaturalist ? "  [field naturalist]" : "")}");
        _world.Memory.AddMemory(agentName, $"Foraged in the forest at ({pos.x},{pos.y}) and found {found}.");
        return $"forages the forest — finds {found}";
    }
}
