using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public class MoodSystem : IMoodSystem
{
    private readonly Dictionary<string, AgentMood> _moods = new();
    private WorldState _world = null!;

    public void Attach(WorldState world) => _world = world;

    public void InitializeAgent(string agentName) =>
        _moods[agentName] = new AgentMood();

    public void RemoveAgent(string agentName) => _moods.Remove(agentName);

    public bool Has(string agentName) => _moods.ContainsKey(agentName);

    public AgentMood GetMood(string agentName) =>
        _moods.TryGetValue(agentName, out var m) ? m : new AgentMood();

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
            _world.LogDev($"[{agentName}] hunger critical => mood {moodHit:+0.0;-0.0}  stress {stressHit:+0.0}");
        }
        if (thirst < 20f)
        {
            float moodHit   = -3f * p.MoodPenaltyMultiplier;
            float stressHit = +10f * p.StressMultiplier;
            mood.AdjustMood(moodHit); mood.AdjustStress(stressHit);
            _world.LogDev($"[{agentName}] thirst critical => mood {moodHit:+0.0;-0.0}  stress {stressHit:+0.0}");
        }
        if (_world.Survival.IsHealthCritical(agentName))
        {
            float moodHit   = -2f * p.MoodPenaltyMultiplier;
            float stressHit = +6f * p.StressMultiplier;
            mood.AdjustMood(moodHit); mood.AdjustStress(stressHit);
            _world.LogDev($"[{agentName}] health critical => mood {moodHit:+0.0;-0.0}  stress {stressHit:+0.0}");
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
                    _world.LogDev($"[{agentName}] romantic partner {otherName} nearby => mood +4  stress -3");
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

        float m0 = mood.Mood, s0 = mood.Stress;
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

        // Optimism floor
        float moodFloor = p.InitialMoodOffset * 0.4f;
        if (mood.Mood < moodFloor)
        {
            float lift = Math.Min(1f, moodFloor - mood.Mood);
            mood.AdjustMood(lift);
        }

        _world.LogDev($"[{agentName}] decay => mood {m0:+0;-0;0}->{mood.Mood:+0;-0;0}  stress {s0:F0}->{mood.Stress:F0}");
    }
}
