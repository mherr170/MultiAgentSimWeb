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

        // Personality trait accumulator for the current agent
        var traitLine = "";
        var flagLine  = "";
        var skillLine = "";

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
                FlushAgent(agents, currentName, currentPersona, traitLine, flagLine, skillLine);
                currentName = line[2..].Trim();
                currentPersona.Clear();
                traitLine = "";
                flagLine  = "";
                skillLine = "";
                continue;
            }

            if (currentName is not null)
            {
                // Intercept trait/flag/skill metadata lines — don't add them to the persona body
                if (line.StartsWith("traits:", StringComparison.OrdinalIgnoreCase))
                    traitLine = line;
                else if (line.StartsWith("flags:", StringComparison.OrdinalIgnoreCase))
                    flagLine = line;
                else if (line.StartsWith("skill:", StringComparison.OrdinalIgnoreCase))
                    skillLine = line;
                else
                    currentPersona.AppendLine(line);
            }
        }

        FlushAgent(agents, currentName, currentPersona, traitLine, flagLine, skillLine);

        return new AgentProfiles
        {
            Situation = situation.ToString().Trim(),
            Agents    = agents,
        };
    }

    private static readonly Dictionary<string, string> SkillDescriptions = new()
    {
        ["crafting_expert"]  = "Crafts one extra item per recipe.",
        ["field_medic"]      = "Medical items restore 50% more health.",
        ["people_reader"]    = "Gains trust and mood from speech 50% faster as a listener.",
        ["survivor_grit"]    = "Rerolls empty building scavenges once; loses 2 less stress per turn.",
        ["silver_tongue"]    = "Recipients of given items gain double trust.",
        ["field_naturalist"] = "Forest foraging +25% and always finds at least something edible.",
        ["urban_forager"]    = "Building scavenging (apartments and storefronts) is 15% more effective.",
        ["animal_handler"]   = "Scare chance +20%; failed scares don't trigger counter-attacks.",
        ["combat_veteran"]   = "+8 base attack damage; counter-attack stress is halved.",
    };

    private static void FlushAgent(List<AgentDefinition> agents, string? name,
        System.Text.StringBuilder persona, string traitLine, string flagLine, string skillLine)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var blurb = persona.ToString().Trim();
        if (blurb.Length == 0) return;

        var profile = ParseProfile(name, blurb, traitLine, flagLine, skillLine);
        agents.Add(new AgentDefinition { Name = name, Persona = blurb, Profile = profile });
    }

    private static PersonalityProfile ParseProfile(string name, string blurb, string traitLine, string flagLine, string skillLine)
    {
        // Parse "traits: Resilience=75 Sociability=45 ..."
        int resilience = 50, sociability = 50, aggression = 50, optimism = 50, resourcefulness = 50;
        if (!string.IsNullOrWhiteSpace(traitLine))
        {
            var parts = traitLine["traits:".Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var kv = part.Split('=');
                if (kv.Length != 2 || !int.TryParse(kv[1], out int val)) continue;
                switch (kv[0].Trim())
                {
                    case "Resilience":      resilience      = Math.Clamp(val, 0, 100); break;
                    case "Sociability":     sociability     = Math.Clamp(val, 0, 100); break;
                    case "Aggression":      aggression      = Math.Clamp(val, 0, 100); break;
                    case "Optimism":        optimism        = Math.Clamp(val, 0, 100); break;
                    case "Resourcefulness": resourcefulness = Math.Clamp(val, 0, 100); break;
                }
            }
        }

        // Parse "flags: hoards_food distrusts_strangers ..." (space or comma separated)
        var flags = new List<string>();
        if (!string.IsNullOrWhiteSpace(flagLine))
        {
            var raw = flagLine["flags:".Length..].Trim()
                .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            flags.AddRange(raw);
        }

        // Parse "skill: crafting_expert"
        var skill = "";
        if (!string.IsNullOrWhiteSpace(skillLine))
            skill = skillLine["skill:".Length..].Trim().ToLowerInvariant();

        SkillDescriptions.TryGetValue(skill, out var skillDesc);

        return new PersonalityProfile
        {
            AgentName        = name,
            Blurb            = blurb,
            Resilience       = resilience,
            Sociability      = sociability,
            Aggression       = aggression,
            Optimism         = optimism,
            Resourcefulness  = resourcefulness,
            Flags            = flags,
            BackgroundSkill  = skill,
            SkillDescription = skillDesc ?? "",
            MaxHealth        = 100f,
        };
    }
}
