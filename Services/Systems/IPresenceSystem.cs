namespace MultiAgentSimWeb.Services.Systems;

public interface IPresenceSystem
{
    void Attach(WorldState world);
    void RemoveAgent(string agentName);

    /// Called once per agent turn after all actions are resolved.
    /// Updates consecutive-stay counter, claims shelter, applies shelter bonus,
    /// and records social meeting points.
    void TickPresence(string agentName);

    /// True if the agent has a claimed shelter cell.
    bool HasShelter(string agentName);

    /// Returns the shelter cell, or (-1,-1) if none.
    (int x, int y) GetShelter(string agentName);

    /// Returns the last cell where agentA and agentB were co-located, or (-1,-1).
    (int x, int y) GetMeetingPoint(string agentA, string agentB);
}
