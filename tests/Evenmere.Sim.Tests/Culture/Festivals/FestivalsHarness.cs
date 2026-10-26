using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Tests.Fakes;

namespace Evenmere.Sim.Tests.Culture.Festivals;

/// <summary>
/// A tiny fixture world for the festival rules: the overlay content with the <c>test_harvest</c> and
/// <c>test_winter</c> festivals, the fake queries and services, and a <see cref="CultureSystem"/> over a fresh
/// <see cref="CultureState"/>. Arrange the world through <see cref="Sim"/>, queue commands with <see cref="Send"/>,
/// then run the day with <see cref="Run"/>. Each <see cref="Run"/> is one daily festival step.
/// </summary>
internal sealed class FestivalsHarness
{
    private readonly CommandQueue _commands = new();
    private readonly CommandRouter _router = new();

    private FestivalsHarness(FakeSim sim)
    {
        Sim = sim;
        System = new CultureSystem(Sim.World.Culture, Sim.Content, () => Sim.Queries);
        System.Register(_router);
    }

    /// <summary>Gets the fake world.</summary>
    public FakeSim Sim { get; }

    /// <summary>Gets the system under test.</summary>
    public CultureSystem System { get; }

    /// <summary>Gets the content.</summary>
    public ContentDb Content => Sim.Content;

    /// <summary>Gets the culture state.</summary>
    public CultureState State => Sim.World.Culture;

    /// <summary>Gets the queries under test: the real ones, over the fake map and progression.</summary>
    public ICultureQueries Queries => System.Queries;

    /// <summary>Creates a harness on the minimal overlay content.</summary>
    /// <param name="tick">The tick the clock starts at; pick an autumn tick to test the Harvest Fair bonus.</param>
    /// <returns>The harness, before any festival.</returns>
    public static FestivalsHarness Create(long tick = 0) => new(FakeSim.Create(FestivalsPack.Minimal, tick: tick));

    /// <summary>Creates a harness on the slice content plus the overlay.</summary>
    /// <param name="tick">The tick the clock starts at.</param>
    /// <returns>The harness, before any festival.</returns>
    public static FestivalsHarness CreateOnSlice(long tick = 0) => new(FakeSim.Create(FestivalsPack.Slice, tick: tick));

    /// <summary>
    /// Creates a harness over the state a save holds: the world is written and read back as a save would be.
    /// </summary>
    /// <param name="original">The harness whose world is saved.</param>
    /// <returns>The harness over the loaded state.</returns>
    public static FestivalsHarness Reload(FestivalsHarness original)
    {
        ArgumentNullException.ThrowIfNull(original);

        byte[] bytes = StateSerializer.Write(original.Sim.World);
        WorldState loaded = StateSerializer.Read(bytes, original.Content);

        var fake = FakeSim.Create(original.Content, tick: loaded.Clock.Tick);
        fake.World.Culture = loaded.Culture;
        // The ground building came from the world's ID allocator, which the swapped-in slice must keep up with.
        fake.World.EntityIds = loaded.EntityIds;
        var harness = new FestivalsHarness(fake);
        return harness;
    }

    /// <summary>Gets a festival's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public FestivalId Festival(string name) => Content.IdOf<FestivalId>(name);

    /// <summary>Gets a good's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public GoodId Good(string name) => Content.IdOf<GoodId>(name);

    /// <summary>Gets a building type's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public BuildingTypeId Building(string name) => Content.IdOf<BuildingTypeId>(name);

    /// <summary>Unlocks festivals, as civics would.</summary>
    /// <param name="names">The festival content IDs.</param>
    /// <returns>This harness.</returns>
    public FestivalsHarness Unlock(params string[] names)
    {
        Sim.Queries.Progression.UnlockFestivals(names.Select(Festival).ToArray());
        return this;
    }

    /// <summary>Sets the pool stock of a good.</summary>
    /// <param name="name">The good's content ID.</param>
    /// <param name="units">The stock in whole units.</param>
    /// <returns>This harness.</returns>
    public FestivalsHarness Stock(string name, long units)
    {
        Sim.Services.Storage.Set(Good(name), Amount.FromUnits(units));
        return this;
    }

    /// <summary>Adds a festival ground through the fake registry.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="type">The building type's content ID.</param>
    /// <param name="state">The registry state to add it in.</param>
    /// <returns>The building's entity ID.</returns>
    public EntityId AddGround(int x, int y, string type = "test_festival_ground", BuildingState state = BuildingState.Active) =>
        Sim.Services.BuildingRegistry.Add(Building(type), x, y, Rotation.Deg0, state);

    /// <summary>Moves the clock, as the simulation's clock step would.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>This harness.</returns>
    public FestivalsHarness SetTick(long tick)
    {
        Sim.SetTick(tick);
        return this;
    }

    /// <summary>Runs the daily culture step once, as the pipeline would at day start.</summary>
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
}
