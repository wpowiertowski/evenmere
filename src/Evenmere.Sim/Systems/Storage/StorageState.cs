using Evenmere.Core.Numerics;
using Evenmere.Sim.Persistence;
using MessagePack;

namespace Evenmere.Sim.Systems.Storage;

/// <summary>
/// The storage service's slice of the world state: the island-wide pool of goods (ADR-0005). It is a dense array
/// indexed by <c>GoodId.Value</c>, one <see cref="Amount"/> per good in the loaded content. Use
/// <see cref="StoragePool"/> to change it.
/// </summary>
[MessagePackObject]
public sealed class StorageState
{
    /// <summary>Gets or sets the stock of each good, indexed by <c>GoodId.Value</c>. No entry is ever negative.</summary>
    [Key(0)]
    [ContentIndexed(ContentKind.Good)]
    public Amount[] Stock { get; set; } = [];

    /// <summary>Creates an empty pool for the given number of goods.</summary>
    /// <param name="goodCount">The number of goods in the loaded content.</param>
    /// <returns>The state, with every stock at zero.</returns>
    internal static StorageState ForGoodCount(int goodCount) => new() { Stock = new Amount[goodCount] };
}
