namespace Evenmere.Sim.Commands;

/// <summary>A command with the number it was queued under.</summary>
/// <param name="Sequence">The queue's monotonic sequence number, starting at 1.</param>
/// <param name="Command">The queued command.</param>
internal readonly record struct QueuedCommand(long Sequence, Command Command);

/// <summary>
/// The commands waiting for the next tick, oldest first. Each one gets a sequence number that only ever grows, so
/// "applied in enqueue order" (ADR-0003) is checkable: the numbers of the commands applied in a tick are ascending.
/// The queue is host input, not world state: it is neither saved nor hashed, so a host saves between ticks after
/// the queue has drained (the command log in M1-14 covers anything still pending).
/// </summary>
internal sealed class CommandQueue
{
    private readonly Queue<QueuedCommand> _pending = new();
    private long _lastSequence;

    /// <summary>Gets the number of commands waiting.</summary>
    public int Count => _pending.Count;

    /// <summary>Gets the sequence number the most recently queued command received; zero before the first.</summary>
    public long LastSequence => _lastSequence;

    /// <summary>Queues a command.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The sequence number it received.</returns>
    public long Enqueue(Command command)
    {
        _lastSequence = checked(_lastSequence + 1);
        _pending.Enqueue(new QueuedCommand(_lastSequence, command));
        return _lastSequence;
    }

    /// <summary>Removes and returns the oldest command.</summary>
    /// <param name="command">The oldest command, when there is one.</param>
    /// <returns><see langword="true"/> if a command was waiting.</returns>
    public bool TryDequeue(out QueuedCommand command) => _pending.TryDequeue(out command);
}
