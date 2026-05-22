namespace MultiAgentSimWeb.Services;

/// Token usage + timing for a single completion, when the provider reports it.
public readonly record struct LlmUsage(int PromptTokens, int CompletionTokens, double ElapsedSeconds);

/// Result of a completion: the text content plus optional usage data.
/// Usage is null when the provider returned no token information.
public sealed record LlmResult(string Content, LlmUsage? Usage);

public interface ILlmClient
{
    Task<LlmResult> CompleteAsync(string systemPrompt, string userMessage, int maxTokens,
        CancellationToken ct = default, Action<string>? onStatus = null);

    Task WarmUpAsync(CancellationToken ct = default, Action<string>? onStatus = null);
}
