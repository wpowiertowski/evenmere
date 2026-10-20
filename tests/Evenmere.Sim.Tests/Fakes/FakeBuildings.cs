using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Tests.Fakes;

/// <summary>
/// The building lifecycle as a fake: it keeps the listeners in subscription order and calls them with the context it was
/// bound to. Tests can also raise a notification by hand.
/// </summary>
internal sealed class FakeBuildingLifecycle : IBuildingLifecycle, IBindsContext
{
    private readonly List<IBuildingLifecycleListener> _listeners = [];
    private SimContext? _context;

    /// <summary>Gets the listeners in the order they subscribed.</summary>
    public IReadOnlyList<IBuildingLifecycleListener> Listeners => _listeners;

    /// <inheritdoc />
    public void Subscribe(IBuildingLifecycleListener listener) => _listeners.Add(listener);

    /// <inheritdoc />
    public void Bind(SimContext context) => _context = context;

    /// <summary>Tells every listener that a building became active.</summary>
    /// <param name="building">The building.</param>
    public void RaiseActivated(BuildingInfo building)
    {
        foreach (IBuildingLifecycleListener listener in _listeners)
        {
            listener.OnActivated(Context, building);
        }
    }

    /// <summary>Tells every listener that a building was removed.</summary>
    /// <param name="building">The building as it stood before removal.</param>
    public void RaiseRemoved(BuildingInfo building)
    {
        foreach (IBuildingLifecycleListener listener in _listeners)
        {
            listener.OnRemoved(Context, building);
        }
    }

    /// <summary>Tells every listener that a building changed type.</summary>
    /// <param name="building">The building with its new type.</param>
    /// <param name="previousType">The type it had.</param>
    public void RaiseTypeChanged(BuildingInfo building, BuildingTypeId previousType)
    {
        foreach (IBuildingLifecycleListener listener in _listeners)
        {
            listener.OnTypeChanged(Context, building, previousType);
        }
    }

    private SimContext Context =>
        _context ?? throw new InvalidOperationException("The lifecycle has no context yet; create a FakeSim, whose SimContext binds it.");
}

/// <summary>
/// The building registry as a fake. It mirrors what the real one promises: it allocates IDs, sizes the footprint from the
/// building's definition and rotation, refuses a footprint that leaves the map or overlaps another building, keeps the
/// <see cref="FakeMapQueries"/> it writes to consistent, marks chunks dirty, and raises the lifecycle notifications.
/// </summary>
/// <param name="content">The content that sizes footprints.</param>
/// <param name="map">The map fake to write to.</param>
/// <param name="lifecycle">The lifecycle to notify.</param>
/// <param name="ids">The entity ID allocator, shared with the world state so IDs are unique.</param>
internal sealed class FakeBuildingRegistry(ContentDb content, FakeMapQueries map, FakeBuildingLifecycle lifecycle, EntityIdAllocator ids)
    : IBuildingRegistry
{
    /// <inheritdoc />
    public EntityId Add(BuildingTypeId type, int x, int y, Rotation rotation, BuildingState state)
    {
        Footprint footprint = rotation.Rotate(content.Get(type).Footprint);
        if (!new TileCoord(x, y).IsInside(map.Width, map.Height)
            || !new TileCoord(x + footprint.Width - 1, y + footprint.Height - 1).IsInside(map.Width, map.Height))
        {
            throw new ArgumentException($"A {footprint.Width} × {footprint.Height} footprint at ({x}, {y}) leaves the {map.Width} × {map.Height} map.");
        }

        for (int dy = 0; dy < footprint.Height; dy++)
        {
            for (int dx = 0; dx < footprint.Width; dx++)
            {
                if (!map.BuildingAt(x + dx, y + dy).IsNone)
                {
                    throw new ArgumentException($"The footprint at ({x}, {y}) overlaps another building at ({x + dx}, {y + dy}).");
                }
            }
        }

        EntityId id = ids.Allocate();
        var building = new BuildingInfo(id, type, new TileCoord(x, y), rotation, footprint.Width, footprint.Height, state, 0);
        map.AddBuilding(building);
        MarkDirty(building);
        if (state == BuildingState.Active)
        {
            lifecycle.RaiseActivated(building);
        }

        return id;
    }

    /// <inheritdoc />
    public bool Remove(EntityId building)
    {
        if (!map.TryGetBuilding(building, out BuildingInfo info))
        {
            return false;
        }

        map.RemoveBuilding(building);
        MarkDirty(info);
        lifecycle.RaiseRemoved(info);
        return true;
    }

    /// <inheritdoc />
    public void SetState(EntityId building, BuildingState state)
    {
        BuildingInfo before = Get(building);
        BuildingInfo after = before with { State = state };
        map.ReplaceBuilding(after);
        MarkDirty(after);
        if (before.State == BuildingState.Site && state == BuildingState.Active)
        {
            lifecycle.RaiseActivated(after);
        }
    }

    /// <inheritdoc />
    public void SetType(EntityId building, BuildingTypeId type)
    {
        BuildingInfo before = Get(building);
        BuildingInfo after = before with { Type = type };
        map.ReplaceBuilding(after);
        MarkDirty(after);
        lifecycle.RaiseTypeChanged(after, before.Type);
    }

    /// <inheritdoc />
    public void SetStage(EntityId building, int stage) => map.ReplaceBuilding(Get(building) with { Stage = stage });

    private BuildingInfo Get(EntityId building) =>
        map.TryGetBuilding(building, out BuildingInfo info) ? info : throw new KeyNotFoundException($"{building} is not a building.");

    // A footprint can straddle chunks, so mark the chunk of each corner.
    private void MarkDirty(BuildingInfo building)
    {
        int lastX = building.Origin.X + building.Width - 1;
        int lastY = building.Origin.Y + building.Height - 1;
        map.MarkDirty(building.Origin.X, building.Origin.Y);
        map.MarkDirty(lastX, building.Origin.Y);
        map.MarkDirty(building.Origin.X, lastY);
        map.MarkDirty(lastX, lastY);
    }
}

/// <summary>The dirty-chunk tracker as a fake, writing to the map fake so <c>DrainDirtyChunks</c> sees the marks.</summary>
/// <param name="map">The map fake.</param>
internal sealed class FakeChunkDirtyTracker(FakeMapQueries map) : IChunkDirtyTracker
{
    /// <inheritdoc />
    public void MarkTile(int x, int y) => map.MarkDirty(x, y);

    /// <inheritdoc />
    public void MarkRect(int minX, int minY, int maxX, int maxY)
    {
        for (int cy = minY / ChunkCoord.TilesPerSide; cy <= maxY / ChunkCoord.TilesPerSide; cy++)
        {
            for (int cx = minX / ChunkCoord.TilesPerSide; cx <= maxX / ChunkCoord.TilesPerSide; cx++)
            {
                map.MarkDirty(cx * ChunkCoord.TilesPerSide, cy * ChunkCoord.TilesPerSide);
            }
        }
    }
}
