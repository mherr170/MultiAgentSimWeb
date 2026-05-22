namespace MultiAgentSimWeb.Services.Systems;

public interface ICraftingSystem
{
    void Attach(WorldState world);

    /// Attempts to craft the given recipe for the agent.
    /// Returns a log-friendly result string, or null if the agent position is invalid.
    string? TryCraft(string agentName, string recipeId);
}
