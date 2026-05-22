using MultiAgentSimWeb.Models;

namespace MultiAgentSimWeb.Services.Maps;

public interface IMapGenerator
{
    MapGrid Generate();
    (int x, int y) DefaultStartPosition { get; }
    IReadOnlyList<(int x, int y)> AgentStartPositions { get; }
}
