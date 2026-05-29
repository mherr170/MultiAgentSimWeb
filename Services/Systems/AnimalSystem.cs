using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public class AnimalSystem : IAnimalSystem
{
    private readonly List<Animal>   _animals = new();
    private readonly List<SimEvent> _events  = new();
    private readonly Random _rng = new();
    private WorldState _world = null!;

    public void Attach(WorldState world) => _world = world;

    public IReadOnlyList<SimEvent> DrainEvents()
    {
        var snap = _events.ToList();
        _events.Clear();
        return snap;
    }

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

    // ── Initialization ───────────────────────────────────────────────────────

    public void InitializeAnimals()
    {
        // Distance from the nearest agent start position (Chebyshev), not just the default one.
        // This prevents large predators from spawning adjacent to any agent at sim start.
        int DistToNearestStart(int px, int py) =>
            MapGrid.AgentStartPositions.Min(s =>
                Math.Max(Math.Abs(px - s.x), Math.Abs(py - s.y)));

        var smallEligible = new List<(int x, int y)>();
        var largeEligible = new List<(int x, int y)>();

        for (int y = 0; y < _world.MapHeight; y++)
        {
            for (int x = 0; x < _world.MapWidth; x++)
            {
                var terrain = _world.GetCell(x, y).Terrain;
                int dist = DistToNearestStart(x, y);

                // Small animals on streets, park, and forest — keep at least 2 cells away
                if ((terrain == TerrainType.Street || terrain == TerrainType.Park || terrain == TerrainType.Forest) && dist >= 2)
                    smallEligible.Add((x, y));

                // Feral dogs patrol streets — must be at least 8 cells from the nearest spawn
                if (terrain == TerrainType.Street && dist >= 8)
                    largeEligible.Add((x, y));
                // Forest deer are fine near the forest start (Greenwood is intentionally remote)
                if (terrain == TerrainType.Forest && dist >= 4)
                    largeEligible.Add((x, y));
            }
        }

        Shuffle(smallEligible);
        Shuffle(largeEligible);

        // Small animals: city types for streets/parks, Fox for forest
        // citySmallIdx tracks rotation through city pool independently of forest spawns
        AnimalType[] citySmall = [AnimalType.Rat, AnimalType.Pigeon, AnimalType.Squirrel, AnimalType.StreetCat];
        int smallSpawned = 0;
        int citySmallIdx = 0;
        foreach (var (x, y) in smallEligible)
        {
            if (smallSpawned >= 12) break;
            var terrain = _world.GetCell(x, y).Terrain;
            if (terrain == TerrainType.Forest)
                SpawnAnimal(AnimalType.Fox, x, y);
            else
            {
                SpawnAnimal(citySmall[citySmallIdx % citySmall.Length], x, y);
                citySmallIdx++;
            }
            smallSpawned++;
        }

        // Large animals: dogs for streets, Deer for forest
        // Cap at 4 (was 6) — fewer apex predators makes early survival viable
        AnimalType[] streetLarge = [AnimalType.DogPack, AnimalType.Rottweiler, AnimalType.PitBull, AnimalType.Coyote];
        int largeSpawned = 0;
        int streetLargeIdx = 0;
        foreach (var (x, y) in largeEligible)
        {
            if (largeSpawned >= 4) break;
            var terrain = _world.GetCell(x, y).Terrain;
            if (terrain == TerrainType.Forest)
                SpawnAnimal(AnimalType.Deer, x, y);
            else
            {
                SpawnAnimal(streetLarge[streetLargeIdx % streetLarge.Length], x, y);
                streetLargeIdx++;
            }
            largeSpawned++;
        }
    }

    private void SpawnAnimal(AnimalType type, int x, int y)
    {
        var tpl = AnimalDefinitions.Get(type);
        var animal = new Animal
        {
            Type                = type,
            Size                = tpl.Size,
            X                   = x,
            Y                   = y,
            Health              = tpl.MaxHealth,
            MaxHealth           = tpl.MaxHealth,
            DetectRadius        = tpl.DetectRadius,
            AttackRadius        = tpl.AttackRadius,
            FleeThreshold       = tpl.FleeThreshold,
            AttackHungerDamage  = tpl.AttackHungerDamage,
            AttackThirstDamage  = tpl.AttackThirstDamage,
            AttackHealthDamage  = tpl.AttackHealthDamage,
            AttackMoodDelta     = tpl.AttackMoodDelta,
            AttackStressDelta   = tpl.AttackStressDelta,
            ScareChance         = tpl.ScareChance,
            LootTable           = tpl.LootTable,
            LootChance          = tpl.LootChance,
            DisplayName         = tpl.DisplayName,
            Description         = tpl.Description,
        };
        _animals.Add(animal);
    }

    // ── Tick ─────────────────────────────────────────────────────────────────

    public void TickAnimals()
    {
        var snapshot = _animals.ToList();

        foreach (var animal in snapshot)
        {
            if (animal.State == AnimalState.Dead) continue;

            if (animal.Size == AnimalSize.Small)
                TickSmallAnimal(animal);
            else
                TickLargeAnimal(animal);
        }

        // Drop loot and remove dead animals
        foreach (var dead in _animals.Where(a => a.State == AnimalState.Dead).ToList())
        {
            DropAnimalLoot(dead);
            _animals.Remove(dead);
        }
    }

    private void TickSmallAnimal(Animal animal)
    {
        animal.State = AnimalState.Idle;
        animal.RoundsInCurrentState++;

        // Companion follow behaviour — move toward feeder while bond lasts
        if (animal.CompanionRoundsLeft > 0)
        {
            animal.CompanionRoundsLeft--;
            if (!string.IsNullOrEmpty(animal.CompanionOf))
            {
                var cpos = _world.GetAgentPosition(animal.CompanionOf);
                if (cpos.x >= 0)
                {
                    int dist = Math.Max(Math.Abs(animal.X - cpos.x), Math.Abs(animal.Y - cpos.y));
                    if (dist > 1) MoveToward(animal, cpos.x, cpos.y);
                }
            }
            if (animal.CompanionRoundsLeft == 0) animal.CompanionOf = null;
        }
        else if (_rng.NextDouble() < 0.40)
        {
            WanderAnimal(animal);
        }

        // Check if the animal's current cell has an armed trap
        var trap = _world.Items.TakeTopTrapAt(animal.X, animal.Y);
        if (trap == null) return;

        var tpl = AnimalDefinitions.Get(animal.Type);
        if (_rng.NextDouble() < 0.85)
        {
            animal.State = AnimalState.Dead;
            _world.LogAt(animal.X, animal.Y, $"An armed trap catches a {tpl.DisplayName}.");
            _world.LogDev($"[trap] caught {tpl.DisplayName} at ({animal.X},{animal.Y})");
            _events.Add(new SimEvent
            {
                Type    = "animal",
                Label   = "trap",
                Content = $"Armed trap at ({animal.X},{animal.Y}) catches a {tpl.DisplayName}!"
            });
        }
        else
        {
            _world.LogAt(animal.X, animal.Y,
                $"A {tpl.DisplayName} triggers the trap — but wriggles free!");
            _world.LogDev($"[trap] {tpl.DisplayName} triggered but escaped at ({animal.X},{animal.Y})");
        }
    }

    private void TickLargeAnimal(Animal animal)
    {
        // Heavy rain / thunderstorm: large animals hunker down — they don't hunt or pursue prey.
        if (_world.Weather.SuppressesLargeAnimals)
        {
            animal.State = AnimalState.Idle;
            return;
        }

        // FleeThreshold is an absolute HP value: 0 = never flee (dogs), 100+ = always flee (deer)
        if (animal.Health < animal.FleeThreshold)
        {
            animal.State = AnimalState.Fleeing;
            WanderAnimal(animal);
            return;
        }

        var tpl = AnimalDefinitions.Get(animal.Type);

        // Night: predators gain +2 detect radius (sharper senses in the dark)
        // Light sources make the bearer detectable at +1 extra distance
        int nightBonus = _world.IsNight ? 2 : 0;

        // Gather all prey within detect radius: small animals and agents alike
        int Dist(int ax, int ay) => Math.Max(Math.Abs(ax - animal.X), Math.Abs(ay - animal.Y));

        var nearestSmallAnimal = _animals
            .Where(a => a != animal && a.Size == AnimalSize.Small && a.State != AnimalState.Dead &&
                        Dist(a.X, a.Y) <= animal.DetectRadius + nightBonus)
            .OrderBy(a => Dist(a.X, a.Y))
            .FirstOrDefault();

        // Per-agent detect radius: light source carriers stand out an extra cell at night
        int AgentDetectRadius(string name)
        {
            int lightBonus = (_world.IsNight && _world.DayNight.HasLightSource(name)) ? 1 : 0;
            return animal.DetectRadius + nightBonus + lightBonus;
        }

        var nearestAgent = _world.GetAgentsInRadius(animal.X, animal.Y, animal.DetectRadius + nightBonus + 1)
            .Where(a => Dist(a.x, a.y) <= AgentDetectRadius(a.name))
            .Where(a => _world.GetAgentPosition(a.name).x >= 0)
            .OrderBy(a => Dist(a.x, a.y))
            .Cast<(string name, int x, int y)?>()
            .FirstOrDefault();

        // Pick whichever prey is closer; agents are just another prey type
        bool pursueAgent = false;
        int preyX = 0, preyY = 0;

        if (nearestSmallAnimal != null && nearestAgent.HasValue)
        {
            pursueAgent = Dist(nearestAgent.Value.x, nearestAgent.Value.y) <
                          Dist(nearestSmallAnimal.X, nearestSmallAnimal.Y);
        }
        else if (nearestAgent.HasValue)
        {
            pursueAgent = true;
        }

        bool hasPrey = nearestSmallAnimal != null || nearestAgent.HasValue;

        if (hasPrey)
        {
            if (pursueAgent)
            {
                var agent = nearestAgent!.Value;
                preyX = agent.x; preyY = agent.y;
                int dist = Dist(preyX, preyY);

                animal.State = AnimalState.Hunting;

                if (dist <= animal.AttackRadius)
                {
                    ApplyAnimalAttack(animal, agent.name);
                }
                else
                {
                    MoveToward(animal, preyX, preyY);
                    var moveTerrain = _world.GetCell(animal.X, animal.Y).Terrain;
                    string moveDesc = moveTerrain == TerrainType.Forest
                        ? "moves through the forest"
                        : "moves through the streets";
                    _world.LogAt(animal.X, animal.Y, $"A {tpl.DisplayName} {moveDesc}.");
                }
            }
            else
            {
                var prey = nearestSmallAnimal!;
                preyX = prey.X; preyY = prey.Y;
                int dist = Dist(preyX, preyY);

                animal.State = AnimalState.Hunting;

                if (dist <= animal.AttackRadius)
                {
                    prey.State = AnimalState.Dead;
                    _world.LogAt(animal.X, animal.Y,
                        $"A {tpl.DisplayName} catches and kills a {AnimalDefinitions.Get(prey.Type).DisplayName}.");
                    _world.LogDev($"[animal] {tpl.DisplayName} killed {AnimalDefinitions.Get(prey.Type).DisplayName} at ({animal.X},{animal.Y})");
                }
                else
                {
                    MoveToward(animal, preyX, preyY);
                }
            }
        }
        else
        {
            animal.State = AnimalState.Idle;
            animal.RoundsInCurrentState++;
            // At night, large predators roam more aggressively instead of sitting idle
            float wanderChance = _world.IsNight ? 0.85f : 0.50f;
            if (_rng.NextDouble() < wanderChance)
                WanderAnimal(animal);
        }
    }

    private void ApplyAnimalAttack(Animal animal, string agentName)
    {
        var tpl = AnimalDefinitions.Get(animal.Type);
        _world.Survival.AddHunger(agentName, -tpl.AttackHungerDamage);
        _world.Survival.AddThirst(agentName, -tpl.AttackThirstDamage);
        if (tpl.AttackHealthDamage > 0)
            _world.Survival.AddHealth(agentName, -tpl.AttackHealthDamage);
        var victim = _world.GetPersonality(agentName);
        // risk_taker and combat_veteran take hits in stride — halved stress from attacks
        float stressMult = (victim.HasFlag("risk_taker") || victim.IsCombatVeteran) ? 0.5f : 1.0f;
        _world.Mood.GetMood(agentName).AdjustMood(tpl.AttackMoodDelta);
        _world.Mood.GetMood(agentName).AdjustStress(tpl.AttackStressDelta * stressMult);
        if (stressMult < 1f)
            _world.LogDev($"[{agentName}] {(victim.HasFlag("risk_taker") ? "risk_taker" : "combat_veteran")} — halved attack stress");

        // prone_to_panic: animal attacks trigger a secondary panic stress spike
        if (victim.HasFlag("prone_to_panic"))
        {
            float panicStress = tpl.AttackStressDelta * 0.5f;
            _world.Mood.GetMood(agentName).AdjustStress(panicStress);
            _world.LogDev($"[{agentName}] prone_to_panic → extra stress +{panicStress:F1}");
        }

        _world.Mood.GetMood(agentName).AdjustTrauma(+8f);

        string healthNote = tpl.AttackHealthDamage > 0 ? $", -{tpl.AttackHealthDamage:F0} health" : "";
        _world.LogAt(animal.X, animal.Y,
            $"A {tpl.DisplayName} attacks {agentName}! " +
            $"(-{tpl.AttackHungerDamage:F0} hunger, -{tpl.AttackThirstDamage:F0} thirst{healthNote})");
        _world.LogDev($"[animal] {tpl.DisplayName} attacked {agentName} → " +
            $"hunger -{tpl.AttackHungerDamage}  thirst -{tpl.AttackThirstDamage}  " +
            $"health -{tpl.AttackHealthDamage}  mood {tpl.AttackMoodDelta}  stress +{tpl.AttackStressDelta}");
        _world.Memory.AddMemory(agentName,
            $"A {tpl.DisplayName} attacked me! Lost hunger, thirst{(tpl.AttackHealthDamage > 0 ? ", and health" : "")} from the injury.");

        _events.Add(new SimEvent
        {
            Type      = "animal_attack",
            AgentName = tpl.DisplayName,
            Label     = "attacks",
            Content   = $"{tpl.DisplayName} attacks {agentName}! (-{tpl.AttackHungerDamage:F0} hunger, -{tpl.AttackThirstDamage:F0} thirst{healthNote})"
        });

        // Witnesses within 2 cells react
        foreach (var (name, _, _) in _world.GetAgentsInRadius(animal.X, animal.Y, 2)
                                           .Where(a => a.name != agentName))
        {
            _world.Memory.AddMemory(name,
                $"Witnessed a {tpl.DisplayName} attack {agentName}. The streets are not safe.");
            _world.Mood.GetMood(name).AdjustStress(+12f);
            _world.Mood.GetMood(name).AdjustMood(-8f);
        }
    }

    private void DropAnimalLoot(Animal animal)
    {
        var tpl = AnimalDefinitions.Get(animal.Type);
        if (_rng.NextDouble() <= tpl.LootChance)
        {
            foreach (var itemId in tpl.LootTable)
                _world.Items.PlaceItemAt(itemId, animal.X, animal.Y);
        }
        _world.LogAt(animal.X, animal.Y, $"The {tpl.DisplayName} lies dead here.");
    }

    // ── Agent interaction methods ─────────────────────────────────────────────

    public string? TryAttackAnimal(string agentName, string animalIdStr)
    {
        var pos = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return null;

        var animal = _animals.FirstOrDefault(a =>
            a.Id.ToString() == animalIdStr &&
            a.X == pos.x && a.Y == pos.y &&
            a.State != AnimalState.Dead);
        if (animal == null) return null;

        var tpl          = AnimalDefinitions.Get(animal.Type);
        var personality  = _world.GetPersonality(agentName);
        float weapon     = _world.Items.GetWeaponBonus(agentName);
        float aggBonus   = personality.AggressionAttackBonus;
        float riskBonus  = personality.HasFlag("risk_taker") ? 5f : 0f;
        float vetBonus   = personality.IsCombatVeteran ? 8f : 0f;
        float baseDmg    = animal.Size == AnimalSize.Large
            ? (float)(_rng.Next(1, 21) + 5)    // 6–25
            : (float)(_rng.Next(1, 11) + 10);   // 11–20
        float damage     = baseDmg + weapon + aggBonus + riskBonus + vetBonus;

        animal.Health -= damage;
        string aggNote    = aggBonus != 0  ? $" ({(aggBonus > 0 ? "+" : "")}{aggBonus:F0} aggression)" : "";
        string weaponNote = weapon   > 0   ? $" (+{weapon:F0} weapon)"          : "";
        string riskNote   = riskBonus > 0  ? " [risk taker]"                    : "";
        string vetNote    = vetBonus  > 0  ? " [combat veteran]"                : "";
        _world.LogAt(pos.x, pos.y, $"{agentName} attacks the {tpl.DisplayName} for {damage:F0} damage{weaponNote}{aggNote}{riskNote}{vetNote}!");
        _world.Memory.AddMemory(agentName, $"Attacked a {tpl.DisplayName} at ({pos.x},{pos.y}).");

        if (animal.Health <= 0)
        {
            animal.State = AnimalState.Dead;
            _world.Memory.AddMemory(agentName, $"Killed a {tpl.DisplayName}! Loot should drop nearby.");
            float killMoodBase = animal.Size == AnimalSize.Large ? +20f : +8f;
            float killMoodBonus = personality.HasFlag("risk_taker") ? +5f : 0f;
            _world.Mood.GetMood(agentName).AdjustMood(killMoodBase + killMoodBonus);
            _world.Mood.GetMood(agentName).AdjustStress(animal.Size == AnimalSize.Large ? -10f : -3f);
            if (animal.Size == AnimalSize.Large)
            {
                _world.Mood.GetMood(agentName).AdjustHope(+6f);
                _world.LogDev($"[{agentName}] large kill → hope +6");
            }
            return $"kills the {tpl.DisplayName}";
        }

        // Small animals flee when struck but not killed
        if (animal.Size == AnimalSize.Small)
        {
            animal.State = AnimalState.Fleeing;
            WanderAnimal(animal);
            WanderAnimal(animal);
            return $"strikes the {tpl.DisplayName} — it bolts! ({animal.Health:F0} HP remaining)";
        }

        // Large animals counter-attack when not already fleeing
        if (animal.State != AnimalState.Fleeing)
        {
            ApplyAnimalAttack(animal, agentName);
            return $"injures the {tpl.DisplayName} ({animal.Health:F0} HP — it counter-attacks!)";
        }

        return $"strikes the {tpl.DisplayName} ({animal.Health:F0} HP remaining)";
    }

    public string? TryTrapAnimal(string agentName, string animalIdStr)
    {
        var inv = _world.Items.GetInventory(agentName);
        var trapItem = inv.FirstOrDefault(i => i.DefinitionId == "wire_bundle");
        if (trapItem == null) return "— no Wire Bundle in inventory to set a trap";

        var pos = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return null;

        var animal = _animals.FirstOrDefault(a =>
            a.Id.ToString() == animalIdStr &&
            a.Size == AnimalSize.Small &&
            a.X == pos.x && a.Y == pos.y &&
            a.State != AnimalState.Dead);
        if (animal == null) return null;

        var tpl = AnimalDefinitions.Get(animal.Type);

        // Consume the wire bundle (removed from inventory, not dropped to ground)
        _world.Items.TryConsume(agentName, trapItem.InstanceId.ToString());

        if (_rng.NextDouble() < 0.60)
        {
            animal.State = AnimalState.Dead;
            _world.Memory.AddMemory(agentName, $"Trapped a {tpl.DisplayName} successfully.");
            _world.Mood.GetMood(agentName).AdjustMood(+10f);
            _world.Mood.GetMood(agentName).AdjustStress(-5f);
            return $"traps the {tpl.DisplayName}! (wire bundle consumed)";
        }
        else
        {
            _world.Memory.AddMemory(agentName, $"Attempted to trap a {tpl.DisplayName} — it escaped.");
            _world.Mood.GetMood(agentName).AdjustMood(-3f);
            return $"attempts to trap the {tpl.DisplayName} — it escapes! (wire bundle lost)";
        }
    }

    public string? TryScareAnimal(string agentName, string animalIdStr)
    {
        var pos = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return null;

        var animal = _animals.FirstOrDefault(a =>
            a.Id.ToString() == animalIdStr &&
            a.Size == AnimalSize.Large &&
            Math.Max(Math.Abs(a.X - pos.x), Math.Abs(a.Y - pos.y)) <= 2 &&
            a.State != AnimalState.Dead);
        if (animal == null)
        {
            // Give feedback if the animal exists but is out of scare range
            var tooFar = _animals.FirstOrDefault(a =>
                a.Id.ToString() == animalIdStr &&
                a.State != AnimalState.Dead);
            if (tooFar != null)
                return $"— {AnimalDefinitions.Get(tooFar.Type).DisplayName} is too far away to scare (must be within 2 cells)";
            return null;
        }

        var tpl = AnimalDefinitions.Get(animal.Type);
        var scarePersonality = _world.GetPersonality(agentName);

        // animal_handler: +20% scare chance and no counter-attack on failed scare
        float scareBonus  = scarePersonality.IsAnimalHandler ? 0.20f : 0f;
        bool  safeFailure = scarePersonality.IsAnimalHandler;

        if (_rng.NextDouble() < tpl.ScareChance + scareBonus)
        {
            animal.State = AnimalState.Fleeing;
            MoveAwayFrom(animal, pos.x, pos.y);
            MoveAwayFrom(animal, pos.x, pos.y);
            _world.Memory.AddMemory(agentName, $"Scared off a {tpl.DisplayName}!");
            _world.Mood.GetMood(agentName).AdjustMood(+12f);
            _world.Mood.GetMood(agentName).AdjustStress(-8f);
            string handlerNote = scareBonus > 0 ? " [animal handler]" : "";
            return $"scares off the {tpl.DisplayName} — it retreats!{handlerNote}";
        }
        else if (safeFailure)
        {
            // Animal handler reads the animal — backs off safely when the scare fails
            animal.State = AnimalState.Idle;
            _world.Memory.AddMemory(agentName, $"Tried to scare a {tpl.DisplayName} — couldn't intimidate it, but backed off safely.");
            _world.Mood.GetMood(agentName).AdjustMood(-2f);
            _world.LogDev($"[{agentName}] animal_handler safe failure — no counter-attack");
            return $"fails to scare the {tpl.DisplayName} — backs off carefully [animal handler]";
        }
        else
        {
            animal.State = AnimalState.Hunting;
            animal.TargetAgentName = agentName;
            ApplyAnimalAttack(animal, agentName);
            _world.Memory.AddMemory(agentName,
                $"Tried to scare a {tpl.DisplayName} — it attacked me instead!");
            return $"fails to scare the {tpl.DisplayName} — it attacks!";
        }
    }

    public string? TryFeedAnimal(string agentName, string animalIdStr)
    {
        var pos = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return null;

        var animal = _animals.FirstOrDefault(a =>
            a.Id.ToString() == animalIdStr &&
            a.State != AnimalState.Dead &&
            Math.Max(Math.Abs(a.X - pos.x), Math.Abs(a.Y - pos.y)) <= 2);
        if (animal == null) return null;

        var tpl = AnimalDefinitions.Get(animal.Type);

        // Require a food item in inventory
        var inv      = _world.Items.GetInventory(agentName);
        var foodItem = inv.FirstOrDefault(i => i.Definition.HungerRestore > 0);
        if (foodItem == null) return "— no food in inventory to offer";

        float foodValue = foodItem.Definition.HungerRestore;
        _world.Items.TryConsume(agentName, foodItem.InstanceId.ToString());

        var personality = _world.GetPersonality(agentName);

        if (animal.Size == AnimalSize.Small)
        {
            // Small animals: bond with feeder for 3 rounds, follow them around
            animal.CompanionOf         = agentName;
            animal.CompanionRoundsLeft = 3;
            animal.State               = AnimalState.Idle;

            float moodBoost   = animal.Type == AnimalType.StreetCat ? 14f : 8f;
            float stressRelief = animal.Type == AnimalType.StreetCat ? -12f : -6f;
            string flavorNote = animal.Type == AnimalType.StreetCat
                ? "The cat inches forward and eats from your hand. Something shifts between you."
                : $"The {tpl.DisplayName} takes the food cautiously, eyeing you.";

            if (_world.Mood.Has(agentName))
            {
                var m = _world.Mood.GetMood(agentName);
                m.AdjustMood(moodBoost);
                m.AdjustStress(stressRelief);
            }
            _world.LogAt(pos.x, pos.y, $"{agentName} feeds a {tpl.DisplayName} with {foodItem.DisplayName}. {flavorNote}");
            _world.LogDev($"[{agentName}] feed {tpl.DisplayName} → mood +{moodBoost}  stress {stressRelief}  companion 3 rounds");
            _world.Memory.AddMemory(agentName, $"Fed a {tpl.DisplayName} — it follows me now.");
            return $"feeds the {tpl.DisplayName} — it warms to you (follows for ~3 turns)";
        }
        else
        {
            // Large animals: food-quality + animal_handler bonus affects distraction chance.
            // Feeding is safe — no counter-attack on failure.
            float feedChance = 0.35f + Math.Min(0.35f, foodValue / 100f);
            bool  isHandler  = personality.IsAnimalHandler;
            if (isHandler) feedChance += 0.15f;

            if (_rng.NextDouble() < feedChance)
            {
                animal.State           = AnimalState.Idle;
                animal.TargetAgentName = null;
                WanderAnimal(animal);
                WanderAnimal(animal);

                if (_world.Mood.Has(agentName))
                {
                    var m = _world.Mood.GetMood(agentName);
                    m.AdjustMood(+10f);
                    m.AdjustStress(-8f);
                }
                _world.LogAt(pos.x, pos.y,
                    $"{agentName} tosses {foodItem.DisplayName} to the {tpl.DisplayName}. It stops and eats.");
                _world.LogDev($"[{agentName}] feed large {tpl.DisplayName} success (chance {feedChance:F2}) → mood +10  stress -8");
                _world.Memory.AddMemory(agentName, $"Distracted a {tpl.DisplayName} with {foodItem.DisplayName} — it backed off.");
                string handlerNote = isHandler ? " [animal handler]" : "";
                return $"distracts the {tpl.DisplayName} with food — it backs off{handlerNote}";
            }
            else
            {
                // Ignored — food wasted, no retaliation
                _world.LogAt(pos.x, pos.y,
                    $"{agentName} tosses food at the {tpl.DisplayName} — it sniffs and ignores it.");
                _world.LogDev($"[{agentName}] feed large {tpl.DisplayName} failed (chance {feedChance:F2}) — ignored");
                _world.Memory.AddMemory(agentName, $"Tried to distract a {tpl.DisplayName} with food — it ignored it.");
                return $"tosses food at the {tpl.DisplayName} — it ignores it ({foodItem.DisplayName} wasted)";
            }
        }
    }

    // ── Movement helpers ─────────────────────────────────────────────────────

    private void MoveToward(Animal animal, int tx, int ty)
    {
        int dx = Math.Sign(tx - animal.X);
        int dy = Math.Sign(ty - animal.Y);

        // Try primary axis first (larger delta), then the other, then diagonal
        var candidates = new List<(int nx, int ny)>();

        int absDx = Math.Abs(tx - animal.X);
        int absDy = Math.Abs(ty - animal.Y);

        if (absDx >= absDy)
        {
            if (dx != 0) candidates.Add((animal.X + dx, animal.Y));
            if (dy != 0) candidates.Add((animal.X, animal.Y + dy));
        }
        else
        {
            if (dy != 0) candidates.Add((animal.X, animal.Y + dy));
            if (dx != 0) candidates.Add((animal.X + dx, animal.Y));
        }
        if (dx != 0 && dy != 0) candidates.Add((animal.X + dx, animal.Y + dy));

        foreach (var (nx, ny) in candidates)
        {
            if (IsValidMove(nx, ny, animal))
            {
                animal.X = nx;
                animal.Y = ny;
                return;
            }
        }
    }

    private void MoveAwayFrom(Animal animal, int fx, int fy)
    {
        int dx = Math.Sign(animal.X - fx);
        int dy = Math.Sign(animal.Y - fy);

        // If on same tile, pick a random direction
        if (dx == 0 && dy == 0)
        {
            dx = _rng.Next(-1, 2);
            dy = _rng.Next(-1, 2);
        }

        var candidates = new List<(int nx, int ny)>();
        if (dx != 0) candidates.Add((animal.X + dx, animal.Y));
        if (dy != 0) candidates.Add((animal.X, animal.Y + dy));
        if (dx != 0 && dy != 0) candidates.Add((animal.X + dx, animal.Y + dy));

        foreach (var (nx, ny) in candidates)
        {
            if (IsValidMove(nx, ny, animal))
            {
                animal.X = nx;
                animal.Y = ny;
                return;
            }
        }

        // Fallback: random adjacent
        WanderAnimal(animal);
    }

    private void WanderAnimal(Animal animal)
    {
        (int dx, int dy)[] dirs = [(0, -1), (0, 1), (1, 0), (-1, 0)];
        var shuffled = dirs.OrderBy(_ => _rng.Next()).ToArray();

        foreach (var (dx, dy) in shuffled)
        {
            int nx = animal.X + dx;
            int ny = animal.Y + dy;
            if (IsValidMove(nx, ny, animal))
            {
                animal.X = nx;
                animal.Y = ny;
                return;
            }
        }
    }

    /// <summary>
    /// Validates a candidate move cell for the given animal.
    /// Small animals can move anywhere on the map.
    /// Large animals are terrain-restricted by type:
    ///   Deer  → Forest or Park (woodland animal)
    ///   Dogs  → Street or Park (urban predators)
    /// </summary>
    private bool IsValidMove(int nx, int ny, Animal animal)
    {
        if (!_world.IsInBounds(nx, ny)) return false;
        var terrain = _world.GetCell(nx, ny).Terrain;
        if (animal.Size == AnimalSize.Small) return true;
        // Large forest animal: roams Forest and Park
        if (animal.Type == AnimalType.Deer)
            return terrain == TerrainType.Forest || terrain == TerrainType.Park;
        // Large urban animals: roam Street and Park
        return terrain == TerrainType.Street || terrain == TerrainType.Park;
    }

    // ── Respawn ──────────────────────────────────────────────────────────────

    private int _respawnTick = 0;

    public void TickRespawn()
    {
        _respawnTick++;
        if (_respawnTick % 5 != 0) return;

        var alive = _animals.Where(a => a.State != AnimalState.Dead).ToList();
        int smallCount = alive.Count(a => a.Size == AnimalSize.Small);
        int largeCount = alive.Count(a => a.Size == AnimalSize.Large);

        var smallEligible = new List<(int x, int y)>();
        var largeEligible = new List<(int x, int y)>();

        for (int y = 0; y < _world.MapHeight; y++)
        for (int x = 0; x < _world.MapWidth; x++)
        {
            var terrain = _world.GetCell(x, y).Terrain;
            bool noAgentsNear  = !_world.GetAgentsInRadius(x, y, 3).Any();
            bool noAnimalsHere = !alive.Any(a => a.X == x && a.Y == y);
            if (!noAgentsNear || !noAnimalsHere) continue;

            if (terrain == TerrainType.Street || terrain == TerrainType.Park || terrain == TerrainType.Forest)
                smallEligible.Add((x, y));
            if (terrain == TerrainType.Street || terrain == TerrainType.Forest)
                largeEligible.Add((x, y));
        }

        Shuffle(smallEligible);
        Shuffle(largeEligible);

        int spawned = 0;

        if (smallCount < 5 && smallEligible.Count > 0)
        {
            AnimalType[] cityPool = [AnimalType.Rat, AnimalType.Pigeon, AnimalType.Squirrel, AnimalType.StreetCat];
            int toSpawn = Math.Min(2, smallEligible.Count);
            for (int i = 0; i < toSpawn; i++)
            {
                var (sx, sy) = smallEligible[i];
                var t2 = _world.GetCell(sx, sy).Terrain;
                var type = t2 == TerrainType.Forest ? AnimalType.Fox : cityPool[_rng.Next(cityPool.Length)];
                SpawnAnimal(type, sx, sy);
                spawned++;
            }
        }

        if (largeCount < 2 && largeEligible.Count > 0)
        {
            AnimalType[] streetPool = [AnimalType.DogPack, AnimalType.Rottweiler, AnimalType.PitBull, AnimalType.Coyote];
            var (lx, ly) = largeEligible[0];
            var lt = _world.GetCell(lx, ly).Terrain;
            var type = lt == TerrainType.Forest ? AnimalType.Deer : streetPool[_rng.Next(streetPool.Length)];
            SpawnAnimal(type, lx, ly);
            _world.LogAt(lx, ly, $"A {AnimalDefinitions.Get(type).DisplayName} moves into the area.");
            spawned++;
        }

        if (spawned > 0)
            _world.LogDev($"[respawn] {spawned} animal(s) appeared in the city.");
    }

    // ── Utilities ────────────────────────────────────────────────────────────

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
