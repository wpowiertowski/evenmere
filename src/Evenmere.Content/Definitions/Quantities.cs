using System.Collections.Immutable;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Content.Definitions;

/// <summary>A quantity of one good. Lists of these are always sorted by <see cref="GoodId"/>.</summary>
/// <param name="Good">The good.</param>
/// <param name="Amount">How much of it.</param>
public readonly record struct GoodAmount(GoodId Good, Amount Amount);

/// <summary>A quantity of one kind of point.</summary>
/// <param name="Kind">The kind of point.</param>
/// <param name="Amount">How many.</param>
public readonly record struct PointAmount(PointKind Kind, Amount Amount);

/// <summary>An amount of creativity towards one type of great work.</summary>
/// <param name="Type">The great-work type the creativity counts towards.</param>
/// <param name="Amount">How much.</param>
public readonly record struct CreativityAmount(GreatWorkTypeId Type, Amount Amount);

/// <summary>
/// A bill of materials: goods plus optional coin. In JSON it is an object of good ID to quantity, where the
/// reserved key <c>coin</c> is coin rather than a good, for example <c>{ "timber": 2, "coin": 20 }</c>.
/// </summary>
/// <param name="Goods">The goods, sorted by good ID.</param>
/// <param name="Coin">The coin; zero when none.</param>
public sealed record CostSpec(ImmutableArray<GoodAmount> Goods, Amount Coin)
{
    /// <summary>Gets a cost of nothing.</summary>
    public static CostSpec Free { get; } = new(ImmutableArray<GoodAmount>.Empty, Amount.Zero);

    /// <summary>Gets a value indicating whether the cost asks for nothing.</summary>
    public bool IsFree => Goods.IsEmpty && Coin == Amount.Zero;
}

/// <summary>
/// Knowledge, culture and coin produced, plus creativity towards great works. For a building the figures are
/// per day; for a tier they are per 1,000 residents per day.
/// </summary>
/// <param name="Knowledge">Knowledge points.</param>
/// <param name="Culture">Culture points.</param>
/// <param name="Coin">Coin.</param>
/// <param name="Creativity">Creativity per great-work type, sorted by type ID.</param>
public sealed record OutputsSpec(Amount Knowledge, Amount Culture, Amount Coin, ImmutableArray<CreativityAmount> Creativity)
{
    /// <summary>Gets outputs of nothing.</summary>
    public static OutputsSpec None { get; } = new(Amount.Zero, Amount.Zero, Amount.Zero, ImmutableArray<CreativityAmount>.Empty);
}
