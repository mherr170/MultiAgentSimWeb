using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public class PresenceSystem : IPresenceSystem
{
    private const int ShelterThreshold = 3;

    private readonly Dictionary<string, (int x, int y)> _shelterCells     = new();
    private readonly Dictionary<string, int>             _consecutiveTurns = new();
    private readonly Dictionary<string, (int x, int y)> _lastPositions    = new();

    // Key is always (alphabetically-first, alphabetically-second) so lookup
    // works regardless of which agent queries it.
    private readonly Dictionary<(string, string), (int x, int y)> _meetingPoints = new();

    private WorldState _world = null!;

    public void Attach(WorldState world) => _world = world;

    public void RemoveAgent(string agentName)
    {
        _shelterCells.Remove(agentName);
        _consecutiveTurns.Remove(agentName);
        _lastPositions.Remove(agentName);
        var staleKeys = _meetingPoints.Keys
            .Where(k => k.Item1 == agentName || k.Item2 == agentName)
            .ToList();
        foreach (var k in staleKeys) _meetingPoints.Remove(k);
    }

    public void TickPresence(string agentName)
    {
        var pos  = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return;
        var cell = _world.GetCell(pos.x, pos.y);

        // ── Shelter tracking ─────────────────────────────────────────────────
        bool sameCellAsLast = _lastPositions.TryGetValue(agentName, out var last) && last == pos;
        _consecutiveTurns[agentName] = sameCellAsLast
            ? _consecutiveTurns.GetValueOrDefault(agentName) + 1
            : 1;
        _lastPositions[agentName] = pos;

        bool settleable = cell.Terrain != TerrainType.Street
                       && cell.Terrain != TerrainType.Park
                       && cell.Terrain != TerrainType.River;
        if (settleable && _consecutiveTurns[agentName] >= ShelterThreshold)
        {
            if (!_shelterCells.TryGetValue(agentName, out var existing) || existing != pos)
            {
                _shelterCells[agentName] = pos;
                var shelterName = cell.BuildingName ?? cell.DisplayName;
                _world.Memory.AddMemory(agentName,
                    $"I've settled at {shelterName} ({pos.x},{pos.y}) — this is my base now.");
                _world.LogDev($"[{agentName}] claimed shelter at ({pos.x},{pos.y})");
            }
        }

        // Passive shelter bonus (or penalty for claustrophobic agents)
        if (_shelterCells.TryGetValue(agentName, out var shelter) && shelter == pos
            && _world.Mood.Has(agentName))
        {
            var m = _world.Mood.GetMood(agentName);
            if (_world.GetPersonality(agentName).HasFlag("claustrophobic"))
            {
                m.AdjustStress(+3f);
                m.AdjustMood(-2f);
                _world.LogDev($"[{agentName}] claustrophobic at shelter → stress +3  mood -2");
            }
            else
            {
                m.AdjustStress(-3f);
                m.AdjustMood(+2f);
                _world.LogDev($"[{agentName}] at shelter → stress -3  mood +2");
            }
        }

        // ── Social anchoring ─────────────────────────────────────────────────
        foreach (var otherName in _world.AgentNames)
        {
            if (otherName == agentName) continue;
            if (_world.GetAgentPosition(otherName) != pos) continue;
            if (!_world.KnowsName(agentName, otherName) && !_world.KnowsName(otherName, agentName)) continue;
            var key = MeetingKey(agentName, otherName);
            if (!_meetingPoints.TryGetValue(key, out var prev) || prev != pos)
            {
                _meetingPoints[key] = pos;
                _world.LogDev($"[meeting] {agentName}+{otherName} anchored at ({pos.x},{pos.y})");
            }
        }
    }

    // NOTE: _lastPositions is updated to the current position during TickPresence, so comparing
    // against it after that tick would always return true. We use _consecutiveTurns instead:
    // a value of ≥2 means the agent was already at this cell at the end of the last turn.
    public bool IsStationary(string agentName) =>
        _consecutiveTurns.GetValueOrDefault(agentName, 0) >= 2;

    public bool          HasShelter(string agentName)     => _shelterCells.ContainsKey(agentName);
    public (int x, int y) GetShelter(string agentName)    => _shelterCells.GetValueOrDefault(agentName, (-1, -1));
    public (int x, int y) GetMeetingPoint(string a, string b)
        => _meetingPoints.GetValueOrDefault(MeetingKey(a, b), (-1, -1));

    private static (string, string) MeetingKey(string a, string b) =>
        string.Compare(a, b, StringComparison.Ordinal) <= 0 ? (a, b) : (b, a);
}
