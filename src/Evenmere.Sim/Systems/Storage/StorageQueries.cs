using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Storage;

/// <summary>Implements <see cref="IStorageQueries"/> as a read-only view of the storage pool.</summary>
internal sealed class StorageQueries(StoragePool pool) : IStorageQueries
{
    /// <inheritdoc />
    public Amount Stock(GoodId good) => pool.Get(good);
}
