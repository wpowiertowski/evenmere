using Godot;

namespace Evenmere.Game.Host;

/// <summary>
/// The root of <c>Main.tscn</c>. It reads the command line and starts the world on the <see cref="SimHost"/> child, or,
/// in smoke mode, runs the headless check and quits.
/// </summary>
public sealed partial class Main : Node3D
{
    private static readonly Vector2I _minimumWindowSize = new(1280, 800);

    /// <inheritdoc />
    public override void _Ready()
    {
        // Godot has no project setting for the minimum window size.
        GetWindow().MinSize = _minimumWindowSize;

        SimHost host = GetNode<SimHost>("SimHost");
        string[] args = OS.GetCmdlineUserArgs();

        if (LaunchOptions.RequestsSmoke(args))
        {
            RunSmoke(host, args);
            return;
        }

        host.StartNewGame(LaunchOptions.Parse(args).Seed);
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
            host.StartNewGame(options.Seed);
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
}
