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
        var (sx, sy) = MapGrid.DefaultStartPosition;

        var smallEligible = new List<(int x, int y)>();
        var largeEligible = new List<(int x, int y)>();

        for (int y = 0; y < _world.MapHeight; y++)
        {
            for (int x = 0; x < _world.MapWidth; x++)
            {
                var terrain = _world.GetCell(x, y).Terrain;
                int dist = Math.Max(Math.Abs(x - sx), Math.Abs(y - sy));

                // Small animals on streets, park, and forest
                if ((terrain == TerrainType.Street || terrain == TerrainType.Park || terrain == TerrainType.Forest) && dist >= 3)
                    smallEligible.Add((x, y));

                // Feral dogs patrol streets; deer roam the forest
                if (terrain == TerrainType.Street && dist >= 5)
                    largeEligible.Add((x, y));
                if (terrain == TerrainType.Forest)
                    largeEligible.Add((x, y));
            }
        }

        Shuffle(smallEligible);
        Shuffle(largeEligible);

        // Small animals: city types for streets/parks, Fox for forest
        AnimalType[] citySmall   = [AnimalType.Rat, AnimalType.Pigeon, AnimalType.Squirrel, AnimalType.StreetCat];
        int smallSpawned = 0;
        foreach (var (x, y) in smallEligible)
        {
            if (smallSpawned >= 12) break;
            var terrain = _world.GetCell(x, y).Terrain;
            if (terrain == TerrainType.Forest)
                SpawnAnimal(AnimalType.Fox, x, y);
            else
                SpawnAnimal(citySmall[smallSpawned % citySmall.Length], x, y);
            smallSpawned++;
        }

        // Large animals: dogs for streets, Deer for forest
        AnimalType[] streetLarge = [AnimalType.DogPack, AnimalType.Rottweiler, AnimalType.PitBull, AnimalType.Coyote];
        int largeSpawned = 0;
        foreach (var (x, y) in largeEligible)
        {
            if (largeSpawned >= 6) break;
            var terrain = _world.GetCell(x, y).Terrain;
            if (terrain == TerrainType.Forest)
                SpawnAnimal(AnimalType.Deer, x, y);
            else
                SpawnAnimal(streetLarge[largeSpawned % streetLarge.Length], x, y);
            largeSpawned++;
        }
    }

    private void SpawnAnimal(AnimalType type, int x, int y)
    {
        var tpl = AnimalDefinitions.Get(type);
        var animal = new Animal
        {
            Type               = type,
            Size               = tpl.Size,
            X                  = x,
            Y                  = y,
            Health             = tpl.MaxHealth,
            MaxHealth          = tpl.MaxHealth,
            DetectRadius       = tpl.DetectRadius,
            AttackRadius       = tpl.AttackRadius,
            FleeThreshold      = tpl.FleeThreshold,
            AttackHungerDamage = tpl.AttackHungerDamage,
            AttackThirstDamage = tpl.AttackThirstDamage,
            AttackMoodDelta    = tpl.AttackMoodDelta,
            AttackStressDelta  = tpl.AttackStressDelta,
            ScareChance        = tpl.ScareChance,
            LootTable          = tpl.LootTable,
            LootChance         = tpl.LootChance,
            DisplayName        = tpl.DisplayName,
            Description        = tpl.Description,
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

        if (_rng.NextDouble() < 0.40)
            WanderAnimal(animal, largeOnly: false);

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
        if (animal.Health < animal.MaxHealth * 0.25f)
        {
            animal.State = AnimalState.Fleeing;
            WanderAnimal(animal, largeOnly: true);
            return;
        }

        var tpl = AnimalDefinitions.Get(animal.Type);

        // Gather all prey within detect radius: small animals and agents alike
        int Dist(int ax, int ay) => Math.Max(Math.Abs(ax - animal.X), Math.Abs(ay - animal.Y));

        var nearestSmallAnimal = _animals
            .Where(a => a != animal && a.Size == AnimalSize.Small && a.State != AnimalState.Dead &&
                        Dist(a.X, a.Y) <= animal.DetectRadius)
            .OrderBy(a => Dist(a.X, a.Y))
            .FirstOrDefault();

        var nearestAgent = _world.GetAgentsInRadius(animal.X, animal.Y, animal.DetectRadius)
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
                    MoveToward(animal, preyX, preyY, largeOnly: true);
                    _world.LogAt(animal.X, animal.Y,
                        $"A {tpl.DisplayName} moves through the streets.");
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
                    MoveToward(animal, preyX, preyY, largeOnly: true);
                }
            }
        }
        else
        {
            animal.State = AnimalState.Idle;
            animal.RoundsInCurrentState++;
            if (_rng.NextDouble() < 0.50)
                WanderAnimal(animal, largeOnly: true);
        }
    }

    private void ApplyAnimalAttack(Animal animal, string agentName)
    {
        var tpl = AnimalDefinitions.Get(animal.Type);
        _world.Survival.AddHunger(agentName, -tpl.AttackHungerDamage);
        _world.Survival.AddThirst(agentName, -tpl.AttackThirstDamage);
        _world.Mood.GetMood(agentName).AdjustMood(tpl.AttackMoodDelta);
        _world.Mood.GetMood(agentName).AdjustStress(tpl.AttackStressDelta);

        _world.LogAt(animal.X, animal.Y,
            $"A {tpl.DisplayName} attacks {agentName}! " +
            $"(-{tpl.AttackHungerDamage:F0} hunger, -{tpl.AttackThirstDamage:F0} thirst)");
        _world.LogDev($"[animal] {tpl.DisplayName} attacked {agentName} → " +
            $"hunger -{tpl.AttackHungerDamage}  thirst -{tpl.AttackThirstDamage}  " +
            $"mood {tpl.AttackMoodDelta}  stress +{tpl.AttackStressDelta}");
        _world.Memory.AddMemory(agentName,
            $"A {tpl.DisplayName} attacked me! Lost hunger and thirst from the injury.");

        _events.Add(new SimEvent
        {
            Type      = "animal_attack",
            AgentName = tpl.DisplayName,
            Label     = "attacks",
            Content   = $"{tpl.DisplayName} attacks {agentName}! (-{tpl.AttackHungerDamage:F0} hunger, -{tpl.AttackThirstDamage:F0} thirst)"
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

        var tpl         = AnimalDefinitions.Get(animal.Type);
        float weapon    = _world.Items.GetWeaponBonus(agentName);
        float baseDmg   = animal.Size == AnimalSize.Large
            ? (float)(_rng.Next(1, 21) + 5)    // 6–25
            : (float)(_rng.Next(1, 11) + 10);   // 11–20
        float damage    = baseDmg + weapon;

        animal.Health -= damage;
        string weaponNote = weapon > 0 ? $" (+{weapon:F0} weapon bonus)" : "";
        _world.LogAt(pos.x, pos.y, $"{agentName} attacks the {tpl.DisplayName} for {damage:F0} damage{weaponNote}!");
        _world.Memory.AddMemory(agentName, $"Attacked a {tpl.DisplayName} at ({pos.x},{pos.y}).");

        if (animal.Health <= 0)
        {
            animal.State = AnimalState.Dead;
            _world.Memory.AddMemory(agentName, $"Killed a {tpl.DisplayName}! Loot should drop nearby.");
            _world.Mood.GetMood(agentName).AdjustMood(animal.Size == AnimalSize.Large ? +20f : +8f);
            _world.Mood.GetMood(agentName).AdjustStress(animal.Size == AnimalSize.Large ? -10f : -3f);
            return $"kills the {tpl.DisplayName}";
        }

        // Large animals counter-attack when not already fleeing
        if (animal.Size == AnimalSize.Large && animal.State != AnimalState.Fleeing)
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

        if (_rng.NextDouble() < tpl.ScareChance)
        {
            animal.State = AnimalState.Fleeing;
            MoveAwayFrom(animal, pos.x, pos.y, largeOnly: true);
            MoveAwayFrom(animal, pos.x, pos.y, largeOnly: true);
            _world.Memory.AddMemory(agentName, $"Scared off a {tpl.DisplayName}!");
            _world.Mood.GetMood(agentName).AdjustMood(+12f);
            _world.Mood.GetMood(agentName).AdjustStress(-8f);
            return $"scares off the {tpl.DisplayName} — it retreats!";
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

    // ── Movement helpers ─────────────────────────────────────────────────────

    private void MoveToward(Animal animal, int tx, int ty, bool largeOnly)
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
            if (IsValidMove(nx, ny, largeOnly))
            {
                animal.X = nx;
                animal.Y = ny;
                return;
            }
        }
    }

    private void MoveAwayFrom(Animal animal, int fx, int fy, bool largeOnly)
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
            if (IsValidMove(nx, ny, largeOnly))
            {
                animal.X = nx;
                animal.Y = ny;
                return;
            }
        }

        // Fallback: random adjacent
        WanderAnimal(animal, largeOnly);
    }

    private void WanderAnimal(Animal animal, bool largeOnly)
    {
        (int dx, int dy)[] dirs = [(0, -1), (0, 1), (1, 0), (-1, 0)];
        var shuffled = dirs.OrderBy(_ => _rng.Next()).ToArray();

        foreach (var (dx, dy) in shuffled)
        {
            int nx = animal.X + dx;
            int ny = animal.Y + dy;
            if (IsValidMove(nx, ny, largeOnly))
            {
                animal.X = nx;
                animal.Y = ny;
                return;
            }
        }
    }

    private bool IsValidMove(int nx, int ny, bool largeOnly)
    {
        if (!_world.IsInBounds(nx, ny)) return false;
        var terrain = _world.GetCell(nx, ny).Terrain;
        if (largeOnly)
            return terrain == TerrainType.Street || terrain == TerrainType.Park;
        // Small animals can go anywhere — rats and pigeons fit everywhere
        return true;
    }

    // ── Utilities ────────────────────────────────────────────────────────────

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
            AnimalType[] cityPool   = [AnimalType.Rat, AnimalType.Pigeon, AnimalType.Squirrel, AnimalType.StreetCat];
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
