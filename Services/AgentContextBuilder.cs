using System.Text;
using MultiAgentSimWeb.Models;
using MultiAgentSimWeb.Services.Systems;

namespace MultiAgentSimWeb.Services;

/// <summary>
/// Renders the LLM prompt context for a single agent.
/// Extracted from WorldState so that prompt-formatting logic lives separately
/// from simulation state. All reads go through WorldState's public API.
/// </summary>
public static class AgentContextBuilder
{
    public static string Build(WorldState world, string agentName)
    {
        var pos = world.GetAgentPosition(agentName);
        if (pos == (-1, -1))
            return "ERROR: agent not initialized";

        var (cx, cy) = pos;
        var cell = world.GetCell(cx, cy);
        var sb   = new StringBuilder();
        var inv  = world.Items.GetInventory(agentName);

        // Full situation for first 5 rounds; abbreviated after that (agents know it by then)
        string situationText = world.CurrentRound <= 5
            ? world.Situation
            : (world.Situation.Length > 150 ? world.Situation[..150] + "…" : world.Situation);
        sb.AppendLine($"SITUATION:\n{situationText}");
        sb.AppendLine();
        sb.AppendLine($"TIME: {world.CurrentTime} (approximately {world.CurrentRound} hour{(world.CurrentRound == 1 ? "" : "s")} since the blackout)");

        // ── Day / Night block ────────────────────────────────────────────────
        var phase = world.CurrentPhase;
        string phaseLabel = phase switch
        {
            DayPhase.Night => "Night",
            DayPhase.Dawn  => "Dawn",
            DayPhase.Day   => "Day",
            DayPhase.Dusk  => "Dusk",
            _              => ""
        };
        sb.AppendLine($"DAY PHASE: {phaseLabel}");

        if (phase == DayPhase.Night)
        {
            bool nightIndoors = pos.x >= 0 &&
                DayNightSystem.IsIndoors(world.GetCell(pos.x, pos.y).Terrain);
            if (!nightIndoors)
            {
                sb.AppendLine("DARKNESS: The city is pitch black. Animals are bolder tonight — large predators hunt more aggressively.");
                if (!world.DayNight.HasWarmth(agentName))
                    sb.AppendLine("COLD: You have no warmth item (blanket, winter_coat, leather_wrap, sleeping_bag, or fur_vest). The cold is draining your energy — you lose extra hunger each turn.");
                else
                    sb.AppendLine("WARMTH: Your gear keeps out the cold.");
                if (!world.DayNight.HasLightSource(agentName))
                    sb.AppendLine("LIGHT: No light source — scavenging is harder in the dark. A flashlight, candle, or lighter would help.");
                else
                    sb.AppendLine("LIGHT: Your light source helps you navigate — but it also makes you visible to predators at greater distance.");
            }
            else
            {
                sb.AppendLine("SHELTERED: You are indoors for the night. No cold drain.");
                if (world.Presence.IsStationary(agentName))
                    sb.AppendLine("REST: You are resting — staying still indoors tonight slowly restores health and reduces stress (+2 health, -5 stress per turn).");
                else
                    sb.AppendLine("REST: Stay in one place indoors to rest and recover health and stress.");
            }
        }
        else if (phase == DayPhase.Dawn)
            sb.AppendLine("DAWN: The sky is lightening. The night's dangers are fading — the city is waking up.");
        else if (phase == DayPhase.Day)
            sb.AppendLine("DAYLIGHT: Full visibility. Best conditions for movement, scavenging, and exploring new areas.");
        else if (phase == DayPhase.Dusk)
            sb.AppendLine("DUSK: Light is fading. Animals will be bolder soon — consider finding shelter before full dark.");

        sb.AppendLine(world.Weather.GetContextBlock(agentName));

        // ── Position / terrain ───────────────────────────────────────────────
        sb.AppendLine($"YOUR POSITION: ({cx}, {cy})");
        string terrainLabel = cell.BuildingName != null
            ? $"{cell.BuildingName} ({cell.DisplayName})"
            : cell.DisplayName;
        sb.AppendLine($"TERRAIN: {terrainLabel} — {cell.Description}");

        // ── Personality flags ────────────────────────────────────────────────
        // Blurb is already in the system prompt — only emit the per-flag behavioural
        // nudges here, since those are not in the system prompt.
        var personality = world.GetPersonality(agentName);
        if (personality.Flags.Count > 0)
        {
            sb.AppendLine();
            foreach (var flag in personality.Flags)
            {
                string? flagNote = flag switch
                {
                    "hoards_food"         => "NOTE (personality): You have a strong instinct to stockpile food and water rather than consume them early. Scarcity is real and you've seen what happens when supplies run out.",
                    "distrusts_strangers" => "NOTE (personality): Your gut tells you not to trust people who haven't proven themselves. Strangers are potential threats until demonstrated otherwise.",
                    "fears_dark"          => "NOTE (personality): The darkness and silence of the blackout are getting to you more than you'd like to admit. Being alone in the dark is difficult.",
                    "protects_others"     => "NOTE (personality): Despite everything, you feel a pull toward protecting people who are weaker or more scared than you — even at cost to yourself.",
                    "prone_to_panic"      => "NOTE (personality): Under extreme pressure your emotions can spiral. High stress makes you less rational and more reactive — watch for it.",
                    "night_owl"           => "NOTE (personality): The night energises you. The dark city feels more alive to you than it should — you're more at ease out here than most.",
                    "claustrophobic"      => "NOTE (personality): Enclosed spaces put you on edge. Staying indoors for too long builds pressure inside you that's hard to shake.",
                    "self_reliant"        => "NOTE (personality): You're used to solving your own problems. Help offered freely makes you more uncomfortable than grateful — it implies debt.",
                    "paranoid"            => "NOTE (personality): You struggle to fully trust people you don't know. Unknown faces nearby keep you watchful and wired regardless of whether the threat is real.",
                    "risk_taker"          => "NOTE (personality): You've always leaned into danger rather than away from it. High-stakes moments sharpen you instead of paralyzing you.",
                    "open_to_romance"     => "NOTE (personality): You're not closing yourself off emotionally, even now. If something real develops with someone you trust deeply, you won't run from it.",
                    _                     => null
                };
                if (flagNote != null) sb.AppendLine(flagNote);
            }
            sb.AppendLine();
        }

        // ── Floor / water sources ────────────────────────────────────────────
        if (cell.Floors > 1)
        {
            int agentFloor = world.GetAgentFloor(agentName);
            sb.AppendLine($"FLOOR: You are on floor {agentFloor} of {cell.Floors}. Use move_floor: \"up\" or \"down\" to change floors.");
        }

        bool hasTap = (cell.Terrain == TerrainType.Apartment || cell.Terrain == TerrainType.Storefront)
                      && world.CurrentRound <= WorldState.TapPressureRounds;
        if (hasTap)
        {
            string tapNote = world.CurrentRound <= 36
                ? "TAP WATER: Taps and sinks still have pressure. Set drink_tap: true to drink (+30 thirst)."
                : "TAP WATER: Water pressure is weakening -- may not last much longer. Set drink_tap: true to drink (+20 thirst).";
            sb.AppendLine(tapNote);
        }

        if (cell.BuildingName == WorldState.FountainName)
            sb.AppendLine("FOUNTAIN: A stone fountain -- underground pressure keeps the water flowing. Set drink_fountain: true to drink (+20 thirst). You can also fill containers here (use item_action \"fill\").");

        if (cell.Terrain == TerrainType.River)
        {
            sb.AppendLine("RIVER: The Irongate River flows here -- clean enough to drink. Set drink_river: true to drink (+25 thirst). You can also fill containers here (use item_action \"fill\"). The river never runs dry.");
            bool hasFishingHook = inv.Any(i => i.DefinitionId == "fishing_hook" && i.UsesRemaining > 0);
            sb.AppendLine(hasFishingHook
                ? "FISHING: You have a Fishing Hook. Set fish: true to cast a line (65% catch chance). Each cast uses one hook charge. Raw fish must be cooked before eating."
                : "FISHING: You need a Fishing Hook to fish here. Search near riverbanks to find one.");
            bool hasPurifyTablet    = inv.Any(i => i.DefinitionId == "purification_tablet" && i.UsesRemaining > 0);
            bool hasFilledContainer = inv.Any(i => !string.IsNullOrEmpty(i.Definition.PurifyResult));
            if (hasPurifyTablet && hasFilledContainer)
                sb.AppendLine("PURIFY: You have a Purification Tablet and a filled container. Use item_action \"purify\" on the container's ID to produce safe stored water (+35 thirst when drunk, stores indefinitely).");
        }

        if (cell.Terrain == TerrainType.Forest)
        {
            bool hasForagingKnife = inv.Any(i => i.DefinitionId == "foraging_knife");
            string knifeNote = hasForagingKnife
                ? " Your Foraging Knife grants bonus rolls for extra berries and mushrooms."
                : " A Foraging Knife (found deeper in the forest or on hunters) would improve yields.";
            sb.AppendLine($"FOREST: Dense woodland. Set scavenge: true to forage for wild berries and mushrooms.{knifeNote} Animals are present -- foxes are harmless, but deer can be hunted for meat.");
        }

        // ── Cooking ──────────────────────────────────────────────────────────
        bool hasCookingTool  = inv.Any(i => WorldState.CookingTools.Contains(i.DefinitionId));
        var  cookableItems   = inv.Where(i => i.Definition.IsCookable && !string.IsNullOrEmpty(i.Definition.CookResult)).ToList();
        if (hasCookingTool && cookableItems.Count > 0)
        {
            sb.AppendLine("COOKING: You have a cooking tool and raw food. Use item_action \"cook\" with the raw item's ID to cook it:");
            foreach (var ci in cookableItems)
                sb.AppendLine($"  [{ci.InstanceId}] {ci.Definition.Name} => {ItemRegistry.Get(ci.Definition.CookResult).Name}");
        }
        else if (cookableItems.Count > 0)
        {
            sb.AppendLine("COOKING: You have raw food but no cooking tool (Fire Steel or Camping Stove needed to cook).");
        }

        // ── Shelter ──────────────────────────────────────────────────────────
        var  shelterPos = world.Presence.GetShelter(agentName);
        bool hasShelter = shelterPos.x >= 0;
        bool atShelter  = hasShelter && shelterPos == pos;
        if (atShelter)
        {
            sb.AppendLine("YOUR SHELTER: You are at your base. Familiarity eases your stress slightly each turn you stay here.");
        }
        else if (hasShelter)
        {
            var sc    = world.GetCell(shelterPos.x, shelterPos.y);
            int dist  = Math.Abs(shelterPos.x - cx) + Math.Abs(shelterPos.y - cy);
            string sn = sc.BuildingName ?? sc.DisplayName;
            sb.AppendLine($"YOUR SHELTER: {sn} ({shelterPos.x},{shelterPos.y}) -- {dist} step{(dist == 1 ? "" : "s")} away. Items you left there may still be waiting.");
        }

        // ── Survival meters ──────────────────────────────────────────────────
        sb.AppendLine();
        float hunger  = world.Survival.GetHunger(agentName);
        float thirst  = world.Survival.GetThirst(agentName);
        float health  = world.Survival.GetHealth(agentName);
        float maxHp   = world.Survival.GetMaxHealth(agentName);
        float stamina = world.Survival.GetStamina(agentName);
        sb.AppendLine($"HUNGER: {hunger:F0}/100 ({world.Survival.HungerLabel(hunger)})");
        sb.AppendLine($"THIRST: {thirst:F0}/100 ({world.Survival.ThirstLabel(thirst)})");
        sb.AppendLine($"HEALTH: {health:F0}/{maxHp:F0} ({world.Survival.HealthLabel(health)})");
        sb.AppendLine($"STAMINA: {stamina:F0}/100 ({world.Survival.StaminaLabel(stamina)})");
        if (world.Survival.IsCritical(agentName))
            sb.AppendLine("WARNING: You are in danger of dying from starvation or dehydration. Find food or water NOW.");
        if (world.Survival.IsHealthCritical(agentName))
            sb.AppendLine("WARNING: You are critically injured. Find and use medical supplies (first_aid_kit, bandage_roll, antiseptic, surgical_kit) immediately.");
        if (world.Survival.IsExhausted(agentName))
            sb.AppendLine("WARNING: You are exhausted. Rest by staying indoors at night to recover stamina. Scavenging is less effective while exhausted.");
        int idleTurns = world.Survival.GetIdleTurns(agentName);
        if (idleTurns >= 3)
            sb.AppendLine($"NOTE: You have been idle for {idleTurns} turn{(idleTurns == 1 ? "" : "s")} without purpose. Restlessness is setting in — move, scavenge, or find someone to talk to.");
        sb.AppendLine();

        // ── Emotional state ──────────────────────────────────────────────────
        if (world.Mood.Has(agentName))
        {
            var ctxMood    = world.Mood.GetMood(agentName);
            var knownPeers = world.AgentNames
                .Where(n => n != agentName && world.KnowsName(agentName, n))
                .ToList();
            sb.AppendLine("EMOTIONAL STATE:");
            sb.AppendLine($"  Mood: {ctxMood.MoodLabel} ({ctxMood.Mood:+0;-0;0})  |  Stress: {ctxMood.StressLabel} ({ctxMood.Stress:F0})");
            sb.AppendLine($"  Long-term: {ctxMood.TraumaLabel} (trauma {ctxMood.Trauma:F0})  |  {ctxMood.HopeLabel} (hope {ctxMood.Hope:F0})");
            if (knownPeers.Count > 0)
            {
                sb.AppendLine("  Attitudes toward people you've met:");
                foreach (var peer in knownPeers)
                {
                    float t  = ctxMood.GetTrust(peer);
                    var   mp = world.Presence.GetMeetingPoint(agentName, peer);
                    string mpNote = mp.Item1 >= 0
                        ? $"  [last met: {world.GetCell(mp.Item1, mp.Item2).BuildingName ?? world.GetCell(mp.Item1, mp.Item2).DisplayName} ({mp.Item1},{mp.Item2})]"
                        : "";
                    string romanceNote = world.AreRomantic(agentName, peer) ? " [romantic partner]" : "";
                    sb.AppendLine($"    {peer} -- {AgentMood.TrustLabel(t)} ({t:+0;-0;0}){romanceNote}{mpNote}");
                }
            }
            sb.AppendLine("  Your emotional state is real. Let it shape your tone, choices, and how you treat others.");
            sb.AppendLine();
        }

        // ── Memory ───────────────────────────────────────────────────────────
        var ctxMemory = world.Memory.GetMemory(agentName);
        if (ctxMemory.Recent.Count > 0)
        {
            sb.AppendLine(ctxMemory.Format());
            sb.AppendLine();
        }

        // ── Surroundings grid ────────────────────────────────────────────────
        sb.AppendLine("NEARBY (within 1 cell):");
        (int dx, int dy, string label)[] dirs =
        [
            ( 0, -1, "North"), ( 0,  1, "South"),
            ( 1,  0, "East"),  (-1,  0, "West"),
            ( 1, -1, "NE"),    (-1, -1, "NW"),
            ( 1,  1, "SE"),    (-1,  1, "SW"),
        ];
        foreach (var (dx, dy, label) in dirs)
        {
            int nx = cx + dx, ny = cy + dy;
            if (world.IsInBounds(nx, ny))
            {
                var nc     = world.GetCell(nx, ny);
                string ncl = nc.BuildingName != null ? $"{nc.BuildingName} ({nc.DisplayName})" : nc.DisplayName;
                sb.AppendLine($"  {label} ({nx},{ny}): {ncl}");
            }
            else
            {
                sb.AppendLine($"  {label}: [edge of map]");
            }
        }

        // ── Other agents ─────────────────────────────────────────────────────
        sb.AppendLine();
        var nearby = world.GetVisibleAgents(agentName);
        if (nearby.Count > 0)
        {
            sb.AppendLine("OTHERS NEARBY:");
            foreach (var (name, nx, ny) in nearby)
            {
                int    dist     = Math.Max(Math.Abs(nx - cx), Math.Abs(ny - cy));
                string distNote = dist == 0 ? "same cell" : $"{dist} cell{(dist == 1 ? "" : "s")} away";
                string who      = world.DescribeAgent(agentName, name);
                string relNote  = "";
                if (world.KnowsName(agentName, name) && world.Mood.Has(agentName))
                {
                    float t = world.Mood.GetMood(agentName).GetTrust(name);
                    if      (world.AreRomantic(agentName, name)) relNote = " [romantic partner]";
                    else if (t > 70f)                            relNote = " [close friend]";
                    else if (t < -30f)                           relNote = " [hostile]";
                }
                sb.AppendLine($"  {who} is at ({nx},{ny}) [{distNote}]{relNote}");
            }
        }
        else
        {
            sb.AppendLine("OTHERS NEARBY: No one nearby.");
        }

        // ── Group ────────────────────────────────────────────────────────────
        var agentGroup    = world.Groups.GetGroup(agentName);
        var pendingInvite = world.Groups.GetPendingInvite(agentName);

        if (agentGroup != null)
        {
            sb.AppendLine();
            sb.AppendLine($"GROUP -- \"{agentGroup.Name}\":");

            var memberParts = new List<string>();
            foreach (var m in agentGroup.Members)
            {
                if (m == agentName) { memberParts.Add($"{m} (you)"); continue; }
                var mp = world.GetAgentPosition(m);
                if (mp == (-1, -1)) { memberParts.Add($"{m} (dead)"); continue; }
                int mdist = Math.Max(Math.Abs(mp.x - cx), Math.Abs(mp.y - cy));
                string prox = mdist == 0 ? "same cell" : $"{mdist} cell{(mdist == 1 ? "" : "s")} away";
                memberParts.Add($"{m} ({prox})");
            }
            sb.AppendLine($"  Members: {string.Join(", ", memberParts)}");

            if (agentGroup.Waypoint is { } wp)
            {
                int    wdist = Math.Abs(wp.x - cx) + Math.Abs(wp.y - cy);
                var    wCell = world.GetCell(wp.x, wp.y);
                string wName = wCell.BuildingName ?? wCell.DisplayName;
                string wDir  = WorldState.CompassDirection(cx, cy, wp.x, wp.y);
                sb.AppendLine($"  Waypoint: \"{wp.Description}\" -- {wName} ({wp.x},{wp.y}), {wdist} step{(wdist == 1 ? "" : "s")} {wDir} (set by {wp.SetBy})");
            }

            if (agentGroup.StashLocation is { } sl)
            {
                var    sc      = world.GetCell(sl.x, sl.y);
                string sn      = sc.BuildingName ?? sc.DisplayName;
                bool   atStash = sl.x == cx && sl.y == cy;
                if (atStash)
                {
                    sb.AppendLine($"  GROUP STASH -- you are here ({sl.x},{sl.y}) -- {agentGroup.Stash.Count} item{(agentGroup.Stash.Count == 1 ? "" : "s")}:");
                    if (agentGroup.Stash.Count > 0)
                        foreach (var si in agentGroup.Stash)
                            sb.AppendLine($"    [{si.InstanceId}] {si.DisplayName} -- {si.Definition.Description}");
                    else
                        sb.AppendLine("    (empty)");
                    sb.AppendLine("    Use item_action \"deposit\" (item_target_id = inventory item ID) to add, \"withdraw\" (item_target_id = stash item ID) to take.");
                }
                else
                {
                    int    sdist = Math.Abs(sl.x - cx) + Math.Abs(sl.y - cy);
                    string sDir  = WorldState.CompassDirection(cx, cy, sl.x, sl.y);
                    sb.AppendLine($"  Group stash: {agentGroup.Stash.Count} item{(agentGroup.Stash.Count == 1 ? "" : "s")} at {sn} ({sl.x},{sl.y}) -- {sdist} step{(sdist == 1 ? "" : "s")} {sDir}");
                }
            }
            else
            {
                sb.AppendLine("  Group stash: not established -- use item_action \"deposit\" anywhere to create it at your current cell.");
            }

            if (agentGroup.ActiveVote is { } vote)
            {
                sb.AppendLine();
                int yes2     = vote.Votes.Values.Count(v => v == "yes");
                int no2      = vote.Votes.Values.Count(v => v == "no");
                int pending2 = agentGroup.Members.Count(m => world.GetAgentPosition(m) != (-1, -1)) - vote.Votes.Count;
                sb.AppendLine($"  ACTIVE VOTE: \"{vote.Question}\" (proposed by {vote.Proposer})");
                sb.AppendLine($"    {yes2} yes / {no2} no / {Math.Max(0, pending2)} pending");
                if (vote.Votes.TryGetValue(agentName, out var myVote))
                    sb.AppendLine($"    You voted: \"{myVote}\"");
                else
                    sb.AppendLine("    You have not voted yet -- set group_vote to \"yes\" or \"no\" this turn.");
            }

            sb.AppendLine("  (Leave group: set leave_group: true. Set meeting point: set group_set_waypoint to a short label.)");
        }
        else if (pendingInvite is { } invite)
        {
            sb.AppendLine();
            sb.AppendLine($"GROUP INVITE: {invite.Inviter} has invited you to join \"{invite.GroupName}\".");
            sb.AppendLine("  Set accept_group_invite: true to join, or leave it false to decline (invite expires after this round).");
        }

        // ── Direct message hint ──────────────────────────────────────────────
        sb.AppendLine();
        var knownNearby = nearby.Where(a => world.KnowsName(agentName, a.name)).ToList();
        if (knownNearby.Count > 0)
        {
            sb.AppendLine("DIRECT MESSAGE -- to address someone privately, copy their name exactly into address_agent:");
            foreach (var (name, _, _) in knownNearby)
                sb.AppendLine($"  \"{name}\"");
        }
        else if (nearby.Count > 0)
        {
            sb.AppendLine("DIRECT MESSAGE: You don't know anyone nearby by name yet -- speak generally to be heard, and say your name to introduce yourself. Leave address_agent empty.");
        }
        else
        {
            sb.AppendLine("DIRECT MESSAGE: No one within range -- leave address_agent empty.");
        }

        // ── Exits / scavenge ─────────────────────────────────────────────────
        sb.AppendLine();
        var exits = new List<string>();
        if (world.IsInBounds(cx, cy - 1)) exits.Add("N");
        if (world.IsInBounds(cx, cy + 1)) exits.Add("S");
        if (world.IsInBounds(cx + 1, cy)) exits.Add("E");
        if (world.IsInBounds(cx - 1, cy)) exits.Add("W");
        sb.AppendLine($"EXITS: {(exits.Count > 0 ? string.Join(", ", exits) : "none")}");
        if (world.Foraging.CanForage(cell.Terrain))
            sb.AppendLine("SCAVENGE: Set \"scavenge\": true to search this building for supplies. Resolves at your final position AFTER any movement this turn.");
        else
            sb.AppendLine("SCAVENGE: Not available here -- must be inside an Apartment or Storefront. Move into a building first, then scavenge next turn.");

        // ── Inventory ────────────────────────────────────────────────────────
        sb.AppendLine();
        int    capacity      = world.Items.GetCarryCapacity(agentName);
        var    containers    = inv.Where(i => i.Definition.CarryCapacity > 0).ToList();
        string containerNote = containers.Count > 0
            ? " -- " + string.Join(", ", containers.Select(c => $"{c.Definition.Name}: +{c.Definition.CarryCapacity}"))
            : "";
        sb.AppendLine($"YOUR INVENTORY ({inv.Count}/{capacity} slots{containerNote}):");
        if (inv.Count > 0)
            foreach (var it in inv)
                sb.AppendLine($"  [{it.InstanceId}] {it.DisplayName}");
        else
            sb.AppendLine("  (empty)");
        if (world.Items.IsInventoryFull(agentName))
            sb.AppendLine("  WARNING: INVENTORY FULL -- drop or use an item before picking up more. Find a bag or backpack to expand capacity.");

        sb.AppendLine();
        var cellItemList = world.Items.GetItemsAt(cx, cy);
        sb.AppendLine("ITEMS HERE (at your position):");
        if (cellItemList.Count > 0)
            foreach (var it in cellItemList)
                sb.AppendLine($"  [{it.InstanceId}] {it.DisplayName} -- {it.Definition.Description}");
        else
            sb.AppendLine("  (none)");

        var armedTraps = world.Items.GetPlacedTrapsAt(cx, cy);
        if (armedTraps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("TRAPS ARMED HERE:");
            foreach (var t in armedTraps)
                sb.AppendLine($"  [{t.InstanceId}] {t.DisplayName} -- armed and waiting");
            sb.AppendLine("  (Small animals that wander here will be caught. Pick up with pick_up to retrieve.)");
        }

        // ── Crafting (dynamic: what can be made right now) ───────────────────
        var available = RecipeRegistry.GetAvailable(world.Items.GetInventory(agentName));
        if (available.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("CRAFTING (recipes available with your current inventory):");
            foreach (var r in available)
            {
                var ingNames = string.Join(" + ", r.Ingredients.Select(id => ItemRegistry.Get(id).Name));
                sb.AppendLine($"  [{r.Id}] {r.Name} -- needs: {ingNames} -- {r.Description}");
            }
        }

        // ── Animals ──────────────────────────────────────────────────────────
        sb.AppendLine();
        var cellAnimals = world.Animals.GetAnimalsAt(cx, cy);
        sb.AppendLine("ANIMALS IN YOUR CELL:");
        if (cellAnimals.Count > 0)
            foreach (var a in cellAnimals)
            {
                string sizeTag = a.Size == AnimalSize.Large ? "[LARGE -- DANGEROUS]" : "[small]";
                sb.AppendLine($"  {sizeTag} {a.DisplayName} [id:{a.Id}] -- HP:{a.Health:F0}/{a.MaxHealth:F0} -- {a.Description}");
            }
        else
            sb.AppendLine("  (none)");

        sb.AppendLine();
        var nearbyAnimals = world.Animals.GetAnimalsInRadius(cx, cy, 3)
            .Where(a => !(a.X == cx && a.Y == cy))
            .OrderBy(a => Math.Max(Math.Abs(a.X - cx), Math.Abs(a.Y - cy)))
            .ToList();
        sb.AppendLine("ANIMALS NEARBY (within 3 cells):");
        if (nearbyAnimals.Count > 0)
            foreach (var a in nearbyAnimals)
            {
                int    dist    = Math.Max(Math.Abs(a.X - cx), Math.Abs(a.Y - cy));
                string sizeTag = a.Size == AnimalSize.Large ? "[LARGE]" : "[small]";
                string state   = a.State switch
                {
                    AnimalState.Hunting => " -- HUNTING",
                    AnimalState.Fleeing => " -- fleeing",
                    _                   => ""
                };
                sb.AppendLine($"  {sizeTag} {a.DisplayName} at ({a.X},{a.Y}) [{dist} cell{(dist == 1 ? "" : "s")} away]{state}");
            }
        else
            sb.AppendLine("  (none nearby)");

        // ── Recent area events ───────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("RECENT EVENTS IN YOUR AREA:");
        var allEntries = new List<string>();
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int lx = cx + dx, ly = cy + dy;
                var entries = world.GetAllCellLog(lx, ly);
                if (world.IsInBounds(lx, ly) && entries.Count > 0)
                    allEntries.AddRange(entries);
            }
        var recentEntries = allEntries.TakeLast(8).ToList();
        sb.AppendLine(recentEntries.Count > 0
            ? string.Join("\n", recentEntries)
            : "(Nothing has happened nearby yet.)");

        return sb.ToString();
    }
}
