namespace MultiAgentSimWeb.Models;

public class ItemDefinition
{
    public string Id                               { get; init; } = "";
    public string Name                             { get; init; } = "";
    public string Description                      { get; init; } = "";
    public bool   IsUsable                         { get; init; }
    public string UseEffect                        { get; init; } = "";
    public bool   IsDeconstructable                { get; init; }
    public float  DeconstructChance                { get; init; }
    public IReadOnlyList<string> DeconstructYields { get; init; } = [];

    // Hunger/thirst restored when used (0 = no effect)
    public float HungerRestore { get; init; }
    public float ThirstRestore { get; init; }

    // Mood and stress delta applied on use.
    // Defaults represent the minor relief of simply doing something productive.
    public float MoodDelta   { get; init; } = 6f;
    public float StressDelta { get; init; } = -5f;

    // 0 = consumed on first use. Positive = number of charges before the item is removed.
    public int MaxUses { get; init; } = 0;

    // Flat damage bonus added to animal attacks when this item is in the agent's inventory.
    public float AttackBonus { get; init; } = 0f;

    // > 0 means this item is a container — it adds this many carry slots while in inventory.
    public int CarryCapacity { get; init; } = 0;

    // HP restored when this item is used (0 = no physical healing effect).
    public float HealthRestore { get; init; } = 0f;

    // Non-empty = the definition ID this item becomes when filled with water.
    // Empty string means not fillable.
    public string FillResult { get; init; } = "";

    // True if this item can be cooked. CookResult is the definition ID it becomes.
    public bool   IsCookable  { get; init; }
    public string CookResult  { get; init; } = "";

    // Non-empty = the definition ID a filled container becomes when purified with a purification tablet.
    public string PurifyResult { get; init; } = "";
}
