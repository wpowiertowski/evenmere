using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Queries;

/// <summary>
/// Read-only queries over the tile grid, terrain and the building registry. Implemented by the map service (M1-01).
/// Coordinates are tile coordinates: X grows to the right, Y grows downwards, and (0, 0) is the top-left tile. Every
/// value returned is a copy, so it stays valid after the simulation changes.
/// </summary>
public interface IMapQueries
{
    /// <summary>Gets the map width in tiles; the valid columns are 0 to <c>Width − 1</c>.</summary>
    int Width { get; }

    /// <summary>Gets the map height in tiles; the valid rows are 0 to <c>Height − 1</c>.</summary>
    int Height { get; }

    /// <summary>Describes one tile.</summary>
    /// <param name="x">The column, 0 to <c>Width − 1</c>.</param>
    /// <param name="y">The row, 0 to <c>Height − 1</c>.</param>
    /// <returns>A copy of the tile's data.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The tile is off the map.</exception>
    TileInfo GetTile(int x, int y);

    /// <summary>Finds the building whose footprint covers a tile (a site, a finished building or a ruin).</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The building's entity ID, or <see cref="EntityId.None"/> when no building covers the tile or the tile is off the map.</returns>
    EntityId BuildingAt(int x, int y);

    /// <summary>Describes one building.</summary>
    /// <param name="id">The building's entity ID.</param>
    /// <param name="building">A copy of the building's data, when it exists.</param>
    /// <returns><see langword="true"/> if a building with that ID exists.</returns>
    bool TryGetBuilding(EntityId id, out BuildingInfo building);

    /// <summary>Gets every building whatever its state (sites, finished buildings and ruins), in ascending ID order.</summary>
    BuildingSequence Buildings { get; }

    /// <summary>Gets the finished buildings, which are those in the Active or Paused state, in ascending ID order. Sites and ruins are left out.</summary>
    BuildingSequence ActiveBuildings { get; }

    /// <summary>
    /// Hands the host the chunks whose terrain, roads or buildings changed since they were last handed out, so the view
    /// can rebuild only those. This is the one query that changes anything: it forgets the chunks it returns, and only
    /// those, so a host that passes a short span gets the rest on its next call. The bookkeeping is not part of the world
    /// state and never changes the state hash.
    /// </summary>
    /// <param name="destination">Receives up to <c>destination.Length</c> dirty chunks, each at most once, in ascending (Y, X) order.</param>
    /// <returns>The number of chunks written; 0 when nothing is dirty.</returns>
    int DrainDirtyChunks(Span<ChunkCoord> destination);
}
