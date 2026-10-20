using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Queries;

/// <summary>Read-only queries over the island storage pool. M1-00 may add more; <see cref="Stock"/> is final.</summary>
public interface IStorageQueries
{
    /// <summary>Gets how much of a good the island holds.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The stock in milli-units; never negative.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="good"/> is not a good of the loaded content.</exception>
    Amount Stock(GoodId good);
}
