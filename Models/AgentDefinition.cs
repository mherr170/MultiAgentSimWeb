namespace MultiAgentSimWeb.Models;

public class AgentDefinition
{
    public string Name    { get; set; } = "";
    public string Persona { get; set; } = "";

    /// Optional personality profile. When set, Profile.Blurb is used as the
    /// system prompt persona (overriding the plain Persona string).
    public PersonalityProfile? Profile { get; set; }
}
