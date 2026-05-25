using MultiAgentSimWeb.Models;
using MultiAgentSimWeb.Services;

namespace MultiAgentSimWebTests;

public class WorldStateTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static WorldState MakeWorld(string situation = "Test situation.")
    {
        var map = MapGrid.CreateDefault();
        return new WorldState(situation, map);
    }

    private static WorldState MakeWorldWithAgent(string name = "Alice", int x = 10, int y = 15)
    {
        var world = MakeWorld();
        world.InitializeAgent(name, x, y);
        return world;
    }

    // ── Agent init / position ────────────────────────────────────────────────

    [Fact]
    public void InitializeAgent_SetsPosition()
    {
        var world = MakeWorld();
        world.InitializeAgent("Alice", 5, 7);
        Assert.Equal((5, 7), world.GetAgentPosition("Alice"));
    }

    [Fact]
    public void GetAgentPosition_UnknownAgent_ReturnsNegativeOne()
    {
        var world = MakeWorld();
        Assert.Equal((-1, -1), world.GetAgentPosition("Nobody"));
    }

    [Fact]
    public void GetAgentsAtPosition_ReturnsCorrectAgents()
    {
        var world = MakeWorld();
        world.InitializeAgent("Alice", 5, 5);
        world.InitializeAgent("Bob",   5, 5);
        world.InitializeAgent("Carol", 6, 6);

        var at55 = world.GetAgentsAtPosition(5, 5);
        Assert.Contains("Alice", at55);
        Assert.Contains("Bob",   at55);
        Assert.DoesNotContain("Carol", at55);
    }

    // ── Movement ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("N", 10, 14)]
    [InlineData("S", 10, 16)]
    [InlineData("E", 11, 15)]
    [InlineData("W",  9, 15)]
    public void MoveAgent_CardinalDirections_UpdatePosition(string dir, int expX, int expY)
    {
        var world = MakeWorldWithAgent("Alice", 10, 15);
        var moved = world.MoveAgent("Alice", dir);
        Assert.True(moved);
        Assert.Equal((expX, expY), world.GetAgentPosition("Alice"));
    }

    [Fact]
    public void MoveAgent_OffMapEdge_ReturnsFalseAndStays()
    {
        var world = MakeWorldWithAgent("Alice", 0, 0);
        var movedN = world.MoveAgent("Alice", "N");
        var movedW = world.MoveAgent("Alice", "W");
        Assert.False(movedN);
        Assert.False(movedW);
        Assert.Equal((0, 0), world.GetAgentPosition("Alice"));
    }

    [Fact]
    public void MoveAgent_InvalidDirection_ReturnsFalse()
    {
        var world = MakeWorldWithAgent("Alice", 10, 10);
        Assert.False(world.MoveAgent("Alice", "X"));
        Assert.False(world.MoveAgent("Alice", ""));
    }

    [Fact]
    public void MoveAgent_IsCaseInsensitive()
    {
        var world = MakeWorldWithAgent("Alice", 10, 15);
        Assert.True(world.MoveAgent("Alice", "n"));
        Assert.Equal((10, 14), world.GetAgentPosition("Alice"));
    }

    // ── GetAgentsInRadius ────────────────────────────────────────────────────

    [Fact]
    public void GetAgentsInRadius_IncludesAgentsWithinChebyshevDistance()
    {
        var world = MakeWorld();
        world.InitializeAgent("Center", 10, 10);
        world.InitializeAgent("Near",   11, 11); // distance 1 — included
        world.InitializeAgent("Far",    12, 12); // distance 2 — excluded

        var inRadius = world.GetAgentsInRadius(10, 10, radius: 1).Select(a => a.name).ToList();
        Assert.Contains("Center", inRadius);
        Assert.Contains("Near",   inRadius);
        Assert.DoesNotContain("Far", inRadius);
    }

    // ── InitializeItems ──────────────────────────────────────────────────────

    [Fact]
    public void InitializeItems_PlacesFixedStartItems()
    {
        var world = MakeWorldWithAgent("Alice");
        world.InitializeItems();

        // Fixed items placed around the starting apartment block (13,13)
        Assert.True(world.HasItemsAt(13, 13)); // first_aid_kit + water_bottle
        Assert.True(world.HasItemsAt(12, 13)); // canned_food + painkillers
        Assert.True(world.HasItemsAt(14, 13)); // wire_bundle
        Assert.True(world.HasItemsAt(12, 12)); // ham_radio
    }

    [Fact]
    public void InitializeItems_FixedItems_ContainExpectedTypes()
    {
        var world = MakeWorldWithAgent("Alice");
        world.InitializeItems();

        var at1313 = world.GetItemsAt(13, 13).Select(i => i.DefinitionId).ToList();
        Assert.Contains("first_aid_kit", at1313);
        Assert.Contains("water_bottle",  at1313);

        Assert.Contains("canned_food", world.GetItemsAt(12, 13).Select(i => i.DefinitionId));
        Assert.Contains("wire_bundle",  world.GetItemsAt(14, 13).Select(i => i.DefinitionId));
        Assert.Contains("ham_radio",    world.GetItemsAt(12, 12).Select(i => i.DefinitionId));
    }

    [Fact]
    public void InitializeItems_ScattersAtLeastSomeWorldItems()
    {
        var world = MakeWorldWithAgent("Alice");
        world.InitializeItems();

        var fixedPositions = new HashSet<(int, int)>
        {
            (13,13),(12,13),(14,13),(13,14),(14,12),(12,12),(11,13),(11,14),(14,14)
        };

        int scattered = 0;
        for (int x = 0; x < 30; x++)
            for (int y = 0; y < 30; y++)
                if (!fixedPositions.Contains((x, y)) && world.HasItemsAt(x, y))
                    scattered += world.GetItemsAt(x, y).Count;

        Assert.True(scattered > 0, "Expected some randomly scattered world items");
    }

    // ── TryPickUp ────────────────────────────────────────────────────────────

    [Fact]
    public void TryPickUp_ItemOnGround_MovesToInventory()
    {
        var world = MakeWorldWithAgent("Alice", 13, 13);
        world.InitializeItems();

        var itemOnGround = world.GetItemsAt(13, 13).First();
        var ok = world.TryPickUp("Alice", itemOnGround.InstanceId.ToString());

        Assert.True(ok);
        Assert.Contains(world.GetInventory("Alice"), i => i.InstanceId == itemOnGround.InstanceId);
        Assert.DoesNotContain(world.GetItemsAt(13, 13), i => i.InstanceId == itemOnGround.InstanceId);
    }

    [Fact]
    public void TryPickUp_ItemNotAtAgentPosition_ReturnsFalse()
    {
        var world = MakeWorldWithAgent("Alice", 5, 5);
        world.InitializeItems();

        // Items at (13,13) — agent is at (5,5)
        var farItem = world.GetItemsAt(13, 13).First();
        var ok = world.TryPickUp("Alice", farItem.InstanceId.ToString());

        Assert.False(ok);
        Assert.Empty(world.GetInventory("Alice"));
    }

    [Fact]
    public void TryPickUp_BogusId_ReturnsFalse()
    {
        var world = MakeWorldWithAgent("Alice", 13, 13);
        world.InitializeItems();

        Assert.False(world.TryPickUp("Alice", Guid.NewGuid().ToString()));
    }

    // ── TryDrop ──────────────────────────────────────────────────────────────

    [Fact]
    public void TryDrop_ItemInInventory_AppearsOnGround()
    {
        var world = MakeWorldWithAgent("Alice", 13, 13);
        world.InitializeItems();

        var item = world.GetItemsAt(13, 13).First();
        world.TryPickUp("Alice", item.InstanceId.ToString());

        // Move to a clear cell
        world.MoveAgent("Alice", "N"); // now at (13,12)
        var ok = world.TryDrop("Alice", item.InstanceId.ToString());

        Assert.True(ok);
        Assert.Empty(world.GetInventory("Alice"));
        Assert.Contains(world.GetItemsAt(13, 12), i => i.InstanceId == item.InstanceId);
    }

    [Fact]
    public void TryDrop_ItemNotInInventory_ReturnsFalse()
    {
        var world = MakeWorldWithAgent("Alice");
        Assert.False(world.TryDrop("Alice", Guid.NewGuid().ToString()));
    }

    // ── TryUse ───────────────────────────────────────────────────────────────

    [Fact]
    public void TryUse_UsableItem_ReturnsEffectAndConsumes()
    {
        var world = MakeWorldWithAgent("Alice", 13, 13);
        world.InitializeItems();

        var kit = world.GetItemsAt(13, 13).First(i => i.DefinitionId == "first_aid_kit");
        world.TryPickUp("Alice", kit.InstanceId.ToString());

        var effect = world.TryUse("Alice", kit.InstanceId.ToString());

        Assert.NotEmpty(effect);
        Assert.DoesNotContain(world.GetInventory("Alice"), i => i.InstanceId == kit.InstanceId);
    }

    [Fact]
    public void TryUse_NonUsableItem_ReturnsEmptyAndDoesNotConsume()
    {
        // blanket at (14,12) — deconstruct it (100% chance) to obtain fabric_strips (IsUsable=false)
        var world = MakeWorldWithAgent("Alice", 14, 12);
        world.InitializeItems();

        var blanket = world.GetItemsAt(14, 12).First(i => i.DefinitionId == "blanket");
        world.TryPickUp("Alice", blanket.InstanceId.ToString());
        var (_, ok, _) = world.TryDeconstruct("Alice", blanket.InstanceId.ToString());
        Assert.True(ok);

        var strip = world.GetInventory("Alice").First(i => i.DefinitionId == "fabric_strips");
        var effect = world.TryUse("Alice", strip.InstanceId.ToString());

        Assert.Empty(effect);
        Assert.Contains(world.GetInventory("Alice"), i => i.InstanceId == strip.InstanceId);
    }

    // ── TryGive ──────────────────────────────────────────────────────────────

    [Fact]
    public void TryGive_BothAgentsSameCell_TransfersItem()
    {
        var world = MakeWorldWithAgent("Alice", 13, 13);
        world.InitializeAgent("Bob", 13, 13);
        world.InitializeItems();

        var item = world.GetItemsAt(13, 13).First();
        world.TryPickUp("Alice", item.InstanceId.ToString());

        var ok = world.TryGive("Alice", item.InstanceId.ToString(), "Bob");

        Assert.True(ok);
        Assert.Empty(world.GetInventory("Alice"));
        Assert.Contains(world.GetInventory("Bob"), i => i.InstanceId == item.InstanceId);
    }

    [Fact]
    public void TryGive_AgentsOnDifferentCells_ReturnsFalse()
    {
        var world = MakeWorldWithAgent("Alice", 13, 13);
        world.InitializeAgent("Bob", 5, 5);
        world.InitializeItems();

        var item = world.GetItemsAt(13, 13).First();
        world.TryPickUp("Alice", item.InstanceId.ToString());

        var ok = world.TryGive("Alice", item.InstanceId.ToString(), "Bob");

        Assert.False(ok);
        Assert.Single(world.GetInventory("Alice")); // Alice still has it
    }

    // ── TryDeconstruct ───────────────────────────────────────────────────────

    [Fact]
    public void TryDeconstruct_DeconstructableItem_AlwaysConsumesItem()
    {
        // ham_radio at (12,12) has 50% chance — run 20 times to confirm item is
        // always consumed regardless of success or failure
        for (int i = 0; i < 20; i++)
        {
            var world = MakeWorldWithAgent("Alice", 12, 12);
            world.InitializeItems();
            var radio = world.GetItemsAt(12, 12).First(it => it.DefinitionId == "ham_radio");
            world.TryPickUp("Alice", radio.InstanceId.ToString());
            world.TryDeconstruct("Alice", radio.InstanceId.ToString());
            Assert.DoesNotContain(world.GetInventory("Alice"), it => it.InstanceId == radio.InstanceId);
        }
    }

    [Fact]
    public void TryDeconstruct_HighChanceItem_EventuallySucceeds()
    {
        // wire_bundle at (14,13) has 0.9 chance — should succeed within 30 attempts
        bool succeeded = false;
        for (int attempt = 0; attempt < 30 && !succeeded; attempt++)
        {
            var world = MakeWorldWithAgent("Alice", 14, 13);
            world.InitializeItems();
            var wire = world.GetItemsAt(14, 13).First(i => i.DefinitionId == "wire_bundle");
            world.TryPickUp("Alice", wire.InstanceId.ToString());
            var (_, ok, yields) = world.TryDeconstruct("Alice", wire.InstanceId.ToString());
            if (ok)
            {
                Assert.Contains("scrap_metal", yields);
                succeeded = true;
            }
        }
        Assert.True(succeeded, "wire_bundle (90% chance) never succeeded in 30 attempts");
    }

    [Fact]
    public void TryDeconstruct_NonDeconstructableItem_ReturnsFalseAndKeepsItem()
    {
        var world = MakeWorldWithAgent("Alice", 13, 13);
        world.InitializeItems();
        var kit = world.GetItemsAt(13, 13).First(i => i.DefinitionId == "first_aid_kit");
        world.TryPickUp("Alice", kit.InstanceId.ToString());

        var (consumed, ok, yields) = world.TryDeconstruct("Alice", kit.InstanceId.ToString());

        Assert.False(consumed);
        Assert.False(ok);
        Assert.Empty(yields);
        Assert.Contains(world.GetInventory("Alice"), i => i.InstanceId == kit.InstanceId);
    }

    [Fact]
    public void TryDeconstruct_DiceFailure_ConsumesItemButYieldsNothing()
    {
        // ham_radio has 0.5 chance — run enough times to hit a failure
        bool hitFailure = false;
        for (int attempt = 0; attempt < 100 && !hitFailure; attempt++)
        {
            var world = MakeWorldWithAgent("Alice", 12, 12);
            world.InitializeItems();
            var radio = world.GetItemsAt(12, 12).First(it => it.DefinitionId == "ham_radio");
            world.TryPickUp("Alice", radio.InstanceId.ToString());
            var (consumed, ok, yields) = world.TryDeconstruct("Alice", radio.InstanceId.ToString());
            if (consumed && !ok)
            {
                Assert.Empty(yields);
                Assert.DoesNotContain(world.GetInventory("Alice"), it => it.InstanceId == radio.InstanceId);
                hitFailure = true;
            }
        }
        Assert.True(hitFailure, "ham_radio (50% chance) never failed in 100 attempts");
    }

    // ── GetContext ────────────────────────────────────────────────────────────

    [Fact]
    public void GetContext_ContainsSituationText()
    {
        var world = MakeWorldWithAgent("Alice");
        var ctx = world.GetContext("Alice");
        Assert.Contains("Test situation.", ctx);
    }

    [Fact]
    public void GetContext_ContainsAgentPosition()
    {
        var world = MakeWorldWithAgent("Alice", 10, 15);
        var ctx = world.GetContext("Alice");
        Assert.Contains("(10, 15)", ctx);
    }

    [Fact]
    public void GetContext_ListsExits()
    {
        var world = MakeWorldWithAgent("Alice", 10, 15);
        var ctx = world.GetContext("Alice");
        Assert.Contains("EXITS:", ctx);
        // (10,15) is interior — all four directions valid
        Assert.Contains("N", ctx);
        Assert.Contains("S", ctx);
        Assert.Contains("E", ctx);
        Assert.Contains("W", ctx);
    }

    [Fact]
    public void GetContext_AtCorner_ShowsLimitedExits()
    {
        var world = MakeWorldWithAgent("Alice", 0, 0);
        var ctx = world.GetContext("Alice");
        // Only S and E are valid from (0,0)
        Assert.Contains("S", ctx);
        Assert.Contains("E", ctx);
    }

    [Fact]
    public void GetContext_ShowsInventorySection()
    {
        var world = MakeWorldWithAgent("Alice", 13, 13);
        world.InitializeItems();
        var item = world.GetItemsAt(13, 13).First();
        world.TryPickUp("Alice", item.InstanceId.ToString());

        var ctx = world.GetContext("Alice");
        Assert.Contains("YOUR INVENTORY (", ctx);
        Assert.Contains(item.InstanceId.ToString(), ctx);
    }

    [Fact]
    public void GetContext_ShowsItemsOnGround()
    {
        var world = MakeWorldWithAgent("Alice", 13, 13);
        world.InitializeItems();

        var ctx = world.GetContext("Alice");
        Assert.Contains("ITEMS HERE", ctx);
        // first_aid_kit and water_bottle are on the ground at (13,13)
        Assert.Contains("First Aid Kit", ctx);
    }

    [Fact]
    public void GetContext_EmptyInventory_ShowsEmptyLabel()
    {
        var world = MakeWorldWithAgent("Alice", 0, 0);
        var ctx = world.GetContext("Alice");
        Assert.Contains("(empty)", ctx);
    }

    [Fact]
    public void GetContext_UnknownAgent_ReturnsError()
    {
        var world = MakeWorld();
        Assert.Contains("ERROR", world.GetContext("Ghost"));
    }

    // ── MapWidth / MapHeight pass-through ────────────────────────────────────

    [Fact]
    public void MapDimensions_MatchUnderlyingGrid()
    {
        var world = MakeWorld();
        Assert.Equal(40, world.MapWidth);
        Assert.Equal(40, world.MapHeight);
    }

    // ── AddEvent + event log visibility ──────────────────────────────────────

    [Fact]
    public void AddEvent_Speech_AppearsInNearbyAgentContext()
    {
        var world = MakeWorld();
        world.InitializeAgent("Alice", 10, 10);
        world.InitializeAgent("Bob",   11, 10); // 1 cell away

        world.AddEvent("Alice", new MultiAgentSimWeb.Models.AgentAction { Speech = "Hello world!" });

        var bobCtx = world.GetContext("Bob");
        Assert.Contains("Hello world!", bobCtx);
    }

    [Fact]
    public void AddEvent_Speech_DoesNotAppearForDistantAgent()
    {
        var world = MakeWorld();
        world.InitializeAgent("Alice", 10, 10);
        world.InitializeAgent("Carol", 20, 20); // far away

        world.AddEvent("Alice", new MultiAgentSimWeb.Models.AgentAction { Speech = "Secret message." });

        var carolCtx = world.GetContext("Carol");
        Assert.DoesNotContain("Secret message.", carolCtx);
    }

    // ── Hunger / thirst ──────────────────────────────────────────────────────

    [Fact]
    public void InitializeAgent_StartsWithFullMeters()
    {
        var world = MakeWorldWithAgent("Alice");
        Assert.Equal(100f, world.GetHunger("Alice"));
        Assert.Equal(100f, world.GetThirst("Alice"));
    }

    [Fact]
    public void TickMeters_DecreasesHungerAndThirst()
    {
        var world = MakeWorldWithAgent("Alice");
        world.TickMeters("Alice");
        Assert.True(world.GetHunger("Alice") < 100f);
        Assert.True(world.GetThirst("Alice") < 100f);
    }

    [Fact]
    public void TickMeters_ThirstDropsFasterThanHunger()
    {
        var world = MakeWorldWithAgent("Alice");
        world.TickMeters("Alice");
        // Thirst decays at 3.5/turn, hunger at 2/turn
        Assert.True(world.GetThirst("Alice") < world.GetHunger("Alice"));
    }

    [Fact]
    public void TickMeters_ReturnsTrueWhenMeterHitsZero()
    {
        var world = MakeWorldWithAgent("Alice");
        bool died = false;
        // Thirst hits 0 after ~29 ticks (100 / 3.5 ≈ 28.6)
        for (int i = 0; i < 30; i++)
            died |= world.TickMeters("Alice");
        Assert.True(died);
    }

    [Fact]
    public void TickMeters_MetersDoNotGoBelowZero()
    {
        var world = MakeWorldWithAgent("Alice");
        // 50 ticks exhausts hunger (2/turn) and thirst (3.5/turn) both to zero
        for (int i = 0; i < 50; i++)
            world.TickMeters("Alice");
        Assert.Equal(0f, world.GetHunger("Alice"));
        Assert.Equal(0f, world.GetThirst("Alice"));
    }

    [Fact]
    public void TryUse_FoodItem_RestoresHunger()
    {
        // canned_food is placed at (12,13)
        var world = MakeWorldWithAgent("Alice", 12, 13);
        world.InitializeItems();

        for (int i = 0; i < 3; i++) world.TickMeters("Alice");
        float hungerBefore = world.GetHunger("Alice");

        var food = world.GetItemsAt(12, 13).First(i => i.DefinitionId == "canned_food");
        world.TryPickUp("Alice", food.InstanceId.ToString());
        world.TryUse("Alice", food.InstanceId.ToString());

        Assert.True(world.GetHunger("Alice") > hungerBefore);
    }

    [Fact]
    public void TryUse_WaterItem_RestoresThirst()
    {
        // water_bottle is placed at (13,13)
        var world = MakeWorldWithAgent("Alice", 13, 13);
        world.InitializeItems();

        for (int i = 0; i < 3; i++) world.TickMeters("Alice");
        float thirstBefore = world.GetThirst("Alice");

        var water = world.GetItemsAt(13, 13).First(i => i.DefinitionId == "water_bottle");
        world.TryPickUp("Alice", water.InstanceId.ToString());
        world.TryUse("Alice", water.InstanceId.ToString());

        Assert.True(world.GetThirst("Alice") > thirstBefore);
    }

    [Fact]
    public void TryUse_MetersCannotExceed100()
    {
        // water_bottle at (13,13) — use when meters are already at 100
        var world = MakeWorldWithAgent("Alice", 13, 13);
        world.InitializeItems();

        var water = world.GetItemsAt(13, 13).First(i => i.DefinitionId == "water_bottle");
        world.TryPickUp("Alice", water.InstanceId.ToString());
        world.TryUse("Alice", water.InstanceId.ToString()); // meters already at 100

        Assert.Equal(100f, world.GetThirst("Alice"));
    }

    // ── KillAgent ────────────────────────────────────────────────────────────

    [Fact]
    public void KillAgent_RemovesAgentFromTracking()
    {
        var world = MakeWorldWithAgent("Alice", 13, 13);
        world.KillAgent("Alice");
        Assert.Equal((-1, -1), world.GetAgentPosition("Alice"));
    }

    [Fact]
    public void KillAgent_DropsInventoryOnGround()
    {
        var world = MakeWorldWithAgent("Alice", 13, 13);
        world.InitializeItems();

        var item = world.GetItemsAt(13, 13).First();
        world.TryPickUp("Alice", item.InstanceId.ToString());

        world.MoveAgent("Alice", "N"); // (13, 12)
        world.KillAgent("Alice");

        Assert.Contains(world.GetItemsAt(13, 12), i => i.InstanceId == item.InstanceId);
    }

    // ── TryForage / scavenge ─────────────────────────────────────────────────

    [Fact]
    public void TryForage_OnStreet_ReturnsNull()
    {
        var world = MakeWorldWithAgent("Alice", 9, 5); // x=9 is a street column
        var result = world.TryForage("Alice");
        Assert.Null(result);
    }

    [Fact]
    public void TryForage_OnApartment_ReturnsNonNull()
    {
        // Run multiple times — at least one should return a result
        bool gotResult = false;
        for (int i = 0; i < 20 && !gotResult; i++)
        {
            var world = MakeWorldWithAgent("Alice", 13, 13); // Apartment
            var result = world.TryForage("Alice");
            if (result is not null) gotResult = true;
        }
        Assert.True(gotResult, "TryForage on Apartment should return a result at least occasionally");
    }

    [Fact]
    public void TryForage_CannotRaiseMetersAbove100()
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            var world = MakeWorldWithAgent("Alice", 13, 13); // Apartment
            world.TryForage("Alice");
            Assert.True(world.GetHunger("Alice") <= 100f);
            Assert.True(world.GetThirst("Alice") <= 100f);
        }
    }

    // ── GetContext — survival fields ─────────────────────────────────────────

    [Fact]
    public void GetContext_ShowsHungerAndThirst()
    {
        var world = MakeWorldWithAgent("Alice");
        var ctx = world.GetContext("Alice");
        Assert.Contains("HUNGER:", ctx);
        Assert.Contains("THIRST:", ctx);
    }

    [Fact]
    public void GetContext_ShowsWarningWhenMetersLow()
    {
        var world = MakeWorldWithAgent("Alice");
        // Thirst drops 3.5/turn — after 23 ticks: 100−80.5 = 19.5 ≤ 20 → IsCritical fires
        for (int i = 0; i < 23; i++) world.TickMeters("Alice");
        var ctx = world.GetContext("Alice");
        Assert.Contains("WARNING", ctx);
    }

    [Fact]
    public void GetContext_ShowsScavengeOnApartmentTerrain()
    {
        var world = MakeWorldWithAgent("Alice", 13, 13); // Apartment
        var ctx = world.GetContext("Alice");
        Assert.Contains("SCAVENGE", ctx);
        Assert.Contains("true", ctx);
    }

    [Fact]
    public void GetContext_ScavengeNotAvailableOnStreet()
    {
        var world = MakeWorldWithAgent("Alice", 9, 5); // Street column
        var ctx = world.GetContext("Alice");
        Assert.Contains("Not available here", ctx);
    }

    // ── InitializeItems — food and water at start ────────────────────────────

    [Fact]
    public void InitializeItems_PlacesFoodAndWaterAtStart()
    {
        var world = MakeWorldWithAgent("Alice");
        world.InitializeItems();

        var at1313 = world.GetItemsAt(13, 13).Select(i => i.DefinitionId).ToList();
        Assert.Contains("first_aid_kit", at1313);
        Assert.Contains("water_bottle",  at1313);
    }

    // ── Stranger / name-learning system ──────────────────────────────────────

    [Fact]
    public void KnowsName_AgentAlwaysKnowsOwnName()
    {
        var world = MakeWorldWithAgent("Alice");
        Assert.True(world.KnowsName("Alice", "Alice"));
    }

    [Fact]
    public void KnowsName_UnknownPeer_ReturnsFalse()
    {
        var world = MakeWorld();
        world.InitializeAgent("Alice", 10, 10);
        world.InitializeAgent("Bob", 11, 10);
        Assert.False(world.KnowsName("Alice", "Bob"));
    }

    [Fact]
    public void LearnName_MakesKnowsNameTrue()
    {
        var world = MakeWorld();
        world.InitializeAgent("Alice", 10, 10);
        world.InitializeAgent("Bob", 11, 10);
        world.LearnName("Alice", "Bob");
        Assert.True(world.KnowsName("Alice", "Bob"));
    }

    [Fact]
    public void DescribeAgent_UnknownPeer_ReturnsAnonymousLabel()
    {
        var world = MakeWorld();
        world.InitializeAgent("Alice", 10, 10);
        world.InitializeAgent("Bob", 11, 10);
        Assert.Equal("an unknown person", world.DescribeAgent("Alice", "Bob"));
    }

    [Fact]
    public void DescribeAgent_KnownPeer_ReturnsName()
    {
        var world = MakeWorld();
        world.InitializeAgent("Alice", 10, 10);
        world.InitializeAgent("Bob", 11, 10);
        world.LearnName("Alice", "Bob");
        Assert.Equal("Bob", world.DescribeAgent("Alice", "Bob"));
    }

    [Fact]
    public void AddEvent_SelfIntroduction_NearbyAgentLearnsName()
    {
        var world = MakeWorld();
        // Use outdoor (street) cells so radius-1 proximity applies
        world.InitializeAgent("Alice", 0, 0);
        world.InitializeAgent("Bob",   1, 0); // 1 cell away on street

        Assert.False(world.KnowsName("Bob", "Alice"));
        world.AddEvent("Alice", new MultiAgentSimWeb.Models.AgentAction { Speech = "Hi, I'm Alice, nice to meet you." });
        Assert.True(world.KnowsName("Bob", "Alice"));
    }

    [Fact]
    public void AddEvent_SpeechWithoutName_DoesNotRevealName()
    {
        var world = MakeWorld();
        world.InitializeAgent("Alice", 0, 0);
        world.InitializeAgent("Bob",   1, 0); // within earshot

        world.AddEvent("Alice", new MultiAgentSimWeb.Models.AgentAction { Speech = "Is anyone out there?" });
        Assert.False(world.KnowsName("Bob", "Alice"));
    }

    [Fact]
    public void AddEvent_Introduction_TooFarAway_DoesNotLearnName()
    {
        var world = MakeWorld();
        world.InitializeAgent("Alice", 0, 0);
        world.InitializeAgent("Bob",   9, 9); // 9 cells away, out of range

        world.AddEvent("Alice", new MultiAgentSimWeb.Models.AgentAction { Speech = "I'm Alice!" });
        Assert.False(world.KnowsName("Bob", "Alice"));
    }

    // ── Water sources ─────────────────────────────────────────────────────────

    [Fact]
    public void TryDrinkRiver_OnRiverTile_RestoresThirst()
    {
        // River tiles: (28-35, 19-26)
        var world = MakeWorldWithAgent("Alice", 30, 20);
        for (int i = 0; i < 5; i++) world.TickMeters("Alice");
        float before = world.GetThirst("Alice");

        var result = world.TryDrinkRiver("Alice");

        Assert.NotNull(result);
        Assert.True(world.GetThirst("Alice") > before);
    }

    [Fact]
    public void TryDrinkRiver_NotOnRiver_ReturnsFailureMessage()
    {
        var world = MakeWorldWithAgent("Alice", 13, 13); // Apartment, not river
        var result = world.TryDrinkRiver("Alice");
        Assert.NotNull(result); // failure message
        Assert.Contains("river", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryDrinkFountain_AtFountain_RestoresThirst()
    {
        // Riverside Fountain: (3-6, 3-6) → park terrain with fountain name
        var world = MakeWorldWithAgent("Alice", 4, 4);
        for (int i = 0; i < 5; i++) world.TickMeters("Alice");
        float before = world.GetThirst("Alice");

        var result = world.TryDrinkFountain("Alice");

        Assert.NotNull(result);
        Assert.True(world.GetThirst("Alice") > before);
    }

    [Fact]
    public void TryDrinkFountain_NotAtFountain_ReturnsFailureMessage()
    {
        var world = MakeWorldWithAgent("Alice", 13, 13); // Apartment, not fountain
        var result = world.TryDrinkFountain("Alice");
        Assert.NotNull(result);
        Assert.Contains("fountain", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryDrinkRiver_CannotExceedMaxThirst()
    {
        var world = MakeWorldWithAgent("Alice", 30, 20); // River tile
        world.TryDrinkRiver("Alice"); // meters already at 100
        Assert.Equal(100f, world.GetThirst("Alice"));
    }

    // ── Fill containers ───────────────────────────────────────────────────────

    [Fact]
    public void TryFillContainer_TinCanAtRiver_ReturnsFilledItem()
    {
        // tin_can is placed at (33,24) which is a River tile
        var world = MakeWorldWithAgent("Alice", 33, 24);
        world.InitializeItems();

        var can = world.GetItemsAt(33, 24).First(i => i.DefinitionId == "tin_can");
        world.TryPickUp("Alice", can.InstanceId.ToString());

        var result = world.TryFillContainer("Alice", can.InstanceId.ToString());

        Assert.NotNull(result);
        Assert.Contains(world.GetInventory("Alice"), i => i.DefinitionId == "filled_tin_can");
        Assert.DoesNotContain(world.GetInventory("Alice"), i => i.InstanceId == can.InstanceId);
    }

    [Fact]
    public void TryFillContainer_NoWaterSource_ReturnsFailureMessage()
    {
        // tin_can placed at (11,12) is Apartment terrain — but we move to a Street tile after picking it up
        var world = MakeWorldWithAgent("Alice", 11, 12);
        world.InitializeItems();

        var can = world.GetItemsAt(11, 12).First(i => i.DefinitionId == "tin_can");
        world.TryPickUp("Alice", can.InstanceId.ToString());

        // Move to a street tile (x=9 is always street)
        world.MoveAgent("Alice", "W"); // 10,12
        world.MoveAgent("Alice", "W"); // 9,12 — street column

        var result = world.TryFillContainer("Alice", can.InstanceId.ToString());

        Assert.NotNull(result);
        Assert.Contains("no water source", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryFillContainer_TinCanAtFountain_ReturnsFilledItem()
    {
        // tin_can placed at (3,4) which is within the Riverside Fountain zone (3-6, 3-6)
        var world = MakeWorldWithAgent("Alice", 3, 4);
        world.InitializeItems();

        var can = world.GetItemsAt(3, 4).First(i => i.DefinitionId == "tin_can");
        world.TryPickUp("Alice", can.InstanceId.ToString());

        var result = world.TryFillContainer("Alice", can.InstanceId.ToString());

        Assert.NotNull(result);
        Assert.Contains(world.GetInventory("Alice"), i => i.DefinitionId == "filled_tin_can");
    }

    // ── Forest forage ─────────────────────────────────────────────────────────

    [Fact]
    public void TryForage_ForestTerrain_ReturnsResultEventually()
    {
        // Forest tiles: (1-8, 28-35) = Greenwood Forest
        bool gotResult = false;
        for (int i = 0; i < 30 && !gotResult; i++)
        {
            var world = MakeWorldWithAgent("Alice", 4, 30);
            var result = world.TryForage("Alice");
            if (result is not null) gotResult = true;
        }
        Assert.True(gotResult, "TryForage on Forest should return a result at least occasionally");
    }

    [Fact]
    public void TryForage_ForestTerrain_CannotRaiseMetersAbove100()
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            var world = MakeWorldWithAgent("Alice", 4, 30); // Greenwood Forest
            world.TryForage("Alice");
            Assert.True(world.GetHunger("Alice") <= 100f);
            Assert.True(world.GetThirst("Alice") <= 100f);
        }
    }

    [Fact]
    public void TryForage_RiverTerrain_ReturnsNull()
    {
        var world = MakeWorldWithAgent("Alice", 30, 20); // River tile
        var result = world.TryForage("Alice");
        Assert.Null(result);
    }
}
