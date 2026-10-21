using Evenmere.Sim;
using Evenmere.Sim.Persistence;

namespace Evenmere.Game.Host.Persistence;

/// <summary>
/// Records every command the game enqueues into the <c>.evlog</c> beside the most recently written or loaded save,
/// so any session stays replayable with <see cref="Replay"/> (ADR-0007). The log always belongs to one slot:
/// writing or loading a save starts a fresh, empty log for that slot, and a new game has no active log until its
/// first save. Every recorded command carries the tick it is applied on, which is <c>CurrentTick + 1</c> at enqueue
/// time, and the stream is flushed after every append so a crash keeps what was recorded.
/// </summary>
public sealed class CommandLogRecorder : IDisposable
{
    private FileStream? _stream;
    private CommandLogWriter? _writer;
    private bool _disposed;

    /// <summary>Gets a value indicating whether a slot's log is currently being recorded.</summary>
    public bool HasActiveSlot => _writer is not null;

    /// <summary>Gets the path of the log being recorded, or <see langword="null"/> when no slot is active.</summary>
    public string? ActiveLogPath { get; private set; }

    /// <summary>
    /// Starts recording a fresh, empty log for a slot, closing the previous one. Call this right after writing or
    /// loading the slot's save, so the log holds exactly the commands made after that save.
    /// </summary>
    /// <param name="logPath">The <c>.evlog</c> path to create (overwriting any existing log).</param>
    public void BeginSlot(string logPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(logPath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        EndSlot();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath)) ?? logPath);
        _stream = new FileStream(logPath, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read);
        _writer = new CommandLogWriter(_stream);
        ActiveLogPath = logPath;
    }

    /// <summary>
    /// Appends one command with the tick it is applied on. It does nothing when no slot is active (a new game before
    /// its first save), and flushes through the OS buffers after every append so a crash keeps the entry.
    /// </summary>
    /// <param name="appliedTick">The tick the command is applied on (<c>CurrentTick + 1</c> at enqueue time).</param>
    /// <param name="command">The enqueued command.</param>
    public void Record(long appliedTick, Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_writer is null || _stream is null)
        {
            return;
        }

        _writer.Append(appliedTick, command);
        _stream.Flush(flushToDisk: true);
    }

    /// <summary>Stops recording and closes the active log, if any. The recorded entries stay on disk.</summary>
    public void EndSlot()
    {
        _writer?.Dispose();
        _writer = null;
        _stream?.Dispose();
        _stream = null;
        ActiveLogPath = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        EndSlot();
        GC.SuppressFinalize(this);
    }
}
