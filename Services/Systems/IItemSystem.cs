using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public interface IItemSystem
{
    void Attach(WorldState world);
    void InitializeItems();
    void InitializeAgent(string agentName);
    void RemoveAgent(string agentName);

    bool HasItemsAt(int x, int y);
    IReadOnlyList<ItemInstance> GetItemsAt(int x, int y);
    IReadOnlyList<ItemInstance> GetInventory(string agentName);

    string? TryPickUp(string agentName, string instanceIdStr);
    string? TryDrop(string agentName, string instanceIdStr);
    string TryUse(string agentName, string instanceIdStr);
    string? TryGive(string fromAgent, string instanceIdStr, string toAgent);

    /// Returns (consumed, success, yields).
    (bool consumed, bool success, IReadOnlyList<string> yielded) TryDeconstruct(string agentName, string instanceIdStr);

    /// Drops all of an agent's inventory at the given cell.
    void DropInventoryAt(string agentName, int x, int y);

    /// Places a new item instance (by definition id) directly on the map cell.
    void PlaceItemAt(string definitionId, int x, int y);

    /// Adds a new item instance (by definition id) directly into an agent's inventory.
    void AddToInventory(string agentName, string definitionId);

    /// Removes an inventory item without placing it on the ground. Returns false if not found.
    bool TryConsume(string agentName, string instanceIdStr);

    /// Silently removes an item from inventory and returns it (for transferring to a stash). Returns null if not found.
    ItemInstance? TryRemoveFromInventory(string agentName, string instanceIdStr);

    /// Adds an existing ItemInstance directly into an agent's inventory. Returns false if inventory full.
    bool TryAddItemInstance(string agentName, ItemInstance item);

    /// Consumes one charge of a multi-use item (or removes it entirely if single-use or last charge).
    bool ConsumeOneUse(string agentName, string instanceIdStr);

    /// Fills a container (one with a non-empty FillResult) with water, replacing it with
    /// the filled version. Caller is responsible for verifying tap access is available.
    string? TryFill(string agentName, string instanceIdStr);

    /// Moves an Improved Trap from inventory to an armed state at the agent's current cell.
    string? TryPlaceTrap(string agentName, string instanceIdStr);

    /// Returns all armed traps at a cell.
    IReadOnlyList<ItemInstance> GetPlacedTrapsAt(int x, int y);

    /// Removes and returns the first armed trap at a cell (triggered or retrieved by agent).
    ItemInstance? TakeTopTrapAt(int x, int y);

    /// Returns the highest AttackBonus among all items in an agent's inventory.
    float GetWeaponBonus(string agentName);

    /// Base 5 slots + sum of CarryCapacity of all container items in inventory.
    int GetCarryCapacity(string agentName);

    /// True when inventory.Count >= GetCarryCapacity(agentName).
    bool IsInventoryFull(string agentName);

    /// Called once per round. Periodically places new items in empty building cells.
    void TickRespawn();
}
