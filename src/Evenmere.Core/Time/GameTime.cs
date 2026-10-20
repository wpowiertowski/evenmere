namespace Evenmere.Core.Time;

/// <summary>The fixed time model (ADR-0003): one tick is 0.25 s of game time, and calendar units are whole numbers of ticks.</summary>
public static class GameTime
{
    /// <summary>The number of ticks in a day.</summary>
    public const int TicksPerDay = 120;

    /// <summary>The number of days in a season.</summary>
    public const int DaysPerSeason = 10;

    /// <summary>The number of seasons in a year.</summary>
    public const int SeasonsPerYear = 4;

    /// <summary>The number of days in a year.</summary>
    public const int DaysPerYear = DaysPerSeason * SeasonsPerYear;

    /// <summary>The number of ticks in a season.</summary>
    public const int TicksPerSeason = TicksPerDay * DaysPerSeason;

    /// <summary>The number of ticks in a year.</summary>
    public const int TicksPerYear = TicksPerDay * DaysPerYear;
}
