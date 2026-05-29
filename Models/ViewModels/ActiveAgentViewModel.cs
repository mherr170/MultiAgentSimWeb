namespace MultiAgentSimWeb.Models.ViewModels;

/// All data the broadcast spotlight panel needs for the currently-active agent.
/// Built once per tick by BroadcastViewModelBuilder; components only read this.
public record ActiveAgentViewModel(
    string   Name,
    string   ColorHex,
    float    Hp,
    float    MaxHp,
    float    Hunger,
    float    Thirst,
    float    Stamina,
    float    Mood,
    float    Stress,
    string?  LastThought,
    string?  LastAction,
    IReadOnlyList<string> Inventory,
    bool     IsThinking,
    bool     IsDead
);
