using Evenmere.Content;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Systems.Map;

/// <summary>
/// The map service: tile grid, terrain and the building registry. A shared service rather than a ticked system, so its cadence is
/// <see cref="Cadence.None"/> and <see cref="Run"/> is never called. It owns the three write services other systems reach
/// through <c>SimContext</c> (<see cref="Registry"/>, <see cref="Lifecycle"/> and <see cref="DirtyChunks"/>) and the read-only
/// <see cref="Queries"/>. It has no commands of its own in M1.
/// </summary>
internal sealed class MapSystem : ISimSystem
{
    private readonly MapState _state;
    private readonly ContentDb _content;
    private readonly ChunkDirtyTracker _dirty = new();

    /// <summary>Initializes a new instance of the <see cref="MapSystem"/> class over a map state, which is empty for a new game or restored from a save.</summary>
    /// <param name="state">The map state.</param>
    /// <param name="entityIds">The world's entity ID allocator, from which buildings take their IDs.</param>
    /// <param name="content">The loaded content.</param>
    /// <exception cref="InvalidDataException">The state is inconsistent with itself or with the content.</exception>
    public MapSystem(MapState state, EntityIdAllocator entityIds, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(entityIds);
        ArgumentNullException.ThrowIfNull(content);

        // A restored map must be sound before anything reads it; a corrupt save then fails here, at load.
        state.Validate(content, entityIds.NextValue);

        _state = state;
        _content = content;
        Lifecycle = new BuildingLifecycle();
        BuildingRegistry registry = new(state, content, entityIds, _dirty, Lifecycle);
        Registry = registry;
        Queries = new MapQueries(state, registry, _dirty);

        // A map that came with the state (a restored save) has never been drawn, so the view needs every chunk once.
        _dirty.Resize(state.Width, state.Height);
        _dirty.MarkAll();
    }

    /// <inheritdoc />
    public string Name => SystemNames.Map;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IMapQueries Queries { get; }

    /// <summary>Gets the write side of the building registry.</summary>
    public IBuildingRegistry Registry { get; }

    /// <summary>Gets the subscription list for building lifecycle notifications.</summary>
    public BuildingLifecycle Lifecycle { get; }

    /// <summary>Gets the dirty-chunk tracker that the roads service marks road changes on.</summary>
    public IChunkDirtyTracker DirtyChunks => _dirty;

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // The map has no commands in M1: buildings are placed and demolished by Construction, which calls the registry.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // A service is never ticked (Cadence.None); it changes only through command handlers and the calls other systems make into it.
    }

    /// <summary>
    /// Replaces the empty map with a loaded one: copies its tile layers, derives the slope and coast, and marks every chunk dirty.
    /// New-game code (M1-13) calls this before it places the Founders' Hall; tests call it to build a world.
    /// </summary>
    /// <param name="map">The loaded map, resolved against the same content as the simulation.</param>
    /// <exception cref="InvalidOperationException">The map already holds buildings, whose footprints would no longer fit the grid.</exception>
    /// <exception cref="ArgumentException">The map was loaded against different content.</exception>
    internal void LoadMap(MapDef map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (_state.Buildings.Count > 0)
        {
            throw new InvalidOperationException("A map can only be loaded while there are no buildings.");
        }

        if (map.TerrainFeatures.Length != _content.Terrains.Count)
        {
            throw new ArgumentException(
                $"The map was loaded against content with {map.TerrainFeatures.Length} terrains but the simulation has {_content.Terrains.Count}.", nameof(map));
        }

        if (_content.Terrains.Count > MapState.MaxTerrains)
        {
            throw new ArgumentException($"The content has {_content.Terrains.Count} terrains but map terrain is stored in a byte.", nameof(map));
        }

        MapStateLoader.Populate(_state, map);
        _dirty.Resize(_state.Width, _state.Height);
        _dirty.MarkAll();
    }
}
