using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Map;

/// <summary>Implements <see cref="IMapQueries"/>. M1-00 declares the methods and M1-01 implements them; until then each throws <see cref="NotImplementedException"/>.</summary>
internal sealed class MapQueries : IMapQueries
{
    /// <inheritdoc />
    public int Width => throw new NotImplementedException("M1-01");

    /// <inheritdoc />
    public int Height => throw new NotImplementedException("M1-01");

    /// <inheritdoc />
    public BuildingSequence Buildings => throw new NotImplementedException("M1-01");

    /// <inheritdoc />
    public BuildingSequence ActiveBuildings => throw new NotImplementedException("M1-01");

    /// <inheritdoc />
    public TileInfo GetTile(int x, int y) => throw new NotImplementedException("M1-01");

    /// <inheritdoc />
    public EntityId BuildingAt(int x, int y) => throw new NotImplementedException("M1-01");

    /// <inheritdoc />
    public bool TryGetBuilding(EntityId id, out BuildingInfo building) => throw new NotImplementedException("M1-01");

    /// <inheritdoc />
    public int DrainDirtyChunks(Span<ChunkCoord> destination) => throw new NotImplementedException("M1-01");
}
