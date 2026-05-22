namespace MultiAgentSimWeb.Models;

public class SimEvent
{
    public string Type { get; set; } = "";      // "round" | "thought" | "speech" | "action"
    public string AgentName { get; set; } = "";
    public string AgentColor { get; set; } = "";
    public string Label { get; set; } = "";
    public string Content { get; set; } = "";
    public int Round { get; set; }
}
