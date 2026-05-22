using MultiAgentSimWeb.Models;

namespace MultiAgentSimWebTests;

public class ItemRegistryTests
{
    private static readonly string[] RepresentativeIds =
    [
        "first_aid_kit", "water_bottle", "canned_food", "wire_bundle", "blanket",
        "ham_radio", "crowbar", "painkillers", "prescription_meds",
        "rat_carcass", "dog_carcass", "scrap_metal", "fabric_strips"
    ];

    [Theory]
    [InlineData("first_aid_kit")]
    [InlineData("water_bottle")]
    [InlineData("canned_food")]
    [InlineData("wire_bundle")]
    [InlineData("blanket")]
    [InlineData("ham_radio")]
    [InlineData("scrap_metal")]
    [InlineData("rat_carcass")]
    [InlineData("dog_carcass")]
    public void Get_KnownId_ReturnsDefinition(string id)
    {
        var def = ItemRegistry.Get(id);
        Assert.Equal(id, def.Id);
        Assert.NotEmpty(def.Name);
        Assert.NotEmpty(def.Description);
    }

    [Fact]
    public void Get_UnknownId_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() => ItemRegistry.Get("unicorn_dust"));
    }

    [Fact]
    public void AllIds_ContainsAllExpectedItems()
    {
        var ids = ItemRegistry.AllIds.ToHashSet();
        foreach (var expected in RepresentativeIds)
            Assert.Contains(expected, ids);
    }

    [Theory]
    [InlineData("canned_food")]
    [InlineData("water_bottle")]
    [InlineData("peanut_butter")]
    [InlineData("protein_bar")]
    [InlineData("canned_meat")]
    public void Get_FoodAndWaterItems_AreUsable(string id)
    {
        var def = ItemRegistry.Get(id);
        Assert.Equal(id, def.Id);
        Assert.True(def.IsUsable);
    }

    [Theory]
    [InlineData("canned_food",    25f, 0f)]
    [InlineData("water_bottle",    0f, 50f)]
    [InlineData("peanut_butter",  35f, 0f)]
    [InlineData("sports_drink",    8f, 35f)]
    [InlineData("first_aid_kit",   0f, 0f)]
    public void ItemDefinition_HungerAndThirstRestoreValues(string id, float expectedHunger, float expectedThirst)
    {
        var def = ItemRegistry.Get(id);
        Assert.Equal(expectedHunger, def.HungerRestore, precision: 5);
        Assert.Equal(expectedThirst, def.ThirstRestore, precision: 5);
    }

    [Theory]
    [InlineData("first_aid_kit",  true,  false)]
    [InlineData("crowbar",        false, true)]
    [InlineData("wire_bundle",    true,  true)]
    [InlineData("ham_radio",      true,  true)]
    [InlineData("blanket",        true,  true)]
    [InlineData("scrap_metal",    false, false)]
    [InlineData("fabric_strips",  false, false)]
    public void ItemDefinition_UsableAndDeconstructableFlags(string id, bool usable, bool deconstructable)
    {
        var def = ItemRegistry.Get(id);
        Assert.Equal(usable, def.IsUsable);
        Assert.Equal(deconstructable, def.IsDeconstructable);
    }

    [Theory]
    [InlineData("blanket",    "fabric_strips")]
    [InlineData("wire_bundle", "scrap_metal")]
    [InlineData("ham_radio",   "wire_bundle", "scrap_metal")]
    public void DeconstructYields_ContainExpectedComponents(string id, params string[] expectedYields)
    {
        var def = ItemRegistry.Get(id);
        Assert.True(def.IsDeconstructable);
        foreach (var y in expectedYields)
            Assert.Contains(y, def.DeconstructYields);
    }

    [Theory]
    [InlineData("blanket",      1.0f)]
    [InlineData("wire_bundle",  0.9f)]
    [InlineData("crowbar",      0.8f)]
    [InlineData("battery_pack", 0.6f)]
    [InlineData("ham_radio",    0.5f)]
    public void DeconstructChance_MatchesExpectedValue(string id, float expected)
    {
        Assert.Equal(expected, ItemRegistry.Get(id).DeconstructChance, precision: 5);
    }
}

public class ItemRegistryIntegrityTests
{
    [Fact]
    public void AllDeconstructYields_ResolveToValidIds()
    {
        var allIds = ItemRegistry.AllIds.ToHashSet();
        foreach (var id in ItemRegistry.AllIds)
        {
            var def = ItemRegistry.Get(id);
            if (def.DeconstructYields is null) continue;
            foreach (var yield in def.DeconstructYields)
                Assert.True(allIds.Contains(yield), $"{id}.DeconstructYields contains unknown id '{yield}'");
        }
    }

    [Fact]
    public void AllFillResults_ResolveToValidIds()
    {
        var allIds = ItemRegistry.AllIds.ToHashSet();
        foreach (var id in ItemRegistry.AllIds)
        {
            var def = ItemRegistry.Get(id);
            if (string.IsNullOrEmpty(def.FillResult)) continue;
            Assert.True(allIds.Contains(def.FillResult), $"{id}.FillResult references unknown id '{def.FillResult}'");
        }
    }

    [Theory]
    [InlineData("honey_jar",    22f)]
    [InlineData("dried_pasta",  20f)]
    [InlineData("cereal_box",   18f)]
    [InlineData("cooking_oil",  14f)]
    public void NewEdibles_HaveCorrectHungerRestoreValues(string id, float expectedHunger)
    {
        var def = ItemRegistry.Get(id);
        Assert.True(def.IsUsable, $"{id} should be usable");
        Assert.Equal(expectedHunger, def.HungerRestore, precision: 5);
    }

    [Theory]
    [InlineData("filled_tin_can")]
    [InlineData("filled_mason_jar")]
    [InlineData("filled_cooking_pot")]
    [InlineData("filled_water_jug")]
    [InlineData("filled_bucket")]
    public void FilledContainers_AreUsableWithPositiveThirstRestore(string id)
    {
        var def = ItemRegistry.Get(id);
        Assert.True(def.IsUsable, $"{id} should be usable");
        Assert.True(def.ThirstRestore > 0f, $"{id} should restore thirst");
    }

    [Theory]
    [InlineData("tin_can",      "filled_tin_can")]
    [InlineData("mason_jar",    "filled_mason_jar")]
    [InlineData("cooking_pot",  "filled_cooking_pot")]
    [InlineData("water_jug",    "filled_water_jug")]
    [InlineData("bucket",       "filled_bucket")]
    public void ContainerFillResult_MapsToCorrectFilledId(string emptyId, string filledId)
    {
        Assert.Equal(filledId, ItemRegistry.Get(emptyId).FillResult);
        Assert.Equal(filledId, ItemRegistry.Get(filledId).Id);
    }
}

public class ItemInstanceTests
{
    [Fact]
    public void NewInstance_HasUniqueGuid()
    {
        var a = new ItemInstance("scrap_metal");
        var b = new ItemInstance("scrap_metal");
        Assert.NotEqual(a.InstanceId, b.InstanceId);
    }

    [Fact]
    public void DisplayName_MatchesDefinitionName()
    {
        var inst = new ItemInstance("first_aid_kit");
        Assert.Equal(ItemRegistry.Get("first_aid_kit").Name, inst.DisplayName);
    }

    [Fact]
    public void Definition_ReturnsCorrectDefinition()
    {
        var inst = new ItemInstance("chocolate_bar");
        Assert.Equal("chocolate_bar", inst.Definition.Id);
    }
}
