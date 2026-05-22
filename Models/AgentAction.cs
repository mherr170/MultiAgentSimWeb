using System.Text.Json.Serialization;

namespace MultiAgentSimWeb.Models;

public class AgentAction
{
    [JsonPropertyName("thought")]
    public string Thought { get; set; } = "";

    [JsonPropertyName("speech")]
    public string Speech { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("move_to")]
    public string? MoveTo { get; set; }

    // "none" | "pick_up" | "drop" | "use" | "give" | "deconstruct"
    [JsonPropertyName("item_action")]
    public string ItemAction { get; set; } = "none";

    // InstanceId (Guid string) of the item to act on
    [JsonPropertyName("item_target_id")]
    public string ItemTargetId { get; set; } = "";

    // Agent name recipient (for "give" only)
    [JsonPropertyName("item_give_to")]
    public string ItemGiveTo { get; set; } = "";

    // true to attempt scavenging (only effective inside Apartment or Storefront)
    [JsonPropertyName("scavenge")]
    public bool Scavenge { get; set; }

    // true to drink from a tap/sink/toilet in an Apartment (while water pressure holds)
    [JsonPropertyName("drink_tap")]
    public bool DrinkTap { get; set; }

    // true to drink from the Riverside Fountain (Park terrain, always available)
    [JsonPropertyName("drink_fountain")]
    public bool DrinkFountain { get; set; }

    // true to drink from the Irongate River (River terrain, always available)
    [JsonPropertyName("drink_river")]
    public bool DrinkRiver { get; set; }

    // Exact name of an agent within 1 cell to speak to directly; empty means general speech
    [JsonPropertyName("address_agent")]
    public string AddressAgent { get; set; } = "";

    // Recipe ID from the CRAFTING section of context (used when item_action = "craft")
    [JsonPropertyName("craft_recipe_id")]
    public string CraftRecipeId { get; set; } = "";

    // "up" | "down" | "" — only valid inside multi-story buildings
    [JsonPropertyName("move_floor")]
    public string MoveFloor { get; init; } = "";

    // "none" | "attack" | "trap" | "scare"
    [JsonPropertyName("animal_action")]
    public string AnimalAction { get; set; } = "none";

    // Guid string of the target animal (from ANIMALS IN YOUR CELL / ANIMALS NEARBY in context)
    [JsonPropertyName("animal_target_id")]
    public string AnimalTargetId { get; set; } = "";
}
