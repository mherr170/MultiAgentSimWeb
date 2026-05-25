using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public interface IWeatherSystem
{
    WeatherState Current { get; }
    string Label { get; }

    void Attach(WorldState world);

    /// Advances weather state once per round (call before agent turns).
    void TickWeather();

    /// Flat probability penalty applied to all forage / scavenge rolls (negative = harder).
    float ForagePenalty { get; }

    /// Extra hunger drained per turn for outdoor agents (rain/wind chill strips warmth).
    float OutdoorWarmthDrain { get; }

    /// When true, outdoor speech only carries to same cell (wind/thunder drowns it out).
    bool ReducesSpeechRange { get; }

    /// When true, large animals hunker and do not hunt this round.
    bool SuppressesLargeAnimals { get; }

    /// Mood delta applied each turn to outdoor agents (negative = gloom).
    float OutdoorMoodDelta { get; }

    /// Stress delta applied each turn to outdoor agents (positive = more stress).
    float OutdoorStressDelta { get; }

    /// Full context block describing current weather and its effect on this agent.
    string GetContextBlock(string agentName);
}
