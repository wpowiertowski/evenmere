namespace Evenmere.Sim.Diagnostics;

/// <summary>
/// An optional extra interface of <see cref="Simulation"/>: the time each ticked step has taken so far. Hosts and
/// tools check for it with <c>simulation as IProfilerReadout</c>; <see cref="ISimulation"/> itself does not require it.
/// The numbers are wall-clock measurements for diagnostics only and never feed back into the state.
/// </summary>
public interface IProfilerReadout
{
    /// <summary>Gets the number of timer ticks per second, for converting <see cref="SystemTiming.ElapsedTicks"/> to time.</summary>
    long TimerTicksPerSecond { get; }

    /// <summary>Gets the timings of the clock step, the command step and each ticked system, in tick order.</summary>
    /// <returns>A snapshot of the timings since the last reset.</returns>
    IReadOnlyList<SystemTiming> GetTimings();

    /// <summary>Forgets every timing recorded so far.</summary>
    void ResetTimings();
}

/// <summary>The time spent in one step of the tick.</summary>
/// <param name="Name">The step: <c>"clock"</c>, <c>"commands"</c> or a system name from <see cref="SystemNames"/>.</param>
/// <param name="Calls">How many times the step ran. A daily system runs once per 120 ticks.</param>
/// <param name="ElapsedTicks">The total elapsed time in timer ticks; divide by <see cref="IProfilerReadout.TimerTicksPerSecond"/> for seconds.</param>
public readonly record struct SystemTiming(string Name, long Calls, long ElapsedTicks);
