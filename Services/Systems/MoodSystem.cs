using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public class MoodSystem : IMoodSystem
{
    private readonly Dictionary<string, AgentMood> _moods    = new();
    // Agents moved here on death; GetMood() returns last-known state for read-only display.
    // Has() excludes this pool so no post-death mutations reach dead agents.
    private readonly Dictionary<string, AgentMood> _deadPool = new();
    private WorldState _world = null!;

    public void Attach(WorldState world) => _world = world;

    public void InitializeAgent(string agentName) =>
        _moods[agentName] = new AgentMood();

    public void RemoveAgent(string agentName)
    {
        if (_moods.Remove(agentName, out var last))
            _deadPool[agentName] = last;
    }

    public bool Has(string agentName) => _moods.ContainsKey(agentName);

    public AgentMood GetMood(string agentName) =>
        _moods.TryGetValue(agentName, out var m)    ? m :
        _deadPool.TryGetValue(agentName, out var d) ? d :
        new AgentMood();

    public void TickMood(string agentName)
    {
        if (!_moods.TryGetValue(agentName, out var mood)) return;
        var pos = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return;

        var p = _world.GetPersonality(agentName);

        float hunger = _world.Survival.GetHunger(agentName);
        float thirst = _world.Survival.GetThirst(agentName);

        bool isOutdoors = !DayNightSystem.IsIndoors(_world.GetCell(pos.x, pos.y).Terrain);

        // Night darkness penalty
        if (_world.IsNight)
        {
            if (isOutdoors)
            {
                if (p.HasFlag("night_owl"))
                {
                    mood.AdjustMood(+2f);
                    _world.LogDev($"[{agentName}] night_owl outdoors => mood +2");
                }
                else
                {
                    float nightStress = 3f * p.StressMultiplier;
                    float fearBonus   = p.HasFlag("fears_dark") ? 3f * p.StressMultiplier : 0f;
                    float fearMood    = p.HasFlag("fears_dark") ? -3f * p.MoodPenaltyMultiplier : 0f;
                    mood.AdjustStress(nightStress + fearBonus);
                    if (fearMood < 0) mood.AdjustMood(fearMood);
                    _world.LogDev($"[{agentName}] night outdoors => stress +{nightStress + fearBonus:F1}");
                }
            }
        }

        // Weather effects
        {
            float wMood   = _world.Weather.OutdoorMoodDelta;
            float wStress = _world.Weather.OutdoorStressDelta;
            if (isOutdoors)
            {
                if (wMood != 0f)
                    mood.AdjustMood(wMood * (wMood < 0 ? p.MoodPenaltyMultiplier : 1f));
                if (wStress != 0f)
                    mood.AdjustStress(wStress * (wStress > 0 ? p.StressMultiplier : 1f));
                _world.LogDev($"[{agentName}] weather ({_world.Weather.Label}) outdoors => mood {wMood:+0.0;-0.0}  stress {wStress:+0.0;-0.0}");

                if (_world.Weather.Current == WeatherState.Thunderstorm
                    && p.HasFlag("prone_to_panic"))
                {
                    float panicSpike = 6f * p.StressMultiplier;
                    mood.AdjustStress(panicSpike);
                    _world.LogDev($"[{agentName}] prone_to_panic in thunderstorm => stress +{panicSpike:F1}");
                }
            }
        }

        // Exhaustion penalty
        if (_world.IsExhausted(agentName))
        {
            float exhaustStress = 3f * p.StressMultiplier;
            float exhaustMood   = -2f * p.MoodPenaltyMultiplier;
            mood.AdjustStress(exhaustStress);
            mood.AdjustMood(exhaustMood);
            _world.LogDev($"[{agentName}] exhausted => stress +{exhaustStress:F1}  mood {exhaustMood:+0.0;-0.0}");
        }

        // Hunger/thirst/health crises
        if (hunger < 20f)
        {
            float moodHit   = -3f * p.MoodPenaltyMultiplier;
            float stressHit = +8f * p.StressMultiplier;
            mood.AdjustMood(moodHit); mood.AdjustStress(stressHit);
            mood.AdjustTrauma(+2f);
            _world.LogDev($"[{agentName}] hunger critical => mood {moodHit:+0.0;-0.0}  stress {stressHit:+0.0}  trauma +2");
        }
        if (thirst < 20f)
        {
            float moodHit   = -3f * p.MoodPenaltyMultiplier;
            float stressHit = +10f * p.StressMultiplier;
            mood.AdjustMood(moodHit); mood.AdjustStress(stressHit);
            mood.AdjustTrauma(+2f);
            _world.LogDev($"[{agentName}] thirst critical => mood {moodHit:+0.0;-0.0}  stress {stressHit:+0.0}  trauma +2");
        }
        if (_world.Survival.IsHealthCritical(agentName))
        {
            float moodHit   = -2f * p.MoodPenaltyMultiplier;
            float stressHit = +6f * p.StressMultiplier;
            mood.AdjustMood(moodHit); mood.AdjustStress(stressHit);
            mood.AdjustTrauma(+3f);
            _world.LogDev($"[{agentName}] health critical => mood {moodHit:+0.0;-0.0}  stress {stressHit:+0.0}  trauma +3");
        }

        // Isolation / company
        var nearby = _world.GetAgentsInRadius(pos.x, pos.y, 1).Where(a => a.name != agentName).ToList();
        if (nearby.Count == 0)
        {
            float isolationHit = p.IsolationMoodHit;
            if (isolationHit < 0)
            {
                mood.AdjustMood(isolationHit);
                _world.LogDev($"[{agentName}] isolated => mood {isolationHit:+0.0;-0.0}  (soc={p.Sociability})");
            }
        }
        else
        {
            float companyMood   = p.CompanyMoodBonus;
            float companyStress = p.CompanyStressRelief;
            mood.AdjustMood(+companyMood);
            mood.AdjustStress(-companyStress);
            _world.LogDev($"[{agentName}] company ({nearby.Count}) => mood +{companyMood:F1}  stress -{companyStress:F1}  (soc={p.Sociability})");

            foreach (var (otherName, _, _) in nearby)
            {
                float trust = mood.GetTrust(otherName);

                if (_world.AreRomantic(agentName, otherName))
                {
                    mood.AdjustMood(+4f);
                    mood.AdjustStress(-3f);
                    mood.AdjustHope(+3f);
                    _world.LogDev($"[{agentName}] romantic partner {otherName} nearby => mood +4  stress -3  hope +3");
                }
                else if (trust > 70f)
                {
                    mood.AdjustMood(+2f);
                    mood.AdjustStress(-1f);
                    _world.LogDev($"[{agentName}] close friend {otherName} nearby => mood +2  stress -1");
                }
                else if (trust > 50f)
                {
                    mood.AdjustMood(+1f);
                    _world.LogDev($"[{agentName}] trusted friend {otherName} nearby => mood +1");
                }
                else if (trust < -30f)
                {
                    float hostileStress = 6f * p.StressMultiplier;
                    mood.AdjustStress(hostileStress);
                    _world.LogDev($"[{agentName}] hostile {otherName} nearby => stress +{hostileStress:F1}");
                }

                _world.TryFormRomance(agentName, otherName);
            }

            if (p.HasFlag("paranoid"))
            {
                int unknownCount = nearby.Count(a => !_world.KnowsName(agentName, a.name));
                if (unknownCount > 0)
                {
                    float panicStress = unknownCount * 4f * p.StressMultiplier;
                    mood.AdjustStress(panicStress);
                    _world.LogDev($"[{agentName}] paranoid -- {unknownCount} unknown nearby => stress +{panicStress:F1}");
                }
            }
        }

        // Hope from sustained good states (checked each tick, before decay)
        if (hunger > 65f && thirst > 65f)
        {
            mood.AdjustHope(+2f);
            _world.LogDev($"[{agentName}] well-provisioned => hope +2");
        }
        if (_world.Groups.GetGroup(agentName) != null)
        {
            mood.AdjustHope(+2f);
            _world.LogDev($"[{agentName}] in group => hope +2");
        }

        float m0 = mood.Mood, s0 = mood.Stress;
        float trauma0 = mood.Trauma, hope0 = mood.Hope;
        mood.Decay();

        // Resilience bonus
        float resilienceBonus = (p.Resilience - 50) / 50f;
        if (resilienceBonus > 0)
        {
            mood.AdjustStress(-resilienceBonus);
            _world.LogDev($"[{agentName}] resilience stress bonus => -{resilienceBonus:F2}");
        }

        // Survivor Grit
        if (p.IsSurvivorGrit)
        {
            mood.AdjustStress(-2f);
            _world.LogDev($"[{agentName}] survivor_grit stress bonus => -2");
        }

        // ── Long-term morale floor ────────────────────────────────────────────
        // Trauma lowers the floor; hope raises it; personality provides the baseline.
        float traumaPenalty = mood.Trauma * 0.15f;   // -0 to -15 on mood floor
        float hopeBonus     = mood.Hope   * 0.12f;   // +0 to +12 on mood floor
        float longTermFloor = p.InitialMoodOffset * 0.4f - traumaPenalty + hopeBonus;
        if (mood.Mood < longTermFloor)
        {
            float lift = Math.Min(1f, longTermFloor - mood.Mood);
            mood.AdjustMood(lift);
        }

        // Trauma: chronic anxiety — stress never fully drains when heavily traumatised
        if (mood.Trauma > 40f)
        {
            float stressFloor = (mood.Trauma - 40f) / 4f;   // 0 → 15 at max trauma
            if (mood.Stress < stressFloor)
                mood.AdjustStress(stressFloor - mood.Stress);
        }
        // Extreme trauma adds a tick of stress each round (can't shake it)
        if (mood.Trauma > 70f)
        {
            mood.AdjustStress(+1f);
            _world.LogDev($"[{agentName}] trauma {mood.Trauma:F0} => chronic stress +1");
        }

        // Hope: settled confidence — extra stress relief above 50
        if (mood.Hope > 50f)
        {
            float relief = (mood.Hope - 50f) / 50f;   // 0.0 → 1.0
            mood.AdjustStress(-relief);
            _world.LogDev($"[{agentName}] hope {mood.Hope:F0} => settled relief -{relief:F2}");
        }

        _world.LogDev($"[{agentName}] decay => mood {m0:+0;-0;0}->{mood.Mood:+0;-0;0}  stress {s0:F0}->{mood.Stress:F0}" +
                      $"  trauma {trauma0:F0}->{mood.Trauma:F0}  hope {hope0:F0}->{mood.Hope:F0}");
    }

    public void ProcessDeath(string deceasedName)
    {
        // Grief: surviving agents who had positive trust in the deceased react emotionally.
        // Agents within radius 2 of the death cell are skipped — KillAgent's witness sweep
        // already applied a proximity-based hit to them, so adding trust-grief on top would
        // double-penalise nearby survivors.
        var deathPos = _world.GetAgentPosition(deceasedName);

        foreach (var survivor in _world.AgentNames)
        {
            if (survivor == deceasedName) continue;
            if (!_moods.TryGetValue(survivor, out var mood)) continue;

            float trust = mood.GetTrust(deceasedName);
            if (trust <= 0f) continue; // indifferent or hostile — no grief

            // Skip if within proximity of the death cell (already handled).
            var (sx, sy) = _world.GetAgentPosition(survivor);
            if (deathPos.x >= 0 && sx >= 0 &&
                Math.Max(Math.Abs(sx - deathPos.x), Math.Abs(sy - deathPos.y)) <= 2)
                continue;

            float moodHit   = trust > 70f ? -20f
                            : trust > 50f ? -14f
                            : trust > 20f ?  -8f
                            :                -4f;
            float stressHit = trust > 70f ? +18f
                            : trust > 50f ? +12f
                            : trust > 20f ?  +7f
                            :                +3f;

            mood.AdjustMood(moodHit);
            mood.AdjustStress(stressHit);
            _world.Memory.AddMemory(survivor, $"{deceasedName} has died. I knew them.");
            _world.LogDev(
                $"[{survivor}] grief for {deceasedName} → mood {moodHit:+0;-0}  stress {stressHit:+0;-0}" +
                $"  (trust was {trust:F0}, distant grief)");
        }
    }
}
