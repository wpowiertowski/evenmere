using Evenmere.Content;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Diagnostics;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;

namespace Evenmere.Sim;

/// <summary>
/// The deterministic fixed-tick simulation (ADR-0003). One <see cref="Tick"/> runs the steps of architecture.md's
/// System order: the clock advances, queued commands are applied, then each ticked system runs on its cadence. The
/// same content, seed and commands always give the same state hash. It is not thread-safe: the host calls it from one
/// thread (ADR-0006).
/// </summary>
public sealed class Simulation : ISimulation, IProfilerReadout
{
    private readonly WorldState _world;
    private readonly CommandQueue _commands = new();
    private readonly CommandRouter _router = new();
    private readonly EventSink _events = new();
    private readonly SimProfiler _profiler = new();
    private readonly SystemPipeline _pipeline;
    private readonly SimContext _context;
    private readonly int _clockSlot;
    private readonly int _commandsSlot;

    private Simulation(ContentDb content, WorldState world)
    {
        _world = world;

        // The clock and command steps come before the pipeline's systems, so they take the first profile slots.
        _clockSlot = _profiler.Register("clock");
        _commandsSlot = _profiler.Register("commands");

        var roster = new SystemRoster(world, content);
        _pipeline = new SystemPipeline(roster.Entries, _profiler);
        _pipeline.RegisterCommands(_router);
        Query = roster.Queries;
        _context = new SimContext(content, world, roster.Queries, _events, roster.Services);
    }

    /// <inheritdoc />
    public long CurrentTick => _world.Clock.Tick;

    /// <inheritdoc />
    public ISimQueries Query { get; }

    /// <summary>Gets the world state, for tests and for M1-14's persistence. Hosts and views must not use it.</summary>
    internal WorldState World => _world;

    /// <summary>Gets the context systems receive, for tests.</summary>
    internal SimContext Context => _context;

    /// <summary>Gets the command router, so tests can register test-only commands.</summary>
    internal CommandRouter Router => _router;

    /// <summary>Gets the pipeline, so tests can inspect it and watch systems run.</summary>
    internal SystemPipeline Pipeline => _pipeline;

    /// <summary>Gets the number of commands queued and not yet applied.</summary>
    internal int PendingCommandCount => _commands.Count;

    /// <inheritdoc />
    public long TimerTicksPerSecond => _profiler.TimerTicksPerSecond;

    /// <summary>Creates an empty world at tick zero. The map comes in M1-01, so there is no island yet.</summary>
    /// <param name="content">The loaded content.</param>
    /// <param name="options">The new-game choices.</param>
    /// <returns>The simulation.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static Simulation CreateNew(ContentDb content, NewGameOptions options)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);
        return new Simulation(content, WorldState.CreateNew(content, options.Seed));
    }

    /// <summary>Restores a simulation from the bytes <see cref="SerializeState"/> produced; it continues exactly where that one stopped.</summary>
    /// <param name="content">The loaded content the state was saved with.</param>
    /// <param name="state">The serialised state.</param>
    /// <returns>The simulation.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a valid state, or do not fit the content.</exception>
    public static Simulation Restore(ContentDb content, ReadOnlyMemory<byte> state)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new Simulation(content, StateSerializer.Read(state, content));
    }

    /// <summary>
    /// Serialises the world state to its canonical, uncompressed bytes. Commands queued and not yet applied, and events not
    /// yet drained, are not part of the state, so save between ticks after the host has drained both.
    /// </summary>
    /// <returns>The bytes.</returns>
    public byte[] SerializeState() => StateSerializer.Write(_world);

    /// <inheritdoc />
    public void Enqueue(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Enqueue(command);
    }

    /// <inheritdoc />
    public void Tick()
    {
        // Step 0: advance the clock. Systems see the new tick, so the first tick they run is tick 1.
        long start = SimProfiler.Start();
        _world.Clock = _world.Clock.Advance();
        _profiler.Stop(_clockSlot, start);

        // Step 1: apply the commands queued before this tick began, in enqueue order.
        start = SimProfiler.Start();
        _router.ApplyQueued(_commands, _context);
        _profiler.Stop(_commandsSlot, start);

        // Steps 2 to 15: the ticked systems, each on its cadence.
        _pipeline.Run(_context);
    }

    /// <inheritdoc />
    public IReadOnlyList<SimEvent> DrainEvents() => _events.Drain();

    /// <inheritdoc />
    public ulong ComputeStateHash() => StateSerializer.ComputeHash(_world);

    /// <inheritdoc />
    public IReadOnlyList<SystemTiming> GetTimings() => _profiler.GetTimings();

    /// <inheritdoc />
    public void ResetTimings() => _profiler.ResetTimings();
}
