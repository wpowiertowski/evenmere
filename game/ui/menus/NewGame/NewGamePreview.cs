using Evenmere.Content;
using Evenmere.Content.Maps;

namespace Evenmere.Game.Ui.Menus.NewGame;

/// <summary>
/// The new-game preview thumbnail (M2-25): a pure, Godot-free render of a <see cref="MapDef"/> to RGBA bytes,
/// one pixel per tile in top-down row order. Only the terrain layer is painted; the river-width, region, deposit
/// and fertility layers are ignored on purpose (river tiles already carry the "river" terrain). The colour table
/// mirrors the shared palette resource (<c>game/data/visuals/palette.tres</c>, the <c>terrain.*</c> tokens)
/// read-only: the sRGB values are copied here and the palette itself is never edited. The screen uploads the
/// bytes to an <c>ImageTexture</c> on the main thread; the hash test pins the mapping, so the same seed always
/// gives the same thumbnail. This class has no Godot types, so the game tests cover it without the engine.
/// </summary>
public static class NewGamePreview
{
    /// <summary>
    /// Maps a world seed (the ulong the simulation runs on) to the integer seed the island generator takes:
    /// the low 31 bits, so small seeds pass through unchanged. The mapping is fixed, so previews,
    /// generation and saves always agree on which island a seed means.
    /// </summary>
    /// <param name="worldSeed">The world seed.</param>
    /// <returns>The generator seed, never negative.</returns>
    public static int GeneratorSeed(ulong worldSeed) => unchecked((int)(worldSeed & 0x7fffffff));

    /// <summary>
    /// Renders the island's terrain to RGBA bytes: <c>Width × Height × 4</c> bytes, fully opaque, one pixel
    /// per tile, rows from the top (north) edge like every other map layer. Terrain names come from
    /// <paramref name="content"/>, so the bytes survive content ID reassignment.
    /// </summary>
    /// <param name="map">The island to preview, generated or authored.</param>
    /// <param name="content">The loaded content, for terrain names.</param>
    /// <returns>The RGBA bytes.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static byte[] Render(MapDef map, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(content);

        byte[] rgba = new byte[checked(map.TileCount * 4)];
        for (int i = 0; i < map.TileCount; i++)
        {
            string name = content.Terrains.NameOf(map.Terrain[i]);
            (byte r, byte g, byte b) = ColourOf(name);
            rgba[(i * 4) + 0] = r;
            rgba[(i * 4) + 1] = g;
            rgba[(i * 4) + 2] = b;
            rgba[(i * 4) + 3] = 255;
        }

        return rgba;
    }

    // The terrain colours as authored sRGB bytes in palette.tres (terrain.*). An unknown terrain falls back
    // to magenta, which is loud on purpose: content defines the terrains in content/terrains/, so a preview
    // should never need the fallback, and a magenta island says so immediately.
    private static (byte R, byte G, byte B) ColourOf(string terrain) => terrain switch
    {
        "plains" => (115, 161, 84),
        "forest" => (61, 115, 66),
        "beach" => (217, 199, 147),
        "hills" => (140, 133, 89),
        "highlands" => (128, 119, 102),
        "peaks" => (191, 189, 184),
        "fen" => (140, 158, 102),
        "marsh" => (89, 107, 77),
        "reef" => (64, 153, 153),
        "sea" => (51, 107, 128),
        "deep_water" => (26, 59, 97),
        "river" => (94, 147, 163),
        _ => (255, 0, 255),
    };
}
