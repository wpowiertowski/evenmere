using System.Globalization;
using Evenmere.Core.Ids;
using Evenmere.Game.Rendering.Camera;
using Evenmere.Game.Rendering.Terrain;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Host.Debug;

/// <summary>
/// Runs the debug command-line paths (<see cref="DebugLaunchOptions"/>) next to <see cref="Main"/>: camera presets,
/// the demo road, the picking check and the frame bench. It only ever exists in debug builds; release builds never
/// construct it, so the flags are inert there.
/// </summary>
internal sealed class DebugDriver
{
    private readonly DebugLaunchOptions _options;
    private readonly double[] _benchTimes;
    private readonly Vector2 _checkPickPoint;

    private bool _benchVsyncOff;
    private DisplayServer.VSyncMode _benchVsyncSaved = DisplayServer.VSyncMode.Enabled;

    private int _benchTaken;
    private int _benchWarmup;
    private bool _checkPickDone;
    private CameraRig? _rig;
    private TerrainRenderer? _terrain;

    /// <summary>Initializes a new instance of the <see cref="DebugDriver"/> class.</summary>
    /// <param name="options">The parsed debug options.</param>
    public DebugDriver(DebugLaunchOptions options)
    {
        _options = options;

        _benchTimes = [];
        _checkPickPoint = new Vector2(-1f, -1f);
        if (options.BenchFrames is { } benchFrames)
        {
            // The first frames compile shaders and build the island; they are rendered but left out of the stats.
            _benchWarmup = Math.Min(30, benchFrames / 2);
            _benchTimes = new double[Math.Max(1, benchFrames - _benchWarmup)];
        }

        if (!string.IsNullOrEmpty(options.CheckPick))
        {
            string[] parts = options.CheckPick.Split(',');
            if (parts.Length == 2
                && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float sx)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sy))
            {
                _checkPickPoint = new Vector2(sx, sy);
            }
            else
            {
                GD.PushWarning($"[Evenmere] ignoring malformed --check-pick '{options.CheckPick}'; want 'sx,sy'.");
            }
        }
    }

    /// <summary>Applies the one-shot debug paths once the world is running: the camera preset, the grid flag and the demo road.</summary>
    /// <param name="host">The running simulation host.</param>
    /// <param name="rig">The camera rig, when the scene has one.</param>
    /// <param name="terrain">The terrain renderer, when the scene has one.</param>
    public void Ready(SimHost host, CameraRig? rig, TerrainRenderer? terrain)
    {
        _rig = rig;
        _terrain = terrain;

        if (rig is null)
        {
            return;
        }

        if (_options.StaticCamera)
        {
            // Scripted runs share the machine's resting cursor, whose edge would otherwise pan the view by a
            // different amount every run. A still camera frames every screenshot identically.
            rig.InputEnabled = false;
            rig.EdgeScrollEnabled = false;
        }

        if (terrain is not null && _options.ShowGrid)
        {
            terrain.SetGridVisible(true);
        }

        switch (_options.CameraView)
        {
            case "":
                break;
            case "overview":
                rig.FocusOnTile(new Vector2I(64, 62));
                rig.SetZoom(190f, true);
                break;
            case "landing":
                rig.FocusOnTile(new Vector2I(52, 86));
                rig.SetZoom(55f, true);
                break;
            case "slopes":
                rig.FocusOnTile(new Vector2I(72, 48));
                rig.SetZoom(55f, true);
                break;
            case "mouth":
                rig.FocusOnTile(new Vector2I(16, 74));
                rig.SetZoom(42f, true);
                break;
            default:
                GD.PushWarning($"[Evenmere] unknown --camera-view '{_options.CameraView}'; want 'overview', 'landing', 'slopes' or 'mouth'.");
                break;
        }

        if (_options.DemoDirty)
        {
            LayDemoRoad(host);
        }
    }

    /// <summary>Runs the per-frame debug paths: the picking check, then the frame bench.</summary>
    /// <param name="main">The scene root, used to quit once a check prints.</param>
    /// <param name="delta">The real time since the last frame.</param>
    public void Process(Main main, double delta)
    {
        RunCheckPick(main);
        RunBench(main, delta);
    }

    // Picks once at a screen point and prints the tile and its rendered surface height, proving the camera's picking
    // lands on the visible terrain (the height comes from the same function the terrain renderer installed).
    private void RunCheckPick(Main main)
    {
        if (_checkPickDone || _checkPickPoint.X < 0f)
        {
            return;
        }

        // Frame 60: the terrain, water and camera have all settled (the island builds on frame 1).
        if (Engine.GetProcessFrames() < 60)
        {
            return;
        }

        _checkPickDone = true;
        if (_rig is null || _terrain is null)
        {
            GD.Print("[Evenmere] PICK no camera or terrain");
        }
        else if (_rig.TryPickTile(_checkPickPoint, out Vector2I tile))
        {
            GD.Print($"[Evenmere] PICK tile=({tile.X}, {tile.Y}) height={_terrain.GetSurfaceHeight(tile.X, tile.Y):F3}");
        }
        else
        {
            GD.Print("[Evenmere] PICK miss");
        }

        main.GetTree().Quit(0);
    }

    private void RunBench(Main main, double delta)
    {
        if (_benchTimes.Length == 0 || _benchTaken >= _benchTimes.Length)
        {
            return;
        }

        if (!_benchVsyncOff)
        {
            // The frame-time baseline must not depend on the display in the room: measure uncapped, then restore.
            _benchVsyncOff = true;
            if (DisplayServer.GetName() != "headless")
            {
                _benchVsyncSaved = (DisplayServer.VSyncMode)DisplayServer.WindowGetVsyncMode((int)DisplayServer.MainWindowId);
                DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled, (int)DisplayServer.MainWindowId);
            }
        }

        if (_benchWarmup > 0)
        {
            _benchWarmup--;
            return;
        }

        _benchTimes[_benchTaken++] = delta;
        if (_benchTaken < _benchTimes.Length)
        {
            return;
        }

        double[] sorted = (double[])_benchTimes.Clone();
        Array.Sort(sorted);
        double sum = 0.0;
        foreach (double sample in _benchTimes)
        {
            sum += sample;
        }

        double avgMs = (sum / sorted.Length) * 1000.0;
        double p99Ms = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.99))] * 1000.0;
        double fps = avgMs > 0.0 ? 1000.0 / avgMs : 0.0;
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetVsyncMode(_benchVsyncSaved, (int)DisplayServer.MainWindowId);
        }

        GD.Print($"BENCH frames={sorted.Length} avg_ms={avgMs:F2} p99_ms={p99Ms:F2} fps={fps:F1}");
        main.GetTree().Quit(0);
    }

    // Lays a short debug road on free plains near the hall, so a dirty rebuild can be watched in the log. It searches a
    // few candidate rows through the placement query and places the first fully valid path.
    private static void LayDemoRoad(SimHost host)
    {
        try
        {
            BuildingTypeId roadType = host.Content.Buildings.IdOf("dirt_road");
            for (int y = 88; y <= 100; y++)
            {
                for (int x = 34; x <= 52; x++)
                {
                    var path = new TileCoord[] { new(x, y), new(x + 1, y), new(x + 2, y), new(x + 3, y), new(x + 4, y), new(x + 5, y) };
                    if (host.Query.Placement.ValidateRoad(path, roadType).CanPlace)
                    {
                        host.Enqueue(new Evenmere.Sim.Systems.Roads.Commands.PlaceRoad(path, roadType));
                        GD.Print($"[Evenmere] Demo: placed debug road at ({x}, {y}).");
                        return;
                    }
                }
            }

            GD.Print("[Evenmere] Demo: no valid debug road found near the Landing.");
        }
        catch (Exception ex)
        {
            GD.Print($"[Evenmere] Demo: debug road failed: {ex.Message.ReplaceLineEndings(" ")}");
        }
    }
}
