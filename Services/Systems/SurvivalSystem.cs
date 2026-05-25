namespace MultiAgentSimWeb.Services.Systems;

public class SurvivalSystem : ISurvivalSystem
{
    private const float HungerDecayPerTurn    = 2f;
    private const float ThirstDecayPerTurn    = 3.5f;
    private const float MaxMeter              = 100f;
    private const float StaminaPassiveDecay   = 1.5f;   // base per-turn drain (scaled by Resilience)
    private const float StaminaStationaryGain = 2f;     // recovered per turn when not moving
    private const float StaminaRestBonus      = 8f;     // extra recovery when resting indoors at night
    private const float StaminaGritBonus      = 2f;     // survivor_grit always-on bonus
    private const float ExhaustedThreshold    = 20f;

    private readonly Dictionary<string, float> _hunger    = new();
    private readonly Dictionary<string, float> _thirst    = new();
    private readonly Dictionary<string, float> _health    = new();
    private readonly Dictionary<string, float> _maxHealth = new();
    private readonly Dictionary<string, float> _stamina    = new();
    private readonly Dictionary<string, int>   _idleTurns  = new();
    private WorldState _world = null!;

    public void Attach(WorldState world) => _world = world;

    public void InitializeAgent(string agentName)
    {
        _hunger[agentName]  = MaxMeter;
        _thirst[agentName]  = MaxMeter;
        _stamina[agentName] = MaxMeter;
        // MaxHealth is set by SetPersonality before the loop; default to 100 if not yet set
        float maxHp = _maxHealth.GetValueOrDefault(agentName, MaxMeter);
        _maxHealth[agentName] = maxHp;
        _health[agentName]    = maxHp;
    }

    public void InitializeAgentHealth(string agentName, float maxHealth)
    {
        _maxHealth[agentName] = maxHealth;
        _health[agentName]    = maxHealth;
    }

    public void RemoveAgent(string agentName)
    {
        _hunger.Remove(agentName);
        _thirst.Remove(agentName);
        _health.Remove(agentName);
        _maxHealth.Remove(agentName);
        _stamina.Remove(agentName);
        _idleTurns.Remove(agentName);
    }

    public bool TickMeters(string agentName)
    {
        if (!_hunger.ContainsKey(agentName)) return false;
        float h0 = _hunger[agentName], t0 = _thirst[agentName];
        _hunger[agentName] = Math.Max(0f, h0 - HungerDecayPerTurn);
        _thirst[agentName] = Math.Max(0f, t0 - ThirstDecayPerTurn);
        _world.LogDev($"[{agentName}] meters tick → hunger {h0:F0}→{_hunger[agentName]:F0}  thirst {t0:F0}→{_thirst[agentName]:F0}");
        return _hunger[agentName] <= 0f || _thirst[agentName] <= 0f || _health.GetValueOrDefault(agentName, 1f) <= 0f;
    }

    public bool IsDead(string agentName) =>
        _hunger.GetValueOrDefault(agentName, 1f) <= 0f ||
        _thirst.GetValueOrDefault(agentName, 1f) <= 0f ||
        _health.GetValueOrDefault(agentName, 1f) <= 0f;

    public float GetHunger(string agentName) => _hunger.GetValueOrDefault(agentName, 0f);
    public float GetThirst(string agentName) => _thirst.GetValueOrDefault(agentName, 0f);
    public float GetHealth(string agentName)    => _health.GetValueOrDefault(agentName, 0f);
    public float GetMaxHealth(string agentName) => _maxHealth.GetValueOrDefault(agentName, MaxMeter);

    public void AddHunger(string agentName, float delta)
    {
        if (!_hunger.ContainsKey(agentName)) return;
        _hunger[agentName] = Math.Clamp(_hunger[agentName] + delta, 0f, MaxMeter);
    }

    public void AddThirst(string agentName, float delta)
    {
        if (!_thirst.ContainsKey(agentName)) return;
        _thirst[agentName] = Math.Clamp(_thirst[agentName] + delta, 0f, MaxMeter);
    }

    public void AddHealth(string agentName, float delta)
    {
        if (!_health.ContainsKey(agentName)) return;
        float maxHp = _maxHealth.GetValueOrDefault(agentName, MaxMeter);
        _health[agentName] = Math.Clamp(_health[agentName] + delta, 0f, maxHp);
        _world.LogDev($"[{agentName}] health {(delta >= 0 ? "+" : "")}{delta:F0} → {_health[agentName]:F0}/{maxHp:F0}");
    }

    public string DeathCause(string agentName)
    {
        if (_health.GetValueOrDefault(agentName, 1f) <= 0f) return "injuries";
        return _hunger.GetValueOrDefault(agentName, 1f) <= 0f ? "starvation" : "dehydration";
    }

    public string HungerLabel(float value) => value switch
    {
        > 80 => "Satisfied",
        > 55 => "Hungry",
        > 25 => "Very Hungry",
        _    => "STARVING"
    };

    public string ThirstLabel(float value) => value switch
    {
        > 80 => "Hydrated",
        > 55 => "Thirsty",
        > 25 => "Very Thirsty",
        _    => "DEHYDRATED"
    };

    public string HealthLabel(float value) => value switch
    {
        > 80 => "Healthy",
        > 55 => "Injured",
        > 25 => "Badly Injured",
        _    => "CRITICAL"
    };

    public bool IsCritical(string agentName) =>
        GetHunger(agentName) <= 20f || GetThirst(agentName) <= 20f;

    public bool IsHealthCritical(string agentName) =>
        GetHealth(agentName) <= 25f;

    // ── Stamina ──────────────────────────────────────────────────────────────

    public void TickStamina(string agentName, bool isStationary, bool isRestingIndoors)
    {
        if (!_stamina.ContainsKey(agentName)) return;

        var p = _world.GetPersonality(agentName);
        // Resilience 100 → 0.5× decay; 50 → 1.0×; 0 → 1.5×
        float decayMult = 1.5f - p.Resilience / 100f;
        float decay     = StaminaPassiveDecay * decayMult;

        float s0 = _stamina[agentName];
        _stamina[agentName] = Math.Max(0f, _stamina[agentName] - decay);

        if (isStationary)
            _stamina[agentName] = Math.Min(MaxMeter, _stamina[agentName] + StaminaStationaryGain);

        if (isRestingIndoors)
            _stamina[agentName] = Math.Min(MaxMeter, _stamina[agentName] + StaminaRestBonus);

        if (p.IsSurvivorGrit)
            _stamina[agentName] = Math.Min(MaxMeter, _stamina[agentName] + StaminaGritBonus);

        _world.LogDev($"[{agentName}] stamina {s0:F0}→{_stamina[agentName]:F0}" +
                      $"  (decay -{decay:F1}" +
                      (isRestingIndoors ? "  rest +8" : isStationary ? "  stationary +2" : "") +
                      (p.IsSurvivorGrit ? "  grit +2" : "") + ")");
    }

    public void DrainStamina(string agentName, float amount)
    {
        if (!_stamina.ContainsKey(agentName)) return;
        _stamina[agentName] = Math.Max(0f, _stamina[agentName] - amount);
    }

    public float GetStamina(string agentName) => _stamina.GetValueOrDefault(agentName, 0f);

    public bool IsExhausted(string agentName) => GetStamina(agentName) < ExhaustedThreshold;

    public string StaminaLabel(float value) => value switch
    {
        > 80 => "Energised",
        > 55 => "Okay",
        > 30 => "Tired",
        _    => "EXHAUSTED"
    };

    // ── Boredom ──────────────────────────────────────────────────────────────

    public void RecordActivity(string agentName, bool wasActive)
    {
        if (!_hunger.ContainsKey(agentName)) return;   // agent not alive

        if (wasActive)
        {
            _idleTurns[agentName] = 0;
            return;
        }

        _idleTurns.TryGetValue(agentName, out var current);
        _idleTurns[agentName] = current + 1;

        // Mood penalty kicks in after 3 idle turns, scaling with how long they've sat still.
        int idle = _idleTurns[agentName];
        if (idle < 3) return;

        if (!_world.Mood.Has(agentName)) return;
        float moodHit = Math.Min(idle - 2, 5) * -1.5f;   // -1.5 at idle-3, capping at -7.5 at idle-7+
        _world.GetMood(agentName).AdjustMood(moodHit);

        var pos = _world.GetAgentPosition(agentName);
        bool isIndoors = pos.x >= 0 && DayNightSystem.IsIndoors(_world.GetCell(pos.x, pos.y).Terrain);
        _world.LogDev($"[{agentName}] boredom (idle {idle} turns, {(isIndoors ? "indoors" : "outdoors")}) → mood {moodHit:+0.#;-0.#}");
    }

    public int GetIdleTurns(string agentName) => _idleTurns.GetValueOrDefault(agentName, 0);
}
