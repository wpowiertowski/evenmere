using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Fakes;

/// <summary>
/// A settable stand-in for <see cref="IMapQueries"/>: a map of default tiles that tests overwrite, and a list of buildings
/// that tests add to directly or through <see cref="FakeBuildingRegistry"/>. Buildings are kept in ascending ID order, as
/// the real registry hands them out.
/// </summary>
internal sealed class FakeMapQueries : IMapQueries, IBuildingSource
{
    private readonly Dictionary<TileCoord, TileInfo> _tiles = [];
    private readonly List<BuildingInfo> _buildings = [];
    private readonly SortedSet<(int Y, int X)> _dirty = [];

    /// <summary>Gets or sets the map width in tiles; the default is 32.</summary>
    public int Width { get; set; } = 32;

    /// <summary>Gets or sets the map height in tiles; the default is 32.</summary>
    public int Height { get; set; } = 32;

    /// <summary>Gets or sets the tile that <see cref="GetTile"/> returns for any tile not set with <see cref="SetTile"/>.</summary>
    public TileInfo DefaultTile { get; set; }

    /// <inheritdoc />
    public BuildingSequence Buildings => new(this, BuildingFilter.Any);

    /// <inheritdoc />
    public BuildingSequence ActiveBuildings => new(this, BuildingFilter.Complete);

    /// <summary>Gets the buildings in ascending ID order, for assertions.</summary>
    public IReadOnlyList<BuildingInfo> AllBuildings => _buildings;

    /// <summary>Overwrites one tile.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="tile">The tile.</param>
    /// <returns>This fake.</returns>
    public FakeMapQueries SetTile(int x, int y, TileInfo tile)
    {
        _tiles[new TileCoord(x, y)] = tile;
        return this;
    }

    /// <summary>Adds a building, keeping the list in ID order.</summary>
    /// <param name="building">The building.</param>
    /// <returns>This fake.</returns>
    public FakeMapQueries AddBuilding(BuildingInfo building)
    {
        int index = _buildings.FindIndex(b => b.Id > building.Id);
        _buildings.Insert(index < 0 ? _buildings.Count : index, building);
        return this;
    }

    /// <summary>Adds a building with its footprint given directly.</summary>
    /// <param name="id">The entity ID.</param>
    /// <param name="type">The building type.</param>
    /// <param name="x">The column of the top-left tile.</param>
    /// <param name="y">The row of the top-left tile.</param>
    /// <param name="width">The footprint width in tiles after rotation.</param>
    /// <param name="height">The footprint height in tiles after rotation.</param>
    /// <param name="state">The state; the default is active.</param>
    /// <returns>The building as added.</returns>
    public BuildingInfo AddBuilding(
        EntityId id, BuildingTypeId type, int x, int y, int width = 1, int height = 1, BuildingState state = BuildingState.Active)
    {
        var building = new BuildingInfo(id, type, new TileCoord(x, y), Rotation.Deg0, width, height, state, 0);
        AddBuilding(building);
        return building;
    }

    /// <summary>Replaces the building that has the same ID.</summary>
    /// <param name="building">The new value.</param>
    public void ReplaceBuilding(BuildingInfo building)
    {
        int index = _buildings.FindIndex(b => b.Id == building.Id);
        _buildings[index] = building;
    }

    /// <summary>Removes a building.</summary>
    /// <param name="id">The entity ID.</param>
    /// <returns><see langword="true"/> if it existed.</returns>
    public bool RemoveBuilding(EntityId id) => _buildings.RemoveAll(b => b.Id == id) > 0;

    /// <summary>Marks the chunk that holds a tile dirty.</summary>
    /// <param name="x">The tile column.</param>
    /// <param name="y">The tile row.</param>
    public void MarkDirty(int x, int y)
    {
        var chunk = ChunkCoord.Of(x, y);
        _dirty.Add((chunk.Y, chunk.X));
    }

    /// <inheritdoc />
    public TileInfo GetTile(int x, int y)
    {
        if (!new TileCoord(x, y).IsInside(Width, Height))
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is off the {Width} × {Height} map.");
        }

        return _tiles.GetValueOrDefault(new TileCoord(x, y), DefaultTile);
    }

    /// <inheritdoc />
    public EntityId BuildingAt(int x, int y)
    {
        foreach (BuildingInfo building in _buildings)
        {
            if (building.Covers(x, y))
            {
                return building.Id;
            }
        }

        return EntityId.None;
    }

    /// <inheritdoc />
    public bool TryGetBuilding(EntityId id, out BuildingInfo building)
    {
        foreach (BuildingInfo candidate in _buildings)
        {
            if (candidate.Id == id)
            {
                building = candidate;
                return true;
            }
        }

        building = default;
        return false;
    }

    /// <inheritdoc />
    public int DrainDirtyChunks(Span<ChunkCoord> destination)
    {
        int written = 0;
        while (written < destination.Length && _dirty.Count > 0)
        {
            (int y, int x) = _dirty.Min;
            _dirty.Remove(_dirty.Min);
            destination[written++] = new ChunkCoord(x, y);
        }

        return written;
    }

    /// <inheritdoc />
    bool IBuildingSource.TryGetNext(EntityId after, BuildingFilter filter, out BuildingInfo building)
    {
        foreach (BuildingInfo candidate in _buildings)
        {
            if (candidate.Id > after && (filter == BuildingFilter.Any || candidate.IsComplete))
            {
                building = candidate;
                return true;
            }
        }

        building = default;
        return false;
    }
}
