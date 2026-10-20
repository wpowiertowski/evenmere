namespace Evenmere.Sim.Pipeline;

/// <summary>How often the pipeline runs a system (architecture.md, System order).</summary>
public enum Cadence
{
    /// <summary>Never ticked. A shared service that changes only through command handlers and the calls other systems make into it.</summary>
    None = 0,

    /// <summary>Runs on every tick.</summary>
    EveryTick = 1,

    /// <summary>Runs once per day, on the first tick of the day (<c>GameClock.IsDayStart</c>).</summary>
    Daily = 2,

    /// <summary>
    /// Runs on every tick and spreads a day's work across the day: the system filters its entities with
    /// <see cref="SimContext.IsInBucket"/>, so each is handled once per day (ADR-0003).
    /// </summary>
    DailyBucketed = 3,
}
