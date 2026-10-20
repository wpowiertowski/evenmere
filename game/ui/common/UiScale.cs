using System.Globalization;

namespace Evenmere.Game.Ui.Common;

/// <summary>
/// The UI scale setting's range and parsing. The scale is applied through the root window's <c>content_scale_factor</c>
/// (see <c>HudRoot.UiScale</c>), which multiplies the automatic stretch from the project's 1600 × 900 design size. There is
/// no settings screen yet; M4-05 adds one, and until then the value is set in code or by the <c>--ui-scale=N</c> launch argument.
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

    private const string ArgumentPrefix = "--ui-scale=";

    /// <summary>Limits a scale to the allowed range.</summary>
    /// <param name="scale">The requested scale.</param>
    /// <returns>The scale between <see cref="Minimum"/> and <see cref="Maximum"/>; <see cref="Default"/> if the request is not a number.</returns>
    public static float Clamp(float scale) => float.IsNaN(scale) ? Default : Math.Clamp(scale, Minimum, Maximum);

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
