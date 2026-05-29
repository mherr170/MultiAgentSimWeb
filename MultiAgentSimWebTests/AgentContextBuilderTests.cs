using MultiAgentSimWeb.Models;
using MultiAgentSimWeb.Services;

namespace MultiAgentSimWebTests;

public class AgentContextBuilderTests
{
    private static WorldState MakeWorld(string situation = "Test situation.", int x = 13, int y = 13)
    {
        var world = new WorldState(situation, MapGrid.CreateDefault());
        world.InitializeAgent("Alice", x, y);
        return world;
    }

    // ── Basic structure ───────────────────────────────────────────────────────

    [Fact]
    public void Build_ContainsSituationHeader()
    {
        var world = MakeWorld();
        Assert.Contains("SITUATION:", AgentContextBuilder.Build(world, "Alice"));
    }

    [Fact]
    public void Build_ContainsAgentPosition()
    {
        var world = MakeWorld(x: 10, y: 15);
        Assert.Contains("(10, 15)", AgentContextBuilder.Build(world, "Alice"));
    }

    [Fact]
    public void Build_ContainsExitsBlock()
    {
        var world = MakeWorld();
        Assert.Contains("EXITS:", AgentContextBuilder.Build(world, "Alice"));
    }

    [Fact]
    public void Build_ContainsInventoryBlock()
    {
        var world = MakeWorld();
        Assert.Contains("YOUR INVENTORY", AgentContextBuilder.Build(world, "Alice"));
    }

    [Fact]
    public void Build_ContainsRecentEventsBlock()
    {
        var world = MakeWorld();
        Assert.Contains("RECENT EVENTS", AgentContextBuilder.Build(world, "Alice"));
    }

    [Fact]
    public void Build_UnknownAgent_ReturnsErrorString()
    {
        var world = MakeWorld();
        Assert.Contains("ERROR", AgentContextBuilder.Build(world, "Ghost"));
    }

    // ── Situation truncation ──────────────────────────────────────────────────

    [Fact]
    public void Build_Round5OrLess_SituationFullTextPresent()
    {
        var longSit = new string('A', 200);
        var world   = new WorldState(longSit, MapGrid.CreateDefault());
        world.InitializeAgent("Alice", 13, 13);
        world.CurrentRound = 5;

        var ctx = AgentContextBuilder.Build(world, "Alice");

        Assert.Contains(longSit, ctx);
    }

    [Fact]
    public void Build_Round1_SituationFullTextPresent()
    {
        var longSit = new string('B', 200);
        var world   = new WorldState(longSit, MapGrid.CreateDefault());
        world.InitializeAgent("Alice", 13, 13);
        world.CurrentRound = 1;

        var ctx = AgentContextBuilder.Build(world, "Alice");

        Assert.Contains(longSit, ctx);
    }

    [Fact]
    public void Build_Round6_LongSituation_TruncatedWithEllipsis()
    {
        var longSit = new string('C', 200);
        var world   = new WorldState(longSit, MapGrid.CreateDefault());
        world.InitializeAgent("Alice", 13, 13);
        world.CurrentRound = 6;

        var ctx = AgentContextBuilder.Build(world, "Alice");

        Assert.Contains("…", ctx);
        Assert.DoesNotContain(longSit, ctx); // full 200-char string not present
    }

    [Fact]
    public void Build_Round6_ShortSituation_NotTruncated()
    {
        const string shortSit = "Brief.";
        var world = new WorldState(shortSit, MapGrid.CreateDefault());
        world.InitializeAgent("Alice", 13, 13);
        world.CurrentRound = 6;

        var ctx = AgentContextBuilder.Build(world, "Alice");

        Assert.Contains(shortSit, ctx);
        Assert.DoesNotContain("…", ctx);
    }

    // ── Crafting section ──────────────────────────────────────────────────────

    [Fact]
    public void Build_NoRecipeIngredients_NoCraftingSection()
    {
        var world = MakeWorld();
        // Alice has no items — no recipes available
        Assert.DoesNotContain("CRAFTING", AgentContextBuilder.Build(world, "Alice"));
    }

    [Fact]
    public void Build_HasIngredients_ShowsCraftingSection()
    {
        var world = MakeWorld();
        // make_shiv requires scrap_metal + duct_tape
        world.Items.AddToInventory("Alice", "scrap_metal");
        world.Items.AddToInventory("Alice", "duct_tape");

        Assert.Contains("CRAFTING", AgentContextBuilder.Build(world, "Alice"));
    }

    [Fact]
    public void Build_HasIngredients_RecipeNameAppearsInCraftingSection()
    {
        var world = MakeWorld();
        world.Items.AddToInventory("Alice", "scrap_metal");
        world.Items.AddToInventory("Alice", "duct_tape");

        Assert.Contains("make_shiv", AgentContextBuilder.Build(world, "Alice"));
    }

    // ── Recent events capped at 8 ─────────────────────────────────────────────

    [Fact]
    public void Build_MoreThan8NearbyEvents_OnlyLast8Shown()
    {
        var world = MakeWorld("Alice", x: 13, y: 13);
        // Add 12 events at Alice's cell (zero-padded so "line 01" isn't a prefix of "line 10")
        for (int i = 1; i <= 12; i++)
            world.LogAt(13, 13, $"Event line {i:D2}");

        var ctx   = AgentContextBuilder.Build(world, "Alice");
        var start = ctx.IndexOf("RECENT EVENTS", StringComparison.Ordinal);
        var block = ctx[start..]; // everything from the header onward

        // Only events 05–12 should appear (last 8 of 12)
        for (int i = 5; i <= 12; i++)
            Assert.Contains($"Event line {i:D2}", block);
        for (int i = 1; i <= 4; i++)
            Assert.DoesNotContain($"Event line {i:D2}", block);
    }

    [Fact]
    public void Build_ExactlyZeroNearbyEvents_ShowsNothingHappenedMessage()
    {
        var world = MakeWorld();
        var ctx   = AgentContextBuilder.Build(world, "Alice");
        Assert.Contains("Nothing has happened nearby yet", ctx);
    }

    // ── Personality flag notes ────────────────────────────────────────────────

    [Fact]
    public void Build_KnownPersonalityFlag_EmitsNoteInOutput()
    {
        var world = MakeWorld();
        world.SetPersonality("Alice", new PersonalityProfile
        {
            AgentName = "Alice",
            Flags     = ["hoards_food"]
        });

        var ctx = AgentContextBuilder.Build(world, "Alice");
        Assert.Contains("NOTE (personality)", ctx);
        Assert.Contains("stockpile", ctx); // specific wording from hoards_food note
    }

    [Fact]
    public void Build_UnknownPersonalityFlag_NoNoteEmitted()
    {
        var world = MakeWorld();
        world.SetPersonality("Alice", new PersonalityProfile
        {
            AgentName = "Alice",
            Flags     = ["nonexistent_flag_xyz"]
        });

        var ctx = AgentContextBuilder.Build(world, "Alice");
        Assert.DoesNotContain("NOTE (personality)", ctx);
    }

    [Fact]
    public void Build_NoPersonalityFlags_NoNoteSection()
    {
        var world = MakeWorld();
        world.SetPersonality("Alice", new PersonalityProfile
        {
            AgentName = "Alice",
            Flags     = []
        });

        Assert.DoesNotContain("NOTE (personality)", AgentContextBuilder.Build(world, "Alice"));
    }

    // ── Inventory vs ground item formatting ───────────────────────────────────

    [Fact]
    public void Build_InventoryItem_ShowsDisplayNameWithoutDescription()
    {
        var world = MakeWorld("Alice", x: 13, y: 13);
        world.InitializeItems();
        var item = world.GetItemsAt(13, 13).First(i => i.DefinitionId == "first_aid_kit");
        world.TryPickUp("Alice", item.InstanceId.ToString());

        var ctx = AgentContextBuilder.Build(world, "Alice");

        // Inventory block should contain the item's display name
        var invStart = ctx.IndexOf("YOUR INVENTORY", StringComparison.Ordinal);
        var invBlock = ctx[invStart..ctx.IndexOf("ITEMS HERE", StringComparison.Ordinal)];
        Assert.Contains("First Aid Kit", invBlock);
        // Descriptions are stripped in inventory — should not appear between brackets and next line
        Assert.DoesNotContain("-- A compact emergency kit", invBlock);
    }

    [Fact]
    public void Build_GroundItem_ShowsDisplayNameAndDescription()
    {
        var world = MakeWorld("Alice", x: 13, y: 13);
        world.InitializeItems();

        // First Aid Kit stays on the ground — don't pick it up
        var ctx = AgentContextBuilder.Build(world, "Alice");

        var groundStart = ctx.IndexOf("ITEMS HERE", StringComparison.Ordinal);
        var groundBlock = ctx[groundStart..];
        Assert.Contains("First Aid Kit", groundBlock);
        // Ground items include " -- <description>"
        Assert.Contains(" -- ", groundBlock);
    }

    [Fact]
    public void Build_EmptyInventory_ShowsEmptyLabel()
    {
        var world = MakeWorld();
        Assert.Contains("(empty)", AgentContextBuilder.Build(world, "Alice"));
    }
}
