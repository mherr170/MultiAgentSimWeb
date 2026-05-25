namespace MultiAgentSimWeb.Models;

public enum TerrainType
{
    Street,
    Apartment,
    Storefront,
    Park,
    Industrial,
    Forest,
    River
}

public class GridCell
{
    public TerrainType Terrain { get; set; } = TerrainType.Street;
    public string? BuildingName { get; set; }
    public int Floors { get; set; } = 1;

    public string DisplayName => Terrain switch
    {
        TerrainType.Street     => "Street",
        TerrainType.Apartment  => "Apartment Building",
        TerrainType.Storefront => "Storefront",
        TerrainType.Park       => "City Park",
        TerrainType.Industrial => "Warehouse District",
        TerrainType.Forest     => "Forest",
        TerrainType.River      => "Riverbank",
        _                      => "Unknown"
    };

    /// True for terrain types where agents share a floor and have limited visibility.
    public bool IsIndoor => Terrain is TerrainType.Apartment or TerrainType.Storefront or TerrainType.Industrial;

    public string Description => Terrain switch
    {
        TerrainType.Street     => "A dark city street. No working streetlights. Quiet but exposed.",
        TerrainType.Apartment  => "A residential apartment building. Kitchens, closets, stairwells — people's homes.",
        TerrainType.Storefront => "A row of darkened shops. Grocery, pharmacy, hardware — all abandoned.",
        TerrainType.Park       => "An open city park. Grass and benches. Eerily quiet without traffic noise.",
        TerrainType.Industrial => "A warehouse and factory block. Heavy equipment, loading docks, storage racks.",
        TerrainType.Forest     => "Dense overgrown forest at the city's edge. Dark, quiet, and full of wild food.",
        TerrainType.River      => "The Irongate River. Cold and fast-moving. You can drink from it and fill containers.",
        _                      => ""
    };
}
