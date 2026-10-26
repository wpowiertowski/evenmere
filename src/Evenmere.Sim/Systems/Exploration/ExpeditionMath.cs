using Evenmere.Sim.Systems.Map;

namespace Evenmere.Sim.Systems.Exploration;

/// <summary>
/// The integer maths of expeditions (M2-06, per ADR-0003): how long a lodge takes to reveal an adjacent fogged region.
/// The formula is pure over integers, so the game UI (M2-23) can show the same ETA the simulation uses by calling
/// <see cref="DurationDays"/> with the same inputs: the Chebyshev distance in tiles, the target region's average
/// elevation, and the summed <c>expeditionSpeed</c> bonus in percent.
/// </summary>
public static class ExpeditionMath
{
    /// <summary>The days an expedition takes before distance: the lodge prepares, travels the first leagues and returns.</summary>
    public const int BaseDays = 2;

    /// <summary>How many tiles of travel one day covers, before the terrain and speed modifiers.</summary>
    public const int TilesPerDay = 10;

    /// <summary>How much rougher ground slows travel: each step of the target region's average elevation adds this per-mille.</summary>
    public const int ElevationFactorPerStep = 50;

    /// <summary>The slowest an expedition may become under a penalty: the divisor clamps here, so a large negative bonus slows to a tenth but never freezes.</summary>
    public const int MinDivisor = 100;

    /// <summary>Gets the days an expedition takes.</summary>
    /// <param name="distance">The Chebyshev distance in tiles from the lodge to the nearest tile of the target region; zero or more.</param>
    /// <param name="averageElevation">The target region's average elevation in steps (0 to 15).</param>
    /// <param name="speedBonusPercent">The summed <c>expeditionSpeed</c> modifiers in percent; positive hurries.</param>
    /// <returns>The days, at least 1.</returns>
    public static int DurationDays(int distance, int averageElevation, int speedBonusPercent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(distance);
        if (averageElevation is < 0 or > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(averageElevation), averageElevation, "Elevation is 0 to 15 steps.");
        }

        int terrainFactor = 1000 + (averageElevation * ElevationFactorPerStep);
        long travel = ((long)distance * terrainFactor) + (1000L * TilesPerDay) - 1;
        int raw = BaseDays + (int)(travel / (1000L * TilesPerDay));

        int divisor = 1000 + speedBonusPercent;
        if (divisor < MinDivisor)
        {
            divisor = MinDivisor;
        }

        long adjusted = ((long)raw * 1000) + divisor - 1;
        return Math.Max(1, (int)(adjusted / divisor));
    }

    /// <summary>Gets the Chebyshev distance from a tile to the nearest tile of a region.</summary>
    /// <param name="map">The map state.</param>
    /// <param name="x">The column to measure from.</param>
    /// <param name="y">The row to measure from.</param>
    /// <param name="region">The target region.</param>
    /// <returns>The distance, or -1 when no tile holds that region.</returns>
    internal static int DistanceToRegion(MapState map, int x, int y, ushort region)
    {
        ArgumentNullException.ThrowIfNull(map);
        int best = -1;
        for (int ty = 0; ty < map.Height; ty++)
        {
            for (int tx = 0; tx < map.Width; tx++)
            {
                if (map.Region[(ty * map.Width) + tx] != region)
                {
                    continue;
                }

                int distance = Math.Max(Math.Abs(tx - x), Math.Abs(ty - y));
                if (best < 0 || distance < best)
                {
                    best = distance;
                    if (best == 0)
                    {
                        return 0;
                    }
                }
            }
        }

        return best;
    }

    /// <summary>Gets the average elevation of a region's tiles, rounded down.</summary>
    /// <param name="map">The map state.</param>
    /// <param name="region">The region.</param>
    /// <returns>The mean elevation, or 0 when no tile holds that region.</returns>
    internal static int AverageElevationOf(MapState map, ushort region)
    {
        ArgumentNullException.ThrowIfNull(map);
        long sum = 0;
        int count = 0;
        for (int i = 0; i < map.Region.Length; i++)
        {
            if (map.Region[i] == region)
            {
                sum += map.Elevation[i];
                count++;
            }
        }

        return count == 0 ? 0 : (int)(sum / count);
    }

    /// <summary>Tests whether a region exists on the map (at least one tile holds its number).</summary>
    /// <param name="map">The map state.</param>
    /// <param name="region">The region.</param>
    /// <returns><see langword="true"/> when some tile holds the region.</returns>
    internal static bool RegionExists(MapState map, ushort region)
    {
        ArgumentNullException.ThrowIfNull(map);
        foreach (ushort candidate in map.Region)
        {
            if (candidate == region)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tests whether a region borders revealed land in four-connectivity: some tile of the region shares an edge with a tile of a revealed region.</summary>
    /// <param name="map">The map state.</param>
    /// <param name="revealed">The revealed bitset, as <see cref="ExplorationState.RevealedRegions"/> holds it.</param>
    /// <param name="region">The candidate region.</param>
    /// <returns><see langword="true"/> when the region touches revealed land.</returns>
    internal static bool BordersRevealed(MapState map, ulong[] revealed, ushort region)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(revealed);
        int width = map.Width;
        int height = map.Height;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (map.Region[(y * width) + x] != region)
                {
                    continue;
                }

                if (x > 0 && IsRevealedBit(revealed, map.Region[(y * width) + x - 1]))
                {
                    return true;
                }

                if (x < width - 1 && IsRevealedBit(revealed, map.Region[(y * width) + x + 1]))
                {
                    return true;
                }

                if (y > 0 && IsRevealedBit(revealed, map.Region[((y - 1) * width) + x]))
                {
                    return true;
                }

                if (y < height - 1 && IsRevealedBit(revealed, map.Region[((y + 1) * width) + x]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool IsRevealedBit(ulong[] revealed, ushort region)
    {
        int word = region >> 6;
        return (uint)word < (uint)revealed.Length && (revealed[word] & (1UL << (region & 63))) != 0;
    }

    internal static void SetRevealedBit(ulong[] revealed, ushort region)
    {
        int word = region >> 6;
        if ((uint)word < (uint)revealed.Length)
        {
            revealed[word] |= 1UL << (region & 63);
        }
    }

    internal static ulong[] SizedToCover(ulong[] revealed, int maxRegion)
    {
        int words = (maxRegion >> 6) + 1;
        if (words < 1)
        {
            words = 1;
        }

        if (revealed.Length >= words)
        {
            return revealed;
        }

        ulong[] grown = new ulong[words];
        Array.Copy(revealed, grown, revealed.Length);
        return grown;
    }
}
