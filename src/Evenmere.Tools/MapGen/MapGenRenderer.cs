using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;

namespace Evenmere.Tools.MapGen;

/// <summary>
/// Renders a generated island (<see cref="MapDef"/>) to flat RGB images for the <c>mapgen</c> command: one layer at a
/// time, plus a labelled contact-sheet gallery. Terrain fills come from the art palette tokens
/// (<c>docs/art/palette.md</c> §4) so the previews match the game. Integer maths throughout; the output is a pure
/// function of the map, the content and the layer.
/// </summary>
internal static class MapGenRenderer
{
    /// <summary>The height of the seed label strip under each gallery cell, in pixels.</summary>
    internal const int LabelHeight = 14;

    /// <summary>The gallery columns; 20 seeds land on a 5 × 4 sheet.</summary>
    internal const int GalleryColumns = 5;

    /// <summary>The scale of the bitmap seed digits: each font pixel becomes this many image pixels.</summary>
    internal const int FontScale = 2;

    private static readonly Dictionary<string, (byte R, byte G, byte B)> _terrainColors = new(StringComparer.Ordinal)
    {
        ["plains"] = (0x73, 0xA1, 0x54),
        ["forest"] = (0x3D, 0x73, 0x42),
        ["beach"] = (0xD9, 0xC7, 0x93),
        ["hills"] = (0x8C, 0x85, 0x59),
        ["highlands"] = (0x80, 0x77, 0x66),
        ["peaks"] = (0xBF, 0xBD, 0xB8),
        ["fen"] = (0x8C, 0x9E, 0x66),
        ["marsh"] = (0x59, 0x6B, 0x4D),
        ["reef"] = (0x40, 0x99, 0x99),
        ["sea"] = (0x33, 0x6B, 0x80),
        ["deep_water"] = (0x1A, 0x3B, 0x61),
        ["river"] = (0x5E, 0x93, 0xA3),
    };

    private static readonly (byte R, byte G, byte B) _unknownTerrain = (0xFF, 0x00, 0xFF);

    private static readonly (byte R, byte G, byte B) _fertileFill = (0x73, 0xA1, 0x54);

    private static readonly (byte R, byte G, byte B) _barrenFill = (0x2E, 0x26, 0x20);

    private static readonly Dictionary<DepositKind, (byte R, byte G, byte B)> _depositColors = new()
    {
        [DepositKind.Stone] = (0x9B, 0x9B, 0x95),
        [DepositKind.Clay] = (0x8F, 0x56, 0x36),
        [DepositKind.Iron] = (0x6B, 0x7B, 0x8C),
        [DepositKind.Coal] = (0x23, 0x20, 0x1C),
        [DepositKind.Copper] = (0xB8, 0x73, 0x33),
    };

    private static readonly (byte R, byte G, byte B) _wonderMark = (0xFF, 0xF8, 0xE8);

    private static readonly (byte R, byte G, byte B) _ruinMark = (0xB2, 0x5A, 0x38);

    private static readonly (byte R, byte G, byte B) _wreckMark = (0xF2, 0xEF, 0xE2);

    private static readonly (byte R, byte G, byte B) _depositMark = (0x9B, 0x9B, 0x95);

    private static readonly (byte R, byte G, byte B) _labelBack = (0x2E, 0x26, 0x20);

    private static readonly (byte R, byte G, byte B) _labelInk = (0xF4, 0xEA, 0xD2);

    // A 3x5 bitmap font for the gallery seed labels ("seed N"): rows of '#' and '.', top row first.
    private static readonly Dictionary<char, string[]> _font = new()
    {
        [' '] = ["...", "...", "...", "...", "..."],
        ['0'] = ["###", "#.#", "#.#", "#.#", "###"],
        ['1'] = [".#.", "##.", ".#.", ".#.", "###"],
        ['2'] = ["###", "..#", "###", "#..", "###"],
        ['3'] = ["###", "..#", ".##", "..#", "###"],
        ['4'] = ["#.#", "#.#", "###", "..#", "..#"],
        ['5'] = ["###", "#..", "###", "..#", "###"],
        ['6'] = ["###", "#..", "###", "#.#", "###"],
        ['7'] = ["###", "..#", "..#", ".#.", ".#."],
        ['8'] = ["###", "#.#", "###", "#.#", "###"],
        ['9'] = ["###", "#.#", "###", "..#", "###"],
        ['s'] = [".##", "#..", ".#.", "..#", "##."],
        ['e'] = ["###", "#..", "##.", "#..", "###"],
        ['d'] = ["##.", "#.#", "#.#", "#.#", "##."],
        ['-'] = ["...", "...", "###", "...", "..."],
    };

    /// <summary>Renders one layer of an island to RGB pixels, top row first.</summary>
    /// <param name="map">The island to render.</param>
    /// <param name="content">The loaded content, for terrain names.</param>
    /// <param name="layer">The layer: <c>terrain</c>, <c>elevation</c>, <c>regions</c>, <c>deposits</c> or <c>fertility:&lt;crop&gt;</c>.</param>
    /// <returns>The image bytes: <c>Width × Height × 3</c> in R, G, B order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The layer is unknown, or the fertility crop does not name a <c>CropKind</c>.</exception>
    public static RenderedImage Render(MapDef map, ContentDb content, string layer)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(layer);

        int width = map.Width;
        int height = map.Height;
        byte[] rgb = new byte[width * height * 3];

        if (layer.Equals("terrain", StringComparison.Ordinal))
        {
            PaintTerrain(map, content, rgb);
            PaintDiscoveries(map, rgb);
        }
        else if (layer.Equals("elevation", StringComparison.Ordinal))
        {
            PaintElevation(map, rgb);
        }
        else if (layer.Equals("regions", StringComparison.Ordinal))
        {
            PaintRegions(map, rgb);
        }
        else if (layer.Equals("deposits", StringComparison.Ordinal))
        {
            PaintDeposits(map, content, rgb);
        }
        else if (layer.StartsWith("fertility:", StringComparison.Ordinal))
        {
            PaintFertility(map, layer.Substring("fertility:".Length), rgb);
        }
        else
        {
            throw new ArgumentException($"Unknown layer \"{layer}\": terrain, elevation, regions, deposits or fertility:<crop>.", nameof(layer));
        }

        return new RenderedImage(width, height, rgb);
    }

    /// <summary>Renders the terrain-grid ASCII of an island with the map-format legend characters, for agents.</summary>
    /// <param name="map">The island to describe.</param>
    /// <param name="content">The loaded content, for terrain names.</param>
    /// <returns>One line per row, top row first, with a trailing newline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    public static string ToAscii(MapDef map, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(content);

        var symbols = new Dictionary<string, char>(StringComparer.Ordinal);
        foreach ((char symbol, string terrain) in MapTerrainRules.DefaultLegend)
        {
            symbols.TryAdd(terrain, symbol);
        }

        using var text = new StringWriter();
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                string name = content.Terrains.NameOf(map.Terrain[y * map.Width + x]);
                text.Write(symbols.TryGetValue(name, out char symbol) ? symbol : '?');
            }

            text.Write('\n');
        }

        return text.ToString();
    }

    /// <summary>Renders a labelled contact sheet of islands, five columns, every cell at one pixel per tile.</summary>
    /// <param name="maps">The islands in seed order; every island must share one size.</param>
    /// <param name="content">The loaded content, for terrain names.</param>
    /// <param name="seeds">The seed of each island, for the labels.</param>
    /// <returns>The sheet image.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="maps"/> or <paramref name="seeds"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">There are no islands, the counts differ, or the sizes differ.</exception>
    public static RenderedImage RenderGallery(IReadOnlyList<MapDef> maps, ContentDb content, IReadOnlyList<int> seeds)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(seeds);
        if (maps.Count == 0)
        {
            throw new ArgumentException("The gallery needs at least one island.", nameof(maps));
        }

        if (maps.Count != seeds.Count)
        {
            throw new ArgumentException($"The gallery needs one seed per island ({maps.Count} islands, {seeds.Count} seeds).", nameof(seeds));
        }

        int cell = maps[0].Width;
        for (int i = 1; i < maps.Count; i++)
        {
            if (maps[i].Width != cell || maps[i].Height != cell)
            {
                throw new ArgumentException("Every gallery island must share one square size.", nameof(maps));
            }
        }

        int columns = Math.Min(GalleryColumns, maps.Count);
        int rows = (maps.Count + columns - 1) / columns;
        int sheetWidth = columns * cell;
        int sheetHeight = (rows * cell) + (rows * LabelHeight);
        byte[] rgb = new byte[sheetWidth * sheetHeight * 3];

        for (int i = 0; i < maps.Count; i++)
        {
            RenderedImage tile = Render(maps[i], content, "terrain");
            int column = i % columns;
            int row = i / columns;
            int originX = column * cell;
            int originY = (row * (cell + LabelHeight)) + LabelHeight;
            for (int y = 0; y < cell; y++)
            {
                Buffer.BlockCopy(tile.Rgb, y * cell * 3, rgb, ((originY + y) * sheetWidth * 3) + (originX * 3), cell * 3);
            }

            PaintLabel(rgb, sheetWidth, sheetHeight, originX, row * (cell + LabelHeight), cell, $"seed {seeds[i]}");
        }

        return new RenderedImage(sheetWidth, sheetHeight, rgb);
    }

    private static void PaintTerrain(MapDef map, ContentDb content, byte[] rgb)
    {
        for (int i = 0; i < map.TileCount; i++)
        {
            string name = content.Terrains.NameOf(map.Terrain[i]);
            (byte r, byte g, byte b) = _terrainColors.TryGetValue(name, out (byte R, byte G, byte B) fill) ? fill : _unknownTerrain;
            rgb[i * 3] = r;
            rgb[(i * 3) + 1] = g;
            rgb[(i * 3) + 2] = b;
        }
    }

    // Discovery markers sit on the terrain layer so the gallery shows what the fog hides: wonders are paper-white
    // diamonds, ruins are brick squares and shipwrecks are foam-white crosses; deposit-kind finds are grey dots.
    private static void PaintDiscoveries(MapDef map, byte[] rgb)
    {
        foreach (MapDiscovery discovery in map.Discoveries)
        {
            switch (discovery.Kind)
            {
                case DiscoveryKind.NaturalWonder:
                    PaintDiamond(map, rgb, discovery.X, discovery.Y, _wonderMark);
                    break;
                case DiscoveryKind.Ruin:
                    PaintSquare(map, rgb, discovery.X, discovery.Y, _ruinMark);
                    break;
                case DiscoveryKind.Shipwreck:
                    PaintCross(map, rgb, discovery.X, discovery.Y, _wreckMark);
                    break;
                default:
                    PaintDot(map, rgb, discovery.X, discovery.Y, _depositMark);
                    break;
            }
        }
    }

    private static void PaintDiamond(MapDef map, byte[] rgb, int cx, int cy, (byte R, byte G, byte B) ink)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) <= 2)
                {
                    Plot(map, rgb, cx + dx, cy + dy, ink);
                }
            }
        }
    }

    private static void PaintSquare(MapDef map, byte[] rgb, int cx, int cy, (byte R, byte G, byte B) ink)
    {
        for (int d = -2; d <= 2; d++)
        {
            Plot(map, rgb, cx + d, cy - 2, ink);
            Plot(map, rgb, cx + d, cy + 2, ink);
            Plot(map, rgb, cx - 2, cy + d, ink);
            Plot(map, rgb, cx + 2, cy + d, ink);
        }
    }

    private static void PaintCross(MapDef map, byte[] rgb, int cx, int cy, (byte R, byte G, byte B) ink)
    {
        for (int d = -2; d <= 2; d++)
        {
            Plot(map, rgb, cx + d, cy + d, ink);
            Plot(map, rgb, cx + d, cy - d, ink);
        }

        Plot(map, rgb, cx, cy, ink);
    }

    private static void PaintDot(MapDef map, byte[] rgb, int cx, int cy, (byte R, byte G, byte B) ink)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Plot(map, rgb, cx + dx, cy + dy, ink);
            }
        }
    }

    private static void Plot(MapDef map, byte[] rgb, int x, int y, (byte R, byte G, byte B) ink)
    {
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
        {
            return;
        }

        int i = (y * map.Width) + x;
        rgb[i * 3] = ink.R;
        rgb[(i * 3) + 1] = ink.G;
        rgb[(i * 3) + 2] = ink.B;
    }

    private static void PaintElevation(MapDef map, byte[] rgb)
    {
        for (int i = 0; i < map.TileCount; i++)
        {
            byte v = (byte)(map.Elevation[i] * 17);
            rgb[i * 3] = v;
            rgb[(i * 3) + 1] = v;
            rgb[(i * 3) + 2] = v;
        }
    }

    private static void PaintRegions(MapDef map, byte[] rgb)
    {
        for (int i = 0; i < map.TileCount; i++)
        {
            uint hash = unchecked((uint)map.Region[i] * 2654435761u) ^ 0x9E3779B9u;
            byte r = (byte)((hash >> 16) & 0xFF);
            byte g = (byte)((hash >> 8) & 0xFF);
            byte b = (byte)(hash & 0xFF);
            if (r < 64 && g < 64 && b < 64)
            {
                r = (byte)(r + 64);
                g = (byte)(g + 64);
                b = (byte)(b + 64);
            }

            rgb[i * 3] = r;
            rgb[(i * 3) + 1] = g;
            rgb[(i * 3) + 2] = b;
        }
    }

    private static void PaintDeposits(MapDef map, ContentDb content, byte[] rgb)
    {
        PaintTerrain(map, content, rgb);
        for (int i = 0; i < map.TileCount; i++)
        {
            if (map.Deposit[i] == 0)
            {
                rgb[i * 3] = (byte)(rgb[i * 3] / 2);
                rgb[(i * 3) + 1] = (byte)(rgb[(i * 3) + 1] / 2);
                rgb[(i * 3) + 2] = (byte)(rgb[(i * 3) + 2] / 2);
                continue;
            }

            var kind = (DepositKind)(map.Deposit[i] - 1);
            (byte r, byte g, byte b) = _depositColors.TryGetValue(kind, out (byte R, byte G, byte B) fill)
                ? fill
                : _unknownTerrain;
            rgb[i * 3] = r;
            rgb[(i * 3) + 1] = g;
            rgb[(i * 3) + 2] = b;
        }
    }

    private static void PaintFertility(MapDef map, string crop, byte[] rgb)
    {
        CropKind kind = ParseCrop(crop);
        uint bit = 1u << (int)kind;
        for (int i = 0; i < map.TileCount; i++)
        {
            (byte r, byte g, byte b) = (map.Fertility[i] & bit) != 0 ? _fertileFill : _barrenFill;
            rgb[i * 3] = r;
            rgb[(i * 3) + 1] = g;
            rgb[(i * 3) + 2] = b;
        }
    }

    private static CropKind ParseCrop(string crop)
    {
        foreach (CropKind kind in Enum.GetValues<CropKind>())
        {
            if (ToSnakeCase(kind.ToString()).Equals(crop.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        throw new ArgumentException($"Unknown crop \"{crop}\": grain, flax, hops, apples, grapes, tea, madder or sugar_beet.", nameof(crop));
    }

    private static string ToSnakeCase(string name)
    {
        using var text = new StringWriter();
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                text.Write('_');
            }

            text.Write(char.ToLowerInvariant(name[i]));
        }

        return text.ToString();
    }

    private static void PaintLabel(byte[] rgb, int sheetWidth, int sheetHeight, int x, int y, int cellWidth, string label)
    {
        for (int row = 0; row < LabelHeight; row++)
        {
            for (int column = 0; column < cellWidth; column++)
            {
                int i = ((y + row) * sheetWidth) + x + column;
                rgb[i * 3] = _labelBack.R;
                rgb[(i * 3) + 1] = _labelBack.G;
                rgb[(i * 3) + 2] = _labelBack.B;
            }
        }

        int penX = x + 4;
        int penY = y + ((LabelHeight - (5 * FontScale)) / 2);
        foreach (char c in label)
        {
            if (_font.TryGetValue(c, out string[]? glyph))
            {
                for (int gy = 0; gy < 5; gy++)
                {
                    for (int gx = 0; gx < 3; gx++)
                    {
                        if (glyph[gy][gx] != '#')
                        {
                            continue;
                        }

                        for (int sy = 0; sy < FontScale; sy++)
                        {
                            for (int sx = 0; sx < FontScale; sx++)
                            {
                                int px = penX + (gx * FontScale) + sx;
                                int py = penY + (gy * FontScale) + sy;
                                if (px < x || py < y || px >= x + cellWidth || py >= y + LabelHeight || py >= sheetHeight)
                                {
                                    continue;
                                }

                                int i = (py * sheetWidth) + px;
                                rgb[i * 3] = _labelInk.R;
                                rgb[(i * 3) + 1] = _labelInk.G;
                                rgb[(i * 3) + 2] = _labelInk.B;
                            }
                        }
                    }
                }
            }

            penX += (3 * FontScale) + FontScale;
        }
    }
}
