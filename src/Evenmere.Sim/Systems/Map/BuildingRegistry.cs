using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Systems.Map;

/// <summary>
/// The building registry: the <see cref="IdTable{T}"/> in <see cref="MapState"/> plus the occupancy grid, kept consistent by
/// every call. It is the write side (<see cref="IBuildingRegistry"/>) and, as an <see cref="IBuildingSource"/>, the source
/// of the read side's allocation-free sequences. It throws on misuse (a footprint off the map or over another building,
/// an unknown building); whether a placement is allowed is Construction's job, which validates before it calls.
/// </summary>
internal sealed class BuildingRegistry : IBuildingRegistry, IBuildingSource
{
    private readonly MapState _state;
    private readonly ContentDb _content;
    private readonly EntityIdAllocator _entityIds;
    private readonly ChunkDirtyTracker _dirty;
    private readonly BuildingLifecycle _lifecycle;

    // The live building IDs in ascending order, derived from the state and rebuilt when the registry is created. It lets a
    // sequence find "the first building after this ID" without scanning from the start, and costs no state.
    private int[] _ids;
    private int _count;

    // Where the sequence last stopped, so walking the buildings in order finds each next ID in one step.
    private int _cursor;

    /// <summary>Initializes a new instance of the <see cref="BuildingRegistry"/> class over a state that may already hold buildings.</summary>
    /// <param name="state">The map state.</param>
    /// <param name="content">The content that sizes footprints.</param>
    /// <param name="entityIds">The entity ID allocator, shared with the world state.</param>
    /// <param name="dirty">The dirty-chunk tracker to mark.</param>
    /// <param name="lifecycle">The lifecycle to notify.</param>
    public BuildingRegistry(MapState state, ContentDb content, EntityIdAllocator entityIds, ChunkDirtyTracker dirty, BuildingLifecycle lifecycle)
    {
        _state = state;
        _content = content;
        _entityIds = entityIds;
        _dirty = dirty;
        _lifecycle = lifecycle;
        _ids = new int[Math.Max(16, state.Buildings.Count)];
        foreach (IdTable<BuildingRecord>.Entry entry in state.Buildings)
        {
            _ids[_count++] = entry.Id.Value;
        }
    }

    /// <inheritdoc />
    public EntityId Add(BuildingTypeId type, int x, int y, Rotation rotation, BuildingState state)
    {
        EnsureNotNotifying();
        if (!rotation.IsValid())
        {
            throw new ArgumentException($"{(byte)rotation} is not a rotation; use 0 to 3.", nameof(rotation));
        }

        if (state is not (BuildingState.Site or BuildingState.Active or BuildingState.Ruin))
        {
            throw new ArgumentException($"A building is added as a Site, Active or Ruin, not {state}.", nameof(state));
        }

        if (type.Value >= _content.Buildings.Count)
        {
            throw new ArgumentException($"{type} is not a building type of the loaded content.", nameof(type));
        }

        Footprint footprint = rotation.Rotate(_content.Get(type).Footprint);
        if (x < 0 || y < 0 || (long)x + footprint.Width > _state.Width || (long)y + footprint.Height > _state.Height)
        {
            throw new ArgumentException($"A {footprint.Width} × {footprint.Height} footprint at ({x}, {y}) leaves the {_state.Width} × {_state.Height} map.");
        }

        for (int dy = 0; dy < footprint.Height; dy++)
        {
            for (int dx = 0; dx < footprint.Width; dx++)
            {
                if (_state.Occupancy[((y + dy) * _state.Width) + x + dx] != 0)
                {
                    throw new ArgumentException($"The footprint at ({x}, {y}) overlaps another building at ({x + dx}, {y + dy}).");
                }
            }
        }

        EntityId id = _entityIds.Allocate();
        var record = new BuildingRecord
        {
            Type = type,
            Origin = new TileCoord(x, y),
            Rotation = rotation,
            Width = footprint.Width,
            Height = footprint.Height,
            State = state,
            Stage = 0,
        };
        _state.Buildings.Add(id, record);
        InsertId(id.Value);
        Fill(record, id.Value);
        MarkDirty(record);

        if (state == BuildingState.Active)
        {
            _lifecycle.NotifyActivated(ToInfo(id, record));
        }

        return id;
    }

    /// <inheritdoc />
    public bool Remove(EntityId building)
    {
        EnsureNotNotifying();
        if (!_state.Buildings.TryGet(building, out BuildingRecord record))
        {
            return false;
        }

        Fill(record, 0);
        _state.Buildings.Remove(building);
        RemoveId(building.Value);
        MarkDirty(record);
        _lifecycle.NotifyRemoved(ToInfo(building, record));
        return true;
    }

    /// <inheritdoc />
    public void SetState(EntityId building, BuildingState state)
    {
        EnsureNotNotifying();
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentException($"{(byte)state} is not a building state.", nameof(state));
        }

        ref BuildingRecord record = ref _state.Buildings.GetRef(building);
        if (record.State == state)
        {
            return;
        }

        if (record.State == BuildingState.Site && state == BuildingState.Paused)
        {
            throw new InvalidOperationException($"{building} is a site; activate it before pausing it, or listeners would never learn that it was finished.");
        }

        bool activated = record.State == BuildingState.Site && state == BuildingState.Active;
        record.State = state;

        // The stage only means something for a site, so it starts over whenever the state changes.
        record.Stage = 0;
        MarkDirty(record);
        if (activated)
        {
            _lifecycle.NotifyActivated(ToInfo(building, record));
        }
    }

    /// <inheritdoc />
    public void SetType(EntityId building, BuildingTypeId type)
    {
        EnsureNotNotifying();
        if (type.Value >= _content.Buildings.Count)
        {
            throw new ArgumentException($"{type} is not a building type of the loaded content.", nameof(type));
        }

        ref BuildingRecord record = ref _state.Buildings.GetRef(building);
        BuildingTypeId previous = record.Type;
        if (previous == type)
        {
            return;
        }

        Footprint footprint = record.Rotation.Rotate(_content.Get(type).Footprint);
        if (footprint.Width != record.Width || footprint.Height != record.Height)
        {
            throw new ArgumentException(
                $"{type} is {footprint.Width} × {footprint.Height} when turned like {building}, which is {record.Width} × {record.Height}; a building keeps its footprint when its type changes.",
                nameof(type));
        }

        record.Type = type;
        MarkDirty(record);
        _lifecycle.NotifyTypeChanged(ToInfo(building, record), previous);
    }

    /// <inheritdoc />
    /// <remarks>A building that is not a site has no stage, so the call changes nothing for it.</remarks>
    public void SetStage(EntityId building, int stage)
    {
        EnsureNotNotifying();
        ArgumentOutOfRangeException.ThrowIfNegative(stage);

        ref BuildingRecord record = ref _state.Buildings.GetRef(building);
        if (record.State != BuildingState.Site || record.Stage == stage)
        {
            return;
        }

        record.Stage = stage;
        MarkDirty(record);
    }

    /// <summary>Looks up a building.</summary>
    /// <param name="id">The building's entity ID.</param>
    /// <param name="building">A copy of the building, when it exists.</param>
    /// <returns><see langword="true"/> if the building exists.</returns>
    public bool TryGet(EntityId id, out BuildingInfo building)
    {
        if (_state.Buildings.TryGet(id, out BuildingRecord record))
        {
            building = ToInfo(id, record);
            return true;
        }

        building = default;
        return false;
    }

    /// <inheritdoc />
    public bool TryGetNext(EntityId after, BuildingFilter filter, out BuildingInfo building)
    {
        int index;
        if (after.Value <= 0)
        {
            index = 0;
        }
        else if (_cursor < _count && _ids[_cursor] == after.Value)
        {
            index = _cursor + 1;
        }
        else
        {
            index = UpperBound(after.Value);
        }

        for (; index < _count; index++)
        {
            var id = new EntityId(_ids[index]);
            if (_state.Buildings.TryGet(id, out BuildingRecord record) && (filter == BuildingFilter.Any || IsComplete(record.State)))
            {
                _cursor = index;
                building = ToInfo(id, record);
                return true;
            }
        }

        building = default;
        return false;
    }

    private static bool IsComplete(BuildingState state) => state is BuildingState.Active or BuildingState.Paused;

    private static BuildingInfo ToInfo(EntityId id, in BuildingRecord record) =>
        new(id, record.Type, record.Origin, record.Rotation, record.Width, record.Height, record.State, record.Stage);

    // A listener that changed buildings from inside a notification would run the next listener against a registry that no
    // longer matches the event it is being told about.
    private void EnsureNotNotifying()
    {
        if (_lifecycle.IsDispatching)
        {
            throw new InvalidOperationException("A building lifecycle listener must not add, remove or change buildings.");
        }
    }

    // Writes an ID (or 0 to free) over every tile of a footprint.
    private void Fill(in BuildingRecord record, int value)
    {
        for (int dy = 0; dy < record.Height; dy++)
        {
            int row = ((record.Origin.Y + dy) * _state.Width) + record.Origin.X;
            for (int dx = 0; dx < record.Width; dx++)
            {
                _state.Occupancy[row + dx] = value;
            }
        }
    }

    private void MarkDirty(in BuildingRecord record) =>
        _dirty.MarkRect(record.Origin.X, record.Origin.Y, record.Origin.X + record.Width - 1, record.Origin.Y + record.Height - 1);

    // IDs are allocated in increasing order, so this nearly always appends.
    private void InsertId(int id)
    {
        if (_count == _ids.Length)
        {
            Array.Resize(ref _ids, _ids.Length * 2);
        }

        int at = _count == 0 || id > _ids[_count - 1] ? _count : UpperBound(id);
        if (at < _count)
        {
            Array.Copy(_ids, at, _ids, at + 1, _count - at);
        }

        _ids[at] = id;
        _count++;
    }

    private void RemoveId(int id)
    {
        int at = UpperBound(id) - 1;
        if (at < 0 || _ids[at] != id)
        {
            return;
        }

        Array.Copy(_ids, at + 1, _ids, at, _count - at - 1);
        _count--;
    }

    // The index of the first ID greater than the given one.
    private int UpperBound(int id)
    {
        int low = 0;
        int high = _count;
        while (low < high)
        {
            int mid = (int)((uint)(low + high) >> 1);
            if (_ids[mid] <= id)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
