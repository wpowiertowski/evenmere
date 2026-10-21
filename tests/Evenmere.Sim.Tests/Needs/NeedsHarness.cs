using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Needs;
using Evenmere.Sim.Systems.Population;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Needs;

/// <summary>
/// A tiny fixture island for the needs and population systems: the real vertical-slice content (or a caller-supplied
/// pack), the fake queries and services, and a <see cref="NeedsSystem"/> plus <see cref="PopulationSystem"/> pair run in
/// pipeline order, with the population system listening to the fake building lifecycle. Arrange the island through
/// <see cref="Sim"/>, add houses through <see cref="AddHouse"/>, then step both systems with <see cref="RunTicks"/>.
/// </summary>
internal sealed class NeedsHarness
{
    private static readonly Lazy<ContentDb> _sliceContent =
        new(static () => ContentLoader.Load(Path.Combine(WorldBuilder.RepoRoot, "content")).GetDbOrThrow());

    private readonly CommandQueue _commands = new();
    private readonly CommandRouter _router = new();
    private int _nextSlot;

    private NeedsHarness(FakeSim sim)
    {
        Sim = sim;
        Needs = new NeedsSystem(Sim.World.Needs, Sim.World.Population, Content);
        Population = new PopulationSystem(Sim.World.Population, Content);
        Population.Register(_router);
        Sim.Services.BuildingLifecycle.Subscribe(Population);
    }

    /// <summary>Gets the real content of the vertical slice, loaded once.</summary>
    public static ContentDb SliceContent => _sliceContent.Value;

    /// <summary>Gets the fake world.</summary>
    public FakeSim Sim { get; }

    /// <summary>Gets the system under test that consumes goods and writes the need masks.</summary>
    public NeedsSystem Needs { get; }

    /// <summary>Gets the system under test that grows houses, advances and declines them.</summary>
    public PopulationSystem Population { get; }

    /// <summary>Gets the content.</summary>
    public ContentDb Content => Sim.Content;

    /// <summary>Creates a harness on the slice content.</summary>
    /// <returns>The harness, at tick 0.</returns>
    public static NeedsHarness Create() => Create(SliceContent);

    /// <summary>Creates a harness on the given content.</summary>
    /// <param name="content">The content.</param>
    /// <returns>The harness, at tick 0.</returns>
    public static NeedsHarness Create(ContentDb content) => new(FakeSim.Create(content));

    /// <summary>Gets a good's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public GoodId Good(string name) => Content.IdOf<GoodId>(name);

    /// <summary>Gets a tier's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public TierId Tier(string name) => Content.IdOf<TierId>(name);

    /// <summary>Adds a settler house through the fake registry, which tells the lifecycle listeners.</summary>
    /// <returns>The house's entity ID.</returns>
    public EntityId AddHouse() => AddHouse("settler_house");

    /// <summary>Adds a residence through the fake registry, which tells the lifecycle listeners.</summary>
    /// <param name="type">The building type's content ID.</param>
    /// <returns>The house's entity ID.</returns>
    public EntityId AddHouse(string type)
    {
        int x = (_nextSlot * 4) % 28;
        int y = (_nextSlot * 4) / 28 * 4;
        _nextSlot++;
        return Sim.Services.BuildingRegistry.Add(Content.IdOf<BuildingTypeId>(type), x, y, Rotation.Deg0, BuildingState.Active);
    }

    /// <summary>Adds a non-residence building through the fake registry.</summary>
    /// <param name="type">The building type's content ID.</param>
    /// <returns>The building's entity ID.</returns>
    public EntityId AddBuilding(string type)
    {
        int x = (_nextSlot * 4) % 28;
        int y = (_nextSlot * 4) / 28 * 4;
        _nextSlot++;
        return Sim.Services.BuildingRegistry.Add(Content.IdOf<BuildingTypeId>(type), x, y, Rotation.Deg0, BuildingState.Active);
    }

    /// <summary>Covers a house with a service.</summary>
    /// <param name="house">The house.</param>
    /// <param name="service">The service.</param>
    /// <returns>This harness.</returns>
    public NeedsHarness Cover(EntityId house, ServiceKind service)
    {
        Sim.Queries.Roads.SetCovered(house, service);
        return this;
    }

    /// <summary>Sets the pool stock of a good.</summary>
    /// <param name="name">The good's content ID.</param>
    /// <param name="amount">The stock.</param>
    /// <returns>This harness.</returns>
    public NeedsHarness Stock(string name, Amount amount)
    {
        Sim.Queries.Storage.Set(Good(name), amount);
        return this;
    }

    /// <summary>Sets what Production made of a good yesterday, which counts towards the fair shares.</summary>
    /// <param name="name">The good's content ID.</param>
    /// <param name="amount">The amount.</param>
    /// <returns>This harness.</returns>
    public NeedsHarness ProducedYesterday(string name, Amount amount)
    {
        Sim.Queries.Production.SetProducedYesterday(Good(name), amount);
        return this;
    }

    /// <summary>Unlocks a tier.</summary>
    /// <param name="name">The tier's content ID.</param>
    /// <returns>This harness.</returns>
    public NeedsHarness UnlockTier(string name)
    {
        Sim.Queries.Progression.Unlock(Tier(name));
        return this;
    }

    /// <summary>Runs both systems for a number of ticks, advancing the clock before each, as the simulation does.</summary>
    /// <param name="ticks">How many ticks.</param>
    public void RunTicks(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            Sim.SetTick(Sim.World.Clock.Tick + 1);
            Needs.Run(Sim.Context);
            Population.Run(Sim.Context);
        }
    }

    /// <summary>Runs both systems for whole days.</summary>
    /// <param name="days">How many days.</param>
    public void RunDays(int days) => RunTicks(120 * days);

    /// <summary>Queues a command and applies it, as the start of a tick would.</summary>
    /// <param name="command">The command.</param>
    public void Send(Command command)
    {
        _commands.Enqueue(command);
        _router.ApplyQueued(_commands, Sim.Context);
    }

    /// <summary>Returns the rejection emitted for the last command sent, if any.</summary>
    /// <returns>The rejection, or <see langword="null"/> when the command applied.</returns>
    public CommandRejected? Rejection() => Sim.DrainEvents().OfType<CommandRejected>().SingleOrDefault();

    /// <summary>Returns the events emitted since the last call.</summary>
    /// <returns>The events in emission order.</returns>
    public IReadOnlyList<SimEvent> Events() => Sim.DrainEvents();

    /// <summary>Gets what the population queries say about a house.</summary>
    /// <param name="house">The house.</param>
    /// <returns>The description.</returns>
    public HouseInfo House(EntityId house)
    {
        Population.Queries.TryGetHouse(house, out HouseInfo info).ShouldBeTrue();
        return info;
    }

    /// <summary>Gets what the needs queries say about a house.</summary>
    /// <param name="house">The house.</param>
    /// <returns>The description.</returns>
    public HouseNeedsInfo HouseNeeds(EntityId house)
    {
        Needs.Queries.TryGetHouseNeeds(house, out HouseNeedsInfo needs).ShouldBeTrue();
        return needs;
    }
}
