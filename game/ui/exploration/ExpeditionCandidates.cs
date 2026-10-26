namespace Evenmere.Game.Ui.Exploration;

/// <summary>
/// One fogged neighbour an expedition can target: the region plus the inputs the ETA needs. The panel turns
/// these into day counts with <c>ExpeditionMath.DurationDays</c>; the helper stays pure over tile data so the
/// selection rule is unit-tested without a simulation.
/// </summary>
/// <param name="Region">The fogged region number.</param>
/// <param name="Nearest">The Chebyshev distance from the lodge to the region's nearest tile.</param>
/// <param name="AverageElevation">The region's mean elevation in steps, rounded down.</param>
internal sealed record ExpeditionTarget(ushort Region, int Nearest, int AverageElevation);

/// <summary>
/// The picker rule (M2-23, round 2): the fogged regions that border revealed land in four-connectivity, in
/// ascending region order, each with the distance and terrain inputs the ETA needs. Pure over tile reads.
/// </summary>
internal static class ExpeditionCandidates
{
    /// <summary>
    /// Finds the expedition targets for a lodge.
    /// </summary>
    /// <param name="width">The map width in tiles.</param>
    /// <param name="height">The map height in tiles.</param>
    /// <param name="regionAt">The region number of tile (x, y).</param>
    /// <param name="elevationAt">The elevation steps of tile (x, y).</param>
    /// <param name="revealed">The revealed region numbers.</param>
    /// <param name="lodgeX">The lodge's column.</param>
    /// <param name="lodgeY">The lodge's row.</param>
    /// <returns>The candidates in ascending region order.</returns>
    internal static List<ExpeditionTarget> Find(
        int width,
        int height,
        Func<int, int, ushort> regionAt,
        Func<int, int, int> elevationAt,
        ISet<ushort> revealed,
        int lodgeX,
        int lodgeY)
    {
        ArgumentNullException.ThrowIfNull(regionAt);
        ArgumentNullException.ThrowIfNull(elevationAt);
        ArgumentNullException.ThrowIfNull(revealed);

        var regions = new Dictionary<ushort, Acc>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                ushort region = regionAt(x, y);
                if (!regions.TryGetValue(region, out Acc? acc))
                {
                    acc = new Acc();
                    regions[region] = acc;
                }

                acc.Count++;
                acc.ElevationSum += elevationAt(x, y);
                int distance = Math.Max(Math.Abs(x - lodgeX), Math.Abs(y - lodgeY));
                if (distance < acc.Nearest)
                {
                    acc.Nearest = distance;
                }
            }
        }

        // Four-connectivity, like the sim's BordersRevealed: a region qualifies when one of its tiles
        // shares an edge with a tile of a revealed region.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                ushort region = regionAt(x, y);
                if (revealed.Contains(region) || !regions.TryGetValue(region, out Acc? acc) || acc.Borders)
                {
                    continue;
                }

                if ((x > 0 && revealed.Contains(regionAt(x - 1, y)))
                    || (x < width - 1 && revealed.Contains(regionAt(x + 1, y)))
                    || (y > 0 && revealed.Contains(regionAt(x, y - 1)))
                    || (y < height - 1 && revealed.Contains(regionAt(x, y + 1))))
                {
                    acc.Borders = true;
                }
            }
        }

        var candidates = new List<ExpeditionTarget>();
        foreach ((ushort region, Acc acc) in regions)
        {
            if (revealed.Contains(region) || !acc.Borders || acc.Count == 0)
            {
                continue;
            }

            candidates.Add(new ExpeditionTarget(region, acc.Nearest, (int)(acc.ElevationSum / acc.Count)));
        }

        candidates.Sort(static (a, b) => a.Region.CompareTo(b.Region));
        return candidates;
    }

    private sealed class Acc
    {
        public int Count;
        public long ElevationSum;
        public int Nearest = int.MaxValue;
        public bool Borders;
    }
}
