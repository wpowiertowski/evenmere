namespace Evenmere.Sim.Queries.Types;

/// <summary>
/// The position of one 32 × 32 tile chunk. The view builds one mesh per chunk and rebuilds it only when the simulation
/// reports the chunk dirty (<c>IMapQueries.DrainDirtyChunks</c>).
/// </summary>
/// <param name="X">The chunk column; chunk 0 covers tile columns 0 to 31.</param>
/// <param name="Y">The chunk row; chunk 0 covers tile rows 0 to 31.</param>
public readonly record struct ChunkCoord(int X, int Y)
{
    /// <summary>The width and height of a chunk in tiles.</summary>
    public const int TilesPerSide = 32;

    /// <summary>Gets the chunk that holds a tile.</summary>
    /// <param name="tileX">The tile column; not negative.</param>
    /// <param name="tileY">The tile row; not negative.</param>
    /// <returns>The chunk coordinate.</returns>
    public static ChunkCoord Of(int tileX, int tileY) => new(tileX / TilesPerSide, tileY / TilesPerSide);

    /// <summary>Gets the chunk that holds a tile.</summary>
    /// <param name="tile">The tile; not negative.</param>
    /// <returns>The chunk coordinate.</returns>
    public static ChunkCoord Of(TileCoord tile) => Of(tile.X, tile.Y);

    /// <inheritdoc />
    public override string ToString() => $"Chunk({X}, {Y})";
}
