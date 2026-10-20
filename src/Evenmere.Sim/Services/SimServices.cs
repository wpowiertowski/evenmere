using Evenmere.Sim.Pipeline;

namespace Evenmere.Sim.Services;

/// <summary>
/// A service that needs the simulation context to do its work, such as the building lifecycle, which hands the context
/// to each listener. <see cref="SimContext"/> binds every such service once, when it is created.
/// </summary>
internal interface IBindsContext
{
    /// <summary>Gives the service the context it will pass on. Called once, before any command or system runs.</summary>
    /// <param name="context">The simulation's context.</param>
    void Bind(SimContext context);
}

/// <summary>
/// The write services that systems reach through <see cref="SimContext"/>: the ways one system changes what another system
/// owns. <c>SystemRoster</c> builds the bundle. Each service stays a throwing stub until the task that owns it replaces
/// that one line in the roster, so the other tasks can be written and tested against the interface in the meantime.
/// </summary>
internal sealed class SimServices
{
    /// <summary>Gets the building registry (M1-01).</summary>
    public required IBuildingRegistry BuildingRegistry { get; init; }

    /// <summary>Gets the building lifecycle subscription list (M1-01).</summary>
    public required IBuildingLifecycle BuildingLifecycle { get; init; }

    /// <summary>Gets the dirty-chunk tracker (M1-01).</summary>
    public required IChunkDirtyTracker DirtyChunks { get; init; }

    /// <summary>Gets the island goods pool (M0-05).</summary>
    public required IStorageService Storage { get; init; }

    /// <summary>Gets the effect applier (M1-04).</summary>
    public required IProgressionService ProgressionService { get; init; }

    /// <summary>Gets the points ledger (M1-04).</summary>
    public required IPointsLedger PointsLedger { get; init; }

    /// <summary>Gets the milestone counters (M1-04).</summary>
    public required ICounters Counters { get; init; }

    /// <summary>Gets Production's buffers, as Logistics sees them (M1-08).</summary>
    public required IProductionBuffers ProductionBuffers { get; init; }

    /// <summary>Gets the workforce supply that Population sets (M1-07).</summary>
    public required IWorkforceSupply WorkforceSupply { get; init; }

    /// <summary>Binds every service that needs the context to it.</summary>
    /// <param name="context">The simulation's context.</param>
    internal void BindTo(SimContext context)
    {
        // The set is fixed, so a list of the services that could need binding is cheaper than reflection.
        object[] all =
        [
            BuildingRegistry, BuildingLifecycle, DirtyChunks, Storage, ProgressionService, PointsLedger, Counters, ProductionBuffers, WorkforceSupply,
        ];
        foreach (object service in all)
        {
            if (service is IBindsContext binds)
            {
                binds.Bind(context);
            }
        }
    }
}
