namespace MultiAgentSimWeb.Services.Systems;

public interface ISurvivalSystem
{
    void Attach(WorldState world);
    void InitializeAgent(string agentName);
    void InitializeAgentHealth(string agentName, float maxHealth);
    void RemoveAgent(string agentName);

    /// Decays meters and returns true if the agent dies this tick.
    bool TickMeters(string agentName);

    bool IsDead(string agentName);
    float GetHunger(string agentName);
    float GetThirst(string agentName);

    void AddHunger(string agentName, float delta);
    void AddThirst(string agentName, float delta);

    float GetHealth(string agentName);
    float GetMaxHealth(string agentName);
    void AddHealth(string agentName, float delta);

    string DeathCause(string agentName);

    string HungerLabel(float value);
    string ThirstLabel(float value);
    string HealthLabel(float value);
    bool IsCritical(string agentName);
    bool IsHealthCritical(string agentName);

    // ── Stamina ──────────────────────────────────────────────────────────────
    /// Per-turn stamina decay/recovery. Call after movement has already been resolved.
    /// <paramref name="isStationary"/> = agent didn't move this turn (from PresenceSystem).
    /// <paramref name="isRestingIndoors"/> = stationary + indoors + night.
    void TickStamina(string agentName, bool isStationary, bool isRestingIndoors);

    /// Immediate stamina drain (call on each cell moved).
    void DrainStamina(string agentName, float amount);

    float GetStamina(string agentName);
    bool IsExhausted(string agentName);
    string StaminaLabel(float value);

    // ── Boredom ──────────────────────────────────────────────────────────────
    /// Call once per turn after the action is resolved.
    /// <paramref name="wasActive"/> = agent moved, spoke, used/gave an item, foraged, fished, cooked, or crafted.
    void RecordActivity(string agentName, bool wasActive);

    int GetIdleTurns(string agentName);
}
