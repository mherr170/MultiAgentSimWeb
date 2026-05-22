namespace MultiAgentSimWeb.Services.Systems;

public class SurvivalSystem : ISurvivalSystem
{
    private const float HungerDecayPerTurn = 2f;
    private const float ThirstDecayPerTurn = 3.5f;
    private const float MaxMeter           = 100f;

    private readonly Dictionary<string, float> _hunger = new();
    private readonly Dictionary<string, float> _thirst = new();
    private WorldState _world = null!;

    public void Attach(WorldState world) => _world = world;

    public void InitializeAgent(string agentName)
    {
        _hunger[agentName] = MaxMeter;
        _thirst[agentName] = MaxMeter;
    }

    public void RemoveAgent(string agentName)
    {
        _hunger.Remove(agentName);
        _thirst.Remove(agentName);
    }

    public bool TickMeters(string agentName)
    {
        if (!_hunger.ContainsKey(agentName)) return false;
        float h0 = _hunger[agentName], t0 = _thirst[agentName];
        _hunger[agentName] = Math.Max(0f, h0 - HungerDecayPerTurn);
        _thirst[agentName] = Math.Max(0f, t0 - ThirstDecayPerTurn);
        _world.LogDev($"[{agentName}] meters tick → hunger {h0:F0}→{_hunger[agentName]:F0}  thirst {t0:F0}→{_thirst[agentName]:F0}");
        return _hunger[agentName] <= 0f || _thirst[agentName] <= 0f;
    }

    public bool IsDead(string agentName) =>
        _hunger.GetValueOrDefault(agentName, 1f) <= 0f ||
        _thirst.GetValueOrDefault(agentName, 1f) <= 0f;

    public float GetHunger(string agentName) => _hunger.GetValueOrDefault(agentName, 0f);
    public float GetThirst(string agentName) => _thirst.GetValueOrDefault(agentName, 0f);

    public void AddHunger(string agentName, float delta)
    {
        if (!_hunger.ContainsKey(agentName)) return;
        _hunger[agentName] = Math.Clamp(_hunger[agentName] + delta, 0f, MaxMeter);
    }

    public void AddThirst(string agentName, float delta)
    {
        if (!_thirst.ContainsKey(agentName)) return;
        _thirst[agentName] = Math.Clamp(_thirst[agentName] + delta, 0f, MaxMeter);
    }

    public string DeathCause(string agentName) =>
        _hunger.GetValueOrDefault(agentName, 1f) <= 0f ? "starvation" : "dehydration";

    public string HungerLabel(float value) => value switch
    {
        > 80 => "Satisfied",
        > 55 => "Hungry",
        > 25 => "Very Hungry",
        _    => "STARVING"
    };

    public string ThirstLabel(float value) => value switch
    {
        > 80 => "Hydrated",
        > 55 => "Thirsty",
        > 25 => "Very Thirsty",
        _    => "DEHYDRATED"
    };

    public bool IsCritical(string agentName) =>
        GetHunger(agentName) <= 20f || GetThirst(agentName) <= 20f;
}
