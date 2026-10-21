using Evenmere.Sim;
using Godot;

namespace Evenmere.Game.Host.Smoke;

/// <summary>
/// Drives smoke v2: after <c>Main</c> loads the save, this node replays the log's commands
/// through <see cref="SimHost.Enqueue"/> at their ticks, a few ticks per frame, so the full
/// <c>Main.tscn</c> scene (terrain, buildings, roads, life, tools, UI) stays active and renders
/// while the simulation runs. When the ticks run out it prints <c>SMOKE OK hash=...</c> and quits.
/// </summary>
internal sealed partial class SmokeRunner : Node
{
    /// <summary>How many ticks one frame runs. Small enough to let the renderers draw between batches.</summary>
    internal const int TicksPerFrame = 10;

    private SimHost? _host;
    private IReadOnlyList<(long Tick, Command Command)> _entries = Array.Empty<(long Tick, Command Command)>();
    private int _next;
    private long _remaining;

    /// <summary>Starts the replay. Call once, right after the host loaded the save.</summary>
    /// <param name="host">The running simulation host.</param>
    /// <param name="entries">The log's entries in file order.</param>
    /// <param name="ticksToRun">How many ticks to run from the loaded tick.</param>
    internal void Setup(SimHost host, IReadOnlyList<(long Tick, Command Command)> entries, long ticksToRun)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfNegative(ticksToRun);

        _host = host;
        _entries = entries;
        _remaining = ticksToRun;

        // The host's own frame step would double-tick the world; the runner drives ticks instead.
        host.Speed = SimSpeed.Paused;

        // Entries at or before the save's tick already happened; the first tick to run applies after them.
        _next = 0;
        while (_next < _entries.Count && _entries[_next].Tick <= host.CurrentTick)
        {
            _next++;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            for (int i = 0; i < TicksPerFrame && _remaining > 0; i++)
            {
                long target = _host.CurrentTick + 1;
                while (_next < _entries.Count && _entries[_next].Tick == target)
                {
                    _host.Enqueue(_entries[_next].Command);
                    _next++;
                }

                // A log entry past the run's last tick never becomes due, like Replay.Run.
                _host.RunTicksNow(1);
                _remaining--;
            }

            if (_remaining <= 0)
            {
                GD.Print($"SMOKE OK hash={_host.ComputeStateHash():x16}");
                GetTree().Quit(0);
            }
        }
        catch (Exception exception)
        {
            GD.Print($"SMOKE FAIL {exception.Message.ReplaceLineEndings(" ")}");
            GD.PrintErr(exception.ToString());
            GetTree().Quit(1);
        }
    }
}
