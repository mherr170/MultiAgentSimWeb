using System.Text.Json.Serialization;

namespace MultiAgentSimWeb.Models;

public class AgentResponse
{
    [JsonPropertyName("thought")]
    public string Thought { get; set; } = "";

    [JsonPropertyName("speech")]
    public string Speech { get; set; } = "";

    // On a parse failure the agent stays silent — never echo the raw model output as
    // speech, since spoken text is logged and stored in nearby agents' memory.
    public static AgentResponse Fallback(string raw) =>
        new() { Thought = "(could not parse reply)", Speech = "" };
}
