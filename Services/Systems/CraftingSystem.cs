using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public class CraftingSystem : ICraftingSystem
{
    private WorldState _world = null!;

    public void Attach(WorldState world) => _world = world;

    public string? TryCraft(string agentName, string recipeId)
    {
        if (!RecipeRegistry.TryGet(recipeId, out var recipe) || recipe is null)
            return "— unknown recipe";

        var pos = _world.GetAgentPosition(agentName);
        if (pos.x < 0) return null;

        var inv = _world.Items.GetInventory(agentName);

        // Resolve specific instances to consume, never reusing the same instance twice.
        var toConsume = new List<ItemInstance>();
        foreach (var ingredientId in recipe.Ingredients)
        {
            var match = inv.FirstOrDefault(i =>
                i.DefinitionId == ingredientId &&
                (i.Definition.MaxUses == 0 || i.UsesRemaining > 0) &&
                !toConsume.Contains(i));

            if (match is null)
                return $"— missing {ItemRegistry.Get(ingredientId).Name}";

            toConsume.Add(match);
        }

        foreach (var item in toConsume)
            _world.Items.ConsumeOneUse(agentName, item.InstanceId.ToString());

        for (int i = 0; i < recipe.ResultCount; i++)
            _world.Items.AddToInventory(agentName, recipe.ResultId);

        var resultName = ItemRegistry.Get(recipe.ResultId).Name;
        _world.LogAt(pos.x, pos.y, $"{agentName} crafts {resultName}.");

        if (_world.Mood.Has(agentName))
        {
            _world.Mood.GetMood(agentName).AdjustMood(+6f);
            _world.Mood.GetMood(agentName).AdjustStress(-4f);
        }

        var ingredientNames = string.Join(" + ", recipe.Ingredients.Select(id => ItemRegistry.Get(id).Name));
        _world.LogDev($"[{agentName}] crafted {resultName} from {ingredientNames} → mood +6  stress -4");
        _world.Memory.AddMemory(agentName,
            $"Crafted {resultName} from {ingredientNames}.");

        return $"crafts {resultName}";
    }
}
