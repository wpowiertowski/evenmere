namespace Evenmere.Game.Rendering.Look;

/// <summary>
/// The non-debug look choice after Godot's <c>--</c> separator: <c>--look=classic</c> restores the M1 materials,
/// perspective camera and neutral sun, while anything else (or nothing) runs the hand-drawn look. The default is
/// hand-drawn in every build, including release and the headless smoke; the setting is read where the camera and
/// the renderers start up, so <c>game/host</c> never hears about it. Debug builds additionally honour the
/// <c>EVENMERE_*</c> experiment switches (<see cref="LookSettings"/>), which override this choice.
/// The parser is pure (no engine calls) so it can be unit tested without Godot.
/// </summary>
public static class LookOption
{
    private const string LookPrefix = "--look=";

    /// <summary>Reads the look choice from the user arguments.</summary>
    /// <param name="userArgs">The user arguments, as <c>OS.GetCmdlineUserArgs()</c> returns them.</param>
    /// <returns><see cref="LookMode.Classic"/> for <c>--look=classic</c>, else <see cref="LookMode.HandDrawn"/>.</returns>
    /// <exception cref="FormatException">A <c>--look</c> argument names an unknown look.</exception>
    public static LookMode Parse(IEnumerable<string> userArgs)
    {
        foreach (string arg in userArgs)
        {
            if (!arg.StartsWith(LookPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string text = arg[LookPrefix.Length..];
            if (string.Equals(text, "classic", StringComparison.OrdinalIgnoreCase))
            {
                return LookMode.Classic;
            }

            if (string.Equals(text, "handdrawn", StringComparison.OrdinalIgnoreCase))
            {
                return LookMode.HandDrawn;
            }

            throw new FormatException($"--look needs 'classic' or 'handdrawn', not '{arg}'.");
        }

        return LookMode.HandDrawn;
    }
}
