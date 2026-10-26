using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Evenmere.Core.Ids;
using Evenmere.Game.Ui.Common;

namespace Evenmere.Game.Tests;

/// <summary>
/// Coverage and budget guards for the ART-02 icon set: every Era I good, building and tier in the art bible's
/// coverage table has a 256 x 256 PNG master, every content item resolves to one, and every opaque pixel samples
/// to a <c>docs/art/palette.md</c> token. The checks parse PNG headers (and pixels) directly so they run without Godot.
/// </summary>
public sealed class IconLibraryTests
{
    // docs/art/README.md: every Era I good, building and tier, plus the build-menu categories from briefs/icons.md.
    private static readonly string[] _expectedGoods =
    [
        "wood", "timber", "fish", "wool", "homespun", "grain", "flour", "bread",
        "stone", "apples", "cider", "clay", "pottery",
    ];

    private static readonly string[] _expectedBuildings =
    [
        "woodcutters_hut", "sawpit", "fishing_hut", "sheep_farm", "weavers_cottage",
        "grain_farm", "windmill", "bakery", "quarry", "clay_pit", "potters_kiln",
        "orchard", "cider_press", "founders_hall", "settler_house", "dirt_road",
        "cobbled_road", "storehouse", "stone_storehouse", "market", "shrine",
        "scouts_lodge", "timber_bridge", "scribes_hall", "storytellers_circle",
        "festival_ground", "plaza", "standing_stones",
    ];

    private static readonly string[] _expectedTiers = ["settlers", "villagers"];

    private static readonly string[] _expectedUi =
    [
        "production", "infrastructure", "services", "knowledge", "culture",
        "residence", "roads", "monuments",
    ];

    private const int MasterEdge = 256;
    private const int ClearSpace = 16;
    private const long IconsBudgetBytes = 512L * 1024L;

    [Fact]
    public void ResPathFor_FollowsConvention()
    {
        IconLibrary.ResPathFor(IconKind.Good, "bread").ShouldBe("res://assets/icons/goods/bread.png");
        IconLibrary.ResPathFor(IconKind.Building, "bakery").ShouldBe("res://assets/icons/buildings/bakery.png");
        IconLibrary.ResPathFor(IconKind.Tier, "settlers").ShouldBe("res://assets/icons/tiers/settlers.png");
        IconLibrary.ResPathFor(IconKind.Ui, "production").ShouldBe("res://assets/icons/ui/production.png");
    }

    [Fact]
    public void FolderFor_MatchesAssetFolders()
    {
        IconLibrary.FolderFor(IconKind.Good).ShouldBe("goods");
        IconLibrary.FolderFor(IconKind.Building).ShouldBe("buildings");
        IconLibrary.FolderFor(IconKind.Tier).ShouldBe("tiers");
        IconLibrary.FolderFor(IconKind.Ui).ShouldBe("ui");
    }

    [Fact]
    public void Bible_EveryExpectedIconExists()
    {
        string root = IconsRoot();
        var missing = new List<string>();
        foreach ((IconKind kind, string id) in AllExpected())
        {
            if (!File.Exists(Path.Combine(root, IconLibrary.FolderFor(kind), $"{id}.png")))
            {
                missing.Add($"{IconLibrary.FolderFor(kind)}/{id}.png");
            }
        }

        missing.ShouldBeEmpty();
    }

    [Fact]
    public void Content_EveryItemHasAnIcon()
    {
        string root = IconsRoot();
        var missing = new List<string>();
        foreach (string name in TestContent.Db.Goods.Names)
        {
            if (!File.Exists(Path.Combine(root, "goods", $"{name}.png")))
            {
                missing.Add($"goods/{name}.png");
            }
        }

        foreach (string name in TestContent.Db.Buildings.Names)
        {
            if (!File.Exists(Path.Combine(root, "buildings", $"{name}.png")))
            {
                missing.Add($"buildings/{name}.png");
            }
        }

        foreach (TierId tier in TestContent.Db.TiersByOrder)
        {
            string name = TestContent.Db.NameOf(tier);
            if (!File.Exists(Path.Combine(root, "tiers", $"{name}.png")))
            {
                missing.Add($"tiers/{name}.png");
            }
        }

        missing.ShouldBeEmpty();
    }

    [Fact]
    public void Icons_Are256x256PngMasters()
    {
        string root = IconsRoot();
        var failures = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
        {
            (int width, int height) = ReadDimensions(file);
            if (width != MasterEdge || height != MasterEdge)
            {
                failures.Add($"{Path.GetRelativePath(root, file)}: {width}x{height}");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public void Icons_KeepClearSpace()
    {
        // Brief section 1: nothing inside 4 units (16 px) of the edge.
        string root = IconsRoot();
        var failures = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
        {
            if (HasOpaqueMarginPixels(file))
            {
                failures.Add(Path.GetRelativePath(root, file));
            }
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public void Icons_UseOnlyPaletteTokensAndInk()
    {
        // Brief section 2: every fill samples to a palette.md token. Ink is token ink.strong itself.
        HashSet<string> allowed = ParseBiblePalette();
        allowed.ShouldNotBeEmpty();
        string root = IconsRoot();
        var failures = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
        {
            foreach (string fill in OpaqueFills(file))
            {
                if (!allowed.Contains(fill))
                {
                    failures.Add($"{Path.GetRelativePath(root, file)}: {fill}");
                }
            }
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public void CompactChips_TooltipsNameTheItem()
    {
        // Compact goods, knowledge and culture chips show no name: the tooltip (and the focus popup)
        // must carry it, so hover and gamepad focus still identify the item.
        var strings = new UiStrings(TestLocales.Table());
        strings.Format("ui.hud.goods.tip", ("good", "Bread"), ("stock", "6"), ("trend", "steady"))
            .ShouldContain("Bread");
        strings.Get("ui.hud.knowledge.tip").ShouldContain("Knowledge");
        strings.Get("ui.hud.culture.tip").ShouldContain("Culture");
    }

    [Fact]
    public void Icons_FitBudget()
    {
        string root = IconsRoot();
        long total = Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
        total.ShouldBeLessThanOrEqualTo(IconsBudgetBytes);
    }

    private static IEnumerable<(IconKind Kind, string Id)> AllExpected()
    {
        foreach (string id in _expectedGoods)
        {
            yield return (IconKind.Good, id);
        }

        foreach (string id in _expectedBuildings)
        {
            yield return (IconKind.Building, id);
        }

        foreach (string id in _expectedTiers)
        {
            yield return (IconKind.Tier, id);
        }

        foreach (string id in _expectedUi)
        {
            yield return (IconKind.Ui, id);
        }
    }

    private static string IconsRoot() => Path.Combine(FindRepoRoot(), "game", "assets", "icons");

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Evenmere.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root (Evenmere.sln).");
    }

    private static HashSet<string> ParseBiblePalette()
    {
        // Bible rows look like: | `token.name` | `#RRGGBB` | ... — the token and hex are always the first cells.
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var pattern = new Regex(@"^\s*\|\s*`[^`]+`\s*\|\s*`(?<hex>#[0-9a-fA-F]{6})`", RegexOptions.Compiled);
        foreach (string line in File.ReadLines(Path.Combine(FindRepoRoot(), "docs", "art", "palette.md")))
        {
            Match match = pattern.Match(line);
            if (match.Success)
            {
                tokens.Add(match.Groups["hex"].Value.ToUpperInvariant());
            }
        }

        return tokens;
    }

    private static (int Width, int Height) ReadDimensions(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bytes.Length.ShouldBeGreaterThan(24, $"Truncated PNG: {path}");
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        bytes[..8].ShouldBe(signature, $"Not a PNG: {path}");
        Encoding.ASCII.GetString(bytes, 12, 4).ShouldBe("IHDR", $"Missing IHDR: {path}");
        int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return (width, height);
    }

    private static bool HasOpaqueMarginPixels(string path)
    {
        byte[] rgba = DecodeRgba(path, out int width, out int height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool margin = x < ClearSpace || y < ClearSpace
                    || x >= width - ClearSpace || y >= height - ClearSpace;
                if (margin && rgba[(y * width + x) * 4 + 3] > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static HashSet<string> OpaqueFills(string path)
    {
        byte[] rgba = DecodeRgba(path, out int width, out int height);
        var fills = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < width * height; i++)
        {
            if (rgba[i * 4 + 3] > 0)
            {
                fills.Add($"#{rgba[i * 4]:X2}{rgba[i * 4 + 1]:X2}{rgba[i * 4 + 2]:X2}");
            }
        }

        return fills;
    }

    // Minimal PNG decoder for the generator's output (8-bit RGBA, no interlace): walks the chunks, inflates
    // the IDAT stream and undoes the per-row filters. Enough to audit fills without a PNG library.
    private static byte[] DecodeRgba(string path, out int width, out int height)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int position = 8;
        int ihdrWidth = 0;
        int ihdrHeight = 0;
        int bitDepth = 0;
        int colourType = 0;
        var idat = new List<byte>();
        while (position + 8 <= bytes.Length)
        {
            int length = (bytes[position] << 24) | (bytes[position + 1] << 16)
                | (bytes[position + 2] << 8) | bytes[position + 3];
            string type = Encoding.ASCII.GetString(bytes, position + 4, 4);
            if (type == "IHDR")
            {
                ihdrWidth = (bytes[position + 8] << 24) | (bytes[position + 9] << 16)
                    | (bytes[position + 10] << 8) | bytes[position + 11];
                ihdrHeight = (bytes[position + 12] << 24) | (bytes[position + 13] << 16)
                    | (bytes[position + 14] << 8) | bytes[position + 15];
                bitDepth = bytes[position + 16];
                colourType = bytes[position + 17];
            }
            else if (type == "IDAT")
            {
                for (int i = 0; i < length; i++)
                {
                    idat.Add(bytes[position + 8 + i]);
                }
            }
            else if (type == "IEND")
            {
                break;
            }

            position += 12 + length;
        }

        if (bitDepth != 8 || colourType != 6)
        {
            throw new InvalidDataException($"{path}: expected 8-bit RGBA, found depth {bitDepth} type {colourType}.");
        }

        width = ihdrWidth;
        height = ihdrHeight;
        byte[] raw;
        using (var stream = new MemoryStream([.. idat], 2, idat.Count - 6))
        using (var inflate = new DeflateStream(stream, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            inflate.CopyTo(output);
            raw = output.ToArray();
        }

        const int Channels = 4;
        int stride = width * Channels;
        byte[] rgba = new byte[width * height * Channels];
        byte[] previous = new byte[stride];
        int offset = 0;
        for (int y = 0; y < height; y++)
        {
            int filter = raw[offset++];
            byte[] row = new byte[stride];
            Array.Copy(raw, offset, row, 0, stride);
            offset += stride;
            for (int i = 0; i < stride; i++)
            {
                int left = i >= Channels ? row[i - Channels] : 0;
                int up = previous[i];
                int upperLeft = i >= Channels ? previous[i - Channels] : 0;
                row[i] = (byte)(row[i] + filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upperLeft),
                    _ => throw new InvalidDataException($"{path}: unknown filter {filter}."),
                });
            }

            Array.Copy(row, 0, rgba, y * stride, stride);
            previous = row;
        }

        return rgba;
    }

    private static int Paeth(int left, int up, int upperLeft)
    {
        int estimate = left + up - upperLeft;
        int distanceLeft = Math.Abs(estimate - left);
        int distanceUp = Math.Abs(estimate - up);
        int distanceUpperLeft = Math.Abs(estimate - upperLeft);
        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpperLeft)
        {
            return left;
        }

        return distanceUp <= distanceUpperLeft ? up : upperLeft;
    }
}
