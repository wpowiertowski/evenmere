namespace Evenmere.Core.Numerics;

/// <summary>
/// A multiplier held as an <see cref="int"/> count of per-mille (1000 = 100%). Ratios replace floating
/// point everywhere in the simulation (ADR-0003). Modifiers on the same stat add their percentages first
/// (see <see cref="FromBonusPercent"/>) and are applied once, so they never compound.
/// </summary>
public readonly record struct Ratio
{
    private const int PerMilleOne = 1000;
    private const int PerMillePerPercent = 10;

    private Ratio(int perMille)
    {
        PerMille = perMille;
    }

    /// <summary>A ratio of exactly 1 (1000 per-mille): applying it changes nothing.</summary>
    public static Ratio One => new(PerMilleOne);

    /// <summary>A ratio of 0: applying it yields zero.</summary>
    public static Ratio Zero => default;

    /// <summary>The ratio in per-mille, where 1000 means 100%.</summary>
    public int PerMille { get; }

    /// <summary>Creates a ratio from a raw per-mille value.</summary>
    /// <param name="perMille">The value in per-mille; 1000 means 100%.</param>
    /// <returns>The ratio.</returns>
    public static Ratio FromPerMille(int perMille) => new(perMille);

    /// <summary>Creates a ratio from a percentage, so 150 becomes 1500 per-mille.</summary>
    /// <param name="percent">The percentage; 100 means 100%.</param>
    /// <returns>The ratio.</returns>
    /// <exception cref="OverflowException">The percentage is too large to represent.</exception>
    public static Ratio FromPercent(int percent) => new(checked(percent * PerMillePerPercent));

    /// <summary>
    /// Turns the sum of every bonus percentage on one stat into a single multiplier: 1000 plus 10 per
    /// percent, clamped at zero so a large penalty never makes the multiplier negative.
    /// </summary>
    /// <param name="sumOfPercents">The summed bonuses (positive) and penalties (negative), in percent.</param>
    /// <returns>The multiplier to apply once.</returns>
    /// <exception cref="OverflowException">The sum is too large to represent.</exception>
    public static Ratio FromBonusPercent(int sumOfPercents) =>
        new(Math.Max(0, checked(PerMilleOne + (PerMillePerPercent * sumOfPercents))));

    /// <summary>Multiplies an amount by this ratio, rounding toward negative infinity.</summary>
    /// <param name="amount">The amount to scale.</param>
    /// <returns>The scaled amount.</returns>
    /// <exception cref="OverflowException">The result does not fit in an <see cref="Amount"/>.</exception>
    public Amount Apply(Amount amount) => amount.MultiplyBy(this);
}
