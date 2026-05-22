namespace MultiAgentSimWeb.Services.Systems;

public interface ISurvivalSystem
{
    void Attach(WorldState world);
    void InitializeAgent(string agentName);
    void RemoveAgent(string agentName);

    /// Decays meters and returns true if the agent dies this tick.
    bool TickMeters(string agentName);

    bool IsDead(string agentName);
    float GetHunger(string agentName);
    float GetThirst(string agentName);

    void AddHunger(string agentName, float delta);
    void AddThirst(string agentName, float delta);

    string DeathCause(string agentName);

    string HungerLabel(float value);
    string ThirstLabel(float value);
    bool IsCritical(string agentName);
}
