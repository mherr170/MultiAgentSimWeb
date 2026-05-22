namespace MultiAgentSimWeb.Models;

public class MapGrid
{
    public int Width  { get; }
    public int Height { get; }

    private readonly GridCell[,] _cells;

    // Center of the central apartment block
    public static (int x, int y) DefaultStartPosition => (13, 13);

    // One start position per building block, spread across the city.
    // Ordered to give each agent a distinct neighbourhood.
    public static IReadOnlyList<(int x, int y)> AgentStartPositions { get; } =
    [
        (13,  4),   // Rosewood Apartments (6F)
        ( 4, 13),   // Calloway Flats (4F)
        (13, 13),   // The Meridian (8F)
        (22, 22),   // Hargrove Towers (5F)
    ];

    public MapGrid(int width = 40, int height = 40)
    {
        Width  = width;
        Height = height;
        _cells = new GridCell[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                _cells[x, y] = new GridCell();
    }

    public bool IsInBounds(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height;

    public GridCell GetCell(int x, int y) => _cells[x, y];

    // City layout — streets run at x = 0,9,18,27,36 and y = 0,9,18,27,36.
    // All cells default to Street; sixteen building blocks fill the 4×4 grid.
    //
    //  x:  1-8       10-17      19-26       28-35
    //  y 1-8   [ Park   ] [Apartment] [Storefront] [Hospital  ]
    //  y10-17  [Apartment][Apartment*][Storefront ] [Apartment ]   * agents start at (13,13)
    //  y19-26  [Industrial][Storefront][Apartment ] [River     ]
    //  y28-35  [Forest   ] [Forest   ] [River     ] [River     ]
    public static MapGrid CreateDefault()
    {
        var grid = new MapGrid(40, 40);

        // ── Row 1 (y 1-8) ─────────────────────────────────────────────────────
        Paint(grid,  1,  8,  1,  8, TerrainType.Park);        Name(grid,  1,  8,  1,  8, "Riverside Park");
        Paint(grid, 10, 17,  1,  8, TerrainType.Apartment);   Name(grid, 10, 17,  1,  8, "Rosewood Apartments");  Floors(grid, 10, 17,  1,  8, 6);
        Paint(grid, 19, 26,  1,  8, TerrainType.Storefront);  Name(grid, 19, 26,  1,  8, "Pak's Grocery");
        Paint(grid, 28, 35,  1,  8, TerrainType.Storefront);  Name(grid, 28, 35,  1,  8, "General Hospital");     Floors(grid, 28, 35,  1,  8, 3);

        // ── Row 2 (y 10-17) ───────────────────────────────────────────────────
        Paint(grid,  1,  8, 10, 17, TerrainType.Apartment);   Name(grid,  1,  8, 10, 17, "Calloway Flats");       Floors(grid,  1,  8, 10, 17, 4);
        Paint(grid, 10, 17, 10, 17, TerrainType.Apartment);   Name(grid, 10, 17, 10, 17, "The Meridian");         Floors(grid, 10, 17, 10, 17, 8);   // start block
        Paint(grid, 19, 26, 10, 17, TerrainType.Storefront);  Name(grid, 19, 26, 10, 17, "Neon Diner");           Floors(grid, 19, 26, 10, 17, 2);
        Paint(grid, 28, 35, 10, 17, TerrainType.Apartment);   Name(grid, 28, 35, 10, 17, "Eastside Flats");       Floors(grid, 28, 35, 10, 17, 4);

        // ── Row 3 (y 19-26) ───────────────────────────────────────────────────
        Paint(grid,  1,  8, 19, 26, TerrainType.Industrial);  Name(grid,  1,  8, 19, 26, "Hendricks Warehouse");  Floors(grid,  1,  8, 19, 26, 3);
        Paint(grid, 10, 17, 19, 26, TerrainType.Storefront);  Name(grid, 10, 17, 19, 26, "Elm Street Pharmacy");
        Paint(grid, 19, 26, 19, 26, TerrainType.Apartment);   Name(grid, 19, 26, 19, 26, "Hargrove Towers");      Floors(grid, 19, 26, 19, 26, 5);
        Paint(grid, 28, 35, 19, 26, TerrainType.River);       Name(grid, 28, 35, 19, 26, "Irongate River");

        // ── Row 4 (y 28-35) ───────────────────────────────────────────────────
        Paint(grid,  1,  8, 28, 35, TerrainType.Forest);      Name(grid,  1,  8, 28, 35, "Greenwood Forest");
        Paint(grid, 10, 17, 28, 35, TerrainType.Forest);      Name(grid, 10, 17, 28, 35, "Birchwood Forest");
        Paint(grid, 19, 26, 28, 35, TerrainType.River);       Name(grid, 19, 26, 28, 35, "River Bend");
        Paint(grid, 28, 35, 28, 35, TerrainType.River);       Name(grid, 28, 35, 28, 35, "The Delta");

        // ── Sub-zone landmarks ────────────────────────────────────────────────
        // These overwrite a portion of an existing block's name to create
        // distinct named areas with their own loot and context.

        // Riverside Park: central fountain plaza
        Name(grid,  3,  6,  3,  6, "Riverside Fountain");

        // Pak's Grocery: back stockroom (heavier loot)
        Name(grid, 19, 26,  6,  8, "Pak's Stockroom");

        // Neon Diner: commercial kitchen behind the counter
        Name(grid, 19, 26, 15, 17, "Neon Diner Kitchen");

        // Elm Street Pharmacy: dispensary / prescription back-room
        Name(grid, 10, 17, 23, 26, "Elm St. Dispensary");

        // Hendricks Warehouse: split lower half into loading dock + cold storage
        Name(grid,  1,  4, 23, 26, "Hendricks Loading Dock");
        Name(grid,  5,  8, 23, 26, "Hendricks Cold Storage");

        // The Meridian: ground-floor lobby (stripped but sheltered)
        Name(grid, 10, 17, 15, 17, "The Meridian Lobby");

        return grid;
    }

    private static void Paint(MapGrid g, int x0, int x1, int y0, int y1, TerrainType t)
    {
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (g.IsInBounds(x, y))
                    g._cells[x, y].Terrain = t;
    }

    private static void Name(MapGrid g, int x0, int x1, int y0, int y1, string name)
    {
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (g.IsInBounds(x, y))
                    g._cells[x, y].BuildingName = name;
    }

    private static void Floors(MapGrid g, int x0, int x1, int y0, int y1, int floors)
    {
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (g.IsInBounds(x, y))
                    g._cells[x, y].Floors = floors;
    }
}
