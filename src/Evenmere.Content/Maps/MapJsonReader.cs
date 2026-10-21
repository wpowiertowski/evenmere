using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Unicode;
using Evenmere.Content.Definitions;
using Evenmere.Content.Loading;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Content.Maps;

/// <summary>Reports problems at a place in <c>map.json</c>: the file, the JSON pointer and the line and column of the key or value.</summary>
/// <param name="File">The file relative to its content root.</param>
/// <param name="Positions">Where things are in the file.</param>
/// <param name="Report">The report.</param>
internal sealed record MapJsonSite(string File, JsonPositions Positions, MapReport Report)
{
    /// <summary>Reports an error at a pointer.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="pointer">The pointer of the property or value.</param>
    /// <param name="atKey">True to point at the property name, false to point at the value.</param>
    /// <param name="message">What is wrong.</param>
    public void Error(string code, string pointer, bool atKey, string message)
    {
        (int line, int column) = atKey ? Positions.KeyOf(pointer) : Positions.ValueOf(pointer);
        Report.Error(code, File, pointer, line, column, message);
    }

    /// <summary>Reports a warning at a pointer.</summary>
    /// <param name="pointer">The pointer of the property or value.</param>
    /// <param name="atKey">True to point at the property name, false to point at the value.</param>
    /// <param name="message">What is wrong.</param>
    public void Warning(string pointer, bool atKey, string message)
    {
        (int line, int column) = atKey ? Positions.KeyOf(pointer) : Positions.ValueOf(pointer);
        Report.Warning(DiagnosticCodes.Evl001, File, pointer, line, column, message);
    }
}

/// <summary>What <c>map.json</c> says, with every reference resolved against the content.</summary>
internal sealed class MapSettings
{
    private const int SymbolTableSize = 128;

    public MapSettings(MapJsonSite site)
    {
        Site = site;
    }

    public MapJsonSite Site { get; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>Gets the terrain for each legend character, indexed by character.</summary>
    public TerrainId?[] TerrainBySymbol { get; } = new TerrainId?[SymbolTableSize];

    /// <summary>Gets, for a default-legend character whose terrain the content does not define, that terrain's name.</summary>
    public string?[] MissingTerrainBySymbol { get; } = new string?[SymbolTableSize];

    /// <summary>Gets the characters the terrain layer may use, for messages.</summary>
    public string TerrainAlphabet { get; set; } = string.Empty;

    /// <summary>Gets the deposit for each deposit-legend character, indexed by character.</summary>
    public DepositKind?[] DepositBySymbol { get; } = new DepositKind?[SymbolTableSize];

    /// <summary>Gets the characters the deposit layer may use, for messages.</summary>
    public string DepositAlphabet { get; set; } = string.Empty;

    public MapStartSite StartSite { get; set; }

    public bool HasStartSite { get; set; }

    public ImmutableArray<GoodAmount> StartStock { get; set; } = [];

    public ImmutableArray<MapRegionName> RegionNames { get; set; } = [];
}

/// <summary>Reads and validates <c>map.json</c>.</summary>
internal static class MapJsonReader
{
    /// <summary>The largest width or height of an authored map, in tiles.</summary>
    public const int MaxSide = 1024;

    private static readonly JsonDocumentOptions _jsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16,
        AllowDuplicateProperties = false,
    };

    public static MapSettings? Read(string path, string file, ContentDb content, MapReport report)
    {
        byte[]? bytes = MapGridReader.ReadBytes(path, file, report);
        if (bytes is null)
        {
            return null;
        }

        if (bytes.AsSpan().StartsWith("﻿"u8))
        {
            report.Error(DiagnosticCodes.Evl001, file, string.Empty, 1, 1, "the file starts with a UTF-8 byte order mark; save it as UTF-8 without BOM");
            return null;
        }

        if (!Utf8.IsValid(bytes))
        {
            report.Error(DiagnosticCodes.Evl001, file, string.Empty, 0, 0, "the file is not valid UTF-8");
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, _jsonOptions);
        }
        catch (JsonException ex)
        {
            string message = ex.Message;
            int detail = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
            report.Error(
                DiagnosticCodes.Evl001, file, string.Empty, (int)(ex.LineNumber ?? -1) + 1, (int)(ex.BytePositionInLine ?? -1) + 1,
                "invalid JSON: " + (detail >= 0 ? message[..detail] : message));
            return null;
        }

        using (document)
        {
            var site = new MapJsonSite(file, JsonPositions.Build(bytes), report);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                site.Error(DiagnosticCodes.Evl001, string.Empty, false, $"the file must contain a JSON object, not {Describe(root)}");
                return null;
            }

            return ReadRoot(root, site, content);
        }
    }

    private static MapSettings ReadRoot(JsonElement root, MapJsonSite site, ContentDb content)
    {
        var settings = new MapSettings(site);
        bool sawWidth = false;
        bool sawHeight = false;
        JsonElement? legend = null;
        JsonElement? depositLegend = null;

        foreach (JsonProperty property in root.EnumerateObject())
        {
            string pointer = JsonPointer.Append(string.Empty, property.Name);
            switch (property.Name)
            {
                case "width":
                    sawWidth = TryReadSide(property.Value, site, pointer, out int width);
                    settings.Width = width;
                    break;
                case "height":
                    sawHeight = TryReadSide(property.Value, site, pointer, out int height);
                    settings.Height = height;
                    break;
                case "legend":
                    legend = property.Value;
                    break;
                case "depositLegend":
                    depositLegend = property.Value;
                    break;
                case "startSite":
                case "startStock":
                case "regions":
                    break;
                default:
                    site.Error(DiagnosticCodes.Evl001, pointer, true, $"unknown property '{property.Name}'; the map file knows width, height, legend, depositLegend, startSite, startStock and regions");
                    break;
            }
        }

        if (!root.TryGetProperty("width", out _))
        {
            site.Error(DiagnosticCodes.Evl001, string.Empty, false, "missing required property 'width'");
        }

        if (!root.TryGetProperty("height", out _))
        {
            site.Error(DiagnosticCodes.Evl001, string.Empty, false, "missing required property 'height'");
        }

        ReadTerrainLegend(legend, site, content, settings);
        ReadDepositLegend(depositLegend, site, settings);

        if (root.TryGetProperty("regions", out JsonElement regions))
        {
            settings.RegionNames = ReadRegions(regions, site);
        }

        if (root.TryGetProperty("startStock", out JsonElement stock))
        {
            settings.StartStock = ReadStartStock(stock, site, content);
        }

        if (root.TryGetProperty("startSite", out JsonElement startSite))
        {
            if (sawWidth && sawHeight)
            {
                settings.HasStartSite = TryReadStartSite(startSite, site, settings.Width, settings.Height, out MapStartSite start);
                settings.StartSite = start;
            }
        }
        else
        {
            site.Error(DiagnosticCodes.Evl001, string.Empty, false, "missing required property 'startSite'");
        }

        return settings;
    }

    private static bool TryReadSide(JsonElement value, MapJsonSite site, string pointer, out int side)
    {
        if (!TryReadInt(value, site, pointer, 1, MaxSide, out side))
        {
            side = 0;
            return false;
        }

        return true;
    }

    private static bool TryReadInt(JsonElement value, MapJsonSite site, string pointer, int min, int max, out int number)
    {
        number = 0;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out number))
        {
            site.Error(DiagnosticCodes.Evl001, pointer, false, $"expected a whole number from {min} to {max} but found {Describe(value)}");
            return false;
        }

        if (number < min || number > max)
        {
            site.Error(DiagnosticCodes.Evl001, pointer, false, $"{number} is out of range; expected a whole number from {min} to {max}");
            return false;
        }

        return true;
    }

    private static void ReadTerrainLegend(JsonElement? legend, MapJsonSite site, ContentDb content, MapSettings settings)
    {
        var symbols = new List<char>();
        if (legend is null)
        {
            // The default legend applies; an entry whose terrain the content lacks only matters if the map uses it.
            foreach ((char symbol, string name) in MapTerrainRules.DefaultLegend)
            {
                symbols.Add(symbol);
                if (content.Terrains.TryGet(name, out TerrainId id))
                {
                    settings.TerrainBySymbol[symbol] = id;
                }
                else
                {
                    settings.MissingTerrainBySymbol[symbol] = name;
                }
            }
        }
        else if (legend.Value.ValueKind != JsonValueKind.Object)
        {
            site.Error(DiagnosticCodes.Evl001, "/legend", false, $"expected an object of character to terrain ID but found {Describe(legend.Value)}");
        }
        else
        {
            foreach (JsonProperty entry in legend.Value.EnumerateObject())
            {
                string pointer = JsonPointer.Append("/legend", entry.Name);
                if (!IsSymbol(entry.Name))
                {
                    site.Error(DiagnosticCodes.Evl001, pointer, true, $"the legend key '{entry.Name}' must be one printable ASCII character other than a space");
                    continue;
                }

                if (entry.Value.ValueKind != JsonValueKind.String)
                {
                    site.Error(DiagnosticCodes.Evl001, pointer, false, $"expected a terrain ID but found {Describe(entry.Value)}");
                    continue;
                }

                string name = entry.Value.GetString() ?? string.Empty;
                if (content.Terrains.TryGet(name, out TerrainId id))
                {
                    settings.TerrainBySymbol[entry.Name[0]] = id;
                    symbols.Add(entry.Name[0]);
                }
                else
                {
                    site.Error(DiagnosticCodes.Evl002, pointer, false, $"'{name}' is not a defined terrain");
                }
            }

            if (symbols.Count == 0 && !legend.Value.EnumerateObject().Any())
            {
                site.Error(DiagnosticCodes.Evl001, "/legend", false, "the legend is empty; omit it to use the default legend");
            }
        }

        symbols.Sort();
        settings.TerrainAlphabet = "one of the legend characters (" + string.Join(' ', symbols) + ")";
    }

    private static void ReadDepositLegend(JsonElement? legend, MapJsonSite site, MapSettings settings)
    {
        var symbols = new List<char> { '.' };
        if (legend is null)
        {
            foreach ((char symbol, string name) in MapTerrainRules.DefaultDepositLegend)
            {
                // The names of the default legend are DepositKind members, so TryParse always succeeds.
                if (ContentNames.TryParse(name, out DepositKind kind))
                {
                    settings.DepositBySymbol[symbol] = kind;
                    symbols.Add(symbol);
                }
            }
        }
        else if (legend.Value.ValueKind != JsonValueKind.Object)
        {
            site.Error(DiagnosticCodes.Evl001, "/depositLegend", false, $"expected an object of character to deposit name but found {Describe(legend.Value)}");
        }
        else
        {
            foreach (JsonProperty entry in legend.Value.EnumerateObject())
            {
                string pointer = JsonPointer.Append("/depositLegend", entry.Name);
                if (!IsSymbol(entry.Name) || entry.Name == ".")
                {
                    site.Error(
                        DiagnosticCodes.Evl001, pointer, true,
                        $"the deposit legend key '{entry.Name}' must be one printable ASCII character other than a space and '.', which means no deposit");
                    continue;
                }

                if (entry.Value.ValueKind != JsonValueKind.String)
                {
                    site.Error(DiagnosticCodes.Evl001, pointer, false, $"expected a deposit name but found {Describe(entry.Value)}");
                    continue;
                }

                string name = entry.Value.GetString() ?? string.Empty;
                if (ContentNames.TryParse(name, out DepositKind kind))
                {
                    settings.DepositBySymbol[entry.Name[0]] = kind;
                    symbols.Add(entry.Name[0]);
                }
                else
                {
                    site.Error(DiagnosticCodes.Evl002, pointer, false, $"'{name}' is not a deposit; expected one of {string.Join(", ", ContentNames.NamesOf<DepositKind>())}");
                }
            }
        }

        symbols.Sort();
        settings.DepositAlphabet = "one of the deposit legend characters (" + string.Join(' ', symbols) + ")";
    }

    private static ImmutableArray<MapRegionName> ReadRegions(JsonElement regions, MapJsonSite site)
    {
        if (regions.ValueKind != JsonValueKind.Object)
        {
            site.Error(DiagnosticCodes.Evl001, "/regions", false, $"expected an object of region character to locale key but found {Describe(regions)}");
            return [];
        }

        var names = new List<MapRegionName>();
        foreach (JsonProperty entry in regions.EnumerateObject())
        {
            string pointer = JsonPointer.Append("/regions", entry.Name);
            if (entry.Name.Length != 1 || !TryRegionOf(entry.Name[0], out ushort region))
            {
                site.Error(DiagnosticCodes.Evl001, pointer, true, $"the region key '{entry.Name}' must be one base-36 character, 0-9 or A-Z");
                continue;
            }

            if (entry.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.Value.GetString()))
            {
                site.Error(DiagnosticCodes.Evl001, pointer, false, $"expected a locale key but found {Describe(entry.Value)}");
                continue;
            }

            names.Add(new MapRegionName(region, entry.Value.GetString()!));
        }

        names.Sort(static (a, b) => a.Region.CompareTo(b.Region));
        return [.. names];
    }

    private static ImmutableArray<GoodAmount> ReadStartStock(JsonElement stock, MapJsonSite site, ContentDb content)
    {
        if (stock.ValueKind != JsonValueKind.Object)
        {
            site.Error(DiagnosticCodes.Evl001, "/startStock", false, $"expected an object of good ID to quantity but found {Describe(stock)}");
            return [];
        }

        var goods = new List<GoodAmount>();
        foreach (JsonProperty entry in stock.EnumerateObject())
        {
            string pointer = JsonPointer.Append("/startStock", entry.Name);
            if (!content.Goods.TryGet(entry.Name, out GoodId good))
            {
                site.Error(DiagnosticCodes.Evl002, pointer, true, $"'{entry.Name}' is not a defined good");
                continue;
            }

            if (TryReadQuantity(entry.Value, site, pointer, out Amount amount))
            {
                goods.Add(new GoodAmount(good, amount));
            }
        }

        goods.Sort(static (a, b) => a.Good.Value.CompareTo(b.Good.Value));
        return [.. goods];
    }

    private static bool TryReadQuantity(JsonElement value, MapJsonSite site, string pointer, out Amount amount)
    {
        amount = Amount.Zero;
        if (value.ValueKind != JsonValueKind.Number)
        {
            site.Error(DiagnosticCodes.Evl001, pointer, false, $"expected a quantity (a JSON number with at most 3 decimals) but found {Describe(value)}");
            return false;
        }

        string text = value.GetRawText();
        try
        {
            amount = Amount.Parse(text);
        }
        catch (FormatException)
        {
            site.Error(
                DiagnosticCodes.Evl001, pointer, false,
                text.Contains('e') || text.Contains('E')
                    ? $"'{text}' uses exponent notation; write quantities as plain decimals with at most 3 decimal places"
                    : $"'{text}' has more than 3 decimal places; quantities hold at most 3 decimals");
            return false;
        }
        catch (OverflowException)
        {
            site.Error(DiagnosticCodes.Evl001, pointer, false, $"'{text}' is too large for a quantity");
            return false;
        }

        if (amount <= Amount.Zero)
        {
            site.Error(DiagnosticCodes.Evl001, pointer, false, $"{text} must be greater than zero");
            return false;
        }

        return true;
    }

    private static bool TryReadStartSite(JsonElement value, MapJsonSite site, int width, int height, out MapStartSite start)
    {
        start = default;
        if (value.ValueKind != JsonValueKind.Object)
        {
            site.Error(DiagnosticCodes.Evl001, "/startSite", false, $"expected an object with x, y and rotation but found {Describe(value)}");
            return false;
        }

        bool ok = true;
        int x = 0;
        int y = 0;
        int rotation = 0;
        bool sawX = false;
        bool sawY = false;
        foreach (JsonProperty entry in value.EnumerateObject())
        {
            string pointer = JsonPointer.Append("/startSite", entry.Name);
            switch (entry.Name)
            {
                case "x":
                    sawX = true;
                    ok &= TryReadInt(entry.Value, site, pointer, 0, width - 1, out x);
                    break;
                case "y":
                    sawY = true;
                    ok &= TryReadInt(entry.Value, site, pointer, 0, height - 1, out y);
                    break;
                case "rotation":
                    ok &= TryReadInt(entry.Value, site, pointer, 0, 3, out rotation);
                    break;
                default:
                    site.Error(DiagnosticCodes.Evl001, pointer, true, $"unknown property '{entry.Name}'; startSite knows x, y and rotation");
                    ok = false;
                    break;
            }
        }

        if (!sawX)
        {
            site.Error(DiagnosticCodes.Evl001, "/startSite", false, "missing required property 'x'");
            ok = false;
        }

        if (!sawY)
        {
            site.Error(DiagnosticCodes.Evl001, "/startSite", false, "missing required property 'y'");
            ok = false;
        }

        start = new MapStartSite(x, y, rotation);
        return ok;
    }

    /// <summary>Tests whether a legend key is one printable ASCII character other than a space.</summary>
    private static bool IsSymbol(string key) => key.Length == 1 && key[0] is >= '!' and <= '~';

    /// <summary>Converts a base-36 character (0-9, then A-Z) to its value.</summary>
    /// <param name="c">The character.</param>
    /// <param name="region">The value, 0 to 35.</param>
    /// <returns><see langword="true"/> if the character is a base-36 digit.</returns>
    public static bool TryRegionOf(char c, out ushort region)
    {
        region = 0;
        if (c is >= '0' and <= '9')
        {
            region = (ushort)(c - '0');
            return true;
        }

        if (c is >= 'A' and <= 'Z')
        {
            region = (ushort)(c - 'A' + 10);
            return true;
        }

        return false;
    }

    private static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "the number " + element.GetRawText(),
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Null => "null",
        _ => "an unexpected value",
    };
}
