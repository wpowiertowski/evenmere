using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Research;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Research;

/// <summary>
/// A tiny fixture world for the research system: the overlay content with the 60-knowledge <c>test_theory</c>, the fake
/// queries and services, and a <see cref="ResearchSystem"/> over a fresh <see cref="ResearchState"/>. Arrange the world
/// through <see cref="Sim"/>, credit knowledge through <see cref="CreditKnowledge"/>, queue commands with
/// <see cref="Send"/>, then run the day with <see cref="Run"/>.
/// </summary>
internal sealed class ResearchHarness
{
    private readonly CommandQueue _commands = new();
    private readonly CommandRouter _router = new();

    private ResearchHarness(FakeSim sim)
    {
        Sim = sim;
    }

    /// <summary>Gets the fake world.</summary>
    public FakeSim Sim { get; }

    /// <summary>Gets the system under test.</summary>
    public ResearchSystem System { get; private set; } = null!;

    /// <summary>Gets the content.</summary>
    public ContentDb Content => Sim.Content;

    /// <summary>Gets the research state.</summary>
    public ResearchState State => Sim.World.Research;

    /// <summary>Gets the queries under test: the real ones, over the fake map and progression.</summary>
    public IResearchQueries Queries => System.Queries;

    /// <summary>Creates a harness and starts the system.</summary>
    /// <param name="tick">The tick the clock starts at.</param>
    /// <returns>The harness, before any accrual.</returns>
    public static ResearchHarness Create(long tick = 0)
    {
        var harness = new ResearchHarness(FakeSim.Create(ResearchPack.Content, tick: tick));
        harness.Start();
        return harness;
    }

    /// <summary>Gets a tech's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public TechId Tech(string name) => Content.IdOf<TechId>(name);

    /// <summary>Gets a building type's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public BuildingTypeId Building(string name) => Content.IdOf<BuildingTypeId>(name);

    /// <summary>Gets an era's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public EraId Era(string name) => Content.IdOf<EraId>(name);

    /// <summary>Moves the island to an era, as M2-11 will.</summary>
    /// <param name="name">The era's content ID.</param>
    public void SetEra(string name) => Sim.Queries.Progression.CurrentEra = Era(name);

    /// <summary>Credits knowledge to the ledger, as Population and other systems do through the day.</summary>
    /// <param name="units">The whole units of knowledge.</param>
    public void CreditKnowledge(long units) => Sim.Services.PointsLedger.Credit(PointKind.Knowledge, Amount.FromUnits(units));

    /// <summary>Adds a building through the fake registry.</summary>
    /// <param name="type">The building type's content ID.</param>
    /// <param name="x">The column of the footprint's top-left tile.</param>
    /// <param name="y">The row of the footprint's top-left tile.</param>
    /// <param name="state">The registry state to add it in.</param>
    /// <returns>The building's entity ID.</returns>
    public EntityId Add(string type, int x, int y, BuildingState state = BuildingState.Active) =>
        Sim.Services.BuildingRegistry.Add(Building(type), x, y, Rotation.Deg0, state);

    /// <summary>Runs the daily research step once, as the pipeline would at day start.</summary>
    public void Run() => System.Run(Sim.Context);

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

    /// <summary>Hashes the whole world state, so determinism tests compare the run's footprint exactly.</summary>
    /// <returns>The state hash.</returns>
    public ulong Hash() => StateSerializer.ComputeHash(Sim.World);

    /// <summary>Creates a harness over the state a save holds: the world is written and read back as a save would be.</summary>
    /// <param name="original">The harness whose world is saved.</param>
    /// <returns>The harness over the loaded state.</returns>
    public static ResearchHarness Reload(ResearchHarness original)
    {
        ArgumentNullException.ThrowIfNull(original);

        byte[] bytes = StateSerializer.Write(original.Sim.World);
        WorldState loaded = StateSerializer.Read(bytes, original.Content);

        var fake = FakeSim.Create(original.Content, tick: loaded.Clock.Tick);
        fake.World.Research = loaded.Research;
        var harness = new ResearchHarness(fake);
        harness.Start();
        return harness;
    }

    private void Start()
    {
        System = new ResearchSystem(Sim.World.Research, Content, () => Sim.Queries);
        System.Register(_router);
        SetEra("founding");
    }
}
