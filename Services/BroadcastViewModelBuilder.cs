using MultiAgentSimWeb.Models;
using MultiAgentSimWeb.Models.ViewModels;

namespace MultiAgentSimWeb.Services;

/// Builds a BroadcastViewModel from raw simulation state.
/// All projection logic lives here so UI components stay data-agnostic.
public static class BroadcastViewModelBuilder
{
    // Event types suppressed in the broadcast log — internal diagnostics only.
    private static readonly HashSet<string> SuppressedTypes =
        [SimEventTypes.Diag, "dev", "llm", "error"];

    /// <param name="allAgents">
    ///   Full roster captured at sim start — includes agents that have since died.
    ///   Used to show dead agents as greyed-out cards in the roster.
    /// </param>
    public static BroadcastViewModel Build(
        WorldState                       world,
        Dictionary<string, string>       agentColorHex,
        IReadOnlyList<(string Name, string ColorHex)> allAgents,
        IReadOnlyList<SimEvent>          events,
        string?                          currentAgentName,
        string?                          spotlightAgentName,
        bool                             isRunning,
        bool                             isPaused)
    {
        // Snapshot: the live dict is mutated by the sim thread; rendering reads it on the
        // Blazor render thread. Dictionary<K,V> is not thread-safe for concurrent access.
        var agentColorSnapshot = new Dictionary<string, string>(agentColorHex);

        var spotlight = spotlightAgentName != null
            ? BuildSpotlight(world, events, spotlightAgentName,
                             ColorFor(allAgents, spotlightAgentName), currentAgentName)
            : null;

        // Roster includes ALL agents (dead ones shown greyed-out), spotlight excluded.
        var roster = allAgents
            .Where(a => a.Name != spotlightAgentName)
            .Select(a => BuildRosterEntry(world, a.Name, a.ColorHex, currentAgentName))
            .ToList();

        // Full simulation log, newest-first. Suppresses internal diagnostics only.
        var narrative = events
            .Where(e => !SuppressedTypes.Contains(e.Type))
            .TakeLast(200)
            .Reverse()
            .ToList();

        var chat = events
            .Where(e => e.Type == SimEventTypes.Speech || e.Type == SimEventTypes.DirectResponse)
            .TakeLast(24)
            .ToList();

        return new BroadcastViewModel(
            Spotlight:        spotlight,
            Roster:           roster,
            NarrativeEvents:  narrative,
            ChatEvents:       chat,
            AgentColorHex:    agentColorSnapshot,
            IsRunning:        isRunning,
            IsPaused:         isPaused,
            CurrentAgentName: currentAgentName);
    }

    private static ActiveAgentViewModel BuildSpotlight(
        WorldState              world,
        IReadOnlyList<SimEvent> events,
        string                  name,
        string                  colorHex,
        string?                 currentAgentName)
    {
        bool isDead = world.IsDead(name);

        var mood      = world.GetMood(name); // safe: MoodSystem preserves last-known state in dead-pool
        var inventory = isDead
            ? (IReadOnlyList<string>)[]
            : world.GetInventory(name).Select(i => i.DisplayName).Take(5).ToList();

        string? lastThought = isDead ? null : LastEventContent(events, name, SimEventTypes.Thought);
        string? lastAction  = isDead ? null : LastEventContent(events, name, SimEventTypes.Action);

        return new ActiveAgentViewModel(
            Name:        name,
            ColorHex:    colorHex,
            Hp:          world.GetHealth(name),
            MaxHp:       world.GetMaxHealth(name),
            Hunger:      world.GetHunger(name),
            Thirst:      world.GetThirst(name),
            Stamina:     world.GetStamina(name),
            Mood:        mood.Mood,
            Stress:      mood.Stress,
            LastThought: lastThought,
            LastAction:  lastAction,
            Inventory:   inventory,
            IsThinking:  name == currentAgentName,
            IsDead:      isDead);
    }

    private static RosterAgentViewModel BuildRosterEntry(
        WorldState world,
        string     name,
        string     colorHex,
        string?    currentAgentName)
    {
        // world.IsDead() is now reliable for removed agents (SurvivalSystem tombstone).
        // GetHealth/Hunger/Thirst return 0f via GetValueOrDefault for removed agents — correct.
        return new RosterAgentViewModel(
            Name:      name,
            ColorHex:  colorHex,
            Hp:        world.GetHealth(name),
            MaxHp:     world.GetMaxHealth(name),
            Hunger:    world.GetHunger(name),
            Thirst:    world.GetThirst(name),
            IsCurrent: name == currentAgentName,
            IsDead:    world.IsDead(name));
    }

    private static string ColorFor(
        IReadOnlyList<(string Name, string ColorHex)> allAgents, string name)
    {
        foreach (var a in allAgents)
            if (a.Name == name) return a.ColorHex;
        return "#888888";
    }

    private static string? LastEventContent(IReadOnlyList<SimEvent> events, string agentName, string type)
    {
        for (int i = events.Count - 1; i >= 0; i--)
        {
            var e = events[i];
            if (e.AgentName == agentName && e.Type == type)
                return e.Content;
        }
        return null;
    }
}
