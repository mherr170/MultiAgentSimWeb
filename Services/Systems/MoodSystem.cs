using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public class MoodSystem : IMoodSystem
{
    private readonly Dictionary<string, AgentMood> _moods = new();
    private WorldState _world = null!;

    public void Attach(WorldState world) => _world = world;

    public void InitializeAgent(string agentName) =>
        _moods[agentName] = new AgentMood();

    public void RemoveAgent(string agentName) => _moods.Remove(agentName);

    public bool Has(string agentName) => _moods.ContainsKey(agentName);

    public AgentMood GetMood(string agentName) =>
        _moods.TryGetValue(agentName, out var m) ? m : new AgentMood();

    public void TickMood(string agentName)
    {
        if (!_moods.TryGetValue(agentName, out var mood)) return;
        var pos = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return;

        float hunger = _world.Survival.GetHunger(agentName);
        float thirst = _world.Survival.GetThirst(agentName);
        if (hunger < 20f)
        { mood.AdjustMood(-3f); mood.AdjustStress(+8f); _world.LogDev($"[{agentName}] hunger critical → mood -3  stress +8"); }
        if (thirst < 20f)
        { mood.AdjustMood(-3f); mood.AdjustStress(+10f); _world.LogDev($"[{agentName}] thirst critical → mood -3  stress +10"); }

        var nearby = _world.GetAgentsInRadius(pos.x, pos.y, 1).Where(a => a.name != agentName).ToList();
        if (nearby.Count == 0)
        { mood.AdjustMood(-3f); _world.LogDev($"[{agentName}] isolated → mood -3"); }
        else
        { mood.AdjustMood(+2f); mood.AdjustStress(-2f); _world.LogDev($"[{agentName}] company ({nearby.Count}) → mood +2  stress -2"); }

        float m0 = mood.Mood, s0 = mood.Stress;
        mood.Decay();
        _world.LogDev($"[{agentName}] decay → mood {m0:+0;-0;0}→{mood.Mood:+0;-0;0}  stress {s0:F0}→{mood.Stress:F0}");
    }
}
