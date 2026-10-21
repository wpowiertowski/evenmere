using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Logistics;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Logistics;

/// <summary>
/// A tiny fixture world for the logistics system: the real vertical-slice content, the fake queries and services, and
/// a <see cref="LogisticsSystem"/> over a fresh <see cref="LogisticsState"/> that listens to the fake building
/// lifecycle. Arrange storehouses, producers and requests, then step the system with <see cref="RunTicks"/>.
/// </summary>
internal sealed class LogisticsHarness
{
    private static readonly Lazy<ContentDb> _sliceContent =
        new(static () => ContentLoader.Load(Path.Combine(WorldBuilder.RepoRoot, "content")).GetDbOrThrow());

    private LogisticsHarness(FakeSim sim)
    {
        Sim = sim;
    }

    /// <summary>Gets the real content of the vertical slice, loaded once.</summary>
    public static ContentDb SliceContent => _sliceContent.Value;

    /// <summary>Gets the fake world.</summary>
    public FakeSim Sim { get; }

    /// <summary>Gets the system under test; it exists once <see cref="Start"/> has run.</summary>
    public LogisticsSystem System { get; private set; } = null!;

    /// <summary>Gets the context the system runs with, whose services are the fakes.</summary>
    public SimContext Context => Sim.Context;

    /// <summary>Gets the content.</summary>
    public ContentDb Content => Sim.Content;

    /// <summary>Gets the logistics state.</summary>
    public LogisticsState State => Sim.World.Logistics;

    /// <summary>Gets a good's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public GoodId Good(string name) => Content.IdOf<GoodId>(name);

    /// <summary>Gets how much one cart carries.</summary>
    public Amount CartLoad => Content.Rules.CartLoad;

    /// <summary>Gets or sets the test producer's ID, so tests can snapshot its buffers.</summary>
    public EntityId Hut { get; set; }

    /// <summary>Gets or sets the test consumer's ID, so tests can snapshot its buffers.</summary>
    public EntityId Bakery { get; set; }

    /// <summary>Creates a harness and starts the system.</summary>
    /// <param name="content">The content; the vertical slice by default.</param>
    /// <returns>The harness, at tick 0.</returns>
    public static LogisticsHarness Create(ContentDb? content = null)
    {
        var harness = new LogisticsHarness(FakeSim.Create(content ?? SliceContent));
        harness.Start();
        return harness;
    }

    /// <summary>Creates a harness without a system, so buildings can be added before it listens.</summary>
    /// <param name="content">The content; the vertical slice by default.</param>
    /// <returns>The harness, at tick 0.</returns>
    public static LogisticsHarness CreateUnstarted(ContentDb? content = null) => new(FakeSim.Create(content ?? SliceContent));

    /// <summary>Creates the system over <see cref="State"/> (or a restored state) and subscribes it to the lifecycle.</summary>
    /// <param name="restored">A logistics state read from a save, which replaces the world's own.</param>
    public void Start(LogisticsState? restored = null)
    {
        if (restored is not null)
        {
            Sim.World.Logistics = restored;
        }

        System = new LogisticsSystem(Sim.World.Logistics, Content, () => Sim.Queries);
        Sim.Services.BuildingLifecycle.Subscribe(System);
    }

    /// <summary>Adds a building through the fake registry, which tells the lifecycle listeners.</summary>
    /// <param name="type">The building type's content ID.</param>
    /// <param name="x">The column of the footprint's top-left tile.</param>
    /// <param name="y">The row of the footprint's top-left tile.</param>
    /// <param name="state">The registry state to add it in.</param>
    /// <returns>The building's entity ID.</returns>
    public EntityId Add(string type, int x, int y, BuildingState state = BuildingState.Active) =>
        Sim.Services.BuildingRegistry.Add(Content.IdOf<BuildingTypeId>(type), x, y, Rotation.Deg0, state);

    /// <summary>Puts a building inside a storehouse's reach at a road cost.</summary>
    /// <param name="storehouse">The storehouse.</param>
    /// <param name="building">The building.</param>
    /// <param name="cost">The road cost in cost units (1000 = one dirt tile).</param>
    public void SetReach(EntityId storehouse, EntityId building, int cost) => Sim.Queries.Roads.SetReach(storehouse, building, cost);

    /// <summary>Opens a pickup request for a building's output buffer.</summary>
    /// <param name="building">The producer.</param>
    /// <param name="good">The good's content ID.</param>
    public void OpenPickup(EntityId building, string good) =>
        Sim.Services.ProductionBuffers.AddRequest(
            new TransportRequest(building, TransportDirection.Pickup, Good(good), CartLoad, Sim.World.Clock.Tick));

    /// <summary>Opens a delivery request for a building's input buffer.</summary>
    /// <param name="building">The consumer.</param>
    /// <param name="good">The good's content ID.</param>
    public void OpenDelivery(EntityId building, string good) =>
        Sim.Services.ProductionBuffers.AddRequest(
            new TransportRequest(building, TransportDirection.Delivery, Good(good), CartLoad, Sim.World.Clock.Tick));

    /// <summary>Puts goods in a building's output buffer.</summary>
    /// <param name="building">The producer.</param>
    /// <param name="good">The good's content ID.</param>
    /// <param name="amount">The amount in milli-units.</param>
    public void SetOutput(EntityId building, string good, Amount amount) =>
        Sim.Services.ProductionBuffers.SetOutput(building, Good(good), amount);

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

    /// <summary>Gets the active jobs as a copy, in ascending job-ID order.</summary>
    /// <returns>The job views.</returns>
    public TransportJobView[] Jobs() => System.Queries.ActiveJobs().ToArray();

    /// <summary>Returns the events emitted since the last call.</summary>
    /// <returns>The events in emission order.</returns>
    public IReadOnlyList<SimEvent> Events() => Sim.DrainEvents();
}
