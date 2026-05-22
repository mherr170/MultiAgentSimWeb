using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public class CommunicationSystem : ICommunicationSystem
{
    private readonly Dictionary<string, List<DirectMessage>> _inboxes = new();
    private WorldState _world = null!;

    public void Attach(WorldState world) => _world = world;

    public void InitializeAgent(string agentName) =>
        _inboxes[agentName] = new List<DirectMessage>();

    public void RemoveAgent(string agentName) =>
        _inboxes.Remove(agentName);

    public void QueueDirectMessage(DirectMessage message)
    {
        if (message.FromAgent == message.ToAgent) return;

        var fromPos = _world.GetAgentPosition(message.FromAgent);
        var toPos   = _world.GetAgentPosition(message.ToAgent);

        if (fromPos == (-1, -1) || toPos == (-1, -1)) return;

        int chebyshev = Math.Max(
            Math.Abs(toPos.x - fromPos.x),
            Math.Abs(toPos.y - fromPos.y));

        if (chebyshev > 1)
        {
            _world.LogDev($"[{message.FromAgent}] tried to address {message.ToAgent} but they are too far away");
            return;
        }

        if (!_inboxes.TryGetValue(message.ToAgent, out var inbox))
        {
            inbox = new List<DirectMessage>();
            _inboxes[message.ToAgent] = inbox;
        }

        inbox.Add(message);
    }

    public bool HasPendingMessages(string agentName) =>
        _inboxes.TryGetValue(agentName, out var inbox) && inbox.Count > 0;

    public IReadOnlyList<DirectMessage> DrainPendingMessages(string agentName)
    {
        if (!_inboxes.TryGetValue(agentName, out var inbox) || inbox.Count == 0)
            return [];

        var snapshot = inbox.ToList();
        inbox.Clear();
        return snapshot;
    }
}
