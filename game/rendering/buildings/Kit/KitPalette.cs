namespace Evenmere.Game.Rendering.Buildings.Kit;

/// <summary>
/// The ordered palette-token table the building kit paints from. Every entry names a token from the art bible
/// (<c>docs/art/palette.md</c>) with its sRGB hex, so every kit vertex samples to a bible token and the written rules
/// stay the single source of colour. The order is the kit slot encoding: <see cref="KitBuilder"/> records one byte
/// per vertex, and the factory resolves it through <see cref="HexOf"/>. This table moves to M2-29's shared
/// <c>game/data/visuals/palette.tres</c> resource once it lands; until then <c>KitTests.Palette_MatchesArtBible</c>
/// parses the bible and fails on any drift, so a palette change can't silently diverge.
/// </summary>
public static class KitPalette
{
    private static readonly (string Name, string Hex)[] _tokens =
    [
        ("ink.strong", "#2E2620"),
        ("ink.soft", "#57493A"),
        ("paper.highlight", "#FFF8E8"),
        ("paper.shadow", "#E2D3B0"),
        ("ui.accent", "#8A2F1B"),
        ("ui.link", "#1F4E5F"),
        ("wood.beam.dark", "#4A3A26"),
        ("wood.bark.dark", "#59371D"),
        ("wood.plank.mid", "#8A6A45"),
        ("wood.timber.light", "#D0A05C"),
        ("wall.wattle", "#CDBF9B"),
        ("wall.plaster", "#EFE6D2"),
        ("wall.plank", "#A8895A"),
        ("roof.thatch.light", "#D3A944"),
        ("roof.thatch.shadow", "#A8812F"),
        ("roof.shingle", "#8D7B5F"),
        ("roof.canvas", "#E4DCC8"),
        ("roof.stone", "#7A7A74"),
        ("stone.light", "#9B9B95"),
        ("stone.mid", "#7A7A74"),
        ("stone.dark", "#55554F"),
        ("clay.raw", "#8F5636"),
        ("brick.fired", "#B25A38"),
        ("pottery.glaze", "#C46A4A"),
        ("prop.tarp", "#4A6B74"),
        ("prop.rope", "#8A7355"),
        ("prop.net", "#5E6E5A"),
        ("prop.sail", "#EFE6D2"),
        ("leaf.deep", "#2F5B2A"),
        ("leaf.orchard", "#5E8C3E"),
        ("leaf.fen", "#8C9E66"),
        ("field.soil", "#6B4E2E"),
        ("field.grain", "#D3A944"),
        ("field.pasture", "#7FA35A"),
        ("field.fallow", "#9A7B52"),
        ("water.river", "#5E93A3"),
        ("good.wood", "#59371D"),
        ("good.bread", "#A9602F"),
        ("good.fish", "#3E7E96"),
        ("good.apples", "#BE3B28"),
        ("good.flour", "#F2EAD8"),
        ("good.grain", "#D3A944"),
        ("good.pottery", "#B25A38"),
        ("good.wool", "#D5CCB2"),
    ];

    /// <summary>Gets how many palette tokens the kit can paint with.</summary>
    public static int Count => _tokens.Length;

    /// <summary>Gets the token name for a kit slot.</summary>
    /// <param name="slot">The kit slot, from <see cref="KitBuilder"/>.</param>
    /// <returns>The palette token name, such as <c>wall.wattle</c>.</returns>
    public static string NameOf(byte slot) => _tokens[slot].Name;

    /// <summary>Gets the sRGB hex for a kit slot.</summary>
    /// <param name="slot">The kit slot, from <see cref="KitBuilder"/>.</param>
    /// <returns>The sRGB hex, such as <c>#CDBF9B</c>.</returns>
    public static string HexOf(byte slot) => _tokens[slot].Hex;

    /// <summary>Finds the kit slot for a palette token name.</summary>
    /// <param name="token">The token name, such as <c>wall.wattle</c>.</param>
    /// <param name="slot">The kit slot, when the token is known.</param>
    /// <returns><see langword="true"/> when the token is in the table.</returns>
    public static bool TryGetSlot(string token, out byte slot)
    {
        for (int i = 0; i < _tokens.Length; i++)
        {
            if (string.Equals(_tokens[i].Name, token, StringComparison.Ordinal))
            {
                slot = (byte)i;
                return true;
            }
        }

        slot = 0;
        return false;
    }
}
