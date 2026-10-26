using Evenmere.Game.Host;
using Evenmere.Game.Ui.Inspector;
using Evenmere.Sim.Diagnostics;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Ui.Debug;

/// <summary>
/// The debug overlay (ADR-0012): fps, tick time per system from the simulation profiler, entity counts, the
/// selected entity's state and the state hash. It toggles with F3 (handled here in code; M2 adds no input action
/// for it) and, like the console, exists only in debug builds and never takes gamepad focus: every control sets
/// <see cref="Control.MouseFilterEnum.Ignore"/>.
/// </summary>
public sealed partial class DebugOverlay : CanvasLayer
{
    private const string ShowFlag = "--show-debug-overlay";
    private const double RefreshSeconds = 1.0;

    private readonly SimHost _host;
    private RichTextLabel? _body;
    private double _sinceRefresh;

    /// <summary>Initializes a new instance of the <see cref="DebugOverlay"/> class.</summary>
    /// <param name="host">The running simulation host.</param>
    public DebugOverlay(SimHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        // Layer 2, like the pause menu: above the HUD (layer 1). The strip hugs the right edge below the
        // HUD bar and stretches to the bottom, so the per-system list is never clipped.
        Layer = 2;

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        panel.OffsetLeft = -440;
        panel.OffsetTop = 175;
        panel.OffsetRight = -8;
        panel.OffsetBottom = -95;
        panel.AnchorBottom = 1f;
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.Visible = false;
        AddChild(panel);

        var body = new RichTextLabel();
        body.BbcodeEnabled = false;
        body.FocusMode = Control.FocusModeEnum.None;
        body.MouseFilter = Control.MouseFilterEnum.Ignore;
        body.ScrollActive = false;
        panel.AddChild(body);
        _body = body;

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == ShowFlag)
            {
                panel.Visible = true;
                break;
            }
        }
    }

    /// <inheritdoc />
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        // F3 has no input action on purpose: M2-26 adds only debug_console (ADR-0012). Key.F3 is an enum, not a raw code.
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F3)
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _sinceRefresh += delta;
        if (_sinceRefresh >= RefreshSeconds && _body is not null && _body.GetParent<PanelContainer>().Visible)
        {
            _sinceRefresh = 0;
            _body.Text = Render();
        }
    }

    /// <summary>Gets whether the overlay panel is currently shown.</summary>
    public bool IsOpen => _body?.GetParent<PanelContainer>()?.Visible ?? false;

    /// <summary>Toggles the overlay panel.</summary>
    public void Toggle()
    {
        PanelContainer? panel = _body?.GetParent<PanelContainer>();
        if (panel is null)
        {
            return;
        }

        panel.Visible = !panel.Visible;
        if (panel.Visible)
        {
            _sinceRefresh = RefreshSeconds;
        }
    }

    private string Render()
    {
        if (!_host.IsRunning)
        {
            return "debug: no simulation";
        }

        var lines = new List<string>
        {
            $"fps {Engine.GetFramesPerSecond()}  tick {_host.CurrentTick}",
        };

        if (_host.Simulation is IProfilerReadout profiler)
        {
            double perSecond = profiler.TimerTicksPerSecond;
            foreach (SystemTiming timing in profiler.GetTimings())
            {
                double ms = timing.ElapsedTicks / perSecond * 1000.0;
                lines.Add($"{timing.Name}: {ms:F2} ms ({timing.Calls})");
            }
        }

        int buildings = 0;
        foreach (BuildingInfo building in _host.Query.Map.ActiveBuildings)
        {
            buildings++;
        }

        lines.Add($"buildings {buildings}  residents {_host.Query.Population.TotalResidents}");
        lines.Add(RenderSelection());
        lines.Add($"hash {_host.ComputeStateHash():x16}");
        return string.Join("\n", lines);
    }

    private string RenderSelection()
    {
        InspectorPanel? inspector = GetParent()?.GetNodeOrNull<InspectorPanel>("InspectorPanel");
        if (inspector is null || inspector.Target == Core.Ids.EntityId.None)
        {
            return "selection: none";
        }

        if (!_host.Query.Map.TryGetBuilding(inspector.Target, out BuildingInfo building))
        {
            return $"selection: {inspector.Target.Value} (gone)";
        }

        return $"selection: {building.Id.Value} {_host.Content.NameOf(building.Type)} {building.State} @ ({building.Origin.X},{building.Origin.Y})";
    }
}
