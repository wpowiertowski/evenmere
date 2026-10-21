using System.Globalization;

namespace Evenmere.Game.Host.Debug;

/// <summary>
/// The debug-only command-line choices after Godot's <c>--</c> separator. They are parsed and applied only in debug
/// builds (<see cref="Godot.OS.IsDebugBuild"/>); release builds ignore them. Arguments that are not recognised are
/// ignored, so other tooling can share the command line. Every flag is documented here, in one place.
/// </summary>
/// <param name="LoadSave">
/// <c>--load-save=&lt;path&gt;</c>: load the save at that path through <c>SimHost.LoadGame</c> after the world starts,
/// for the M1-26 evidence runs on <c>tests/fixtures/saves/v1-slice.evsave</c>. Empty loads nothing.
/// </param>
/// <param name="DemoNotifications">
/// <c>--demo-notifications</c>: place three houses and a road-less sawpit near the hall, run days until they finish,
/// so the feed shows a batched toast, then dismiss the oldest advice for the dismissal evidence. Implies a paused world.
/// </param>
/// <param name="DemoFeed">
/// <c>--demo-feed=&lt;mode&gt;</c>: script the feed for screenshots — <c>history:&lt;filter&gt;</c> opens the history with
/// a filter applied, <c>click</c> focuses the newest building notification (logging the inspector open),
/// and <c>collapse</c> collapses the pinned notes to their header chip. Empty does nothing.
/// </param>
/// <param name="BenchFrames">
/// <c>--bench-frames=N</c>: render N frames on the starter island, print one
/// <c>BENCH avg_ms=&lt;x&gt; p99_ms=&lt;y&gt; fps=&lt;z&gt;</c> line and quit. The first frames compile shaders and build
/// the island; they are rendered but left out of the stats.
/// </param>
/// <param name="CameraView">
/// <c>--camera-view=NAME</c>: move the camera to a debug preset for screenshots — <c>overview</c> (the whole island),
/// <c>landing</c> (the Landing peninsula with both rivers), <c>slopes</c> (across the hills into the highland core),
/// <c>mouth</c> (the wide river meeting the sea on the west coast) or <c>hall</c> (close over the Founders' Hall for the
/// M1-21 tool shots). Empty keeps the default view. Unknown names warn and keep the default.
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
/// <param name="ToolBuild">
/// <c>--tool-build=ID</c>: start the M1-21 build tool with the named building type, for the tool screenshots.
/// Empty starts no tool.
/// </param>
/// <param name="ToolRoad">
/// <c>--tool-road=ID</c>: start the M1-21 road tool with the named road type, for the tool screenshots.
/// Empty starts no tool.
/// </param>
/// <param name="ToolDemolish">
/// <c>--tool-demolish</c>: start the M1-21 demolish tool, for the tool screenshots.
/// </param>
/// <param name="ToolTile">
/// <c>--tool-tile=X,Y</c>: pin the tool hover to one tile, so screenshots frame the same ghost every run. A recognised
/// <c>--tool-spot</c> value overrides it.
/// </param>
/// <param name="ToolAnchor">
/// <c>--tool-anchor=X,Y</c>: with <c>--tool-road</c>, show an in-progress drag from the anchor to the hover tile.
/// A recognised <c>--tool-spot</c> value overrides it.
/// </param>
/// <param name="ToolSpot">
/// <c>--tool-spot=want</c>: find a tile by itself — <c>valid</c> (placeable with no warnings), <c>invalid</c> (refused),
/// <c>warning</c> (placeable with a warning, road access preferred), <c>forest</c> (placeable on forest terrain) — and pin
/// the hover there; with the road tool it also sets a drag to a tile six steps east and four south. Unknown wants
/// warn and pin nothing.
/// </param>
/// <param name="ToolInvalid">
/// <c>--tool-invalid</c>: enqueue an out-of-bounds placement for the toast evidence, so the rejection path runs headless.
/// </param>
/// <param name="ToolGamepad">
/// <c>--tool-gamepad</c>: show the gamepad's virtual cursor ring for the gamepad screenshot, even with no pad attached.
/// </param>
/// <param name="DemoExplain">
/// <c>--demo-explain[=bakery|house|hud]</c>: build a stalled-bakery and blocked-house setup for the M1-24
/// screenshots (bakery with no flour because the windmill has no grain, a house blocked by the bread
/// shortage), then open the inspector on the bakery, the house, or show the HUD bread tooltip.
/// Empty means no demo; a bare <c>--demo-explain</c> opens the bakery. The world is paused afterwards.
/// </param>
internal sealed record DebugLaunchOptions(
    string LoadSave,
    bool DemoNotifications,
    string DemoFeed,
    int? BenchFrames,
    string CameraView,
    bool StaticCamera,
    bool ShowGrid,
    string CheckPick,
    bool DemoDirty,
    bool DemoBuildings,
    string DemoExplain,
    string ToolBuild,
    string ToolRoad,
    bool ToolDemolish,
    string ToolTile,
    string ToolAnchor,
    string ToolSpot,
    bool ToolInvalid,
    bool ToolGamepad)
{
    private const string BenchFramesPrefix = "--bench-frames";
    private const string CameraViewPrefix = "--camera-view=";
    private const string StaticCameraFlag = "--static-camera";
    private const string ShowGridFlag = "--show-grid";
    private const string CheckPickPrefix = "--check-pick=";
    private const string DemoDirtyFlag = "--demo-dirty";
    private const string DemoBuildingsFlag = "--demo-buildings";
    private const string DemoExplainPrefix = "--demo-explain";
    private const string ToolBuildPrefix = "--tool-build=";
    private const string ToolRoadPrefix = "--tool-road=";
    private const string ToolDemolishFlag = "--tool-demolish";
    private const string ToolTilePrefix = "--tool-tile=";
    private const string ToolAnchorPrefix = "--tool-anchor=";
    private const string ToolSpotPrefix = "--tool-spot=";
    private const string ToolInvalidFlag = "--tool-invalid";
    private const string ToolGamepadFlag = "--tool-gamepad";
    private const string LoadSavePrefix = "--load-save=";
    private const string DemoNotificationsFlag = "--demo-notifications";
    private const string DemoFeedPrefix = "--demo-feed=";

    /// <summary>The options for a normal run: no bench, default camera, no checks.</summary>
    public static DebugLaunchOptions None => new(string.Empty, false, string.Empty, null, string.Empty, false, false, string.Empty, false, false, string.Empty, string.Empty, string.Empty, false, string.Empty, string.Empty, string.Empty, false, false);

    /// <summary>Reads the debug options from the user arguments.</summary>
    /// <param name="userArgs">The user arguments, as <c>OS.GetCmdlineUserArgs()</c> returns them.</param>
    /// <returns>The options, with defaults for anything not given.</returns>
    /// <exception cref="FormatException">A recognised argument has a missing or malformed value.</exception>
    public static DebugLaunchOptions Parse(IEnumerable<string> userArgs)
    {
        string loadSave = string.Empty;
        bool demoNotifications = false;
        string demoFeed = string.Empty;
        int? benchFrames = null;
        string cameraView = string.Empty;
        bool staticCamera = false;
        bool showGrid = false;
        string checkPick = string.Empty;
        bool demoDirty = false;
        bool demoBuildings = false;
        string demoExplain = string.Empty;
        string toolBuild = string.Empty;
        string toolRoad = string.Empty;
        bool toolDemolish = false;
        string toolTile = string.Empty;
        string toolAnchor = string.Empty;
        string toolSpot = string.Empty;
        bool toolInvalid = false;
        bool toolGamepad = false;

        foreach (string arg in userArgs)
        {
            if (arg.StartsWith(LoadSavePrefix, StringComparison.Ordinal))
            {
                loadSave = arg[LoadSavePrefix.Length..];
            }
            else if (arg == DemoNotificationsFlag)
            {
                demoNotifications = true;
            }
            else if (arg.StartsWith(DemoFeedPrefix, StringComparison.Ordinal))
            {
                demoFeed = arg[DemoFeedPrefix.Length..];
            }
            else if (arg.StartsWith(BenchFramesPrefix, StringComparison.Ordinal))
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
            else if (arg.StartsWith(DemoExplainPrefix, StringComparison.Ordinal))
            {
                demoExplain = arg == DemoExplainPrefix ? "bakery" : arg.StartsWith(DemoExplainPrefix + "=", StringComparison.Ordinal) ? arg[(DemoExplainPrefix.Length + 1)..] : string.Empty;
                if (string.IsNullOrEmpty(demoExplain))
                {
                    demoExplain = "bakery";
                }
            }
            else if (arg.StartsWith(ToolBuildPrefix, StringComparison.Ordinal))
            {
                toolBuild = arg[ToolBuildPrefix.Length..];
            }
            else if (arg.StartsWith(ToolRoadPrefix, StringComparison.Ordinal))
            {
                toolRoad = arg[ToolRoadPrefix.Length..];
            }
            else if (arg == ToolDemolishFlag)
            {
                toolDemolish = true;
            }
            else if (arg.StartsWith(ToolTilePrefix, StringComparison.Ordinal))
            {
                toolTile = arg[ToolTilePrefix.Length..];
            }
            else if (arg.StartsWith(ToolAnchorPrefix, StringComparison.Ordinal))
            {
                toolAnchor = arg[ToolAnchorPrefix.Length..];
            }
            else if (arg.StartsWith(ToolSpotPrefix, StringComparison.Ordinal))
            {
                toolSpot = arg[ToolSpotPrefix.Length..];
            }
            else if (arg == ToolInvalidFlag)
            {
                toolInvalid = true;
            }
            else if (arg == ToolGamepadFlag)
            {
                toolGamepad = true;
            }
        }

        return new DebugLaunchOptions(loadSave, demoNotifications, demoFeed, benchFrames, cameraView, staticCamera, showGrid, checkPick, demoDirty, demoBuildings, demoExplain, toolBuild, toolRoad, toolDemolish, toolTile, toolAnchor, toolSpot, toolInvalid, toolGamepad);
    }
}
