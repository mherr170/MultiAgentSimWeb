using System.Text;
using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public class WeatherSystem : IWeatherSystem
{
    private readonly Random _rng = new();
    private WorldState _world = null!;

    public WeatherState Current { get; private set; } = WeatherState.Clear;

    public void Attach(WorldState world) => _world = world;

    // ── Per-round tick ────────────────────────────────────────────────────────

    public void TickWeather()
    {
        var next = Transition(Current);
        if (next == Current) return;

        var oldLabel = Label;
        Current = next;
        string msg = ChangeMessage(next);

        // Announce at every living agent's position and write to their memory.
        foreach (var name in _world.AgentNames)
        {
            var pos = _world.GetAgentPosition(name);
            if (pos.x >= 0) _world.LogAt(pos.x, pos.y, msg);
            _world.Memory.AddMemory(name, msg);
        }
        _world.LogDev($"[weather] {oldLabel} → {Label}");
    }

    // ── Markov transition matrix ──────────────────────────────────────────────

    private WeatherState Transition(WeatherState current)
    {
        double r = _rng.NextDouble();
        return current switch
        {
            WeatherState.Clear =>
                r < 0.65 ? WeatherState.Clear      :
                r < 0.88 ? WeatherState.Overcast   :
                           WeatherState.WindStorm,

            WeatherState.Overcast =>
                r < 0.22 ? WeatherState.Clear      :
                r < 0.52 ? WeatherState.Overcast   :
                r < 0.75 ? WeatherState.LightRain  :
                           WeatherState.WindStorm,

            WeatherState.LightRain =>
                r < 0.12 ? WeatherState.Clear      :
                r < 0.30 ? WeatherState.Overcast   :
                r < 0.65 ? WeatherState.LightRain  :
                r < 0.85 ? WeatherState.HeavyRain  :
                           WeatherState.WindStorm,

            WeatherState.HeavyRain =>
                r < 0.22 ? WeatherState.LightRain  :
                r < 0.55 ? WeatherState.HeavyRain  :
                           WeatherState.Thunderstorm,

            WeatherState.Thunderstorm =>
                r < 0.12 ? WeatherState.Clear      :
                r < 0.55 ? WeatherState.HeavyRain  :
                           WeatherState.Thunderstorm,

            WeatherState.WindStorm =>
                r < 0.18 ? WeatherState.Clear      :
                r < 0.55 ? WeatherState.Overcast   :
                           WeatherState.WindStorm,

            _ => current
        };
    }

    // ── Modifiers ─────────────────────────────────────────────────────────────

    public float ForagePenalty => Current switch
    {
        WeatherState.Clear        =>  0.00f,
        WeatherState.Overcast     => -0.05f,
        WeatherState.LightRain    => -0.10f,
        WeatherState.HeavyRain    => -0.20f,
        WeatherState.Thunderstorm => -0.30f,
        WeatherState.WindStorm    => -0.15f,
        _ => 0f
    };

    public float OutdoorWarmthDrain => Current switch
    {
        WeatherState.LightRain    => 1.0f,
        WeatherState.HeavyRain    => 2.0f,
        WeatherState.Thunderstorm => 3.0f,
        WeatherState.WindStorm    => 3.0f,
        _ => 0f
    };

    public bool ReducesSpeechRange =>
        Current is WeatherState.Thunderstorm or WeatherState.WindStorm;

    public bool SuppressesLargeAnimals =>
        Current is WeatherState.HeavyRain or WeatherState.Thunderstorm;

    public float OutdoorMoodDelta => Current switch
    {
        WeatherState.Clear        =>  1.0f,
        WeatherState.Overcast     => -1.0f,
        WeatherState.LightRain    => -2.0f,
        WeatherState.HeavyRain    => -4.0f,
        WeatherState.Thunderstorm => -6.0f,
        WeatherState.WindStorm    => -3.0f,
        _ => 0f
    };

    public float OutdoorStressDelta => Current switch
    {
        WeatherState.Clear        => -1.0f,
        WeatherState.Overcast     =>  0.0f,
        WeatherState.LightRain    =>  2.0f,
        WeatherState.HeavyRain    =>  4.0f,
        WeatherState.Thunderstorm =>  8.0f,
        WeatherState.WindStorm    =>  6.0f,
        _ => 0f
    };

    // ── Labels and context ────────────────────────────────────────────────────

    public string Label => Current switch
    {
        WeatherState.Clear        => "Clear",
        WeatherState.Overcast     => "Overcast",
        WeatherState.LightRain    => "Light Rain",
        WeatherState.HeavyRain    => "Heavy Rain",
        WeatherState.Thunderstorm => "Thunderstorm",
        WeatherState.WindStorm    => "Wind Storm",
        _ => "Unknown"
    };

    public string GetContextBlock(string agentName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"WEATHER: {Label}");

        var pos     = _world.GetAgentPosition(agentName);
        bool indoor = pos.x >= 0 && DayNightSystem.IsIndoors(_world.GetCell(pos.x, pos.y).Terrain);

        string effect = Current switch
        {
            WeatherState.Clear =>
                "Clear skies. No penalties — slightly better mood outdoors.",
            WeatherState.Overcast =>
                "Heavy cloud cover. A mild mood drag. Small penalty to outdoor scavenging.",
            WeatherState.LightRain =>
                "Steady drizzle. Foraging is harder and you're losing warmth faster outdoors.",
            WeatherState.HeavyRain =>
                "A downpour. Foraging is badly impaired. Being outside strips warmth and energy fast.",
            WeatherState.Thunderstorm =>
                "A violent thunderstorm. Lightning strobes across the city. Foraging is nearly impossible outdoors. " +
                "Large animals have taken cover. Wind and thunder drown out voices — you can only hear people in your exact location.",
            WeatherState.WindStorm =>
                "Powerful gusts tear through the streets. Warmth bleeds away fast outdoors. " +
                "The noise and wind mean you can only hear people right next to you.",
            _ => ""
        };

        if (indoor && Current is WeatherState.HeavyRain or WeatherState.Thunderstorm or WeatherState.WindStorm)
            effect += " You're indoors — sheltered from the worst of it.";

        sb.Append(effect);
        return sb.ToString();
    }

    private static string ChangeMessage(WeatherState weather) => weather switch
    {
        WeatherState.Clear        => "The weather has cleared. The air goes still.",
        WeatherState.Overcast     => "Clouds roll in, blocking out the sky.",
        WeatherState.LightRain    => "A light rain begins to fall.",
        WeatherState.HeavyRain    => "The rain intensifies — a real downpour now.",
        WeatherState.Thunderstorm => "A thunderstorm breaks. Lightning tears across the city and thunder shakes the walls.",
        WeatherState.WindStorm    => "A wind storm hits — powerful gusts rattle everything that isn't tied down.",
        _ => ""
    };
}
