using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Maps;

public class DefaultMapGenerator : IMapGenerator
{
    public (int x, int y) DefaultStartPosition => MapGrid.DefaultStartPosition;
    public IReadOnlyList<(int x, int y)> AgentStartPositions => MapGrid.AgentStartPositions;

    public MapGrid Generate() => MapGrid.CreateDefault();
}
