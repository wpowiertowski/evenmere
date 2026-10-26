using Godot;

namespace Evenmere.Game.Rendering.Look;

/// <summary>
/// The shared hand-drawn palette (M2-29): every art token from <c>docs/art/palette.md</c>, named
/// <c>family.name</c> (for example <c>wood.timber.light</c>). The authored instance lives in
/// <c>game/data/visuals/palette.tres</c>, which takes its tokens verbatim from the palette document; later tracks
/// (ART-02 icons, the ART-03 kit, M2-24 seasonal shifts) colour every part with these tokens and introduce no other
/// colours. The renderer works in linear space, so lookups return the linearised colour while the resource keeps the
/// sRGB hex values human-readable.
/// </summary>
public sealed partial class LookPalette : Resource
{
    /// <summary>Gets or sets the colour per palette token, authored in sRGB.</summary>
    [Export]
    public Godot.Collections.Dictionary<string, Color> Colors { get; set; } = new();

    /// <summary>Gets how many tokens the palette holds. The art bible fixes the count at 81.</summary>
    public int TokenCount => Colors.Count;

    private readonly HashSet<string> _warned = new();

    /// <summary>Looks up the linearised colour for a palette token.</summary>
    /// <param name="token">The token, such as <c>ink.strong</c>.</param>
    /// <returns>The linear palette colour, or a magenta fallback when the token has no entry.</returns>
    public Color ColourOf(string token)
    {
        if (Colors.TryGetValue(token, out Color colour))
        {
            return colour.SrgbToLinear();
        }

        if (_warned.Add(token))
        {
            GD.PushWarning($"[Evenmere] Look: no palette token '{token}'; using the fallback.");
        }

        return new Color(0.7f, 0.3f, 0.7f).SrgbToLinear();
    }

    /// <summary>
    /// Looks up the raw sRGB colour for a palette token, for shader <c>uniform</c> slots. The engine linearises
    /// <c>source_color</c> uniform colours on upload, so uniform-bound colours must stay sRGB (M2-32); only the
    /// vertex-colour path (<see cref="ColourOf"/>) pre-linearises, because vertex buffers pass through raw.
    /// </summary>
    /// <param name="token">The token, such as <c>ink.water</c>.</param>
    /// <returns>The authored sRGB palette colour, or a magenta fallback when the token has no entry.</returns>
    public Color SrgbOf(string token)
    {
        if (Colors.TryGetValue(token, out Color colour))
        {
            return colour;
        }

        if (_warned.Add(token))
        {
            GD.PushWarning($"[Evenmere] Look: no palette token '{token}'; using the fallback.");
        }

        return new Color(0.7f, 0.3f, 0.7f);
    }
}
