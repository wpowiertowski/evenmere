using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Map;

/// <summary>Implements <see cref="IMapQueries"/> over the map state, the building registry and the dirty-chunk tracker. Every value it returns is a copy.</summary>
internal sealed class MapQueries : IMapQueries
{
    private readonly MapState _state;
    private readonly BuildingRegistry _registry;
    private readonly ChunkDirtyTracker _dirty;

    /// <summary>Initializes a new instance of the <see cref="MapQueries"/> class.</summary>
    /// <param name="state">The map state.</param>
    /// <param name="registry">The building registry.</param>
    /// <param name="dirty">The dirty-chunk tracker.</param>
    public MapQueries(MapState state, BuildingRegistry registry, ChunkDirtyTracker dirty)
    {
        _state = state;
        _registry = registry;
        _dirty = dirty;
    }

    /// <inheritdoc />
    public int Width => _state.Width;

    /// <inheritdoc />
    public int Height => _state.Height;

    /// <inheritdoc />
    public BuildingSequence Buildings => new(_registry, BuildingFilter.Any);

    /// <inheritdoc />
    public BuildingSequence ActiveBuildings => new(_registry, BuildingFilter.Complete);

    /// <inheritdoc />
    public TileInfo GetTile(int x, int y)
    {
        if (!new TileCoord(x, y).IsInside(_state.Width, _state.Height))
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is off the {_state.Width} × {_state.Height} map.");
        }

        int i = (y * _state.Width) + x;
        byte deposit = _state.Deposit[i];
        return new TileInfo(
            TerrainId.From(_state.Terrain[i]),
            _state.Elevation[i],
            (SlopeClass)_state.Slope[i],
            (TileFeatures)_state.Features[i],
            (RiverWidthClass)_state.RiverWidth[i],
            _state.Region[i],
            deposit == 0 ? null : (DepositKind)(deposit - 1),
            _state.Fertility[i]);
    }

    /// <inheritdoc />
    public EntityId BuildingAt(int x, int y) =>
        new TileCoord(x, y).IsInside(_state.Width, _state.Height) ? new EntityId(_state.Occupancy[(y * _state.Width) + x]) : EntityId.None;

    /// <inheritdoc />
    public bool TryGetBuilding(EntityId id, out BuildingInfo building) => _registry.TryGet(id, out building);

    /// <inheritdoc />
    public int DrainDirtyChunks(Span<ChunkCoord> destination) => _dirty.Drain(destination);
}
