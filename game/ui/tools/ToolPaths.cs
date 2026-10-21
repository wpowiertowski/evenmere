using Evenmere.Sim.Queries.Types;

namespace Evenmere.Game.Ui.Tools;

/// <summary>
/// The pure geometry behind the road and demolish tools: L-shaped drag paths and normalised selection rectangles.
/// It has no Godot types, so the shapes stay deterministic and unit-testable on their own.
/// </summary>
public static class ToolPaths
{
    /// <summary>
    /// Builds the L-shaped road preview from the press tile to the current tile: horizontal first, then vertical,
    /// or vertical first when the elbow is flipped (<c>tool_rotate</c>). The corner tile belongs to both arms but is
    /// listed once.
    /// </summary>
    /// <param name="anchor">The tile the press started on.</param>
    /// <param name="current">The tile the pointer is on now.</param>
    /// <param name="elbowFlipped">Whether the corner runs vertical-first instead of horizontal-first.</param>
    /// <param name="destination">Receives the path tiles in order; it must hold at least the Manhattan distance plus one.</param>
    /// <returns>The number of tiles written; always at least one.</returns>
    public static int BuildLPath(TileCoord anchor, TileCoord current, bool elbowFlipped, Span<TileCoord> destination)
    {
        // The press tile is always laid: a click without a drag places one tile.
        destination[0] = anchor;
        int count = 1;
        if (!elbowFlipped)
        {
            count = WriteArm(anchor.X, current.X, anchor.Y, static (v, fixedCoord) => new TileCoord(v, fixedCoord), destination, count);
            count = WriteArm(anchor.Y, current.Y, current.X, static (v, fixedCoord) => new TileCoord(fixedCoord, v), destination, count);
        }
        else
        {
            count = WriteArm(anchor.Y, current.Y, anchor.X, static (v, fixedCoord) => new TileCoord(fixedCoord, v), destination, count);
            count = WriteArm(anchor.X, current.X, current.Y, static (v, fixedCoord) => new TileCoord(v, fixedCoord), destination, count);
        }

        return count;
    }

    // Writes one arm of the L: the tiles from `from` to `to` along the moving axis at the fixed coordinate, skipping
    // the arm's first tile (the previous arm already listed it, or it is the anchor). A zero-length arm writes nothing.
    private static int WriteArm(int from, int to, int fixedCoord, Func<int, int, TileCoord> at, Span<TileCoord> destination, int count)
    {
        int step = Math.Sign(to - from);
        for (int v = from + step; step != 0 && (step > 0 ? v <= to : v >= to); v += step)
        {
            destination[count++] = at(v, fixedCoord);
        }

        return count;
    }

    /// <summary>
    /// Normalises a drag rectangle so the minimum corner comes first, for the demolish selection.
    /// </summary>
    /// <param name="first">One corner of the drag, where the press started.</param>
    /// <param name="second">The opposite corner, where the pointer is now.</param>
    /// <param name="min">The top-left corner, inclusive.</param>
    /// <param name="max">The bottom-right corner, inclusive.</param>
    public static void NormalizeRect(TileCoord first, TileCoord second, out TileCoord min, out TileCoord max) =>
        (min, max) = (new TileCoord(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y)), new TileCoord(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y)));

    /// <summary>
    /// Tests whether a building footprint overlaps a normalised selection rectangle.
    /// </summary>
    /// <param name="origin">The footprint's top-left tile.</param>
    /// <param name="width">The footprint width in tiles.</param>
    /// <param name="height">The footprint height in tiles.</param>
    /// <param name="min">The selection's top-left tile, inclusive.</param>
    /// <param name="max">The selection's bottom-right tile, inclusive.</param>
    /// <returns><see langword="true"/> when any footprint tile lies inside the selection.</returns>
    public static bool FootprintOverlaps(TileCoord origin, int width, int height, TileCoord min, TileCoord max) =>
        origin.X <= max.X && min.X <= origin.X + width - 1
        && origin.Y <= max.Y && min.Y <= origin.Y + height - 1;
}
