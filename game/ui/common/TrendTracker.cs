using Evenmere.Core.Time;

namespace Evenmere.Game.Ui.Common;

/// <summary>Which way a number has moved.</summary>
public enum Trend
{
    /// <summary>The number is the same as it was.</summary>
    Flat = 0,

    /// <summary>The number has gone up.</summary>
    Up = 1,

    /// <summary>The number has gone down.</summary>
    Down = 2,
}

/// <summary>
/// Tells whether a displayed number rose, fell or held over the last game day. The view-model feeds it one sample per refresh
/// (a game tick and a value); it keeps the samples of the last day and compares the newest with the oldest it needs. While
/// less than a day has passed since the first sample, the first sample is the baseline, so a new game shows movement early
/// rather than waiting a whole day. The class has no Godot types.
/// <para>
/// Values are the whole units the player sees, so the arrow agrees with the number: it is flat exactly when the
/// number is the same as a day ago. A tick that goes backwards (a new game or a loaded save) restarts the history.
/// </para>
/// </summary>
public sealed class TrendTracker
{
    private readonly List<Sample> _samples = [];
    private readonly long _windowTicks;

    /// <summary>Initializes a new instance of the <see cref="TrendTracker"/> class that looks back one game day.</summary>
    public TrendTracker()
        : this(GameTime.TicksPerDay)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TrendTracker"/> class.</summary>
    /// <param name="windowTicks">How far back to compare, in ticks; positive.</param>
    public TrendTracker(long windowTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowTicks);
        _windowTicks = windowTicks;
    }

    /// <summary>Gets the trend as of the last <see cref="Record"/>.</summary>
    public Trend Current { get; private set; }

    /// <summary>Adds a sample and updates <see cref="Current"/>.</summary>
    /// <param name="tick">The simulation tick of the sample.</param>
    /// <param name="value">The value at that tick, in the units shown to the player.</param>
    public void Record(long tick, long value)
    {
        if (_samples.Count > 0 && tick < _samples[^1].Tick)
        {
            _samples.Clear();
        }

        // A paused game samples again and again at one tick; keeping only the latest stops the list growing without bound.
        if (_samples.Count > 0 && tick == _samples[^1].Tick)
        {
            _samples[^1] = new Sample(tick, value);
        }
        else
        {
            _samples.Add(new Sample(tick, value));
        }

        // Keep exactly one sample at or before the window's start: the newest such sample is the baseline.
        long windowStart = tick - _windowTicks;
        int firstToKeep = 0;
        while (firstToKeep + 1 < _samples.Count && _samples[firstToKeep + 1].Tick <= windowStart)
        {
            firstToKeep++;
        }

        if (firstToKeep > 0)
        {
            _samples.RemoveRange(0, firstToKeep);
        }

        long baseline = _samples[0].Value;
        Current = value > baseline ? Trend.Up : value < baseline ? Trend.Down : Trend.Flat;
    }

    /// <summary>Forgets every sample and returns to <see cref="Trend.Flat"/>.</summary>
    public void Reset()
    {
        _samples.Clear();
        Current = Trend.Flat;
    }

    private readonly record struct Sample(long Tick, long Value);
}
