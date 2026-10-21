using Godot;
using Godot.Collections;

namespace Evenmere.Game.Data.Visuals.Terrain;

/// <summary>
/// The terrain palette: the content terrain name (such as <c>plains</c>) to its flat-shaded face colour, authored in
/// sRGB like every other artist-picked colour. The renderer works in linear space, so lookups return the linearised
/// colour; storing the authored values untouched keeps the resource human-readable. A name missing here falls back to
/// <see cref="Fallback"/> and logs once.
/// </summary>
public sealed partial class TerrainPalette : Resource
{
    /// <summary>Gets or sets the colour per content terrain name.</summary>
    [Export]
    public Godot.Collections.Dictionary<string, Color> Colors { get; set; } = new();

    /// <summary>Gets or sets the colour used when a terrain name has no entry.</summary>
    [Export]
    public Color Fallback { get; set; } = new(0.7f, 0.3f, 0.7f);

    private readonly HashSet<string> _warned = new();

    /// <summary>Looks up the colour for a content terrain name, linearised for the renderer.</summary>
    /// <param name="terrainName">The content terrain name.</param>
    /// <returns>The linear palette colour, or the linearised <see cref="Fallback"/> when it has no entry.</returns>
    public Color ColourOf(string terrainName)
    {
        if (Colors.TryGetValue(terrainName, out Color colour))
        {
            return colour.SrgbToLinear();
        }

        if (_warned.Add(terrainName))
        {
            GD.PushWarning($"[Evenmere] TerrainPalette: no colour for terrain '{terrainName}'; using fallback.");
        }

        return Fallback.SrgbToLinear();
    }
}
