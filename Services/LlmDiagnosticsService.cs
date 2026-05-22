using System.Text;

namespace MultiAgentSimWeb.Services;

public sealed class LlmDiagnosticsService : IDisposable
{
    // Running totals
    public int TotalCalls { get; private set; }
    public long TotalPromptTokens { get; private set; }
    public long TotalCompletionTokens { get; private set; }
    public double TotalElapsedSeconds { get; private set; }
    public int MaxPromptTokensSeen { get; private set; }

    private readonly Dictionary<string, AgentStat> _agentStats = new();
    private StreamWriter? _writer;
    private int _callIndex;

    public string? LogPath { get; private set; }

    public void Reset()
    {
        TotalCalls = 0;
        TotalPromptTokens = 0;
        TotalCompletionTokens = 0;
        TotalElapsedSeconds = 0;
        MaxPromptTokensSeen = 0;
        _callIndex = 0;
        _agentStats.Clear();
        _writer?.Dispose();
        _writer = null;

        Directory.CreateDirectory("logs");
        LogPath = Path.Combine("logs", $"llm_diag_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        _writer = new StreamWriter(LogPath, false) { AutoFlush = true };
        _writer.WriteLine("call,timestamp,agent,call_type,prompt_chars,prompt_tokens,completion_tokens,elapsed_s");
    }

    public void Record(string agentName, string callType, int promptChars, int promptTokens, int completionTokens, double elapsedSeconds)
    {
        _callIndex++;
        TotalCalls++;
        TotalPromptTokens += promptTokens;
        TotalCompletionTokens += completionTokens;
        TotalElapsedSeconds += elapsedSeconds;
        if (promptTokens > MaxPromptTokensSeen) MaxPromptTokensSeen = promptTokens;

        if (!_agentStats.TryGetValue(agentName, out var s))
            _agentStats[agentName] = s = new AgentStat();
        s.Calls++;
        s.PromptTokens += promptTokens;
        s.CompletionTokens += completionTokens;

        _writer?.WriteLine(
            $"{_callIndex},{DateTime.Now:O},{agentName},{callType},{promptChars},{promptTokens},{completionTokens},{elapsedSeconds:F2}");
    }

    public string GetRoundSummary()
    {
        if (TotalCalls == 0) return "No LLM calls yet";
        var avgPrompt = TotalPromptTokens / TotalCalls;
        return $"calls={TotalCalls}  in={TotalPromptTokens:N0}  out={TotalCompletionTokens:N0}  " +
               $"avg_prompt={avgPrompt:N0}  peak_prompt={MaxPromptTokensSeen:N0}  " +
               $"total_time={TotalElapsedSeconds:F1}s";
    }

    public string GetFinalReport()
    {
        if (TotalCalls == 0) return "No LLM calls recorded.";
        var sb = new StringBuilder();
        sb.AppendLine($"Total calls: {TotalCalls}  |  Tokens in: {TotalPromptTokens:N0}  |  Tokens out: {TotalCompletionTokens:N0}");
        sb.AppendLine($"Peak prompt: {MaxPromptTokensSeen:N0} tok  |  Total LLM time: {TotalElapsedSeconds:F1}s");
        if (_agentStats.Count > 0)
        {
            sb.AppendLine("Per-agent:");
            foreach (var (name, s) in _agentStats.OrderByDescending(x => x.Value.PromptTokens))
            {
                var avg = s.Calls > 0 ? s.PromptTokens / s.Calls : 0;
                sb.AppendLine($"  {name}: {s.Calls} calls, {s.PromptTokens:N0} in, {s.CompletionTokens:N0} out, avg {avg:N0} tok/call");
            }
        }
        if (LogPath != null) sb.AppendLine($"Log: {LogPath}");
        return sb.ToString().TrimEnd();
    }

    public void Close()
    {
        _writer?.Dispose();
        _writer = null;
    }

    public void Dispose() => Close();

    private sealed class AgentStat
    {
        public int Calls { get; set; }
        public long PromptTokens { get; set; }
        public long CompletionTokens { get; set; }
    }
}
