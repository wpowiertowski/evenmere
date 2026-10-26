using Evenmere.Content.Maps;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Map;

/// <summary>Copies a loaded <see cref="MapDef"/> into <see cref="MapState"/> and derives the slope and coast (docs/design/map-format.md, "Derived data").</summary>
internal static class MapStateLoader
{
    /// <summary>Fills the state's tile layers from a map and clears the occupancy grid. The state must hold no buildings.</summary>
    /// <param name="state">The state to fill.</param>
    /// <param name="map">The loaded map.</param>
    public static void Populate(MapState state, MapDef map)
    {
        int width = map.Width;
        int height = map.Height;
        int tiles = map.TileCount;

        byte[] terrain = new byte[tiles];
        byte[] features = new byte[tiles];
        for (int i = 0; i < tiles; i++)
        {
            ushort id = map.Terrain[i].Value;
            terrain[i] = (byte)id;

            // The stored feature bits have the same values as TileFeatures (a test pins that); Water is not stored.
            features[i] = (byte)(map.TerrainFeatures[id] & MapTerrainRules.StoredFeatureMask);
        }

        byte[] slope = new byte[tiles];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                slope[i] = (byte)SlopeOf(map, x, y);
                if (IsCoast(map, x, y))
                {
                    features[i] |= (byte)TileFeatures.Coast;
                }
            }
        }

        state.Width = width;
        state.Height = height;
        state.Terrain = terrain;
        state.Elevation = [.. map.Elevation];
        state.Features = features;
        state.RiverWidth = [.. map.RiverWidth];
        state.Region = [.. map.Region];
        state.Deposit = [.. map.Deposit];
        state.Fertility = [.. map.Fertility];
        state.Slope = slope;
        state.Occupancy = new int[tiles];
        state.SubSeed = map.SubSeed;
        var discoveries = new StoredDiscovery[map.Discoveries.Length];
        for (int i = 0; i < discoveries.Length; i++)
        {
            MapDiscovery source = map.Discoveries[i];
            discoveries[i] = new StoredDiscovery(source.Region, source.Kind, source.X, source.Y, source.NameSeed);
        }

        state.Discoveries = discoveries;
    }

    // The largest elevation difference to a neighbour that exists, as a class.
    private static SlopeClass SlopeOf(MapDef map, int x, int y)
    {
        int here = map.Elevation[(y * map.Width) + x];
        int steepest = 0;
        if (x > 0)
        {
            steepest = Math.Max(steepest, Math.Abs(here - map.Elevation[(y * map.Width) + x - 1]));
        }

        if (x < map.Width - 1)
        {
            steepest = Math.Max(steepest, Math.Abs(here - map.Elevation[(y * map.Width) + x + 1]));
        }

        if (y > 0)
        {
            steepest = Math.Max(steepest, Math.Abs(here - map.Elevation[((y - 1) * map.Width) + x]));
        }

        if (y < map.Height - 1)
        {
            steepest = Math.Max(steepest, Math.Abs(here - map.Elevation[((y + 1) * map.Width) + x]));
        }

        return steepest >= (int)SlopeClass.Steep ? SlopeClass.Steep : (SlopeClass)steepest;
    }

    // A tile that is neither open water nor river, with open water among its four neighbours.
    private static bool IsCoast(MapDef map, int x, int y)
    {
        if ((FeaturesAt(map, x, y) & (MapTerrainFeatures.Water | MapTerrainFeatures.River)) != 0)
        {
            return false;
        }

        return IsWater(map, x - 1, y) || IsWater(map, x + 1, y) || IsWater(map, x, y - 1) || IsWater(map, x, y + 1);
    }

    private static bool IsWater(MapDef map, int x, int y) =>
        (uint)x < (uint)map.Width && (uint)y < (uint)map.Height && (FeaturesAt(map, x, y) & MapTerrainFeatures.Water) != 0;

    private static MapTerrainFeatures FeaturesAt(MapDef map, int x, int y) => map.TerrainFeatures[map.Terrain[(y * map.Width) + x].Value];
}
