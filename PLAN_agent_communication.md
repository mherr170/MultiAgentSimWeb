# Implementation Plan: Agent-to-Agent Direct Communication

## Overview

This plan adds a **direct address mechanic**: an agent can name a specific other agent in
their speech, that message is routed to the target as a `DirectMessage`, and the target
gets a focused **response turn** at the end of the round — after all primary turns have
completed — to reply in-character.

The result within a single round: A acts, addresses B → end-of-round response phase →
B reads A's direct message and responds → A's memory records B's reply → next round, A
can act on the response.

---

## The Core Design Decision: Two-Phase Round

The sequential turn model creates an ordering asymmetry: if A comes before B, B hasn't
acted yet when A addresses them. If B comes before A, B has already taken their turn.
A naive "inject message into GetContext during primary turns" would only work for one
ordering.

The chosen solution is a **two-phase round**:

- **Phase 1 (Primary turns)**: Each agent takes their normal full turn in turn order.
  `address_agent` fields are collected but the target agent is NOT given the message
  during their primary turn. Every agent takes a clean, uninterrupted primary turn.

- **Phase 2 (Response turns)**: After all primary turns complete, any agent who received
  one or more direct messages gets a short, focused LLM call to respond. Response turns
  produce only `thought` + `speech` — no movement, no item actions.

This eliminates the ordering problem entirely. Both "A before B" and "B before A" cases
are handled identically. Response turns always have full phase-1 world state as context.

---

## New Files

### 1. `Models/DirectMessage.cs`

A lightweight record capturing a queued message.

**Fields:**
- `string FromAgent` — sender name
- `string ToAgent` — recipient name
- `string Message` — the speech content (copied from `AgentAction.Speech`)
- `int Round` — round number (for logging and deduplication)

No methods. This is a pure data model.

---

### 2. `Models/AgentResponse.cs`

The JSON the LLM returns for a response turn. Intentionally slimmer than `AgentAction`
because response turns are narrowly scoped to the conversational exchange.

**Fields (all with `[JsonPropertyName]`):**
- `string Thought` — private reasoning, not broadcast
- `string Speech` — what the agent says in response (may be empty if silent)

Mirror the `[JsonPropertyName]` convention from `AgentAction.cs`. Include a static
`Fallback(string raw)` returning `new AgentResponse { Thought = "(parse error)", Speech = raw }`
to match the pattern in `AgentRunner`.

---

### 3. `Services/Systems/ICommunicationSystem.cs`

Follows the exact same interface contract as `IMemorySystem`, `IMoodSystem`, etc.

**Methods:**
```
void Attach(WorldState world);
void InitializeAgent(string agentName);
void RemoveAgent(string agentName);

void QueueDirectMessage(DirectMessage message);
bool HasPendingMessages(string agentName);
IReadOnlyList<DirectMessage> DrainPendingMessages(string agentName);
```

`DrainPendingMessages` clears the queue for that agent and returns what was there.
Calling it twice in the same round returns an empty list the second time.

---

### 4. `Services/Systems/CommunicationSystem.cs`

Default implementation of `ICommunicationSystem`.

**Internal state:**
- `Dictionary<string, List<DirectMessage>> _inboxes` — keyed by recipient name
- `WorldState _world` — attached reference (used for position validation)

**`QueueDirectMessage(DirectMessage message)` logic:**
1. If `message.FromAgent == message.ToAgent`, drop silently (self-address).
2. Look up both agents' positions via `_world.GetAgentPosition(...)`.
3. If either returns `(-1, -1)` (dead/not found), drop silently.
4. Compute Chebyshev distance. If `> 1` (not adjacent or co-located), drop the message
   and call `_world.LogDev($"[{fromAgent}] tried to address {toAgent} but they are
   too far away")`.
5. Otherwise, ensure `_inboxes[toAgent]` exists and append the message.

**`HasPendingMessages` / `DrainPendingMessages`:** straightforward dictionary operations.
`DrainPendingMessages` removes the list entirely (or clears it) before returning.

**`InitializeAgent`:** create an empty `List<DirectMessage>` entry.
**`RemoveAgent`:** remove the inbox entry.

---

## Modified Files

### 5. `Models/AgentAction.cs`

Add one new field at the end of the class:

```
[JsonPropertyName("address_agent")]
public string AddressAgent { get; set; } = "";
```

`AddressAgent` is the exact name of the agent being directly spoken to. Empty string
means the speech is general/broadcast. When non-empty, the speech is still public
(everyone nearby hears it in the location log) but the named agent is also queued for
a response turn.

No other fields change. Existing JSON payloads that omit `address_agent` deserialize
with the default empty string — fully backward compatible.

---

### 6. `Services/AgentRunner.cs`

**Two changes:**

**A. Update `SystemTemplate`**

In the JSON format block at the bottom of the template string, add one line after
`"item_give_to"`:
```
"address_agent": "exact name of an agent within 1 cell you are speaking directly to, or empty string"
```

Add a new paragraph before the JSON block (after the existing `"Respond ONLY..."` line):

```
DIRECT COMMUNICATION: To speak directly to a nearby agent and get a same-round reply,
set address_agent to their exact name (they must be within 1 cell). Your speech field
is what you say — it is still heard by everyone nearby, but the named agent will be
prompted to respond this round. Leave address_agent empty for general speech.
```

**B. Add `RespondAsync` method**

Signature:
```csharp
public async Task<AgentResponse> RespondAsync(
    IReadOnlyList<DirectMessage> messages,
    string worldContext,
    CancellationToken ct = default)
```

This method constructs a focused system prompt:

```
You are {Name}. {_persona}

You have received a direct message this turn. Reply in character.
You cannot move or take items in this response — only speak (or stay silent).

Respond ONLY in this exact JSON format — no other text, no markdown fences:
{
  "thought": "your private reaction",
  "speech": "what you say in response — empty string to stay silent"
}
```

The user prompt (the content passed to `CompleteAsync`) is built from the messages list:

```
{foreach message: "{fromAgent} says directly to you: \"{message.Message}\""}

Your current position and state (for context):
{worldContext}

It is your turn to respond to the above direct message(s).
```

If there are multiple messages (edge case: two agents both addressed B in the same
round), list them all in order so B can respond to both.

Call `_client.CompleteAsync(systemPrompt, userPrompt, maxTokens: 256, ct)` — deliberately
lower token budget than `ActAsync`'s 768, since this is a focused exchange.

Use the same `ExtractJson` helper (already `private static`) to parse the result.
Deserialize into `AgentResponse`. On `JsonException`, return `AgentResponse.Fallback(raw)`.

---

### 7. `Services/WorldState.cs`

**A. Add `ICommunicationSystem` as a system property**

Add to the class body (alongside `Survival`, `Mood`, `Memory`, `Items`, `Foraging`):
```csharp
public ICommunicationSystem Communication { get; }
```

**B. Update the full constructor**

Add `ICommunicationSystem communication` as the last parameter. Store it. Call
`communication.Attach(this)` at the end of the constructor body.

**C. Update the convenience constructor**

Add `new CommunicationSystem()` as the last argument in the `: this(...)` call.

**D. Update `InitializeAgent`**

Add `Communication.InitializeAgent(agentName)` after the other system calls.

**E. Update `KillAgent`**

Add `Communication.RemoveAgent(agentName)` after the other `RemoveAgent` calls.

**F. Add forwarding methods** (follow the same pattern as `GetMood`, `AddMemory`, etc.)

```csharp
public void QueueDirectMessage(DirectMessage msg) =>
    Communication.QueueDirectMessage(msg);

public bool HasPendingMessages(string agentName) =>
    Communication.HasPendingMessages(agentName);

public IReadOnlyList<DirectMessage> DrainPendingMessages(string agentName) =>
    Communication.DrainPendingMessages(agentName);
```

No changes to `GetContext()` — the primary-turn context stays clean. Pending messages
are not injected here; they are handled entirely in the response phase.

---

### 8. `Services/SimulationService.cs`

This is the most significant change. Three additions:

**A. After item-action dispatch in the primary turn loop, add direct-message queuing**

Inside the `foreach (var (runner, color, _) in runners)` block, after the call to
`DispatchItemAction` and before `world.TickMeters`, add:

```
// Queue direct message if the agent addressed someone
if (!string.IsNullOrWhiteSpace(action.AddressAgent) &&
    !string.IsNullOrWhiteSpace(action.Speech))
{
    world.QueueDirectMessage(new DirectMessage
    {
        FromAgent = runner.Name,
        ToAgent   = action.AddressAgent.Trim(),
        Message   = action.Speech,
        Round     = round
    });
}
```

`QueueDirectMessage` performs range validation internally; no need to validate here.

**B. Add the response phase after the primary turn loop**

After the `foreach` block closes (but still inside the `for (int round = ...)` loop),
add a new block:

```
// ── Response phase ──────────────────────────────────────────────────────────
foreach (var (runner, color, _) in runners)
{
    if (ct.IsCancellationRequested) break;
    if (!world.HasPendingMessages(runner.Name)) continue;

    var pending = world.DrainPendingMessages(runner.Name);

    AgentResponse response;
    try
    {
        response = await runner.RespondAsync(
            pending,
            world.GetContext(runner.Name),
            ct);
    }
    catch (OperationCanceledException)
    {
        break;
    }
    catch (Exception ex)
    {
        response = new AgentResponse { Thought = $"Error: {ex.Message}", Speech = "" };
    }

    // Apply response effects
    ApplyDirectResponse(world, runner.Name, color, pending, response, round);

    foreach (var msg in world.DrainDevLog())
        Events.Add(new SimEvent { Type = "dev", Label = "dev", Content = msg });

    await NotifyAsync();
    await WaitIfPaused(ct);
}
```

**C. Add `ApplyDirectResponse` as a private static method**

This is analogous to `DispatchItemAction` — a focused helper that applies the effects
of a response turn.

```
private void ApplyDirectResponse(
    WorldState world,
    string responderName,
    string responderColor,
    IReadOnlyList<DirectMessage> messages,
    AgentResponse response,
    int round)
{
    var (rx, ry) = world.GetAgentPosition(responderName);

    // Log the thought
    if (!string.IsNullOrWhiteSpace(response.Thought))
        Events.Add(new SimEvent
        {
            Type = "thought", AgentName = responderName, AgentColor = responderColor,
            Label = "thinks", Content = response.Thought
        });

    if (string.IsNullOrWhiteSpace(response.Speech)) return;

    // Log the response as a distinct event type for UI styling
    Events.Add(new SimEvent
    {
        Type = "direct_response", AgentName = responderName, AgentColor = responderColor,
        Label = "replies", Content = $"\"{response.Speech}\""
    });

    // Write to location log (same cell as responder)
    world.LogAt(rx, ry, $"{responderName} replies: \"{response.Speech}\"");

    // Mood effect for responder (being addressed = social engagement)
    if (world.Mood.Has(responderName))
    {
        world.GetMood(responderName).AdjustMood(+3f);
        world.GetMood(responderName).AdjustStress(-2f);
        world.LogDev($"[{responderName}] responded directly → mood +3  stress -2");
    }

    // Effects on each sender
    foreach (var msg in messages)
    {
        // Memory for responder: record that they replied to this agent
        world.Memory.AddMemory(responderName,
            $"Replied to {msg.FromAgent}'s direct message: \"{response.Speech}\".");

        // Memory for the original sender (if still alive)
        if (world.GetAgentPosition(msg.FromAgent) != (-1, -1))
        {
            world.Memory.AddMemory(msg.FromAgent,
                $"{responderName} responded to me: \"{response.Speech}\".");

            // Trust boost for sender: someone replied to them
            if (world.Mood.Has(msg.FromAgent))
            {
                world.GetMood(msg.FromAgent).AdjustTrust(responderName, +5f);
                world.GetMood(msg.FromAgent).AdjustMood(+2f);
                world.LogDev(
                    $"[{msg.FromAgent}] received direct reply from {responderName} " +
                    $"→ trust[{responderName}] +5  mood +2");
            }

            // Trust boost for responder toward each sender
            if (world.Mood.Has(responderName))
            {
                world.GetMood(responderName).AdjustTrust(msg.FromAgent, +3f);
                world.LogDev(
                    $"[{responderName}] trust[{msg.FromAgent}] +3 (replied to them)");
            }
        }
    }
}
```

Note: `ApplyDirectResponse` does NOT call `world.AddEvent(...)` (which handles primary
turn speech). It writes to the location log directly via `world.LogAt` and creates
`SimEvent` entries explicitly. This keeps the two phases cleanly separated and avoids
the "nearby listeners get mood boost twice" problem.

---

## Data Flow: End to End

```
Round N, Primary Phase:
─────────────────────────────────────────────────────────────────────────────
SimulationService.RunAsync
  └─ foreach runner in runners (turn order):
       1. world.GetContext(runner.Name)   ← no pending messages shown here
       2. runner.ActAsync(context, ct)    ← LLM returns AgentAction
          • action.AddressAgent = "B"
          • action.Speech       = "B, do you still have water?"
       3. world.AddEvent(runner.Name, action)
          ├─ logs speech at runner's location cell
          ├─ nearby agents (within 1 cell) get mood +2 / stress -1 / trust +3
          └─ nearby agents get memory: "Heard A say: '...'"
       4. world.MoveAgent / TryForage / DispatchItemAction  ← unchanged
       5. NEW: if action.AddressAgent is non-empty:
          └─ world.QueueDirectMessage(new DirectMessage {
                 FromAgent = "A", ToAgent = "B",
                 Message = "B, do you still have water?", Round = N })
             ├─ CommunicationSystem validates: B is alive, within 1 cell of A
             └─ Appended to _inboxes["B"]
       6. world.TickMeters / world.TickMood  ← unchanged

Round N, Response Phase (begins after all primary turns complete):
─────────────────────────────────────────────────────────────────────────────
  └─ foreach runner in runners (same turn order):
       skip if !world.HasPendingMessages(runner.Name)
       
       For runner "B" (has pending message from "A"):
       1. pending = world.DrainPendingMessages("B")
          → [DirectMessage { From="A", To="B", Message="B, do you still have water?" }]
       2. runner.RespondAsync(pending, world.GetContext("B"), ct)
          ├─ Builds focused system prompt  ("You are B. {persona}. Reply only.")
          ├─ Builds user prompt: 'A says directly to you: "B, do you still have water?"'
          │   + abbreviated world context
          └─ LLM returns AgentResponse { Thought="...", Speech="I have one canteen left." }
       3. ApplyDirectResponse(world, "B", color, pending, response, round)
          ├─ Events.Add SimEvent { Type="direct_response", Label="replies", ... }
          ├─ world.LogAt(B's position, "B replies: \"I have one canteen left.\"")
          ├─ B mood +3, stress -2
          ├─ B memory: "Replied to A's direct message: '...'"
          ├─ A memory: "B responded to me: 'I have one canteen left.'"
          ├─ A trust[B] +5, A mood +2
          └─ B trust[A] +3
       4. DevLog drained and emitted as SimEvents

Round N+1, Primary Phase:
─────────────────────────────────────────────────────────────────────────────
  Agent A's GetContext includes in MEMORIES:
    "- B responded to me: 'I have one canteen left.'"
  → A can now request the item, negotiate, or act on B's response.
```

---

## Edge Cases and Decisions

**B already had their primary turn when A addresses them (A comes later in turn order)**
→ No problem. B's primary turn is clean (no pending message). B gets their response turn
in phase 2. B's primary-turn actions are already committed but this is no different from
any other post-hoc social signal. The conversation flows naturally across the round
boundary in B's memory.

**Multiple agents address B in the same round**
→ All messages accumulate in B's inbox. `RespondAsync` receives the full list and its
prompt lists all messages. B produces one response addressing all of them. `ApplyDirectResponse`
loops over all senders to distribute memory/mood effects.

**Agent addresses someone outside 1-cell range**
→ `CommunicationSystem.QueueDirectMessage` sees Chebyshev distance > 1, drops the message,
calls `world.LogDev(...)`. A gets no feedback in-turn, but the LLM won't know this — it's
a silent fail. Future enhancement: add a system message to A's memory next round.

**Agent addresses a dead agent**
→ `QueueDirectMessage` sees target position `(-1, -1)`, drops silently.

**A dies during primary phase, B's response turn references A**
→ `ApplyDirectResponse` checks `world.GetAgentPosition(msg.FromAgent) != (-1, -1)` before
writing memory to A or adjusting A's mood. If A is dead, the memory and mood effects are
skipped; B still gets their own memory and the response is still logged.

**Response turns and Pause/Step**
→ The response phase uses the same `WaitIfPaused(ct)` call after each agent's response
turn. Pausing mid-response-phase works exactly as pausing mid-primary-phase. Each response
turn is a single Step unit.

**Agent addresses themselves**
→ `CommunicationSystem.QueueDirectMessage` drops if `FromAgent == ToAgent`.

**LLM hallucination: `address_agent` names a non-existent agent**
→ `QueueDirectMessage` checks `world.GetAgentPosition(toAgent)` returns `(-1, -1)` for
unknown names → dropped silently.

**Response turns do not chain**
→ `AgentResponse` has no `address_agent` field. The response phase runs exactly once per
round and cannot trigger further response turns. This prevents infinite loops.

---

## UI Impact (`Components/Pages/Home.razor`)

The event rendering loop at line 127 uses `evt.Type` as a CSS class:
```csharp
<div class="event @evt.Type">
```

The new `"direct_response"` type will automatically get its own CSS class. Add a rule in
`wwwroot/app.css` to visually distinguish it (e.g., italic text, slightly inset indentation,
or a distinct color accent) so players can tell primary speech from direct responses at a
glance.

No other Razor changes are required. The `"direct_response"` SimEvent has the same
`AgentName`, `AgentColor`, `Label`, and `Content` fields as all other events.

Optionally add `"direct_message"` as a distinct event type for the moment A addresses B
(to distinguish "A addressed B" from "A spoke generally"). If added, it would be created
in `SimulationService` after calling `QueueDirectMessage`, showing something like
`[A] addresses → "B, do you still have water?"` before B's reply appears.

---

## Implementation Order

Follow this sequence to avoid compilation errors at each step. Each step compiles cleanly
before the next begins.

**Step 1 — Pure model layer (no dependencies on existing code)**
1. Create `Models/DirectMessage.cs`
2. Create `Models/AgentResponse.cs`

**Step 2 — Interface (depends only on DirectMessage)**
3. Create `Services/Systems/ICommunicationSystem.cs`

**Step 3 — Extend AgentAction (no other dependencies)**
4. Edit `Models/AgentAction.cs` — add `AddressAgent` field

**Step 4 — WorldState (depends on ICommunicationSystem)**
5. Edit `Services/WorldState.cs`
   - Add `ICommunicationSystem Communication { get; }` property
   - Update full constructor signature and body
   - Update convenience constructor to pass `new CommunicationSystem()`
   - Update `InitializeAgent` and `KillAgent`
   - Add three forwarding methods

**Step 5 — Default implementation (depends on WorldState, DirectMessage)**
6. Create `Services/Systems/CommunicationSystem.cs`
   - Now `WorldState` compiles and the convenience constructor works

**Step 6 — AgentRunner extension (depends on AgentResponse, DirectMessage)**
7. Edit `Services/AgentRunner.cs`
   - Update `SystemTemplate` constant (add address_agent to JSON + DIRECT COMMUNICATION paragraph)
   - Add `RespondAsync` method (uses existing `ExtractJson` helper, same pattern as `ActAsync`)

**Step 7 — SimulationService wiring (depends on everything above)**
8. Edit `Services/SimulationService.cs`
   - Add direct-message queuing block inside primary turn loop
   - Add response phase loop after primary turn loop
   - Add `ApplyDirectResponse` private static method

**Step 8 — UI styling**
9. Edit `wwwroot/app.css` — add `.event.direct_response` CSS rule

---

## What This Does Not Change

- `AgentRunner.ActAsync` — unchanged. Primary turns work exactly as before.
- `WorldState.GetContext()` — unchanged. No new context sections during primary phase.
- `WorldState.AddEvent()` — unchanged. Primary speech effects (mood +2, trust +3 for
  listeners) still fire for addressed speech too — being addressed is still public speech.
- All existing systems (`ISurvivalSystem`, `IForagingSystem`, `IItemSystem`, `IMoodSystem`,
  `IMemorySystem`) — zero changes.
- `SimulationConfig` — unchanged. No new configuration parameters.
- DI registration in `Program.cs` — the `CommunicationSystem` is constructed inside the
  `WorldState` convenience constructor (same pattern as all other systems). No new DI
  registration needed.

---

## Approximate Cost Per Round

Each round now makes up to `N` extra LLM calls in the response phase, where `N` is the
number of agents who received a direct message. Each call uses a token budget of 256
output tokens (vs. 768 for primary turns). In a 3-agent sim where all three communicate
in the same round, this is 3 extra calls at ~⅓ the token cost of a primary call — roughly
doubling round cost in a communication-heavy round, with zero extra cost in quiet rounds.
