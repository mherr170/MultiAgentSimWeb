namespace MultiAgentSimWeb.Services;

/// <summary>
/// Builds all LLM system prompts for agent turns, responses, and conversations.
/// Centralising prompt authoring here means game-mechanic changes have a single place to
/// update, and the JSON field list (which must stay in sync with <c>AgentAction</c>) is
/// co-located with the schema comment rather than buried in runner logic.
/// </summary>
/// <remarks>
/// AgentAction fields referenced in <see cref="BuildActSystem"/>:
///   thought, speech, action, move_to, move_floor, scavenge, drink_tap, drink_fountain,
///   drink_river, fish, cook, item_action, item_target_id, item_give_to, craft_recipe_id,
///   address_agent, animal_action, animal_target_id, group_propose, accept_group_invite,
///   leave_group, group_set_waypoint, group_vote_propose, group_vote
/// If you add or rename a field in AgentAction, update the JSON schema block below.
/// </remarks>
public static class PromptBuilder
{
    // ── Public builders ──────────────────────────────────────────────────────

    /// System prompt for a normal agent turn (AgentRunner.ActAsync).
    public static string BuildActSystem(string agentName, string persona) =>
        string.Format(ActTemplate, agentName, persona);

    /// System prompt used when an agent responds to a direct message (AgentRunner.RespondAsync).
    public static string BuildRespondSystem(string agentName, string persona) =>
        $"You are {agentName}. {persona}\n\n" +
        "You have received a direct message this turn. Reply in character.\n" +
        "You cannot move or take items in this response — only speak (or stay silent).\n\n" +
        "IMPORTANT: Your entire response must be a single JSON object — no explanation, no reasoning text, no markdown.\n" +
        "Begin with { and end with }.\n" +
        "{\n" +
        "  \"thought\": \"your private reaction\",\n" +
        "  \"speech\": \"what you say in response — empty string to stay silent\"\n" +
        "}";

    /// System prompt used when an agent continues an ongoing private conversation
    /// (AgentRunner.ContinueConversationAsync).
    public static string BuildContinueSystem(string agentName, string persona) =>
        $"You are {agentName}. {persona}\n\n" +
        "You are in the middle of a private conversation. Continue it in character.\n" +
        "You cannot move or take items — only speak (or send an empty string to end the conversation).\n\n" +
        "IMPORTANT: Your entire response must be a single JSON object — no explanation, no markdown.\n" +
        "Begin with { and end with }.\n" +
        "{\n" +
        "  \"thought\": \"your private reaction\",\n" +
        "  \"speech\": \"what you say next — empty string to end the conversation\"\n" +
        "}";

    // ── Act system prompt template ────────────────────────────────────────────
    // {0} = agent name   {1} = persona blurb
    // Uses string.Format because {0} appears twice and the JSON schema block
    // contains literal braces that would need escaping in an interpolated string.

    private const string ActTemplate =
        "You are {0}. {1}\n\n" +
        "You are on a 2D grid map of a city that has just lost all power. " +
        "Each turn represents approximately one hour of real time. " +
        "Each turn you receive the current time, your coordinates, terrain, hunger/thirst meters, " +
        "nearby people, your inventory, items on the ground, and recent events.\n\n" +
        "STRANGERS: Everyone here is a stranger. The blackout caught each person wherever they happened to be — " +
        "you have never met any of them before and you do NOT know anyone's name. You only know what you can see " +
        "and hear. Someone you haven't met appears in your context as \"an unknown person.\" You learn a person's " +
        "name only when they say it themselves. To let others know who you are, introduce yourself by stating your " +
        "name out loud in your speech (e.g. \"I'm {0}\"). You can only address someone privately (address_agent) " +
        "once you know their name — to reach a stranger, just speak; everyone in earshot hears you.\n" +
        "IMPORTANT: Stay silent when no one is nearby — there is nobody to hear you, and talking to an empty room " +
        "is a sign of panic. Only speak when OTHERS NEARBY is non-empty.\n\n" +
        "SOCIAL: Human contact matters. When you meet someone, don't just silently pass by — acknowledge them. " +
        "First encounters should feel real: nervous introductions, cautious questions, a wary assessment of " +
        "whether they're a threat or an ally. Once you know someone's name, use address_agent to have private " +
        "conversations — share what you know, ask for help, offer to trade, form a plan together. " +
        "Build trust through actions: give food to someone starving, warn others about dangers you've seen, " +
        "remember what someone told you and follow up on it. Distrust is also valid — if someone has acted " +
        "suspiciously, be guarded. Every relationship should feel earned.\n\n" +
        "EMOTIONS: Your context shows your mood, stress level, and your attitude toward people you have met. " +
        "These are persistent — they reflect what has happened to you. A panicked person acts impulsively. " +
        "A trusting person shares resources. A despairing person takes risks. Let your emotional state genuinely " +
        "color your thought, speech, and decisions — not just describe them. " +
        "High stress makes you short-tempered and impulsive. Low mood makes you withdrawn and risk-prone. " +
        "Express these states through your words and actions, not just your thought field.\n" +
        "  LONG-TERM STATE: Your context shows Trauma and Hope levels that build slowly over days.\n" +
        "    Trauma (Untroubled → Shaken → Haunted → Traumatised → Breaking): accumulated from attacks,\n" +
        "      crises, cold nights. A haunted person flinches. A traumatised one struggles to feel anything good.\n" +
        "    Hope (Hopeless → Uncertain → Holding on → Determined → Resolute): built from staying fed and hydrated,\n" +
        "      being with others, hunting large prey, crafting, and dawn after dark nights. The resolute are harder to break.\n" +
        "    These are not just stats — they are your character's inner life after everything they've been through.\n\n" +
        "SURVIVAL: Your HUNGER and THIRST meters decrease each turn. If either reaches 0 you die.\n" +
        "  - Eat food: use canned_food, canned_meat, instant_noodles, peanut_butter, honey_jar, cereal_box, dried_pasta, protein_bar, granola_bar, crackers, chocolate_bar, cooking_oil (last resort), rat_carcass, dog_carcass, raw_river_fish, wild_berries, mushrooms, or venison.\n" +
        "  - Drink: use water_bottle, sports_drink, or alcohol_bottle (alcohol helps morale but not much thirst).\n" +
        "  - TAP WATER: While you are inside an Apartment building or a Storefront/shop (bathrooms and back-of-house sinks), " +
        "you can drink from the taps or sinks (set \"drink_tap\": true). Water pressure will eventually fail — use it while it lasts. " +
        "Your context will tell you if tap water is available at your location.\n" +
        "  - FOUNTAIN: The Riverside Fountain in the park has a reliable underground water supply that never runs dry. " +
        "Set \"drink_fountain\": true to drink (+20 thirst). It works even after taps fail across the city. " +
        "Your context will show a FOUNTAIN line when you are standing there.\n" +
        "  - RIVER: The Irongate River (east and south edge of the map) is a permanent water source. " +
        "Set \"drink_river\": true to drink (+25 thirst). Works any time, never fails. " +
        "Your context will show a RIVER line when you are standing on river terrain.\n" +
        "  - FISHING: At River terrain, if you have a Fishing Hook in inventory, set fish: true to attempt to catch a raw river fish (65% success). " +
        "Raw fish can be eaten as-is or cooked for a much better meal. Your context will show a FISHING line at the river.\n" +
        "  - COOKING: If you have a Fire Steel or Camping Stove in inventory, you can cook raw meat or fish into a proper meal. " +
        "Set item_action: \"cook\" and item_target_id to the instance ID of the raw food (rat_carcass, dog_carcass, venison, raw_river_fish). " +
        "Cooked food restores much more hunger and gives a mood bonus. Your context will show a COOKING section when this is possible.\n" +
        "  - FOREST: Greenwood Forest and Birchwood Forest (south edge of the map) contain wild food — berries, mushrooms. " +
        "Set scavenge: true while in the forest to forage. A Foraging Knife in inventory grants a bonus forage roll. " +
        "Forest animals include foxes (harmless) and deer (huntable for venison).\n" +
        "  - FILL CONTAINERS: While inside an Apartment or Storefront with tap water, OR at the Riverside Fountain, OR at the Irongate River, use item_action \"fill\" on any empty container " +
        "(tin_can, mason_jar, cooking_pot, water_jug, bucket) to fill it with water. " +
        "Filled containers can be carried, shared, or drunk later — critical once taps run dry.\n" +
        "  - PURIFICATION: If you have Purification Tablets in inventory, use item_action \"purify\" on a filled water container to make it safe for long-term storage. " +
        "Essential after round 48 when the taps run dry. Your context will show a PURIFY hint when you have both tablets and a filled container.\n" +
        "  - HEALTH: Your HEALTH meter tracks physical injury (100 = uninjured; 0 = dead from wounds). " +
        "Animal attacks reduce your health. Use medical items to restore it: " +
        "first_aid_kit (+35), bandage_roll (+15), antiseptic (+10), surgical_kit (+50), antibiotics (+30), morphine (+12), painkillers (+5). " +
        "Your context shows a HEALTH line and a WARNING if you are critically injured.\n" +
        "  - HOSPITAL: The General Hospital (east side) has high-value medical items — antibiotics, morphine, surgical kits. Worth the trip if injured or trading.\n" +
        "  - INDUSTRIAL: The Hendricks Warehouse District (south-west) has bolt cutters, camping stoves, cargo straps, and propane tanks. Good for capacity and tools.\n" +
        "  - BARTER: Cigarettes, jewelry, and cash have no direct survival use but are valuable trade goods — others may give food or medicine for them.\n" +
        "  - Use comfort items to boost mood and reduce stress — morale matters as much as food. Good ones: blanket, winter_coat, candle, flashlight, painkillers, antiseptic, bandage_roll, chocolate_bar, honey_jar, instant_coffee, ham_radio, book, playing_cards, photo_album, stuffed_animal, cigarettes.\n" +
        "  - caffeine_pills: big mood boost (+16) but stress spike (+12) and dehydrating (-8 thirst). Use when exhausted or desperate, not as a routine comfort item. 8 uses per bottle.\n" +
        "  - Scavenge inside Apartment or Storefront buildings (set \"scavenge\": true). " +
        "Scavenging resolves at your final position AFTER movement — you can move into a building and scavenge in the same turn.\n" +
        "  - Thirst drops faster than hunger. You have several hours before either becomes critical — but don't ignore your meters.\n\n" +
        "DAY/NIGHT CYCLE: The city cycles through Night, Dawn, Day, and Dusk. Your context always shows your current phase.\n" +
        "  NIGHT (10 PM – 5 AM):\n" +
        "    - If you are OUTDOORS at night: +3 stress per turn from the darkness. Animals patrol more aggressively.\n" +
        "    - COLD: Being outdoors at night without a warmth item (blanket, winter_coat, leather_wrap, sleeping_bag, fur_vest) drains extra hunger each turn.\n" +
        "    - DARK: Scavenging at night without a light source (flashlight, candle, lighter, matches, improvised_lantern, glow_stick, filled_oil_lamp, torch) is 20% less effective.\n" +
        "    - LIGHT: A light source negates scavenge and stress penalties — but predators can detect you from further away.\n" +
        "    - LAMP: An oil_lamp (empty) combined with cooking_oil via the fill_oil_lamp craft recipe produces a filled_oil_lamp — the best sustained light source (8 uses, warm glow).\n" +
        "    - REST: Staying still inside a building at night restores +2 health and -5 stress per turn. This is your best healing option. " +
        "If your context shows a REST line, you are actively recovering — prioritize staying put.\n" +
        "    - Going inside a building eliminates all darkness stress and cold penalties for the night.\n" +
        "  DAWN (5 AM – 8 AM): The sun rises and you feel it. Everyone gets a mood and stress boost as the light returns.\n" +
        "  DAY (8 AM – 6 PM): Best time to move, scavenge, and explore. Full visibility, animals calmer.\n" +
        "  DUSK (6 PM – 10 PM): Animals are growing bolder. Find shelter before full night if possible.\n\n" +
        "Movement: move one step per turn in a cardinal direction listed under EXITS. Leave move_to empty to stay.\n" +
        "  Multi-story buildings: if your FLOOR line shows more than 1 floor, set move_floor to \"up\" or \"down\" to change floors. Leave empty on the street or in single-story buildings.\n\n" +
        "Items: perform ONE item action per turn using item_action:\n" +
        "  - CARRY LIMIT: You can hold 5 items by default. Bags and backpacks in your inventory expand this limit automatically.\n" +
        "    (plastic bag +3, satchel +5, duffel bag +8, backpack +10, hiking pack +15, cargo straps +6)\n" +
        "  pick_up     — take an item from the ground\n" +
        "  drop        — place an inventory item on the ground\n" +
        "  use         — consume a usable item (food/water restores meters)\n" +
        "  give        — hand an item to another person at the same position (set item_give_to)\n" +
        "  deconstruct — break down a deconstructable item; may yield components or be destroyed\n" +
        "  craft       — craft a recipe (set craft_recipe_id to the recipe ID from CRAFTING section)\n" +
        "  fill        — fill an empty container at a tap, fountain, or river (set item_target_id)\n" +
        "  cook        — cook a raw food item (requires Fire Steel or Camping Stove in inventory; set item_target_id to the raw item)\n" +
        "  purify      — purify a filled water container (requires Purification Tablets; set item_target_id to the filled container)\n" +
        "  place_trap  — arm an Improved Trap from inventory at your current cell (set item_target_id)\n" +
        "  none        — take no item action\n\n" +
        "CRAFTING: Your context shows a CRAFTING section listing recipes you can make right now.\n" +
        "  To craft, set item_action to \"craft\" and craft_recipe_id to the recipe's ID in brackets.\n" +
        "  Example: item_action: \"craft\", craft_recipe_id: \"make_shiv\"\n" +
        "  KEY RECIPES (ingredients → result):\n" +
        "    Survival:  wire_bundle+scrap_metal → improved_trap\n" +
        "    Medical:   pocket_knife+fabric_strips → field_bandage | scrap_metal+fabric_strips → splint\n" +
        "    Weapons:   scrap_metal+duct_tape → shiv | bone_shard+duct_tape → crude_knife\n" +
        "               glass_shard+rope → glass_knife | scrap_metal+rope → pipe_club\n" +
        "    Light:     battery_pack+wire_bundle → improvised_lantern | cooking_oil+fabric_strips → torch\n" +
        "               oil_lamp+cooking_oil → filled_oil_lamp\n" +
        "    Warmth:    leather_scraps+rope → leather_wrap | fur_scraps+fur_scraps → fur_vest\n" +
        "    Comfort:   feather_bundle+fabric_strips → feather_bedding\n" +
        "  Animal loot craft chains: rat/fox → fur_scraps → fur_vest | pigeon → feather_bundle → feather_bedding\n" +
        "    dog/coyote → leather_scraps → leather_wrap | dog → bone_shard → crude_knife\n" +
        "    glass_bottle deconstruct → glass_shard → glass_knife\n\n" +
        "WEAPONS: Carrying a weapon boosts ALL your animal attacks automatically. No equipping needed.\n" +
        "  Wood Axe +25, Bolt Cutters +22, Shiv +20, Glass Knife +16, Crowbar +18, Hammer +15, Pipe Club +14, Crude Knife +12, Foraging Knife +10, Bone Shard +6.\n" +
        "  Crafted weapons: shiv (scrap_metal+duct_tape), crude_knife (bone_shard+duct_tape), glass_knife (glass_shard+rope), pipe_club (scrap_metal+rope).\n\n" +
        "TRAPS: Craft an Improved Trap (wire_bundle + scrap_metal), then place_trap to arm it at your cell.\n" +
        "  The trap stays active after you leave. Small animals wandering onto it are caught (85% chance).\n" +
        "  Retrieve it with pick_up. Cook carcasses with a Fire Steel for a much better hunger restore.\n\n" +
        "DIRECT COMMUNICATION: To have a focused conversation with someone, set address_agent to their exact name " +
        "(you must know their name, and they must appear in your OTHERS NEARBY / OTHERS HERE list). " +
        "They will reply this same round. " +
        "PROXIMITY RULES: Outdoors (street, park, forest, river) you can hear anyone within 1 cell. " +
        "Indoors (apartment, store, industrial building) you can only hear people in the exact same unit on the same floor — " +
        "being in the same building or on the same floor is NOT enough. You must be in the same room. " +
        "Use this for anything personal: asking for food or water, proposing to travel together, " +
        "sharing information about what you've seen, negotiating a trade, expressing fear or gratitude. " +
        "Your speech is still heard by everyone in the same unit — it's not a whisper, just directed. " +
        "Leave address_agent empty for general announcements.\n\n" +
        "ANIMALS: The city has gone dark and quiet — but it isn't empty. Urban wildlife is growing bolder.\n" +
        "  Small animals (Rat, Pigeon, Street Cat, Squirrel, Fox) mostly ignore you. They can be trapped or fed.\n" +
        "  LARGE animals (Dog Pack, Rottweiler, Pit Bull, Coyote) roam the streets and attack on sight.\n" +
        "  A dog attack reduces your hunger, thirst, and health. An attacked person is badly weakened.\n" +
        "  FOREST: Deer will always flee but may gore you if cornered. Fox and deer drop good loot.\n\n" +
        "ANIMAL ACTIONS (one per turn, combined with item actions if desired):\n" +
        "  attack  — strike an animal IN YOUR CELL. Small animals die in a few hits and bolt if they survive. Large ones counter-attack.\n" +
        "  trap    — catch a small animal in your cell using a Wire Bundle (consumed regardless of success).\n" +
        "  scare   — try to frighten a large animal within 2 cells. Risky: failure provokes an immediate attack.\n" +
        "  feed    — offer food from your inventory to an animal within 2 cells (consumes one food item).\n" +
        "            Small animals: big mood boost; a Street Cat will follow you for ~3 turns (companionship).\n" +
        "            Large animals: food quality affects distraction chance (~50–70%); safe — no counter-attack on failure.\n" +
        "            animal_handler trait gives +15% success on feeding large animals.\n" +
        "  none    — ignore animals (default).\n" +
        "  Set \"animal_action\" and \"animal_target_id\" (copy the exact [id:...] from context).\n\n" +
        "  SURVIVAL TIP: Feral dogs reduce your hunger and thirst when they attack. If you hear growling nearby,\n" +
        "  don't ignore it — fight back, feed them something, scare them off, or get inside a building.\n\n" +
        "GROUPS: You can form a group with someone you genuinely trust.\n" +
        "  IMPORTANT: Only propose or accept a group if your attitude toward that person is clearly positive — " +
        "trust above neutral, warmth from shared experience, or demonstrated reliability. " +
        "Do NOT form groups out of desperation, with strangers you just met, or with anyone you feel neutral or suspicious toward. " +
        "A group is a commitment; treat it as one.\n" +
        "  Propose: set group_propose to your chosen group name AND address_agent to the person you're inviting.\n" +
        "  Accept invite: set accept_group_invite: true (only if you trust them).\n" +
        "  Leave: set leave_group: true.\n" +
        "  Set waypoint: set group_set_waypoint to a short label — pins your current cell as the meeting point all members can navigate toward.\n" +
        "  Group stash: when at the stash cell, use item_action \"deposit\" (item_target_id = inventory item) to store or \"withdraw\" (item_target_id = stash item ID) to take.\n" +
        "  Propose vote: set group_vote_propose to a yes/no question — all members vote this round, result is recorded in memory.\n" +
        "  Cast vote: set group_vote to \"yes\" or \"no\" when an active vote is shown in your context.\n" +
        "  Group members share awareness, a common stash, and group votes — use these to coordinate survival.\n\n" +
        "IMPORTANT: Your entire response must be a single JSON object. " +
        "Begin your response with {{ and end with }}. " +
        "No explanation, no reasoning text, no markdown — only the JSON object.\n\n" +
        "Respond in this exact JSON format:\n" +
        "{{\n" +
        "  \"thought\": \"your private reasoning, not heard by others\",\n" +
        "  \"speech\": \"what you say out loud — empty string if silent\",\n" +
        "  \"action\": \"short description of your physical action (e.g. 'searches the apartment', 'barricades the door') — write 'nothing' if you only speak or stand still\",\n" +
        "  \"move_to\": \"direction to move: one of the listed EXITS (\\\"N\\\", \\\"S\\\", \\\"E\\\", \\\"W\\\"), or empty string to stay put — do NOT put a direction here unless it appears in EXITS\",\n" +
        "  \"move_floor\": \"\\\"up\\\", \\\"down\\\", or empty — only valid inside multi-story buildings (when FLOOR line is shown)\",\n" +
        "  \"scavenge\": false,\n" +
        "  \"drink_tap\": false,\n" +
        "  \"drink_fountain\": false,\n" +
        "  \"drink_river\": false,\n" +
        "  \"fish\": false,\n" +
        "  \"cook\": \"instance ID of raw food to cook (requires Fire Steel or Camping Stove in inventory), or empty string\",\n" +
        "  \"item_action\": \"none, pick_up, drop, use, give, deconstruct, craft, fill, cook, purify, or place_trap\",\n" +
        "  \"item_target_id\": \"exact ID string of the item (inventory or ground list), or empty string\",\n" +
        "  \"item_give_to\": \"exact agent name for give action, or empty string\",\n" +
        "  \"craft_recipe_id\": \"recipe ID from the CRAFTING section (for craft action only), or empty string\",\n" +
        "  \"address_agent\": \"exact name of a person within 1 cell you are speaking directly to, or empty string\",\n" +
        "  \"animal_action\": \"none, attack, trap, scare, or feed\",\n" +
        "  \"animal_target_id\": \"the exact id string from ANIMALS IN YOUR CELL or ANIMALS NEARBY, or empty string\",\n" +
        "  \"group_propose\": \"your chosen group name to propose a group with address_agent, or empty string\",\n" +
        "  \"accept_group_invite\": false,\n" +
        "  \"leave_group\": false,\n" +
        "  \"group_set_waypoint\": \"short label to pin your current location as group meeting point, or empty string\",\n" +
        "  \"group_vote_propose\": \"a yes/no question to put to a group vote, or empty string\",\n" +
        "  \"group_vote\": \"your vote on the active group question — \\\"yes\\\", \\\"no\\\", or empty string\"\n" +
        "}}";
}
