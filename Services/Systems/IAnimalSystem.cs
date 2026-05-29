using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public interface IAnimalSystem
{
    void Attach(WorldState world);
    void InitializeAnimals();

    /// Run one full animal tick (movement + attacks). Called once per round before agent turns.
    void TickAnimals();

    IReadOnlyList<Animal> AllAnimals { get; }
    IReadOnlyList<Animal> GetAnimalsAt(int x, int y);
    IReadOnlyList<Animal> GetAnimalsInRadius(int cx, int cy, int radius);

    /// Agent strikes an animal in their cell. Returns result description or null if no valid target.
    string? TryAttackAnimal(string agentName, string animalIdStr);

    /// Agent catches a small animal in their cell using a Wire Bundle. Returns result or null.
    string? TryTrapAnimal(string agentName, string animalIdStr);

    /// Agent attempts to frighten a large animal within 2 cells. May backfire. Returns result or null.
    string? TryScareAnimal(string agentName, string animalIdStr);

    /// Agent offers food to an animal within 2 cells. Consumes a food item from inventory.
    /// Small animals: mood boost, animal becomes a companion for ~3 rounds.
    /// Large animals: food quality–weighted chance to distract (no counter-attack on failure).
    string? TryFeedAnimal(string agentName, string animalIdStr);

    /// Drains and returns any SimEvents queued by the last TickAnimals call.
    IReadOnlyList<SimEvent> DrainEvents();

    /// Called once per round. Respawns animals if population drops below thresholds.
    void TickRespawn();
}
