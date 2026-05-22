using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public interface ICommunicationSystem
{
    void Attach(WorldState world);
    void InitializeAgent(string agentName);
    void RemoveAgent(string agentName);

    void QueueDirectMessage(DirectMessage message);
    bool HasPendingMessages(string agentName);
    IReadOnlyList<DirectMessage> DrainPendingMessages(string agentName);
}
