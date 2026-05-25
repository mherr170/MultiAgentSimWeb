using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public interface IDayNightSystem
{
    void Attach(WorldState world);

    DayPhase GetPhase(int round);
    bool IsNight(int round);

    /// Returns true exactly once per Night→Dawn transition.
    bool TryFireDawnBoost(int round);

    /// Per-agent night tick: cold hunger drain (outdoors, no warmth) + rest bonus
    /// (indoors, stationary). Safe to call every turn — no-ops outside Night phase.
    void TickAgentNight(string agentName);

    // ── Inventory queries (shared by GetContext and system checks) ────────────

    /// True if agent has a usable light source (flashlight, candle, or lighter).
    bool HasLightSource(string agentName);

    /// True if agent has a warmth item (blanket or winter_coat).
    bool HasWarmth(string agentName);
}
