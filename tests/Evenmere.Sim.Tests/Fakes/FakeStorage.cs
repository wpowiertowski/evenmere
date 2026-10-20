using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Tests.Fakes;

/// <summary>
/// A settable island goods pool, both its read side (<see cref="IStorageQueries"/>) and its write side
/// (<see cref="IStorageService"/>), so a system under test sees the goods it takes and adds. It follows the real pool's
/// rules: stock never goes negative, <see cref="TryTake"/> is all or nothing, and an unknown good throws.
/// </summary>
internal sealed class FakeStorage(int goodCount = 16) : IStorageQueries, IStorageService
{
    private readonly Amount[] _stock = new Amount[goodCount];

    /// <inheritdoc />
    public int GoodCount => _stock.Length;

    /// <summary>Sets the stock of a good.</summary>
    /// <param name="good">The good.</param>
    /// <param name="amount">The stock.</param>
    /// <returns>This fake.</returns>
    public FakeStorage Set(GoodId good, Amount amount)
    {
        Check(good);
        _stock[good.Value] = amount;
        return this;
    }

    /// <inheritdoc />
    public Amount Stock(GoodId good) => Get(good);

    /// <inheritdoc />
    public Amount Get(GoodId good)
    {
        Check(good);
        return _stock[good.Value];
    }

    /// <inheritdoc />
    public void Add(GoodId good, Amount amount)
    {
        Check(good);
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, Amount.Zero);
        _stock[good.Value] += amount;
    }

    /// <inheritdoc />
    public bool TryTake(GoodId good, Amount amount)
    {
        Check(good);
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, Amount.Zero);
        if (_stock[good.Value] < amount)
        {
            return false;
        }

        _stock[good.Value] -= amount;
        return true;
    }

    private void Check(GoodId good) => ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(good.Value, _stock.Length);
}
