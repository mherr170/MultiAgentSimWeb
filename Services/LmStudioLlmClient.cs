using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MultiAgentSimWeb.Services;

public class LmStudioLlmClient : ILlmClient
{
    private readonly string _endpoint;
    private string _model;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8)];
    private const int MaxAttempts = 2;

    public LmStudioLlmClient(string endpoint = "http://10.0.0.119:1234/v1/chat/completions",
                              string model    = "llama-3.2-3b-instruct")
    {
        _endpoint = endpoint;
        _model    = model;
    }

    public async Task<LlmResult> CompleteAsync(string systemPrompt, string userMessage, int maxTokens,
        CancellationToken ct = default, Action<string>? onStatus = null)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

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

                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    // 400 usually means model name mismatch — auto-detect and retry once
                    var detected = await TryDetectModelAsync(ct);
                    if (detected != null && detected != _model)
                    {
                        onStatus?.Invoke($"⚠ HTTP 400 — model mismatch. Auto-detected '{detected}', retrying…");
                        _model = detected;
                        await DelayBeforeRetry(attempt, ct);
                        continue;
                    }
                    var body = await response.Content.ReadAsStringAsync(ct);
                    onStatus?.Invoke($"✗ HTTP 400 — giving up. Body: {(body.Length > 200 ? body[..200] : body)}");
                    throw new HttpRequestException("LM Studio returned HTTP 400 — check model name");
                }

                if (!response.IsSuccessStatusCode)
                {
                    onStatus?.Invoke($"✗ HTTP {(int)response.StatusCode} — giving up");
                    throw new HttpRequestException($"LM Studio returned HTTP {(int)response.StatusCode}");
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

    private async Task<string?> TryDetectModelAsync(CancellationToken ct)
    {
        try
        {
            var uri = new Uri(_endpoint);
            var modelsUrl = $"{uri.Scheme}://{uri.Authority}/v1/models";
            var resp = await _http.GetFromJsonAsync<ModelsResponse>(modelsUrl, ct);
            return resp?.Data?.FirstOrDefault()?.Id;
        }
        catch { return null; }
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

    private sealed class ModelsResponse
    {
        [JsonPropertyName("data")] public List<ModelData>? Data { get; init; }
    }
    private sealed class ModelData
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
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
