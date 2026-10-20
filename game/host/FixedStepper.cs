namespace Evenmere.Game.Host;

/// <summary>
/// The fixed-step accumulator behind <see cref="SimHost"/>, with no Godot types so it can be tested on its own. It keeps
/// game time as whole microseconds in a <see langword="long"/>, so no time is lost to binary rounding: at 1× ten seconds
/// of frames always give exactly forty ticks, and the speed multiplier scales the time without adding error.
/// </summary>
/// <remarks>
/// A frame calls <see cref="Accumulate"/>, runs up to <see cref="TicksDue"/> ticks, then reports how many actually ran
/// with <see cref="Consume"/>. The host may run fewer than are due when its time budget runs out; the rest stay in the
/// accumulator for the next frame, up to <see cref="MaxBacklogMicroseconds"/>.
/// </remarks>
public sealed class FixedStepper
{
    /// <summary>The length of one tick: 0.25 s (ADR-0003).</summary>
    public const long TickMicroseconds = 250_000;

    /// <summary>
    /// The most game time the accumulator may hold after a frame. Past this, the simulation lags rather than the frame rate
    /// dropping, and the excess is dropped so a slow patch cannot snowball into a long catch-up.
    /// </summary>
    public const long MaxBacklogMicroseconds = 1_000_000;

    /// <summary>The most ticks one frame may run (ADR-0006).</summary>
    public const int MaxTicksPerFrame = 8;

    private const long MicrosecondsPerSecond = 1_000_000;

    private long _accumulator;

    /// <summary>Gets the game time waiting to be ticked, in microseconds.</summary>
    public long AccumulatorMicroseconds => _accumulator;

    /// <summary>Gets how far the accumulator has run toward the next tick, from 0 to 1, so the view can interpolate between ticks.</summary>
    public double InterpolationAlpha => Math.Clamp(_accumulator / (double)TickMicroseconds, 0.0, 1.0);

    /// <summary>
    /// Adds one frame of real time, scaled by the speed. The delta is rounded to whole microseconds once, and only then
    /// multiplied by the integer multiplier.
    /// </summary>
    /// <param name="deltaSeconds">The real time since the last frame; finite and not negative.</param>
    /// <param name="speedMultiplier">Game seconds per real second: 0 when paused, otherwise 1, 2, 4 or 16.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deltaSeconds"/> is negative, NaN or infinite, or <paramref name="speedMultiplier"/> is negative.</exception>
    public void Accumulate(double deltaSeconds, int speedMultiplier)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds, "The frame delta must be finite and not negative.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(speedMultiplier);

        _accumulator += (long)Math.Round(deltaSeconds * MicrosecondsPerSecond) * speedMultiplier;
    }

    /// <summary>Gets how many whole ticks the accumulator holds, at most <see cref="MaxTicksPerFrame"/>.</summary>
    /// <returns>The number of ticks to run this frame.</returns>
    public int TicksDue() => (int)Math.Min(_accumulator / TickMicroseconds, MaxTicksPerFrame);

    /// <summary>Takes the ticks that were run out of the accumulator, then drops any backlog beyond <see cref="MaxBacklogMicroseconds"/>.</summary>
    /// <param name="ticksRun">The number of ticks the host actually ran; not more than the accumulator holds.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticksRun"/> is negative or more than the accumulator holds.</exception>
    public void Consume(int ticksRun)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticksRun);
        if (ticksRun > _accumulator / TickMicroseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksRun), ticksRun, "More ticks were consumed than the accumulator holds.");
        }

        _accumulator = Math.Min(_accumulator - (ticksRun * TickMicroseconds), MaxBacklogMicroseconds);
    }

    /// <summary>
    /// Drops whole ticks of backlog and keeps the fraction of a tick, for pausing: resuming then starts without a burst,
    /// and the view holds its interpolation pose.
    /// </summary>
    public void DropBacklog() => _accumulator %= TickMicroseconds;

    /// <summary>Empties the accumulator, for a new world.</summary>
    public void Reset() => _accumulator = 0;
}
