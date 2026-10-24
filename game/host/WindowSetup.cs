namespace Evenmere.Game.Host;

/// <summary>
/// The window-size policy (M1-35): a normal launch starts maximized, while fixed-size runs keep the exact launch
/// size (1280 × 800 for screenshots). The fixed-size runs are the headless smoke (<c>--smoke-ticks</c>), the
/// screenshot capture (<c>--screenshot*</c>) and the frame bench (<c>--bench-frames</c>). This class is pure and
/// Godot-free so the game unit tests cover it; <see cref="Main"/> applies the answer to the real window and
/// additionally skips maximizing on a headless display.
/// </summary>
internal static class WindowSetup
{
    private const string ScreenshotPrefix = "--screenshot";
    private const string BenchFramesPrefix = "--bench-frames";

    /// <summary>Tests whether this launch should start with the window maximized.</summary>
    /// <param name="userArgs">The user arguments, as <c>Godot.OS.GetCmdlineUserArgs()</c> returns them.</param>
    /// <returns><see langword="false"/> for the fixed-size runs (smoke, screenshot, bench), <see langword="true"/> otherwise.</returns>
    internal static bool ShouldMaximize(IEnumerable<string> userArgs)
    {
        if (LaunchOptions.RequestsSmoke(userArgs))
        {
            return false;
        }

        foreach (string arg in userArgs)
        {
            if (arg.StartsWith(ScreenshotPrefix, StringComparison.Ordinal)
                || arg.StartsWith(BenchFramesPrefix, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Names why <see cref="ShouldMaximize"/> returned <see langword="false"/>, for the startup log line.</summary>
    /// <param name="userArgs">The user arguments, as <c>Godot.OS.GetCmdlineUserArgs()</c> returns them.</param>
    /// <returns><c>"smoke"</c>, <c>"screenshot"</c>, <c>"bench"</c> or <c>"normal launch"</c>.</returns>
    internal static string FixedSizeReason(IEnumerable<string> userArgs)
    {
        if (LaunchOptions.RequestsSmoke(userArgs))
        {
            return "smoke";
        }

        foreach (string arg in userArgs)
        {
            if (arg.StartsWith(ScreenshotPrefix, StringComparison.Ordinal))
            {
                return "screenshot";
            }

            if (arg.StartsWith(BenchFramesPrefix, StringComparison.Ordinal))
            {
                return "bench";
            }
        }

        return "normal launch";
    }
}
