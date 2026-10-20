using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Rng;
using Evenmere.Core.Time;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Services;
using Evenmere.Sim.State;

namespace Evenmere.Sim.Pipeline;

/// <summary>
/// Everything a system or command handler needs while it runs: the immutable content, the world state, the clock, the
/// event sink, the read-only queries, the write services and the current system's random stream. One context lives as
/// long as its simulation; the pipeline points <see cref="CurrentSystem"/> at the system it is about to run.
/// </summary>
public sealed class SimContext
{
    private readonly SimServices _services;

    internal SimContext(ContentDb content, WorldState world, ISimQueries queries, EventSink events, SimServices services)
    {
        Content = content;
        World = world;
        Queries = queries;
        Events = events;
        _services = services;
        services.BindTo(this);
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

    /// <summary>Gets the island storage pool, the write side of <see cref="IStorageQueries"/>.</summary>
    internal IStorageService Storage => _services.Storage;

    /// <summary>Gets the building registry, the write side of <see cref="IMapQueries"/> (M1-01).</summary>
    internal IBuildingRegistry BuildingRegistry => _services.BuildingRegistry;

    /// <summary>Gets the subscription list for building lifecycle notifications (M1-01).</summary>
    internal IBuildingLifecycle BuildingLifecycle => _services.BuildingLifecycle;

    /// <summary>Gets the dirty-chunk tracker, which the roads service calls when roads change (M1-01).</summary>
    internal IChunkDirtyTracker DirtyChunks => _services.DirtyChunks;

    /// <summary>Gets the service that applies the effects of researched techs, adopted civics and the like (M1-04).</summary>
    internal IProgressionService ProgressionService => _services.ProgressionService;

    /// <summary>Gets the ledger of knowledge, culture and coin credited since Research and Culture last took it (M1-04).</summary>
    internal IPointsLedger PointsLedger => _services.PointsLedger;

    /// <summary>Gets the milestone counters that conditions read (M1-04).</summary>
    internal ICounters Counters => _services.Counters;

    /// <summary>Gets Production's buffers as Logistics uses them (M1-08).</summary>
    internal IProductionBuffers ProductionBuffers => _services.ProductionBuffers;

    /// <summary>Gets the workforce supply that Population sets once a day (M1-07).</summary>
    internal IWorkforceSupply WorkforceSupply => _services.WorkforceSupply;

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
