using MultiAgentSimWeb.Models;
using MultiAgentSimWeb.Services;

namespace MultiAgentSimWebTests;

public class ActionResolverResolveTests
{
    private const string Color = "#aabbcc";

    private static WorldState MakeWorld(string agent = "Alice", int x = 13, int y = 13)
    {
        var world = new WorldState("Test.", MapGrid.CreateDefault());
        world.InitializeAgent(agent, x, y);
        return world;
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_ValidMoveTo_EmitsMoveEvent()
    {
        var world  = MakeWorld("Alice", 13, 13);
        var action = new AgentAction { MoveTo = "N" };

        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();

        Assert.Contains(events, e => e.Type == "move");
    }

    [Fact]
    public void Resolve_ValidMoveTo_NewPositionInEventContent()
    {
        var world  = MakeWorld("Alice", 13, 13);
        var action = new AgentAction { MoveTo = "N" };

        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();
        var mv     = events.Single(e => e.Type == "move");

        // After moving north from (13,13) agent should be at (13,12)
        Assert.Contains("13,12", mv.Content);
    }

    [Fact]
    public void Resolve_InvalidMoveTo_NoMoveEvent()
    {
        var world  = MakeWorld("Alice", 13, 13);
        var action = new AgentAction { MoveTo = "X" };

        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();

        Assert.DoesNotContain(events, e => e.Type == "move");
        Assert.Equal((13, 13), world.GetAgentPosition("Alice")); // position unchanged
    }

    [Fact]
    public void Resolve_MoveOffEdge_NoMoveEvent()
    {
        var world  = MakeWorld("Alice", 0, 0); // top-left corner
        var action = new AgentAction { MoveTo = "N" };

        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();

        Assert.DoesNotContain(events, e => e.Type == "move");
    }

    // ── Scavenge ──────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_Scavenge_OnStreet_NoScavengeEvent()
    {
        // Column x=9 is always Street terrain
        var world  = MakeWorld("Alice", 9, 5);
        var action = new AgentAction { Scavenge = true };

        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();

        Assert.DoesNotContain(events, e => e.Type == "scavenge");
    }

    [Fact]
    public void Resolve_Scavenge_OnApartment_EmitsScavengeEventEventually()
    {
        bool gotEvent = false;
        for (int i = 0; i < 30 && !gotEvent; i++)
        {
            var world  = MakeWorld("Alice", 13, 13); // Apartment
            var action = new AgentAction { Scavenge = true };
            var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();
            if (events.Any(e => e.Type == "scavenge"))
                gotEvent = true;
        }
        Assert.True(gotEvent, "Expected a scavenge event on Apartment terrain within 30 attempts");
    }

    // ── DrinkTap ─────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DrinkTap_OnApartment_EmitsDrinkTapEvent()
    {
        var world  = MakeWorld("Alice", 13, 13); // Apartment, tap available early rounds
        world.CurrentRound = 1;
        var action = new AgentAction { DrinkTap = true };

        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();

        Assert.Contains(events, e => e.Type == "drink_tap");
    }

    [Fact]
    public void Resolve_DrinkTap_OnStreet_EmitsEventWithFailureContent()
    {
        // TryDrinkTap always returns non-null (success or failure message),
        // so the event is always emitted — but on a non-tap tile the content
        // describes the failure.
        var world  = MakeWorld("Alice", 9, 5); // Street
        var action = new AgentAction { DrinkTap = true };

        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();

        var ev = Assert.Single(events, e => e.Type == "drink_tap");
        // Failure messages mention "tap" or "no" in some form
        Assert.False(string.IsNullOrEmpty(ev.Content));
    }

    // ── Item actions ─────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_PickUp_ValidItem_EmitsItemEvent()
    {
        var world = MakeWorld("Alice", 13, 13);
        world.InitializeItems();
        var groundItem = world.GetItemsAt(13, 13).First();
        var action = new AgentAction
        {
            ItemAction   = "pick_up",
            ItemTargetId = groundItem.InstanceId.ToString()
        };

        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();

        Assert.Contains(events, e => e.Type == "item");
        Assert.Contains(world.GetInventory("Alice"), i => i.InstanceId == groundItem.InstanceId);
    }

    [Fact]
    public void Resolve_PickUp_InvalidId_NoItemEvent()
    {
        var world  = MakeWorld("Alice", 13, 13);
        var action = new AgentAction
        {
            ItemAction   = "pick_up",
            ItemTargetId = Guid.NewGuid().ToString()
        };

        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();

        Assert.DoesNotContain(events, e => e.Type == "item");
    }

    [Fact]
    public void Resolve_Drop_ItemInInventory_EmitsItemEventAndLandsOnGround()
    {
        var world = MakeWorld("Alice", 13, 13);
        world.InitializeItems();
        var groundItem = world.GetItemsAt(13, 13).First();
        world.TryPickUp("Alice", groundItem.InstanceId.ToString());
        world.MoveAgent("Alice", "N"); // now at (13,12) — clear cell

        var action = new AgentAction
        {
            ItemAction   = "drop",
            ItemTargetId = groundItem.InstanceId.ToString()
        };
        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();

        Assert.Contains(events, e => e.Type == "item");
        Assert.DoesNotContain(world.GetInventory("Alice"), i => i.InstanceId == groundItem.InstanceId);
        Assert.Contains(world.GetItemsAt(13, 12), i => i.InstanceId == groundItem.InstanceId);
    }

    [Fact]
    public void Resolve_Give_ToAgentOnSameCell_EmitsItemEvent()
    {
        var world = new WorldState("Test.", MapGrid.CreateDefault());
        world.InitializeAgent("Alice", 13, 13);
        world.InitializeAgent("Bob",   13, 13);
        world.InitializeItems();
        world.LearnName("Alice", "Bob");

        var item = world.GetItemsAt(13, 13).First();
        world.TryPickUp("Alice", item.InstanceId.ToString());

        var action = new AgentAction
        {
            ItemAction   = "give",
            ItemTargetId = item.InstanceId.ToString(),
            ItemGiveTo   = "Bob"
        };
        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();

        Assert.Contains(events, e => e.Type == "item");
        Assert.Contains(world.GetInventory("Bob"), i => i.InstanceId == item.InstanceId);
    }

    [Fact]
    public void Resolve_Give_ToAgentOnDifferentCell_NoItemEvent()
    {
        var world = new WorldState("Test.", MapGrid.CreateDefault());
        world.InitializeAgent("Alice", 13, 13);
        world.InitializeAgent("Bob",   5,  5); // far away
        world.InitializeItems();

        var item = world.GetItemsAt(13, 13).First();
        world.TryPickUp("Alice", item.InstanceId.ToString());

        var action = new AgentAction
        {
            ItemAction   = "give",
            ItemTargetId = item.InstanceId.ToString(),
            ItemGiveTo   = "Bob"
        };
        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();

        Assert.DoesNotContain(events, e => e.Type == "item");
        Assert.Empty(world.GetInventory("Bob"));
    }

    // ── Direct message ────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DirectMessage_KnownRecipientAdjacent_EmitsDmEvent()
    {
        var world = new WorldState("Test.", MapGrid.CreateDefault());
        world.InitializeAgent("Alice", 10, 10);
        world.InitializeAgent("Bob",   10, 10); // same cell
        world.LearnName("Alice", "Bob");

        var action = new AgentAction { Speech = "Hey Bob!", AddressAgent = "Bob" };
        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();

        Assert.Contains(events, e => e.Type == SimEventTypes.DirectMessage);
    }

    [Fact]
    public void Resolve_DirectMessage_KnownRecipientAdjacent_MessageQueuedInInbox()
    {
        var world = new WorldState("Test.", MapGrid.CreateDefault());
        world.InitializeAgent("Alice", 10, 10);
        world.InitializeAgent("Bob",   10, 10);
        world.LearnName("Alice", "Bob");

        var action = new AgentAction { Speech = "Hello!", AddressAgent = "Bob" };
        ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList(); // enumerate to execute

        Assert.True(world.Communication.HasPendingMessages("Bob"));
    }

    [Fact]
    public void Resolve_DirectMessage_UnknownRecipient_NoDmEvent()
    {
        var world = new WorldState("Test.", MapGrid.CreateDefault());
        world.InitializeAgent("Alice", 10, 10);
        world.InitializeAgent("Bob",   10, 10);
        // Alice does NOT know Bob's name

        var action = new AgentAction { Speech = "Hey!", AddressAgent = "Bob" };
        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();

        Assert.DoesNotContain(events, e => e.Type == SimEventTypes.DirectMessage);
        Assert.False(world.Communication.HasPendingMessages("Bob"));
    }

    [Fact]
    public void Resolve_DirectMessage_EmptyAddressAgent_NoDmEvent()
    {
        var world = new WorldState("Test.", MapGrid.CreateDefault());
        world.InitializeAgent("Alice", 10, 10);
        world.InitializeAgent("Bob",   10, 10);
        world.LearnName("Alice", "Bob");

        var action = new AgentAction { Speech = "Hello!", AddressAgent = "" };
        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();

        Assert.DoesNotContain(events, e => e.Type == SimEventTypes.DirectMessage);
    }

    // ── AgentName / AgentColor propagation ───────────────────────────────────

    [Fact]
    public void Resolve_AllEmittedEvents_CarryAgentNameAndColor()
    {
        var world  = MakeWorld("Alice", 13, 13);
        var action = new AgentAction { MoveTo = "N" };

        var events = ActionResolver.Resolve(world, "Alice", Color, action, 1).ToList();

        Assert.All(events, e =>
        {
            Assert.Equal("Alice", e.AgentName);
            Assert.Equal(Color,   e.AgentColor);
        });
    }
}
