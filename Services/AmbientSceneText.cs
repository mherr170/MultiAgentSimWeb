using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services;

/// Provides atmospheric vignette text for the follow-mode map overlay.
/// Text is keyed on terrain category × day phase, with weather overlays mixed in.
public static class AmbientSceneText
{
    public static IReadOnlyList<string> GetPool(
        TerrainType terrain, DayPhase phase, string weatherLabel, string agentFirstName)
    {
        var base_ = GetBasePool(terrain, phase);
        var pool  = new List<string>(base_);

        bool rainy = weatherLabel.Contains("Rain", StringComparison.OrdinalIgnoreCase);
        bool stormy = weatherLabel.Contains("Storm", StringComparison.OrdinalIgnoreCase)
                   || weatherLabel.Contains("Thunder", StringComparison.OrdinalIgnoreCase);

        if (stormy)      pool.AddRange(StormLines);
        else if (rainy)  pool.AddRange(RainLines);

        // Substitute {name} with the agent's first name throughout
        for (int i = 0; i < pool.Count; i++)
            pool[i] = pool[i].Replace("{name}", agentFirstName);

        return pool;
    }

    private static IReadOnlyList<string> GetBasePool(TerrainType terrain, DayPhase phase)
    {
        bool indoors = terrain is TerrainType.Apartment or TerrainType.Storefront or TerrainType.Industrial;
        bool water   = terrain == TerrainType.River;
        bool nature  = terrain is TerrainType.Park or TerrainType.Forest;

        return (indoors, water, nature, phase) switch
        {
            (true, _, _, DayPhase.Night) => IndoorsNight,
            (true, _, _, DayPhase.Dawn) => IndoorsDawn,
            (true, _, _, DayPhase.Day)  => IndoorsDay,
            (true, _, _, DayPhase.Dusk) => IndoorsDusk,
            (_, true, _, DayPhase.Night) => RiverNight,
            (_, true, _, DayPhase.Dawn)  => RiverDawn,
            (_, true, _, _)              => RiverDay,
            (_, _, true, DayPhase.Night) => NatureNight,
            (_, _, true, DayPhase.Dawn)  => NatureDawn,
            (_, _, true, DayPhase.Day)   => NatureDay,
            (_, _, true, DayPhase.Dusk)  => NatureDusk,
            (_, _, _, DayPhase.Night)    => StreetNight,
            (_, _, _, DayPhase.Dawn)     => StreetDawn,
            (_, _, _, DayPhase.Day)      => StreetDay,
            _                            => StreetDusk,
        };
    }

    private static readonly string[] IndoorsNight =
    [
        "{name} listens. The building is quiet in a way buildings never should be.",
        "A flicker of movement at the window. Just a shadow. {name} doesn't move.",
        "{name} checks the time. Checks it again. Time moves strangely tonight.",
        "The emergency lights died an hour ago. {name} is used to the dark by now.",
        "{name} runs through options. Most of them lead nowhere good.",
        "Somewhere on another floor a door opens and closes. {name} waits to hear footsteps.",
        "The building creaks as the temperature drops. Just the building.",
        "{name} thinks about what they know. It isn't much.",
        "The dark has texture here. {name} has learned its shapes.",
        "Still air. Stale air. {name} wonders who else is in this building tonight.",
        "A distant siren. {name} turns toward the window without thinking.",
        "{name} rests against the wall and closes their eyes. Just for a moment.",
    ];

    private static readonly string[] IndoorsDawn =
    [
        "Gray light finds the gaps in the curtains. {name} hasn't slept.",
        "The night lasted longer than any night has a right to.",
        "{name} stretches stiff muscles and watches the light change slowly.",
        "The sounds of morning are starting. Distant birds. It's something.",
        "Dawn comes slowly. {name} tries to think clearly now that it's here.",
        "{name} hears the light before they see it — birdsong, a wind shift.",
        "The world outside is still broken. But it's visible now, at least.",
        "A few hours of morning, and {name} has to figure out what comes next.",
    ];

    private static readonly string[] IndoorsDay =
    [
        "Daylight makes it easier to pretend things might be okay.",
        "{name} takes stock. What they have. What they need. Where they can go.",
        "The city is quieter in daylight than it should be.",
        "{name} listens to the sounds outside. Mostly wind. Some birds.",
        "Shadows are shorter. The space feels larger. {name} uses the quiet to think.",
        "The window shows sky. Just sky. {name} finds that reassuring.",
        "{name} is tired. The kind of tired that sleep doesn't fix.",
        "The daytime belongs to movement. {name} is trying to plan for it.",
    ];

    private static readonly string[] IndoorsDusk =
    [
        "The light is going again. {name} had hoped the day would last longer.",
        "Dusk drains color from everything. The room grays.",
        "{name} watches the light change on the wall. Almost peaceful.",
        "Night again. {name} thinks about what tonight might bring.",
        "The sounds change as the sun goes down. {name} has noticed that.",
        "{name} makes a decision. Or tries to. The dusk makes it harder.",
        "Fading light. It'll be dark soon. {name} is already thinking about that.",
        "Whatever comes next comes after dark. {name} prepares.",
    ];

    private static readonly string[] StreetNight =
    [
        "{name} moves close to the buildings. Less exposed that way.",
        "The streetlights are off. Every shadow moves.",
        "No cars. No voices. Just wind through the empty corridors of the city.",
        "{name} hears glass crunch somewhere ahead and stops.",
        "The city is quieter than a city has any right to be.",
        "{name} keeps moving. Stopping feels worse than moving.",
        "A shape at the end of the block. {name} watches until it doesn't move.",
        "Broken windows everywhere. The city has been picked at already.",
        "{name} tries to remember what this street looked like with the lights on.",
        "Something rattles in the wind. Just a sign. Probably just a sign.",
    ];

    private static readonly string[] StreetDawn =
    [
        "First light catches the edges of the buildings. {name} uses it.",
        "The street looks less dangerous in the gray of early morning. Looks.",
        "{name} can see further now. Not far enough, but further.",
        "Dawn arrives without ceremony. {name} is grateful anyway.",
        "The sky is lighter than it was. {name} starts moving again.",
        "Morning changes the math. {name} reassesses.",
    ];

    private static readonly string[] StreetDay =
    [
        "Daylight. {name} moves faster in daylight.",
        "The open street feels less hostile when you can see it.",
        "{name} scans the road ahead. Mostly debris. Some things worth noting.",
        "The city looks like itself in daylight — just empty and broken.",
        "{name} keeps moving. The day won't last.",
        "Bright enough to see clearly. Quiet enough to remember why that matters.",
    ];

    private static readonly string[] StreetDusk =
    [
        "The light is draining from the street. {name} picks up the pace.",
        "Shadows stretch long across the empty road.",
        "{name} watches the sun go and thinks about shelter.",
        "Dusk turns the debris into shapes. {name} steps carefully.",
        "Night is coming. {name} isn't ready for another night.",
        "The street changes at dusk. {name} has learned that.",
    ];

    private static readonly string[] NatureNight =
    [
        "{name} can hear the park's animals — at least some of them aren't hiding.",
        "The trees block what little light the sky offers. {name} moves by feel.",
        "Something rustles in the undergrowth to the left. {name} doesn't investigate.",
        "The dark between the trees is absolute. {name} keeps to the path.",
        "An owl calls once and then goes silent. {name} waits for it to call again.",
        "The grass is wet. Cold seeps through {name}'s shoes before long.",
        "{name} finds it easier to breathe out here. The air is different.",
        "Somewhere in the dark something is alive. {name} can only hope it keeps its distance.",
        "Nature doesn't care about the blackout. {name} finds that either comforting or unsettling.",
        "Wind moves the canopy above. It sounds like something breathing.",
    ];

    private static readonly string[] NatureDawn =
    [
        "The forest wakes before the city does. {name} hears it happen.",
        "Birds first, then light. {name} waits for both.",
        "The trees catch the dawn differently than buildings do. {name} watches.",
        "Morning in the park is almost normal. {name} stands in it as long as they can.",
        "Dew on everything. Cold, but present. {name} is here.",
        "The gray of dawn is welcome after the dark. {name} breathes easier.",
    ];

    private static readonly string[] NatureDay =
    [
        "Daylight reaches the forest floor in patches. {name} moves through them.",
        "The park in daylight could almost be ordinary.",
        "Birdsong. {name} hadn't noticed it was gone until it came back.",
        "The trees are full of small sounds. None of them are people.",
        "{name} rests for a moment. Just a moment.",
        "The city is broken somewhere beyond the trees. Here it's just green and quiet.",
    ];

    private static readonly string[] NatureDusk =
    [
        "The forest gets its sounds back as the day fades. {name} listens.",
        "Animals come out at dusk. {name} knows that. Stays alert.",
        "The last light catches the leaves at strange angles.",
        "The park is less safe at night. {name} has maybe an hour.",
        "Dusk comes early under the trees. {name} hadn't planned for that.",
        "The wildlife is more active now. {name} keeps their distance.",
    ];

    private static readonly string[] RiverNight =
    [
        "The river moves in the dark, indifferent and loud.",
        "{name} hears the water and tries to find it calming.",
        "The Irongate is swollen tonight. {name} keeps back from the bank.",
        "Mist off the river. {name}'s jacket is damp before long.",
        "Water reflects nothing in this darkness. It just absorbs.",
        "{name} can't see the far bank. Easier not to think about what's over there.",
        "The current is faster than it looks. {name} knows not to trust it.",
        "River sounds cover everything else. {name} has to rely on other senses.",
    ];

    private static readonly string[] RiverDawn =
    [
        "The river picks up the first light before anything else does.",
        "Mist off the water. Dawn and the river and the sound of both.",
        "{name} watches the current carry whatever the night left behind.",
        "The Irongate looks different in the morning. More like itself.",
    ];

    private static readonly string[] RiverDay =
    [
        "The river is loud in daylight. It doesn't care what happened.",
        "Water moves. That's something. {name} watches it for a moment.",
        "The bank is clear. {name} can see in all directions from here.",
        "The river doesn't know about the blackout. It just keeps moving.",
    ];

    private static readonly string[] RainLines =
    [
        "Rain drums on every surface. {name} can only hear what's close.",
        "Cold rain. It's been coming down for hours now.",
        "The rain makes it harder to think. Or maybe that's not the rain.",
        "Wet through. Moving is harder than it should be.",
    ];

    private static readonly string[] StormLines =
    [
        "Thunder rolls. {name} flinches before they can stop themself.",
        "Lightning, then darkness, then the slow count to thunder.",
        "The storm makes the world louder and smaller at once.",
        "Wind presses hard. {name} leans into it and keeps going.",
    ];
}
