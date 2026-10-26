using Evenmere.Core.Ids;
using Evenmere.Core.Time;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Happenings;

/// <summary>Implements <see cref="IHappeningsQueries"/> over the happenings state. (M2-12.)</summary>
internal sealed class HappeningsQueries : IHappeningsQueries
{
    private readonly HappeningsState _state;
    private readonly Func<GameClock> _clock;

    /// <summary>Initializes a new instance of the <see cref="HappeningsQueries"/> class.</summary>
    /// <param name="state">The happenings state to read.</param>
    /// <param name="clock">Reads the simulation clock, so cooldowns answer against the current tick.</param>
    internal HappeningsQueries(HappeningsState state, Func<GameClock> clock)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(clock);

        _state = state;
        _clock = clock;
    }

    /// <inheritdoc />
    PendingHappening? IHappeningsQueries.PendingHappening()
    {
        if (_state.Pending is not { } pending)
        {
            return null;
        }

        // A damaged state (or content that shrank without a remap) names nothing the content defines; report none.
        return IsKnown(pending.Happening) ? pending : null;
    }

    /// <inheritdoc />
    PendingHappeningView? IHappeningsQueries.PendingView()
    {
        if (_state.Pending is not { } pending)
        {
            return null;
        }

        return IsKnown(pending.Happening) ? new PendingHappeningView(pending.Happening, pending.RaisedTick) : null;
    }

    /// <inheritdoc />
    bool IHappeningsQueries.IsOnCooldown(HappeningId happening)
    {
        if (!IsKnown(happening) || (uint)happening.Value >= (uint)_state.CooldownUntil.Length)
        {
            return false;
        }

        return _state.CooldownUntil[happening.Value] > _clock().Tick;
    }

    private bool IsKnown(HappeningId happening) => _state.CooldownUntil.Length > happening.Value;
}
