using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services;

/// <summary>
/// Resolves the physical, resource, item, animal, and direct-message consequences of an
/// <see cref="AgentAction"/> into a stream of <see cref="SimEvent"/>s.
/// No state is stored — this is a pure dispatch table.
/// </summary>
public static class ActionResolver
{
    /// <summary>
    /// Yields the narrative events (thought, speech, action-label) for a turn.
    /// Call before <see cref="Resolve"/> so narrative appears ahead of mechanical results.
    /// </summary>
    public static IEnumerable<SimEvent> NarrativeEvents(
        string agentName, string agentColor, AgentAction action)
    {
        if (!string.IsNullOrWhiteSpace(action.Thought))
            yield return new SimEvent
            {
                Type = "thought", AgentName = agentName, AgentColor = agentColor,
                Label = "thinks", Content = action.Thought
            };

        if (!string.IsNullOrWhiteSpace(action.Speech))
            yield return new SimEvent
            {
                Type = "speech", AgentName = agentName, AgentColor = agentColor,
                Label = "says", Content = $"\"{action.Speech}\""
            };

        if (!string.IsNullOrWhiteSpace(action.Action) && action.Action != "nothing")
            yield return new SimEvent
            {
                Type = "action", AgentName = agentName, AgentColor = agentColor,
                Label = "does", Content = action.Action
            };
    }

    /// <summary>
    /// Resolves movement, resource, item, animal, and direct-message actions.
    /// Yields any <see cref="SimEvent"/>s produced as a result.
    /// </summary>
    public static IEnumerable<SimEvent> Resolve(
        WorldState world, string agentName, string agentColor, AgentAction action, int round)
    {
        // ── Movement ─────────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(action.MoveTo))
        {
            if (world.MoveAgent(agentName, action.MoveTo))
            {
                var (nx, ny) = world.GetAgentPosition(agentName);
                yield return new SimEvent
                {
                    Type = "move", AgentName = agentName, AgentColor = agentColor,
                    Label = "moves", Content = $"{action.MoveTo.ToUpperInvariant()} → ({nx},{ny})"
                };
            }
        }

        if (!string.IsNullOrEmpty(action.MoveFloor))
        {
            var fpos         = world.GetAgentPosition(agentName);
            var fcell        = world.GetCell(fpos.x, fpos.y);
            var currentFloor = world.GetAgentFloor(agentName);
            var maxFloors    = fcell.Floors;
            if (action.MoveFloor == "up" && currentFloor < maxFloors)
            {
                world.SetAgentFloor(agentName, currentFloor + 1);
                world.LogAt(fpos.x, fpos.y, $"{agentName} climbed to floor {currentFloor + 1}");
                yield return new SimEvent
                {
                    Type = "move", AgentName = agentName, AgentColor = agentColor,
                    Content = $"climbs to floor {currentFloor + 1} of {maxFloors}", Round = round
                };
            }
            else if (action.MoveFloor == "down" && currentFloor > 1)
            {
                world.SetAgentFloor(agentName, currentFloor - 1);
                world.LogAt(fpos.x, fpos.y, $"{agentName} descended to floor {currentFloor - 1}");
                yield return new SimEvent
                {
                    Type = "move", AgentName = agentName, AgentColor = agentColor,
                    Content = $"descends to floor {currentFloor - 1}", Round = round
                };
            }
        }

        // ── Resource actions ─────────────────────────────────────────────────
        if (action.Scavenge)
        {
            var result = world.TryForage(agentName);
            if (result is not null)
                yield return new SimEvent
                {
                    Type = "scavenge", AgentName = agentName, AgentColor = agentColor,
                    Label = "scavenges", Content = result
                };
        }

        if (action.DrinkTap)
        {
            var result = world.TryDrinkTap(agentName);
            if (result is not null)
                yield return new SimEvent
                {
                    Type = "drink_tap", AgentName = agentName, AgentColor = agentColor,
                    Label = "drinks", Content = result
                };
        }

        if (action.DrinkFountain)
        {
            var result = world.TryDrinkFountain(agentName);
            if (result is not null)
                yield return new SimEvent
                {
                    Type = SimEventTypes.DrinkFountain, AgentName = agentName, AgentColor = agentColor,
                    Label = "drinks", Content = result
                };
        }

        if (action.DrinkRiver)
        {
            var result = world.TryDrinkRiver(agentName);
            if (result is not null)
                yield return new SimEvent
                {
                    Type = SimEventTypes.DrinkRiver, AgentName = agentName, AgentColor = agentColor,
                    Label = "drinks", Content = result
                };
        }

        if (action.Fish)
        {
            var result = world.TryFish(agentName);
            if (result is not null)
                yield return new SimEvent
                {
                    Type = SimEventTypes.Fish, AgentName = agentName, AgentColor = agentColor,
                    Label = "fishes", Content = result
                };
        }

        if (!string.IsNullOrWhiteSpace(action.Cook))
        {
            var result = world.TryCook(agentName, action.Cook.Trim());
            if (result is not null)
                yield return new SimEvent
                {
                    Type = SimEventTypes.Cook, AgentName = agentName, AgentColor = agentColor,
                    Label = "cooks", Content = result
                };
        }

        // ── Item action ──────────────────────────────────────────────────────
        var itemContent = ResolveItemAction(world, agentName, action);
        if (itemContent is not null)
            yield return new SimEvent
            {
                Type = "item", AgentName = agentName, AgentColor = agentColor,
                Label = action.ItemAction, Content = itemContent
            };

        // ── Animal action ────────────────────────────────────────────────────
        var animalContent = ResolveAnimalAction(world, agentName, action);
        if (animalContent is not null)
            yield return new SimEvent
            {
                Type = "animal", AgentName = agentName, AgentColor = agentColor,
                Label = action.AnimalAction, Content = animalContent, Round = round
            };

        // ── Direct message ───────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(action.AddressAgent) &&
            !string.IsNullOrWhiteSpace(action.Speech) &&
            world.KnowsName(agentName, action.AddressAgent.Trim()))
        {
            var target = action.AddressAgent.Trim();
            world.QueueDirectMessage(new DirectMessage
            {
                FromAgent = agentName,
                ToAgent   = target,
                Message   = action.Speech,
                Round     = round
            });
            yield return new SimEvent
            {
                Type       = SimEventTypes.DirectMessage,
                AgentName  = agentName,
                AgentColor = agentColor,
                Label      = $"→ {target}",
                Content    = $"\"{action.Speech}\"",
                Round      = round
            };
        }
    }

    // ── Private dispatch helpers ─────────────────────────────────────────────

    private static string? ResolveAnimalAction(WorldState world, string agentName, AgentAction action)
    {
        var id = action.AnimalTargetId?.Trim() ?? "";
        return action.AnimalAction switch
        {
            "attack" => world.TryAttackAnimal(agentName, id),
            "trap"   => world.TryTrapAnimal(agentName, id),
            "scare"  => world.TryScareAnimal(agentName, id),
            "feed"   => world.TryFeedAnimal(agentName, id),
            _        => null
        };
    }

    private static string? ResolveItemAction(WorldState world, string agentName, AgentAction action)
    {
        var id = action.ItemTargetId?.Trim() ?? "";
        switch (action.ItemAction)
        {
            case "pick_up":
            {
                var name = world.TryPickUp(agentName, id);
                return name is not null ? $"picks up {name}" : null;
            }
            case "drop":
            {
                var name = world.TryDrop(agentName, id);
                return name is not null ? $"drops {name}" : null;
            }
            case "use":
            {
                var fx = world.TryUse(agentName, id);
                return fx.Length > 0 ? fx : null;
            }
            case "give":
            {
                var name = world.TryGive(agentName, id, action.ItemGiveTo?.Trim() ?? "");
                return name is not null ? $"gives {name} to {action.ItemGiveTo}" : null;
            }
            case "deconstruct":
            {
                var (consumed, ok, yields) = world.TryDeconstruct(agentName, id);
                if (ok)       return $"deconstructs → {string.Join(", ", yields)}";
                if (consumed) return "deconstruct failed — item crumbled to nothing";
                return null;
            }
            case "craft":
            {
                var result = world.TryCraft(agentName, action.CraftRecipeId?.Trim() ?? "");
                return result?.StartsWith("crafts ") == true ? result : null;
            }
            case "place_trap": return world.TryPlaceTrap(agentName, id);
            case "fill":       return world.TryFillContainer(agentName, id);
            default:           return null;
        }
    }
}
