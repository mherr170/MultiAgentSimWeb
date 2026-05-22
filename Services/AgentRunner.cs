using MultiAgentSimWeb.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MultiAgentSimWeb.Services;

public class AgentRunner
{
    public string Name { get; }
    private readonly string _persona;
    private readonly ILlmClient _client;

    private const string SystemTemplate =
        "You are {0}. {1}\n\n" +
        "You are on a 2D grid map of a city that has just lost all power. " +
        "Each turn represents approximately one hour of real time. " +
        "Each turn you receive the current time, your coordinates, terrain, hunger/thirst meters, " +
        "nearby people, your inventory, items on the ground, and recent events.\n\n" +
        "STRANGERS: Everyone here is a stranger. The blackout caught each person wherever they happened to be — " +
        "you have never met any of them before and you do NOT know anyone's name. You only know what you can see " +
        "and hear. Someone you haven't met appears in your context as \"an unknown person.\" You learn a person's " +
        "name only when they say it themselves. To let others know who you are, introduce yourself by stating your " +
        "name out loud in your speech (e.g. \"I'm {0}\"). You can only address someone privately (address_agent) " +
        "once you know their name — to reach a stranger, just speak; everyone nearby hears you.\n\n" +
        "EMOTIONS: Your context shows your mood, stress level, and your attitude toward people you have met. " +
        "These are persistent — they reflect what has happened to you. A panicked person acts impulsively. " +
        "A trusting person shares resources. A despairing person takes risks. Let your emotional state genuinely " +
        "color your thought, speech, and decisions — not just describe them.\n\n" +
        "SURVIVAL: Your HUNGER and THIRST meters decrease each turn. If either reaches 0 you die.\n" +
        "  - Eat food: use canned_food, canned_meat, instant_noodles, peanut_butter, honey_jar, cereal_box, dried_pasta, protein_bar, granola_bar, crackers, chocolate_bar, cooking_oil (last resort), rat_carcass, or dog_carcass.\n" +
        "  - Drink: use water_bottle, sports_drink, or alcohol_bottle (alcohol helps morale but not much thirst).\n" +
        "  - TAP WATER: While you are inside an Apartment building, you can drink from the taps, sinks, or toilet tank " +
        "(set \"drink_tap\": true). Water pressure will eventually fail — use it while it lasts. " +
        "Your context will tell you if tap water is available at your location.\n" +
        "  - FOUNTAIN: The Riverside Fountain in the park has a reliable underground water supply that never runs dry. " +
        "Set \"drink_fountain\": true to drink (+20 thirst). It works even after taps fail across the city. " +
        "Your context will show a FOUNTAIN line when you are standing there.\n" +
        "  - RIVER: The Irongate River (east and south edge of the map) is a permanent water source. " +
        "Set \"drink_river\": true to drink (+25 thirst). Works any time, never fails. " +
        "Your context will show a RIVER line when you are standing on river terrain.\n" +
        "  - FOREST: Greenwood Forest and Birchwood Forest (south edge of the map) contain wild food — berries, mushrooms. " +
        "Set scavenge: true while in the forest to forage. Forest animals include foxes (harmless) and deer (huntable for venison).\n" +
        "  - FILL CONTAINERS: While inside an Apartment with tap water, OR at the Riverside Fountain, OR at the Irongate River, use item_action \"fill\" on any empty container " +
        "(tin_can, mason_jar, cooking_pot, water_jug, bucket) to fill it with water. " +
        "Filled containers can be carried, shared, or drunk later — critical once taps run dry.\n" +
        "  - Use comfort items to boost mood and reduce stress — morale matters as much as food. Good ones: blanket, winter_coat, candle, flashlight, painkillers, antiseptic, bandage_roll, chocolate_bar, honey_jar, instant_coffee, ham_radio, book, playing_cards, photo_album, stuffed_animal.\n" +
        "  - Scavenge inside Apartment or Storefront buildings (set \"scavenge\": true). " +
        "Scavenging resolves at your final position AFTER movement — you can move into a building and scavenge in the same turn.\n" +
        "  - Thirst drops faster than hunger. You have several hours before either becomes critical — but don't ignore your meters.\n\n" +
        "Movement: move one step per turn in a cardinal direction listed under EXITS. Leave move_to empty to stay.\n" +
        "  Multi-story buildings: if your FLOOR line shows more than 1 floor, set move_floor to \"up\" or \"down\" to change floors. Leave empty on the street or in single-story buildings.\n\n" +
        "Items: perform ONE item action per turn using item_action:\n" +
        "  - CARRY LIMIT: You can hold 5 items by default. Bags and backpacks in your inventory expand this limit automatically.\n" +
        "    (plastic bag +3, satchel +5, duffel bag +8, backpack +10, hiking pack +15)\n" +
        "  pick_up     — take an item from the ground\n" +
        "  drop        — place an inventory item on the ground\n" +
        "  use         — consume a usable item (food/water restores meters)\n" +
        "  give        — hand an item to another person at the same position (set item_give_to)\n" +
        "  deconstruct — break down a deconstructable item; may yield components or be destroyed\n" +
        "  craft       — craft a recipe (set craft_recipe_id to the recipe ID from CRAFTING section)\n" +
        "  fill        — fill an empty container (tin_can, mason_jar, cooking_pot, water_jug, bucket) at a tap inside an apartment, at the Riverside Fountain, OR at the Irongate River (set item_target_id)\n" +
        "  place_trap  — arm an Improved Trap from inventory at your current cell (set item_target_id)\n" +
        "  none        — take no item action\n\n" +
        "CRAFTING: Your context shows a CRAFTING section listing recipes you can make right now.\n" +
        "  To craft, set item_action to \"craft\" and craft_recipe_id to the recipe's ID in brackets.\n" +
        "  Example: item_action: \"craft\", craft_recipe_id: \"make_shiv\"\n\n" +
        "WEAPONS: Carrying a Shiv (+20) or Crude Knife (+12) boosts ALL your animal attacks automatically.\n" +
        "  No equipping needed — the bonus applies while the weapon is anywhere in your inventory.\n\n" +
        "TRAPS: Craft an Improved Trap (wire_bundle + scrap_metal), then place_trap to arm it at your cell.\n" +
        "  The trap stays active after you leave. Small animals wandering onto it are caught (85% chance).\n" +
        "  Retrieve it with pick_up. Cook carcasses with a Lighter to get better hunger restore.\n\n" +
        "DIRECT COMMUNICATION: To speak directly to someone nearby and get a same-round reply, " +
        "set address_agent to their exact name (they must be within 1 cell). Your speech field " +
        "is what you say — it is still heard by everyone nearby, but the named person will be " +
        "prompted to respond this round. Leave address_agent empty for general speech.\n\n" +
        "ANIMALS: The city has gone dark and quiet — but it isn't empty. Urban wildlife is growing bolder.\n" +
        "  Small animals (Rat, Pigeon, Street Cat, Squirrel) mostly ignore you. They can be trapped for food.\n" +
        "  LARGE animals (Dog Pack, Rottweiler, Pit Bull, Coyote) roam the streets and attack on sight.\n" +
        "  A dog attack reduces your hunger and thirst from injuries. An attacked person is weakened.\n\n" +
        "ANIMAL ACTIONS (one per turn, combined with item actions if desired):\n" +
        "  attack  — strike an animal IN YOUR CELL. Small animals die quickly. Large ones counter-attack.\n" +
        "  trap    — catch a small animal in your cell using a Wire Bundle (consumed regardless of success).\n" +
        "  scare   — try to frighten a large animal within 2 cells. Risky: failure provokes an immediate attack.\n" +
        "  none    — ignore animals (default).\n" +
        "  Set \"animal_action\" and \"animal_target_id\" (copy the exact [id:...] from context).\n\n" +
        "  SURVIVAL TIP: Feral dogs reduce your hunger and thirst when they attack. If you hear growling nearby,\n" +
        "  don't ignore it — fight back, scare them off, or get inside a building.\n\n" +
        "IMPORTANT: Your entire response must be a single JSON object. " +
        "Begin your response with {{ and end with }}. " +
        "No explanation, no reasoning text, no markdown — only the JSON object.\n\n" +
        "Respond in this exact JSON format:\n" +
        "{{\n" +
        "  \"thought\": \"your private reasoning, not heard by others\",\n" +
        "  \"speech\": \"what you say out loud — empty string if silent\",\n" +
        "  \"action\": \"short description of your physical action (e.g. 'searches the apartment', 'barricades the door') — write 'nothing' if you only speak or stand still\",\n" +
        "  \"move_to\": \"direction to move: one of the listed EXITS (\\\"N\\\", \\\"S\\\", \\\"E\\\", \\\"W\\\"), or empty string to stay put — do NOT put a direction here unless it appears in EXITS\",\n" +
        "  \"move_floor\": \"\\\"up\\\", \\\"down\\\", or empty — only valid inside multi-story buildings (when FLOOR line is shown)\",\n" +
        "  \"scavenge\": false,\n" +
        "  \"drink_tap\": false,\n" +
        "  \"drink_fountain\": false,\n" +
        "  \"drink_river\": false,\n" +
        "  \"item_action\": \"none, pick_up, drop, use, give, deconstruct, craft, or place_trap\",\n" +
        "  \"item_target_id\": \"exact ID string of the item (inventory or ground list), or empty string\",\n" +
        "  \"item_give_to\": \"exact agent name for give action, or empty string\",\n" +
        "  \"craft_recipe_id\": \"recipe ID from the CRAFTING section (for craft action only), or empty string\",\n" +
        "  \"address_agent\": \"exact name of a person within 1 cell you are speaking directly to, or empty string\",\n" +
        "  \"animal_action\": \"none, attack, trap, or scare\",\n" +
        "  \"animal_target_id\": \"the exact id string from ANIMALS IN YOUR CELL or ANIMALS NEARBY, or empty string\"\n" +
        "}}";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas         = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        Converters                  = { new FlexBoolConverter() },
    };

    private readonly Action<string>? _statusLogger;
    private readonly LlmDiagnosticsService? _diagnostics;

    public AgentRunner(string name, string persona, ILlmClient client,
        Action<string>? statusLogger = null, LlmDiagnosticsService? diagnostics = null)
    {
        Name          = name;
        _persona      = persona;
        _client       = client;
        _statusLogger = statusLogger;
        _diagnostics  = diagnostics;
    }

    // How many times to send the same prompt when the model returns an
    // empty / unparseable response (a successful HTTP call with no usable content).
    private const int MaxActAttempts = 3;

    public async Task<AgentAction> ActAsync(string worldContext, CancellationToken ct = default)
    {
        var systemPrompt = string.Format(SystemTemplate, Name, _persona);
        var userMessage  = worldContext + "\n\nIt is your turn to respond.";
        var promptChars  = systemPrompt.Length + userMessage.Length;

        AgentAction? lastResult = null;

        for (int attempt = 1; attempt <= MaxActAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var result = await _client.CompleteAsync(systemPrompt, userMessage, 1500, ct, _statusLogger);
            RecordUsage("act", promptChars, result.Usage);

            var (action, usable) = TryParseAction(result.Content);
            if (usable) return action;

            lastResult = action;
            if (attempt < MaxActAttempts)
                _statusLogger?.Invoke($"⟳ Empty/unusable response — retrying request (attempt {attempt + 1}/{MaxActAttempts})");
        }

        _statusLogger?.Invoke($"✗ Model returned no usable response after {MaxActAttempts} attempts");
        return lastResult ?? new AgentAction { Thought = "(no response from model)", Action = "nothing" };
    }

    /// Parses a raw completion into an AgentAction. The bool indicates whether the
    /// response was usable; an empty body, prose-without-JSON, all-empty fields, or a
    /// parse error all return usable=false so the caller can retry the same request.
    private (AgentAction action, bool usable) TryParseAction(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            _statusLogger?.Invoke("✗ Model returned empty body");
            return (new AgentAction { Thought = "(no response from model)", Action = "nothing" }, false);
        }

        var json = ExtractJson(raw);

        // Model returned prose with no JSON object — surface the reasoning as a thought,
        // but strip harmony/control tokens first so the user never sees raw <|channel|> tags.
        if (json == "{}" && !raw.Contains('{'))
        {
            var cleaned = StripThinkingBlocks(raw);
            var preview = raw.Length > 200 ? raw[..200] + "…" : raw;
            _statusLogger?.Invoke($"✗ No JSON in response — raw: {preview}");
            return (new AgentAction
            {
                Thought = cleaned.Length > 300 ? cleaned[..300] + "…" : cleaned,
                Action  = "nothing",
            }, false);
        }

        try
        {
            var action = JsonSerializer.Deserialize<AgentAction>(json, _jsonOptions) ?? Fallback(raw, null);
            if (string.IsNullOrWhiteSpace(action.Thought) &&
                string.IsNullOrWhiteSpace(action.Speech)  &&
                string.IsNullOrWhiteSpace(action.Action))
            {
                // Show a preview of the actual raw response so it's clear what the model said
                var rawPreview  = raw.Length  > 300 ? raw[..300].ReplaceLineEndings(" ") + "…" : raw.ReplaceLineEndings(" ");
                var jsonPreview = json.Length > 100 ? json[..100] + "…" : json;
                _statusLogger?.Invoke($"✗ All fields empty (extracted JSON: {jsonPreview})");
                _statusLogger?.Invoke($"  raw response: {rawPreview}");
                action.Thought = "(model returned empty response)";
                return (action, false);
            }
            return (action, true);
        }
        catch (JsonException ex)
        {
            var preview = raw.Length > 200 ? raw[..200] + "…" : raw;
            _statusLogger?.Invoke($"✗ JSON parse error: {ex.Message} — raw: {preview}");
            return (Fallback(raw, ex), false);
        }
    }

    public async Task<AgentResponse> RespondAsync(
        IReadOnlyList<DirectMessage> messages,
        string worldContext,
        CancellationToken ct = default,
        Func<string, string>? senderLabel = null)
    {
        var systemPrompt =
            $"You are {Name}. {_persona}\n\n" +
            "You have received a direct message this turn. Reply in character.\n" +
            "You cannot move or take items in this response — only speak (or stay silent).\n\n" +
            "IMPORTANT: Your entire response must be a single JSON object — no explanation, no reasoning text, no markdown.\n" +
            "Begin with { and end with }.\n" +
            "{\n" +
            "  \"thought\": \"your private reaction\",\n" +
            "  \"speech\": \"what you say in response — empty string to stay silent\"\n" +
            "}";

        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            var from = senderLabel?.Invoke(msg.FromAgent) ?? msg.FromAgent;
            sb.AppendLine($"{from} says directly to you: \"{msg.Message}\"");
        }
        sb.AppendLine();
        sb.AppendLine("Your current position and state (for context):");
        sb.AppendLine(worldContext);
        sb.AppendLine();
        sb.Append("It is your turn to respond to the above direct message(s).");

        var userMsg     = sb.ToString();
        var promptChars = systemPrompt.Length + userMsg.Length;

        AgentResponse? lastResult = null;

        for (int attempt = 1; attempt <= MaxActAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var result = await _client.CompleteAsync(systemPrompt, userMsg, 512, ct, _statusLogger);
            RecordUsage("respond", promptChars, result.Usage);

            var (response, usable) = TryParseResponse(result.Content);
            if (usable) return response;

            lastResult = response;
            if (attempt < MaxActAttempts)
                _statusLogger?.Invoke($"⟳ Empty/unusable reply — retrying request (attempt {attempt + 1}/{MaxActAttempts})");
        }

        return lastResult ?? AgentResponse.Fallback("");
    }

    /// Parses a raw completion into an AgentResponse. usable=false for empty bodies,
    /// all-empty fields, or parse errors so the caller can retry the same request.
    private (AgentResponse response, bool usable) TryParseResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            _statusLogger?.Invoke("✗ Model returned empty body");
            return (AgentResponse.Fallback(""), false);
        }

        var json = ExtractJson(raw);
        try
        {
            var response = JsonSerializer.Deserialize<AgentResponse>(json, _jsonOptions) ?? AgentResponse.Fallback(raw);
            bool empty = string.IsNullOrWhiteSpace(response.Thought) &&
                         string.IsNullOrWhiteSpace(response.Speech);
            return (response, !empty);
        }
        catch (JsonException)
        {
            return (AgentResponse.Fallback(raw), false);
        }
    }

    private void RecordUsage(string callType, int promptChars, LlmUsage? usage)
    {
        if (_diagnostics is not null && usage is { } u)
            _diagnostics.Record(Name, callType, promptChars, u.PromptTokens, u.CompletionTokens, u.ElapsedSeconds);
    }

    private static string ExtractJson(string raw)
    {
        var s = raw.Trim();

        // Strip chain-of-thought blocks emitted by reasoning-capable models.
        // LM Studio models may wrap their thinking in <|channel|>thought...
        // or the more common <think>...</think> before writing the JSON.
        s = StripThinkingBlocks(s);

        // Strip markdown fences
        if (s.StartsWith("```"))
            s = string.Join("\n", s.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("```"))).Trim();

        // LLM sometimes returns the JSON object wrapped in a JSON string literal: "{ ... }"
        if (s.StartsWith("\"") && s.EndsWith("\""))
        {
            try { s = JsonSerializer.Deserialize<string>(s) ?? s; }
            catch { /* leave as-is */ }
            s = s.Trim();
        }

        // If there's prose before/after the JSON object, extract the {...} block.
        // Return "{}" if no object found (empty response, pure prose) so Deserialize
        // produces a default action rather than throwing a JsonException.
        var start = s.IndexOf('{');
        var end   = s.LastIndexOf('}');
        return (start >= 0 && end > start) ? s[start..(end + 1)] : "{}";
    }

    // Matches harmony / reasoning control tokens like <|channel|>, <|message|>,
    // <|start|>, <|end|>, <|return|>, <|assistant|> so they can be stripped out.
    private static readonly Regex HarmonyTokenRegex =
        new(@"<\|[^|>]*\|>", RegexOptions.Compiled);

    private static string StripThinkingBlocks(string s)
    {
        // <think>...</think> — drop everything up to and including the closing tag.
        var closeThink = s.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (closeThink >= 0)
            s = s[(closeThink + 8)..];

        // Harmony format (gpt-oss / reasoning models) looks like:
        //   <|channel|>analysis<|message|>…reasoning…<|end|>
        //   <|start|>assistant<|channel|>final<|message|>…answer…<|return|>
        // The real answer is the content of the LAST channel — everything after
        // the final <|message|> marker. Taking the last one skips the reasoning.
        const string msgTok = "<|message|>";
        var lastMessage = s.LastIndexOf(msgTok, StringComparison.OrdinalIgnoreCase);
        if (lastMessage >= 0)
            s = s[(lastMessage + msgTok.Length)..];

        // Remove any remaining control tokens (e.g. trailing <|return|>, <|end|>,
        // or a bare <|channel|>thought prefix when no <|message|> was emitted) so
        // they can't pollute the JSON search downstream.
        s = HarmonyTokenRegex.Replace(s, "");

        return s.Trim();
    }

    private static AgentAction Fallback(string raw, JsonException? ex)
    {
        var detail = ex is null ? "" : $" [{ex.Message}]";
        return new AgentAction
        {
            Thought = $"(could not parse response{detail})",
            Action  = "nothing",
        };
    }
}

/// Accepts bool from JSON boolean, integer (0/1), or string ("true"/"false"/"1"/"0").
file sealed class FlexBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True   => true,
            JsonTokenType.False  => false,
            JsonTokenType.Number => reader.TryGetInt32(out int n) && n != 0,
            JsonTokenType.String => reader.GetString()?.ToLowerInvariant() is "true" or "1" or "yes",
            _                    => false,
        };
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        => writer.WriteBooleanValue(value);
}
