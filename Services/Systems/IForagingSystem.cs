using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public interface IForagingSystem
{
    void Attach(WorldState world);

    /// Returns a description of what was found, or null if unsuccessful (e.g. wrong terrain).
    string? TryForage(string agentName);

    bool CanForage(TerrainType terrain);

    /// Returns the SCAVENGE context line for the given terrain, or null if scavenging is not
    /// available there. Callers show the "not available" fallback when null is returned.
    string? ScavengeHint(TerrainType terrain);
}
