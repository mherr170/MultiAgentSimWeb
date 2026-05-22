namespace MultiAgentSimWeb.Models;

public enum AnimalSize  { Small, Large }
public enum AnimalState { Idle, Fleeing, Hunting, Dead }

public enum AnimalType
{
    // Small (passive, roam and scatter)
    Rat,
    Pigeon,
    StreetCat,
    Squirrel,
    Fox,        // forest — passive, flees early

    // Large (territorial/aggressive, will hunt and attack)
    DogPack,
    Rottweiler,
    PitBull,
    Coyote,
    Deer        // forest — passive/flees, but can gore if cornered
}

public class Animal
{
    public Guid        Id          { get; }      = Guid.NewGuid();
    public AnimalType  Type        { get; init; }
    public AnimalSize  Size        { get; init; }
    public AnimalState State       { get; set; } = AnimalState.Idle;
    public int         X           { get; set; }
    public int         Y           { get; set; }
    public float       Health      { get; set; }
    public float       MaxHealth   { get; init; }

    public int   DetectRadius  { get; init; }
    public int   AttackRadius  { get; init; }
    public int   FleeThreshold { get; init; }

    public float AttackHungerDamage { get; init; }
    public float AttackThirstDamage { get; init; }
    public float AttackMoodDelta    { get; init; }
    public float AttackStressDelta  { get; init; }

    public float ScareChance { get; init; }

    public IReadOnlyList<string> LootTable  { get; init; } = [];
    public float                 LootChance { get; init; } = 1f;

    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";

    public int     RoundsInCurrentState { get; set; }
    public string? TargetAgentName      { get; set; }
}
