using System.Diagnostics;
using MultiAgentSimWeb.Models;
using MultiAgentSimWeb.Services.Maps;

namespace MultiAgentSimWeb.Services;

public class SimulationConfig
{
    public string Situation { get; init; } = "";
    public IReadOnlyList<AgentDefinition> Agents { get; init; } = [];
    public int Rounds { get; init; } = 50;
    public IReadOnlyList<string> AgentColors { get; init; } = [];
    public IReadOnlyList<string> AgentColorHex { get; init; } = [];
    public Func<ILlmClient> LlmClientFactory { get; init; } = () => throw new InvalidOperationException("No LLM client factory provided.");

    /// Minimum wall-clock milliseconds per agent turn. If the LLM responds faster
    /// than this, the simulation waits out the remainder before moving on.
    /// 0 = no minimum (run as fast as possible).
    public int MinTurnMs { get; init; } = 0;
}

/// Runs the per-turn agent loop. UI layer subscribes to OnStateChanged
/// and reads the public state properties to render.
public class SimulationService
{
    private readonly IMapGenerator _mapGenerator;
    private readonly LlmDiagnosticsService _diagnostics;

    public SimulationService(IMapGenerator mapGenerator, LlmDiagnosticsService diagnostics)
    {
        _mapGenerator = mapGenerator;
        _diagnostics  = diagnostics;
    }

    public WorldState? World { get; private set; }
    public Dictionary<string, string> AgentColorHex { get; } = new();
    public List<SimEvent> Events { get; } = new();
    public ActiveAgent? CurrentAgent { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsPaused  { get; private set; }

    /// Fired after each meaningful state change. UI should marshal to its
    /// render thread (e.g. InvokeAsync(StateHasChanged)).
    public event Func<Task>? OnStateChanged;

    private CancellationTokenSource? _cts;
    private bool _stepRequested;
    private bool _stepRoundRequested;

    public void Pause()  { IsPaused = true; }
    public void Resume() { IsPaused = false; _stepRequested = false; _stepRoundRequested = false; }
    public void Step()   { _stepRequested = true; }

    /// Runs all remaining agents in the current round, then pauses at the start of the next.
    public void StepRound() { _stepRoundRequested = true; _stepRequested = true; }

    public void Stop()
    {
        IsPaused = false;
        _stepRequested = true; // break any current pause-wait
        _cts?.Cancel();
    }

    public async Task RunAsync(SimulationConfig cfg)
    {
        Events.Clear();
        World = null;
        AgentColorHex.Clear();
        IsRunning = true;
        CurrentAgent = null;
        IsPaused = true;
        _stepRequested = false;
        _stepRoundRequested = false;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            _diagnostics.Reset();

            var llmClient = cfg.LlmClientFactory();

            Action<string> simLogger = msg =>
            {
                Events.Add(new SimEvent { Type = "llm", Label = "llm", Content = msg });
                _ = NotifyAsync();
            };

            await llmClient.WarmUpAsync(ct, simLogger);

            var map   = _mapGenerator.Generate();
            var world = new WorldState(cfg.Situation, map);
            World = world;

            var runners = cfg.Agents
                .Select((def, i) =>
                {
                    var color = cfg.AgentColors[i % cfg.AgentColors.Count];
                    var hex   = cfg.AgentColorHex[i % cfg.AgentColorHex.Count];
                    Action<string> logger = msg =>
                    {
                        Events.Add(new SimEvent { Type = "llm", AgentName = def.Name, AgentColor = color, Label = "llm", Content = msg });
                        _ = NotifyAsync();
                    };
                    return (Runner: new AgentRunner(def.Name, def.Persona, llmClient, logger, _diagnostics), Color: color, Hex: hex);
                })
                .ToList();

            var startPositions = _mapGenerator.AgentStartPositions;
            for (int i = 0; i < runners.Count; i++)
            {
                var (runner, _, hex) = runners[i];
                var (sx, sy) = startPositions[i % startPositions.Count];
                world.InitializeAgent(runner.Name, sx, sy);
                var spawnCell = world.GetCell(sx, sy);
                int spawnFloor = Math.Min(i + 1, spawnCell.Floors);
                world.SetAgentFloor(runner.Name, spawnFloor);
                AgentColorHex[runner.Name] = hex;
                var cell = world.GetCell(sx, sy);
                world.Memory.AddMemory(runner.Name,
                    $"The lights went out at 11:47 PM. I was on floor {spawnFloor} of a {cell.DisplayName} when it happened. " +
                    "Everything went dark at once — no flicker, just gone. My phone has no signal. " +
                    "I haven't heard from anyone. Could be a major outage. Could be something else. " +
                    "I'm waiting to see if power comes back.");
            }

            world.InitializeItems();
            world.InitializeAnimals();
            await NotifyAsync();
            await WaitIfPaused(ct);

            for (int round = 1; round <= cfg.Rounds; round++)
            {
                if (ct.IsCancellationRequested) break;

                world.CurrentRound = round;
                Events.Add(new SimEvent { Type = "round", Content = round.ToString(), Label = world.CurrentTime });

                // Pause between rounds when stepping by round (never before round 1 — the
                // initial WaitIfPaused already handled that).
                if (_stepRoundRequested && round > 1)
                {
                    _stepRoundRequested = false;
                    _stepRequested = false;  // discard any lingering flag from mid-round StepRound
                    await NotifyAsync();
                    while (IsPaused && !_stepRequested && !_stepRoundRequested)
                    {
                        if (ct.IsCancellationRequested) break;
                        await Task.Delay(50);
                    }
                    _stepRequested = false;
                    if (ct.IsCancellationRequested) break;
                }

                // Animal tick runs before any agent turn so agents see fresh positions
                world.TickAnimals();
                world.TickRespawn();
                foreach (var ae in world.DrainAnimalEvents())
                {
                    ae.Round = round;
                    Events.Add(ae);
                }

                await NotifyAsync();

                var deadThisRound = new List<string>();

                foreach (var (runner, color, _) in runners)
                {
                    if (ct.IsCancellationRequested) break;
                    if (world.GetAgentPosition(runner.Name) == (-1, -1)) continue;

                    CurrentAgent = new ActiveAgent(runner.Name, color);
                    var turnStart = Stopwatch.GetTimestamp();
                    await NotifyAsync();

                    AgentAction action;
                    try
                    {
                        action = await runner.ActAsync(world.GetContext(runner.Name), ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        action = new AgentAction { Thought = $"Error: {ex.Message}", Action = "nothing" };
                    }

                    world.AddEvent(runner.Name, action);
                    CurrentAgent = null;

                    if (!string.IsNullOrWhiteSpace(action.Thought))
                        Events.Add(new SimEvent { Type = "thought", AgentName = runner.Name, AgentColor = color, Label = "thinks", Content = action.Thought });

                    if (!string.IsNullOrWhiteSpace(action.Speech))
                        Events.Add(new SimEvent { Type = "speech", AgentName = runner.Name, AgentColor = color, Label = "says", Content = $"\"{action.Speech}\"" });

                    if (!string.IsNullOrWhiteSpace(action.Action) && action.Action != "nothing")
                        Events.Add(new SimEvent { Type = "action", AgentName = runner.Name, AgentColor = color, Label = "does", Content = action.Action });

                    if (!string.IsNullOrWhiteSpace(action.MoveTo))
                    {
                        var moved = world.MoveAgent(runner.Name, action.MoveTo);
                        if (moved)
                        {
                            var (nx, ny) = world.GetAgentPosition(runner.Name);
                            Events.Add(new SimEvent { Type = "move", AgentName = runner.Name, AgentColor = color, Label = "moves", Content = $"{action.MoveTo.ToUpperInvariant()} → ({nx},{ny})" });
                        }
                    }

                    if (!string.IsNullOrEmpty(action.MoveFloor))
                    {
                        var fpos = world.GetAgentPosition(runner.Name);
                        var fcell = world.GetCell(fpos.x, fpos.y);
                        var currentFloor = world.GetAgentFloor(runner.Name);
                        var maxFloors = fcell.Floors;
                        if (action.MoveFloor == "up" && currentFloor < maxFloors)
                        {
                            world.SetAgentFloor(runner.Name, currentFloor + 1);
                            world.LogAt(fpos.x, fpos.y, $"{runner.Name} climbed to floor {currentFloor + 1}");
                            Events.Add(new SimEvent { Type = "move", AgentName = runner.Name, AgentColor = color, Content = $"climbs to floor {currentFloor + 1} of {maxFloors}", Round = round });
                        }
                        else if (action.MoveFloor == "down" && currentFloor > 1)
                        {
                            world.SetAgentFloor(runner.Name, currentFloor - 1);
                            world.LogAt(fpos.x, fpos.y, $"{runner.Name} descended to floor {currentFloor - 1}");
                            Events.Add(new SimEvent { Type = "move", AgentName = runner.Name, AgentColor = color, Content = $"descends to floor {currentFloor - 1}", Round = round });
                        }
                    }

                    if (action.Scavenge)
                    {
                        var scavengeResult = world.TryForage(runner.Name);
                        if (scavengeResult is not null)
                            Events.Add(new SimEvent { Type = "scavenge", AgentName = runner.Name, AgentColor = color, Label = "scavenges", Content = scavengeResult });
                    }

                    if (action.DrinkTap)
                    {
                        var tapResult = world.TryDrinkTap(runner.Name);
                        if (tapResult is not null)
                            Events.Add(new SimEvent { Type = "drink_tap", AgentName = runner.Name, AgentColor = color, Label = "drinks", Content = tapResult });
                    }

                    if (action.DrinkFountain)
                    {
                        var fountainResult = world.TryDrinkFountain(runner.Name);
                        if (fountainResult is not null)
                            Events.Add(new SimEvent { Type = SimEventTypes.DrinkFountain, AgentName = runner.Name, AgentColor = color, Label = "drinks", Content = fountainResult });
                    }

                    if (action.DrinkRiver)
                    {
                        var riverResult = world.TryDrinkRiver(runner.Name);
                        if (riverResult is not null)
                            Events.Add(new SimEvent { Type = SimEventTypes.DrinkRiver, AgentName = runner.Name, AgentColor = color, Label = "drinks", Content = riverResult });
                    }

                    var itemEventContent = DispatchItemAction(world, runner.Name, action);
                    if (itemEventContent is not null)
                        Events.Add(new SimEvent { Type = "item", AgentName = runner.Name, AgentColor = color, Label = action.ItemAction, Content = itemEventContent });

                    var animalEventContent = DispatchAnimalAction(world, runner.Name, action);
                    if (animalEventContent is not null)
                        Events.Add(new SimEvent { Type = "animal", AgentName = runner.Name, AgentColor = color, Label = action.AnimalAction, Content = animalEventContent, Round = round });

                    if (!string.IsNullOrWhiteSpace(action.AddressAgent) &&
                        !string.IsNullOrWhiteSpace(action.Speech) &&
                        world.KnowsName(runner.Name, action.AddressAgent.Trim()))
                    {
                        world.QueueDirectMessage(new DirectMessage
                        {
                            FromAgent = runner.Name,
                            ToAgent   = action.AddressAgent.Trim(),
                            Message   = action.Speech,
                            Round     = round
                        });
                    }

                    bool died = world.TickMeters(runner.Name);
                    if (!died) world.TickMood(runner.Name);
                    if (!died) world.TickPresence(runner.Name);
                    if (died)
                    {
                        var cause = world.GetHunger(runner.Name) <= 0 ? "starvation" : "dehydration";
                        Events.Add(new SimEvent { Type = "death", AgentName = runner.Name, AgentColor = color, Label = "dies", Content = $"{runner.Name} has died of {cause}.", Round = round });
                        world.KillAgent(runner.Name);
                        AgentColorHex.Remove(runner.Name);
                        deadThisRound.Add(runner.Name);
                    }

                    foreach (var msg in world.DrainDevLog())
                        Events.Add(new SimEvent { Type = "dev", Label = "dev", Content = msg });

                    await NotifyAsync();

                    // Pace control: wait out the remainder of the minimum turn window.
                    if (cfg.MinTurnMs > 0 && !ct.IsCancellationRequested)
                    {
                        var elapsed = (int)Stopwatch.GetElapsedTime(turnStart).TotalMilliseconds;
                        var remaining = cfg.MinTurnMs - elapsed;
                        if (remaining > 0)
                            await Task.Delay(remaining, ct).ConfigureAwait(false);
                    }

                    await WaitIfPaused(ct);
                    if (ct.IsCancellationRequested) break;
                }

                // ── Response phase ────────────────────────────────────────────────────
                foreach (var (runner, color, _) in runners)
                {
                    if (ct.IsCancellationRequested) break;
                    if (world.GetAgentPosition(runner.Name) == (-1, -1)) continue;
                    if (!world.HasPendingMessages(runner.Name)) continue;

                    var pending = world.DrainPendingMessages(runner.Name);

                    AgentResponse response;
                    try
                    {
                        response = await runner.RespondAsync(
                            pending,
                            world.GetContext(runner.Name),
                            ct,
                            from => world.DescribeAgent(runner.Name, from));
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        response = new AgentResponse { Thought = $"Error: {ex.Message}", Speech = "" };
                    }

                    ApplyDirectResponse(world, runner.Name, color, pending, response, round);

                    foreach (var msg in world.DrainDevLog())
                        Events.Add(new SimEvent { Type = "dev", Label = "dev", Content = msg });

                    await NotifyAsync();
                    await WaitIfPaused(ct);
                }

                runners.RemoveAll(r => deadThisRound.Contains(r.Runner.Name));

                // Emit per-round diagnostics summary
                if (_diagnostics.TotalCalls > 0)
                    Events.Add(new SimEvent
                    {
                        Type    = SimEventTypes.Diag,
                        Label   = "diag",
                        Content = _diagnostics.GetRoundSummary(),
                        Round   = round,
                    });

                if (runners.Count == 0)
                {
                    Events.Add(new SimEvent { Type = "round", Content = "All agents have perished." });
                    await NotifyAsync();
                    break;
                }
            }
        }
        finally
        {
            // Emit final diagnostics report
            if (_diagnostics.TotalCalls > 0)
                Events.Add(new SimEvent
                {
                    Type    = SimEventTypes.Diag,
                    Label   = "diag — final",
                    Content = _diagnostics.GetFinalReport(),
                });

            _diagnostics.Close();
            IsRunning = false;
            IsPaused = false;
            _stepRequested = false;
            _stepRoundRequested = false;
            _cts?.Dispose();
            _cts = null;
            await NotifyAsync();
        }
    }

    private void ApplyDirectResponse(
        WorldState world,
        string responderName,
        string responderColor,
        IReadOnlyList<DirectMessage> messages,
        AgentResponse response,
        int round)
    {
        var (rx, ry) = world.GetAgentPosition(responderName);

        if (!string.IsNullOrWhiteSpace(response.Thought))
            Events.Add(new SimEvent
            {
                Type = "thought", AgentName = responderName, AgentColor = responderColor,
                Label = "thinks", Content = response.Thought
            });

        if (string.IsNullOrWhiteSpace(response.Speech)) return;

        Events.Add(new SimEvent
        {
            Type = "direct_response", AgentName = responderName, AgentColor = responderColor,
            Label = "replies", Content = $"\"{response.Speech}\"",
            Round = round
        });

        world.LogAt(rx, ry, $"{responderName} replies: \"{response.Speech}\"");

        if (world.Mood.Has(responderName))
        {
            world.GetMood(responderName).AdjustMood(+3f);
            world.GetMood(responderName).AdjustStress(-2f);
            world.LogDev($"[{responderName}] responded directly → mood +3  stress -2");
        }

        foreach (var msg in messages)
        {
            world.Memory.AddMemory(responderName,
                $"Replied to {world.DescribeAgent(responderName, msg.FromAgent)}'s direct message: \"{response.Speech}\".");

            if (world.GetAgentPosition(msg.FromAgent) != (-1, -1))
            {
                world.Memory.AddMemory(msg.FromAgent,
                    $"{world.DescribeAgent(msg.FromAgent, responderName)} responded to me: \"{response.Speech}\".");

                if (world.Mood.Has(msg.FromAgent))
                {
                    world.GetMood(msg.FromAgent).AdjustTrust(responderName, +5f);
                    world.GetMood(msg.FromAgent).AdjustMood(+2f);
                    world.LogDev(
                        $"[{msg.FromAgent}] received direct reply from {responderName} " +
                        $"→ trust[{responderName}] +5  mood +2");
                }

                if (world.Mood.Has(responderName))
                {
                    world.GetMood(responderName).AdjustTrust(msg.FromAgent, +3f);
                    world.LogDev(
                        $"[{responderName}] trust[{msg.FromAgent}] +3 (replied to them)");
                }
            }
        }
    }

    private static string? DispatchAnimalAction(WorldState world, string agentName, AgentAction action)
    {
        var id = action.AnimalTargetId?.Trim() ?? "";
        return action.AnimalAction switch
        {
            "attack" => world.TryAttackAnimal(agentName, id),
            "trap"   => world.TryTrapAnimal(agentName, id),
            "scare"  => world.TryScareAnimal(agentName, id),
            _        => null
        };
    }

    private static string? DispatchItemAction(WorldState world, string agentName, AgentAction action)
    {
        var id = action.ItemTargetId?.Trim() ?? "";
        switch (action.ItemAction)
        {
            case "pick_up": return world.TryPickUp(agentName, id) ? "picks up item" : null;
            case "drop":    return world.TryDrop(agentName, id)   ? "drops item"    : null;
            case "use":
            {
                var fx = world.TryUse(agentName, id);
                return fx.Length > 0 ? fx : null;
            }
            case "give":    return world.TryGive(agentName, id, action.ItemGiveTo?.Trim() ?? "") ? $"gives item to {action.ItemGiveTo}" : null;
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
            case "place_trap":
            {
                return world.TryPlaceTrap(agentName, id);
            }
            case "fill":
            {
                return world.TryFillContainer(agentName, id);
            }
            default: return null;
        }
    }

    private async Task WaitIfPaused(CancellationToken ct)
    {
        if (!IsPaused) return;
        if (_stepRoundRequested) return; // skip mid-round pauses; we'll stop at the round boundary
        await NotifyAsync();
        while (IsPaused && !_stepRequested && !_stepRoundRequested)
        {
            if (ct.IsCancellationRequested) return;
            await Task.Delay(50);
        }
        _stepRequested = false;
    }

    private Task NotifyAsync() => OnStateChanged?.Invoke() ?? Task.CompletedTask;
}
