using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Construction.Placement;

/// <summary>
/// The fog rule of the shared placement check (M2-06): a building may not stand on fogged land. The rule reads the
/// exploration queries, so the ghost preview and the command handler agree; the check itself lives in
/// <see cref="BuildingPlacement"/> alongside the other footprint rules.
/// </summary>
internal static class FogRule
{
    /// <summary>Collects the footprint tiles whose region is still fogged, in ascending (Y, X) order.</summary>
    /// <param name="map">The map queries, for the region of each tile.</param>
    /// <param name="exploration">The exploration queries, for whether each region is revealed.</param>
    /// <param name="x">The column of the footprint's top-left tile.</param>
    /// <param name="y">The row of the footprint's top-left tile.</param>
    /// <param name="width">The footprint width in tiles after rotation.</param>
    /// <param name="height">The footprint height in tiles after rotation.</param>
    /// <param name="found">Receives the fogged tiles; it is not cleared first.</param>
    public static void CollectFogged(IMapQueries map, IExplorationQueries exploration, int x, int y, int width, int height, List<TileCoord> found)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(exploration);
        ArgumentNullException.ThrowIfNull(found);

        for (int dy = 0; dy < height; dy++)
        {
            for (int dx = 0; dx < width; dx++)
            {
                var at = new TileCoord(x + dx, y + dy);
                if (!at.IsInside(map.Width, map.Height))
                {
                    continue;
                }

                ushort region = map.GetTile(at.X, at.Y).Region;
                if (!exploration.IsRevealed(region))
                {
                    found.Add(at);
                }
            }
        }
    }
}
