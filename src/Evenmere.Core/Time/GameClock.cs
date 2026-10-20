using Evenmere.Core.Ids;

namespace Evenmere.Core.Time;

/// <summary>
/// The simulation clock: a count of ticks since the world began, with the calendar derived from it. Every
/// count is zero-based, so tick 0 is the first tick of day 0 of year 0, in spring. The clock is immutable;
/// <see cref="Advance"/> returns the next one.
/// </summary>
public readonly struct GameClock : IEquatable<GameClock>
{
    /// <summary>Initializes a new instance of the <see cref="GameClock"/> struct at a given tick.</summary>
    /// <param name="tick">The number of ticks since the world began; not negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tick"/> is negative.</exception>
    public GameClock(long tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        Tick = tick;
    }

    /// <summary>Gets the number of ticks since the world began.</summary>
    public long Tick { get; }

    /// <summary>Gets the number of whole days since the world began.</summary>
    public long Day => Tick / GameTime.TicksPerDay;

    /// <summary>Gets the tick within the current day, from 0 to <see cref="GameTime.TicksPerDay"/> minus 1.</summary>
    public int TickOfDay => (int)(Tick % GameTime.TicksPerDay);

    /// <summary>Gets the day within the current season, from 0 to <see cref="GameTime.DaysPerSeason"/> minus 1.</summary>
    public int DayOfSeason => (int)(Day % GameTime.DaysPerSeason);

    /// <summary>Gets the current season.</summary>
    public Season Season => (Season)(int)(Day / GameTime.DaysPerSeason % GameTime.SeasonsPerYear);

    /// <summary>Gets the number of whole years since the world began, so day 40 is the first day of year 1.</summary>
    public long Year => Day / GameTime.DaysPerYear;

    /// <summary>Gets a value indicating whether this is the first tick of a day.</summary>
    public bool IsDayStart => TickOfDay == 0;

    /// <summary>Gets a value indicating whether this is the first tick of a season.</summary>
    public bool IsSeasonStart => IsDayStart && DayOfSeason == 0;

    /// <summary>Tests whether two clocks are at the same tick.</summary>
    /// <param name="left">The first clock.</param>
    /// <param name="right">The second clock.</param>
    /// <returns><see langword="true"/> if the ticks are equal.</returns>
    public static bool operator ==(GameClock left, GameClock right) => left.Equals(right);

    /// <summary>Tests whether two clocks are at different ticks.</summary>
    /// <param name="left">The first clock.</param>
    /// <param name="right">The second clock.</param>
    /// <returns><see langword="true"/> if the ticks differ.</returns>
    public static bool operator !=(GameClock left, GameClock right) => !left.Equals(right);

    /// <summary>
    /// Gets the daily bucket of an entity: <c>id % 120</c>. Daily systems spread their work across the day by
    /// handling an entity only on the tick where <see cref="TickOfDay"/> equals its bucket.
    /// </summary>
    /// <param name="id">The entity.</param>
    /// <returns>A value from 0 to <see cref="GameTime.TicksPerDay"/> minus 1.</returns>
    public static int BucketOf(EntityId id) => id.Value % GameTime.TicksPerDay;

    /// <summary>Tests whether an entity's daily bucket is the current tick of the day.</summary>
    /// <param name="id">The entity.</param>
    /// <returns><see langword="true"/> if a daily-bucketed system should process the entity this tick.</returns>
    public bool IsInBucket(EntityId id) => BucketOf(id) == TickOfDay;

    /// <summary>Returns the clock one tick later.</summary>
    /// <returns>The next clock.</returns>
    /// <exception cref="OverflowException">The tick count would overflow.</exception>
    public GameClock Advance() => new(checked(Tick + 1));

    /// <inheritdoc />
    public bool Equals(GameClock other) => Tick == other.Tick;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GameClock other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Tick.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => $"GameClock(tick {Tick}: year {Year}, {Season}, day {DayOfSeason}, tick {TickOfDay})";
}
