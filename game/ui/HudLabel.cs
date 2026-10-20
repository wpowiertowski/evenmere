using Evenmere.Core.Time;
using Evenmere.Game.Host;
using Godot;

namespace Evenmere.Game.Ui;

/// <summary>
/// A placeholder readout of the simulation clock: <c>Tick N · Day D · Season · speed</c>. It shows the raw, zero-based
/// numbers the sim counts with; the real HUD (M1) formats them for the player.
/// </summary>
public sealed partial class HudLabel : Label
{
    private SimHost? _host;
    private long _shownTick = -1;
    private SimSpeed _shownSpeed = SimSpeed.Paused;

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this label.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../../SimHost");

    /// <inheritdoc />
    public override void _Ready() => _host = GetNodeOrNull<SimHost>(HostPath);

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_host is not { IsRunning: true })
        {
            Text = "No simulation";
            _shownTick = -1;
            return;
        }

        long tick = _host.CurrentTick;
        if (tick == _shownTick && _host.Speed == _shownSpeed)
        {
            return;
        }

        _shownTick = tick;
        _shownSpeed = _host.Speed;

        GameClock clock = _host.Clock;
        Text = $"Tick {clock.Tick} · Day {clock.Day} · {clock.Season} · {_shownSpeed.Label()}";
    }
}
