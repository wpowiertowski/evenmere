using System.Diagnostics;

namespace Evenmere.Sim.Diagnostics;

// Stopwatch is banned in the simulation (ADR-0003: no hidden inputs). This is the one place it is allowed: the
// profiler only measures how long a step took and exposes the numbers for display. Nothing here is read by a system,
// stored in WorldState or hashed, so the measurements can never change what the simulation computes.
#pragma warning disable RS0030
/// <summary>
/// Accumulates the elapsed timer ticks and the call count of each ticked step. A step registers once, gets a slot,
/// and the pipeline brackets each run with <see cref="Start"/> and <see cref="Stop"/>.
/// </summary>
internal sealed class SimProfiler : IProfilerReadout
{
    private readonly List<string> _names = [];
    private readonly List<long> _elapsed = [];
    private readonly List<long> _calls = [];

    /// <inheritdoc />
    public long TimerTicksPerSecond => Stopwatch.Frequency;

    /// <summary>Adds a step to the profile.</summary>
    /// <param name="name">The step's name.</param>
    /// <returns>The slot to pass to <see cref="Stop"/>.</returns>
    public int Register(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _names.Add(name);
        _elapsed.Add(0);
        _calls.Add(0);
        return _names.Count - 1;
    }

    /// <summary>Reads the timer.</summary>
    /// <returns>A timestamp to pass to <see cref="Stop"/>.</returns>
    public static long Start() => Stopwatch.GetTimestamp();

    /// <summary>Charges the time since <paramref name="start"/> to a step.</summary>
    /// <param name="slot">The slot <see cref="Register"/> returned; a negative slot is ignored.</param>
    /// <param name="start">The timestamp <see cref="Start"/> returned.</param>
    public void Stop(int slot, long start)
    {
        if (slot < 0)
        {
            return;
        }

        _elapsed[slot] += Stopwatch.GetTimestamp() - start;
        _calls[slot]++;
    }

    /// <inheritdoc />
    public IReadOnlyList<SystemTiming> GetTimings()
    {
        var timings = new SystemTiming[_names.Count];
        for (int i = 0; i < timings.Length; i++)
        {
            timings[i] = new SystemTiming(_names[i], _calls[i], _elapsed[i]);
        }

        return timings;
    }

    /// <inheritdoc />
    public void ResetTimings()
    {
        for (int i = 0; i < _names.Count; i++)
        {
            _elapsed[i] = 0;
            _calls[i] = 0;
        }
    }
}
#pragma warning restore RS0030
