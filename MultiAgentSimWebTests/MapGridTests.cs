using MultiAgentSimWeb.Models;

namespace MultiAgentSimWebTests;

public class MapGridTests
{
    [Fact]
    public void DefaultGrid_Is40x40()
    {
        var grid = MapGrid.CreateDefault();
        Assert.Equal(40, grid.Width);
        Assert.Equal(40, grid.Height);
    }

    [Theory]
    [InlineData(0,  0)] [InlineData(9,  0)] [InlineData(18, 0)] [InlineData(27, 0)]
    [InlineData(0,  9)] [InlineData(9,  9)] [InlineData(18, 9)] [InlineData(27, 9)]
    public void CreateDefault_StreetColumns_AreStreet(int x, int y)
    {
        var grid = MapGrid.CreateDefault();
        Assert.Equal(TerrainType.Street, grid.GetCell(x, y).Terrain);
    }

    [Theory]
    [InlineData(1, 1)] [InlineData(5, 5)]   // NW block — Park
    public void CreateDefault_NorthwestBlock_IsPark(int x, int y)
    {
        var grid = MapGrid.CreateDefault();
        Assert.Equal(TerrainType.Park, grid.GetCell(x, y).Terrain);
    }

    [Theory]
    [InlineData(10, 1)] [InlineData(14, 5)]  // N block — Apartment
    public void CreateDefault_NorthBlock_IsApartment(int x, int y)
    {
        var grid = MapGrid.CreateDefault();
        Assert.Equal(TerrainType.Apartment, grid.GetCell(x, y).Terrain);
    }

    [Theory]
    [InlineData(19, 1)] [InlineData(23, 5)]  // NE block — Storefront
    public void CreateDefault_NortheastBlock_IsStorefront(int x, int y)
    {
        var grid = MapGrid.CreateDefault();
        Assert.Equal(TerrainType.Storefront, grid.GetCell(x, y).Terrain);
    }

    [Theory]
    [InlineData(13, 13)] [InlineData(11, 11)] [InlineData(15, 16)]  // CENTER block — Apartment (start area)
    public void CreateDefault_CenterBlock_IsApartment(int x, int y)
    {
        var grid = MapGrid.CreateDefault();
        Assert.Equal(TerrainType.Apartment, grid.GetCell(x, y).Terrain);
    }

    [Fact]
    public void DefaultStartPosition_IsApartment()
    {
        var grid = MapGrid.CreateDefault();
        var (sx, sy) = MapGrid.DefaultStartPosition;
        Assert.Equal(TerrainType.Apartment, grid.GetCell(sx, sy).Terrain);
    }

    [Fact]
    public void DefaultStartPosition_Is13_13()
    {
        Assert.Equal((13, 13), MapGrid.DefaultStartPosition);
    }

    [Theory]
    [InlineData(0,  0,  true)]
    [InlineData(39, 39, true)]
    [InlineData(15, 15, true)]
    [InlineData(30,  0, true)]
    [InlineData(0,  30, true)]
    [InlineData(-1, 0,  false)]
    [InlineData(0,  -1, false)]
    [InlineData(40, 0,  false)]
    [InlineData(0,  40, false)]
    public void IsInBounds_CorrectForBoundaryValues(int x, int y, bool expected)
    {
        var grid = new MapGrid();
        Assert.Equal(expected, grid.IsInBounds(x, y));
    }
}
