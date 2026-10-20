namespace Evenmere.Sim.Pipeline;

/// <summary>
/// Collects the events the simulation emits until the host drains them. It is append-only for systems: they can emit
/// but never read, reorder or remove events. Events are not state, so draining them never changes the state hash.
/// </summary>
public sealed class EventSink
{
    private readonly List<SimEvent> _events = [];

    /// <summary>Gets the number of events waiting to be drained.</summary>
    public int Count => _events.Count;

    /// <summary>Appends an event after those already emitted.</summary>
    /// <param name="simEvent">The event.</param>
    /// <exception cref="ArgumentNullException"><paramref name="simEvent"/> is <see langword="null"/>.</exception>
    public void Emit(SimEvent simEvent)
    {
        ArgumentNullException.ThrowIfNull(simEvent);
        _events.Add(simEvent);
    }

    /// <summary>Removes and returns every event, in emission order.</summary>
    /// <returns>A snapshot the caller owns; empty when nothing was emitted.</returns>
    internal IReadOnlyList<SimEvent> Drain()
    {
        if (_events.Count == 0)
        {
            return [];
        }

        SimEvent[] drained = [.. _events];
        _events.Clear();
        return drained;
    }
}
