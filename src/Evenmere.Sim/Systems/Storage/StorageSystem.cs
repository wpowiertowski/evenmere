using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Storage;

/// <summary>
/// The storage service: the island-wide pool of goods (ADR-0005). It is the one real service in M0, because
/// construction, logistics and needs all depend on it. Its cadence is <see cref="Cadence.None"/>: it changes only
/// when other systems call <see cref="Pool"/>.
/// </summary>
internal sealed class StorageSystem : ISimSystem
{
    /// <summary>Initializes a new instance of the <see cref="StorageSystem"/> class.</summary>
    /// <param name="state">The pool's state, sized to the loaded content's goods.</param>
    public StorageSystem(StorageState state)
    {
        Pool = new StoragePool(state);
        Queries = new StorageQueries(Pool);
    }

    /// <inheritdoc />
    public string Name => SystemNames.Storage;

    /// <summary>Gets the write side of the pool, which other systems use through <c>SimContext.Storage</c>.</summary>
    public StoragePool Pool { get; }

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IStorageQueries Queries { get; }

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // The pool has no commands of its own: goods move only as a side effect of other systems' work.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // A service is never ticked (Cadence.None).
    }
}
