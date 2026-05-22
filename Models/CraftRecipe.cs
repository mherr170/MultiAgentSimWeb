namespace MultiAgentSimWeb.Models;

public record CraftRecipe(
    string   Id,
    string   Name,
    string[] Ingredients,   // definition IDs; duplicates allowed (e.g. two scrap_metal)
    string   ResultId,
    int      ResultCount,
    string   Description    // one-liner shown in the CRAFTING context section
);
