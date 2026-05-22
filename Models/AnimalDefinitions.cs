namespace MultiAgentSimWeb.Models;

public record AnimalTemplate(
    AnimalType  Type,
    AnimalSize  Size,
    float       MaxHealth,
    int         DetectRadius,
    int         AttackRadius,
    int         FleeThreshold,
    float       AttackHungerDamage,
    float       AttackThirstDamage,
    float       AttackMoodDelta,
    float       AttackStressDelta,
    float       ScareChance,
    string[]    LootTable,
    float       LootChance,
    string      DisplayName,
    string      Description
);

public static class AnimalDefinitions
{
    private static readonly Dictionary<AnimalType, AnimalTemplate> _templates = new()
    {
        [AnimalType.Rat] = new(
            Type: AnimalType.Rat, Size: AnimalSize.Small,
            MaxHealth: 8f,
            DetectRadius: 2, AttackRadius: 0, FleeThreshold: 2,
            AttackHungerDamage: 0, AttackThirstDamage: 0,
            AttackMoodDelta: 0, AttackStressDelta: 0, ScareChance: 0f,
            LootTable: ["rat_carcass"], LootChance: 0.75f,
            DisplayName: "Rat",
            Description: "A large city rat emboldened by the silence. Skittish but persistent."),

        [AnimalType.Pigeon] = new(
            Type: AnimalType.Pigeon, Size: AnimalSize.Small,
            MaxHealth: 5f,
            DetectRadius: 2, AttackRadius: 0, FleeThreshold: 2,
            AttackHungerDamage: 0, AttackThirstDamage: 0,
            AttackMoodDelta: 0, AttackStressDelta: 0, ScareChance: 0f,
            LootTable: ["feather_bundle"], LootChance: 0.50f,
            DisplayName: "Pigeon",
            Description: "A street pigeon roosting on a darkened ledge. Startles easily."),

        [AnimalType.StreetCat] = new(
            Type: AnimalType.StreetCat, Size: AnimalSize.Small,
            MaxHealth: 10f,
            DetectRadius: 3, AttackRadius: 0, FleeThreshold: 3,
            AttackHungerDamage: 0, AttackThirstDamage: 0,
            AttackMoodDelta: 0, AttackStressDelta: 0, ScareChance: 0f,
            LootTable: ["fur_scraps"], LootChance: 0.65f,
            DisplayName: "Street Cat",
            Description: "A lean, wary feral cat. Watches you from the shadows but won't fight."),

        [AnimalType.Squirrel] = new(
            Type: AnimalType.Squirrel, Size: AnimalSize.Small,
            MaxHealth: 6f,
            DetectRadius: 3, AttackRadius: 0, FleeThreshold: 3,
            AttackHungerDamage: 0, AttackThirstDamage: 0,
            AttackMoodDelta: 0, AttackStressDelta: 0, ScareChance: 0f,
            LootTable: ["fur_scraps", "rat_carcass"], LootChance: 0.60f,
            DisplayName: "Squirrel",
            Description: "A park squirrel, confused and hungry. Harmless."),

        // ── Large — feral dogs ───────────────────────────────────────────────

        [AnimalType.DogPack] = new(
            Type: AnimalType.DogPack, Size: AnimalSize.Large,
            MaxHealth: 60f,
            DetectRadius: 4, AttackRadius: 1, FleeThreshold: 0,
            AttackHungerDamage: 20f, AttackThirstDamage: 10f,
            AttackMoodDelta: -20f, AttackStressDelta: +30f, ScareChance: 0.25f,
            LootTable: ["dog_carcass", "leather_scraps"], LootChance: 1.0f,
            DisplayName: "Dog Pack",
            Description: "Four or five feral dogs moving together. Coordinated, hungry, extremely dangerous."),

        [AnimalType.Rottweiler] = new(
            Type: AnimalType.Rottweiler, Size: AnimalSize.Large,
            MaxHealth: 80f,
            DetectRadius: 3, AttackRadius: 1, FleeThreshold: 0,
            AttackHungerDamage: 25f, AttackThirstDamage: 5f,
            AttackMoodDelta: -15f, AttackStressDelta: +25f, ScareChance: 0.15f,
            LootTable: ["dog_carcass", "bone_shard", "leather_scraps"], LootChance: 1.0f,
            DisplayName: "Rottweiler",
            Description: "A massive former guard dog, collar still around its neck. Territorial and unpredictable."),

        [AnimalType.PitBull] = new(
            Type: AnimalType.PitBull, Size: AnimalSize.Large,
            MaxHealth: 50f,
            DetectRadius: 3, AttackRadius: 1, FleeThreshold: 0,
            AttackHungerDamage: 15f, AttackThirstDamage: 15f,
            AttackMoodDelta: -18f, AttackStressDelta: +28f, ScareChance: 0.30f,
            LootTable: ["dog_carcass", "bone_shard"], LootChance: 1.0f,
            DisplayName: "Pit Bull",
            Description: "A scarred, muscular dog that has survived the streets on aggression alone."),

        [AnimalType.Coyote] = new(
            Type: AnimalType.Coyote, Size: AnimalSize.Large,
            MaxHealth: 45f,
            DetectRadius: 5, AttackRadius: 1, FleeThreshold: 0,
            AttackHungerDamage: 18f, AttackThirstDamage: 8f,
            AttackMoodDelta: -22f, AttackStressDelta: +32f, ScareChance: 0.20f,
            LootTable: ["dog_carcass", "leather_scraps", "fur_scraps"], LootChance: 1.0f,
            DisplayName: "Coyote",
            Description: "An urban coyote — usually avoidable, but hunger has made this one bold and reckless."),

        // ── Forest animals ───────────────────────────────────────────────────

        [AnimalType.Fox] = new(
            Type: AnimalType.Fox, Size: AnimalSize.Small,
            MaxHealth: 18f,
            DetectRadius: 5, AttackRadius: 0, FleeThreshold: 4,
            AttackHungerDamage: 0f, AttackThirstDamage: 0f,
            AttackMoodDelta: 0f, AttackStressDelta: 0f, ScareChance: 0f,
            LootTable: ["fur_scraps"], LootChance: 0.80f,
            DisplayName: "Fox",
            Description: "A red fox, lean and cautious. It watches you with amber eyes before bolting into the undergrowth."),

        [AnimalType.Deer] = new(
            Type: AnimalType.Deer, Size: AnimalSize.Large,
            MaxHealth: 55f,
            DetectRadius: 6, AttackRadius: 1, FleeThreshold: 100,  // always flees, never hunts
            AttackHungerDamage: 20f, AttackThirstDamage: 0f,
            AttackMoodDelta: -15f, AttackStressDelta: +20f, ScareChance: 0.85f,
            LootTable: ["venison", "leather_scraps"], LootChance: 1.0f,
            DisplayName: "Deer",
            Description: "A white-tailed deer at the forest edge. Enormous and silent — it will bolt if you get too close, but if cornered it lashes out with its hooves."),
    };

    public static AnimalTemplate Get(AnimalType type) =>
        _templates.TryGetValue(type, out var t) ? t
        : throw new KeyNotFoundException($"Unknown animal type: {type}");

    public static IEnumerable<AnimalTemplate> All => _templates.Values;
}
