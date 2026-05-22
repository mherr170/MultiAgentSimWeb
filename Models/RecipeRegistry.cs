namespace MultiAgentSimWeb.Models;

public static class RecipeRegistry
{
    private static readonly Dictionary<string, CraftRecipe> _recipes = new()
    {
        // ── Survival tools ───────────────────────────────────────────────────
        ["makeshift_trap"] = new("makeshift_trap", "Improved Trap",
            ["wire_bundle", "scrap_metal"], "improved_trap", 1,
            "reusable armed trap — catches small animals automatically (85% chance)"),

        ["cooked_rat"] = new("cooked_rat", "Cooked Rat Meat",
            ["rat_carcass", "lighter"], "cooked_rat_meat", 1,
            "removes mood penalty, +25 hunger vs +15 raw (uses 1 lighter charge)"),

        ["cooked_dog"] = new("cooked_dog", "Cooked Dog Meat",
            ["dog_carcass", "lighter"], "cooked_dog_meat", 1,
            "reduces mood penalty, +55 hunger vs +40 raw (uses 1 lighter charge)"),

        // ── Medical ──────────────────────────────────────────────────────────
        ["make_bandages"] = new("make_bandages", "Field Bandage",
            ["pocket_knife", "fabric_strips"], "field_bandage", 1,
            "treats wounds — stress -20, mood +8"),

        ["make_splint"] = new("make_splint", "Improvised Splint",
            ["scrap_metal", "fabric_strips"], "splint", 1,
            "stabilises injury — hunger +10, thirst +5"),

        // ── Weapons ──────────────────────────────────────────────────────────
        ["make_shiv"] = new("make_shiv", "Shiv",
            ["scrap_metal", "duct_tape"], "shiv", 1,
            "weapon — +20 attack damage vs animals (applies automatically while carried)"),

        ["make_crude_knife"] = new("make_crude_knife", "Crude Knife",
            ["bone_shard", "fabric_strips"], "crude_knife", 1,
            "weapon — +12 attack damage vs animals (applies automatically while carried)"),

        // ── Comfort ──────────────────────────────────────────────────────────
        ["make_lantern"] = new("make_lantern", "Improvised Lantern",
            ["battery_pack", "wire_bundle"], "improvised_lantern", 1,
            "5 uses — mood +20, stress -18 per use"),

        ["make_leather_wrap"] = new("make_leather_wrap", "Leather Wrap",
            ["leather_scraps", "fabric_strips"], "leather_wrap", 1,
            "warmth — mood +10, stress -10"),
    };

    public static bool TryGet(string id, out CraftRecipe? recipe) =>
        _recipes.TryGetValue(id, out recipe);

    public static IEnumerable<CraftRecipe> AllRecipes => _recipes.Values;

    /// Returns recipes whose ingredients are all present in the given inventory.
    /// Handles duplicate-ingredient recipes (e.g. 2x scrap_metal) correctly.
    public static IReadOnlyList<CraftRecipe> GetAvailable(IReadOnlyList<ItemInstance> inventory)
    {
        return _recipes.Values.Where(r => HasIngredients(r, inventory)).ToList();
    }

    private static bool HasIngredients(CraftRecipe recipe, IReadOnlyList<ItemInstance> inventory)
    {
        var required = recipe.Ingredients
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        var available = inventory
            .Where(i => i.Definition.MaxUses == 0 || i.UsesRemaining > 0)
            .GroupBy(i => i.DefinitionId)
            .ToDictionary(g => g.Key, g => g.Count());

        return required.All(kv => available.GetValueOrDefault(kv.Key) >= kv.Value);
    }
}
