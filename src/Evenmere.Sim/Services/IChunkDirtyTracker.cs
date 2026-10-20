namespace Evenmere.Sim.Services;

/// <summary>
/// Marks map chunks dirty so the view rebuilds them. The map service (M1-01) owns the tracker and marks terrain and
/// building changes itself; the roads service calls this for road changes, which is the "Roads call into Map" of M1-01.
/// The host drains the marks through <c>IMapQueries.DrainDirtyChunks</c>. Marks are not world state.
/// </summary>
internal interface IChunkDirtyTracker
{
    /// <summary>Marks the chunk that holds a tile.</summary>
    /// <param name="x">The tile column.</param>
    /// <param name="y">The tile row.</param>
    void MarkTile(int x, int y);

    /// <summary>Marks every chunk that a rectangle of tiles touches.</summary>
    /// <param name="minX">The first column, inclusive.</param>
    /// <param name="minY">The first row, inclusive.</param>
    /// <param name="maxX">The last column, inclusive.</param>
    /// <param name="maxY">The last row, inclusive.</param>
    void MarkRect(int minX, int minY, int maxX, int maxY);
}
