using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Systems;

public class CraftingSystem : ICraftingSystem
{
    private WorldState _world = null!;
    private readonly Random _rng = new();

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

        // Failure chance: only agents with Resourcefulness < 40 can fail (max ~20% at 0).
        // crafting_expert is immune to failure entirely.
        var personality     = _world.GetPersonality(agentName);
        bool craftingExpert = personality.IsCraftingExpert;
        var ingredientNames = string.Join(" + ", recipe.Ingredients.Select(id => ItemRegistry.Get(id).Name));
        var resultName      = ItemRegistry.Get(recipe.ResultId).Name;

        if (!craftingExpert && personality.Resourcefulness < 40)
        {
            float failChance = (40 - personality.Resourcefulness) / 200f;  // 0-20%
            if (_rng.NextDouble() < failChance)
            {
                // Consume ingredients -- they're ruined in the attempt.
                foreach (var item in toConsume)
                    _world.Items.ConsumeOneUse(agentName, item.InstanceId.ToString());

                _world.LogAt(pos.x, pos.y,
                    $"{agentName} tries to craft {resultName} but botches it -- ingredients lost.");
                if (_world.Mood.Has(agentName))
                {
                    _world.Mood.GetMood(agentName).AdjustMood(-8f);
                    _world.Mood.GetMood(agentName).AdjustStress(+5f);
                }
                _world.LogDev($"[{agentName}] crafting FAILED {resultName} (fail chance {failChance:P0}, resourcefulness {personality.Resourcefulness}) -> mood -8  stress +5");
                _world.Memory.AddMemory(agentName,
                    $"Tried to craft {resultName} from {ingredientNames} -- botched it and wasted the materials.");
                return $"botches the craft -- {ingredientNames} lost";
            }
        }

        foreach (var item in toConsume)
            _world.Items.ConsumeOneUse(agentName, item.InstanceId.ToString());

        int totalCount = recipe.ResultCount + (craftingExpert ? 1 : 0);
        for (int i = 0; i < totalCount; i++)
            _world.Items.AddToInventory(agentName, recipe.ResultId);

        string expertNote = craftingExpert ? " (crafting expert: +1 bonus)" : "";
        _world.LogAt(pos.x, pos.y, $"{agentName} crafts {resultName}{expertNote}.");

        if (_world.Mood.Has(agentName))
        {
            _world.Mood.GetMood(agentName).AdjustMood(+6f);
            _world.Mood.GetMood(agentName).AdjustStress(-4f);
            _world.Mood.GetMood(agentName).AdjustHope(+3f);
        }

        _world.LogDev($"[{agentName}] crafted {resultName} x{totalCount} from {ingredientNames} -> mood +6  stress -4  hope +3{(craftingExpert ? "  [crafting expert]" : "")}");
        _world.Memory.AddMemory(agentName,
            $"Crafted {resultName} from {ingredientNames}.");

        return $"crafts {resultName}";
    }
}
