using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Happenings;
using Evenmere.Sim.Tests.Fakes;

namespace Evenmere.Sim.Tests.Happenings;

/// <summary>
/// A tiny fixture world for the happening rules: the overlay content with the four test happenings, the fake queries
/// and services, and a <see cref="HappeningsSystem"/> over a fresh <see cref="HappeningsState"/>. Conditions evaluate
/// to false until a test allows them (see <see cref="EligibleOnly"/>). Arrange the world through <see cref="Sim"/>,
/// queue commands with <see cref="Send"/>, then run the day with <see cref="Run"/>. Each <see cref="Run"/> is one
/// daily happening step at the current tick.
/// </summary>
internal sealed class HappeningsHarness
{
    private readonly CommandQueue _commands = new();
    private readonly CommandRouter _router = new();

    private HappeningsHarness(FakeSim sim)
    {
        Sim = sim;
        System = new HappeningsSystem(Sim.World.Happenings, Sim.Content, () => Sim.World.Clock);
        System.Register(_router);
    }

    /// <summary>Gets the fake world.</summary>
    public FakeSim Sim { get; }

    /// <summary>Gets the system under test.</summary>
    public HappeningsSystem System { get; }

    /// <summary>Gets the content.</summary>
    public ContentDb Content => Sim.Content;

    /// <summary>Gets the happenings state.</summary>
    public HappeningsState State => Sim.World.Happenings;

    /// <summary>Gets the queries under test: the real ones, over the fake clock.</summary>
    public IHappeningsQueries Queries => System.Queries;

    /// <summary>Creates a harness on the overlay content at tick zero (day zero, spring).</summary>
    /// <returns>The harness, before any happening.</returns>
    public static HappeningsHarness Create() => new(FakeSim.Create(HappeningsPack.Content));

    /// <summary>
    /// Creates a harness over the state a save holds: the world is written and read back as a save would be.
    /// </summary>
    /// <param name="original">The harness whose world is saved.</param>
    /// <returns>The harness over the loaded state.</returns>
    public static HappeningsHarness Reload(HappeningsHarness original)
    {
        ArgumentNullException.ThrowIfNull(original);

        byte[] bytes = StateSerializer.Write(original.Sim.World);
        WorldState loaded = StateSerializer.Read(bytes, original.Content);

        var fake = FakeSim.Create(original.Content, tick: loaded.Clock.Tick);
        fake.World.Happenings = loaded.Happenings;
        fake.World.SystemRngs = loaded.SystemRngs;
        // The pool and entity IDs came from the world's allocator, which the swapped-in slice must keep up with.
        fake.World.EntityIds = loaded.EntityIds;
        return new HappeningsHarness(fake);
    }

    /// <summary>Gets a happening's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public HappeningId Happening(string name) => Content.IdOf<HappeningId>(name);

    /// <summary>Gets a good's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public GoodId Good(string name) => Content.IdOf<GoodId>(name);

    /// <summary>
    /// Makes exactly the named happenings eligible: every other trigger evaluates to false, so rolls never touch
    /// them. Uses reference equality on the content condition instances, which the loader shares.
    /// </summary>
    /// <param name="names">The happening content IDs that may roll.</param>
    /// <returns>This harness.</returns>
    public HappeningsHarness EligibleOnly(params string[] names)
    {
        var triggers = new HashSet<Condition>(ReferenceEqualityComparer.Instance);
        foreach (string name in names)
        {
            triggers.Add(Content.Get(Happening(name)).Trigger);
        }

        Sim.Queries.Progression.ConditionEvaluator = triggers.Contains;
        return this;
    }

    /// <summary>Moves the clock, as the simulation's clock step would.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>This harness.</returns>
    public HappeningsHarness SetTick(long tick)
    {
        Sim.SetTick(tick);
        return this;
    }

    /// <summary>Runs one daily happening step at the current tick, as the pipeline would at day start.</summary>
    public void Run()
    {
        Sim.Context.CurrentSystem = SystemId.Happenings;
        System.Run(Sim.Context);
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
}
