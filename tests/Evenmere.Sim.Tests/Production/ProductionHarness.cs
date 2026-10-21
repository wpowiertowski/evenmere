using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Production;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Production;

/// <summary>
/// A tiny fixture world for the production system: the real vertical-slice content, the fake queries and services, and a
/// <see cref="ProductionSystem"/> over a fresh <see cref="ProductionState"/> that listens to the fake building lifecycle.
/// Arrange the map, workforce, roads and modifiers through <see cref="Sim"/>, add buildings through
/// <see cref="Add"/>, then step the system with <see cref="RunTicks"/>.
/// </summary>
internal sealed class ProductionHarness
{
    private static readonly Lazy<ContentDb> _sliceContent =
        new(static () => ContentLoader.Load(Path.Combine(WorldBuilder.RepoRoot, "content")).GetDbOrThrow());

    private readonly CommandQueue _commands = new();
    private readonly CommandRouter _router = new();

    private ProductionHarness(FakeSim sim)
    {
        Sim = sim;
        Sim.Queries.Map.DefaultTile = Tile("plains");
    }

    /// <summary>Gets the id every building's storehouse reach is granted from, see <see cref="GrantReach"/>.</summary>
    public static EntityId Storehouse { get; } = new(9000);

    /// <summary>Gets the real content of the vertical slice, loaded once.</summary>
    public static ContentDb SliceContent => _sliceContent.Value;

    /// <summary>Gets the fake world.</summary>
    public FakeSim Sim { get; }

    /// <summary>Gets the system under test; it exists once <see cref="Start"/> has run.</summary>
    public ProductionSystem System { get; private set; } = null!;

    /// <summary>Gets the context the system runs with, whose services use the real production buffers.</summary>
    public SimContext Context { get; private set; } = null!;

    /// <summary>Gets the content.</summary>
    public ContentDb Content => Sim.Content;

    /// <summary>Gets the production state.</summary>
    public ProductionState State => Sim.World.Production;

    /// <summary>Gets the fake map.</summary>
    public FakeMapQueries Map => Sim.Queries.Map;

    /// <summary>Creates a harness and starts the system.</summary>
    /// <param name="content">The content; the vertical slice by default.</param>
    /// <returns>The harness, at tick 0.</returns>
    public static ProductionHarness Create(ContentDb? content = null)
    {
        var harness = new ProductionHarness(FakeSim.Create(content ?? SliceContent));
        harness.Start();
        return harness;
    }

    /// <summary>Creates a harness without a system, so buildings can be added before it listens (a restored save).</summary>
    /// <param name="content">The content; the vertical slice by default.</param>
    /// <returns>The harness, at tick 0.</returns>
    public static ProductionHarness CreateStopped(ContentDb? content = null) => new(FakeSim.Create(content ?? SliceContent));

    /// <summary>Describes a tile of the named terrain.</summary>
    /// <param name="terrain">The terrain's content ID.</param>
    /// <param name="fertility">The fertility mask.</param>
    /// <param name="deposit">The deposit under the tile.</param>
    /// <returns>The tile.</returns>
    public TileInfo Tile(string terrain, uint fertility = 0, DepositKind? deposit = null) =>
        new(Content.IdOf<TerrainId>(terrain), 0, SlopeClass.Flat, TileFeatures.None, RiverWidthClass.None, 0, deposit, fertility);

    /// <summary>Creates the system over <see cref="State"/> (or a restored state) and subscribes it to the lifecycle.</summary>
    /// <param name="restored">A production state read from a save, which replaces the world's own.</param>
    public void Start(ProductionState? restored = null)
    {
        if (restored is not null)
        {
            Sim.World.Production = restored;
        }

        System = new ProductionSystem(Sim.World.Production, Content);
        System.Register(_router);
        Sim.Services.BuildingLifecycle.Subscribe(System);

        // FakeSim wires the fake production buffers. Production owns the real ones, so build a second context that carries
        // them and shares everything else; creating it binds the shared fakes to it, and the last context bound wins.
        var fakes = Sim.Services.ToSimServices();
        Context = new SimContext(
            Sim.Content,
            Sim.World,
            Sim.Queries,
            Sim.Events,
            new SimServices
            {
                BuildingRegistry = fakes.BuildingRegistry,
                BuildingLifecycle = fakes.BuildingLifecycle,
                DirtyChunks = fakes.DirtyChunks,
                Storage = fakes.Storage,
                ProgressionService = fakes.ProgressionService,
                PointsLedger = fakes.PointsLedger,
                Counters = fakes.Counters,
                ProductionBuffers = System.Buffers,
                WorkforceSupply = fakes.WorkforceSupply,
            });
    }

    /// <summary>Gets a building type's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public BuildingTypeId Type(string name) => Content.IdOf<BuildingTypeId>(name);

    /// <summary>Gets a good's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public GoodId Good(string name) => Content.IdOf<GoodId>(name);

    /// <summary>Adds a building through the fake registry, which tells the lifecycle listeners.</summary>
    /// <param name="type">The building type's content ID.</param>
    /// <param name="x">The column of the footprint's top-left tile.</param>
    /// <param name="y">The row of the footprint's top-left tile.</param>
    /// <param name="state">The registry state to add it in.</param>
    /// <returns>The building's entity ID.</returns>
    public EntityId Add(string type, int x, int y, BuildingState state = BuildingState.Active) =>
        Sim.Services.BuildingRegistry.Add(Type(type), x, y, Rotation.Deg0, state);

    /// <summary>Adds a building that is staffed, inside a storehouse's reach and ready to run.</summary>
    /// <param name="type">The building type's content ID.</param>
    /// <param name="x">The column of the footprint's top-left tile.</param>
    /// <param name="y">The row of the footprint's top-left tile.</param>
    /// <returns>The building's entity ID.</returns>
    public EntityId AddReady(string type, int x, int y)
    {
        EntityId id = Add(type, x, y);
        GrantReach(id);
        return id;
    }

    /// <summary>Puts a building inside a storehouse's reach.</summary>
    /// <param name="building">The building.</param>
    public void GrantReach(EntityId building) => Sim.Queries.Roads.SetReach(Storehouse, building, 1000);

    /// <summary>Sets a building's workforce fill.</summary>
    /// <param name="building">The building.</param>
    /// <param name="perMille">The fill in per-mille.</param>
    public void SetFill(EntityId building, int perMille) => Sim.Queries.Workforce.SetFill(building, Ratio.FromPerMille(perMille));

    /// <summary>Runs the system for a number of ticks, advancing the clock before each, as the simulation does.</summary>
    /// <param name="ticks">How many ticks.</param>
    public void RunTicks(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            Sim.SetTick(Sim.World.Clock.Tick + 1);
            System.Run(Context);
        }
    }

    /// <summary>Gets what the production queries say about a producer.</summary>
    /// <param name="building">The producer.</param>
    /// <returns>The description.</returns>
    public ProductionInfo Info(EntityId building)
    {
        System.Queries.TryGet(building, out ProductionInfo info).ShouldBeTrue();
        return info;
    }

    /// <summary>Gets the amount of a good in a producer's output buffer.</summary>
    /// <param name="building">The producer.</param>
    /// <param name="good">The good's content ID.</param>
    /// <returns>The amount.</returns>
    public Amount Output(EntityId building, string good) => AmountOf(Info(building).OutputBuffer, Good(good));

    /// <summary>Gets the amount of a good in a producer's input buffer.</summary>
    /// <param name="building">The producer.</param>
    /// <param name="good">The good's content ID.</param>
    /// <returns>The amount.</returns>
    public Amount Input(EntityId building, string good) => AmountOf(Info(building).InputBuffer, Good(good));

    /// <summary>Queues a command and applies it, as the start of a tick would.</summary>
    /// <param name="command">The command.</param>
    public void Send(Command command)
    {
        _commands.Enqueue(command);
        _router.ApplyQueued(_commands, Context);
    }

    /// <summary>Returns the events emitted since the last call.</summary>
    /// <returns>The events in emission order.</returns>
    public IReadOnlyList<SimEvent> Events() => Sim.DrainEvents();

    private static Amount AmountOf(IEnumerable<GoodAmount> buffer, GoodId good) =>
        buffer.Single(entry => entry.Good == good).Amount;
}
