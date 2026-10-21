using Evenmere.Core.Numerics;

namespace Evenmere.Game.Ui.Progress;

/// <summary>
/// The arithmetic behind the research and civics rows: progress fractions and day estimates.
/// It works on raw milli-unit counts, has no Godot types, and never touches the simulation.
/// </summary>
public static class ProgressMath
{
    /// <summary>
    /// Estimates how many whole days a node still needs: the ceiling of the remaining points over the
    /// points gained per day.
    /// </summary>
    /// <param name="remainingMilli">The points still needed, in milli-units; zero or less means done.</param>
    /// <param name="perDayMilli">The points gained per day, in milli-units.</param>
    /// <returns>The days, rounded up, or <see langword="null"/> when nothing is gained per day.</returns>
    public static int? EtaDays(long remainingMilli, long perDayMilli)
    {
        if (perDayMilli <= 0)
        {
            return null;
        }

        if (remainingMilli <= 0)
        {
            return 0;
        }

        return checked((int)((remainingMilli + perDayMilli - 1) / perDayMilli));
    }

    /// <summary>
    /// Estimates how many whole days a node still needs.
    /// </summary>
    /// <param name="cost">What the node still costs in all, after any inspiration discount.</param>
    /// <param name="progress">What the node has received so far.</param>
    /// <param name="perDay">The points gained per day; zero before the first accrual.</param>
    /// <returns>The days, rounded up, or <see langword="null"/> when nothing is gained per day.</returns>
    public static int? EtaDays(Amount cost, Amount progress, Amount perDay) =>
        EtaDays(cost.Milli - progress.Milli, perDay.Milli);

    /// <summary>
    /// Computes how full a progress bar should be.
    /// </summary>
    /// <param name="progress">What the node has received so far.</param>
    /// <param name="cost">What the node still costs in all.</param>
    /// <returns>The share done, from 0 to 1; 1 when the cost is zero.</returns>
    public static double Fraction(Amount progress, Amount cost)
    {
        if (cost.Milli <= 0)
        {
            return 1.0;
        }

        double fraction = (double)progress.Milli / cost.Milli;
        return Math.Clamp(fraction, 0.0, 1.0);
    }
}
