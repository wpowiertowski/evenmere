using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Services;

/// <summary>
/// The write side of the island storage pool: goods enter it when a cart unloads and leave it when construction,
/// production or a house draws on it (ADR-0005). The M0 <c>StoragePool</c> implements it, and the read side is
/// <c>IStorageQueries</c>. Stock never goes negative, and <see cref="TryTake"/> is all or nothing. Every amount is in
/// milli-units.
/// </summary>
internal interface IStorageService
{
    /// <summary>Gets the number of goods the pool has a slot for, which is the number of goods in the loaded content.</summary>
    int GoodCount { get; }

    /// <summary>Gets how much of a good the pool holds.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The stock; zero or more.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="good"/> has no slot in the pool.</exception>
    Amount Get(GoodId good);

    /// <summary>Adds goods to the pool.</summary>
    /// <param name="good">The good.</param>
    /// <param name="amount">How much to add; zero or more.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="good"/> has no slot, or <paramref name="amount"/> is negative.</exception>
    void Add(GoodId good, Amount amount);

    /// <summary>Takes goods out of the pool, all or nothing: if the pool holds less than requested, nothing is taken.</summary>
    /// <param name="good">The good.</param>
    /// <param name="amount">How much to take; zero or more. Taking zero always succeeds.</param>
    /// <returns><see langword="true"/> if the whole amount was taken; <see langword="false"/> if the stock was too low and is unchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="good"/> has no slot, or <paramref name="amount"/> is negative.</exception>
    bool TryTake(GoodId good, Amount amount);
}
