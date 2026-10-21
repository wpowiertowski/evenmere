using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Construction;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Construction;

/// <summary>
/// A 32 × 32 map of flat plains on the real slice content plus the small test pack beside this file (a quarry on a deposit,
/// a water mill, upgrades, a unique shrine and so on), with the construction system registered on a command router. A test
/// arranges the map and the stock, queues commands, and calls <see cref="Tick"/>, which does what the simulation does each
/// tick: advance the clock, apply the queued commands, run the system.
/// </summary>
internal sealed class ConstructionWorld
{
    private static readonly Lazy<ContentDb> _content = new(static () => ContentLoader.Load(
        [Path.Combine(WorldBuilder.RepoRoot, "content"), Path.Combine(WorldBuilder.RepoRoot, "tests", "Evenmere.Sim.Tests", "Construction", "Content")])
        .GetDbOrThrow());

    private readonly CommandQueue _queue = new();
    private readonly CommandRouter _router = new();

    /// <summary>Initializes a new instance of the <see cref="ConstructionWorld"/> class.</summary>
    /// <param name="unlockAll">Whether every building type starts unlocked; the fake progression unlocks nothing by itself.</param>
    /// <param name="restored">A construction state read from a save to continue from; a fresh one by default.</param>
    public ConstructionWorld(bool unlockAll = true, ConstructionState? restored = null)
    {
        Sim = FakeSim.Create(Content);
        if (restored is not null)
        {
            Sim.World.Construction = restored;
        }

        State = Sim.World.Construction;
        System = new ConstructionSystem(State, Content);
        System.Register(_router);

        Sim.Queries.Map.DefaultTile = new TileInfo(Terrain("plains"), 4, SlopeClass.Flat, TileFeatures.None, RiverWidthClass.None, 0, null, 0);
        if (unlockAll)
        {
            foreach (string name in Content.Buildings.Names)
            {
                Sim.Queries.Progression.Unlock(Content.IdOf<BuildingTypeId>(name));
            }
        }
    }

    /// <summary>Creates a world.</summary>
    /// <param name="unlockAll">Whether every building type starts unlocked.</param>
    /// <returns>The world.</returns>
    public static ConstructionWorld Create(bool unlockAll = true) => new(unlockAll);

    /// <summary>Creates a world that carries on from a construction state read from a save.</summary>
    /// <param name="restored">The state.</param>
    /// <returns>The world.</returns>
    public static ConstructionWorld Create(ConstructionState restored) => new(true, restored);

    /// <summary>Gets the real slice content plus the test pack, loaded once.</summary>
    public static ContentDb Content => _content.Value;

    /// <summary>Gets the harness over fakes.</summary>
    public FakeSim Sim { get; }

    /// <summary>Gets the construction state the system under test works on.</summary>
    public ConstructionState State { get; }

    /// <summary>Gets the construction system.</summary>
    public ConstructionSystem System { get; }

    /// <summary>Gets the current tick.</summary>
    public long Now => Sim.World.Clock.Tick;

    /// <summary>Gets a building type by its content name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The ID.</returns>
    public static BuildingTypeId Type(string name) => Content.IdOf<BuildingTypeId>(name);

    /// <summary>Gets a good by its content name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The ID.</returns>
    public static GoodId Good(string name) => Content.IdOf<GoodId>(name);

    /// <summary>Gets a terrain by its content name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The ID.</returns>
    public static TerrainId Terrain(string name) => Content.IdOf<TerrainId>(name);

    /// <summary>Builds the check the ghost preview uses, over this world.</summary>
    /// <returns>The check.</returns>
    public BuildingPlacement Placement() => BuildingPlacement.For(Sim.Context, State);

    /// <summary>Rewrites one tile.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="change">What to change, given the tile as it is.</param>
    public void Edit(int x, int y, Func<TileInfo, TileInfo> change) =>
        Sim.Queries.Map.SetTile(x, y, change(Sim.Queries.Map.GetTile(x, y)));

    /// <summary>Sets the terrain of a rectangle of tiles.</summary>
    /// <param name="terrain">The terrain name.</param>
    /// <param name="x">The first column.</param>
    /// <param name="y">The first row.</param>
    /// <param name="width">The width in tiles.</param>
    /// <param name="height">The height in tiles.</param>
    public void Paint(string terrain, int x, int y, int width, int height)
    {
        TerrainId id = Terrain(terrain);
        for (int ty = y; ty < y + height; ty++)
        {
            for (int tx = x; tx < x + width; tx++)
            {
                Edit(tx, ty, tile => tile with { Terrain = id });
            }
        }
    }

    /// <summary>Puts a stock of a good in the pool.</summary>
    /// <param name="good">The good name.</param>
    /// <param name="units">The stock in units.</param>
    public void Stock(string good, string units) => Sim.Services.Storage.Set(Good(good), Amount.Parse(units));

    /// <summary>Reads the stock of a good.</summary>
    /// <param name="good">The good name.</param>
    /// <returns>The stock.</returns>
    public Amount Stocked(string good) => Sim.Services.Storage.Get(Good(good));

    /// <summary>Adds a building straight to the registry, as another system or an earlier game would have.</summary>
    /// <param name="type">The building type name.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="state">The state to register it in.</param>
    /// <returns>The building's ID.</returns>
    public EntityId Build(string type, int x, int y, BuildingState state = BuildingState.Active) =>
        Sim.Services.BuildingRegistry.Add(Type(type), x, y, Rotation.Deg0, state);

    /// <summary>Builds a storehouse in the top-left corner with a dirt road along row 2 under it, from column 0 to column 30.</summary>
    /// <param name="storehouseState">The storehouse's state; only finished storehouses reach anything.</param>
    /// <returns>The storehouse.</returns>
    public EntityId StorehouseWithRoad(BuildingState storehouseState = BuildingState.Active)
    {
        EntityId storehouse = Build("storehouse", 0, 0, storehouseState);
        for (int x = 0; x <= 30; x++)
        {
            Sim.Queries.Roads.SetRoad(x, 2, Type("dirt_road"));
        }

        return storehouse;
    }

    /// <summary>Puts a building in some storehouse's reach, as the roads system would report it.</summary>
    /// <param name="building">The building.</param>
    public void GrantReach(EntityId building) => Sim.Queries.Roads.SetReach(new EntityId(9000), building, 1000);

    /// <summary>Queues a command for the next tick.</summary>
    /// <param name="command">The command.</param>
    public void Enqueue(Command command) => _queue.Enqueue(command);

    /// <summary>Runs one tick: the clock moves on, the queued commands are applied, then the system runs.</summary>
    public void Tick()
    {
        Sim.SetTick(Now + 1);
        _router.ApplyQueued(_queue, Sim.Context);
        System.Run(Sim.Context);
    }

    /// <summary>Runs several ticks.</summary>
    /// <param name="ticks">How many.</param>
    public void Tick(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            Tick();
        }
    }

    /// <summary>Queues a placement and runs the tick that applies it.</summary>
    /// <param name="type">The building type name.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="rotation">The orientation.</param>
    /// <returns>The new site's ID, or <see cref="EntityId.None"/> if the command was refused.</returns>
    public EntityId Place(string type, int x, int y, Rotation rotation = Rotation.Deg0)
    {
        Enqueue(new PlaceBuilding(Type(type), x, y, rotation));
        Tick();
        return Sim.Queries.Map.BuildingAt(x, y);
    }

    /// <summary>Reads the single rejection among the events drained, if there is one.</summary>
    /// <returns>The rejection, or <see langword="null"/> when no command was refused.</returns>
    public CommandRejected? Rejection() => Sim.DrainEvents().OfType<CommandRejected>().SingleOrDefault();

    /// <summary>Reads a building from the map.</summary>
    /// <param name="id">The building.</param>
    /// <returns>The building.</returns>
    public BuildingInfo Info(EntityId id)
    {
        Sim.Queries.Map.TryGetBuilding(id, out BuildingInfo building).ShouldBeTrue($"{id} should exist");
        return building;
    }

    /// <summary>Reads a site's progress.</summary>
    /// <param name="id">The building.</param>
    /// <returns>The site.</returns>
    public SiteInfo Site(EntityId id)
    {
        System.Queries.TryGetSite(id, out SiteInfo site).ShouldBeTrue($"{id} should be a site");
        return site;
    }
}

/// <summary>A lifecycle listener that records what it is told.</summary>
/// <param name="activated">Receives each building that became active.</param>
/// <param name="removed">Receives each building that was removed.</param>
/// <param name="typeChanged">Receives each building whose type changed, with the type it had.</param>
internal sealed class RecordingListener(
    List<BuildingInfo> activated, List<BuildingInfo> removed, List<(BuildingInfo Building, BuildingTypeId Previous)> typeChanged)
    : IBuildingLifecycleListener
{
    /// <inheritdoc />
    public void OnActivated(SimContext ctx, BuildingInfo building) => activated.Add(building);

    /// <inheritdoc />
    public void OnRemoved(SimContext ctx, BuildingInfo building) => removed.Add(building);

    /// <inheritdoc />
    public void OnTypeChanged(SimContext ctx, BuildingInfo building, BuildingTypeId previousType) => typeChanged.Add((building, previousType));
}
