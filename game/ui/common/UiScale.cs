using System.Globalization;

namespace Evenmere.Game.Ui.Common;

/// <summary>
/// The UI scale setting's range and parsing, plus the window scale policy (M2-31). The project uses the default
/// <c>disabled</c> stretch mode over a 1600 × 900 design size in logical points, and the UI keeps that design size:
/// the root window's <c>content_scale_factor</c> (see <c>HudRoot.UiScaleFactor</c>) is
/// <c>screenScale × uiScaleSetting × clamp(min(windowPointsW / 1600, windowPointsH / 900), 0.8, 1.0)</c>.
/// So the UI never grows past its design size as the window grows, and it shrinks only below 1600 × 900, down to the
/// 1280 × 800 minimum. <c>screenScale</c> is the display's HiDPI scale (2 on a Retina Mac, 1 on a Steam Deck), because
/// the content scale counts physical pixels while the window size counts logical points. There is no settings screen
/// yet; M4-05 adds one, and until then the value is set in code or by the <c>--ui-scale=N</c> launch argument.
/// The class has no Godot types.
/// </summary>
public static class UiScale
{
    /// <summary>The smallest allowed scale.</summary>
    public const float Minimum = 0.75f;

    /// <summary>The largest allowed scale.</summary>
    public const float Maximum = 1.5f;

    /// <summary>The scale used when nothing else is chosen.</summary>
    public const float Default = 1.0f;

    /// <summary>The UI design width in logical points: the layout is drawn for this size.</summary>
    public const float DesignWidth = 1600f;

    /// <summary>The UI design height in logical points: the layout is drawn for this size.</summary>
    public const float DesignHeight = 900f;

    /// <summary>The smallest the window-size factor shrinks to, matching the 1280 × 800 minimum window.</summary>
    public const float MinWindowFactor = 0.8f;

    /// <summary>The largest the window-size factor grows to: the UI never grows past its design size.</summary>
    public const float MaxWindowFactor = 1.0f;

    private const string ArgumentPrefix = "--ui-scale=";

    /// <summary>Limits a scale to the allowed range.</summary>
    /// <param name="scale">The requested scale.</param>
    /// <returns>The scale between <see cref="Minimum"/> and <see cref="Maximum"/>; <see cref="Default"/> if the request is not a number.</returns>
    public static float Clamp(float scale) => float.IsNaN(scale) ? Default : Math.Clamp(scale, Minimum, Maximum);

    /// <summary>
    /// Converts a window size from physical pixels to logical points. Godot 4 reports window sizes in physical
    /// (backing-store) pixels: <c>Window.Size</c> is "The window's size in pixels" and
    /// <c>DisplayServer.WindowGetSize</c> "Returns the size of the window (in pixels)" (Godot 4.7 C# API reference,
    /// shipped in the 4.7.2 editor bundle). On macOS with <c>display/window/dpi/allow_hidpi</c> (on by default) the
    /// backing store is scaled by the screen scale: the macOS display server drives its layer through Cocoa's
    /// <c>backingScaleFactor</c>, and <c>DisplayServer.ScreenGetScale</c> "is 2.0 for hiDPI (Retina) screens". So a
    /// 1280 x 800-point window on Retina reports as 2560 x 1600 pixels, and points are pixels divided by the scale.
    /// </summary>
    /// <param name="pixels">The size in physical pixels, as <c>Window.Size</c> reports it.</param>
    /// <param name="screenScale">The display's HiDPI scale; non-positive counts as 1.</param>
    /// <returns>The size in logical points.</returns>
    public static float PointsFromPixels(float pixels, float screenScale)
    {
        float screen = screenScale > 0f ? screenScale : 1f;
        return pixels / screen;
    }

    /// <summary>
    /// Gets the window-size factor: how far a window of the given size in logical points sits between the 1600 × 900
    /// design size (1.0) and the 1280 × 800 minimum (0.8). Larger windows stay at 1.0, so the UI keeps its design size
    /// and the extra room shows more of the island instead.
    /// </summary>
    /// <param name="windowPointsW">The window width in logical points.</param>
    /// <param name="windowPointsH">The window height in logical points.</param>
    /// <returns>The fit between <see cref="MinWindowFactor"/> and <see cref="MaxWindowFactor"/>.</returns>
    public static float WindowFactor(float windowPointsW, float windowPointsH)
    {
        float fit = Math.Min(windowPointsW / DesignWidth, windowPointsH / DesignHeight);
        return Math.Clamp(fit, MinWindowFactor, MaxWindowFactor);
    }

    /// <summary>
    /// Gets the root window's content scale for a window of the given size: the HiDPI screen scale times the UI scale
    /// setting times the <see cref="WindowFactor"/>. A 1600 × 900 window at screen scale 1 and at screen scale 2 gives
    /// the same size in points, and a maximized window (such as 1821 × 1024 points) keeps 1.0.
    /// </summary>
    /// <param name="windowPointsW">The window width in logical points.</param>
    /// <param name="windowPointsH">The window height in logical points.</param>
    /// <param name="screenScale">The display's HiDPI scale (2 on a Retina Mac, 1 on a Steam Deck); non-positive counts as 1.</param>
    /// <param name="uiScale">The UI scale setting, clamped to the allowed range.</param>
    /// <returns>The content scale factor to apply to the root window.</returns>
    public static float ContentScale(float windowPointsW, float windowPointsH, float screenScale, float uiScale)
    {
        float screen = screenScale > 0f ? screenScale : 1f;
        return screen * Clamp(uiScale) * WindowFactor(windowPointsW, windowPointsH);
    }

    /// <summary>Looks for <c>--ui-scale=N</c> among the launch arguments.</summary>
    /// <param name="userArgs">The user arguments, as <c>OS.GetCmdlineUserArgs()</c> returns them.</param>
    /// <param name="scale">The requested scale, already clamped, when the argument is present and valid.</param>
    /// <returns><see langword="true"/> if a valid argument was found; the last valid one wins.</returns>
    public static bool TryParseArgument(IEnumerable<string> userArgs, out float scale)
    {
        ArgumentNullException.ThrowIfNull(userArgs);
        scale = Default;
        bool found = false;

        foreach (string arg in userArgs)
        {
            if (arg.StartsWith(ArgumentPrefix, StringComparison.Ordinal)
                && float.TryParse(arg.AsSpan(ArgumentPrefix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                && float.IsFinite(parsed))
            {
                scale = Clamp(parsed);
                found = true;
            }
        }

        return found;
    }
}
