using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Systems.Storage;

namespace Evenmere.Sim.Services;

/// <summary>Exposes the M0 <see cref="StoragePool"/> as an <see cref="IStorageService"/>; it adds no behaviour of its own.</summary>
internal sealed class StorageService(StoragePool pool) : IStorageService
{
    /// <inheritdoc />
    public int GoodCount => pool.GoodCount;

    /// <inheritdoc />
    public Amount Get(GoodId good) => pool.Get(good);

    /// <inheritdoc />
    public void Add(GoodId good, Amount amount) => pool.Add(good, amount);

    /// <inheritdoc />
    public bool TryTake(GoodId good, Amount amount) => pool.TryTake(good, amount);
}
