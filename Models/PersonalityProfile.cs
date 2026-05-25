namespace MultiAgentSimWeb.Models;

/// <summary>
/// Personality traits and narrative blurb for an agent.
/// Traits are 0–100 and drive both mechanical modifiers and contextual hints.
/// </summary>
public class PersonalityProfile
{
    public string AgentName { get; init; } = "";

    /// Full narrative blurb injected into the agent's system prompt.
    /// When set, replaces the plain Persona field on AgentDefinition.
    public string Blurb { get; init; } = "";

    /// 0–100. High = bounces back from stress; Low = stress hits harder and lingers.
    public int Resilience      { get; init; } = 50;

    /// 0–100. High = mood lifted by company, hurt by isolation; Low = indifferent to solitude.
    public int Sociability     { get; init; } = 50;

    /// 0–100. High = attack-first mentality, risk tolerance, damage bonus.
    public int Aggression      { get; init; } = 50;

    /// 0–100. High = mood floor raised, negative events land softer.
    public int Optimism        { get; init; } = 50;

    /// 0–100. High = better scavenge/forage odds; Low = inefficient in the field.
    public int Resourcefulness { get; init; } = 50;

    /// Behavioral tags for conditional context hints shown to the agent.
    /// Supported values: "hoards_food", "distrusts_strangers", "fears_dark",
    ///                   "protects_others", "prone_to_panic",
    ///                   "night_owl", "claustrophobic", "self_reliant",
    ///                   "paranoid", "risk_taker", "open_to_romance"
    public IReadOnlyList<string> Flags { get; init; } = [];

    /// Unique background skill ID. Checked inline in each system.
    /// Values: "crafting_expert", "field_medic", "people_reader",
    ///         "survivor_grit", "silver_tongue", "field_naturalist"
    public string BackgroundSkill  { get; init; } = "";

    /// One-line human-readable description of the skill.
    public string SkillDescription { get; init; } = "";

    /// Maximum HP for this agent. Defaults to 100. Allows stronger/weaker profiles.
    public float MaxHealth { get; init; } = 100f;

    // ── Derived modifiers ────────────────────────────────────────────────────

    /// Multiplier on incoming stress deltas. Resilience 100 → 0.5×; 0 → 1.5×.
    public float StressMultiplier => 1.0f + (50 - Resilience) / 100f;

    /// Multiplier on negative mood deltas. Optimism 100 → 0.75×; 0 → 1.25×.
    public float MoodPenaltyMultiplier => 1.0f + (50 - Optimism) / 200f;

    /// Mood hit per turn when alone. Sociability 100 → -4; 50 → -2; 0 → 0.
    public float IsolationMoodHit => -Sociability / 25f;

    /// Mood bonus per turn when others are nearby. Sociability 100 → +4; 50 → +2; 0 → 0.
    public float CompanyMoodBonus => Sociability / 25f;

    /// Stress relief per turn from company. Sociability 100 → 4; 50 → 2; 0 → 0.
    public float CompanyStressRelief => Sociability / 25f;

    /// Flat probability bonus on scavenge/forage rolls. Range -0.10 → +0.10.
    public float ScavengeBonus => (Resourcefulness - 50) / 500f;

    /// Flat attack damage bonus from aggression. Range -10 → +10.
    public float AggressionAttackBonus => (Aggression - 50) / 5f;

    /// Starting mood offset. Optimism 100 → +20; 50 → 0; 0 → -20.
    public float InitialMoodOffset => (Optimism - 50) / 2.5f;

    /// Starting stress. Resilience 100 → 10; 50 → 15; 0 → 20.
    public float InitialStress => 15f - (Resilience - 50) / 10f;

    /// Returns true if the given flag is set on this profile.
    public bool HasFlag(string flag) => Flags.Contains(flag, StringComparer.OrdinalIgnoreCase);

    // Skill shorthands — single definition point; avoids scattered magic strings.
    public bool IsFieldMedic      => BackgroundSkill == "field_medic";
    public bool IsSilverTongue    => BackgroundSkill == "silver_tongue";
    public bool IsSurvivorGrit    => BackgroundSkill == "survivor_grit";
    public bool IsPeopleReader    => BackgroundSkill == "people_reader";
    public bool IsCraftingExpert  => BackgroundSkill == "crafting_expert";
    public bool IsFieldNaturalist => BackgroundSkill == "field_naturalist";
    public bool IsUrbanForager    => BackgroundSkill == "urban_forager";
    public bool IsAnimalHandler   => BackgroundSkill == "animal_handler";
    public bool IsCombatVeteran   => BackgroundSkill == "combat_veteran";

    // Relationship flag
    public bool IsOpenToRomance   => HasFlag("open_to_romance");

    // ── Default (neutral) ────────────────────────────────────────────────────

    public static readonly PersonalityProfile Default = new();
}
