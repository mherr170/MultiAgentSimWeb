using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services;

public class AgentProfiles
{
    public string Situation { get; init; } = "";
    public IReadOnlyList<AgentDefinition> Agents { get; init; } = [];
}

public class AgentProfileService
{
    public AgentProfiles Profiles { get; }

    public AgentProfileService(IWebHostEnvironment env)
    {
        var path = Path.Combine(env.ContentRootPath, "Content", "agents.md");
        Profiles = File.Exists(path) ? Parse(File.ReadAllText(path)) : new AgentProfiles();
    }

    private static AgentProfiles Parse(string markdown)
    {
        var situation = new System.Text.StringBuilder();
        var agents    = new List<AgentDefinition>();

        string? currentName    = null;
        var     currentPersona = new System.Text.StringBuilder();
        bool    inSituation    = false;
        bool    pastDivider    = false;

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.TrimEnd();

            if (line.TrimStart().StartsWith("---"))
            {
                pastDivider = true;
                inSituation = false;
                continue;
            }

            if (!pastDivider)
            {
                // Before the divider: only a "# Situation" heading and its body
                if (line.StartsWith("# ") && line[2..].Trim().Equals("Situation", StringComparison.OrdinalIgnoreCase))
                {
                    inSituation = true;
                    continue;
                }
                if (inSituation)
                    situation.AppendLine(line);
                continue;
            }

            // After the divider: "# Name" headings introduce agents
            if (line.StartsWith("# "))
            {
                FlushAgent(agents, currentName, currentPersona);
                currentName = line[2..].Trim();
                currentPersona.Clear();
                continue;
            }

            if (currentName is not null)
                currentPersona.AppendLine(line);
        }

        FlushAgent(agents, currentName, currentPersona);

        return new AgentProfiles
        {
            Situation = situation.ToString().Trim(),
            Agents    = agents,
        };
    }

    private static void FlushAgent(List<AgentDefinition> agents, string? name, System.Text.StringBuilder persona)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var text = persona.ToString().Trim();
        if (text.Length > 0)
            agents.Add(new AgentDefinition { Name = name, Persona = text });
    }
}
