using Evenmere.Game.Controls;
using Evenmere.Game.Host.Debug;
using Evenmere.Game.Host.Persistence;
using Evenmere.Game.Host.Smoke;
using Evenmere.Sim.Persistence;
using Evenmere.Game.Rendering.Camera;
using Evenmere.Game.Rendering.Terrain;
using Evenmere.Game.Ui.Menus;
using Godot;

namespace Evenmere.Game.Host;

/// <summary>
/// The root of <c>Main.tscn</c>. It reads the command line and either starts the world on the <see cref="SimHost"/>
/// child straight away, runs a headless check and quits, or shows the main menu over the empty scene:
/// <list type="bullet">
/// <item><description>Smoke mode (<c>--smoke-ticks=N</c>) runs the headless check and quits.</description></item>
/// <item><description>Any other user argument starting with <c>--</c> (<c>--seed</c>, <c>--map</c>, the debug flags)
/// skips the menu and starts a new game exactly as before, so the headless smoke and the debug
/// drivers keep working unchanged. <c>--menu-selftest</c> is a debug-only flag handled the same way.</description></item>
/// <item><description>With no user arguments, the main menu shows and no simulation runs until the player starts or
/// loads a game.</description></item>
/// </list>
/// Debug builds additionally run the debug driver (<see cref="DebugDriver"/>) for screenshots, the bench and the
/// picking check; release builds ignore those flags.
/// </summary>
public sealed partial class Main : Node3D
{
    private static readonly Vector2I _minimumWindowSize = new(1280, 800);
    private float _minSizeScreen = -1f;

    private bool _startedMaximized;

    /// <summary>The menu scene instantiated for menu mode.</summary>
    private const string MenuScenePath = "res://scenes/MainMenu.tscn";

    private readonly CommandLogRecorder _recorder = new();
    private DebugDriver? _debug;

    private MainMenu? _menu;
    private CanvasLayer? _menuRoot;

    /// <inheritdoc />
    public override void _Ready()
    {
        SimHost host = GetNode<SimHost>("SimHost");
        PauseMenu? pause = GetNodeOrNull<PauseMenu>("PauseMenu/Root");
        string[] args = OS.GetCmdlineUserArgs();
        ApplyWindowMode(args);

        // Every enqueued command lands in the active slot's log with the tick it is applied on; with no active slot
        // (a new game before its first save) the recorder stays quiet.
        host.CommandEnqueued += (appliedTick, command) => _recorder.Record(appliedTick, command);

        if (pause is not null)
        {
            pause.Recorder = _recorder;
            pause.QuitToMenuRequested += () => ShowMenu(host, pause);
            pause.FullscreenToggleRequested += ToggleFullscreen;
        }

        if (LaunchOptions.RequestsSmoke(args))
        {
            RunSmoke(host, args);
            return;
        }

        if (args.Any(static arg => arg.StartsWith("--", StringComparison.Ordinal)))
        {
            var options = LaunchOptions.Parse(args);
            host.StartNewGame(options.Seed, options.Map);

            // The self-test is debug-only: in release builds the flag is ignored like the other debug flags.
            if (OS.IsDebugBuild() && args.Contains(MenuSelfTest.Flag, StringComparer.Ordinal))
            {
                MenuSelfTest.Run(this, host, _recorder);
                return;
            }

            if (OS.IsDebugBuild())
            {
                var driver = new DebugDriver(DebugLaunchOptions.Parse(args));
                _debug = driver;
                driver.Ready(
                    host,
                    GetNodeOrNull<CameraRig>("CameraRig"),
                    GetNodeOrNull<TerrainRenderer>("Terrain"));
            }

            pause?.SetMenuMode(false);
            return;
        }

        pause?.SetMenuMode(true);
        ShowMenu(host, pause);
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        // Unhandled only, so typing in a text field never toggles the window.
        if (@event.IsActionPressed(InputActions.ToggleFullscreen))
        {
            ToggleFullscreen();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Applies the window contract (M1-35, M2-31): every run keeps the 1280 × 800 minimum, a normal launch starts
    /// maximized, and the fixed-size runs (the headless smoke, screenshots, the frame bench and the debug
    /// <c>--window-size</c> override) keep the exact launch size. Maximizing happens here in code, rather than through
    /// <c>window/size/mode</c> in project.godot, because the choice depends on the launch options: a project setting
    /// could not exempt the fixed-size runs.
    /// </summary>
    /// <param name="args">The user arguments, as <c>OS.GetCmdlineUserArgs()</c> returns them.</param>
    private void ApplyWindowMode(string[] args)
    {
        Window window = GetWindow();

        // The 1280 x 800 minimum counts logical points; Window.MinSize counts physical pixels (backing store),
        // so it is scaled by the screen scale and re-applied in _Process when the scale changes.
        ApplyMinSize();

        // Debug only: an exact window size in logical points for the evidence screenshots, so the 1280 × 800,
        // 1600 × 900 and 2560 × 1600 shots frame those sizes exactly. Window.Size counts physical pixels, so the
        // points are scaled up. Un-maximize first: assigning a size to a maximized window would otherwise fight
        // the window manager.
        if (OS.IsDebugBuild() && WindowSetup.TryParseWindowSize(args, out int width, out int height))
        {
            float scale = ScreenScale();
            window.Mode = Window.ModeEnum.Windowed;
            window.Size = new Vector2I(Mathf.RoundToInt(width * scale), Mathf.RoundToInt(height * scale));
            GD.Print($"[Evenmere] window: debug override {width}x{height} points (minimum 1280x800); F11 or the pause menu toggles fullscreen.");
            return;
        }

        if (!WindowSetup.ShouldMaximize(args))
        {
            GD.Print($"[Evenmere] window: keeping the launch size {window.Size.X}x{window.Size.Y} (minimum 1280x800) for a fixed-size {WindowSetup.FixedSizeReason(args)} run; F11 or the pause menu toggles fullscreen.");
            return;
        }

        if (DisplayServer.GetName() == "headless")
        {
            GD.Print($"[Evenmere] window: keeping the launch size {window.Size.X}x{window.Size.Y} on a headless display; a desktop run would start maximized. F11 or the pause menu toggles fullscreen.");
            return;
        }

        window.Mode = Window.ModeEnum.Maximized;
        _startedMaximized = true;
        GD.Print("[Evenmere] window: starting maximized (minimum 1280x800); F11 or the pause menu toggles fullscreen.");
    }

    /// <summary>
    /// Switches between fullscreen and the windowed mode, restoring the maximized state a normal launch started in.
    /// The pause menu's Fullscreen button calls this through <c>PauseMenu.FullscreenToggleRequested</c>.
    /// </summary>
    public void ToggleFullscreen()
    {
        Window window = GetWindow();
        window.Mode = window.Mode == Window.ModeEnum.Fullscreen
            ? (_startedMaximized ? Window.ModeEnum.Maximized : Window.ModeEnum.Windowed)
            : Window.ModeEnum.Fullscreen;
        GD.Print($"[Evenmere] window: fullscreen toggle, mode is now {window.Mode}.");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        ApplyMinSize();
        _debug?.Process(this, delta);
    }

    /// <summary>Reads the display's HiDPI scale for the window's current screen; non-positive counts as 1.</summary>
    /// <returns>The screen scale (2 on a Retina Mac, 1 on a Steam Deck).</returns>
    private static float ScreenScale()
    {
        float screen = DisplayServer.ScreenGetScale((int)DisplayServer.ScreenOfMainWindow);
        return screen > 0f ? screen : 1f;
    }

    // The minimum window size is 1280 x 800 logical points, but Window.MinSize counts physical pixels: scale it by
    // the screen scale, and re-apply it when the scale changes (the window moving to another display).
    private void ApplyMinSize()
    {
        float screen = DisplayServer.ScreenGetScale((int)DisplayServer.ScreenOfMainWindow);
        if (screen == _minSizeScreen)
        {
            return;
        }

        _minSizeScreen = screen;
        float scale = ScreenScale();
        GetWindow().MinSize = new Vector2I(Mathf.RoundToInt(_minimumWindowSize.X * scale), Mathf.RoundToInt(_minimumWindowSize.Y * scale));
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _recorder.Dispose();
    }

    private void ShowMenu(SimHost host, PauseMenu? pause)
    {
        pause?.SetMenuMode(true);
        if (_menu is null)
        {
            PackedScene scene = ResourceLoader.Load<PackedScene>(MenuScenePath);
            CanvasLayer menuRoot = scene.Instantiate<CanvasLayer>();
            MainMenu menu = menuRoot.GetNode<MainMenu>("Root");
            menu.Host = host;
            menu.Recorder = _recorder;
            menu.PlayStarted += () =>
            {
                menuRoot.Visible = false;
                pause?.SetMenuMode(false);
            };
            AddChild(menuRoot);
            _menuRoot = menuRoot;
            _menu = menu;
        }

        if (_menuRoot is not null)
        {
            _menuRoot.Visible = true;
        }

        _menu?.Refresh();
    }

    /// <summary>
    /// The headless check behind <c>scripts/godot-smoke.sh</c>: without <c>--smoke-save</c> it creates the world and runs N ticks
    /// as fast as possible; with a save and log it loads the save and replays the log across real frames with the full
    /// scene active. Either way it prints one <c>SMOKE OK hash=&lt;16 hex digits&gt;</c> line and exits 0, or prints
    /// <c>SMOKE FAIL &lt;message&gt;</c> and exits 1.
    /// </summary>
    private void RunSmoke(SimHost host, string[] args)
    {
        // This is the top-level handler of a test entry point: every failure must become SMOKE FAIL and exit code 1.
        try
        {
            var options = LaunchOptions.Parse(args);
            if (options.SmokeSave is not null || options.SmokeLog is not null)
            {
                RunSmokeV2(host, options);
                return;
            }

            host.StartNewGame(options.Seed, options.Map);
            host.RunTicksNow(options.SmokeTicks ?? 0);
            GD.Print($"SMOKE OK hash={host.ComputeStateHash():x16}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.Print($"SMOKE FAIL {ex.Message.ReplaceLineEndings(" ")}");
            GD.PrintErr(ex.ToString());
            GetTree().Quit(1);
        }
    }

    private void RunSmokeV2(SimHost host, LaunchOptions options)
    {
        if (options.SmokeSave is null || options.SmokeLog is null)
        {
            throw new InvalidOperationException("Smoke v2 needs both --smoke-save and --smoke-log.");
        }

        host.LoadGame(options.SmokeSave);
        using FileStream log = File.OpenRead(options.SmokeLog);
        IReadOnlyList<(long Tick, Sim.Command Command)> entries = CommandLogReader.ForContent(log, host.Content);
        var runner = new SmokeRunner();
        AddChild(runner);
        runner.Setup(host, entries, options.SmokeTicks ?? 0);
    }

    /// <summary>
}
