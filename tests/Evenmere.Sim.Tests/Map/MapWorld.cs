using System.Text;
using Evenmere.Content;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Map;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Map;

/// <summary>
/// A real simulation on the fixture content (the minimal pack plus the terrains and buildings of <c>tests/fixtures/maps/pack</c>)
/// with a map loaded, and handles on the map service. Nothing here is faked: it is the composition root's own wiring.
/// </summary>
internal sealed class MapWorld
{
    private static readonly Lazy<ContentDb> _content = new(() => ContentLoader.Load(Roots).GetDbOrThrow());

    private MapWorld(Simulation sim)
    {
        Sim = sim;
        Map = (MapSystem)sim.Pipeline.Entries.Single(e => e.Id == SystemId.Map).System;
    }

    /// <summary>Gets the content roots of the fixture content.</summary>
    public static IReadOnlyList<string> Roots => [WorldBuilder.MinimalPackPath, Pack];

    /// <summary>Gets the fixture pack folder.</summary>
    public static string Pack => Path.Combine(WorldBuilder.RepoRoot, "tests", "fixtures", "maps", "pack");

    /// <summary>Gets the fixture content, loaded once.</summary>
    public static ContentDb Content => _content.Value;

    /// <summary>Gets the simulation.</summary>
    public Simulation Sim { get; }

    /// <summary>Gets the map service.</summary>
    public MapSystem Map { get; }

    /// <summary>Gets the read-only map queries.</summary>
    public IMapQueries Queries => Sim.Query.Map;

    /// <summary>Gets the registry as systems reach it, through the context.</summary>
    public IBuildingRegistry Registry => Sim.Context.BuildingRegistry;

    /// <summary>Gets the lifecycle as systems reach it.</summary>
    public IBuildingLifecycle Lifecycle => Sim.Context.BuildingLifecycle;

    /// <summary>Gets the dirty-chunk tracker as the roads service reaches it.</summary>
    public IChunkDirtyTracker Dirty => Sim.Context.DirtyChunks;

    /// <summary>Creates a new game with the empty 0 × 0 map.</summary>
    /// <returns>The world.</returns>
    public static MapWorld Empty() => new(Simulation.CreateNew(Content, new NewGameOptions { Seed = 7 }));

    /// <summary>Creates a new game and loads a map from the fixture pack.</summary>
    /// <param name="mapId">The fixture map ID.</param>
    /// <returns>The world.</returns>
    public static MapWorld WithFixture(string mapId)
    {
        MapWorld world = Empty();
        world.Map.LoadMap(MapLoader.Load(Roots, mapId, Content).GetMapOrThrow());
        return world;
    }

    /// <summary>Creates a new game on an all-plains map of any size, so chunk boundaries can be tested.</summary>
    /// <param name="width">The width in tiles.</param>
    /// <param name="height">The height in tiles.</param>
    /// <returns>The world.</returns>
    public static MapWorld Plains(int width, int height)
    {
        string root = Path.Combine(Path.GetTempPath(), "evenmere-sim-map-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "maps", "plain");
        Directory.CreateDirectory(folder);
        try
        {
            var rows = new StringBuilder();
            for (int y = 0; y < height; y++)
            {
                rows.Append('.', width).Append('\n');
            }

            File.WriteAllText(Path.Combine(folder, "terrain.txt"), rows.ToString());
            File.WriteAllText(Path.Combine(folder, "map.json"), $$"""{ "width": {{width}}, "height": {{height}}, "startSite": { "x": 0, "y": 0 } }""");
            MapWorld world = Empty();
            world.Map.LoadMap(MapLoader.Load([.. Roots, root], "plain", Content).GetMapOrThrow());
            return world;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Gets a building type of the fixture content.</summary>
    /// <param name="name">The string ID.</param>
    /// <returns>The ID.</returns>
    public static BuildingTypeId Type(string name) => Content.IdOf<BuildingTypeId>(name);

    /// <summary>Restores a copy of the simulation from its serialised state.</summary>
    /// <returns>The restored world.</returns>
    public MapWorld SaveAndRestore() => new(Simulation.Restore(Content, Sim.SerializeState()));

    /// <summary>Drains every dirty chunk.</summary>
    /// <returns>The chunks, in the order the query handed them out.</returns>
    public List<ChunkCoord> DrainAll()
    {
        var all = new List<ChunkCoord>();
        var buffer = new ChunkCoord[64];
        int count;
        while ((count = Queries.DrainDirtyChunks(buffer)) > 0)
        {
            all.AddRange(buffer.AsSpan(0, count).ToArray());
        }

        return all;
    }

    /// <summary>Lists every building the queries return, in order.</summary>
    /// <returns>The buildings.</returns>
    public List<BuildingInfo> AllBuildings()
    {
        var list = new List<BuildingInfo>();
        foreach (BuildingInfo building in Queries.Buildings)
        {
            list.Add(building);
        }

        return list;
    }

    /// <summary>Lists the finished buildings the queries return, in order.</summary>
    /// <returns>The buildings.</returns>
    public List<BuildingInfo> ActiveBuildings()
    {
        var list = new List<BuildingInfo>();
        foreach (BuildingInfo building in Queries.ActiveBuildings)
        {
            list.Add(building);
        }

        return list;
    }
}

/// <summary>A lifecycle listener that writes what it is told to a shared log.</summary>
/// <param name="name">The listener's name in the log.</param>
/// <param name="log">The log shared by the listeners of a test.</param>
internal sealed class RecordingListener(string name, List<string> log) : IBuildingLifecycleListener
{
    /// <summary>Gets or sets something to do while a notification is being sent, such as reading the queries.</summary>
    public Action<SimContext, BuildingInfo>? OnEvent { get; set; }

    /// <inheritdoc />
    public void OnActivated(SimContext ctx, BuildingInfo building)
    {
        log.Add($"{name}:activated:{building.Id.Value}");
        OnEvent?.Invoke(ctx, building);
    }

    /// <inheritdoc />
    public void OnRemoved(SimContext ctx, BuildingInfo building)
    {
        log.Add($"{name}:removed:{building.Id.Value}");
        OnEvent?.Invoke(ctx, building);
    }

    /// <inheritdoc />
    public void OnTypeChanged(SimContext ctx, BuildingInfo building, BuildingTypeId previousType)
    {
        log.Add($"{name}:typechanged:{building.Id.Value}:{previousType.Value}>{building.Type.Value}");
        OnEvent?.Invoke(ctx, building);
    }
}
