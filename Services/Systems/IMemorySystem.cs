using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public interface IMemorySystem
{
    void Attach(WorldState world);
    void InitializeAgent(string agentName);
    void RemoveAgent(string agentName);

    void AddMemory(string agentName, string entry);
    AgentMemory GetMemory(string agentName);
}
