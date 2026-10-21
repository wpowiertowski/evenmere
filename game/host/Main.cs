using Evenmere.Game.Host.Debug;
using Evenmere.Game.Host.Persistence;
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

    /// <summary>The menu scene instantiated for menu mode.</summary>
    private const string MenuScenePath = "res://scenes/MainMenu.tscn";

    private readonly CommandLogRecorder _recorder = new();
    private DebugDriver? _debug;

    private MainMenu? _menu;
    private CanvasLayer? _menuRoot;

    /// <inheritdoc />
    public override void _Ready()
    {
        // Godot has no project setting for the minimum window size.
        GetWindow().MinSize = _minimumWindowSize;

        SimHost host = GetNode<SimHost>("SimHost");
        PauseMenu? pause = GetNodeOrNull<PauseMenu>("PauseMenu/Root");
        string[] args = OS.GetCmdlineUserArgs();

        // Every enqueued command lands in the active slot's log with the tick it is applied on; with no active slot
        // (a new game before its first save) the recorder stays quiet.
        host.CommandEnqueued += (appliedTick, command) => _recorder.Record(appliedTick, command);

        if (pause is not null)
        {
            pause.Recorder = _recorder;
            pause.QuitToMenuRequested += () => ShowMenu(host, pause);
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
    public override void _Process(double delta)
    {
        _debug?.Process(this, delta);
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
    /// The headless check behind <c>scripts/godot-smoke.sh</c>: create the world, run N ticks as fast as possible, print one
    /// <c>SMOKE OK hash=&lt;16 hex digits&gt;</c> line and exit 0, or print <c>SMOKE FAIL &lt;message&gt;</c> and exit 1.
    /// </summary>
    private void RunSmoke(SimHost host, string[] args)
    {
        // This is the top-level handler of a test entry point: every failure must become SMOKE FAIL and exit code 1.
        try
        {
            var options = LaunchOptions.Parse(args);
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

    /// <summary>
}
