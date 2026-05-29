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

    // Prompts are defined in PromptBuilder — edit there to change LLM instructions.

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
        var systemPrompt = PromptBuilder.BuildActSystem(Name, _persona);
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
        return lastResult ?? new AgentAction { Action = "nothing" };
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
                action.Action = "nothing";
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
        var systemPrompt = PromptBuilder.BuildRespondSystem(Name, _persona);

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

    /// Continues an ongoing private conversation given the full history so far.
    /// describeAgent maps a speaker's internal name to how *this* agent perceives them.
    public async Task<AgentResponse> ContinueConversationAsync(
        IReadOnlyList<ConversationLine> history,
        Func<string, string> describeAgent,
        string worldContext,
        CancellationToken ct = default)
    {
        var systemPrompt = PromptBuilder.BuildContinueSystem(Name, _persona);

        var sb = new StringBuilder();
        sb.AppendLine("Private conversation so far:");
        sb.AppendLine();
        foreach (var line in history)
        {
            var label = line.Speaker == Name ? "You" : describeAgent(line.Speaker);
            sb.AppendLine($"[{label}]: \"{line.Text}\"");
        }
        sb.AppendLine();
        sb.AppendLine("Your current state (for context):");
        sb.AppendLine(worldContext);
        sb.AppendLine();
        sb.Append("It is your turn. Continue the conversation or send an empty string to end it.");

        var userMsg     = sb.ToString();
        var promptChars = systemPrompt.Length + userMsg.Length;

        AgentResponse? lastResult = null;

        for (int attempt = 1; attempt <= MaxActAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _client.CompleteAsync(systemPrompt, userMsg, 512, ct, _statusLogger);
            RecordUsage("converse", promptChars, result.Usage);
            var (response, usable) = TryParseResponse(result.Content);
            if (usable) return response;
            lastResult = response;
            if (attempt < MaxActAttempts)
                _statusLogger?.Invoke($"⟳ Empty/unusable reply — retrying (attempt {attempt + 1}/{MaxActAttempts})");
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
