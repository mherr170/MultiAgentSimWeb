namespace MultiAgentSimWeb.Models;

public static class ItemRegistry
{
    private static readonly Dictionary<string, ItemDefinition> _items = new()
    {
        // ── Water & drinks ───────────────────────────────────────────────────
        ["water_bottle"] = new ItemDefinition
        {
            Id = "water_bottle", Name = "Water Bottle",
            Description = "A sealed 500ml bottle of water. Clean and safe to drink.",
            IsUsable = true, UseEffect = "You drain the bottle. The dryness in your throat eases.",
            ThirstRestore = 50f, MoodDelta = 8f, StressDelta = -6f,
            IsDeconstructable = false
        },
        ["sports_drink"] = new ItemDefinition
        {
            Id = "sports_drink", Name = "Sports Drink",
            Description = "A 750ml bottle of electrolyte drink. Warm, but hydrating.",
            IsUsable = true, UseEffect = "You drink it in several gulps. Salty-sweet and oddly refreshing.",
            ThirstRestore = 35f, HungerRestore = 8f, MoodDelta = 8f, StressDelta = -3f,
            IsDeconstructable = false
        },
        ["alcohol_bottle"] = new ItemDefinition
        {
            Id = "alcohol_bottle", Name = "Bottle of Spirits",
            Description = "A half-full bottle of whiskey or vodka. The end of the world is a reasonable excuse.",
            IsUsable = true, UseEffect = "You take a long pull. The warmth spreads through your chest and the edge comes off — for now.",
            HungerRestore = 5f, ThirstRestore = 5f, MoodDelta = 22f, StressDelta = -18f,
            IsDeconstructable = false
        },
        ["energy_drink"] = new ItemDefinition
        {
            Id = "energy_drink", Name = "Energy Drink",
            Description = "A 250ml can. 200mg caffeine. Your hands may shake, but you'll be alert.",
            IsUsable = true, UseEffect = "You crack it open and drink fast. Your heart rate climbs and your thoughts race.",
            HungerRestore = 5f, ThirstRestore = 20f, MoodDelta = 5f, StressDelta = 8f,
            IsDeconstructable = false
        },

        // ── Food ─────────────────────────────────────────────────────────────
        ["canned_food"] = new ItemDefinition
        {
            Id = "canned_food", Name = "Canned Food",
            Description = "A dented can of beans, soup, or vegetables. No label, but definitely edible.",
            IsUsable = true, UseEffect = "You eat straight from the can with a spoon. It's not much but it takes the edge off.",
            HungerRestore = 25f, MoodDelta = 6f, StressDelta = -5f,
            IsDeconstructable = false
        },
        ["instant_noodles"] = new ItemDefinition
        {
            Id = "instant_noodles", Name = "Instant Noodles",
            Description = "A cup of dry ramen. Without power, you eat them uncooked. Surprisingly filling.",
            IsUsable = true, UseEffect = "You crush the dry noodles and eat them from the cup. Crunchy, salty, and oddly satisfying.",
            HungerRestore = 28f, MoodDelta = 5f, StressDelta = -4f,
            IsDeconstructable = false
        },
        ["peanut_butter"] = new ItemDefinition
        {
            Id = "peanut_butter", Name = "Peanut Butter",
            Description = "A 500g jar of peanut butter. Calorie-dense and long-lasting — a survival staple.",
            IsUsable = true, UseEffect = "You eat it by the spoonful. Thick and rich. You feel the calories hitting immediately.",
            HungerRestore = 35f, MoodDelta = 8f, StressDelta = -5f,
            IsDeconstructable = false
        },
        ["protein_bar"] = new ItemDefinition
        {
            Id = "protein_bar", Name = "Protein Bar",
            Description = "A dense 80g bar from an emergency kit. Chalky but calorie-packed.",
            IsUsable = true, UseEffect = "You eat the bar. Chalky and thick, but sustaining.",
            HungerRestore = 35f, MoodDelta = 6f, StressDelta = -5f,
            IsDeconstructable = false
        },
        ["granola_bar"] = new ItemDefinition
        {
            Id = "granola_bar", Name = "Granola Bar",
            Description = "A slightly stale oat-and-honey bar. Small, but better than nothing.",
            IsUsable = true, UseEffect = "You eat the granola bar. Sweet and a little filling.",
            HungerRestore = 18f, MoodDelta = 5f, StressDelta = -3f,
            IsDeconstructable = false
        },
        ["chocolate_bar"] = new ItemDefinition
        {
            Id = "chocolate_bar", Name = "Chocolate Bar",
            Description = "A full-size chocolate bar. More valuable for morale than calories right now.",
            IsUsable = true, UseEffect = "The chocolate melts on your tongue. For a brief moment everything feels almost normal.",
            HungerRestore = 12f, MoodDelta = 14f, StressDelta = -10f,
            IsDeconstructable = false
        },
        ["crackers"] = new ItemDefinition
        {
            Id = "crackers", Name = "Box of Crackers",
            Description = "A large box of salted crackers from someone's pantry. Dry but filling.",
            IsUsable = true, UseEffect = "You eat a handful. Dry and bland, but they fill the hollow feeling.",
            HungerRestore = 20f, MoodDelta = 4f, StressDelta = -3f,
            IsDeconstructable = false
        },
        ["canned_meat"] = new ItemDefinition
        {
            Id = "canned_meat", Name = "Canned Meat",
            Description = "A can of chicken or tuna. High protein, no cooking required.",
            IsUsable = true, UseEffect = "You eat the meat from the can. Salty and dense — your body needed this.",
            HungerRestore = 38f, MoodDelta = 7f, StressDelta = -5f,
            IsDeconstructable = false
        },

        // ── Kitchen pantry ───────────────────────────────────────────────────
        ["instant_coffee"] = new ItemDefinition
        {
            Id = "instant_coffee", Name = "Instant Coffee",
            Description = "A jar of instant coffee. Mixed with cold water it's bitter, but it's something.",
            IsUsable = true, UseEffect = "You stir a spoonful into cold water and drink it. Bitter and cold — but the familiar smell alone does something for you.",
            ThirstRestore = 10f, MoodDelta = 18f, StressDelta = -15f,
            IsDeconstructable = false
        },
        ["honey_jar"] = new ItemDefinition
        {
            Id = "honey_jar", Name = "Jar of Honey",
            Description = "A 500g jar of honey. Indefinite shelf life, dense calories, and one of the few genuinely good things left.",
            IsUsable = true, UseEffect = "You eat several spoonfuls straight from the jar. Sweet and thick. Your body responds immediately.",
            HungerRestore = 22f, MoodDelta = 12f, StressDelta = -8f,
            IsDeconstructable = false
        },
        ["dried_pasta"] = new ItemDefinition
        {
            Id = "dried_pasta", Name = "Bag of Pasta",
            Description = "A 500g bag of dry pasta. Unpleasant raw, but edible when you're hungry enough.",
            IsUsable = true, UseEffect = "You crunch through a handful of dry pasta. Starchy and odd, but filling.",
            HungerRestore = 20f, MoodDelta = 3f, StressDelta = -3f,
            IsDeconstructable = false
        },
        ["cereal_box"] = new ItemDefinition
        {
            Id = "cereal_box", Name = "Box of Cereal",
            Description = "A mostly-full box of breakfast cereal. Dry and stale, but reminds you of mornings before.",
            IsUsable = true, UseEffect = "You eat handfuls from the box. Dry and bland, but the familiar taste helps.",
            HungerRestore = 18f, MoodDelta = 6f, StressDelta = -5f,
            IsDeconstructable = false
        },
        ["cooking_oil"] = new ItemDefinition
        {
            Id = "cooking_oil", Name = "Bottle of Cooking Oil",
            Description = "A litre of vegetable oil. Dense in calories if you can stomach it. Could fuel an improvised lamp.",
            IsUsable = true, UseEffect = "You force down a small amount of oil. Nauseating, but your body is running low on fat.",
            HungerRestore = 14f, MoodDelta = -4f, StressDelta = 3f,
            IsDeconstructable = false
        },

        // ── Medical & comfort ────────────────────────────────────────────────
        ["first_aid_kit"] = new ItemDefinition
        {
            Id = "first_aid_kit", Name = "First Aid Kit",
            Description = "A standard red kit. Bandages, antiseptic wipes, pain tablets, a tourniquet.",
            IsUsable = true, UseEffect = "You clean and dress your injuries. The sting fades and your head clears.",
            MoodDelta = 10f, StressDelta = -15f,
            IsDeconstructable = false
        },
        ["painkillers"] = new ItemDefinition
        {
            Id = "painkillers", Name = "Painkillers",
            Description = "A blister pack of ibuprofen or paracetamol. Widely available, genuinely useful.",
            IsUsable = true, UseEffect = "You swallow two tablets. The headache and tension ease within minutes.",
            MoodDelta = 15f, StressDelta = -12f,
            IsDeconstructable = false
        },
        ["prescription_meds"] = new ItemDefinition
        {
            Id = "prescription_meds", Name = "Prescription Medication",
            Description = "Someone's anti-anxiety or sleep medication. Could calm a person down considerably.",
            IsUsable = true, UseEffect = "You take a dose. A heavy calm settles over you. The panic recedes to a manageable distance.",
            MoodDelta = 5f, StressDelta = -22f,
            IsDeconstructable = false
        },
        ["blanket"] = new ItemDefinition
        {
            Id = "blanket", Name = "Blanket",
            Description = "A thick fleece blanket from someone's couch. Warmth does something for the soul in the dark.",
            IsUsable = true, UseEffect = "You wrap the blanket around your shoulders. The darkness feels slightly less absolute.",
            MoodDelta = 15f, StressDelta = -15f,
            IsDeconstructable = true, DeconstructChance = 1.0f,
            DeconstructYields = ["fabric_strips", "fabric_strips"]
        },
        ["fabric_strips"] = new ItemDefinition
        {
            Id = "fabric_strips", Name = "Fabric Strips",
            Description = "Long strips of cloth torn from a blanket or clothing. Useful for bandaging or binding.",
            IsUsable = false,
            IsDeconstructable = false
        },
        ["antiseptic"] = new ItemDefinition
        {
            Id = "antiseptic", Name = "Antiseptic",
            Description = "A bottle of hydrogen peroxide or iodine. Burns like fire, but cleans wounds properly.",
            IsUsable = true, UseEffect = "You clean the wound. It stings sharply. Better than an infection.",
            MoodDelta = 8f, StressDelta = -12f,
            IsDeconstructable = false
        },
        ["bandage_roll"] = new ItemDefinition
        {
            Id = "bandage_roll", Name = "Bandage Roll",
            Description = "A roll of gauze from the bathroom cabinet. Basic, but it stops the bleeding.",
            IsUsable = true, UseEffect = "You wrap the gauze tight. The wound is covered. It'll hold.",
            MoodDelta = 6f, StressDelta = -10f,
            IsDeconstructable = false
        },
        ["hand_sanitizer"] = new ItemDefinition
        {
            Id = "hand_sanitizer", Name = "Hand Sanitizer",
            Description = "A 250ml pump bottle. Disinfects wounds in a pinch and keeps your hands clean.",
            IsUsable = true, UseEffect = "You clean your hands and treat any small cuts. Small thing, but it matters.",
            MoodDelta = 5f, StressDelta = -6f,
            IsDeconstructable = false
        },
        ["photo_album"] = new ItemDefinition
        {
            Id = "photo_album", Name = "Photo Album",
            Description = "A family photo album left on a coffee table. Looking at it in the dark is both painful and steadying.",
            IsUsable = true, UseEffect = "You sit with the album for a while. Faces you miss. It hurts, but it also reminds you what you're holding on for.",
            MoodDelta = 16f, StressDelta = -18f,
            IsDeconstructable = false
        },
        ["book"] = new ItemDefinition
        {
            Id = "book", Name = "Paperback Novel",
            Description = "A dog-eared thriller from someone's bookshelf. You won't sleep tonight anyway.",
            IsUsable = true, UseEffect = "You read by whatever light you have. The story pulls you somewhere else for a while. You needed this.",
            MoodDelta = 14f, StressDelta = -18f,
            IsDeconstructable = false
        },
        ["playing_cards"] = new ItemDefinition
        {
            Id = "playing_cards", Name = "Deck of Cards",
            Description = "A worn deck of playing cards. Solitaire in the dark — at least it's something to do.",
            IsUsable = true, UseEffect = "You play several hands of solitaire. Quiet, methodical. The anxiety loosens slightly.",
            MoodDelta = 12f, StressDelta = -12f,
            IsDeconstructable = false
        },
        ["stuffed_animal"] = new ItemDefinition
        {
            Id = "stuffed_animal", Name = "Stuffed Animal",
            Description = "A child's soft toy left on a bed. You'd be embarrassed, but nobody is watching.",
            IsUsable = true, UseEffect = "You hold it for a while. It's absurd. It helps.",
            MoodDelta = 14f, StressDelta = -18f,
            IsDeconstructable = false
        },
        ["winter_coat"] = new ItemDefinition
        {
            Id = "winter_coat", Name = "Winter Coat",
            Description = "A heavy insulated coat from someone's closet. Warmth in the dark does wonders.",
            IsUsable = true, UseEffect = "You pull the coat on. The warmth is immediate and profound. You feel less exposed.",
            MoodDelta = 12f, StressDelta = -14f,
            IsDeconstructable = true, DeconstructChance = 1.0f,
            DeconstructYields = ["fabric_strips", "fabric_strips"]
        },

        // ── Light ────────────────────────────────────────────────────────────
        ["flashlight"] = new ItemDefinition
        {
            Id = "flashlight", Name = "Flashlight",
            Description = "A battery-powered torch. Still has charge. Light is hope.",
            IsUsable = true, UseEffect = "You click it on. A bright beam cuts the darkness. Your heart rate drops.",
            MoodDelta = 14f, StressDelta = -12f,
            IsDeconstructable = false
        },
        ["candle"] = new ItemDefinition
        {
            Id = "candle", Name = "Candle",
            Description = "A thick pillar candle. Burns for hours and makes a room feel almost human again.",
            IsUsable = true, UseEffect = "You light the candle. The small flame throws warm shadows. It's something.",
            MoodDelta = 12f, StressDelta = -10f,
            IsDeconstructable = false
        },
        ["lighter"] = new ItemDefinition
        {
            Id = "lighter", Name = "Lighter",
            Description = "A butane lighter. About 100 strikes left before it runs dry.",
            IsUsable = true, UseEffect = "You flick the lighter on. The flame makes the darkness feel less total.",
            MoodDelta = 7f, StressDelta = -6f,
            MaxUses = 100,
            IsDeconstructable = false
        },
        ["matches"] = new ItemDefinition
        {
            Id = "matches", Name = "Box of Matches",
            Description = "A box of safety matches — about 20 left.",
            IsUsable = true, UseEffect = "You strike a match. It hisses and flares. Brief, but warm.",
            MoodDelta = 6f, StressDelta = -5f,
            MaxUses = 20,
            IsDeconstructable = false
        },

        // ── Tools & hardware ──────────────────────────────────────────────────
        ["hammer"] = new ItemDefinition
        {
            Id = "hammer", Name = "Claw Hammer",
            Description = "A steel-headed claw hammer from a toolbox. Reassuringly heavy. +15 attack damage.",
            IsUsable = true, UseEffect = "You grip the handle. Something about holding a real tool calms you down.",
            MoodDelta = 6f, StressDelta = -5f,
            AttackBonus = 15f,
            IsDeconstructable = true, DeconstructChance = 0.7f,
            DeconstructYields = ["scrap_metal"]
        },
        ["scissors"] = new ItemDefinition
        {
            Id = "scissors", Name = "Kitchen Scissors",
            Description = "Heavy-duty kitchen scissors. Not ideal, but sharp and accessible. +8 attack damage.",
            IsUsable = false,
            AttackBonus = 8f,
            IsDeconstructable = true, DeconstructChance = 0.8f,
            DeconstructYields = ["scrap_metal"]
        },
        ["rope"] = new ItemDefinition
        {
            Id = "rope", Name = "Length of Rope",
            Description = "About 10 metres of nylon cord from a storage closet. Countless uses in a survival situation.",
            IsUsable = true, UseEffect = "You rig the rope into a useful configuration. Having it feels like having options.",
            MoodDelta = 7f, StressDelta = -5f,
            IsDeconstructable = true, DeconstructChance = 1.0f,
            DeconstructYields = ["fabric_strips", "fabric_strips"]
        },
        ["batteries"] = new ItemDefinition
        {
            Id = "batteries", Name = "Pack of Batteries",
            Description = "A blister pack of AA batteries. Three still have charge. Extends a flashlight or radio considerably.",
            IsUsable = true, UseEffect = "You swap in fresh batteries. The light gets brighter. Small victories.",
            MoodDelta = 10f, StressDelta = -8f,
            MaxUses = 3,
            IsDeconstructable = false
        },
        ["glass_bottle"] = new ItemDefinition
        {
            Id = "glass_bottle", Name = "Glass Bottle",
            Description = "An empty wine or beer bottle. Could be smashed into something sharp.",
            IsUsable = false,
            IsDeconstructable = true, DeconstructChance = 1.0f,
            DeconstructYields = ["glass_shard"]
        },
        ["glass_shard"] = new ItemDefinition
        {
            Id = "glass_shard", Name = "Glass Shard",
            Description = "A jagged wedge of broken glass, wrapped in cloth at one end. Crude but cutting. +10 attack damage.",
            IsUsable = true, UseEffect = "You tape the cloth tighter around the base. It'll do.",
            MoodDelta = 3f, StressDelta = -3f,
            AttackBonus = 10f,
            IsDeconstructable = false
        },
        ["wire_bundle"] = new ItemDefinition
        {
            Id = "wire_bundle", Name = "Wire Bundle",
            Description = "A coil of electrical wire stripped from a wall panel. Useful for trapping small animals.",
            IsUsable = true, UseEffect = "You rig the wires together. Sparks fly — it might hold.",
            MoodDelta = 6f, StressDelta = -4f,
            IsDeconstructable = true, DeconstructChance = 0.9f,
            DeconstructYields = ["scrap_metal"]
        },
        ["crowbar"] = new ItemDefinition
        {
            Id = "crowbar", Name = "Crowbar",
            Description = "A heavy steel pry bar. Forces locked doors, breaks crates, and fends off dogs.",
            IsUsable = false,
            IsDeconstructable = true, DeconstructChance = 0.8f,
            DeconstructYields = ["scrap_metal", "scrap_metal"]
        },
        ["duct_tape"] = new ItemDefinition
        {
            Id = "duct_tape", Name = "Duct Tape",
            Description = "A full roll of heavy-duty tape. Fixes everything, at least temporarily.",
            IsUsable = true, UseEffect = "You tape it together. It won't last forever, but it'll hold for now.",
            MoodDelta = 7f, StressDelta = -5f,
            IsDeconstructable = false
        },
        ["pocket_knife"] = new ItemDefinition
        {
            Id = "pocket_knife", Name = "Pocket Knife",
            Description = "A folding knife with a 3-inch blade. Useful for a dozen small tasks.",
            IsUsable = true, UseEffect = "You use the knife for a quick task. It feels good to have a real tool.",
            MoodDelta = 6f, StressDelta = -4f,
            IsDeconstructable = false
        },
        ["battery_pack"] = new ItemDefinition
        {
            Id = "battery_pack", Name = "Battery Pack",
            Description = "A drained lithium pack from an emergency lighting unit.",
            IsUsable = false,
            IsDeconstructable = true, DeconstructChance = 0.6f,
            DeconstructYields = ["scrap_metal"]
        },
        ["scrap_metal"] = new ItemDefinition
        {
            Id = "scrap_metal", Name = "Scrap Metal",
            Description = "A jagged piece of metal salvaged from equipment or fixtures.",
            IsUsable = false,
            IsDeconstructable = false
        },

        // ── Communication ─────────────────────────────────────────────────────
        ["ham_radio"] = new ItemDefinition
        {
            Id = "ham_radio", Name = "Ham Radio",
            Description = "A battery-powered shortwave radio. Static mostly, but sometimes voices break through.",
            IsUsable = true, UseEffect = "You scan the frequencies. Fragments of other survivors' voices drift through the static. You are not alone.",
            MoodDelta = 16f, StressDelta = -12f,
            IsDeconstructable = true, DeconstructChance = 0.5f,
            DeconstructYields = ["wire_bundle", "scrap_metal"]
        },

        // ── Animal loot ───────────────────────────────────────────────────────
        ["rat_carcass"] = new ItemDefinition
        {
            Id = "rat_carcass", Name = "Rat Carcass",
            Description = "A freshly trapped rat. Unappetising but edible in a crisis.",
            IsUsable = true,
            UseEffect = "You gut and eat the rat. Every part of you resists, but hunger wins.",
            HungerRestore = 15f, MoodDelta = -5f, StressDelta = -2f,
            IsDeconstructable = true, DeconstructChance = 1.0f,
            DeconstructYields = ["fur_scraps"]
        },
        ["dog_carcass"] = new ItemDefinition
        {
            Id = "dog_carcass", Name = "Dog Carcass",
            Description = "The body of a large feral dog. More meat than you'd expect.",
            IsUsable = true,
            UseEffect = "You butcher and eat a portion of the dog. A grim meal, but substantial.",
            HungerRestore = 40f, MoodDelta = -8f, StressDelta = -3f,
            IsDeconstructable = true, DeconstructChance = 1.0f,
            DeconstructYields = ["leather_scraps", "bone_shard"]
        },
        ["fur_scraps"] = new ItemDefinition
        {
            Id = "fur_scraps", Name = "Fur Scraps",
            Description = "Strips of hide and fur. Could be used for insulation or padding.",
            IsUsable = false,
            IsDeconstructable = false
        },
        ["feather_bundle"] = new ItemDefinition
        {
            Id = "feather_bundle", Name = "Feather Bundle",
            Description = "A handful of bird feathers. Soft insulating material.",
            IsUsable = false,
            IsDeconstructable = false
        },
        ["leather_scraps"] = new ItemDefinition
        {
            Id = "leather_scraps", Name = "Leather Scraps",
            Description = "Strips of tough hide from a large dog. Durable and flexible.",
            IsUsable = false,
            IsDeconstructable = false
        },
        ["bone_shard"] = new ItemDefinition
        {
            Id = "bone_shard", Name = "Bone Shard",
            Description = "A dense fragment of bone. Sharp enough to use as a crude cutting tool.",
            IsUsable = true,
            UseEffect = "You use the bone shard as a crude blade. It holds its edge for now.",
            MoodDelta = 4f, StressDelta = -3f,
            IsDeconstructable = false
        },

        // ── Crafted items ─────────────────────────────────────────────────────
        ["improved_trap"] = new ItemDefinition
        {
            Id = "improved_trap", Name = "Improved Trap",
            Description = "A reinforced wire-and-metal snare. More reliable than a basic wire bundle. Deploy with place_trap.",
            IsUsable = false,
            IsDeconstructable = false,
            MoodDelta = 0f, StressDelta = 0f
        },
        ["cooked_rat_meat"] = new ItemDefinition
        {
            Id = "cooked_rat_meat", Name = "Cooked Rat Meat",
            Description = "A rat, gutted and cooked over a flame. Still grim, but substantially better than raw.",
            IsUsable = true,
            UseEffect = "You eat the cooked meat. Warm and filling. The worst part was the smell while it cooked.",
            HungerRestore = 25f, MoodDelta = 2f, StressDelta = -2f,
            IsDeconstructable = false
        },
        ["cooked_dog_meat"] = new ItemDefinition
        {
            Id = "cooked_dog_meat", Name = "Cooked Dog Meat",
            Description = "A portion of dog, cooked over a flame. Enough calories to last most of a day.",
            IsUsable = true,
            UseEffect = "You eat the cooked meat. It's substantial. You try not to think too hard about it.",
            HungerRestore = 55f, MoodDelta = -2f, StressDelta = -3f,
            IsDeconstructable = false
        },
        ["field_bandage"] = new ItemDefinition
        {
            Id = "field_bandage", Name = "Field Bandage",
            Description = "Strips of clean cloth bound tightly. Won't heal anything deep, but stops deterioration.",
            IsUsable = true,
            UseEffect = "You wrap the bandage tightly. The bleeding slows and the pain becomes manageable.",
            MoodDelta = 8f, StressDelta = -20f,
            IsDeconstructable = false
        },
        ["splint"] = new ItemDefinition
        {
            Id = "splint", Name = "Improvised Splint",
            Description = "A scrap metal brace wrapped in fabric. Stabilises an injured limb.",
            IsUsable = true,
            UseEffect = "You strap the splint in place. The grinding pain settles into a dull ache.",
            HungerRestore = 10f, ThirstRestore = 5f, MoodDelta = 5f, StressDelta = -12f,
            IsDeconstructable = false
        },
        ["shiv"] = new ItemDefinition
        {
            Id = "shiv", Name = "Shiv",
            Description = "A shard of scrap metal wrapped in duct tape. Crude but effective at close range. +20 attack damage.",
            IsUsable = true,
            UseEffect = "You grip the shiv. It's not much, but it's something.",
            MoodDelta = 4f, StressDelta = -3f,
            AttackBonus = 20f,
            IsDeconstructable = false
        },
        ["crude_knife"] = new ItemDefinition
        {
            Id = "crude_knife", Name = "Crude Knife",
            Description = "A bone shard bound to a fabric handle. Surprisingly sharp. +12 attack damage.",
            IsUsable = true,
            UseEffect = "You test the edge. It holds.",
            MoodDelta = 3f, StressDelta = -2f,
            AttackBonus = 12f,
            IsDeconstructable = false
        },
        ["improvised_lantern"] = new ItemDefinition
        {
            Id = "improvised_lantern", Name = "Improvised Lantern",
            Description = "A battery pack wired to an exposed coil — glows dimly but steadily. 5 uses.",
            IsUsable = true,
            UseEffect = "The lantern flickers to life. A steady glow fills the immediate darkness.",
            MoodDelta = 20f, StressDelta = -18f,
            MaxUses = 5,
            IsDeconstructable = false
        },
        ["leather_wrap"] = new ItemDefinition
        {
            Id = "leather_wrap", Name = "Leather Wrap",
            Description = "Strips of leather and fabric wound together. Rough but warm.",
            IsUsable = true,
            UseEffect = "You wrap it around your hands and shoulders. The cold retreats slightly.",
            MoodDelta = 10f, StressDelta = -10f,
            IsDeconstructable = false
        },

        // ── Liquid containers (empty) ─────────────────────────────────────────
        // Fill at any apartment tap while water pressure holds (set item_action = "fill").
        ["tin_can"] = new ItemDefinition
        {
            Id = "tin_can", Name = "Tin Can",
            Description = "An empty food tin. Small but holds water. Fill it at a tap to carry ~400ml.",
            IsUsable = false, FillResult = "filled_tin_can",
            IsDeconstructable = true, DeconstructChance = 1.0f,
            DeconstructYields = ["scrap_metal"]
        },
        ["mason_jar"] = new ItemDefinition
        {
            Id = "mason_jar", Name = "Mason Jar",
            Description = "A wide-mouth glass jar with a lid. Holds about 500ml of water cleanly.",
            IsUsable = false, FillResult = "filled_mason_jar",
            IsDeconstructable = false
        },
        ["cooking_pot"] = new ItemDefinition
        {
            Id = "cooking_pot", Name = "Cooking Pot",
            Description = "A 2-litre stockpot from someone's kitchen. Heavy but carries a lot of water — enough for several people.",
            IsUsable = false, FillResult = "filled_cooking_pot",
            IsDeconstructable = true, DeconstructChance = 0.8f,
            DeconstructYields = ["scrap_metal"]
        },
        ["water_jug"] = new ItemDefinition
        {
            Id = "water_jug", Name = "Water Jug",
            Description = "A 4-litre plastic jug. A serious water store. Fill it while the taps still work.",
            IsUsable = false, FillResult = "filled_water_jug",
            IsDeconstructable = false
        },
        ["bucket"] = new ItemDefinition
        {
            Id = "bucket", Name = "Bucket",
            Description = "A 10-litre plastic bucket. Bulky to carry, but a bucket of water is worth its weight when the taps run dry.",
            IsUsable = false, FillResult = "filled_bucket",
            IsDeconstructable = false
        },

        // ── Liquid containers (filled) ─────────────────────────────────────────
        ["filled_tin_can"] = new ItemDefinition
        {
            Id = "filled_tin_can", Name = "Tin Can (Water)",
            Description = "A tin can filled with tap water. About 400ml — one solid drink.",
            IsUsable = true, UseEffect = "You drink from the tin can. Cold and metallic, but clean.",
            ThirstRestore = 20f, MoodDelta = 6f, StressDelta = -4f,
            IsDeconstructable = false
        },
        ["filled_mason_jar"] = new ItemDefinition
        {
            Id = "filled_mason_jar", Name = "Mason Jar (Water)",
            Description = "A sealed mason jar of tap water. About 500ml.",
            IsUsable = true, UseEffect = "You unscrew the lid and drink. Clean and satisfying.",
            ThirstRestore = 28f, MoodDelta = 7f, StressDelta = -5f,
            IsDeconstructable = false
        },
        ["filled_cooking_pot"] = new ItemDefinition
        {
            Id = "filled_cooking_pot", Name = "Cooking Pot (Water)",
            Description = "A pot filled with roughly 2 litres of tap water. Heavy but invaluable — 3 good drinks.",
            IsUsable = true, UseEffect = "You cup your hands and drink from the pot. Cold and clean.",
            ThirstRestore = 28f, MoodDelta = 8f, StressDelta = -6f,
            MaxUses = 3,
            IsDeconstructable = false
        },
        ["filled_water_jug"] = new ItemDefinition
        {
            Id = "filled_water_jug", Name = "Water Jug (Filled)",
            Description = "A 4-litre jug of tap water. Five solid drinks. Carry this and thirst becomes manageable.",
            IsUsable = true, UseEffect = "You tip the jug and drink deeply. The weight of it is reassuring.",
            ThirstRestore = 30f, MoodDelta = 8f, StressDelta = -6f,
            MaxUses = 5,
            IsDeconstructable = false
        },
        ["filled_bucket"] = new ItemDefinition
        {
            Id = "filled_bucket", Name = "Bucket (Water)",
            Description = "A bucket holding about 8 litres. Eight long drinks. Share it or hoard it — your call.",
            IsUsable = true, UseEffect = "You scoop water from the bucket and drink. There's plenty left.",
            ThirstRestore = 32f, MoodDelta = 9f, StressDelta = -7f,
            MaxUses = 8,
            IsDeconstructable = false
        },

        // ── Carry containers ────────────────────────────────────────────────────
        ["plastic_bag"] = new ItemDefinition
        {
            Id = "plastic_bag", Name = "Plastic Bag", IsUsable = false,
            Description = "A sturdy shopping bag. Holds a few extra items. (+3 carry slots)",
            CarryCapacity = 3,
            MoodDelta = 2f, StressDelta = -1f,
        },
        ["satchel"] = new ItemDefinition
        {
            Id = "satchel", Name = "Satchel", IsUsable = false,
            Description = "A worn canvas shoulder bag. Decent extra storage. (+5 carry slots)",
            CarryCapacity = 5,
            MoodDelta = 4f, StressDelta = -2f,
        },
        ["backpack"] = new ItemDefinition
        {
            Id = "backpack", Name = "Backpack", IsUsable = false,
            Description = "A sturdy daypack. Significantly expands what you can carry. (+10 carry slots)",
            CarryCapacity = 10,
            MoodDelta = 6f, StressDelta = -4f,
        },
        ["duffel_bag"] = new ItemDefinition
        {
            Id = "duffel_bag", Name = "Duffel Bag", IsUsable = false,
            Description = "A large sports bag. Bulky but holds a lot. (+8 carry slots)",
            CarryCapacity = 8,
            MoodDelta = 5f, StressDelta = -3f,
        },
        ["hiking_pack"] = new ItemDefinition
        {
            Id = "hiking_pack", Name = "Hiking Pack", IsUsable = false,
            Description = "A high-capacity expedition pack. Rare find. (+15 carry slots)",
            CarryCapacity = 15,
            MoodDelta = 8f, StressDelta = -5f,
        },

        // ── Forest forage items ───────────────────────────────────────────────
        ["wild_berries"] = new ItemDefinition
        {
            Id = "wild_berries", Name = "Wild Berries",
            Description = "A handful of berries gathered from the undergrowth. Tart, but they fill you up.",
            IsUsable = true, UseEffect = "You eat the berries. Tart and filling.",
            HungerRestore = 20f, MoodDelta = 5f,
            IsDeconstructable = false
        },
        ["mushrooms"] = new ItemDefinition
        {
            Id = "mushrooms", Name = "Mushrooms",
            Description = "Forest mushrooms — probably edible. The right ones are nutritious; the wrong ones are not.",
            IsUsable = true, UseEffect = "You eat the mushrooms. Earthy and dense.",
            HungerRestore = 15f, MoodDelta = 3f,
            IsDeconstructable = false
        },
        ["venison"] = new ItemDefinition
        {
            Id = "venison", Name = "Venison",
            Description = "Raw deer meat, thick-cut. Needs cooking ideally, but hunger doesn't wait.",
            IsUsable = true, UseEffect = "You eat the raw venison. It's rough going, but your body needed it.",
            HungerRestore = 50f, MoodDelta = -5f,
            MaxUses = 3,
            IsDeconstructable = false
        },
    };

    public static ItemDefinition Get(string id) =>
        _items.TryGetValue(id, out var def) ? def : throw new KeyNotFoundException($"Unknown item: {id}");

    public static IEnumerable<string> AllIds => _items.Keys;
}
