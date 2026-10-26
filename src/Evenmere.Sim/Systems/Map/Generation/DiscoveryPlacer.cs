using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Core.Rng;

namespace Evenmere.Sim.Systems.Map.Generation;

/// <summary>
/// Places discoveries on a generated island (all integers, per ADR-0003): natural wonders on the high peaks, ruins on
/// buildable land, and shipwrecks on shore sea tiles. Counts are drawn from a <see cref="Pcg32"/> held for this pass,
/// and every placement keeps its distance from the discoveries already placed, so wonders never crowd each other. The
/// same seed always places the same discoveries: candidates run in ascending tile order and picks come from the one
/// generator. Regions are already built; every discovery records the region of its tile.
/// </summary>
internal static class DiscoveryPlacer
{
    /// <summary>The fewest natural wonders on an island.</summary>
    public const int MinWonders = 3;

    /// <summary>The most natural wonders on an island.</summary>
    public const int MaxWonders = 6;

    /// <summary>The fewest ruins on an island.</summary>
    public const int MinRuins = 4;

    /// <summary>The most ruins on an island.</summary>
    public const int MaxRuins = 8;

    /// <summary>The fewest shipwrecks on an island.</summary>
    public const int MinWrecks = 3;

    /// <summary>The most shipwrecks on an island.</summary>
    public const int MaxWrecks = 6;

    /// <summary>The lowest elevation that can hold a natural wonder: highlands and peaks.</summary>
    public const int WonderElevation = 11;

    /// <summary>How far apart two discoveries stay in Chebyshev tiles while picks succeed.</summary>
    public const int MinSpacing = 10;

    /// <summary>How many spaced picks are tried before a placement accepts the last candidate.</summary>
    public const int MaxPickTries = 60;

    /// <summary>Stream separation for the discovery dice: it must differ from every other generator stream.</summary>
    public const int DiscoveryStream = unchecked((int)0x7A4C2E91);

    // Neighbour offsets in a fixed order (N, NE, E, SE, S, SW, W, NW), so shore walks are identical everywhere.
    private static readonly int[] _dx = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] _dy = [-1, -1, 0, 1, 1, 1, 0, -1];

    /// <summary>
    /// Places discoveries on a heightmap whose regions are built: wonders, then ruins, then wrecks. The map itself is
    /// not changed; the discoveries come back sorted by region then kind, ready for <c>MapDef.Discoveries</c>.
    /// </summary>
    /// <param name="map">The heightmap with regions; terrain, elevation and river widths are read.</param>
    /// <param name="seed">The generator seed.</param>
    /// <param name="content">The loaded content, which must define the sea and reef terrains.</param>
    /// <returns>The placed discoveries, sorted by region then kind.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">The content does not define a terrain this placer needs.</exception>
    internal static MapDiscovery[] Place(GeneratedHeightmap map, int seed, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(content);

        TerrainId sea = content.IdOf<TerrainId>("sea");
        TerrainId reef = content.IdOf<TerrainId>("reef");

        var rng = new Pcg32(unchecked((ulong)(uint)seed), unchecked((ulong)(uint)DiscoveryStream));
        int[] wonderTiles = CollectWonders(map);
        int[] ruinTiles = CollectRuins(map, content);
        int[] wreckTiles = CollectWrecks(map, sea, reef);
        int wonders = Math.Min(MinWonders + rng.NextInt((MaxWonders - MinWonders) + 1), wonderTiles.Length);
        int ruins = Math.Min(MinRuins + rng.NextInt((MaxRuins - MinRuins) + 1), ruinTiles.Length);
        int wrecks = Math.Min(MinWrecks + rng.NextInt((MaxWrecks - MinWrecks) + 1), wreckTiles.Length);

        var placed = new MapDiscovery[wonders + ruins + wrecks];
        bool[] used = new bool[map.TileCount];
        int at = 0;
        at = PickKind(map, rng, placed, at, used, DiscoveryKind.NaturalWonder, wonderTiles, wonders);
        at = PickKind(map, rng, placed, at, used, DiscoveryKind.Ruin, ruinTiles, ruins);
        at = PickKind(map, rng, placed, at, used, DiscoveryKind.Shipwreck, wreckTiles, wrecks);
        Array.Resize(ref placed, at);

        Array.Sort(placed, static (a, b) =>
        {
            int region = a.Region.CompareTo(b.Region);
            if (region != 0)
            {
                return region;
            }

            int kind = a.Kind.CompareTo(b.Kind);
            if (kind != 0)
            {
                return kind;
            }

            int y = a.Y.CompareTo(b.Y);
            return y != 0 ? y : a.X.CompareTo(b.X);
        });
        return placed;
    }

    private static int PickKind(
        GeneratedHeightmap map,
        Pcg32 rng,
        MapDiscovery[] placed,
        int at,
        bool[] used,
        DiscoveryKind kind,
        int[] candidates,
        int count)
    {
        for (int p = 0; p < count; p++)
        {
            int tile = PickTile(map, rng, placed, at, used, candidates);
            if (tile < 0)
            {
                break;
            }

            used[tile] = true;
            int nameSeed = unchecked((int)rng.NextUInt());
            placed[at++] = new MapDiscovery(map.Region[tile], kind, tile % map.Size, tile / map.Size, nameSeed);
        }

        return at;
    }

    // Draws one tile for a discovery: up to MaxPickTries spaced candidates, then the last draw stands. Candidates run
    // in ascending order and every draw comes from the pass generator, so the picks are identical on every CPU. Used
    // tiles are skipped to the next free candidate, wrapping around, which keeps the draw uniform over the free tiles.
    private static int PickTile(
        GeneratedHeightmap map,
        Pcg32 rng,
        MapDiscovery[] placed,
        int placedCount,
        bool[] used,
        int[] candidates)
    {
        if (candidates.Length == 0)
        {
            return -1;
        }

        int fallback = -1;
        for (int t = 0; t < MaxPickTries; t++)
        {
            fallback = NextFree(candidates, used, rng.NextInt(candidates.Length));
            if (fallback < 0)
            {
                return -1;
            }

            if (IsSpaced(map, placed, placedCount, fallback))
            {
                return fallback;
            }
        }

        return fallback;
    }

    private static int NextFree(int[] candidates, bool[] used, int pick)
    {
        for (int step = 0; step < candidates.Length; step++)
        {
            int tile = candidates[(pick + step) % candidates.Length];
            if (!used[tile])
            {
                return tile;
            }
        }

        return -1;
    }

    private static bool IsSpaced(GeneratedHeightmap map, MapDiscovery[] placed, int placedCount, int tile)
    {
        int x = tile % map.Size;
        int y = tile / map.Size;
        for (int p = 0; p < placedCount; p++)
        {
            if (Math.Max(Math.Abs(x - placed[p].X), Math.Abs(y - placed[p].Y)) < MinSpacing)
            {
                return false;
            }
        }

        return true;
    }

    // The kind's candidates in ascending tile order: one linear scan per kind per island.
    private static int[] CollectWonders(GeneratedHeightmap map)
    {
        int[] found = new int[map.TileCount];
        int count = 0;
        for (int i = 0; i < map.TileCount; i++)
        {
            if (IsWonderCandidate(map, i))
            {
                found[count++] = i;
            }
        }

        Array.Resize(ref found, count);
        return found;
    }

    private static int[] CollectRuins(GeneratedHeightmap map, ContentDb content)
    {
        int[] found = new int[map.TileCount];
        int count = 0;
        for (int i = 0; i < map.TileCount; i++)
        {
            if (IsRuinCandidate(map, content, i))
            {
                found[count++] = i;
            }
        }

        Array.Resize(ref found, count);
        return found;
    }

    private static int[] CollectWrecks(GeneratedHeightmap map, TerrainId sea, TerrainId reef)
    {
        int[] found = new int[map.TileCount];
        int count = 0;
        for (int i = 0; i < map.TileCount; i++)
        {
            if (IsWreckCandidate(map, sea, reef, i))
            {
                found[count++] = i;
            }
        }

        Array.Resize(ref found, count);
        return found;
    }

    // Wonders crown the high ground: elevation at or above the highlands line, on any terrain that reaches it.
    private static bool IsWonderCandidate(GeneratedHeightmap map, int tile) =>
        map.Elevation[tile] >= WonderElevation;

    // Ruins rest on buildable land that is not a river: the highlands stay out because their access is locked in Era I.
    private static bool IsRuinCandidate(GeneratedHeightmap map, ContentDb content, int tile) =>
        map.RiverWidth[tile] == 0 && content.Get(map.Terrain[tile]).Buildable && map.Elevation[tile] > 0;

    // Shipwrecks lie on sea tiles beside the shore: open water with a land neighbour.
    private static bool IsWreckCandidate(GeneratedHeightmap map, TerrainId sea, TerrainId reef, int tile)
    {
        if (!map.Terrain[tile].Equals(sea))
        {
            return false;
        }

        int size = map.Size;
        int x = tile % size;
        int y = tile / size;
        for (int d = 0; d < 8; d++)
        {
            int nx = x + _dx[d];
            int ny = y + _dy[d];
            if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
            {
                continue;
            }

            TerrainId neighbour = map.Terrain[(ny * size) + nx];
            if (!neighbour.Equals(sea) && !neighbour.Equals(reef))
            {
                return true;
            }
        }

        return false;
    }
}
