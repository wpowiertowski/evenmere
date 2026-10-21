using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Advisors;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Advisors;

/// <summary>
/// A tiny fixture world for the advisors system: the minimal content pack, the fake queries and services, and a real
/// <see cref="AdvisorsSystem"/> over a fresh <see cref="AdvisorsState"/>. Arrange the world through
/// <see cref="Sim"/>, run the daily evaluation with <see cref="Run"/> (advancing whole days with
/// <see cref="AdvanceDays"/>), queue dismissals with <see cref="Send"/>, then assert on the queries and
/// <see cref="Events"/>.
/// </summary>
internal sealed class AdvisorsHarness
{
    private readonly CommandQueue _commands = new();
    private readonly CommandRouter _router = new();

    private AdvisorsHarness(FakeSim sim)
    {
        Sim = sim;
    }

    /// <summary>Gets the fake world.</summary>
    public FakeSim Sim { get; }

    /// <summary>Gets the system under test.</summary>
    public AdvisorsSystem System { get; private set; } = null!;

    /// <summary>Gets the content.</summary>
    public ContentDb Content => Sim.Content;

    /// <summary>Gets the advisors state.</summary>
    public AdvisorsState State => Sim.World.Advisors;

    /// <summary>Gets the queries under test: the real ones, over the state.</summary>
    public IAdvisorsQueries Queries => System.Queries;

    /// <summary>Creates a harness and starts the system.</summary>
    /// <param name="tick">The tick the clock starts at.</param>
    /// <returns>The harness, before any evaluation.</returns>
    public static AdvisorsHarness Create(long tick = 0)
    {
        var harness = new AdvisorsHarness(FakeSim.Create(WorldBuilder.Content, tick: tick));
        harness.Start();
        return harness;
    }

    /// <summary>Gets a good's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public GoodId Good(string name) => Content.IdOf<GoodId>(name);

    /// <summary>Gets a tier's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public TierId Tier(string name) => Content.IdOf<TierId>(name);

    /// <summary>Gets a building type's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public BuildingTypeId Building(string name) => Content.IdOf<BuildingTypeId>(name);

    /// <summary>Adds a building through the fake registry.</summary>
    /// <param name="type">The building type's content ID.</param>
    /// <param name="x">The column of the footprint's top-left tile.</param>
    /// <param name="y">The row of the footprint's top-left tile.</param>
    /// <param name="state">The registry state to add it in.</param>
    /// <returns>The building's entity ID.</returns>
    public EntityId Add(string type, int x, int y, BuildingState state = BuildingState.Active) =>
        Sim.Services.BuildingRegistry.Add(Building(type), x, y, Rotation.Deg0, state);

    /// <summary>Adds a storehouse with road access and puts a building in its reach.</summary>
    /// <param name="building">The building to reach.</param>
    /// <param name="x">The column of the storehouse's top-left tile.</param>
    /// <param name="y">The row of the storehouse's top-left tile.</param>
    /// <returns>The storehouse's entity ID.</returns>
    public EntityId AddReachedStorehouse(EntityId building, int x, int y)
    {
        EntityId hall = Add("storehouse", x, y);
        Sim.Queries.Roads.GrantRoadAccess(hall);
        Sim.Queries.Roads.SetReach(hall, building, 1000);
        return hall;
    }

    /// <summary>Runs the daily advisors evaluation once, as the pipeline would at day start.</summary>
    public void Run() => System.Run(Sim.Context);

    /// <summary>Advances whole days, running the evaluation at each day start.</summary>
    /// <param name="days">How many days to advance.</param>
    public void AdvanceDays(int days)
    {
        for (int day = 0; day < days; day++)
        {
            Sim.SetTick(Sim.World.Clock.Tick + Core.Time.GameTime.TicksPerDay);
            Run();
        }
    }

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

    /// <summary>Returns the advice that is showing now.</summary>
    /// <returns>The active advice, in display order.</returns>
    public AdviceView[] Active() => Queries.Active().ToArray();

    /// <summary>Creates a harness over the state a save holds: the world is written and read back as a save would be.</summary>
    /// <param name="original">The harness whose world is saved. Its fake query arrangements are not part of the world and must be re-applied.</param>
    /// <returns>The harness over the loaded state.</returns>
    public static AdvisorsHarness Reload(AdvisorsHarness original)
    {
        ArgumentNullException.ThrowIfNull(original);

        byte[] bytes = StateSerializer.Write(original.Sim.World);
        WorldState loaded = StateSerializer.Read(bytes, original.Content);

        var fake = FakeSim.Create(original.Content, tick: loaded.Clock.Tick);
        fake.World.Advisors = loaded.Advisors;
        fake.World.EntityIds = loaded.EntityIds;
        var harness = new AdvisorsHarness(fake);
        harness.Start();
        return harness;
    }

    private void Start()
    {
        System = new AdvisorsSystem(Sim.World.Advisors, Content);
        System.Register(_router);
    }
}
