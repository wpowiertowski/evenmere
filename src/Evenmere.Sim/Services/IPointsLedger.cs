using Evenmere.Content.Definitions;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Services;

/// <summary>
/// The points credited since Research or Culture last took them: knowledge, culture and (from M3) coin. Population credits
/// each tier's output into it as the day goes by, and Research and Culture take the total once a day at the start of the
/// day. Implemented by the progression system (M1-04). Every amount is in milli-units.
/// </summary>
internal interface IPointsLedger
{
    /// <summary>Adds points to today's total.</summary>
    /// <param name="kind">The kind of point.</param>
    /// <param name="amount">How many; zero or more.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is negative.</exception>
    void Credit(PointKind kind, Amount amount);

    /// <summary>Returns everything credited since the last take and resets the total to zero.</summary>
    /// <param name="kind">The kind of point.</param>
    /// <returns>The credited total in milli-units; zero if nothing was credited.</returns>
    Amount TakeToday(PointKind kind);
}
