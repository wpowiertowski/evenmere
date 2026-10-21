using System.Globalization;

namespace Evenmere.Game.Host.Debug;

/// <summary>
/// The debug-only command-line choices after Godot's <c>--</c> separator. They are parsed and applied only in debug
/// builds (<see cref="Godot.OS.IsDebugBuild"/>); release builds ignore them. Arguments that are not recognised are
/// ignored, so other tooling can share the command line. Every flag is documented here, in one place.
/// </summary>
/// <param name="BenchFrames">
/// <c>--bench-frames=N</c>: render N frames on the starter island, print one
/// <c>BENCH avg_ms=&lt;x&gt; p99_ms=&lt;y&gt; fps=&lt;z&gt;</c> line and quit. The first frames compile shaders and build
/// the island; they are rendered but left out of the stats.
/// </param>
/// <param name="CameraView">
/// <c>--camera-view=NAME</c>: move the camera to a debug preset for screenshots — <c>overview</c> (the whole island),
/// <c>landing</c> (the Landing peninsula with both rivers), <c>slopes</c> (across the hills into the highland core)
/// or <c>mouth</c> (the wide river meeting the sea on the west coast). Empty keeps the default view.
/// Unknown names warn and keep the default.
/// </param>
/// <param name="StaticCamera">
/// <c>--static-camera</c>: the camera ignores all input, so scripted screenshots frame identically run after run.
/// (Without it the shared machine's resting cursor can edge-pan a different amount every run.)
/// </param>
/// <param name="ShowGrid">
/// <c>--show-grid</c>: start with the terrain tile-grid overlay on (it is off by default; the G key toggles it).
/// </param>
/// <param name="CheckPick">
/// <c>--check-pick=sx,sy</c>: once the world is built, pick the tile under that screen point, print one
/// <c>PICK tile=(x, y) height=h</c> line proving picking lands on the visible surface, and quit.
/// </param>
/// <param name="DemoDirty">
/// <c>--demo-dirty</c>: lay a debug road near the Landing through <c>SimHost</c> after starting, so a dirty-chunk
/// rebuild can be watched in the log. Roads are free and always valid somewhere near the hall; when none fits, it
/// logs that instead of failing.
/// </param>
/// <param name="DemoBuildings">
/// <c>--demo-buildings</c>: unlock the slice techs and civic, place one of each slice building near the hall with roads
/// connecting them (through <c>SimHost.Enqueue</c>), pause one house next to an active one, and leave the last
/// buildings as sites, for the M1-20 screenshots. The world is paused afterwards so every shot is deterministic.
/// </param>
/// <param name="CartOverlay">
/// <c>--cart-overlay</c>: log each transport job's predicted arrival tick (<c>PhaseEndTick</c>) and, on
/// <c>TransportJobCompleted</c>, the visual arrival tick against the event tick, for the M1-22 evidence. The world is
/// left running (unpaused) so jobs keep flowing past the demo setup.
/// </param>
/// <param name="LifeWalkers">
/// <c>--life-walkers=N</c>: spawn N synthetic walkers spread over the existing buildings for the M1-22 walker bench,
/// ignoring the residents rule but using the same routes, caching and MultiMesh path. Zero keeps the rule.
/// </param>
internal sealed record DebugLaunchOptions(
    int? BenchFrames,
    string CameraView,
    bool StaticCamera,
    bool ShowGrid,
    string CheckPick,
    bool DemoDirty,
    bool DemoBuildings,
    bool CartOverlay,
    int LifeWalkers)
{
    private const string BenchFramesPrefix = "--bench-frames";
    private const string CameraViewPrefix = "--camera-view=";
    private const string StaticCameraFlag = "--static-camera";
    private const string ShowGridFlag = "--show-grid";
    private const string CheckPickPrefix = "--check-pick=";
    private const string DemoDirtyFlag = "--demo-dirty";
    private const string DemoBuildingsFlag = "--demo-buildings";
    private const string CartOverlayFlag = "--cart-overlay";
    private const string LifeWalkersPrefix = "--life-walkers=";

    /// <summary>The options for a normal run: no bench, default camera, no checks.</summary>
    public static DebugLaunchOptions None => new(null, string.Empty, false, false, string.Empty, false, false, false, 0);

    /// <summary>Reads the debug options from the user arguments.</summary>
    /// <param name="userArgs">The user arguments, as <c>OS.GetCmdlineUserArgs()</c> returns them.</param>
    /// <returns>The options, with defaults for anything not given.</returns>
    /// <exception cref="FormatException">A recognised argument has a missing or malformed value.</exception>
    public static DebugLaunchOptions Parse(IEnumerable<string> userArgs)
    {
        int? benchFrames = null;
        string cameraView = string.Empty;
        bool staticCamera = false;
        bool showGrid = false;
        string checkPick = string.Empty;
        bool demoDirty = false;
        bool demoBuildings = false;
        bool cartOverlay = false;
        int lifeWalkers = 0;

        foreach (string arg in userArgs)
        {
            if (arg.StartsWith(BenchFramesPrefix, StringComparison.Ordinal))
            {
                string text = arg.StartsWith(BenchFramesPrefix + "=", StringComparison.Ordinal) ? arg[(BenchFramesPrefix.Length + 1)..] : string.Empty;
                benchFrames = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
                    ? parsed
                    : throw new FormatException($"--bench-frames needs a non-negative whole number, not '{text}'.");
            }
            else if (arg.StartsWith(CameraViewPrefix, StringComparison.Ordinal))
            {
                cameraView = arg[CameraViewPrefix.Length..];
            }
            else if (arg == StaticCameraFlag)
            {
                staticCamera = true;
            }
            else if (arg == ShowGridFlag)
            {
                showGrid = true;
            }
            else if (arg.StartsWith(CheckPickPrefix, StringComparison.Ordinal))
            {
                checkPick = arg[CheckPickPrefix.Length..];
            }
            else if (arg == DemoDirtyFlag)
            {
                demoDirty = true;
            }
            else if (arg == DemoBuildingsFlag)
            {
                demoBuildings = true;
            }
            else if (arg == CartOverlayFlag)
            {
                cartOverlay = true;
            }
            else if (arg.StartsWith(LifeWalkersPrefix, StringComparison.Ordinal))
            {
                string text = arg[LifeWalkersPrefix.Length..];
                lifeWalkers = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
                    ? parsed
                    : throw new FormatException($"--life-walkers needs a non-negative whole number, not '{text}'.");
            }
        }

        return new DebugLaunchOptions(benchFrames, cameraView, staticCamera, showGrid, checkPick, demoDirty, demoBuildings, cartOverlay, lifeWalkers);
    }
}
