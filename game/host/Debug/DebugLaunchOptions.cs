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
internal sealed record DebugLaunchOptions(
    int? BenchFrames,
    string CameraView,
    bool StaticCamera,
    bool ShowGrid,
    string CheckPick,
    bool DemoDirty)
{
    private const string BenchFramesPrefix = "--bench-frames";
    private const string CameraViewPrefix = "--camera-view=";
    private const string StaticCameraFlag = "--static-camera";
    private const string ShowGridFlag = "--show-grid";
    private const string CheckPickPrefix = "--check-pick=";
    private const string DemoDirtyFlag = "--demo-dirty";

    /// <summary>The options for a normal run: no bench, default camera, no checks.</summary>
    public static DebugLaunchOptions None => new(null, string.Empty, false, false, string.Empty, false);

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
        }

        return new DebugLaunchOptions(benchFrames, cameraView, staticCamera, showGrid, checkPick, demoDirty);
    }
}
