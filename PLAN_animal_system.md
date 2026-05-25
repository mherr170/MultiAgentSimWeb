# Animal System — Implementation Plan

**Scope:** Two tiers of forest wildlife (small passive animals, large predatory animals) that move autonomously each round, interact with agents through deterministic rules, drop loot on death, and feed memories and mood into the LLM agent prompts.

**Research only — no code has been changed.**

---

## 1. Architecture Overview

The animal system follows the established `IXxxSystem` → concrete class → `WorldState` property → `SimulationService` dispatch pattern exactly. No existing file is restructured; each change is an additive insertion point.

```
Models/
  Animal.cs               ← new: Animal class + AnimalType/AnimalState/AnimalSize enums
  AnimalDefinitions.cs    ← new: static registry mapping AnimalType → AnimalTemplate
  ItemRegistry.cs         ← modify: 6 new loot items appended
  AgentAction.cs          ← modify: 2 new fields (animal_action, animal_target_id)

Services/Systems/
  IAnimalSystem.cs        ← new: interface (swappable/disableable)
  AnimalSystem.cs         ← new: concrete implementation (~250 lines)

Services/
  WorldState.cs           ← modify: add Animals property, wire in constructor,
                                     add forwarding helpers, extend GetContext()
  SimulationService.cs    ← modify: InitializeAnimals(), TickAnimals() call,
                                     DispatchAnimalAction() helper

Services/AgentRunner.cs   ← modify: extend SystemTemplate to describe animal actions

Program.cs                ← modify: register IAnimalSystem → AnimalSystem

Components/MapView.razor  ← modify (optional): Layer 3.5 — animal icon overlays
```

---

## 2. Animal Data Model

### 2.1 `Models/Animal.cs`

```csharp
namespace MultiAgentSimWeb.Models;

public enum AnimalSize  { Small, Large }
public enum AnimalState { Idle, Fleeing, Hunting, Dead }

public enum AnimalType
{
    // Small (passive, flee-on-sight)
    GlowMouse,      // alien rodent, bioluminescent fur
    FeatherBug,     // large insect with iridescent wings, skittish
    TwigBird,       // camouflaged avian, bolt-speed flee
    SporeVole,      // burrowing mammal-analog, leaves spore trail

    // Large (territorial/predatory, will hunt and attack)
    ShadowStrider,  // apex pack hunter, long limbs, ambush style
    BoneWarden,     // armoured territorial beast, charges intruders
    ThornBeast,     // quadruped with quill-like dorsal spines
    CrestHunter     // solitary ambush predator, camouflage capable
}

public class Animal
{
    public Guid        Id           { get; }      = Guid.NewGuid();
    public AnimalType  Type         { get; init; }
    public AnimalSize  Size         { get; init; }
    public AnimalState State        { get; set; } = AnimalState.Idle;
    public int         X            { get; set; }
    public int         Y            { get; set; }
    public float       Health       { get; set; }
    public float       MaxHealth    { get; init; }

    // Detection and behaviour radii (Chebyshev distance)
    public int   DetectRadius  { get; init; }  // how far animal senses agents
    public int   AttackRadius  { get; init; }  // must be ≤1 (same or adjacent cell)
    public int   FleeThreshold { get; init; }  // distance at which small animal starts fleeing

    // Combat (large only; 0 for small)
    public float AttackHungerDamage { get; init; }  // hunger reduction when attacked
    public float AttackThirstDamage { get; init; }  // thirst reduction when attacked
    public float AttackMoodDelta    { get; init; }  // mood delta (negative)
    public float AttackStressDelta  { get; init; }  // stress delta (positive)

    // Scare mechanics (large only)
    public float ScareChance { get; init; }         // 0-1 probability scare attempt succeeds

    // Loot
    public IReadOnlyList<string> LootTable { get; init; } = [];
    public float                 LootChance { get; init; } = 1f;

    // Internal AI state
    public int RoundsInCurrentState { get; set; }
    public string? TargetAgentName  { get; set; }  // large animals track their prey
}
```

**Key design choices:**

- `Health` is a float so partial damage from combat is precise.
- `DetectRadius` / `FleeThreshold` are separate: a small animal might detect an agent at radius 2 but only start running at radius 1 (so it doesn't bolt at ghosts).
- Damage is split into HungerDamage + ThirstDamage rather than adding a new "health" stat for agents. This keeps agent mortality inside the existing `SurvivalSystem` — no new survival plumbing required.
- `TargetAgentName` lets large animals maintain chase continuity across ticks even if the agent moves.

---

### 2.2 `Models/AnimalDefinitions.cs`

A static registry mirroring `ItemRegistry`, mapping `AnimalType` → an `AnimalTemplate` record. `AnimalSystem.SpawnAnimal()` reads from this to stamp out `Animal` instances.

```csharp
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
        [AnimalType.GlowMouse] = new(
            Type: AnimalType.GlowMouse, Size: AnimalSize.Small,
            MaxHealth: 10f,
            DetectRadius: 2, AttackRadius: 0, FleeThreshold: 2,
            AttackHungerDamage: 0, AttackThirstDamage: 0,
            AttackMoodDelta: 0, AttackStressDelta: 0, ScareChance: 0f,
            LootTable: ["small_carcass"], LootChance: 0.80f,
            DisplayName: "Glow-Mouse",
            Description: "A palm-sized rodent with softly luminescent fur. Harmless."),

        [AnimalType.FeatherBug] = new(
            Type: AnimalType.FeatherBug, Size: AnimalSize.Small,
            MaxHealth: 6f,
            DetectRadius: 1, AttackRadius: 0, FleeThreshold: 1,
            AttackHungerDamage: 0, AttackThirstDamage: 0,
            AttackMoodDelta: 0, AttackStressDelta: 0, ScareChance: 0f,
            LootTable: ["chitin_shard", "feather_tuft"], LootChance: 0.60f,
            DisplayName: "Feather-Bug",
            Description: "A fist-sized insect with iridescent feather-wings. Bolt-fast."),

        [AnimalType.TwigBird] = new(
            Type: AnimalType.TwigBird, Size: AnimalSize.Small,
            MaxHealth: 8f,
            DetectRadius: 3, AttackRadius: 0, FleeThreshold: 3,
            AttackHungerDamage: 0, AttackThirstDamage: 0,
            AttackMoodDelta: 0, AttackStressDelta: 0, ScareChance: 0f,
            LootTable: ["feather_tuft", "small_carcass"], LootChance: 0.70f,
            DisplayName: "Twig-Bird",
            Description: "A camouflaged avian that launches into flight at any approach."),

        [AnimalType.SporeVole] = new(
            Type: AnimalType.SporeVole, Size: AnimalSize.Small,
            MaxHealth: 12f,
            DetectRadius: 2, AttackRadius: 0, FleeThreshold: 2,
            AttackHungerDamage: 0, AttackThirstDamage: 0,
            AttackMoodDelta: 0, AttackStressDelta: 0, ScareChance: 0f,
            LootTable: ["small_carcass", "chitin_shard"], LootChance: 0.75f,
            DisplayName: "Spore-Vole",
            Description: "A stout burrowing mammal-analog. Leaves a spore trail but won't fight."),

        // ── Large predators ────────────────────────────────────────────────────

        [AnimalType.ShadowStrider] = new(
            Type: AnimalType.ShadowStrider, Size: AnimalSize.Large,
            MaxHealth: 60f,
            DetectRadius: 4, AttackRadius: 1, FleeThreshold: 0,
            AttackHungerDamage: 20f, AttackThirstDamage: 10f,
            AttackMoodDelta: -20f, AttackStressDelta: +30f, ScareChance: 0.25f,
            LootTable: ["large_carcass", "tough_hide"], LootChance: 1.0f,
            DisplayName: "Shadow-Strider",
            Description: "An apex pack-hunter with long, silent limbs. Extremely dangerous."),

        [AnimalType.BoneWarden] = new(
            Type: AnimalType.BoneWarden, Size: AnimalSize.Large,
            MaxHealth: 80f,
            DetectRadius: 3, AttackRadius: 1, FleeThreshold: 0,
            AttackHungerDamage: 25f, AttackThirstDamage: 5f,
            AttackMoodDelta: -15f, AttackStressDelta: +25f, ScareChance: 0.15f,
            LootTable: ["large_carcass", "bone_shard", "tough_hide"], LootChance: 1.0f,
            DisplayName: "Bone-Warden",
            Description: "An armoured territorial beast with a bone-plated hide. Charges intruders."),

        [AnimalType.ThornBeast] = new(
            Type: AnimalType.ThornBeast, Size: AnimalSize.Large,
            MaxHealth: 50f,
            DetectRadius: 3, AttackRadius: 1, FleeThreshold: 0,
            AttackHungerDamage: 15f, AttackThirstDamage: 15f,
            AttackMoodDelta: -18f, AttackStressDelta: +28f, ScareChance: 0.30f,
            LootTable: ["large_carcass", "bone_shard"], LootChance: 1.0f,
            DisplayName: "Thorn-Beast",
            Description: "A quadruped with quill-like dorsal spines that spray when threatened."),

        [AnimalType.CrestHunter] = new(
            Type: AnimalType.CrestHunter, Size: AnimalSize.Large,
            MaxHealth: 45f,
            DetectRadius: 5, AttackRadius: 1, FleeThreshold: 0,
            AttackHungerDamage: 18f, AttackThirstDamage: 8f,
            AttackMoodDelta: -22f, AttackStressDelta: +32f, ScareChance: 0.20f,
            LootTable: ["large_carcass", "tough_hide", "chitin_shard"], LootChance: 1.0f,
            DisplayName: "Crest-Hunter",
            Description: "A solitary ambush predator with adaptive camouflage. Strikes without warning."),
    };

    public static AnimalTemplate Get(AnimalType type) =>
        _templates.TryGetValue(type, out var t) ? t
        : throw new KeyNotFoundException($"Unknown animal type: {type}");

    public static IEnumerable<AnimalTemplate> All => _templates.Values;
}
```

---

## 3. New Loot Items — `Models/ItemRegistry.cs` Additions

Append these six entries to `_items` in the existing `ItemRegistry` dictionary. No other change to that file.

```csharp
["small_carcass"] = new ItemDefinition
{
    Id = "small_carcass", Name = "Small Animal Carcass",
    Description = "A freshly caught small forest creature. Edible if desperate.",
    IsUsable = true,
    UseEffect = "You gut and eat the small animal raw. Unpleasant but filling.",
    HungerRestore = 18f,
    IsDeconstructable = true, DeconstructChance = 1.0f,
    DeconstructYields = ["feather_tuft"]
},
["large_carcass"] = new ItemDefinition
{
    Id = "large_carcass", Name = "Large Predator Carcass",
    Description = "The body of a dangerous forest creature. Rich meat, useful hide.",
    IsUsable = true,
    UseEffect = "You butcher and eat part of the predator. Substantial meal.",
    HungerRestore = 45f,
    IsDeconstructable = true, DeconstructChance = 1.0f,
    DeconstructYields = ["tough_hide", "bone_shard"]
},
["feather_tuft"] = new ItemDefinition
{
    Id = "feather_tuft", Name = "Feather Tuft",
    Description = "A cluster of iridescent alien feathers. Light and strong.",
    IsUsable = false,
    IsDeconstructable = false
},
["chitin_shard"] = new ItemDefinition
{
    Id = "chitin_shard", Name = "Chitin Shard",
    Description = "A curved plate of insect chitin, surprisingly hard.",
    IsUsable = false,
    IsDeconstructable = false
},
["tough_hide"] = new ItemDefinition
{
    Id = "tough_hide", Name = "Tough Hide",
    Description = "Thick, leathery skin from a large predator. Could insulate or reinforce.",
    IsUsable = false,
    IsDeconstructable = false
},
["bone_shard"] = new ItemDefinition
{
    Id = "bone_shard", Name = "Bone Shard",
    Description = "A dense, sharp fragment of alien bone. Useful as a cutting tool.",
    IsUsable = true,
    UseEffect = "You use the bone as a crude blade. It holds its edge for now.",
    IsDeconstructable = false
},
```

---

## 4. Interface — `Services/Systems/IAnimalSystem.cs`

```csharp
using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public interface IAnimalSystem
{
    // Lifecycle
    void Attach(WorldState world);
    void InitializeAnimals();

    /// Run one full animal tick (movement + attacks). Called once per round,
    /// BEFORE any agent takes their turn.
    void TickAnimals();

    // Queries used by WorldState.GetContext()
    IReadOnlyList<Animal> AllAnimals { get; }
    IReadOnlyList<Animal> GetAnimalsAt(int x, int y);
    IReadOnlyList<Animal> GetAnimalsInRadius(int cx, int cy, int radius);

    // Agent interactions (called from SimulationService after agent action is parsed)
    /// Agent physically attacks an animal in their cell. Returns result description or null.
    string? TryAttackAnimal(string agentName, string animalIdStr);

    /// Agent attempts to catch a small animal in their cell (requires Wire Bundle in inventory).
    /// Returns result description or null.
    string? TryTrapAnimal(string agentName, string animalIdStr);

    /// Agent tries to scare a large animal away. Chance-based; may backfire.
    /// Returns result description or null.
    string? TryScareAnimal(string agentName, string animalIdStr);
}
```

**Disabling the system:** To disable animals entirely, register a `NullAnimalSystem` that implements the interface with all no-ops. No other file changes required.

---

## 5. Concrete Implementation — `Services/Systems/AnimalSystem.cs`

Below is the full specification for each method. Pseudocode-level detail is given so implementation is mechanical.

### 5.1 Fields

```csharp
private readonly List<Animal> _animals = new();
private readonly Random _rng = new();
private WorldState _world = null!;
```

### 5.2 `Attach` / `InitializeAnimals`

`Attach` stores the `WorldState` reference. `InitializeAnimals` is called once from `SimulationService` after `world.InitializeItems()`.

**Spawn strategy:**

```
Small animals: 10 total
  - Eligible tiles: Forest or ForestEdge, not within 3 cells of DefaultStartPosition (10,15)
  - Scan map, collect eligible tiles, shuffle, take first 10
  - Distribute types: 3 GlowMouse, 3 TwigBird, 2 FeatherBug, 2 SporeVole
    (assign type by index mod 4 weighted, or just cycle through list)

Large animals: 3 total
  - Eligible tiles: Forest ONLY, not within 5 cells of DefaultStartPosition
  - Take first 3 eligible shuffled tiles
  - Types: 1 ShadowStrider, 1 BoneWarden, 1 ThornBeast (or CrestHunter)
    (one per forest cluster if possible; use tile position to pick cluster)
```

Private helper `SpawnAnimal(AnimalType type, int x, int y)` reads `AnimalDefinitions.Get(type)`, stamps out a new `Animal` with `Health = MaxHealth`, adds to `_animals`.

### 5.3 `TickAnimals` — The Animal AI Loop

Called once per round before agent turns. Iterates `_animals` (copy the list first so deaths mid-tick don't cause enumeration issues).

**Per-animal tick pseudocode:**

```
if animal.State == Dead: skip (will be removed after loot drop)

agentsNearby = world.GetAgentsInRadius(animal.X, animal.Y, animal.DetectRadius)
               .Where(a => a is not dead)
               .ToList()

nearestAgent = agentsNearby.MinBy(Chebyshev distance) // or null

if animal.Size == Small:
    TickSmallAnimal(animal, nearestAgent)
else:
    TickLargeAnimal(animal, nearestAgent)
```

**`TickSmallAnimal(animal, nearestAgent)`:**

```
if nearestAgent != null && ChebyshevDist(animal, nearestAgent) <= animal.FleeThreshold:
    animal.State = Fleeing
    animal.RoundsInCurrentState++
    MoveAwayFrom(animal, nearestAgent.x, nearestAgent.y)
    world.LogAt(animal.X, animal.Y,
        $"A {animal.DisplayName} scurries away in fright.")
    // Notify all agents within 1 cell via memory
    foreach agent in world.GetAgentsInRadius(animal.X, animal.Y, 1):
        world.Memory.AddMemory(agent.name, 
            $"A {animal.DisplayName} fled from my presence.")
else:
    animal.State = Idle
    animal.RoundsInCurrentState++
    // 40% chance to wander one step to adjacent forest/forestedge tile
    if rng.NextDouble() < 0.40:
        WanderAnimal(animal)
```

**`TickLargeAnimal(animal, nearestAgent)`:**

```
// Flee if health is critical
if animal.Health < animal.MaxHealth * 0.25f:
    animal.State = Fleeing
    if nearestAgent != null:
        MoveAwayFrom(animal, nearestAgent.x, nearestAgent.y)
    return

if nearestAgent != null:
    int dist = ChebyshevDist(animal, nearestAgent)

    if dist <= animal.AttackRadius:   // same or adjacent cell — ATTACK
        animal.State = Hunting
        animal.TargetAgentName = nearestAgent.name
        ApplyAnimalAttack(animal, nearestAgent.name)

    else if dist <= animal.DetectRadius:  // in range — PURSUE
        animal.State = Hunting
        animal.TargetAgentName = nearestAgent.name
        MoveToward(animal, nearestAgent.x, nearestAgent.y)
        world.LogAt(animal.X, animal.Y,
            $"A {animal.DisplayName} stalks toward {nearestAgent.name}.")
        // Alert nearby agents
        foreach agent in world.GetAgentsInRadius(animal.X, animal.Y, 2):
            world.Memory.AddMemory(agent.name,
                $"A {animal.DisplayName} is hunting {nearestAgent.name} nearby — dangerous!")
            world.Mood.GetMood(agent.name).AdjustStress(+8f)
            world.Mood.GetMood(agent.name).AdjustMood(-5f)
else:
    animal.State = Idle
    animal.RoundsInCurrentState++
    if rng.NextDouble() < 0.50:
        WanderAnimal(animal)
```

**`ApplyAnimalAttack(animal, agentName)`:**

```
template = AnimalDefinitions.Get(animal.Type)
world.Survival.AddHunger(agentName, -template.AttackHungerDamage)
world.Survival.AddThirst(agentName, -template.AttackThirstDamage)
world.Mood.GetMood(agentName).AdjustMood(template.AttackMoodDelta)
world.Mood.GetMood(agentName).AdjustStress(template.AttackStressDelta)

world.LogAt(animal.X, animal.Y,
    $"A {animal.DisplayName} attacks {agentName}! " +
    $"(-{template.AttackHungerDamage:F0} hunger, -{template.AttackThirstDamage:F0} thirst)")
world.LogDev($"[animal] {animal.DisplayName} attacked {agentName} → " +
    $"hunger -{template.AttackHungerDamage}  thirst -{template.AttackThirstDamage}  " +
    $"mood {template.AttackMoodDelta}  stress +{template.AttackStressDelta}")
world.Memory.AddMemory(agentName,
    $"A {animal.DisplayName} attacked me! Lost hunger and thirst from the injury.")

// Witnesses within 2 cells react
foreach other in world.GetAgentsInRadius(animal.X, animal.Y, 2).Where(a => a.name != agentName):
    world.Memory.AddMemory(other.name,
        $"Witnessed a {animal.DisplayName} attack {agentName}. The forest is dangerous.")
    world.Mood.GetMood(other.name).AdjustStress(+12f)
    world.Mood.GetMood(other.name).AdjustMood(-8f)
```

**Movement helpers:**

`MoveToward(animal, tx, ty)` — compute dx = sign(tx - animal.X), dy = sign(ty - animal.Y); pick whichever axis has larger distance; try primary move first, fall back to diagonal or secondary. Only move to Forest or ForestEdge tiles (large animals do not leave the forest). If no valid move, stay.

`MoveAwayFrom(animal, fx, fy)` — invert the direction from MoveToward. Small animals can move to any non-blocked tile (they are lighter and more mobile). Large animals stay in forest.

`WanderAnimal(animal)` — pick a random adjacent tile (4-cardinal) that is Forest or ForestEdge and in-bounds; move there with 50% chance.

**Dead animal cleanup:** At the end of `TickAnimals`, iterate `_animals` where `State == Dead` and call `DropAnimalLoot(animal)`, then remove from list.

```csharp
private void DropAnimalLoot(Animal animal)
{
    var template = AnimalDefinitions.Get(animal.Type);
    if (rng.NextDouble() <= template.LootChance)
    {
        foreach (var itemId in template.LootTable)
            _world.Items.PlaceItemAt(itemId, animal.X, animal.Y);
        // Note: requires adding PlaceItemAt(string defId, int x, int y) to IItemSystem
    }
    _world.LogAt(animal.X, animal.Y,
        $"The {AnimalDefinitions.Get(animal.Type).DisplayName} lies dead here.");
}
```

> **Note:** `IItemSystem` needs one new method: `void PlaceItemAt(string definitionId, int x, int y)`. This matches the private `PlaceItem` already in `ItemSystem.cs` — just make it public and add it to the interface. This is the only addition to an existing interface.

### 5.4 Agent Interaction Methods

**`TryAttackAnimal(agentName, animalIdStr)`:**

```
pos = world.GetAgentPosition(agentName)
animal = _animals.FirstOrDefault(a => a.Id.ToString() == animalIdStr 
                                   && a.X == pos.x && a.Y == pos.y
                                   && a.State != Dead)
if animal == null: return null

// Agent deals damage: 1d20+5 for large, 1d10+10 for small (instant kill possible)
float damage = animal.Size == Large
    ? (float)(rng.Next(1, 21) + 5)   // 6–25 damage
    : (float)(rng.Next(1, 11) + 10)  // 11–20 damage (small animals die quickly)

animal.Health -= damage
world.LogAt(pos.x, pos.y, $"{agentName} attacks the {template.DisplayName} for {damage:F0} damage!")
world.Memory.AddMemory(agentName, $"Attacked a {template.DisplayName} at ({pos.x},{pos.y}).")

if animal.Health <= 0:
    animal.State = Dead
    world.Memory.AddMemory(agentName, $"Killed a {template.DisplayName}! Loot should drop nearby.")
    world.Mood.GetMood(agentName).AdjustMood(animal.Size == Large ? +20f : +8f)
    world.Mood.GetMood(agentName).AdjustStress(animal.Size == Large ? -10f : -3f)
    return $"kills the {template.DisplayName}"
else:
    // Large animal counter-attacks immediately if not fleeing
    if animal.Size == Large && animal.State != Fleeing:
        ApplyAnimalAttack(animal, agentName)
        return $"injures the {template.DisplayName} (it counter-attacks!)"
    return $"strikes the {template.DisplayName} ({animal.Health:F0} HP remaining)"
```

**`TryTrapAnimal(agentName, animalIdStr)`:**

```
// Requires "wire_bundle" in inventory
inv = world.Items.GetInventory(agentName)
trapItem = inv.FirstOrDefault(i => i.DefinitionId == "wire_bundle")
if trapItem == null: return "— no Wire Bundle in inventory to set a trap"

pos = world.GetAgentPosition(agentName)
animal = _animals.FirstOrDefault(a => a.Id.ToString() == animalIdStr
                                   && a.Size == Small
                                   && a.X == pos.x && a.Y == pos.y
                                   && a.State != Dead)
if animal == null: return null

// Consume wire bundle regardless of outcome
world.Items.TryDrop(agentName, trapItem.InstanceId.ToString())
// (drop wire bundle as "consumed" — or alternatively call a new ConsumeItem method)

if rng.NextDouble() < 0.60:
    animal.State = Dead  // trapped = dead for loot purposes
    // loot drops at end of tick
    world.Memory.AddMemory(agentName, $"Trapped a {template.DisplayName} successfully.")
    world.Mood.GetMood(agentName).AdjustMood(+10f)
    world.Mood.GetMood(agentName).AdjustStress(-5f)
    return $"traps the {template.DisplayName}! (wire bundle consumed)"
else:
    world.Memory.AddMemory(agentName, $"Attempted to trap a {template.DisplayName} — it escaped.")
    world.Mood.GetMood(agentName).AdjustMood(-3f)
    return $"attempts to trap the {template.DisplayName} — it escapes! (wire bundle lost)"
```

> **Alternative to dropping the wire bundle:** Add `void ConsumeItem(string agentName, string instanceIdStr)` to `IItemSystem`. This avoids the item appearing on the ground briefly. Simpler: just remove directly from inventory list if the system has access. Given `ItemSystem` holds `_agentInventories` internally, the cleanest path is to add `bool TryConsume(string agentName, string instanceIdStr)` to the interface.

**`TryScareAnimal(agentName, animalIdStr)`:**

```
pos = world.GetAgentPosition(agentName)
animal = _animals.FirstOrDefault(a => a.Id.ToString() == animalIdStr
                                   && a.Size == Large
                                   && ChebyshevDist(a, pos) <= 2
                                   && a.State != Dead)
if animal == null: return null

template = AnimalDefinitions.Get(animal.Type)
if rng.NextDouble() < template.ScareChance:
    // Success — animal flees
    animal.State = Fleeing
    MoveAwayFrom(animal, pos.x, pos.y)
    MoveAwayFrom(animal, pos.x, pos.y)  // flee 2 steps
    world.Memory.AddMemory(agentName, $"Scared off a {template.DisplayName}!")
    world.Mood.GetMood(agentName).AdjustMood(+12f)
    world.Mood.GetMood(agentName).AdjustStress(-8f)
    return $"scares off the {template.DisplayName} — it retreats!"
else:
    // Failure — animal aggros harder
    animal.State = Hunting
    animal.TargetAgentName = agentName
    ApplyAnimalAttack(animal, agentName)  // immediate attack from provocation
    world.Memory.AddMemory(agentName, $"Tried to scare a {template.DisplayName} — it attacked me instead!")
    return $"fails to scare the {template.DisplayName} — it attacks!"
```

### 5.5 Query Methods

```csharp
public IReadOnlyList<Animal> AllAnimals =>
    _animals.Where(a => a.State != AnimalState.Dead).ToList();

public IReadOnlyList<Animal> GetAnimalsAt(int x, int y) =>
    _animals.Where(a => a.X == x && a.Y == y && a.State != AnimalState.Dead).ToList();

public IReadOnlyList<Animal> GetAnimalsInRadius(int cx, int cy, int radius) =>
    _animals.Where(a =>
        a.State != AnimalState.Dead &&
        Math.Abs(a.X - cx) <= radius &&
        Math.Abs(a.Y - cy) <= radius)
    .ToList();
```

---

## 6. `AgentAction` Additions — `Models/AgentAction.cs`

Add two fields at the bottom of the existing class:

```csharp
// "none" | "attack" | "trap" | "scare"
[JsonPropertyName("animal_action")]
public string AnimalAction { get; set; } = "none";

// Guid string of the target animal (from ANIMALS NEARBY list in context)
[JsonPropertyName("animal_target_id")]
public string AnimalTargetId { get; set; } = "";
```

No other changes to `AgentAction`. The existing `item_action` pattern is the exact model.

---

## 7. `WorldState` Changes — `Services/WorldState.cs`

### 7.1 Constructor signature

Add `IAnimalSystem animals` parameter in both the full constructor and the convenience constructor. Follow the exact existing pattern for every other system:

```csharp
// Full constructor — add parameter
public WorldState(string situation, MapGrid map,
    ISurvivalSystem      survival,
    IMoodSystem          mood,
    IMemorySystem        memory,
    IItemSystem          items,
    IForagingSystem      foraging,
    ICommunicationSystem communication,
    IAnimalSystem        animals)           // ← ADD
{
    // ... existing assignments ...
    Animals = animals;
    // ... existing Attach calls ...
    Animals.Attach(this);                   // ← ADD
}

// Convenience constructor — add AnimalSystem() to the chain
public WorldState(string situation, MapGrid map)
    : this(situation, map,
           new SurvivalSystem(),
           new MoodSystem(),
           new MemorySystem(),
           new ItemSystem(),
           new ForagingSystem(),
           new CommunicationSystem(),
           new AnimalSystem())              // ← ADD
{ }
```

### 7.2 Property

```csharp
public IAnimalSystem Animals { get; }
```

### 7.3 Forwarding helpers (convenience, parallel to existing ones)

```csharp
public IReadOnlyList<Animal> GetAnimalsAt(int x, int y)                => Animals.GetAnimalsAt(x, y);
public IReadOnlyList<Animal> GetAnimalsInRadius(int cx, int cy, int r)  => Animals.GetAnimalsInRadius(cx, cy, r);
public string? TryAttackAnimal(string agent, string id)                 => Animals.TryAttackAnimal(agent, id);
public string? TryTrapAnimal(string agent, string id)                   => Animals.TryTrapAnimal(agent, id);
public string? TryScareAnimal(string agent, string id)                  => Animals.TryScareAnimal(agent, id);
```

### 7.4 `GetContext()` additions

Insert two new blocks into the existing `GetContext()` method.

**Block A — ANIMALS IN YOUR CELL** (insert after the ITEMS HERE block, before ITEM ACTIONS):

```csharp
sb.AppendLine();
var cellAnimals = Animals.GetAnimalsAt(cx, cy)
    .Where(a => a.State != AnimalState.Dead)
    .ToList();
sb.AppendLine("ANIMALS IN YOUR CELL:");
if (cellAnimals.Count > 0)
{
    foreach (var a in cellAnimals)
    {
        var tpl = AnimalDefinitions.Get(a.Type);
        string sizeTag = a.Size == AnimalSize.Large ? "[LARGE — DANGEROUS]" : "[small]";
        sb.AppendLine($"  {sizeTag} {tpl.DisplayName} [id:{a.Id}] — HP:{a.Health:F0}/{a.MaxHealth:F0} — {tpl.Description}");
    }
}
else
{
    sb.AppendLine("  (none)");
}

sb.AppendLine();
var nearbyAnimals = Animals.GetAnimalsInRadius(cx, cy, 3)
    .Where(a => !(a.X == cx && a.Y == cy) && a.State != AnimalState.Dead)
    .OrderBy(a => Math.Max(Math.Abs(a.X - cx), Math.Abs(a.Y - cy)))
    .ToList();
sb.AppendLine("ANIMALS NEARBY (within 3 cells):");
if (nearbyAnimals.Count > 0)
{
    foreach (var a in nearbyAnimals)
    {
        var tpl = AnimalDefinitions.Get(a.Type);
        int dist = Math.Max(Math.Abs(a.X - cx), Math.Abs(a.Y - cy));
        string sizeTag = a.Size == AnimalSize.Large ? "[LARGE]" : "[small]";
        string stateStr = a.State switch
        {
            AnimalState.Hunting => " — HUNTING",
            AnimalState.Fleeing => " — fleeing",
            _ => ""
        };
        sb.AppendLine($"  {sizeTag} {tpl.DisplayName} at ({a.X},{a.Y}) [{dist} cell{(dist == 1 ? "" : "s")} away]{stateStr}");
    }
}
else
{
    sb.AppendLine("  (none nearby)");
}
```

**Block B — ANIMAL ACTIONS** (insert after ITEM ACTIONS block):

```csharp
sb.AppendLine();
sb.AppendLine("ANIMAL ACTIONS: attack, trap, scare, none");
sb.AppendLine("  Set \"animal_action\" to one of the above, \"animal_target_id\" to the animal's [id:...] string.");
sb.AppendLine("  attack — strike an animal IN YOUR CELL. Risky against large ones (they counter-attack).");
sb.AppendLine("  trap   — catch a SMALL animal in your cell using a Wire Bundle from your inventory.");
sb.AppendLine("  scare  — attempt to frighten a LARGE animal within 2 cells away. May backfire.");
sb.AppendLine("  none   — take no animal action (default).");
sb.AppendLine("  You may only do one animal action per turn, combined with a normal item action if desired.");
```

---

## 8. `SimulationService` Changes — `Services/SimulationService.cs`

### 8.1 `InitializeAnimals` call

After `world.InitializeItems()` on line 86, add:

```csharp
world.Animals.InitializeAnimals();   // ← ADD (line 87)
```

### 8.2 `TickAnimals` call — **ordering justification**

Animals tick **at the very start of each round loop, before any agent takes their turn**:

```csharp
for (int round = 1; round <= cfg.Rounds; round++)
{
    if (ct.IsCancellationRequested) break;

    Events.Add(new SimEvent { Type = "round", Content = round.ToString() });

    // ── Animal tick (BEFORE agents) ──────────────────────────────────────
    world.Animals.TickAnimals();                            // ← ADD
    foreach (var animalEvent in world.DrainAnimalEvents()) // ← ADD (see §8.3)
        Events.Add(animalEvent);
    // ────────────────────────────────────────────────────────────────────

    await NotifyAsync();
    // ... rest of existing round loop unchanged
```

**Ordering rationale:** Ticking animals first means:
1. When each agent calls `GetContext()`, it sees the freshest animal positions and states from this round's movement.
2. Damage from large animal attacks has already been applied before the agent decides their action — they react to injuries that just happened, creating authentic urgency.
3. It avoids the "simultaneous move problem": if agents moved first, an animal could teleport into a cell the agent just vacated, then damage them retroactively. Pre-tick animals occupy cells before agents move.
4. The `SimulationService` loop is already sequential per agent, so any damage applied in the animal tick persists cleanly through each agent's survival meter check at end of their turn.

**Edge case:** An agent could die from animal attack damage even before they take their turn. The existing `world.TickMeters(runner.Name)` at the end of each agent's turn catches this — if hunger/thirst hit 0 from the animal damage, the agent dies normally through the established path. No new death logic is needed.

### 8.3 Animal events drain

Add a small helper in `WorldState` (or pipe directly through `DrainDevLog`). The simplest approach: animal attack events are already written to `world.LogAt()` (which appears in `GetContext()` under RECENT EVENTS) and to `world.LogDev()` (which `SimulationService` drains into `dev` SimEvents). No new drain method is strictly needed — reuse the dev log pipe. This means animal attack messages appear in the dev/log feed automatically.

To surface animal attacks as **first-class events** in the UI feed (same as agent actions), add a small list to `WorldState`:

```csharp
// In WorldState:
private readonly List<SimEvent> _animalEvents = new();
public void LogAnimalEvent(SimEvent e) => _animalEvents.Add(e);
public IReadOnlyList<SimEvent> DrainAnimalEvents()
{
    var snap = _animalEvents.ToList();
    _animalEvents.Clear();
    return snap;
}
```

`AnimalSystem.ApplyAnimalAttack()` calls `_world.LogAnimalEvent(new SimEvent { Type = "animal_attack", Label = "attacks", AgentName = animal.DisplayName, Content = "..." })`. `SimulationService` drains these and adds them to `Events` with `Type = "animal_attack"` so they render distinctively in the UI.

### 8.4 `DispatchAnimalAction` helper

Parallel to the existing `DispatchItemAction`:

```csharp
private static string? DispatchAnimalAction(WorldState world, string agentName, AgentAction action)
{
    var id = action.AnimalTargetId?.Trim() ?? "";
    return action.AnimalAction switch
    {
        "attack" => world.TryAttackAnimal(agentName, id),
        "trap"   => world.TryTrapAnimal(agentName, id),
        "scare"  => world.TryScareAnimal(agentName, id),
        _        => null
    };
}
```

Call this in the agent turn loop immediately after `DispatchItemAction`:

```csharp
var animalEventContent = DispatchAnimalAction(world, runner.Name, action);
if (animalEventContent is not null)
    Events.Add(new SimEvent { Type = "animal", AgentName = runner.Name,
        AgentColor = color, Label = action.AnimalAction, Content = animalEventContent });
```

---

## 9. `AgentRunner` System Prompt — `Services/AgentRunner.cs`

In `SystemTemplate`, extend the existing content (after the DIRECT COMMUNICATION block, before the JSON format block):

```
ANIMALS: The forest contains wildlife — some harmless, some deadly.
  Small animals (Glow-Mouse, Twig-Bird, etc.) flee on sight. They can be caught for food.
  LARGE animals (Shadow-Strider, Bone-Warden, etc.) will hunt and attack you, reducing your
  hunger and thirst. An attacked agent is weakened — act fast or die.

ANIMAL ACTIONS (one per turn, combined with item actions if desired):
  attack  — strike an animal IN YOUR CELL. Smaller animals die quickly. Large ones counter.
  trap    — catch a small animal in your cell using a Wire Bundle (consumed regardless of success).
  scare   — try to frighten a large animal within 2 cells. Risky: failure provokes an attack.
  none    — ignore animals (default).
  Set "animal_action" and "animal_target_id" (copy the exact [id:...] from context).

  SURVIVAL TIP: Large predators reduce your hunger and thirst when they attack. If critically
  low on supplies and a predator is nearby, consider fighting or fleeing — not ignoring it.
```

Also add two fields to the JSON format example block:

```json
  "animal_action": "none, attack, trap, or scare",
  "animal_target_id": "the exact id string from ANIMALS IN YOUR CELL or ANIMALS NEARBY, or empty"
```

---

## 10. `Program.cs` Registration

```csharp
// After existing registrations:
builder.Services.AddSingleton<IAnimalSystem, AnimalSystem>();
```

**Wait — scoped vs singleton?**

`SimulationService` is scoped (one per Blazor SignalR circuit). `WorldState` is created inside `RunAsync` and lives only for that simulation run. `AnimalSystem` is instantiated by `WorldState`'s convenience constructor today (like all other systems), so it does not go through DI injection. 

The registration in `Program.cs` is only needed if you want to inject `IAnimalSystem` into `WorldState` via constructor injection rather than the convenience `new AnimalSystem()` constructor. Currently no other system is registered in DI — they all use `new SurvivalSystem()` etc. in the convenience constructor. **Follow the same pattern:** use `new AnimalSystem()` in `WorldState`'s convenience constructor, and don't register it in `Program.cs` for now. This keeps the system self-contained and swappable without DI plumbing. If full DI injection of animals is desired later, add the registration at that point.

---

## 11. `MapView.razor` — Visual Overlay (Optional Layer 3.5)

Between the item marker layer and the agent avatar layer, insert an animal overlay. Animals are rendered as small glyphs:

```razor
@* Layer 3.5: Animal markers *@
@if (World.Animals is not null)
{
    @foreach (var animal in World.Animals.AllAnimals)
    {
        double ax2 = animal.X * CellSize + CellSize * 0.75;
        double ay2 = animal.Y * CellSize + CellSize * 0.25;
        string glyph = animal.Size == AnimalSize.Large ? "✦" : "·";
        string color = animal.Size == AnimalSize.Large ? "#ef4444" : "#86efac";
        <text x="@ax2.ToString("F1")" y="@ay2.ToString("F1")"
              font-size="@(animal.Size == AnimalSize.Large ? 9 : 7)"
              fill="@color"
              font-family="monospace"
              text-anchor="middle"
              style="user-select:none">@glyph</text>
    }
}
```

This requires `WorldState` to expose `Animals` publicly (already done via the property in §7). The `MapView` receives `World` as a parameter, so no additional parameter is needed.

Add to legend:

```razor
<span class="legend-item">
    <svg width="10" height="10" viewBox="0 0 10 10">
        <text x="5" y="8" text-anchor="middle" font-size="8" fill="#86efac" font-family="monospace">·</text>
    </svg>
    Small animal
</span>
<span class="legend-item">
    <svg width="10" height="10" viewBox="0 0 10 10">
        <text x="5" y="8" text-anchor="middle" font-size="9" fill="#ef4444" font-family="monospace">✦</text>
    </svg>
    Large predator
</span>
```

---

## 12. `IItemSystem` Addition (Minor)

Add one method to the interface and implement it in `ItemSystem`:

```csharp
// IItemSystem.cs — add:
void PlaceItemAt(string definitionId, int x, int y);

// ItemSystem.cs — implement (just exposes the existing private PlaceItem):
public void PlaceItemAt(string definitionId, int x, int y) => PlaceItem(definitionId, x, y);
```

This is required by `AnimalSystem.DropAnimalLoot`. It's the only modification to an existing interface beyond `WorldState`.

---

## 13. Summary: Files to Create / Modify

| File | Action | What changes |
|------|--------|--------------|
| `Models/Animal.cs` | **Create** | `AnimalSize`, `AnimalState`, `AnimalType` enums + `Animal` class |
| `Models/AnimalDefinitions.cs` | **Create** | `AnimalTemplate` record + `AnimalDefinitions` static registry (8 animals) |
| `Models/ItemRegistry.cs` | **Modify** | 6 new entries: `small_carcass`, `large_carcass`, `feather_tuft`, `chitin_shard`, `tough_hide`, `bone_shard` |
| `Models/AgentAction.cs` | **Modify** | 2 new fields: `AnimalAction`, `AnimalTargetId` |
| `Services/Systems/IAnimalSystem.cs` | **Create** | Full interface with 7 methods |
| `Services/Systems/AnimalSystem.cs` | **Create** | Concrete implementation (~280 lines) |
| `Services/Systems/IItemSystem.cs` | **Modify** | Add `PlaceItemAt(string, int, int)` |
| `Services/Systems/ItemSystem.cs` | **Modify** | Implement `PlaceItemAt` (1 line) |
| `Services/WorldState.cs` | **Modify** | `Animals` property, constructor wiring, forwarding helpers, 2 new `GetContext()` blocks, `LogAnimalEvent`/`DrainAnimalEvents` |
| `Services/SimulationService.cs` | **Modify** | `InitializeAnimals()` call, `TickAnimals()` at round start, `DispatchAnimalAction()` helper + dispatch call, drain animal events |
| `Services/AgentRunner.cs` | **Modify** | Extend `SystemTemplate` with ANIMALS block + 2 JSON fields |
| `Components/MapView.razor` | **Modify (optional)** | Layer 3.5 animal glyphs + legend entries |

---

## 14. Recommended Implementation Order

Implement in this sequence to allow incremental compilation and testing at each step:

**Step 1 — Models (no dependencies)**
- `Models/Animal.cs`
- `Models/AnimalDefinitions.cs`
- `Models/ItemRegistry.cs` (add loot items)

**Step 2 — Minimal interface + stub**
- `Services/Systems/IAnimalSystem.cs`
- `Services/Systems/AnimalSystem.cs` — implement interface with no-op bodies first so the project compiles
- `Services/Systems/IItemSystem.cs` — add `PlaceItemAt`
- `Services/Systems/ItemSystem.cs` — implement `PlaceItemAt`

**Step 3 — Wire WorldState**
- `Services/WorldState.cs` — all changes (property, constructor, helpers)
- At this point: project compiles, animals exist but do nothing

**Step 4 — Wire SimulationService**
- `Models/AgentAction.cs` — add two fields
- `Services/SimulationService.cs` — `InitializeAnimals()`, `TickAnimals()`, `DispatchAnimalAction()`
- At this point: animals spawn and tick each round (movement/attacks active)

**Step 5 — Agent awareness**
- `Services/WorldState.cs` `GetContext()` additions
- `Services/AgentRunner.cs` system prompt extension
- At this point: agents can see and react to animals in their prompts

**Step 6 — Flesh out AnimalSystem**
- Fill in all methods in `AnimalSystem.cs` with real logic

**Step 7 — Visual polish (optional)**
- `Components/MapView.razor` overlay + legend

---

## 15. Design Decisions and Tradeoffs

**No new "health" stat for agents.** Animal attacks deduct hunger and thirst instead. This piggybacks on the existing `SurvivalSystem.AddHunger/AddThirst` (already have the `AddHunger(delta)` path for negative deltas) and means agent death from animal attack flows through the exact same end-of-turn `TickMeters` → `KillAgent` path. Zero new death code.

**No LLM calls for animals.** Animals run on the deterministic rule engine in `AnimalSystem.TickAnimals()`. This keeps the simulation fast regardless of round count and avoids unpredictable animal behavior that could confuse agents.

**Animals are persistent.** Once spawned, animals stay alive until killed (or flee off map — which won't happen since movement is constrained to forest tiles). No per-round respawn. This creates meaningful stakes: agents who kill the `ShadowStrider` have actually made the forest permanently safer.

**Trapping consumes Wire Bundle regardless of success.** Wire bundles are already in the item system and plentiful enough that this is a real cost but not punishing. Agents will learn the tradeoff.

**Animal positions are visible 3 cells out** in `GetContext()`. This is intentional — the LLM needs legible context to make good decisions. Hiding animal positions entirely would cause agents to wander into predators blindly every time, making the game frustrating rather than scary.

**`scare` action scales by `ScareChance` on the template.** Large animals tuned with `ScareChance: 0.15–0.30f` means scare is useful in a pinch but unreliable enough to not trivialize predators. The counter-attack on failure adds real risk to the decision.
