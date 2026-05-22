using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public interface IForagingSystem
{
    void Attach(WorldState world);

    /// Returns a description of what was found, or null if unsuccessful (e.g. wrong terrain).
    string? TryForage(string agentName);

    bool CanForage(TerrainType terrain);
}
