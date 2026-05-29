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

    // Agents who can perceive events fired during the current agent's turn.
    // null between turns so events default to global (always visible).
    private HashSet<string>? _turnWitnesses;

    private HashSet<string> ComputeWitnesses(WorldState world, string agentName)
    {
        var set = new HashSet<string> { agentName };
        foreach (var (name, _, _) in world.GetVisibleAgents(agentName))
            set.Add(name);
        return set;
    }

    // Tags a SimEvent with the current turn's witness set, or leaves it global if null.
    private SimEvent W(SimEvent e)
    {
        if (_turnWitnesses != null)
            e.WitnessedBy = new HashSet<string>(_turnWitnesses);
        return e;
    }

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

    /// When set, non-followed agents skip MinTurnMs so the followed agent's turn arrives faster.
    public string? FollowedAgent { get; set; }

    /// Fired after each meaningful state change. UI should marshal to its
    /// render thread (e.g. InvokeAsync(StateHasChanged)).
    public event Func<Task>? OnStateChanged;

    private CancellationTokenSource? _cts;
    private bool _stepRequested;
    private bool _stepRoundRequested;
    private int  _pendingRoundSteps;

    public void Pause()  { IsPaused = true; }
    public void Resume() { IsPaused = false; _stepRequested = false; _stepRoundRequested = false; _pendingRoundSteps = 0; }

    /// Runs all remaining agents in the current round, then pauses at the start of the next.
    public void StepRound() { _pendingRoundSteps = 1; _stepRoundRequested = true; _stepRequested = true; }

    /// Runs <paramref name="count"/> complete rounds, then pauses.
    public void StepRounds(int count) { _pendingRoundSteps = Math.Max(1, count); _stepRoundRequested = true; _stepRequested = true; }

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

                    // If more rounds are queued, consume one and keep going without pausing.
                    if (_pendingRoundSteps > 1)
                    {
                        _pendingRoundSteps--;
                        _stepRoundRequested = true;
                        if (ct.IsCancellationRequested) break;
                        continue;
                    }
                    _pendingRoundSteps = 0;

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

                // ── Agent turns ──────────────────────────────────────────────────────
                // In follow mode, agents far from the watched agent run their LLM calls
                // concurrently (world mutations are still applied sequentially afterward).
                if (FollowedAgent != null && runners.Count > 1)
                {
                    var (fx, fy) = world.GetAgentPosition(FollowedAgent);

                    var parallelBatch    = new List<(AgentRunner Runner, string Color)>();
                    var sequentialNames  = new HashSet<string>();

                    foreach (var (runner, _, color, _) in runners)
                    {
                        if (world.GetAgentPosition(runner.Name) == (-1, -1)) continue;
                        if (runner.Name == FollowedAgent) { sequentialNames.Add(runner.Name); continue; }

                        bool nearFollowed = false;
                        if (fx >= 0)
                        {
                            var (ax, ay) = world.GetAgentPosition(runner.Name);
                            nearFollowed = Math.Abs(ax - fx) + Math.Abs(ay - fy) <= ParallelProximityThreshold;
                            if (!nearFollowed)
                            {
                                var fb = world.GetCell(fx, fy).BuildingName;
                                nearFollowed = fb != null && fb == world.GetCell(ax, ay).BuildingName;
                            }
                        }

                        if (nearFollowed) sequentialNames.Add(runner.Name);
                        else             parallelBatch.Add((runner, color));
                    }

                    // ── Parallel batch: concurrent LLM calls, sequential apply ────────
                    if (parallelBatch.Count > 0 && !ct.IsCancellationRequested)
                    {
                        var ctxPairs = parallelBatch
                            .Select(r => (r.Runner, r.Color, Ctx: world.GetContext(r.Runner.Name)))
                            .ToList();

                        var actionTasks = ctxPairs.Select(async p =>
                        {
                            AgentAction act;
                            try   { act = await p.Runner.ActAsync(p.Ctx, ct); }
                            catch (OperationCanceledException) when (ct.IsCancellationRequested)
                                  { return (p.Runner, p.Color, Action: (AgentAction?)null); }
                            catch (Exception ex)
                            {
                                Events.Enqueue(new SimEvent { Type = "error", Label = "error", AgentName = p.Runner.Name, AgentColor = p.Color, Content = $"ActAsync error: {ex.GetType().Name}: {ex.Message}" });
                                act = new AgentAction { Action = "nothing" };
                            }
                            return (p.Runner, p.Color, Action: (AgentAction?)act);
                        }).ToList();

                        var results = await Task.WhenAll(actionTasks);

                        foreach (var (runner, color, action) in results)
                        {
                            if (ct.IsCancellationRequested || action == null) break;
                            ApplyAgentTurn(runner, color, action, world, round, deadThisRound);
                        }

                        await NotifyAsync();
                    }

                    // ── Sequential: followed agent + nearby agents ────────────────────
                    foreach (var (runner, _, color, _) in runners)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (!sequentialNames.Contains(runner.Name)) continue;
                        if (world.GetAgentPosition(runner.Name) == (-1, -1)) continue;

                        CurrentAgent = new ActiveAgent(runner.Name, color);
                        _turnWitnesses = ComputeWitnesses(world, runner.Name);
                        var turnStart = Stopwatch.GetTimestamp();
                        await NotifyAsync();

                        AgentAction action;
                        try
                        {
                            action = await runner.ActAsync(world.GetContext(runner.Name), ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                        catch (Exception ex)
                        {
                            Events.Enqueue(new SimEvent { Type = "error", Label = "error", AgentName = runner.Name, AgentColor = color, Content = $"ActAsync error: {ex.GetType().Name}: {ex.Message}" });
                            action = new AgentAction { Action = "nothing" };
                        }

                        CurrentAgent = null;
                        ApplyAgentTurn(runner, color, action, world, round, deadThisRound);
                        await NotifyAsync();

                        // Only the followed agent gets pace delay.
                        if (cfg.MinTurnMs > 0 && runner.Name == FollowedAgent && !ct.IsCancellationRequested)
                        {
                            var elapsed   = (int)Stopwatch.GetElapsedTime(turnStart).TotalMilliseconds;
                            var remaining = cfg.MinTurnMs - elapsed;
                            if (remaining > 0)
                                await Task.Delay(remaining, ct).ConfigureAwait(false);
                        }

                        await WaitIfPaused(ct);
                        if (ct.IsCancellationRequested) break;
                    }
                }
                else
                {
                    // ── Standard sequential loop (no follow mode) ────────────────────
                    foreach (var (runner, _, color, _) in runners)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (world.GetAgentPosition(runner.Name) == (-1, -1)) continue;

                        CurrentAgent = new ActiveAgent(runner.Name, color);
                        _turnWitnesses = ComputeWitnesses(world, runner.Name);
                        var turnStart = Stopwatch.GetTimestamp();
                        await NotifyAsync();

                        AgentAction action;
                        try
                        {
                            action = await runner.ActAsync(world.GetContext(runner.Name), ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                        catch (Exception ex)
                        {
                            Events.Enqueue(new SimEvent { Type = "error", Label = "error", AgentName = runner.Name, AgentColor = color, Content = $"ActAsync error: {ex.GetType().Name}: {ex.Message}" });
                            action = new AgentAction { Action = "nothing" };
                        }

                        CurrentAgent = null;
                        ApplyAgentTurn(runner, color, action, world, round, deadThisRound);
                        await NotifyAsync();

                        bool applyPace = cfg.MinTurnMs > 0
                            && (FollowedAgent == null || runner.Name == FollowedAgent)
                            && !ct.IsCancellationRequested;
                        if (applyPace)
                        {
                            var elapsed   = (int)Stopwatch.GetElapsedTime(turnStart).TotalMilliseconds;
                            var remaining = cfg.MinTurnMs - elapsed;
                            if (remaining > 0)
                                await Task.Delay(remaining, ct).ConfigureAwait(false);
                        }

                        await WaitIfPaused(ct);
                        if (ct.IsCancellationRequested) break;
                    }
                }

                // ── Response phase ────────────────────────────────────────────────────
                foreach (var (runner, _, color, _) in runners)
                {
                    if (ct.IsCancellationRequested) break;
                    if (world.GetAgentPosition(runner.Name) == (-1, -1)) continue;
                    if (!world.HasPendingMessages(runner.Name)) continue;

                    var pending = world.DrainPendingMessages(runner.Name);
                    _turnWitnesses = ComputeWitnesses(world, runner.Name);
                    try
                    {
                        await RunConversationAsync(world, runners, runner, color, pending, round, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        _turnWitnesses = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        Events.Enqueue(new SimEvent { Type = "error", Label = "error", AgentName = runner.Name, AgentColor = color, Content = $"Conversation error: {ex.GetType().Name}: {ex.Message}" });
                    }

                    _turnWitnesses = null;
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

                // Keep the event queue bounded so UI renders don't get progressively
                // heavier as events accumulate over a long run. Drop the oldest entries
                // once the queue exceeds the limit — recent events are what the UI shows.
                TrimEvents();

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
            IsRunning     = false;
            IsPaused      = false;
            FollowedAgent = null;
            _stepRequested      = false;
            _stepRoundRequested = false;
            _cts?.Dispose();
            _cts = null;
            await NotifyAsync();
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
                Events.Enqueue(W(new SimEvent { Type = "thought", AgentName = nextSpeaker, AgentColor = nextRunner.Color, Label = "thinks", Content = reply.Thought }));

            if (string.IsNullOrWhiteSpace(reply.Speech)) break;

            // Emit the right event type depending on direction.
            bool nextIsRespondent = nextSpeaker == respondent.Name;
            Events.Enqueue(W(new SimEvent
            {
                Type      = nextIsRespondent ? SimEventTypes.DirectResponse : SimEventTypes.DirectMessage,
                AgentName = nextSpeaker,
                AgentColor= nextRunner.Color,
                Label     = nextIsRespondent
                    ? "replies"
                    : $"→ {world.DescribeAgent(nextSpeaker, respondent.Name)}",
                Content   = $"\"{reply.Speech}\"",
                Round     = round,
            }));

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
            Events.Enqueue(W(new SimEvent
            {
                Type = "thought", AgentName = responderName, AgentColor = responderColor,
                Label = "thinks", Content = response.Thought
            }));

        if (string.IsNullOrWhiteSpace(response.Speech)) return;

        Events.Enqueue(W(new SimEvent
        {
            Type = "direct_response", AgentName = responderName, AgentColor = responderColor,
            Label = "replies", Content = $"\"{response.Speech}\"",
            Round = round
        }));

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

    // Agents within this Manhattan distance of the followed agent run sequentially.
    // Those farther away have their LLM calls batched in parallel.
    private const int ParallelProximityThreshold = 8;

    /// Applies a resolved AgentAction to the world (post-LLM, sequential).
    /// Sets and clears _turnWitnesses internally so W() tags events correctly.
    private void ApplyAgentTurn(
        AgentRunner runner, string color, AgentAction action,
        WorldState world, int round, List<string> deadThisRound)
    {
        bool anyoneNearby = world.GetVisibleAgents(runner.Name).Count > 0;
        if (!anyoneNearby)
            action.Speech = string.Empty;

        _turnWitnesses = ComputeWitnesses(world, runner.Name);

        try
        {
            world.AddEvent(runner.Name, action);
        }
        catch (Exception ex)
        {
            Events.Enqueue(new SimEvent { Type = "error", Label = "error", AgentName = runner.Name, AgentColor = color, Content = $"AddEvent error: {ex.GetType().Name}: {ex.Message}" });
        }

        foreach (var ev in ActionResolver.NarrativeEvents(runner.Name, color, action))
            Events.Enqueue(W(ev));

        try
        {
            foreach (var ev in ActionResolver.Resolve(world, runner.Name, color, action, round))
                Events.Enqueue(W(ev));

            foreach (var ev in GroupActionHandler.Handle(world, runner.Name, color, action, round))
                Events.Enqueue(W(ev));

            world.RecordActivity(runner.Name, action.IsActive);

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
                world.Mood.ProcessDeath(runner.Name);
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

        _turnWitnesses = null;
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

    // Maximum number of events to keep in the queue. Older entries are dropped
    // once this limit is exceeded so that UI renders stay O(1) in run length.
    private const int MaxEvents = 500;

    private void TrimEvents()
    {
        while (Events.Count > MaxEvents)
            Events.TryDequeue(out _);
    }
}
