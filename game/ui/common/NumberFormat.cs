using System.Globalization;
using Evenmere.Core.Numerics;

namespace Evenmere.Game.Ui.Common;

/// <summary>
/// Number formatting for the UI, with no Godot types so it can be tested on its own. Output never depends on the machine's
/// culture: groups of three digits are separated by a comma and the decimal separator is a point. (M4-05 can make both
/// follow the chosen language.) The simulation's <see cref="Amount"/> is held in milli-units, so everything here is integer
/// arithmetic.
/// </summary>
public static class NumberFormat
{
    /// <summary>Formats a whole number with thousands separators.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The text, such as <c>1,240</c> or <c>-3</c>.</returns>
    public static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats the whole units of an amount, dropping the milli-units below one: a stock of 12.9 shows as <c>12</c>, so the
    /// number only changes when the player could see a whole unit appear.
    /// </summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The text, such as <c>1,240</c>.</returns>
    public static string Units(Amount amount) => Count(amount.WholeUnits);

    /// <summary>
    /// Formats an amount to one decimal place, rounding half away from zero, for rates such as knowledge per day.
    /// </summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The text, such as <c>12.5</c>, <c>0.0</c> or <c>-3.2</c>; never <c>-0.0</c>.</returns>
    public static string Rate(Amount amount)
    {
        long milli = amount.Milli;
        bool negative = milli < 0;

        // Work in ulong so the smallest long does not overflow when negated.
        ulong magnitude = negative ? (ulong)(-(milli + 1)) + 1 : (ulong)milli;
        ulong tenths = (magnitude + 50) / 100;
        long whole = (long)(tenths / 10);
        long fraction = (long)(tenths % 10);

        string sign = negative && tenths != 0 ? "-" : string.Empty;
        return $"{sign}{Count(whole)}.{fraction.ToString(CultureInfo.InvariantCulture)}";
    }
}
