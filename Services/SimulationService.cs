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
    private readonly Random _rng = new();

    public SimulationService(IMapGenerator mapGenerator, LlmDiagnosticsService diagnostics)
    {
        _mapGenerator = mapGenerator;
        _diagnostics  = diagnostics;
    }

    public WorldState? World { get; private set; }
    public Dictionary<string, string> AgentColorHex { get; } = new();
    public System.Collections.Concurrent.ConcurrentQueue<SimEvent> Events { get; } = new();
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
        Events.Clear(); // ConcurrentQueue.Clear() is thread-safe (.NET 5+)
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
                Events.Enqueue(new SimEvent { Type = "llm", Label = "llm", Content = msg });
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
                        Events.Enqueue(new SimEvent { Type = "llm", AgentName = def.Name, AgentColor = color, Label = "llm", Content = msg });
                        _ = NotifyAsync();
                    };
                    // Use the profile's blurb as the persona if one is set; else fall back to plain Persona string.
                    var persona = (!string.IsNullOrWhiteSpace(def.Profile?.Blurb)) ? def.Profile.Blurb : def.Persona;
                    return (Runner: new AgentRunner(def.Name, persona, llmClient, logger, _diagnostics),
                            Profile: def.Profile, Color: color, Hex: hex);
                })
                .ToList();

            var startPositions = _mapGenerator.AgentStartPositions
                .OrderBy(_ => Random.Shared.Next())
                .ToList();
            for (int i = 0; i < runners.Count; i++)
            {
                var (runner, profile, _, hex) = runners[i];
                var (sx, sy) = startPositions[i < startPositions.Count ? i : i % startPositions.Count];

                // Register personality before InitializeAgent so GetContext can read it immediately
                var p = profile ?? MultiAgentSimWeb.Models.PersonalityProfile.Default;
                world.SetPersonality(runner.Name, p);

                world.InitializeAgent(runner.Name, sx, sy);
                var spawnCell = world.GetCell(sx, sy);
                int spawnFloor = 1; // always start on ground level
                world.SetAgentFloor(runner.Name, spawnFloor);
                AgentColorHex[runner.Name] = hex;

                // Apply personality-driven starting mood and stress
                var startMood = world.GetMood(runner.Name);
                startMood.AdjustMood(p.InitialMoodOffset);
                float stressAdj = p.InitialStress - 15f; // 15 is the AgentMood default
                if (stressAdj != 0) startMood.AdjustStress(stressAdj);

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
                Events.Enqueue(new SimEvent { Type = "round", Content = round.ToString(), Label = world.CurrentTime });

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

                // Dawn boost fires once per Night→Dawn transition (before any agent turn)
                world.TickDawnBoost();

                // Weather advances once per round (before agents act so context is current)
                world.TickWeather();

                // Animal tick runs before any agent turn so agents see fresh positions
                world.TickAnimals();
                world.TickRespawn();
                foreach (var ae in world.DrainAnimalEvents())
                {
                    ae.Round = round;
                    Events.Enqueue(ae);
                }

                await NotifyAsync();

                var deadThisRound = new List<string>();

                foreach (var (runner, _, color, _) in runners)
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
                        Events.Enqueue(new SimEvent { Type = "error", Label = "error", AgentName = runner.Name, AgentColor = color, Content = $"ActAsync error: {ex.GetType().Name}: {ex.Message}" });
                        action = new AgentAction { Action = "nothing" };
                    }

                    // Suppress speech when no one can hear (same-unit indoors, radius-1 outdoors).
                    var (ax, ay) = world.GetAgentPosition(runner.Name);
                    bool anyoneNearby = world.GetVisibleAgents(runner.Name).Count > 0;
                    if (!anyoneNearby)
                        action.Speech = string.Empty;

                    try
                    {
                        world.AddEvent(runner.Name, action);
                    }
                    catch (Exception ex)
                    {
                        Events.Enqueue(new SimEvent { Type = "error", Label = "error", AgentName = runner.Name, AgentColor = color, Content = $"AddEvent error: {ex.GetType().Name}: {ex.Message}" });
                    }
                    CurrentAgent = null;

                    if (!string.IsNullOrWhiteSpace(action.Thought))
                        Events.Enqueue(new SimEvent { Type = "thought", AgentName = runner.Name, AgentColor = color, Label = "thinks", Content = action.Thought });

                    if (!string.IsNullOrWhiteSpace(action.Speech))
                        Events.Enqueue(new SimEvent { Type = "speech", AgentName = runner.Name, AgentColor = color, Label = "says", Content = $"\"{action.Speech}\"" });

                    if (!string.IsNullOrWhiteSpace(action.Action) && action.Action != "nothing")
                        Events.Enqueue(new SimEvent { Type = "action", AgentName = runner.Name, AgentColor = color, Label = "does", Content = action.Action });

                    try
                    {
                        if (!string.IsNullOrWhiteSpace(action.MoveTo))
                        {
                            var moved = world.MoveAgent(runner.Name, action.MoveTo);
                            if (moved)
                            {
                                var (nx, ny) = world.GetAgentPosition(runner.Name);
                                Events.Enqueue(new SimEvent { Type = "move", AgentName = runner.Name, AgentColor = color, Label = "moves", Content = $"{action.MoveTo.ToUpperInvariant()} → ({nx},{ny})" });
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
                                Events.Enqueue(new SimEvent { Type = "move", AgentName = runner.Name, AgentColor = color, Content = $"climbs to floor {currentFloor + 1} of {maxFloors}", Round = round });
                            }
                            else if (action.MoveFloor == "down" && currentFloor > 1)
                            {
                                world.SetAgentFloor(runner.Name, currentFloor - 1);
                                world.LogAt(fpos.x, fpos.y, $"{runner.Name} descended to floor {currentFloor - 1}");
                                Events.Enqueue(new SimEvent { Type = "move", AgentName = runner.Name, AgentColor = color, Content = $"descends to floor {currentFloor - 1}", Round = round });
                            }
                        }

                        if (action.Scavenge)
                        {
                            var scavengeResult = world.TryForage(runner.Name);
                            if (scavengeResult is not null)
                                Events.Enqueue(new SimEvent { Type = "scavenge", AgentName = runner.Name, AgentColor = color, Label = "scavenges", Content = scavengeResult });
                        }

                        if (action.DrinkTap)
                        {
                            var tapResult = world.TryDrinkTap(runner.Name);
                            if (tapResult is not null)
                                Events.Enqueue(new SimEvent { Type = "drink_tap", AgentName = runner.Name, AgentColor = color, Label = "drinks", Content = tapResult });
                        }

                        if (action.DrinkFountain)
                        {
                            var fountainResult = world.TryDrinkFountain(runner.Name);
                            if (fountainResult is not null)
                                Events.Enqueue(new SimEvent { Type = SimEventTypes.DrinkFountain, AgentName = runner.Name, AgentColor = color, Label = "drinks", Content = fountainResult });
                        }

                        if (action.DrinkRiver)
                        {
                            var riverResult = world.TryDrinkRiver(runner.Name);
                            if (riverResult is not null)
                                Events.Enqueue(new SimEvent { Type = SimEventTypes.DrinkRiver, AgentName = runner.Name, AgentColor = color, Label = "drinks", Content = riverResult });
                        }

                        if (action.Fish)
                        {
                            var fishResult = world.TryFish(runner.Name);
                            if (fishResult is not null)
                                Events.Enqueue(new SimEvent { Type = SimEventTypes.Fish, AgentName = runner.Name, AgentColor = color, Label = "fishes", Content = fishResult });
                        }

                        if (!string.IsNullOrWhiteSpace(action.Cook))
                        {
                            var cookResult = world.TryCook(runner.Name, action.Cook.Trim());
                            if (cookResult is not null)
                                Events.Enqueue(new SimEvent { Type = SimEventTypes.Cook, AgentName = runner.Name, AgentColor = color, Label = "cooks", Content = cookResult });
                        }

                        var itemEventContent = DispatchItemAction(world, runner.Name, action);
                        if (itemEventContent is not null)
                            Events.Enqueue(new SimEvent { Type = "item", AgentName = runner.Name, AgentColor = color, Label = action.ItemAction, Content = itemEventContent });

                        var animalEventContent = DispatchAnimalAction(world, runner.Name, action);
                        if (animalEventContent is not null)
                            Events.Enqueue(new SimEvent { Type = "animal", AgentName = runner.Name, AgentColor = color, Label = action.AnimalAction, Content = animalEventContent, Round = round });

                        if (!string.IsNullOrWhiteSpace(action.AddressAgent) &&
                            !string.IsNullOrWhiteSpace(action.Speech) &&
                            world.KnowsName(runner.Name, action.AddressAgent.Trim()))
                        {
                            var target = action.AddressAgent.Trim();
                            world.QueueDirectMessage(new DirectMessage
                            {
                                FromAgent = runner.Name,
                                ToAgent   = target,
                                Message   = action.Speech,
                                Round     = round
                            });
                            Events.Enqueue(new SimEvent
                            {
                                Type       = SimEventTypes.DirectMessage,
                                AgentName  = runner.Name,
                                AgentColor = color,
                                Label      = $"→ {target}",
                                Content    = $"\"{action.Speech}\"",
                                Round      = round
                            });
                        }

                        // ── Group actions ──────────────────────────────────────────────────
                        DispatchGroupAction(world, runner.Name, color, action, round);

                        // Boredom: any meaningful action resets the idle counter.
                        bool wasActive = !string.IsNullOrWhiteSpace(action.MoveTo)
                                      || !string.IsNullOrEmpty(action.MoveFloor)
                                      || action.Scavenge
                                      || action.DrinkTap || action.DrinkFountain || action.DrinkRiver
                                      || action.Fish
                                      || !string.IsNullOrWhiteSpace(action.Cook)
                                      || !string.IsNullOrWhiteSpace(action.Speech)
                                      || (action.ItemAction != "none" && !string.IsNullOrWhiteSpace(action.ItemAction))
                                      || (action.AnimalAction != "none" && !string.IsNullOrWhiteSpace(action.AnimalAction))
                                      || !string.IsNullOrWhiteSpace(action.CraftRecipeId);
                        world.RecordActivity(runner.Name, wasActive);

                        bool died = world.TickMeters(runner.Name);
                        if (!died) world.TickMood(runner.Name);
                        if (!died) world.TickPresence(runner.Name);
                        if (!died) world.TickDayNight(runner.Name);
                        if (!died) world.TickWeatherEffects(runner.Name);
                        if (!died) world.TickStamina(runner.Name);
                        if (died)
                        {
                            var cause = world.Survival.DeathCause(runner.Name);
                            Events.Enqueue(new SimEvent { Type = "death", AgentName = runner.Name, AgentColor = color, Label = "dies", Content = $"{runner.Name} has died of {cause}.", Round = round });

                            // Grief: surviving agents who knew the deceased react emotionally,
                            // scaled by how much they trusted them.
                            // Skip agents within radius 2 of the death cell — KillAgent's witness
                            // sweep already applies a proximity-based mood/stress hit to them, so
                            // adding trust-grief on top would double-penalise nearby agents.
                            var deathPos = world.GetAgentPosition(runner.Name);
                            foreach (var survivor in world.AgentNames)
                            {
                                if (survivor == runner.Name) continue;
                                if (!world.Mood.Has(survivor)) continue;
                                float trust = world.GetMood(survivor).GetTrust(runner.Name);
                                if (trust <= 0f) continue;   // indifferent or hostile — no grief

                                // Already handled by KillAgent's proximity sweep.
                                var (sx, sy) = world.GetAgentPosition(survivor);
                                if (deathPos.x >= 0 && sx >= 0 &&
                                    Math.Max(Math.Abs(sx - deathPos.x), Math.Abs(sy - deathPos.y)) <= 2)
                                    continue;

                                float moodHit   = trust > 70f ? -20f
                                                : trust > 50f ? -14f
                                                : trust > 20f ?  -8f
                                                :                -4f;
                                float stressHit = trust > 70f ? +18f
                                                : trust > 50f ? +12f
                                                : trust > 20f ?  +7f
                                                :                +3f;

                                world.GetMood(survivor).AdjustMood(moodHit);
                                world.GetMood(survivor).AdjustStress(stressHit);
                                world.Memory.AddMemory(survivor,
                                    $"{runner.Name} has died. I knew them.");
                                world.LogDev($"[{survivor}] grief for {runner.Name} → mood {moodHit:+0;-0}  stress {stressHit:+0;-0}  (trust was {trust:F0}, distant grief)");
                            }

                            world.KillAgent(runner.Name);
                            world.Groups.LeaveGroup(runner.Name);
                            AgentColorHex.Remove(runner.Name);
                            deadThisRound.Add(runner.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        Events.Enqueue(new SimEvent { Type = "error", Label = "error", AgentName = runner.Name, AgentColor = color, Content = $"Turn error: {ex.GetType().Name}: {ex.Message}" });
                    }

                    foreach (var msg in world.DrainDevLog())
                        Events.Enqueue(new SimEvent { Type = "dev", Label = "dev", Content = msg });

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
                foreach (var (runner, _, color, _) in runners)
                {
                    if (ct.IsCancellationRequested) break;
                    if (world.GetAgentPosition(runner.Name) == (-1, -1)) continue;
                    if (!world.HasPendingMessages(runner.Name)) continue;

                    var pending = world.DrainPendingMessages(runner.Name);
                    try
                    {
                        await RunConversationAsync(world, runners, runner, color, pending, round, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Events.Enqueue(new SimEvent { Type = "error", Label = "error", AgentName = runner.Name, AgentColor = color, Content = $"Conversation error: {ex.GetType().Name}: {ex.Message}" });
                    }

                    foreach (var msg in world.DrainDevLog())
                        Events.Enqueue(new SimEvent { Type = "dev", Label = "dev", Content = msg });

                    await NotifyAsync();
                    await WaitIfPaused(ct);
                }

                runners.RemoveAll(r => deadThisRound.Contains(r.Runner.Name));

                // ── Group memory sharing ──────────────────────────────────────────────
                ShareGroupMemories(world, runners);

                // Expire group invites that weren't accepted this round.
                world.Groups.ExpireInvites(round + 1);

                // Emit per-round diagnostics summary
                if (_diagnostics.TotalCalls > 0)
                    Events.Enqueue(new SimEvent
                    {
                        Type    = SimEventTypes.Diag,
                        Label   = "diag",
                        Content = _diagnostics.GetRoundSummary(),
                        Round   = round,
                    });

                if (runners.Count == 0)
                {
                    Events.Enqueue(new SimEvent { Type = "round", Content = "All agents have perished." });
                    await NotifyAsync();
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
            // Normal stop — swallow.
        }
        catch (Exception ex)
        {
            Events.Enqueue(new SimEvent
            {
                Type    = "error",
                Label   = "error",
                Content = $"Simulation crashed: {ex.GetType().Name}: {ex.Message}",
            });
        }
        finally
        {
            // Emit final diagnostics report
            if (_diagnostics.TotalCalls > 0)
                Events.Enqueue(new SimEvent
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

    private void DispatchGroupAction(WorldState world, string agentName, string agentColor, AgentAction action, int round)
    {
        // Leave group
        if (action.LeaveGroup)
        {
            // Snapshot members before removal so we can notify them.
            var formerMembers = world.Groups.GetGroup(agentName)?.Members.ToList();
            var leftName = world.Groups.LeaveGroup(agentName);
            if (leftName != null)
            {
                Events.Enqueue(new SimEvent
                {
                    Type      = SimEventTypes.Group,
                    AgentName = agentName,
                    AgentColor= agentColor,
                    Label     = "leaves group",
                    Content   = $"{agentName} left \"{leftName}\"",
                    Round     = round,
                });
                world.Memory.AddMemory(agentName, $"I left the group \"{leftName}\".");

                // Remaining members who know the leaver lose trust in them.
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
            return; // can't simultaneously propose or accept
        }

        // Accept a pending invite
        if (action.AcceptGroupInvite)
        {
            var joined = world.Groups.AcceptInvite(agentName);
            if (joined != null)
            {
                Events.Enqueue(new SimEvent
                {
                    Type      = SimEventTypes.Group,
                    AgentName = agentName,
                    AgentColor= agentColor,
                    Label     = "joins group",
                    Content   = $"{agentName} joined \"{joined.Name}\" (members: {string.Join(", ", joined.Members)})",
                    Round     = round,
                });
                world.Memory.AddMemory(agentName, $"I joined the group \"{joined.Name}\".");
                foreach (var member in joined.Members.Where(m => m != agentName))
                    world.Memory.AddMemory(member, $"{agentName} joined our group \"{joined.Name}\".");
            }
            return;
        }

        // Propose / invite
        if (!string.IsNullOrWhiteSpace(action.GroupPropose) && !string.IsNullOrWhiteSpace(action.AddressAgent))
        {
            var target = action.AddressAgent.Trim();
            // Must know the target's name and be in earshot
            if (!world.KnowsName(agentName, target)) return;
            if (!world.GetVisibleAgents(agentName).Any(a => a.name == target)) return;
            // Must have a genuinely positive attitude toward the target — trust strictly above neutral
            float trust = world.Mood.Has(agentName) ? world.GetMood(agentName).GetTrust(target) : 0f;
            if (trust <= 0f) return;

            var existingGroupId = world.Groups.GetGroupId(agentName);
            string groupId, groupName;
            if (existingGroupId != null)
            {
                // Already in a group — invite the target to join it
                groupId   = existingGroupId;
                groupName = world.Groups.GetGroup(agentName)!.Name;
            }
            else
            {
                // Create a new group
                groupName = action.GroupPropose.Trim();
                groupId   = world.Groups.CreateGroup(agentName, groupName);
                Events.Enqueue(new SimEvent
                {
                    Type      = SimEventTypes.Group,
                    AgentName = agentName,
                    AgentColor= agentColor,
                    Label     = "forms group",
                    Content   = $"{agentName} formed the group \"{groupName}\"",
                    Round     = round,
                });
                world.Memory.AddMemory(agentName, $"I formed the group \"{groupName}\" and invited {target}.");
            }

            world.Groups.SendInvite(agentName, target, groupId, groupName);
            Events.Enqueue(new SimEvent
            {
                Type      = SimEventTypes.Group,
                AgentName = agentName,
                AgentColor= agentColor,
                Label     = "invites",
                Content   = $"{agentName} invited {target} to \"{groupName}\"",
                Round     = round,
            });
        }

        // Waypoint
        if (!string.IsNullOrWhiteSpace(action.GroupSetWaypoint))
        {
            var grp = world.Groups.GetGroup(agentName);
            if (grp != null)
            {
                var pos = world.GetAgentPosition(agentName);
                grp.Waypoint = (pos.x, pos.y, action.GroupSetWaypoint.Trim(), agentName);
                Events.Enqueue(new SimEvent
                {
                    Type      = SimEventTypes.Group,
                    AgentName = agentName,
                    AgentColor= agentColor,
                    Label     = "sets waypoint",
                    Content   = $"{agentName} marked \"{action.GroupSetWaypoint.Trim()}\" ({pos.x},{pos.y}) as the group meeting point",
                    Round     = round,
                });
            }
        }

        // Propose vote
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
                Events.Enqueue(new SimEvent
                {
                    Type      = SimEventTypes.Group,
                    AgentName = agentName,
                    AgentColor= agentColor,
                    Label     = "calls vote",
                    Content   = $"{agentName} called a group vote: \"{action.GroupVotePropose.Trim()}\"",
                    Round     = round,
                });
            }
        }

        // Cast vote
        if (!string.IsNullOrWhiteSpace(action.GroupVote))
        {
            var grp = world.Groups.GetGroup(agentName);
            if (grp?.ActiveVote != null && !grp.ActiveVote.Votes.ContainsKey(agentName))
            {
                grp.ActiveVote.Votes[agentName] = action.GroupVote.Trim().ToLowerInvariant();
                Events.Enqueue(new SimEvent
                {
                    Type      = SimEventTypes.Group,
                    AgentName = agentName,
                    AgentColor= agentColor,
                    Label     = "votes",
                    Content   = $"{agentName} voted \"{action.GroupVote.Trim()}\" on \"{grp.ActiveVote.Question}\"",
                    Round     = round,
                });
            }
        }
    }

    /// Cross-pollinate one recent memory between group members who are in earshot, and tally completed votes.
    private void ShareGroupMemories(WorldState world, List<(AgentRunner Runner, PersonalityProfile? Profile, string Color, string Hex)> runners)
    {
        foreach (var group in world.Groups.AllGroups.ToList())
        {
            var alive = group.Members.Where(m => world.GetAgentPosition(m) != (-1, -1)).ToList();

            // Tally vote if all alive members have cast one (or it's been open one round already)
            if (group.ActiveVote is { } vote && alive.Count > 0)
            {
                bool allVoted = alive.All(m => vote.Votes.ContainsKey(m));
                if (allVoted || vote.ProposedRound < world.CurrentRound)
                {
                    int yes = vote.Votes.Values.Count(v => v == "yes");
                    int no  = vote.Votes.Values.Count(v => v == "no");
                    string outcome = yes > no ? "PASSED" : no > yes ? "REJECTED" : "TIED";
                    string summary = $"Group vote on \"{vote.Question}\": {yes} yes / {no} no — {outcome}";
                    foreach (var m in alive)
                        world.Memory.AddMemory(m, summary);
                    Events.Enqueue(new SimEvent
                    {
                        Type    = SimEventTypes.Group,
                        Label   = "vote result",
                        Content = $"\"{group.Name}\": {summary}",
                    });
                    group.ActiveVote = null;
                }
            }

            if (alive.Count < 2) continue;

            foreach (var a in alive)
            {
                foreach (var b in alive)
                {
                    if (a == b) continue;
                    if (!world.GetVisibleAgents(a).Any(v => v.name == b)) continue;

                    var bMemory = world.Memory.GetMemory(b);
                    if (bMemory.Recent.Count == 0) continue;
                    var snippet  = bMemory.Recent[^1];
                    var prefixed = $"[Group — {b} told me]: {snippet}";
                    var aMemory  = world.Memory.GetMemory(a);
                    if (!aMemory.Recent.Contains(prefixed))
                        world.Memory.AddMemory(a, prefixed);
                }
            }
        }
    }

    // Max back-and-forth exchanges in a single conversation (1 = one reply each, no continuation).
    private const int MaxConversationExchanges = 3;

    private async Task RunConversationAsync(
        WorldState world,
        List<(AgentRunner Runner, PersonalityProfile? Profile, string Color, string Hex)> runners,
        AgentRunner respondent,
        string respondentColor,
        IReadOnlyList<DirectMessage> pending,
        int round,
        CancellationToken ct)
    {
        // Build the initial history from incoming messages.
        var history = new List<ConversationLine>(
            pending.Select(m => new ConversationLine(m.FromAgent, m.Message)));

        // First reply from the respondent.
        AgentResponse firstResponse;
        try
        {
            firstResponse = await respondent.RespondAsync(
                pending, world.GetContext(respondent.Name), ct,
                from => world.DescribeAgent(respondent.Name, from));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Events.Enqueue(new SimEvent { Type = "error", Label = "error", AgentName = respondent.Name, AgentColor = respondentColor, Content = $"RespondAsync error: {ex.GetType().Name}: {ex.Message}" });
            return;
        }

        ApplyDirectResponse(world, respondent.Name, respondentColor, pending, firstResponse, round);

        // Silent snub: respondent was directly addressed but said nothing — every sender
        // who knows them loses a little trust and registers the slight.
        if (string.IsNullOrWhiteSpace(firstResponse.Speech))
        {
            foreach (var msg in pending)
            {
                var sender = msg.FromAgent;
                if (!world.KnowsName(sender, respondent.Name)) continue;
                if (!world.Mood.Has(sender)) continue;
                world.GetMood(sender).AdjustTrust(respondent.Name, -7f);
                world.GetMood(sender).AdjustStress(+3f);
                world.Memory.AddMemory(sender,
                    $"{world.DescribeAgent(sender, respondent.Name)} completely ignored me when I spoke to them.");
                world.LogDev($"[{sender}] trust[{respondent.Name}] -7  stress +3  (silent snub)");
            }
            return;
        }
        history.Add(new ConversationLine(respondent.Name, firstResponse.Speech));

        // Let bystanders overhear the respondent's first reply.
        var conversationParticipants = pending.Select(m => m.FromAgent).Prepend(respondent.Name).ToHashSet();
        OverhearSpeech(world, runners, respondent.Name, firstResponse.Speech, conversationParticipants);

        // Continue the conversation up to MaxConversationExchanges - 1 more times.
        for (int exchange = 1; exchange < MaxConversationExchanges; exchange++)
        {
            ct.ThrowIfCancellationRequested();

            // Determine who speaks next: the other party from the last message.
            var lastSpeaker = history[^1].Speaker;
            var nextSpeaker = lastSpeaker == respondent.Name ? pending[0].FromAgent : respondent.Name;

            // Make sure the next speaker is still alive and in earshot.
            if (world.GetAgentPosition(nextSpeaker) == (-1, -1)) break;
            if (!world.GetVisibleAgents(respondent.Name).Any(a => a.name == nextSpeaker)) break;

            var nextRunner = runners.Find(r => r.Runner.Name == nextSpeaker);
            if (nextRunner == default) break;

            AgentResponse reply;
            try
            {
                reply = await nextRunner.Runner.ContinueConversationAsync(
                    history,
                    name => world.DescribeAgent(nextSpeaker, name),
                    world.GetContext(nextSpeaker),
                    ct);
            }
            catch (Exception ex)
            {
                Events.Enqueue(new SimEvent { Type = "error", Label = "error", AgentName = nextSpeaker, AgentColor = nextRunner.Color, Content = $"Conversation error: {ex.GetType().Name}: {ex.Message}" });
                break;
            }

            if (!string.IsNullOrWhiteSpace(reply.Thought))
                Events.Enqueue(new SimEvent { Type = "thought", AgentName = nextSpeaker, AgentColor = nextRunner.Color, Label = "thinks", Content = reply.Thought });

            if (string.IsNullOrWhiteSpace(reply.Speech)) break;

            // Emit the right event type depending on direction.
            bool nextIsRespondent = nextSpeaker == respondent.Name;
            Events.Enqueue(new SimEvent
            {
                Type      = nextIsRespondent ? SimEventTypes.DirectResponse : SimEventTypes.DirectMessage,
                AgentName = nextSpeaker,
                AgentColor= nextRunner.Color,
                Label     = nextIsRespondent
                    ? "replies"
                    : $"→ {world.DescribeAgent(nextSpeaker, respondent.Name)}",
                Content   = $"\"{reply.Speech}\"",
                Round     = round,
            });

            world.LogAt(world.GetAgentPosition(nextSpeaker).x,
                        world.GetAgentPosition(nextSpeaker).y,
                        $"{nextSpeaker}: \"{reply.Speech}\"");

            if (world.Mood.Has(nextSpeaker))
            {
                world.GetMood(nextSpeaker).AdjustMood(+2f);
                world.GetMood(nextSpeaker).AdjustStress(-1f);
            }

            // Let bystanders overhear each exchange in the conversation.
            OverhearSpeech(world, runners, nextSpeaker, reply.Speech, conversationParticipants);

            history.Add(new ConversationLine(nextSpeaker, reply.Speech));
        }
    }

    /// <summary>
    /// Lets nearby agents who aren't part of the conversation overhear a line of speech.
    /// Each bystander within visibility range gains a memory of what was said.
    /// </summary>
    private void OverhearSpeech(
        WorldState world,
        List<(AgentRunner Runner, PersonalityProfile? Profile, string Color, string Hex)> runners,
        string speaker,
        string speech,
        HashSet<string> participants)
    {
        if (string.IsNullOrWhiteSpace(speech)) return;

        foreach (var (runner, _, color, _) in runners)
        {
            var bystander = runner.Name;
            if (participants.Contains(bystander)) continue;
            if (world.GetAgentPosition(bystander) == (-1, -1)) continue;

            // Only overhear if the bystander can see the speaker.
            if (!world.GetVisibleAgents(bystander).Any(a => a.name == speaker)) continue;

            world.Memory.AddMemory(bystander,
                $"Overheard {world.DescribeAgent(bystander, speaker)} say: \"{speech}\"");
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
            Events.Enqueue(new SimEvent
            {
                Type = "thought", AgentName = responderName, AgentColor = responderColor,
                Label = "thinks", Content = response.Thought
            });

        if (string.IsNullOrWhiteSpace(response.Speech)) return;

        Events.Enqueue(new SimEvent
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
            world.LogDev($"[{responderName}] responded directly \u2192 mood +3  stress -2");
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
                        $"\u2192 trust[{responderName}] +5  mood +2");
                }

                if (world.Mood.Has(responderName))
                {
                    world.GetMood(responderName).AdjustTrust(msg.FromAgent, +3f);
                    world.LogDev(
                        $"[{responderName}] trust[{msg.FromAgent}] +3 (replied to them)");
                }

                // Gossip: 25% chance the respondent's strong opinions about a third party
                // colour the sender's perception of that person (diluted, second-hand trust shift).
                if (world.Mood.Has(responderName) && world.Mood.Has(msg.FromAgent) && _rng.NextDouble() < 0.25)
                {
                    var responderTrust = world.GetMood(responderName).AllTrust;
                    var gossipTarget = responderTrust
                        .Where(kv => kv.Key != msg.FromAgent
                                  && Math.Abs(kv.Value) > 35f
                                  && world.Mood.Has(kv.Key)
                                  && world.KnowsName(msg.FromAgent, kv.Key))
                        .OrderByDescending(kv => Math.Abs(kv.Value))
                        .Cast<KeyValuePair<string, float>?>()
                        .FirstOrDefault();

                    if (gossipTarget.HasValue)
                    {
                        var (targetName, rawTrust) = (gossipTarget.Value.Key, gossipTarget.Value.Value);
                        float inherited = rawTrust * 0.20f;
                        world.GetMood(msg.FromAgent).AdjustTrust(targetName, inherited);
                        string sentiment = rawTrust > 0 ? "warmly" : "warily";
                        world.Memory.AddMemory(msg.FromAgent,
                            $"{world.DescribeAgent(msg.FromAgent, responderName)} spoke {sentiment} about {targetName} \u2014 adjusting my view of them.");
                        world.LogDev($"[{msg.FromAgent}] gossip from {responderName} about {targetName} \u2192 trust {inherited:+0.#;-0.#} (source trust was {rawTrust:+0;-0})");
                    }
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
                if (ok)       return $"deconstructs \u2192 {string.Join(", ", yields)}";
                if (consumed) return "deconstruct failed \u2014 item crumbled to nothing";
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
        if (_stepRoundRequested) return;
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
