using MultiAgentSimWeb.Models;
using MultiAgentSimWeb.Services;

namespace MultiAgentSimWebTests;

public class GroupSystemTests
{
    private static WorldState MakeWorld()
    {
        var world = new WorldState("Test.", MapGrid.CreateDefault());
        world.InitializeAgent("Alice", 10, 10);
        world.InitializeAgent("Bob",   10, 10);
        world.InitializeAgent("Carol", 10, 10);
        return world;
    }

    // ── CreateGroup ───────────────────────────────────────────────────────────

    [Fact]
    public void CreateGroup_FounderIsInGroup()
    {
        var world = MakeWorld();
        world.Groups.CreateGroup("Alice", "Survivors");
        Assert.True(world.Groups.IsInGroup("Alice"));
    }

    [Fact]
    public void CreateGroup_FounderIsListedAsMember()
    {
        var world = MakeWorld();
        world.Groups.CreateGroup("Alice", "Survivors");
        var group = world.Groups.GetGroup("Alice");
        Assert.NotNull(group);
        Assert.Contains("Alice", group!.Members);
    }

    [Fact]
    public void CreateGroup_GroupNameMatches()
    {
        var world = MakeWorld();
        world.Groups.CreateGroup("Alice", "Survivors");
        Assert.Equal("Survivors", world.Groups.GetGroup("Alice")!.Name);
    }

    [Fact]
    public void CreateGroup_NonFounder_IsNotInGroup()
    {
        var world = MakeWorld();
        world.Groups.CreateGroup("Alice", "Survivors");
        Assert.False(world.Groups.IsInGroup("Bob"));
    }

    [Fact]
    public void CreateGroup_ReturnsNonEmptyId()
    {
        var world = MakeWorld();
        var id = world.Groups.CreateGroup("Alice", "Survivors");
        Assert.False(string.IsNullOrEmpty(id));
    }

    // ── SendInvite / AcceptInvite ─────────────────────────────────────────────

    [Fact]
    public void SendInvite_PendingInviteVisibleToInvitee()
    {
        var world = MakeWorld();
        var groupId = world.Groups.CreateGroup("Alice", "Survivors");
        world.Groups.SendInvite("Alice", "Bob", groupId, "Survivors");

        var invite = world.Groups.GetPendingInvite("Bob");
        Assert.NotNull(invite);
        Assert.Equal("Alice", invite!.Value.Inviter);
        Assert.Equal("Survivors", invite.Value.GroupName);
    }

    [Fact]
    public void AcceptInvite_InviteeJoinsGroup()
    {
        var world = MakeWorld();
        var groupId = world.Groups.CreateGroup("Alice", "Survivors");
        world.Groups.SendInvite("Alice", "Bob", groupId, "Survivors");
        world.Groups.AcceptInvite("Bob");

        Assert.True(world.Groups.IsInGroup("Bob"));
        Assert.Contains("Bob", world.Groups.GetGroup("Bob")!.Members);
    }

    [Fact]
    public void AcceptInvite_InviteeAndFounderShareSameGroup()
    {
        var world = MakeWorld();
        var groupId = world.Groups.CreateGroup("Alice", "Survivors");
        world.Groups.SendInvite("Alice", "Bob", groupId, "Survivors");
        world.Groups.AcceptInvite("Bob");

        Assert.Equal(world.Groups.GetGroupId("Alice"), world.Groups.GetGroupId("Bob"));
    }

    [Fact]
    public void AcceptInvite_ClearsPendingInvite()
    {
        var world = MakeWorld();
        var groupId = world.Groups.CreateGroup("Alice", "Survivors");
        world.Groups.SendInvite("Alice", "Bob", groupId, "Survivors");
        world.Groups.AcceptInvite("Bob");

        Assert.Null(world.Groups.GetPendingInvite("Bob"));
    }

    [Fact]
    public void AcceptInvite_NoPendingInvite_ReturnsNull()
    {
        var world = MakeWorld();
        var result = world.Groups.AcceptInvite("Bob");
        Assert.Null(result);
    }

    // ── ClearInvite ───────────────────────────────────────────────────────────

    [Fact]
    public void ClearInvite_RemovesPendingInvite()
    {
        var world = MakeWorld();
        var groupId = world.Groups.CreateGroup("Alice", "Survivors");
        world.Groups.SendInvite("Alice", "Bob", groupId, "Survivors");
        world.Groups.ClearInvite("Bob");

        Assert.Null(world.Groups.GetPendingInvite("Bob"));
    }

    // ── LeaveGroup ────────────────────────────────────────────────────────────

    [Fact]
    public void LeaveGroup_AgentNoLongerInGroup()
    {
        var world = MakeWorld();
        var groupId = world.Groups.CreateGroup("Alice", "Survivors");
        world.Groups.SendInvite("Alice", "Bob", groupId, "Survivors");
        world.Groups.AcceptInvite("Bob");

        world.Groups.LeaveGroup("Bob");

        Assert.False(world.Groups.IsInGroup("Bob"));
    }

    [Fact]
    public void LeaveGroup_ReturnsGroupName()
    {
        var world = MakeWorld();
        world.Groups.CreateGroup("Alice", "Survivors");
        var name = world.Groups.LeaveGroup("Alice");
        Assert.Equal("Survivors", name);
    }

    [Fact]
    public void LeaveGroup_RemainingMemberStillInGroup()
    {
        var world = MakeWorld();
        var groupId = world.Groups.CreateGroup("Alice", "Survivors");
        world.Groups.SendInvite("Alice", "Bob", groupId, "Survivors");
        world.Groups.AcceptInvite("Bob");

        world.Groups.LeaveGroup("Alice");

        Assert.True(world.Groups.IsInGroup("Bob"));
        Assert.DoesNotContain("Alice", world.Groups.GetGroup("Bob")!.Members);
    }

    [Fact]
    public void LeaveGroup_LastMember_GroupDissolves()
    {
        var world = MakeWorld();
        world.Groups.CreateGroup("Alice", "Survivors");
        world.Groups.LeaveGroup("Alice");

        Assert.Empty(world.Groups.AllGroups);
    }

    [Fact]
    public void LeaveGroup_NotInGroup_ReturnsNull()
    {
        var world = MakeWorld();
        Assert.Null(world.Groups.LeaveGroup("Bob"));
    }

    // ── ExpireInvites ─────────────────────────────────────────────────────────

    [Fact]
    public void ExpireInvites_OlderThanCurrentRound_Removed()
    {
        var world = MakeWorld();
        world.CurrentRound = 1;
        var groupId = world.Groups.CreateGroup("Alice", "Survivors");
        world.Groups.SendInvite("Alice", "Bob", groupId, "Survivors"); // sent on round 1
        world.CurrentRound = 2;
        world.Groups.ExpireInvites(2); // expires invites sent before round 2

        Assert.Null(world.Groups.GetPendingInvite("Bob"));
    }

    [Fact]
    public void ExpireInvites_SameRound_NotRemoved()
    {
        var world = MakeWorld();
        world.CurrentRound = 2;
        var groupId = world.Groups.CreateGroup("Alice", "Survivors");
        world.Groups.SendInvite("Alice", "Bob", groupId, "Survivors"); // sent on round 2
        world.Groups.ExpireInvites(2); // only removes invites sent BEFORE round 2

        Assert.NotNull(world.Groups.GetPendingInvite("Bob"));
    }

    // ── Waypoint ──────────────────────────────────────────────────────────────

    [Fact]
    public void Waypoint_SetAndRetrieved()
    {
        var world = MakeWorld();
        world.Groups.CreateGroup("Alice", "Survivors");
        var group = world.Groups.GetGroup("Alice")!;

        group.Waypoint = (5, 7, "the hospital", "Alice");

        Assert.NotNull(group.Waypoint);
        Assert.Equal(5, group.Waypoint.Value.x);
        Assert.Equal(7, group.Waypoint.Value.y);
        Assert.Equal("the hospital", group.Waypoint.Value.Description);
    }

    [Fact]
    public void Waypoint_ClearedBySettingNull()
    {
        var world = MakeWorld();
        world.Groups.CreateGroup("Alice", "Survivors");
        var group = world.Groups.GetGroup("Alice")!;
        group.Waypoint = (5, 7, "hospital", "Alice");

        group.Waypoint = null;

        Assert.Null(group.Waypoint);
    }

    // ── TryDeposit / TryWithdraw ──────────────────────────────────────────────

    [Fact]
    public void TryDeposit_ItemMovesToGroupStash()
    {
        var world = MakeWorld();
        world.Groups.CreateGroup("Alice", "Survivors");
        world.Items.AddToInventory("Alice", "water_bottle");
        var item = world.GetInventory("Alice").First(i => i.DefinitionId == "water_bottle");

        var result = world.Groups.TryDeposit("Alice", item.InstanceId.ToString());

        Assert.NotNull(result);
        Assert.Empty(world.GetInventory("Alice").Where(i => i.DefinitionId == "water_bottle"));
        Assert.Contains(world.Groups.GetGroup("Alice")!.Stash, i => i.InstanceId == item.InstanceId);
    }

    [Fact]
    public void TryDeposit_SetsStashLocationToAgentPosition()
    {
        var world = MakeWorld();
        world.Groups.CreateGroup("Alice", "Survivors");
        world.Items.AddToInventory("Alice", "water_bottle");
        var item = world.GetInventory("Alice").First(i => i.DefinitionId == "water_bottle");

        world.Groups.TryDeposit("Alice", item.InstanceId.ToString());

        var stash = world.Groups.GetGroup("Alice")!.StashLocation;
        Assert.NotNull(stash);
        Assert.Equal(world.GetAgentPosition("Alice"), (stash!.Value.x, stash.Value.y));
    }

    [Fact]
    public void TryDeposit_NotInGroup_ReturnsNull()
    {
        var world = MakeWorld();
        world.Items.AddToInventory("Alice", "water_bottle");
        var item = world.GetInventory("Alice").First();
        Assert.Null(world.Groups.TryDeposit("Alice", item.InstanceId.ToString()));
    }

    [Fact]
    public void TryWithdraw_ItemMovesFromStashToInventory()
    {
        var world = MakeWorld();
        world.Groups.CreateGroup("Alice", "Survivors");
        world.Items.AddToInventory("Alice", "water_bottle");
        var item = world.GetInventory("Alice").First(i => i.DefinitionId == "water_bottle");
        world.Groups.TryDeposit("Alice", item.InstanceId.ToString());

        var result = world.Groups.TryWithdraw("Alice", item.InstanceId.ToString());

        Assert.NotNull(result);
        Assert.Contains(world.GetInventory("Alice"), i => i.InstanceId == item.InstanceId);
        Assert.Empty(world.Groups.GetGroup("Alice")!.Stash);
    }

    [Fact]
    public void TryWithdraw_UnknownId_ReturnsNull()
    {
        var world = MakeWorld();
        world.Groups.CreateGroup("Alice", "Survivors");
        Assert.Null(world.Groups.TryWithdraw("Alice", Guid.NewGuid().ToString()));
    }

    // ── AllGroups ─────────────────────────────────────────────────────────────

    [Fact]
    public void AllGroups_ContainsCreatedGroup()
    {
        var world = MakeWorld();
        world.Groups.CreateGroup("Alice", "Survivors");
        Assert.Single(world.Groups.AllGroups);
        Assert.Equal("Survivors", world.Groups.AllGroups.First().Name);
    }

    [Fact]
    public void AllGroups_EmptyWhenNoGroupsCreated()
    {
        var world = MakeWorld();
        Assert.Empty(world.Groups.AllGroups);
    }
}
