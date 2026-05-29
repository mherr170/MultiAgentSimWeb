using MultiAgentSimWeb.Models;

namespace MultiAgentSimWebTests;

public class RecipeRegistryTests
{
    // ── TryGet ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("make_shiv")]
    [InlineData("make_bandages")]
    [InlineData("makeshift_trap")]
    [InlineData("make_fur_vest")]
    public void TryGet_KnownId_ReturnsTrueAndRecipe(string id)
    {
        var found = RecipeRegistry.TryGet(id, out var recipe);
        Assert.True(found);
        Assert.NotNull(recipe);
        Assert.Equal(id, recipe!.Id);
    }

    [Fact]
    public void TryGet_UnknownId_ReturnsFalse()
    {
        Assert.False(RecipeRegistry.TryGet("does_not_exist", out var recipe));
        Assert.Null(recipe);
    }

    // ── AllRecipes ────────────────────────────────────────────────────────────

    [Fact]
    public void AllRecipes_IsNonEmpty()
    {
        Assert.NotEmpty(RecipeRegistry.AllRecipes);
    }

    [Fact]
    public void AllRecipes_ContainsExpectedRecipes()
    {
        var ids = RecipeRegistry.AllRecipes.Select(r => r.Id).ToHashSet();
        Assert.Contains("make_shiv",      ids);
        Assert.Contains("make_bandages",  ids);
        Assert.Contains("makeshift_trap", ids);
        Assert.Contains("make_fur_vest",  ids);
    }

    // ── GetAvailable — basic matching ─────────────────────────────────────────

    [Fact]
    public void GetAvailable_EmptyInventory_ReturnsEmpty()
    {
        var result = RecipeRegistry.GetAvailable([]);
        Assert.Empty(result);
    }

    [Fact]
    public void GetAvailable_HasAllIngredients_ReturnsRecipe()
    {
        // make_shiv requires scrap_metal + duct_tape
        var inv = new List<ItemInstance>
        {
            new("scrap_metal"),
            new("duct_tape")
        };
        var available = RecipeRegistry.GetAvailable(inv);
        Assert.Contains(available, r => r.Id == "make_shiv");
    }

    [Fact]
    public void GetAvailable_MissingOneIngredient_DoesNotReturnRecipe()
    {
        // make_shiv requires scrap_metal + duct_tape — only scrap_metal present
        var inv = new List<ItemInstance> { new("scrap_metal") };
        var available = RecipeRegistry.GetAvailable(inv);
        Assert.DoesNotContain(available, r => r.Id == "make_shiv");
    }

    [Fact]
    public void GetAvailable_ExtraUnrelatedItems_DoesNotBlockRecipe()
    {
        var inv = new List<ItemInstance>
        {
            new("scrap_metal"),
            new("duct_tape"),
            new("water_bottle"),   // irrelevant
            new("first_aid_kit")   // irrelevant
        };
        var available = RecipeRegistry.GetAvailable(inv);
        Assert.Contains(available, r => r.Id == "make_shiv");
    }

    // ── Duplicate-ingredient recipes ──────────────────────────────────────────

    [Fact]
    public void GetAvailable_DuplicateIngredient_RequiresCorrectCount()
    {
        // make_fur_vest needs fur_scraps x2
        var oneOnly = new List<ItemInstance> { new("fur_scraps") };
        var twoOfThem = new List<ItemInstance> { new("fur_scraps"), new("fur_scraps") };

        Assert.DoesNotContain(RecipeRegistry.GetAvailable(oneOnly),  r => r.Id == "make_fur_vest");
        Assert.Contains       (RecipeRegistry.GetAvailable(twoOfThem), r => r.Id == "make_fur_vest");
    }

    // ── Exhausted-charge items ────────────────────────────────────────────────

    [Fact]
    public void GetAvailable_ExhaustedChargeItem_NotCountedAsIngredient()
    {
        // cooked_rat recipe needs rat_carcass + lighter (lighter has MaxUses=100)
        // A lighter with 0 uses remaining should not satisfy the ingredient
        var lighter = new ItemInstance("lighter") { UsesRemaining = 0 };
        var inv = new List<ItemInstance>
        {
            new("rat_carcass"),
            lighter
        };
        var available = RecipeRegistry.GetAvailable(inv);
        Assert.DoesNotContain(available, r => r.Id == "cooked_rat");
    }

    [Fact]
    public void GetAvailable_ChargedItem_CountsAsIngredient()
    {
        // lighter with remaining uses should satisfy the ingredient
        var inv = new List<ItemInstance>
        {
            new("rat_carcass"),
            new("lighter")   // UsesRemaining initialized to MaxUses (100)
        };
        var available = RecipeRegistry.GetAvailable(inv);
        Assert.Contains(available, r => r.Id == "cooked_rat");
    }
}

// ── Integrity ─────────────────────────────────────────────────────────────────

public class RecipeRegistryIntegrityTests
{
    [Fact]
    public void AllRecipeIngredients_ResolveToValidItemIds()
    {
        var allIds = ItemRegistry.AllIds.ToHashSet();
        foreach (var recipe in RecipeRegistry.AllRecipes)
            foreach (var ingredient in recipe.Ingredients)
                Assert.True(allIds.Contains(ingredient),
                    $"Recipe '{recipe.Id}' ingredient '{ingredient}' is not a valid item ID");
    }

    [Fact]
    public void AllRecipeResults_ResolveToValidItemIds()
    {
        var allIds = ItemRegistry.AllIds.ToHashSet();
        foreach (var recipe in RecipeRegistry.AllRecipes)
            Assert.True(allIds.Contains(recipe.ResultId),
                $"Recipe '{recipe.Id}' ResultId '{recipe.ResultId}' is not a valid item ID");
    }

    [Fact]
    public void AllRecipes_HaveNonEmptyNames()
    {
        foreach (var recipe in RecipeRegistry.AllRecipes)
            Assert.False(string.IsNullOrWhiteSpace(recipe.Name),
                $"Recipe '{recipe.Id}' has an empty Name");
    }

    [Fact]
    public void AllRecipes_HaveAtLeastOneIngredient()
    {
        foreach (var recipe in RecipeRegistry.AllRecipes)
            Assert.True(recipe.Ingredients.Length > 0,
                $"Recipe '{recipe.Id}' has no ingredients");
    }

    [Fact]
    public void AllRecipes_HavePositiveResultCount()
    {
        foreach (var recipe in RecipeRegistry.AllRecipes)
            Assert.True(recipe.ResultCount > 0,
                $"Recipe '{recipe.Id}' has ResultCount <= 0");
    }
}
