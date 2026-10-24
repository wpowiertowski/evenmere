using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Workforce;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Workforce;

/// <summary>
/// A tiny fixture world for the workforce system: the slice content with a five-worker workshop, the fake queries and
/// services, and a <see cref="WorkforceSystem"/> over a fresh <see cref="WorkforceState"/> that listens to the fake
/// building lifecycle. Arrange the map and modifiers through <see cref="Sim"/>, publish supply through
/// <see cref="Supply"/>, add buildings through <see cref="Add"/>, then allocate with <see cref="Allocate"/>.
/// </summary>
internal sealed class WorkforceHarness
{
    private readonly CommandQueue _commands = new();
    private readonly CommandRouter _router = new();

    private WorkforceHarness(FakeSim sim)
    {
        Sim = sim;
    }

    /// <summary>Gets the fake world.</summary>
    public FakeSim Sim { get; }

    /// <summary>Gets the system under test.</summary>
    public WorkforceSystem System { get; private set; } = null!;

    /// <summary>Gets the content.</summary>
    public ContentDb Content => Sim.Content;

    /// <summary>Gets the workforce state.</summary>
    public WorkforceState State => Sim.World.Workforce;

    /// <summary>Gets the supply service under test: the real one, not the fake.</summary>
    public IWorkforceSupply Supply => System.Supply;

    /// <summary>Gets the queries under test: the real ones, over the fake map and progression.</summary>
    public IWorkforceQueries Queries => System.Queries;

    /// <summary>Creates a harness and starts the system.</summary>
    /// <returns>The harness, before any allocation.</returns>
    public static WorkforceHarness Create() => Create(ExtraContent.Db);

    /// <summary>Creates a harness on the given content and starts the system.</summary>
    /// <param name="content">The content, which must define the <c>test_workshop</c> building the tests arrange with.</param>
    /// <returns>The harness, before any allocation.</returns>
    public static WorkforceHarness Create(ContentDb content)
    {
        var harness = new WorkforceHarness(FakeSim.Create(content));
        harness.Start();
        return harness;
    }

    /// <summary>Gets a tier's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public TierId Tier(string name) => Content.IdOf<TierId>(name);

    /// <summary>Gets a building type's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public BuildingTypeId Type(string name) => Content.IdOf<BuildingTypeId>(name);

    /// <summary>Adds a building through the fake registry, which tells the lifecycle listeners.</summary>
    /// <param name="type">The building type's content ID.</param>
    /// <param name="x">The column of the footprint's top-left tile.</param>
    /// <param name="y">The row of the footprint's top-left tile.</param>
    /// <param name="state">The registry state to add it in.</param>
    /// <returns>The building's entity ID.</returns>
    public EntityId Add(string type, int x, int y, BuildingState state = BuildingState.Active) =>
        Sim.Services.BuildingRegistry.Add(Type(type), x, y, Rotation.Deg0, state);

    /// <summary>Publishes a tier's supply through the real supply service.</summary>
    /// <param name="tier">The tier's content ID.</param>
    /// <param name="workers">The number of workers.</param>
    public void SetSupply(string tier, int workers) => Supply.SetSupply(Tier(tier), workers);

    /// <summary>Runs the daily allocation once, as the pipeline would at day start.</summary>
    public void Allocate() => System.Run(Sim.Context);

    /// <summary>Gets the workers the last allocation assigned to a building.</summary>
    /// <param name="building">The building.</param>
    /// <returns>The assigned workers, or 0 when the building has no record.</returns>
    public int Allocated(EntityId building) =>
        State.Workplaces.TryGet(building, out WorkplaceRecord? record) ? record.Allocated : 0;

    /// <summary>Queues a command and applies it, as the start of a tick would.</summary>
    /// <param name="command">The command.</param>
    public void Send(Command command)
    {
        _commands.Enqueue(command);
        _router.ApplyQueued(_commands, Sim.Context);
    }

    /// <summary>Returns the events emitted since the last call.</summary>
    /// <returns>The events in emission order.</returns>
    public IReadOnlyList<SimEvent> Events() => Sim.DrainEvents();

    /// <summary>Hashes the whole world state, so determinism tests compare the allocation's footprint exactly.</summary>
    /// <returns>The state hash.</returns>
    public ulong Hash() => StateSerializer.ComputeHash(Sim.World);

    private void Start()
    {
        System = new WorkforceSystem(Sim.World.Workforce, Content, () => Sim.Queries);
        System.Register(_router);
        Sim.Services.BuildingLifecycle.Subscribe(System);
    }
}
