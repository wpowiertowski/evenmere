using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Systems.Map;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.StarterMap;

/// <summary>
/// The starter island loaded through the real map loader against the real content, inside a real simulation.
/// Nothing is faked: it is the same path new-game setup will use (M1-13).
/// </summary>
internal sealed class StarterWorld
{
    private StarterWorld(Simulation sim, ContentDb content, MapDef map)
    {
        Sim = sim;
        Content = content;
        Map = map;
    }

    /// <summary>Gets the simulation holding the loaded map.</summary>
    public Simulation Sim { get; }

    /// <summary>Gets the real game content.</summary>
    public ContentDb Content { get; }

    /// <summary>Gets the loaded starter map.</summary>
    public MapDef Map { get; }

    /// <summary>Gets the read-only map queries.</summary>
    public IMapQueries Queries => Sim.Query.Map;

    /// <summary>Gets the folder of the real game content.</summary>
    public static string ContentRoot => Path.Combine(WorldBuilder.RepoRoot, "content");

    /// <summary>Loads the starter island: real content, real loader, real simulation.</summary>
    /// <returns>The world.</returns>
    public static StarterWorld Load()
    {
        ContentDb content = ContentLoader.Load([ContentRoot]).GetDbOrThrow();
        MapLoadResult result = MapLoader.Load([ContentRoot], "starter", content);
        MapDef map = result.GetMapOrThrow();
        var sim = Simulation.CreateNew(content, new NewGameOptions { Seed = 7 });
        var system = (MapSystem)sim.Pipeline.Entries.Single(e => e.Id == SystemId.Map).System;
        system.LoadMap(map);
        return new StarterWorld(sim, content, map);
    }

    /// <summary>Gets the Founders' Hall footprint width after the start rotation.</summary>
    public int HallWidth => RotatedFootprint().Width;

    /// <summary>Gets the Founders' Hall footprint height after the start rotation.</summary>
    public int HallHeight => RotatedFootprint().Height;

    /// <summary>Measures the Chebyshev distance from a tile to the hall footprint (0 inside it).</summary>
    /// <param name="x">The tile column.</param>
    /// <param name="y">The tile row.</param>
    /// <returns>The distance in tiles.</returns>
    public int DistanceToHall(int x, int y)
    {
        int dx = 0;
        if (x < Map.StartSite.X)
        {
            dx = Map.StartSite.X - x;
        }
        else if (x > Map.StartSite.X + HallWidth - 1)
        {
            dx = x - (Map.StartSite.X + HallWidth - 1);
        }

        int dy = 0;
        if (y < Map.StartSite.Y)
        {
            dy = Map.StartSite.Y - y;
        }
        else if (y > Map.StartSite.Y + HallHeight - 1)
        {
            dy = y - (Map.StartSite.Y + HallHeight - 1);
        }

        return Math.Max(dx, dy);
    }

    /// <summary>
    /// Finds non-overlapping square placements whose every tile is within 24 tiles of the hall and passes
    /// a check, scanning top to bottom, left to right.
    /// </summary>
    /// <param name="size">The side length in tiles.</param>
    /// <param name="fits">Whether the tile at a column and row may be used.</param>
    /// <returns>The top-left corners of the placements, in scan order.</returns>
    public List<(int X, int Y)> GreedyPlacements(int size, Func<int, int, bool> fits)
    {
        var found = new List<(int X, int Y)>();
        bool[,] used = new bool[Map.Width, Map.Height];
        for (int by = 0; by + size <= Map.Height; by++)
        {
            for (int bx = 0; bx + size <= Map.Width; bx++)
            {
                bool ok = true;
                for (int dy = 0; dy < size && ok; dy++)
                {
                    for (int dx = 0; dx < size; dx++)
                    {
                        int x = bx + dx;
                        int y = by + dy;
                        if (used[x, y] || DistanceToHall(x, y) > 24 || !fits(x, y))
                        {
                            ok = false;
                            break;
                        }
                    }
                }

                if (ok)
                {
                    found.Add((bx, by));
                    for (int dy = 0; dy < size; dy++)
                    {
                        for (int dx = 0; dx < size; dx++)
                        {
                            used[bx + dx, by + dy] = true;
                        }
                    }
                }
            }
        }

        return found;
    }

    private (int Width, int Height) RotatedFootprint()
    {
        Footprint footprint = Content.Get(Content.Required.FoundersHall).Footprint;
        return (Map.StartSite.Rotation & 1) == 1
            ? (footprint.Height, footprint.Width)
            : (footprint.Width, footprint.Height);
    }
}
