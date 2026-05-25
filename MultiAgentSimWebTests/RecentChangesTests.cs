using MultiAgentSimWeb.Models;
using MultiAgentSimWeb.Services;

namespace MultiAgentSimWebTests;

// ── Helpers shared across all classes ────────────────────────────────────────

file static class Helpers
{
    public static WorldState MakeWorld()
    {
        var map = MapGrid.CreateDefault();
        return new WorldState("Test situation.", map);
    }

    public static WorldState MakeWorldWithAgent(string name = "Alice", int x = 10, int y = 15)
    {
        var world = MakeWorld();
        world.InitializeAgent(name, x, y);
        return world;
    }

    public static WorldState MakeWorldWithAgents(string a, string b, int ax, int ay, int bx, int by)
    {
        var world = MakeWorld();
        world.InitializeAgent(a, ax, ay);
        world.InitializeAgent(b, bx, by);
        world.LearnName(a, b);
        world.LearnName(b, a);
        return world;
    }
}

// ── Trust decay (AgentMood.Decay) ─────────────────────────────────────────────

public class TrustDecayTests
{
    [Fact]
    public void Decay_PositiveTrust_MovesTowardZero()
    {
        var mood = new AgentMood();
        mood.AdjustTrust("Bob", 50f);
        mood.Decay();
        Assert.True(mood.GetTrust("Bob") < 50f);
    }

    [Fact]
    public void Decay_NegativeTrust_MovesTowardZero()
    {
        var mood = new AgentMood();
        mood.AdjustTrust("Bob", -50f);
        mood.Decay();
        Assert.True(mood.GetTrust("Bob") > -50f);
    }

    [Fact]
    public void Decay_TrustBetweenThresholds_SnapsToZero()
    {
        var mood = new AgentMood();
        mood.AdjustTrust("Bob", 0.3f);   // within ±0.5 dead zone
        mood.Decay();
        Assert.Equal(0f, mood.GetTrust("Bob"));
    }

    [Fact]
    public void Decay_NegativeTrustBetweenThresholds_SnapsToZero()
    {
        var mood = new AgentMood();
        mood.AdjustTrust("Bob", -0.3f);
        mood.Decay();
        Assert.Equal(0f, mood.GetTrust("Bob"));
    }

    [Fact]
    public void Decay_HighTrustEventuallyReachesZero()
    {
        var mood = new AgentMood();
        mood.AdjustTrust("Bob", 100f);
        for (int i = 0; i < 250; i++) mood.Decay();
        Assert.Equal(0f, mood.GetTrust("Bob"));
    }

    [Fact]
    public void Decay_DecaysAtExpectedRatePerTurn()
    {
        var mood = new AgentMood();
        mood.AdjustTrust("Bob", 10f);
        mood.Decay();
        // Each turn above 0.5 loses exactly 0.5
        Assert.Equal(9.5f, mood.GetTrust("Bob"), precision: 4);
    }
}

// ── Crafting failure ──────────────────────────────────────────────────────────

public class CraftingFailureTests
{
    // make_shiv requires scrap_metal + duct_tape
    private const string RecipeId    = "make_shiv";
    private const string Ingredient1 = "scrap_metal";
    private const string Ingredient2 = "duct_tape";

    private static WorldState SetupCrafter(int resourcefulness, string skill = "")
    {
        var world = Helpers.MakeWorldWithAgent("Alice", 10, 15);
        world.SetPersonality("Alice", new PersonalityProfile
        {
            AgentName     = "Alice",
            Resourcefulness = resourcefulness,
            BackgroundSkill = skill
        });
        world.Items.AddToInventory("Alice", Ingredient1);
        world.Items.AddToInventory("Alice", Ingredient2);
        return world;
    }

    [Fact]
    public void Craft_HighResourcefulness_NeverFails()
    {
        // Resourcefulness=50 → failChance=0 (threshold is <40) — should never fail
        for (int i = 0; i < 100; i++)
        {
            var world = SetupCrafter(50);
            var result = world.TryCraft("Alice", RecipeId);
            Assert.NotNull(result);
            Assert.DoesNotContain("botch", result, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Craft_CraftingExpert_NeverFails()
    {
        // crafting_expert is immune regardless of Resourcefulness
        for (int i = 0; i < 100; i++)
        {
            var world = SetupCrafter(0, "crafting_expert");
            var result = world.TryCraft("Alice", RecipeId);
            Assert.NotNull(result);
            Assert.DoesNotContain("botch", result, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Craft_VeryLowResourcefulness_EventuallyFails()
    {
        // Resourcefulness=0 → 20% fail chance; should trigger within 100 attempts
        bool failed = false;
        for (int i = 0; i < 100 && !failed; i++)
        {
            var world = SetupCrafter(0);
            var result = world.TryCraft("Alice", RecipeId);
            if (result != null && result.Contains("botch", StringComparison.OrdinalIgnoreCase))
                failed = true;
        }
        Assert.True(failed, "Expected at least one failure with Resourcefulness=0 in 100 attempts");
    }

    [Fact]
    public void Craft_OnFailure_IngredientsAreConsumed()
    {
        // Force a failure by running until one occurs
        for (int i = 0; i < 200; i++)
        {
            var world = SetupCrafter(0);
            var result = world.TryCraft("Alice", RecipeId);
            if (result != null && result.Contains("botch", StringComparison.OrdinalIgnoreCase))
            {
                var inv = world.GetInventory("Alice");
                Assert.DoesNotContain(inv, item => item.DefinitionId == Ingredient1);
                Assert.DoesNotContain(inv, item => item.DefinitionId == Ingredient2);
                return;
            }
        }
        Assert.Fail("Could not trigger a crafting failure in 200 attempts");
    }

    [Fact]
    public void Craft_OnSuccess_ResultAppearsInInventory()
    {
        var world = SetupCrafter(50);
        var result = world.TryCraft("Alice", RecipeId);
        Assert.NotNull(result);
        Assert.Contains(world.GetInventory("Alice"), item => item.DefinitionId == "shiv");
    }

    [Fact]
    public void Craft_OnSuccess_IngredientsAreConsumed()
    {
        var world = SetupCrafter(50);
        world.TryCraft("Alice", RecipeId);
        var inv = world.GetInventory("Alice");
        // Ingredients consumed — only the result remains
        Assert.DoesNotContain(inv, item => item.DefinitionId == Ingredient1);
        Assert.DoesNotContain(inv, item => item.DefinitionId == Ingredient2);
    }

    [Fact]
    public void Craft_OnFailure_NegativeMoodHit()
    {
        for (int i = 0; i < 200; i++)
        {
            var world = SetupCrafter(0);
            float moodBefore = world.GetMood("Alice").Mood;
            var result = world.TryCraft("Alice", RecipeId);
            if (result != null && result.Contains("botch", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(world.GetMood("Alice").Mood < moodBefore,
                    "Mood should drop on crafting failure");
                return;
            }
        }
        Assert.Fail("Could not trigger a crafting failure in 200 attempts");
    }
}

// ── Boredom ───────────────────────────────────────────────────────────────────

public class BoredomTests
{
    [Fact]
    public void RecordActivity_Active_IdleCountIsZero()
    {
        var world = Helpers.MakeWorldWithAgent("Alice");
        world.RecordActivity("Alice", wasActive: true);
        Assert.Equal(0, world.GetIdleTurns("Alice"));
    }

    [Fact]
    public void RecordActivity_Idle_IncrementsCounter()
    {
        var world = Helpers.MakeWorldWithAgent("Alice");
        world.RecordActivity("Alice", wasActive: false);
        world.RecordActivity("Alice", wasActive: false);
        Assert.Equal(2, world.GetIdleTurns("Alice"));
    }

    [Fact]
    public void RecordActivity_ActiveAfterIdle_ResetsCounter()
    {
        var world = Helpers.MakeWorldWithAgent("Alice");
        world.RecordActivity("Alice", wasActive: false);
        world.RecordActivity("Alice", wasActive: false);
        world.RecordActivity("Alice", wasActive: false);
        Assert.Equal(3, world.GetIdleTurns("Alice"));

        world.RecordActivity("Alice", wasActive: true);
        Assert.Equal(0, world.GetIdleTurns("Alice"));
    }

    [Fact]
    public void RecordActivity_LessThan3IdleTurns_NoMoodPenalty()
    {
        var world = Helpers.MakeWorldWithAgent("Alice");
        float moodBefore = world.GetMood("Alice").Mood;

        world.RecordActivity("Alice", wasActive: false);
        world.RecordActivity("Alice", wasActive: false);   // 2 idle — no penalty yet

        Assert.Equal(moodBefore, world.GetMood("Alice").Mood);
    }

    [Fact]
    public void RecordActivity_3IdleTurns_MoodStartsDropping()
    {
        var world = Helpers.MakeWorldWithAgent("Alice");
        float moodBefore = world.GetMood("Alice").Mood;

        world.RecordActivity("Alice", wasActive: false);
        world.RecordActivity("Alice", wasActive: false);
        world.RecordActivity("Alice", wasActive: false);   // 3 idle — penalty kicks in

        Assert.True(world.GetMood("Alice").Mood < moodBefore,
            "Mood should drop after 3 consecutive idle turns");
    }

    [Fact]
    public void RecordActivity_MoodPenaltyScalesWithIdleTurns()
    {
        // 5 idle turns should result in a larger mood drop than 3 idle turns
        var worldFew = Helpers.MakeWorldWithAgent("Alice");
        var worldMany = Helpers.MakeWorldWithAgent("Alice");

        float moodStart = worldFew.GetMood("Alice").Mood;

        for (int i = 0; i < 3; i++) worldFew.RecordActivity("Alice", wasActive: false);
        for (int i = 0; i < 7; i++) worldMany.RecordActivity("Alice", wasActive: false);

        Assert.True(worldMany.GetMood("Alice").Mood < worldFew.GetMood("Alice").Mood,
            "7 idle turns should produce a larger mood penalty than 3");
    }

    [Fact]
    public void GetContext_IdleFor3Turns_ShowsBoredomNote()
    {
        var world = Helpers.MakeWorldWithAgent("Alice");
        world.RecordActivity("Alice", wasActive: false);
        world.RecordActivity("Alice", wasActive: false);
        world.RecordActivity("Alice", wasActive: false);

        var ctx = world.GetContext("Alice");
        Assert.Contains("idle", ctx, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetContext_ActiveAgent_NoBoredomNote()
    {
        var world = Helpers.MakeWorldWithAgent("Alice");
        world.RecordActivity("Alice", wasActive: true);

        var ctx = world.GetContext("Alice");
        Assert.DoesNotContain("idle for", ctx, StringComparison.OrdinalIgnoreCase);
    }
}

// ── Stamina ───────────────────────────────────────────────────────────────────

public class StaminaTests
{
    [Fact]
    public void InitializeAgent_StartsWithFullStamina()
    {
        var world = Helpers.MakeWorldWithAgent("Alice");
        Assert.Equal(100f, world.GetStamina("Alice"));
    }

    [Fact]
    public void MoveAgent_DrainsStamina()
    {
        var world = Helpers.MakeWorldWithAgent("Alice", 10, 15);
        float before = world.GetStamina("Alice");
        world.MoveAgent("Alice", "N");
        Assert.True(world.GetStamina("Alice") < before);
    }

    [Fact]
    public void IsExhausted_WhenStaminaBelowThreshold_ReturnsTrue()
    {
        var world = Helpers.MakeWorldWithAgent("Alice", 10, 15);
        // Each move costs 2.5 stamina; 34 moves → 100 - 85 = 15 stamina (below threshold of 20)
        for (int i = 0; i < 34; i++)
        {
            world.MoveAgent("Alice", i % 2 == 0 ? "N" : "S");
        }
        Assert.True(world.IsExhausted("Alice"));
    }

    [Fact]
    public void IsExhausted_FullStamina_ReturnsFalse()
    {
        var world = Helpers.MakeWorldWithAgent("Alice");
        Assert.False(world.IsExhausted("Alice"));
    }

    [Fact]
    public void GetContext_ShowsStaminaSection()
    {
        var world = Helpers.MakeWorldWithAgent("Alice");
        var ctx = world.GetContext("Alice");
        Assert.Contains("STAMINA:", ctx);
    }

    [Fact]
    public void GetContext_ExhaustedAgent_ShowsWarning()
    {
        var world = Helpers.MakeWorldWithAgent("Alice", 10, 15);
        for (int i = 0; i < 34; i++) world.MoveAgent("Alice", i % 2 == 0 ? "N" : "S");
        var ctx = world.GetContext("Alice");
        Assert.Contains("exhausted", ctx, StringComparison.OrdinalIgnoreCase);
    }
}

// ── Theft detection ───────────────────────────────────────────────────────────

public class TheftDetectionTests
{
    [Fact]
    public void PickUp_OwnDroppedItem_NoTrustChange()
    {
        var world = Helpers.MakeWorldWithAgent("Alice", 10, 10);
        world.InitializeItems();

        var item = world.GetItemsAt(13, 13).First();
        // Alice picks up then drops at her location
        world.MoveAgent("Alice", "S"); world.MoveAgent("Alice", "S");
        world.MoveAgent("Alice", "S"); world.MoveAgent("Alice", "S");
        world.MoveAgent("Alice", "S"); // 10,20 — arbitrary clear spot

        // Place an item, pick it up, drop it, pick it up again — same agent, no theft
        world.Items.AddToInventory("Alice", "water_bottle");
        var owned = world.GetInventory("Alice").First(i => i.DefinitionId == "water_bottle");
        world.TryDrop("Alice", owned.InstanceId.ToString());
        float trustBefore = world.GetMood("Alice").GetTrust("Alice");
        world.TryPickUp("Alice", owned.InstanceId.ToString());

        Assert.Equal(trustBefore, world.GetMood("Alice").GetTrust("Alice"));
    }

    [Fact]
    public void PickUp_DroppedByOtherAgent_DropperLosesTrustWhenWatching()
    {
        // Alice and Bob adjacent; Alice drops item; Bob picks it up while Alice watches
        var world = Helpers.MakeWorldWithAgents("Alice", "Bob", 10, 10, 10, 10);

        world.Items.AddToInventory("Alice", "water_bottle");
        var item = world.GetInventory("Alice").First(i => i.DefinitionId == "water_bottle");
        world.TryDrop("Alice", item.InstanceId.ToString());

        float trustBefore = world.GetMood("Alice").GetTrust("Bob");
        world.TryPickUp("Bob", item.InstanceId.ToString());

        Assert.True(world.GetMood("Alice").GetTrust("Bob") < trustBefore,
            "Alice should lose trust in Bob for taking her item while she watched");
    }

    [Fact]
    public void PickUp_DroppedItem_DropperNotNearby_NoTrustChange()
    {
        // Alice drops item at (10,10), then moves far away; Bob picks it up
        var world = Helpers.MakeWorldWithAgents("Alice", "Bob", 10, 10, 10, 10);

        world.Items.AddToInventory("Alice", "water_bottle");
        var item = world.GetInventory("Alice").First(i => i.DefinitionId == "water_bottle");
        world.TryDrop("Alice", item.InstanceId.ToString());

        // Teleport Alice far away by re-initialising her position isn't straightforward,
        // so instead we check that the _logic_ path used here does NOT apply the theft
        // penalty when the dropper is on a completely different tile out of view.
        // We verify by placing Alice 10 cells away, well outside visible range.
        world.MoveAgent("Alice", "N"); world.MoveAgent("Alice", "N");
        world.MoveAgent("Alice", "N"); world.MoveAgent("Alice", "N");
        world.MoveAgent("Alice", "N"); world.MoveAgent("Alice", "N");
        world.MoveAgent("Alice", "N"); world.MoveAgent("Alice", "N");
        world.MoveAgent("Alice", "N"); world.MoveAgent("Alice", "N"); // Alice now 10 cells north

        float trustBefore = world.GetMood("Alice").GetTrust("Bob");
        world.TryPickUp("Bob", item.InstanceId.ToString());

        Assert.True(trustBefore == world.GetMood("Alice").GetTrust("Bob"),
            "No trust penalty when dropper cannot see the thief");
    }
}

// ── Eating while neighbor is starving ─────────────────────────────────────────

public class EatingWhileStarvingTests
{
    [Fact]
    public void EatFood_StarvingNeighborNearby_NeighborLosesTrust()
    {
        // Alice eats food; Bob is adjacent and starving
        var world = Helpers.MakeWorldWithAgents("Alice", "Bob", 10, 10, 10, 10);

        // Drain Bob to starvation
        for (int i = 0; i < 41; i++) world.TickMeters("Bob");   // hunger ~100 - 41*2 = 18 < 20
        Assert.True(world.GetHunger("Bob") < 20f);

        world.Items.AddToInventory("Alice", "canned_food");
        var food = world.GetInventory("Alice").First(i => i.DefinitionId == "canned_food");

        float trustBefore = world.GetMood("Bob").GetTrust("Alice");
        world.TryUse("Alice", food.InstanceId.ToString());

        Assert.True(world.GetMood("Bob").GetTrust("Alice") < trustBefore,
            "Starving Bob should lose trust in Alice for eating in front of him");
    }

    [Fact]
    public void EatFood_NeighborNotStarving_NoTrustPenalty()
    {
        // Bob is well-fed — eating Alice's food should not affect him
        var world = Helpers.MakeWorldWithAgents("Alice", "Bob", 10, 10, 10, 10);

        Assert.True(world.GetHunger("Bob") >= 20f, "Bob should not be starving at start");

        world.Items.AddToInventory("Alice", "canned_food");
        var food = world.GetInventory("Alice").First(i => i.DefinitionId == "canned_food");

        float trustBefore = world.GetMood("Bob").GetTrust("Alice");
        world.TryUse("Alice", food.InstanceId.ToString());

        Assert.True(trustBefore == world.GetMood("Bob").GetTrust("Alice"),
            "Non-starving Bob should not care that Alice ate");
    }

    [Fact]
    public void EatFood_ProtectsOthersFlag_LargerTrustPenalty()
    {
        // protects_others witness should lose MORE trust than a neutral witness
        var worldNeutral   = Helpers.MakeWorldWithAgents("Alice", "Bob", 10, 10, 10, 10);
        var worldProtector = Helpers.MakeWorldWithAgents("Alice", "Carol", 10, 10, 10, 10);

        worldProtector.SetPersonality("Carol", new PersonalityProfile
        {
            AgentName = "Carol",
            Flags     = ["protects_others"]
        });

        // Drain both witnesses to starvation
        for (int i = 0; i < 41; i++)
        {
            worldNeutral.TickMeters("Bob");
            worldProtector.TickMeters("Carol");
        }

        worldNeutral.Items.AddToInventory("Alice", "canned_food");
        worldProtector.Items.AddToInventory("Alice", "canned_food");
        var foodNeutral   = worldNeutral.GetInventory("Alice").First(i => i.DefinitionId == "canned_food");
        var foodProtector = worldProtector.GetInventory("Alice").First(i => i.DefinitionId == "canned_food");

        worldNeutral.TryUse("Alice", foodNeutral.InstanceId.ToString());
        worldProtector.TryUse("Alice", foodProtector.InstanceId.ToString());

        Assert.True(
            worldProtector.GetMood("Carol").GetTrust("Alice") < worldNeutral.GetMood("Bob").GetTrust("Alice"),
            "protects_others Carol should lose more trust than neutral Bob");
    }

    [Fact]
    public void EatFood_SelfReliantFlag_SmallerTrustPenalty()
    {
        // self_reliant witness should lose LESS trust than a neutral witness
        var worldNeutral    = Helpers.MakeWorldWithAgents("Alice", "Bob",  10, 10, 10, 10);
        var worldSelfReliant = Helpers.MakeWorldWithAgents("Alice", "Dave", 10, 10, 10, 10);

        worldSelfReliant.SetPersonality("Dave", new PersonalityProfile
        {
            AgentName = "Dave",
            Flags     = ["self_reliant"]
        });

        for (int i = 0; i < 41; i++)
        {
            worldNeutral.TickMeters("Bob");
            worldSelfReliant.TickMeters("Dave");
        }

        worldNeutral.Items.AddToInventory("Alice", "canned_food");
        worldSelfReliant.Items.AddToInventory("Alice", "canned_food");
        var foodNeutral    = worldNeutral.GetInventory("Alice").First(i => i.DefinitionId == "canned_food");
        var foodSelfReliant = worldSelfReliant.GetInventory("Alice").First(i => i.DefinitionId == "canned_food");

        worldNeutral.TryUse("Alice", foodNeutral.InstanceId.ToString());
        worldSelfReliant.TryUse("Alice", foodSelfReliant.InstanceId.ToString());

        Assert.True(
            worldSelfReliant.GetMood("Dave").GetTrust("Alice") > worldNeutral.GetMood("Bob").GetTrust("Alice"),
            "self_reliant Dave should lose less trust than neutral Bob");
    }
}
