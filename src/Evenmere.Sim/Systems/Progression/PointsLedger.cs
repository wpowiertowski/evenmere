using Evenmere.Content.Definitions;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Systems.Progression;

/// <summary>
/// The points credited since Research or Culture last took them. Population credits tier outputs into it through the day,
/// and Research and Culture take the total once a day. The totals live in <see cref="ProgressionState.Points"/>, indexed by
/// <see cref="PointKind"/>. Coin is credited here like the others but nothing spends it until M3.
/// </summary>
internal sealed class PointsLedger : IPointsLedger
{
    private readonly ProgressionState _state;

    /// <summary>Initializes a new instance of the <see cref="PointsLedger"/> class over the progression state.</summary>
    /// <param name="state">The state, already fitted to the content.</param>
    public PointsLedger(ProgressionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
    }

    /// <inheritdoc />
    public void Credit(PointKind kind, Amount amount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, Amount.Zero);
        int slot = SlotOf(kind);
        _state.Points[slot] += amount;
    }

    /// <inheritdoc />
    public Amount TakeToday(PointKind kind)
    {
        int slot = SlotOf(kind);
        Amount total = _state.Points[slot];
        _state.Points[slot] = Amount.Zero;
        return total;
    }

    /// <summary>Reads what has been credited and not yet taken, without taking it.</summary>
    /// <param name="kind">The kind of point.</param>
    /// <returns>The credited total in milli-units.</returns>
    public Amount Peek(PointKind kind) => _state.Points[SlotOf(kind)];

    private static int SlotOf(PointKind kind) =>
        (uint)kind < (uint)ProgressionState.PointKindCount ? (int)kind : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a kind of point.");
}
