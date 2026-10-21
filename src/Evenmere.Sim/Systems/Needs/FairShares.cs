using Evenmere.Core.Numerics;
using Evenmere.Sim.Persistence;
using MessagePack;

namespace Evenmere.Sim.Systems.Needs;

/// <summary>
/// What the houses want of each good today and how much of it the island can supply, computed once at the start of the day
/// (progression.md, How needs work). Both arrays are indexed by <c>GoodId.Value</c> and sized to the loaded content's goods
/// the first time the needs system runs; until then they are empty.
/// </summary>
[MessagePackObject]
public sealed class FairShares
{
    /// <summary>Gets or sets what the houses wanted, in milli-units per day: <c>Σ residents × rate ÷ 1000</c>, with the populations yesterday ended with.</summary>
    [Key(0)]
    [ContentIndexed(ContentKind.Good)]
    public Amount[] Demand { get; set; } = [];

    /// <summary>Gets or sets the fraction of each good that every house consumes today: <c>min(1, (stock + producedYesterday) ÷ demand)</c>, or 1000 per-mille when nobody needs the good.</summary>
    [Key(1)]
    [ContentIndexed(ContentKind.Good)]
    public Ratio[] Fulfilment { get; set; } = [];
}
