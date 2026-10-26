using Godot;

namespace Evenmere.Game.Rendering.Look;

/// <summary>
/// The hand-drawn scene light (M2-29): a warm late-afternoon key with a matched cool ambient, applied over the
/// authored M1 scene only while the hand-drawn look is active. Classic runs keep the neutral M1 sun untouched.
/// The committed scene is never edited; renderers read the live environment afterwards, so their ambient terms
/// follow the override without extra plumbing.
/// </summary>
public static class LookScene
{
    /// <summary>The warm key tint (<c>light.key</c>): the late-afternoon sun shared by buildings, terrain and water.</summary>
    public static readonly Color KeyColor = Color.FromHtml("FFE3B0");

    /// <summary>The key energy: with the ambient below, a lit flat tile renders at about its token.</summary>
    public const float KeyEnergy = 0.8f;

    // NOTE (calibration, 2026-10-25): the default filmic tonemap crushes mid-tones unevenly per channel, so no
    // linear light math can land on the tokens through it. The hand-drawn look uses the linear tonemap:
    // WYSIWYG palette response. Classic keeps the M1 tonemap untouched.

    /// <summary>The cool fill ambient the key is matched against, linear space to match the shader maths.
    /// Calibrated 2026-10-26 with the warm sun: together they land a lit flat tile on its token.</summary>
    public static readonly Color AmbientColor = new(0.2f, 0.38f, 0.65f);

    /// <summary>The ambient energy.</summary>
    public const float AmbientEnergy = 1.0f;

    /// <summary>
    /// Applies the hand-drawn scene light when the look is hand-drawn, and reports whether it did. Otherwise it
    /// does nothing, so the M1 scene light survives untouched.
    /// </summary>
    /// <param name="sceneRoot">The scene root holding the light and the environment (Main's children).</param>
    /// <param name="look">The active look, read once by the caller.</param>
    /// <returns><see langword="true"/> when the hand-drawn light was applied.</returns>
    public static bool ApplyIfEnabled(Node sceneRoot, LookMode look)
    {
        if (look != LookMode.HandDrawn)
        {
            return false;
        }

        bool applied = false;
        DirectionalLight3D? sun = sceneRoot.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
        if (sun is not null)
        {
            GD.Print($"[Evenmere] Look: sun before color={sun.LightColor} energy={sun.LightEnergy}");
            sun.LightColor = KeyColor;
            sun.LightEnergy = KeyEnergy;
            applied = true;
        }
        else
        {
            GD.PushWarning("[Evenmere] Look: no DirectionalLight3D under the scene root; the sun stays M1.");
        }

        WorldEnvironment? holder = sceneRoot.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        Godot.Environment? environment = holder?.Environment;
        if (environment is not null)
        {
            environment.AmbientLightColor = AmbientColor;
            environment.AmbientLightEnergy = AmbientEnergy;
            environment.TonemapMode = Godot.Environment.ToneMapper.Linear;
            applied = true;
        }
        else
        {
            GD.PushWarning("[Evenmere] Look: no WorldEnvironment under the scene root; the ambient stays M1.");
        }

        if (applied)
        {
            GD.Print("[Evenmere] Look: dimmed key and matched cool ambient on.");
        }

        return applied;
    }
}
