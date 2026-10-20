using MessagePack;

namespace Evenmere.Sim.Queries.Types;

/// <summary>
/// The position of one tile on the island grid. X grows to the right and Y grows downwards, so the tile at the top-left
/// corner of the map is (0, 0). A flattened per-tile array is indexed by <c>Y × width + X</c> (see <see cref="ToIndex"/>).
/// The value carries no map size, so it can describe a position that is off the map; <see cref="IsInside"/> tests that.
/// </summary>
/// <param name="X">The column, counted from the left edge; 0 is the first column.</param>
/// <param name="Y">The row, counted from the top edge; 0 is the first row.</param>
[MessagePackObject]
public readonly record struct TileCoord([property: Key(0)] int X, [property: Key(1)] int Y)
{
    /// <summary>Returns the tile a number of columns and rows away.</summary>
    /// <param name="dx">The columns to move right; negative moves left.</param>
    /// <param name="dy">The rows to move down; negative moves up.</param>
    /// <returns>The moved coordinate, which may be off the map.</returns>
    public TileCoord Offset(int dx, int dy) => new(X + dx, Y + dy);

    /// <summary>Tests whether the tile lies on a map of the given size.</summary>
    /// <param name="width">The map width in tiles.</param>
    /// <param name="height">The map height in tiles.</param>
    /// <returns><see langword="true"/> if 0 ≤ X &lt; <paramref name="width"/> and 0 ≤ Y &lt; <paramref name="height"/>.</returns>
    public bool IsInside(int width, int height) => (uint)X < (uint)width && (uint)Y < (uint)height;

    /// <summary>Gets the index of the tile in a flattened per-tile array, rows top to bottom.</summary>
    /// <param name="width">The map width in tiles.</param>
    /// <returns><c>Y × width + X</c>; meaningful only when <see cref="IsInside"/> holds.</returns>
    public int ToIndex(int width) => (Y * width) + X;

    /// <summary>Gets the tile at an index of a flattened per-tile array.</summary>
    /// <param name="index">The index, <c>Y × width + X</c>.</param>
    /// <param name="width">The map width in tiles; at least 1.</param>
    /// <returns>The coordinate.</returns>
    public static TileCoord FromIndex(int index, int width) => new(index % width, index / width);

    /// <inheritdoc />
    public override string ToString() => $"({X}, {Y})";
}
