namespace MultiAgentSimWeb.Models.ViewModels;

/// Per-agent data for the compact roster column (all agents except the spotlight).
public record RosterAgentViewModel(
    string Name,
    string ColorHex,
    float  Hp,
    float  MaxHp,
    float  Hunger,
    float  Thirst,
    bool   IsCurrent,
    bool   IsDead
);
