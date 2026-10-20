using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Rng;
using Evenmere.Core.Time;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Systems.Storage;
using Evenmere.Sim.State;

namespace Evenmere.Sim.Pipeline;

/// <summary>
/// Everything a system or command handler needs while it runs: the immutable content, the world state, the clock, the
/// event sink, the read-only queries and the current system's random stream. One context lives as long as its
/// simulation; the pipeline points <see cref="CurrentSystem"/> at the system it is about to run.
/// </summary>
public sealed class SimContext
{
    internal SimContext(ContentDb content, WorldState world, ISimQueries queries, EventSink events, StoragePool storage)
    {
        Content = content;
        World = world;
        Queries = queries;
        Events = events;
        Storage = storage;
    }

    /// <summary>Gets the immutable game content.</summary>
    public ContentDb Content { get; }

    /// <summary>Gets the world state. A system writes only its own slice and reads the others through <see cref="Queries"/>.</summary>
    public WorldState World { get; }

    /// <summary>Gets the read-only queries over every system.</summary>
    public ISimQueries Queries { get; }

    /// <summary>Gets the sink that collects events for the host.</summary>
    public EventSink Events { get; }

    /// <summary>Gets the clock as of the current tick.</summary>
    public GameClock Clock => World.Clock;

    /// <summary>Gets the current tick, which is the tick count the clock holds while systems run.</summary>
    public long Tick => World.Clock.Tick;

    /// <summary>
    /// Gets the current system's random stream by reference, so a draw advances the generator stored in the world
    /// state. Keep the reference or call methods on this property directly; copying it into a local variable
    /// would advance only the copy and break determinism.
    /// </summary>
    public ref Pcg32 Rng => ref World.SystemRngs[(int)CurrentSystem];

    /// <summary>Gets the island storage pool, the write side of <see cref="IStorageQueries"/>. Other services will join it here.</summary>
    internal StoragePool Storage { get; }

    /// <summary>Gets or sets the system whose stream <see cref="Rng"/> returns; the pipeline and the command router set it.</summary>
    internal SystemId CurrentSystem { get; set; }

    /// <summary>Tests whether a daily-bucketed system should process an entity on this tick (<c>id % 120 == tick % 120</c>).</summary>
    /// <param name="id">The entity.</param>
    /// <returns><see langword="true"/> if the entity's bucket is the current tick of the day.</returns>
    public bool IsInBucket(EntityId id) => World.Clock.IsInBucket(id);

    /// <summary>Gets one system's random stream by reference. The pipeline and tests use it; systems use <see cref="Rng"/>.</summary>
    /// <param name="system">The system.</param>
    /// <returns>A reference to the generator stored in the world state.</returns>
    internal ref Pcg32 RngOf(SystemId system) => ref World.SystemRngs[(int)system];
}
