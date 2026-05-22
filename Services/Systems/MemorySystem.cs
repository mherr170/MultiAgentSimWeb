using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public class MemorySystem : IMemorySystem
{
    private readonly Dictionary<string, AgentMemory> _memories = new();

    public void Attach(WorldState world) { }

    public void InitializeAgent(string agentName) =>
        _memories[agentName] = new AgentMemory();

    public void RemoveAgent(string agentName) => _memories.Remove(agentName);

    public void AddMemory(string agentName, string entry)
    {
        if (_memories.TryGetValue(agentName, out var mem))
            mem.Add(entry);
    }

    public AgentMemory GetMemory(string agentName) =>
        _memories.TryGetValue(agentName, out var mem) ? mem : new AgentMemory();
}
