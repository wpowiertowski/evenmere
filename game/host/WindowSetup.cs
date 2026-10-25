namespace Evenmere.Game.Host;

/// <summary>
/// The window-size policy (M1-35): a normal launch starts maximized, while fixed-size runs keep the exact launch
/// size (1280 × 800 for screenshots). The fixed-size runs are the headless smoke (<c>--smoke-ticks</c>), the
/// screenshot capture (<c>--screenshot*</c>), the frame bench (<c>--bench-frames</c>) and the debug window-size
/// override (<c>--window-size=WxH</c>). This class is pure and
/// Godot-free so the game unit tests cover it; <see cref="Main"/> applies the answer to the real window and
/// additionally skips maximizing on a headless display.
/// </summary>
internal static class WindowSetup
{
    private const string ScreenshotPrefix = "--screenshot";
    private const string BenchFramesPrefix = "--bench-frames";
    private const string WindowSizePrefix = "--window-size=";

    /// <summary>Tests whether this launch should start with the window maximized.</summary>
    /// <param name="userArgs">The user arguments, as <c>Godot.OS.GetCmdlineUserArgs()</c> returns them.</param>
    /// <returns><see langword="false"/> for the fixed-size runs (smoke, screenshot, bench, window-size), <see langword="true"/> otherwise.</returns>
    internal static bool ShouldMaximize(IEnumerable<string> userArgs)
    {
        if (LaunchOptions.RequestsSmoke(userArgs))
        {
            return false;
        }

        foreach (string arg in userArgs)
        {
            if (arg.StartsWith(ScreenshotPrefix, StringComparison.Ordinal)
                || arg.StartsWith(BenchFramesPrefix, StringComparison.Ordinal)
                || arg.StartsWith(WindowSizePrefix, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads a debug window-size override (<c>--window-size=WxH</c>) from the user arguments. It pins the window to an
    /// exact size in logical points for the screenshot runs, so the evidence shots frame 1280 × 800, 1600 × 900 and
    /// 2560 × 1600 points exactly. Debug builds apply it in <see cref="Main"/>; release builds ignore it like the other
    /// debug flags, but it still counts as a fixed-size run.
    /// </summary>
    /// <param name="userArgs">The user arguments, as <c>Godot.OS.GetCmdlineUserArgs()</c> returns them.</param>
    /// <param name="width">The requested width in points, when the method returns <see langword="true"/>.</param>
    /// <param name="height">The requested height in points, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a valid argument was found; the last valid one wins.</returns>
    internal static bool TryParseWindowSize(IEnumerable<string> userArgs, out int width, out int height)
    {
        width = 0;
        height = 0;
        bool found = false;

        foreach (string arg in userArgs)
        {
            if (!arg.StartsWith(WindowSizePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = arg[WindowSizePrefix.Length..].Split('x');
            if (parts.Length == 2
                && int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int w)
                && int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int h)
                && w > 0 && h > 0)
            {
                width = w;
                height = h;
                found = true;
            }
        }

        return found;
    }

    /// <summary>Names why <see cref="ShouldMaximize"/> returned <see langword="false"/>, for the startup log line.</summary>
    /// <param name="userArgs">The user arguments, as <c>Godot.OS.GetCmdlineUserArgs()</c> returns them.</param>
    /// <returns><c>"smoke"</c>, <c>"screenshot"</c>, <c>"bench"</c>, <c>"window-size"</c> or <c>"normal launch"</c>.</returns>
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

            if (arg.StartsWith(WindowSizePrefix, StringComparison.Ordinal))
            {
                return "window-size";
            }
        }

        return "normal launch";
    }
}
