using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Time;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Services;
using Evenmere.Sim.State;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Fakes;

/// <summary>
/// The write services made of fakes, wired to the fake queries so what one system writes another can read, for example a
/// building added through <see cref="BuildingRegistry"/> appears in <c>FakeSimQueries.Map</c>.
/// </summary>
internal sealed class FakeServices
{
    /// <summary>Initializes a new instance of the <see cref="FakeServices"/> class over a set of fake queries.</summary>
    /// <param name="content">The content that sizes footprints.</param>
    /// <param name="queries">The fake queries to share state with.</param>
    /// <param name="ids">The entity ID allocator, normally the world state's.</param>
    public FakeServices(ContentDb content, FakeSimQueries queries, EntityIdAllocator ids)
    {
        BuildingLifecycle = new FakeBuildingLifecycle();
        BuildingRegistry = new FakeBuildingRegistry(content, queries.Map, BuildingLifecycle, ids);
        DirtyChunks = new FakeChunkDirtyTracker(queries.Map);
        Storage = queries.Storage;
        ProgressionService = new FakeProgressionService();
        PointsLedger = new FakePointsLedger();
        Counters = new FakeCounters(queries.Progression);
        ProductionBuffers = new FakeProductionBuffers();
        WorkforceSupply = new FakeWorkforceSupply(queries.Workforce);
    }

    /// <summary>Gets the building registry fake.</summary>
    public FakeBuildingRegistry BuildingRegistry { get; }

    /// <summary>Gets the building lifecycle fake.</summary>
    public FakeBuildingLifecycle BuildingLifecycle { get; }

    /// <summary>Gets the dirty-chunk tracker fake.</summary>
    public FakeChunkDirtyTracker DirtyChunks { get; }

    /// <summary>Gets the goods pool, the same object as <see cref="FakeSimQueries.Storage"/>.</summary>
    public FakeStorage Storage { get; }

    /// <summary>Gets the progression service fake, which records the effects applied.</summary>
    public FakeProgressionService ProgressionService { get; }

    /// <summary>Gets the points ledger fake.</summary>
    public FakePointsLedger PointsLedger { get; }

    /// <summary>Gets the counters fake, which feeds <see cref="FakeSimQueries.Progression"/>.</summary>
    public FakeCounters Counters { get; }

    /// <summary>Gets the production buffers fake.</summary>
    public FakeProductionBuffers ProductionBuffers { get; }

    /// <summary>Gets the workforce supply fake, which feeds <see cref="FakeSimQueries.Workforce"/>.</summary>
    public FakeWorkforceSupply WorkforceSupply { get; }

    /// <summary>Bundles the fakes as the services a <see cref="SimContext"/> takes.</summary>
    /// <returns>The bundle.</returns>
    public SimServices ToSimServices() => new()
    {
        BuildingRegistry = BuildingRegistry,
        BuildingLifecycle = BuildingLifecycle,
        DirtyChunks = DirtyChunks,
        Storage = Storage,
        ProgressionService = ProgressionService,
        PointsLedger = PointsLedger,
        Counters = Counters,
        ProductionBuffers = ProductionBuffers,
        WorkforceSupply = WorkforceSupply,
    };
}

/// <summary>
/// A <see cref="SimContext"/> over fakes: the harness for testing one system (or one command handler) in isolation. It
/// holds the real world state and clock, the content, an event sink, <see cref="FakeSimQueries"/> for reading and
/// <see cref="FakeServices"/> for writing. Arrange the world through <see cref="Queries"/> and <see cref="Services"/>, call
/// the system with <see cref="Context"/>, then assert on the fakes and on <see cref="DrainEvents"/>.
/// </summary>
internal sealed class FakeSim
{
    private FakeSim(ContentDb content, WorldState world)
    {
        Content = content;
        World = world;
        Events = new EventSink();
        Queries = new FakeSimQueries(content.Goods.Count);
        Services = new FakeServices(content, Queries, world.EntityIds);
        Context = new SimContext(content, world, Queries, Events, Services.ToSimServices());
    }

    /// <summary>Gets the content.</summary>
    public ContentDb Content { get; }

    /// <summary>Gets the world state, for the system's own slice and the clock.</summary>
    public WorldState World { get; }

    /// <summary>Gets the event sink the context emits into.</summary>
    public EventSink Events { get; }

    /// <summary>Gets the fake queries.</summary>
    public FakeSimQueries Queries { get; }

    /// <summary>Gets the fake write services.</summary>
    public FakeServices Services { get; }

    /// <summary>Gets the context to hand to the system or handler under test.</summary>
    public SimContext Context { get; }

    /// <summary>Creates a fake world.</summary>
    /// <param name="content">The content; the minimal fixture pack by default.</param>
    /// <param name="seed">The world seed, which seeds the system random streams.</param>
    /// <param name="tick">The tick the clock starts at.</param>
    /// <returns>The harness.</returns>
    public static FakeSim Create(ContentDb? content = null, ulong seed = 42, long tick = 0)
    {
        ContentDb db = content ?? WorldBuilder.Content;
        var world = WorldState.CreateNew(db, seed);
        var sim = new FakeSim(db, world);
        sim.SetTick(tick);
        return sim;
    }

    /// <summary>Moves the clock to a tick, as the simulation's clock step would before the systems run.</summary>
    /// <param name="tick">The tick.</param>
    public void SetTick(long tick) => World.Clock = new GameClock(tick);

    /// <summary>Returns the events emitted since the last drain, and clears them.</summary>
    /// <returns>The events in emission order.</returns>
    public IReadOnlyList<SimEvent> DrainEvents() => Events.Drain();
}
