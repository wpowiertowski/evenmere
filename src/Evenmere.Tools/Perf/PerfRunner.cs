using System.Collections.Immutable;
using System.Diagnostics;
using Evenmere.Sim;
using Evenmere.Sim.Diagnostics;
using Evenmere.Tools.Reporting;

namespace Evenmere.Tools.Perf;

/// <summary>
/// Measures the wall-clock cost of running a save: it ticks the simulation and records every tick's
/// duration, plus every profiler step's duration within that tick, then summarises them into a
/// <see cref="PerfReport"/>. The numbers vary between machines and never feed back into the state;
/// the simulation itself is untouched.
/// </summary>
public static class PerfRunner
{
    /// <summary>
    /// Runs the simulation and measures it.
    /// </summary>
    /// <param name="simulation">The simulation to tick; it must expose <see cref="IProfilerReadout"/> (every simulation this assembly creates does).</param>
    /// <param name="savePath">The save file the simulation was loaded from, recorded in the report.</param>
    /// <param name="ticks">How many ticks to measure; at least 1.</param>
    /// <param name="enforced">Whether to check the Deck-equivalent averages against budget × 1.25.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="simulation"/> or <paramref name="savePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticks"/> is less than 1.</exception>
    /// <exception cref="ArgumentException"><paramref name="simulation"/> does not expose <see cref="IProfilerReadout"/>.</exception>
    public static PerfReport Run(ISimulation simulation, string savePath, long ticks, bool enforced)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(savePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(ticks, 1);

        if (simulation is not IProfilerReadout readout)
        {
            throw new ArgumentException("The simulation does not expose its profiler timings.", nameof(simulation));
        }

        long saveTick = simulation.CurrentTick;
        var totals = new List<double>();
        var perStep = new Dictionary<string, List<double>>(StringComparer.Ordinal);

        // The profiler accumulates across ticks, so each tick is bracketed by a reset: the snapshot after
        // the tick is that tick's cost per step. Resetting and snapshotting allocate a little per tick, which
        // the totals (measured around the whole tick) include, so nothing is hidden.
        var watch = new Stopwatch();
        for (long i = 0; i < ticks; i++)
        {
            readout.ResetTimings();
            watch.Restart();
            simulation.Tick();
            watch.Stop();
            totals.Add(ElapsedMs(watch));

            foreach (SystemTiming timing in readout.GetTimings())
            {
                if (!perStep.TryGetValue(timing.Name, out List<double>? samples))
                {
                    samples = [];
                    perStep[timing.Name] = samples;
                }

                samples.Add((double)timing.ElapsedTicks / readout.TimerTicksPerSecond * 1000);
            }
        }

        // Tick order is the profiler's registration order, which GetTimings returns.
        var steps = new List<PerfStepStats>();
        foreach (SystemTiming timing in readout.GetTimings())
        {
            if (perStep.TryGetValue(timing.Name, out List<double>? samples))
            {
                (double average, double p50, double p99, double max) = Summarise(samples);
                steps.Add(new PerfStepStats(timing.Name, samples.Count, average, p50, p99, max));
            }
        }

        ImmutableArray<PerfStepStats> stepStats = [.. steps];
        ImmutableArray<PerfBucketStats> buckets = SummariseBuckets(perStep, ticks);
        (double totalAverage, double totalP50, double totalP99, double totalMax) = Summarise(totals);

        bool passed = true;
        if (enforced)
        {
            foreach (PerfBucketStats bucket in buckets)
            {
                if (bucket.OverBudget)
                {
                    passed = false;
                    break;
                }
            }

            if (totalAverage * PerfBudgets.MacToDeckFactor
                > PerfBudgets.TotalAverageMs * PerfBudgets.OverBudgetTolerance)
            {
                passed = false;
            }
        }

        return new PerfReport(
            savePath,
            saveTick,
            ticks,
            simulation.Query.Population.TotalResidents,
            Hex.Format(simulation.ComputeStateHash()),
            stepStats,
            buckets,
            totalAverage,
            totalP50,
            totalP99,
            totalMax,
            enforced,
            passed);
    }

    // A budget row sums its steps tick by tick, so the row's average, p50, p99 and max are the statistics of
    // the summed whole-tick row costs. Steps the run never recorded contribute nothing; steps the budget
    // table does not name join the "clock, commands, other" row.
    private static ImmutableArray<PerfBucketStats> SummariseBuckets(
        Dictionary<string, List<double>> perStep, long ticks)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (PerfBudgets.BudgetRow row in PerfBudgets.Rows)
        {
            foreach (string step in row.Steps)
            {
                named.Add(step);
            }
        }

        var buckets = new List<PerfBucketStats>(PerfBudgets.Rows.Length);
        foreach (PerfBudgets.BudgetRow row in PerfBudgets.Rows)
        {
            bool other = row == PerfBudgets.Rows[^1];
            var names = new List<string>();
            var sums = new List<double>();
            foreach ((string name, List<double> samples) in perStep)
            {
                bool mine = other ? !named.Contains(name) || row.Steps.Contains(name) : row.Steps.Contains(name);
                if (!mine)
                {
                    continue;
                }

                names.Add(name);
                if (sums.Count == 0)
                {
                    sums.AddRange(samples);
                }
                else
                {
                    for (int i = 0; i < sums.Count && i < samples.Count; i++)
                    {
                        sums[i] += samples[i];
                    }
                }
            }

            names.Sort(StringComparer.Ordinal);
            (double average, double p50, double p99, double max) = Summarise(sums);
            bool over = average * PerfBudgets.MacToDeckFactor > row.BudgetMs * PerfBudgets.OverBudgetTolerance;
            buckets.Add(new PerfBucketStats(row.Name, [.. names], average, p50, p99, max, row.BudgetMs, over));
        }

        return [.. buckets];
    }

    private static double ElapsedMs(Stopwatch watch) =>
        (double)watch.ElapsedTicks / Stopwatch.Frequency * 1000;

    private static (double Average, double P50, double P99, double Max) Summarise(List<double> samples)
    {
        if (samples.Count == 0)
        {
            return (0, 0, 0, 0);
        }

        double sum = 0;
        double max = 0;
        foreach (double sample in samples)
        {
            sum += sample;
            if (sample > max)
            {
                max = sample;
            }
        }

        double[] sorted = [.. samples];
        Array.Sort(sorted);
        return (sum / sorted.Length, Percentile(sorted, 0.50), Percentile(sorted, 0.99), max);
    }

    // Linear interpolation between closest ranks: the same inputs always give the same percentile.
    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1)
        {
            return sorted[0];
        }

        double rank = p * (sorted.Length - 1);
        int lower = (int)rank;
        int upper = Math.Min(lower + 1, sorted.Length - 1);
        return sorted[lower] + ((rank - lower) * (sorted[upper] - sorted[lower]));
    }
}
