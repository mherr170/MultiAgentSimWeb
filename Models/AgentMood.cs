namespace MultiAgentSimWeb.Models;

public class AgentMood
{
    public float Mood   { get; private set; } =  0f;   // -100 to +100
    public float Stress { get; private set; } = 15f;   // 0 to 100

    // ── Long-term morale ─────────────────────────────────────────────────────
    // Trauma: accumulated psychological damage — lowers mood floor, creates stress floor.
    // Hope:   accumulated resilience and meaning — raises mood floor, eases stress recovery.
    // Both decay passively; they represent the slow-moving baseline underneath instant mood.
    public float Trauma { get; private set; } =  0f;   // 0 to 100
    public float Hope   { get; private set; } =  0f;   // 0 to 100

    private readonly Dictionary<string, float> _trust = new();

    public void AdjustMood(float delta) =>
        Mood = Math.Clamp(Mood + delta, -100f, 100f);

    public void AdjustStress(float delta) =>
        Stress = Math.Clamp(Stress + delta, 0f, 100f);

    public void AdjustTrauma(float delta) =>
        Trauma = Math.Clamp(Trauma + delta, 0f, 100f);

    public void AdjustHope(float delta) =>
        Hope = Math.Clamp(Hope + delta, 0f, 100f);

    public void AdjustTrust(string agentName, float delta)
    {
        _trust.TryGetValue(agentName, out var current);
        _trust[agentName] = Math.Clamp(current + delta, -100f, 100f);
    }

    public float GetTrust(string agentName) => _trust.GetValueOrDefault(agentName, 0f);
    public IReadOnlyDictionary<string, float> AllTrust => _trust;

    /// Passive drift toward neutral each turn.
    public void Decay()
    {
        if (Mood > 0) Mood = Math.Max(0f, Mood - 3f);
        else if (Mood < 0) Mood = Math.Min(0f, Mood + 3f);

        AdjustStress(-2f);

        // Trauma heals slowly; hope fades a bit faster — resilience is harder won than lost.
        if (Trauma > 0) Trauma = Math.Max(0f, Trauma - 0.3f);
        if (Hope   > 0) Hope   = Math.Max(0f, Hope   - 0.5f);

        // Trust drifts toward neutral at 0.5/turn — relationships fade without contact,
        // and grudges slowly heal. Active interaction rebuilds trust faster than this erodes it.
        foreach (var key in _trust.Keys.ToList())
        {
            float t = _trust[key];
            if      (t >  0.5f) _trust[key] = t - 0.5f;
            else if (t < -0.5f) _trust[key] = t + 0.5f;
            else                 _trust[key] = 0f;
        }
    }

    // ── Labels ───────────────────────────────────────────────────────────────

    public string MoodLabel => Mood switch
    {
        > 50f  => "Hopeful",
        > 20f  => "Upbeat",
        > -10f => "Neutral",
        > -30f => "Uneasy",
        > -60f => "Discouraged",
        _      => "Despairing"
    };

    public string StressLabel => Stress switch
    {
        < 15f => "Calm",
        < 35f => "Mild tension",
        < 55f => "Stressed",
        < 75f => "Highly stressed",
        _     => "PANICKED"
    };

    public string TraumaLabel => Trauma switch
    {
        < 10f  => "Untroubled",
        < 30f  => "Shaken",
        < 55f  => "Haunted",
        < 75f  => "Traumatised",
        _      => "Breaking"
    };

    public string HopeLabel => Hope switch
    {
        < 10f  => "Hopeless",
        < 25f  => "Uncertain",
        < 50f  => "Holding on",
        < 75f  => "Determined",
        _      => "Resolute"
    };

    public static string TrustLabel(float trust) => trust switch
    {
        > 85f  => "deeply bonded",
        > 70f  => "close friend",
        > 50f  => "trusted friend",
        > 20f  => "friendly",
        > -10f => "neutral",
        > -30f => "wary",
        > -60f => "suspicious",
        _      => "hostile"
    };
}
