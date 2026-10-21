using Evenmere.Content;
using Evenmere.Game.Rendering.Terrain;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Game.Data.Visuals;

/// <summary>
/// The rendered ground heights the building and road renderers stand on. The corner rule mirrors
/// <c>TerrainRenderer</c> exactly (carved river beds, otherwise the average of the sharing base heights), so a pad or a
/// road strip computed here lands on the visible mesh. It duplicates that logic instead of sharing it because the
/// renderers own their folders (M1-20), while the terrain files stay untouched.
/// </summary>
public static class GroundSampler
{
    /// <summary>
    /// Gets the height of the tile corner shared by the tiles around vertex (vx, vy), in world units.
    /// </summary>
    /// <param name="map">The map queries.</param>
    /// <param name="content">The content, for terrain names.</param>
    /// <param name="vx">The corner column.</param>
    /// <param name="vy">The corner row.</param>
    /// <returns>The corner height; off-map neighbours are skipped like the terrain renderer skips them.</returns>
    public static float CornerHeight(IMapQueries map, ContentDb content, int vx, int vy)
    {
        float first = 0f;
        float second = 0f;
        float third = 0f;
        float fourth = 0f;
        int riverMask = 0;
        int count = 0;
        int bit = 1;
        for (int dy = -1; dy <= 0; dy++)
        {
            for (int dx = -1; dx <= 0; dx++)
            {
                int tileX = vx + dx;
                int tileY = vy + dy;
                if (tileX >= 0 && tileY >= 0 && tileX < map.Width && tileY < map.Height)
                {
                    TileInfo tile = map.GetTile(tileX, tileY);
                    string name = content.Terrains.Names[tile.Terrain.Value];
                    WaterKind kind = TerrainSurface.Classify(name, (tile.Features & TileFeatures.River) != 0);
                    float baseHeight = TerrainSurface.MeshTopHeight(tile.Elevation, kind);
                    if (bit == 1)
                    {
                        first = baseHeight;
                    }
                    else if (bit == 2)
                    {
                        second = baseHeight;
                    }
                    else if (bit == 4)
                    {
                        third = baseHeight;
                    }
                    else
                    {
                        fourth = baseHeight;
                    }

                    if (kind == WaterKind.River)
                    {
                        riverMask |= bit;
                    }

                    count++;
                }

                bit <<= 1;
            }
        }

        return TerrainSurface.ResolveCorner(first, second, third, fourth, riverMask, count);
    }

    /// <summary>
    /// Gets the highest and lowest corner heights of a footprint. A building sits on a flat pad at the highest corner
    /// so it never sinks into a slope, with a plinth down to the lowest so it never floats.
    /// </summary>
    /// <param name="map">The map queries.</param>
    /// <param name="content">The content, for terrain names.</param>
    /// <param name="originX">The footprint's smallest column.</param>
    /// <param name="originY">The footprint's smallest row.</param>
    /// <param name="width">The footprint width in tiles.</param>
    /// <param name="height">The footprint height in tiles.</param>
    /// <param name="highest">The highest corner height, in world units.</param>
    /// <param name="lowest">The lowest corner height, in world units.</param>
    public static void FootprintHeights(
        IMapQueries map,
        ContentDb content,
        int originX,
        int originY,
        int width,
        int height,
        out float highest,
        out float lowest)
    {
        highest = float.MinValue;
        lowest = float.MaxValue;
        for (int vy = originY; vy <= originY + height; vy++)
        {
            for (int vx = originX; vx <= originX + width; vx++)
            {
                float corner = CornerHeight(map, content, vx, vy);
                highest = Math.Max(highest, corner);
                lowest = Math.Min(lowest, corner);
            }
        }
    }
}
