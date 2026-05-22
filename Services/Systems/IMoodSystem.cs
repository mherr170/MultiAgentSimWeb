using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public interface IMoodSystem
{
    void Attach(WorldState world);
    void InitializeAgent(string agentName);
    void RemoveAgent(string agentName);

    /// Called each turn after TickMeters. Applies loneliness/company effects,
    /// hunger/thirst-driven stress, and passive decay.
    void TickMood(string agentName);

    AgentMood GetMood(string agentName);
    bool Has(string agentName);
}
