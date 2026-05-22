using System.Text;

namespace MultiAgentSimWeb.Models;

public class AgentMemory
{
    private const int Capacity = 12;
    private readonly List<string> _entries = new();

    public void Add(string entry)
    {
        if (_entries.Count >= Capacity)
            _entries.RemoveAt(0);
        _entries.Add(entry);
    }

    public IReadOnlyList<string> Recent => _entries;

    public string Format()
    {
        if (_entries.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine("MEMORIES (what you recall from previous turns):");
        foreach (var e in _entries)
            sb.AppendLine($"- {e}");
        return sb.ToString().TrimEnd();
    }
}
