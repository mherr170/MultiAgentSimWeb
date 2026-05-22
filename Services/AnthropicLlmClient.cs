using System.Diagnostics;
using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;

namespace MultiAgentSimWeb.Services;

public class AnthropicLlmClient : ILlmClient
{
    private readonly AnthropicClient _client;

    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];
    private const int MaxAttempts = 2;

    public AnthropicLlmClient(string apiKey)
    {
        _client = new AnthropicClient(apiKey);
    }

    public Task WarmUpAsync(CancellationToken ct = default, Action<string>? onStatus = null)
    {
        onStatus?.Invoke("→ Anthropic API — no warm-up needed");
        return Task.CompletedTask;
    }

    public async Task<LlmResult> CompleteAsync(string systemPrompt, string userMessage, int maxTokens,
        CancellationToken ct = default, Action<string>? onStatus = null)
    {
        var parameters = new MessageParameters
        {
            Model         = AnthropicModels.Claude35Sonnet,
            MaxTokens     = maxTokens,
            SystemMessage = systemPrompt,
            Messages      = [new Message(RoleType.User, userMessage)]
        };

        Exception? lastException = null;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var estPromptTokens = (systemPrompt.Length + userMessage.Length) / 4;
            onStatus?.Invoke($"→ Sending to Anthropic (attempt {attempt + 1}/{MaxAttempts}, ~{estPromptTokens} prompt tok in, up to {maxTokens} tok out)");
            var sw = Stopwatch.StartNew();

            try
            {
                var response = await _client.Messages.GetClaudeMessageAsync(parameters, null, ct);
                sw.Stop();
                var content    = response.Message.ToString()?.Trim() ?? "";
                var inputTok   = response.Usage?.InputTokens  ?? 0;
                var outputTok  = response.Usage?.OutputTokens ?? 0;
                if (inputTok > 0 || outputTok > 0)
                {
                    onStatus?.Invoke($"← Response in {sw.Elapsed.TotalSeconds:F1}s — prompt: {inputTok} tok, completion: {outputTok} tok, total: {inputTok + outputTok} tok");
                    return new LlmResult(content, new LlmUsage(inputTok, outputTok, sw.Elapsed.TotalSeconds));
                }
                onStatus?.Invoke($"← Response in {sw.Elapsed.TotalSeconds:F1}s ({content.Length} chars)");
                return new LlmResult(content, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                onStatus?.Invoke("✗ Request cancelled");
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                lastException = ex;
                if (attempt < MaxAttempts - 1)
                {
                    var delay = RetryDelays[attempt];
                    onStatus?.Invoke($"⚠ {ex.GetType().Name} after {sw.Elapsed.TotalSeconds:F1}s — retrying in {delay.TotalSeconds:F0}s ({ex.Message})");
                    try { await Task.Delay(delay, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                }
                else
                {
                    onStatus?.Invoke($"✗ {ex.GetType().Name} after {sw.Elapsed.TotalSeconds:F1}s — all retries exhausted ({ex.Message})");
                }
            }
        }

        throw lastException ?? new InvalidOperationException("Anthropic request failed after all retries");
    }
}
