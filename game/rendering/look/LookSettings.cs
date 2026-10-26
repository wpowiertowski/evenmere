using System.Globalization;
using Godot;

namespace Evenmere.Game.Rendering.Look;

/// <summary>The rendering look: the M1 flat-shaded placeholder look, or the hand-drawn look.</summary>
public enum LookMode
{
    /// <summary>The M1 look: perspective camera, smooth shading, neutral sun. Selected with <c>--look=classic</c>.</summary>
    Classic = 0,

    /// <summary>The hand-drawn look: ink outlines, banded light, paper grain and a warm grade. This is the default.</summary>
    HandDrawn = 1,
}

/// <summary>
/// The look switches (M2-29). Hand-drawn plus orthographic is the default in every build, including release and
/// the headless smoke; <c>--look=classic</c> (<see cref="LookOption"/>) restores the M1 look until the settings
/// screen owns a toggle. Debug builds additionally honour three environment variables for experiments, which
/// override the flag: <c>EVENMERE_LOOK=handdrawn|classic</c> forces the look, <c>EVENMERE_PROJECTION=ortho</c>
/// forces the orthographic camera, and <c>EVENMERE_YAW=45</c> locks the yaw offset in degrees (otherwise it blends
/// from 30° to 45° with zoom).
/// </summary>
public static class LookSettings
{
    /// <summary>The environment variable that forces the look in debug builds: <c>handdrawn</c> or <c>classic</c>.</summary>
    public const string LookVariable = "EVENMERE_LOOK";

    /// <summary>The environment variable that forces the orthographic camera in debug builds (<c>ortho</c>).</summary>
    public const string ProjectionVariable = "EVENMERE_PROJECTION";

    /// <summary>The environment variable that locks the camera yaw offset in degrees in debug builds.</summary>
    public const string YawVariable = "EVENMERE_YAW";

    /// <summary>
    /// Gets the active look for user arguments: the debug environment override when it names a look, else the
    /// <c>--look</c> flag (hand-drawn by default).
    /// </summary>
    /// <param name="userArgs">The user arguments, as <c>OS.GetCmdlineUserArgs()</c> returns them.</param>
    /// <returns>The look every renderer and the camera agree on.</returns>
    public static LookMode ActiveMode(IEnumerable<string> userArgs)
    {
        if (OS.IsDebugBuild())
        {
            string? env = System.Environment.GetEnvironmentVariable(LookVariable);
            if (string.Equals(env, "handdrawn", StringComparison.OrdinalIgnoreCase))
            {
                return LookMode.HandDrawn;
            }

            if (string.Equals(env, "classic", StringComparison.OrdinalIgnoreCase))
            {
                return LookMode.Classic;
            }
        }

        return LookOption.Parse(userArgs);
    }

    /// <summary>Gets the active look for this run, reading the launch arguments directly.</summary>
    public static LookMode ActiveLook => ActiveMode(OS.GetCmdlineUserArgs());

    /// <summary>Gets whether the requested projection is orthographic (<c>ortho</c>); anything else is perspective.</summary>
    public static bool WantsOrthographic =>
        string.Equals(System.Environment.GetEnvironmentVariable(ProjectionVariable), "ortho", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the requested yaw offset in degrees, or <see langword="null"/> to keep the zoom blend.
    /// </summary>
    /// <returns>The parsed degrees, or <see langword="null"/> when the variable is empty or not a number.</returns>
    public static float? YawOverrideDegrees()
    {
        string? text = System.Environment.GetEnvironmentVariable(YawVariable);
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float degrees)
            ? degrees
            : null;
    }

    /// <summary>
    /// Applies the debug-only camera overrides to a rig: the projection switch and the yaw lock. It is a no-op
    /// in release builds, where the environment variables are ignored.
    /// </summary>
    /// <param name="rig">The camera rig to configure.</param>
    public static void ApplyCameraOverrides(Camera.CameraRig rig)
    {
        if (!OS.IsDebugBuild())
        {
            return;
        }

        if (WantsOrthographic)
        {
            rig.Projection = Camera.CameraRig.CameraProjection.Orthographic;
        }

        if (YawOverrideDegrees() is { } degrees)
        {
            rig.OverrideYawOffset(degrees);
        }
    }
}
