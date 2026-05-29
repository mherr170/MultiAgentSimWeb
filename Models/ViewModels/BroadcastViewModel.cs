namespace MultiAgentSimWeb.Models.ViewModels;

/// Everything the broadcast HUD needs to render a single tick.
/// Produced by BroadcastViewModelBuilder; passed wholesale to BroadcastHud.
public record BroadcastViewModel(
    ActiveAgentViewModel?            Spotlight,
    IReadOnlyList<RosterAgentViewModel> Roster,
    IReadOnlyList<SimEvent>          NarrativeEvents,
    IReadOnlyList<SimEvent>          ChatEvents,
    Dictionary<string, string>       AgentColorHex,
    bool                             IsRunning,
    bool                             IsPaused,
    string?                          CurrentAgentName
);
