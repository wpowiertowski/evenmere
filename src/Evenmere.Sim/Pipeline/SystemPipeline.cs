using Evenmere.Core.Time;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Diagnostics;

namespace Evenmere.Sim.Pipeline;

/// <summary>
/// The systems in tick order, each with its cadence (architecture.md, System order). The clock and command steps
/// come first and belong to <see cref="Simulation"/>; the pipeline runs steps 2 to 15. Services appear in the list
/// with <see cref="Cadence.None"/> so their command handlers are registered, but they are never ticked.
/// </summary>
internal sealed class SystemPipeline
{
    private readonly PipelineEntry[] _entries;
    private readonly int[] _profilerSlots;
    private readonly SimProfiler _profiler;

    /// <summary>Initializes a new instance of the <see cref="SystemPipeline"/> class.</summary>
    /// <param name="entries">Every system, services first, then the ticked systems in tick order.</param>
    /// <param name="profiler">Receives each ticked system's elapsed time.</param>
    /// <exception cref="ArgumentException">A system appears twice, or a system's name differs from its ID's name.</exception>
    public SystemPipeline(IReadOnlyList<PipelineEntry> entries, SimProfiler profiler)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(profiler);

        _entries = [.. entries];
        _profiler = profiler;
        _profilerSlots = new int[_entries.Length];

        bool[] seen = new bool[SystemNames.Count];
        for (int i = 0; i < _entries.Length; i++)
        {
            PipelineEntry entry = _entries[i];
            string expected = SystemNames.Of(entry.Id);
            if (entry.System.Name != expected)
            {
                throw new ArgumentException($"The system registered as {entry.Id} is named '{entry.System.Name}', not '{expected}'.", nameof(entries));
            }

            if (seen[(int)entry.Id])
            {
                throw new ArgumentException($"{entry.Id} appears twice in the pipeline.", nameof(entries));
            }

            seen[(int)entry.Id] = true;
            _profilerSlots[i] = entry.Cadence == Cadence.None ? -1 : profiler.Register(expected);
        }
    }

    /// <summary>Gets every system in pipeline order: services first, then the ticked systems in tick order.</summary>
    public IReadOnlyList<PipelineEntry> Entries => _entries;

    /// <summary>Lets every system register its command handlers, in pipeline order.</summary>
    /// <param name="router">The router to register with.</param>
    public void RegisterCommands(CommandRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        try
        {
            foreach (PipelineEntry entry in _entries)
            {
                router.SetRegisteringSystem(entry.Id);
                entry.System.Register(router);
            }
        }
        finally
        {
            router.SetRegisteringSystem(null);
        }
    }

    /// <summary>Runs every system whose cadence is due on the context's current tick, in pipeline order.</summary>
    /// <param name="ctx">The simulation context.</param>
    public void Run(SimContext ctx)
    {
        GameClock clock = ctx.Clock;
        for (int i = 0; i < _entries.Length; i++)
        {
            PipelineEntry entry = _entries[i];
            if (!IsDue(entry.Cadence, clock))
            {
                continue;
            }

            ctx.CurrentSystem = entry.Id;
            long start = SimProfiler.Start();
            entry.System.Run(ctx);
            _profiler.Stop(_profilerSlots[i], start);
        }
    }

    /// <summary>Replaces a system and keeps its cadence. A seam for tests that watch the pipeline run; production code never calls it.</summary>
    /// <param name="id">The system to replace.</param>
    /// <param name="replacement">The system to run in its place; its name must match.</param>
    public void Replace(SystemId id, ISimSystem replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.Name != SystemNames.Of(id))
        {
            throw new ArgumentException($"The replacement for {id} must be named '{SystemNames.Of(id)}'.", nameof(replacement));
        }

        for (int i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].Id == id)
            {
                _entries[i] = _entries[i] with { System = replacement };
                return;
            }
        }

        throw new ArgumentException($"{id} is not in the pipeline.", nameof(id));
    }

    /// <summary>Tests whether a system with the given cadence runs on the clock's current tick.</summary>
    /// <param name="cadence">The cadence.</param>
    /// <param name="clock">The clock as of the tick being run.</param>
    /// <returns><see langword="true"/> if the system is due.</returns>
    internal static bool IsDue(Cadence cadence, GameClock clock) => cadence switch
    {
        Cadence.EveryTick => true,
        Cadence.DailyBucketed => true,
        Cadence.Daily => clock.IsDayStart,
        _ => false,
    };
}
