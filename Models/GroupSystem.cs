namespace MultiAgentSimWeb.Models;

public class GroupVote
{
    public string                    Proposer      { get; init; } = "";
    public string                    Question      { get; init; } = "";
    public int                       ProposedRound { get; init; }
    public Dictionary<string, string> Votes        { get; } = new();
}

public class GroupData
{
    public string       Id      { get; }
    public string       Name    { get; }
    public string       Leader  { get; }
    public List<string> Members { get; } = new();

    // Shared item stash — physical cache at a fixed map cell.
    public List<ItemInstance>  Stash         { get; } = new();
    public (int x, int y)?    StashLocation { get; set; }

    // Waypoint for coordinated movement — set by any member at their current cell.
    public (int x, int y, string Description, string SetBy)? Waypoint { get; set; }

    // Active group vote (one at a time).
    public GroupVote? ActiveVote { get; set; }

    public GroupData(string id, string name, string leader)
    {
        Id     = id;
        Name   = name;
        Leader = leader;
    }
}

public interface IGroupSystem
{
    void Attach(MultiAgentSimWeb.Services.WorldState world);

    bool        IsInGroup(string agent);
    string?     GetGroupId(string agent);
    GroupData?  GetGroup(string agent);
    (string Inviter, string GroupId, string GroupName)? GetPendingInvite(string agent);

    string     CreateGroup(string founder, string name);
    void       SendInvite(string inviter, string invitee, string groupId, string groupName);
    GroupData? AcceptInvite(string invitee);
    string?    LeaveGroup(string agent);
    void       ClearInvite(string invitee);

    /// Removes all pending invites that were sent on a round earlier than <paramref name="currentRound"/>.
    void ExpireInvites(int currentRound);

    IEnumerable<GroupData> AllGroups { get; }

    /// Removes item from agent's inventory and places it in their group stash.
    /// Returns the item's display name on success, null otherwise.
    string? TryDeposit(string agentName, string instanceIdStr);

    /// Removes item from agent's group stash and places it in their inventory.
    /// Returns the item's display name on success, null otherwise.
    string? TryWithdraw(string agentName, string instanceIdStr);
}

/// Tracks group membership, pending invites, stash, waypoints, and votes.
public class GroupSystem : IGroupSystem
{
    private readonly Dictionary<string, string>     _memberToGroup  = new();
    private readonly Dictionary<string, GroupData>  _groups         = new();
    private readonly Dictionary<string, (string Inviter, string GroupId, string GroupName, int SentRound)> _pendingInvites = new();

    private MultiAgentSimWeb.Services.WorldState _world = null!;
    public void Attach(MultiAgentSimWeb.Services.WorldState world) => _world = world;

    public bool        IsInGroup(string agent)  => _memberToGroup.ContainsKey(agent);
    public string?     GetGroupId(string agent) => _memberToGroup.GetValueOrDefault(agent);
    public GroupData?  GetGroup(string agent)   => _memberToGroup.TryGetValue(agent, out var id) ? _groups.GetValueOrDefault(id) : null;

    public (string Inviter, string GroupId, string GroupName)? GetPendingInvite(string agent)
        => _pendingInvites.TryGetValue(agent, out var inv) ? (inv.Inviter, inv.GroupId, inv.GroupName) : null;

    public string CreateGroup(string founder, string name)
    {
        var id    = Guid.NewGuid().ToString("N")[..8];
        var group = new GroupData(id, name, founder);
        group.Members.Add(founder);
        _groups[id]             = group;
        _memberToGroup[founder] = id;
        return id;
    }

    public void SendInvite(string inviter, string invitee, string groupId, string groupName)
        => _pendingInvites[invitee] = (inviter, groupId, groupName, _world.CurrentRound);

    public GroupData? AcceptInvite(string invitee)
    {
        if (!_pendingInvites.TryGetValue(invitee, out var inv)) return null;
        _pendingInvites.Remove(invitee);
        if (!_groups.TryGetValue(inv.GroupId, out var group)) return null;
        group.Members.Add(invitee);
        _memberToGroup[invitee] = inv.GroupId;
        return group;
    }

    public string? LeaveGroup(string agent)
    {
        if (!_memberToGroup.TryGetValue(agent, out var groupId)) return null;
        _memberToGroup.Remove(agent);
        if (!_groups.TryGetValue(groupId, out var group)) return null;
        group.Members.Remove(agent);
        if (group.Members.Count == 0)
            _groups.Remove(groupId);
        return group.Name;
    }

    public void ClearInvite(string invitee) => _pendingInvites.Remove(invitee);

    public void ExpireInvites(int currentRound)
    {
        var stale = _pendingInvites
            .Where(kv => kv.Value.SentRound < currentRound)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var invitee in stale)
        {
            _pendingInvites.Remove(invitee);
            _world.LogDev($"[group] invite to {invitee} expired (sent round {currentRound - 1})");
        }
    }

    public IEnumerable<GroupData> AllGroups => _groups.Values;

    public string? TryDeposit(string agentName, string instanceIdStr)
    {
        var grp = GetGroup(agentName);
        if (grp == null) return null;
        var item = _world.Items.TryRemoveFromInventory(agentName, instanceIdStr);
        if (item == null) return null;
        grp.Stash.Add(item);
        if (grp.StashLocation == null)
        {
            var pos = _world.GetAgentPosition(agentName);
            if (pos.x >= 0) grp.StashLocation = (pos.x, pos.y);
        }
        _world.LogDev($"[{agentName}] deposited {item.DisplayName} into group stash \"{grp.Name}\"");
        return item.DisplayName;
    }

    public string? TryWithdraw(string agentName, string instanceIdStr)
    {
        var grp = GetGroup(agentName);
        if (grp == null) return null;
        var item = grp.Stash.FirstOrDefault(i =>
            string.Equals(i.InstanceId.ToString(), instanceIdStr, StringComparison.OrdinalIgnoreCase));
        if (item == null) return null;
        if (!_world.Items.TryAddItemInstance(agentName, item)) return null;
        grp.Stash.Remove(item);
        _world.LogDev($"[{agentName}] withdrew {item.DisplayName} from group stash \"{grp.Name}\"");
        return item.DisplayName;
    }
}
