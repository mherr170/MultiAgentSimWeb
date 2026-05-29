using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services;

/// <summary>
/// Handles all group-related actions (form, join, leave, waypoint, vote) extracted from
/// <see cref="SimulationService"/>. Returns the resulting <see cref="SimEvent"/> stream and
/// applies side-effects to <see cref="WorldState"/> (memory, mood, group registry).
/// </summary>
public static class GroupActionHandler
{
    public static IEnumerable<SimEvent> Handle(
        WorldState world, string agentName, string agentColor, AgentAction action, int round)
    {
        // ── Leave group ──────────────────────────────────────────────────────
        if (action.LeaveGroup)
        {
            var formerMembers = world.Groups.GetGroup(agentName)?.Members.ToList();
            var leftName      = world.Groups.LeaveGroup(agentName);
            if (leftName != null)
            {
                yield return new SimEvent
                {
                    Type       = SimEventTypes.Group,
                    AgentName  = agentName,
                    AgentColor = agentColor,
                    Label      = "leaves group",
                    Content    = $"{agentName} left \"{leftName}\"",
                    Round      = round,
                };
                world.Memory.AddMemory(agentName, $"I left the group \"{leftName}\".");

                if (formerMembers != null)
                {
                    foreach (var member in formerMembers.Where(m => m != agentName))
                    {
                        if (!world.KnowsName(member, agentName)) continue;
                        if (!world.Mood.Has(member)) continue;
                        world.GetMood(member).AdjustTrust(agentName, -12f);
                        world.GetMood(member).AdjustStress(+5f);
                        world.Memory.AddMemory(member, $"{agentName} abandoned our group \"{leftName}\".");
                        world.LogDev($"[{member}] trust[{agentName}] -12  stress +5  (abandoned group \"{leftName}\")");
                    }
                }
            }
            yield break; // can't simultaneously propose or accept
        }

        // ── Accept a pending invite ──────────────────────────────────────────
        if (action.AcceptGroupInvite)
        {
            var joined = world.Groups.AcceptInvite(agentName);
            if (joined != null)
            {
                yield return new SimEvent
                {
                    Type       = SimEventTypes.Group,
                    AgentName  = agentName,
                    AgentColor = agentColor,
                    Label      = "joins group",
                    Content    = $"{agentName} joined \"{joined.Name}\" (members: {string.Join(", ", joined.Members)})",
                    Round      = round,
                };
                world.Memory.AddMemory(agentName, $"I joined the group \"{joined.Name}\".");
                foreach (var member in joined.Members.Where(m => m != agentName))
                    world.Memory.AddMemory(member, $"{agentName} joined our group \"{joined.Name}\".");
            }
            yield break;
        }

        // ── Propose / invite ─────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(action.GroupPropose) && !string.IsNullOrWhiteSpace(action.AddressAgent))
        {
            var target = action.AddressAgent.Trim();
            if (!world.KnowsName(agentName, target))                                   yield break;
            if (!world.GetVisibleAgents(agentName).Any(a => a.name == target))         yield break;
            float trust = world.Mood.Has(agentName) ? world.GetMood(agentName).GetTrust(target) : 0f;
            if (trust <= 0f)                                                            yield break;

            var existingGroupId = world.Groups.GetGroupId(agentName);
            string groupId, groupName;
            if (existingGroupId != null)
            {
                groupId   = existingGroupId;
                groupName = world.Groups.GetGroup(agentName)!.Name;
            }
            else
            {
                groupName = action.GroupPropose.Trim();
                groupId   = world.Groups.CreateGroup(agentName, groupName);
                yield return new SimEvent
                {
                    Type       = SimEventTypes.Group,
                    AgentName  = agentName,
                    AgentColor = agentColor,
                    Label      = "forms group",
                    Content    = $"{agentName} formed the group \"{groupName}\"",
                    Round      = round,
                };
                world.Memory.AddMemory(agentName, $"I formed the group \"{groupName}\" and invited {target}.");
            }

            world.Groups.SendInvite(agentName, target, groupId, groupName);
            yield return new SimEvent
            {
                Type       = SimEventTypes.Group,
                AgentName  = agentName,
                AgentColor = agentColor,
                Label      = "invites",
                Content    = $"{agentName} invited {target} to \"{groupName}\"",
                Round      = round,
            };
        }

        // ── Waypoint ─────────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(action.GroupSetWaypoint))
        {
            var grp = world.Groups.GetGroup(agentName);
            if (grp != null)
            {
                var pos = world.GetAgentPosition(agentName);
                grp.Waypoint = (pos.x, pos.y, action.GroupSetWaypoint.Trim(), agentName);
                yield return new SimEvent
                {
                    Type       = SimEventTypes.Group,
                    AgentName  = agentName,
                    AgentColor = agentColor,
                    Label      = "sets waypoint",
                    Content    = $"{agentName} marked \"{action.GroupSetWaypoint.Trim()}\" ({pos.x},{pos.y}) as the group meeting point",
                    Round      = round,
                };
            }
        }

        // ── Propose vote ─────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(action.GroupVotePropose))
        {
            var grp = world.Groups.GetGroup(agentName);
            if (grp != null && grp.ActiveVote == null)
            {
                grp.ActiveVote = new GroupVote
                {
                    Proposer      = agentName,
                    Question      = action.GroupVotePropose.Trim(),
                    ProposedRound = round,
                };
                grp.ActiveVote.Votes[agentName] = "yes"; // proposer implicitly votes yes
                yield return new SimEvent
                {
                    Type       = SimEventTypes.Group,
                    AgentName  = agentName,
                    AgentColor = agentColor,
                    Label      = "calls vote",
                    Content    = $"{agentName} called a group vote: \"{action.GroupVotePropose.Trim()}\"",
                    Round      = round,
                };
            }
        }

        // ── Cast vote ────────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(action.GroupVote))
        {
            var grp = world.Groups.GetGroup(agentName);
            if (grp?.ActiveVote != null && !grp.ActiveVote.Votes.ContainsKey(agentName))
            {
                grp.ActiveVote.Votes[agentName] = action.GroupVote.Trim().ToLowerInvariant();
                yield return new SimEvent
                {
                    Type       = SimEventTypes.Group,
                    AgentName  = agentName,
                    AgentColor = agentColor,
                    Label      = "votes",
                    Content    = $"{agentName} voted \"{action.GroupVote.Trim()}\" on \"{grp.ActiveVote.Question}\"",
                    Round      = round,
                };
            }
        }
    }
}
