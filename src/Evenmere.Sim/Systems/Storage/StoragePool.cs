using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Systems.Storage;

/// <summary>
/// The write side of the island storage pool: goods enter it when a cart unloads and leave it when construction,
/// production or a house draws on it (ADR-0005). Other systems reach it through <c>SimContext.Storage</c>; the view
/// and other systems read it through <see cref="Queries.IStorageQueries"/>. Stock never goes negative, and
/// <see cref="TryTake"/> is all or nothing.
/// </summary>
internal sealed class StoragePool
{
    private readonly StorageState _state;

    /// <summary>Initializes a new instance of the <see cref="StoragePool"/> class over a storage state.</summary>
    /// <param name="state">The state to read and change; its array is indexed by <c>GoodId.Value</c>.</param>
    public StoragePool(StorageState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
    }

    /// <summary>Gets the number of goods the pool has a slot for.</summary>
    public int GoodCount => _state.Stock.Length;

    /// <summary>Gets how much of a good the pool holds.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The stock; zero or more.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="good"/> has no slot in the pool.</exception>
    public Amount Get(GoodId good)
    {
        Amount[] stock = _state.Stock;
        CheckGood(good, stock);
        return stock[good.Value];
    }

    /// <summary>Adds goods to the pool.</summary>
    /// <param name="good">The good.</param>
    /// <param name="amount">How much to add; zero or more.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="good"/> has no slot, or <paramref name="amount"/> is negative.</exception>
    /// <exception cref="OverflowException">The new stock does not fit in an <see cref="Amount"/>; the stock is unchanged.</exception>
    public void Add(GoodId good, Amount amount)
    {
        Amount[] stock = _state.Stock;
        CheckGood(good, stock);
        CheckNotNegative(amount);
        stock[good.Value] += amount;
    }

    /// <summary>Takes goods out of the pool, all or nothing: if the pool holds less than requested, nothing is taken.</summary>
    /// <param name="good">The good.</param>
    /// <param name="amount">How much to take; zero or more. Taking zero always succeeds.</param>
    /// <returns><see langword="true"/> if the whole amount was taken; <see langword="false"/> if the stock was too low and is unchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="good"/> has no slot, or <paramref name="amount"/> is negative.</exception>
    public bool TryTake(GoodId good, Amount amount)
    {
        Amount[] stock = _state.Stock;
        CheckGood(good, stock);
        CheckNotNegative(amount);

        ref Amount held = ref stock[good.Value];
        if (held < amount)
        {
            return false;
        }

        held -= amount;
        return true;
    }

    private static void CheckGood(GoodId good, Amount[] stock)
    {
        if (good.Value >= stock.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(good), good, $"The pool has {stock.Length} goods; {good} is not one of them.");
        }
    }

    private static void CheckNotNegative(Amount amount)
    {
        if (amount < Amount.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "An amount to add or take must not be negative.");
        }
    }
}
