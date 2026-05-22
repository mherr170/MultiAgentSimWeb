using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public class ForagingSystem : IForagingSystem
{
    private readonly Random _rng = new();
    private WorldState _world = null!;

    public void Attach(WorldState world) => _world = world;

    public bool CanForage(TerrainType terrain) =>
        terrain == TerrainType.Apartment || terrain == TerrainType.Storefront || terrain == TerrainType.Forest;

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

        // Food: 70% in stores (grocery, deli, pharmacy snacks), 55% in apartments (pantry)
        if (_rng.NextDouble() < (isStore ? 0.70 : 0.55))
        {
            float gain = 15f + (float)(_rng.NextDouble() * 20f);
            _world.Survival.AddHunger(agentName, gain);
            string found = isStore ? "packaged food" : "canned goods";
            results.Add($"{found} (+{gain:F0} hunger)");
        }

        // Water: 45% in apartments (taps, stored bottles), 30% in stores (bottled drinks)
        if (_rng.NextDouble() < (isStore ? 0.30 : 0.45))
        {
            float gain = 15f + (float)(_rng.NextDouble() * 20f);
            _world.Survival.AddThirst(agentName, gain);
            results.Add($"bottled water (+{gain:F0} thirst)");
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
        _world.LogDev($"[{agentName}] scavenge success → mood +8  stress -6");
        _world.Memory.AddMemory(agentName, $"Scavenged at ({pos.x},{pos.y}) and found {found2}.");
        return $"scavenges — finds {found2}";
    }

    private string TryForageForest(string agentName, (int x, int y) pos)
    {
        var results = new List<string>();

        // Berries: 65% chance
        if (_rng.NextDouble() < 0.65)
        {
            float gain = 12f + (float)(_rng.NextDouble() * 18f);
            _world.Survival.AddHunger(agentName, gain);
            results.Add($"wild berries (+{gain:F0} hunger)");
        }

        // Mushrooms: 50% chance (independent roll)
        if (_rng.NextDouble() < 0.50)
        {
            float gain = 8f + (float)(_rng.NextDouble() * 12f);
            _world.Survival.AddHunger(agentName, gain);
            results.Add($"mushrooms (+{gain:F0} hunger)");
        }

        if (results.Count == 0)
        {
            _world.LogAt(pos.x, pos.y, $"{agentName} searches the undergrowth but finds nothing worth eating right now.");
            return "forages the forest — found nothing";
        }

        var found = string.Join(" and ", results);
        _world.LogAt(pos.x, pos.y, $"{agentName} forages and finds {found}.");
        if (_world.Mood.Has(agentName))
        {
            var m = _world.Mood.GetMood(agentName);
            m.AdjustMood(+5f); m.AdjustStress(-4f);
        }
        _world.LogDev($"[{agentName}] forest forage → mood +5  stress -4");
        _world.Memory.AddMemory(agentName, $"Foraged in the forest at ({pos.x},{pos.y}) and found {found}.");
        return $"forages the forest — finds {found}";
    }
}
