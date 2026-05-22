using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MultiAgentSimWeb.Services;

public class LmStudioLlmClient : ILlmClient
{
    private readonly string _endpoint;
    private readonly string _model;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(12)];
    private const int MaxAttempts = 3;

    public LmStudioLlmClient(string endpoint = "http://10.0.0.119:1234/v1/chat/completions",
                              string model    = "llama-3.2-3b-instruct")
    {
        _endpoint = endpoint;
        _model    = model;
    }

    public async Task<LlmResult> CompleteAsync(string systemPrompt, string userMessage, int maxTokens,
        CancellationToken ct = default, Action<string>? onStatus = null)
    {
        var payload = new
        {
            model    = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage  },
            },
            max_tokens  = maxTokens,
            temperature = 0.7,
        };

        Exception? lastException = null;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var estPromptTokens = (systemPrompt.Length + userMessage.Length) / 4;
            onStatus?.Invoke($"→ Sending to LM Studio (attempt {attempt + 1}/{MaxAttempts}, ~{estPromptTokens} prompt tok in, up to {maxTokens} tok out)");
            var sw = Stopwatch.StartNew();

            try
            {
                var response = await _http.PostAsJsonAsync(_endpoint, payload, ct);
                sw.Stop();

                if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests)
                {
                    var delay = attempt < RetryDelays.Length ? RetryDelays[attempt] : RetryDelays[^1];
                    onStatus?.Invoke($"⚠ HTTP {(int)response.StatusCode} — retrying in {delay.TotalSeconds:F0}s");
                    lastException = new HttpRequestException($"HTTP {(int)response.StatusCode} from LM Studio");
                    await DelayBeforeRetry(attempt, ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    onStatus?.Invoke($"✗ HTTP {(int)response.StatusCode} — giving up");
                    return new LlmResult($"(LM Studio returned HTTP {(int)response.StatusCode})", null);
                }

                ChatResponse? json;
                try
                {
                    json = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    onStatus?.Invoke($"⚠ Failed to parse response body — retrying ({ex.Message})");
                    lastException = ex;
                    await DelayBeforeRetry(attempt, ct);
                    continue;
                }

                var content = json?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "";
                var usage   = json?.Usage;
                if (usage != null)
                {
                    onStatus?.Invoke($"← Response in {sw.Elapsed.TotalSeconds:F1}s — prompt: {usage.PromptTokens} tok, completion: {usage.CompletionTokens} tok, total: {usage.TotalTokens} tok");
                    return new LlmResult(content, new LlmUsage(usage.PromptTokens, usage.CompletionTokens, sw.Elapsed.TotalSeconds));
                }
                onStatus?.Invoke($"← Response in {sw.Elapsed.TotalSeconds:F1}s ({content.Length} chars, no usage data)");
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
                var delay = attempt < RetryDelays.Length ? RetryDelays[attempt] : RetryDelays[^1];
                lastException = ex;
                if (attempt < MaxAttempts - 1)
                    onStatus?.Invoke($"⚠ {ex.GetType().Name} after {sw.Elapsed.TotalSeconds:F1}s — retrying in {delay.TotalSeconds:F0}s ({ex.Message})");
                else
                    onStatus?.Invoke($"✗ {ex.GetType().Name} after {sw.Elapsed.TotalSeconds:F1}s — all retries exhausted ({ex.Message})");
                await DelayBeforeRetry(attempt, ct);
            }
        }

        throw lastException ?? new InvalidOperationException("LM Studio request failed after all retries");
    }

    public async Task WarmUpAsync(CancellationToken ct = default, Action<string>? onStatus = null)
    {
        onStatus?.Invoke("→ Sending warm-up ping to LM Studio...");
        var sw = Stopwatch.StartNew();
        try
        {
            var payload = new
            {
                model      = _model,
                messages   = new[] { new { role = "user", content = "Hi" } },
                max_tokens = 1,
            };
            var response = await _http.PostAsJsonAsync(_endpoint, payload, ct);
            sw.Stop();
            if (response.IsSuccessStatusCode)
                onStatus?.Invoke($"← Model ready in {sw.Elapsed.TotalSeconds:F1}s");
            else
                onStatus?.Invoke($"⚠ Warm-up got HTTP {(int)response.StatusCode} after {sw.Elapsed.TotalSeconds:F1}s — proceeding anyway");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            onStatus?.Invoke($"⚠ Warm-up failed after {sw.Elapsed.TotalSeconds:F1}s ({ex.Message}) — proceeding anyway");
        }
    }

    private static async Task DelayBeforeRetry(int attempt, CancellationToken ct)
    {
        if (attempt < RetryDelays.Length)
        {
            try { await Task.Delay(RetryDelays[attempt], ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        }
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; init; }
        [JsonPropertyName("usage")]   public UsageInfo?   Usage   { get; init; }
    }
    private sealed class Choice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; init; }
    }
    private sealed class ChatMessage
    {
        [JsonPropertyName("content")] public string? Content { get; init; }
    }
    private sealed class UsageInfo
    {
        [JsonPropertyName("prompt_tokens")]     public int PromptTokens     { get; init; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; init; }
        [JsonPropertyName("total_tokens")]      public int TotalTokens      { get; init; }
    }
}
