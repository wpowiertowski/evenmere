using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Evenmere.Content.Definitions;
using Evenmere.Content.Loading;
using Evenmere.Core.Ids;

namespace Evenmere.Content.Maps;

/// <summary>
/// Loads an authored map (docs/design/map-format.md) from <c>maps/&lt;id&gt;/</c> in a content root into an immutable
/// <see cref="MapDef"/>. Problems become <see cref="ContentDiagnostic"/>s with the file, line and column (codes
/// <c>EVL001</c> for shape and <c>EVL002</c> for references); nothing here throws for a bad map. Maps are loaded on demand by
/// ID and are not part of <see cref="ContentLoader"/>'s database, although their files count toward the content hash.
/// </summary>
public static class MapLoader
{
    private const string MapFileName = "map.json";
    private const string TerrainFileName = "terrain.txt";
    private const string RiversFileName = "rivers.txt";
    private const string ElevationFileName = "elevation.txt";
    private const string RegionsFileName = "regions.txt";
    private const string DepositsFileName = "deposits.txt";
    private const string FertilityPrefix = "fertility-";
    private const int MaxTerrains = 256;

    /// <summary>Loads one map.</summary>
    /// <param name="roots">The content roots in load order; the last root that has the map's folder provides the whole map.</param>
    /// <param name="mapId">The map's ID, which is its folder name under <c>maps/</c>.</param>
    /// <param name="content">The loaded content that terrain, good and crop references resolve against.</param>
    /// <returns>The result: the map when there is no error, and always the diagnostics.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static MapLoadResult Load(IReadOnlyList<string> roots, string mapId, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(mapId);
        ArgumentNullException.ThrowIfNull(content);

        var report = new MapReport();
        MapDef? map = LoadCore(roots, mapId, content, report);
        ImmutableArray<ContentDiagnostic> diagnostics = report.Finish();
        return new MapLoadResult(report.HasErrors ? null : map, diagnostics);
    }

    private static MapDef? LoadCore(IReadOnlyList<string> roots, string mapId, ContentDb content, MapReport report)
    {
        if (!Ids.IsValid(mapId))
        {
            report.Error(DiagnosticCodes.Evl001, string.Empty, string.Empty, 0, 0, $"'{mapId}' is not a valid map ID; use snake_case (lowercase letters, digits and single underscores, starting with a letter)");
            return null;
        }

        string? folder = FindFolder(roots, mapId);
        if (folder is null)
        {
            report.Error(DiagnosticCodes.Evl002, string.Empty, string.Empty, 0, 0, $"map '{mapId}' was not found: no content root has a maps/{mapId} folder");
            return null;
        }

        if (content.Terrains.Count > MaxTerrains)
        {
            report.Error(DiagnosticCodes.Evl001, string.Empty, string.Empty, 0, 0, $"the content defines {content.Terrains.Count} terrains but map terrain is stored in a byte (at most {MaxTerrains})");
            return null;
        }

        string prefix = $"maps/{mapId}/";
        WarnAboutUnknownFiles(folder, prefix, report);

        string mapFile = prefix + MapFileName;
        string mapPath = Path.Combine(folder, MapFileName);
        if (!File.Exists(mapPath))
        {
            report.Error(DiagnosticCodes.Evl001, mapFile, string.Empty, 0, 0, "the required file is missing");
            return null;
        }

        MapSettings? settings = MapJsonReader.Read(mapPath, mapFile, content, report);
        if (settings is null || settings.Width < 1 || settings.Height < 1)
        {
            // Without a size the layers cannot be checked; the errors above say why.
            return null;
        }

        int width = settings.Width;
        int height = settings.Height;
        int tiles = width * height;

        // The features of every terrain, indexed by TerrainId.Value.
        var features = new MapTerrainFeatures[content.Terrains.Count];
        for (int i = 0; i < features.Length; i++)
        {
            features[i] = MapTerrainRules.FeaturesOf(content.Terrains.Names[i]);
        }

        TerrainId[]? terrain = ReadTerrain(folder, prefix, settings, tiles, report);
        byte[]? riverWidth = ReadRivers(folder, prefix, settings, terrain, features, report);
        byte[] elevation = ReadByteLayer(folder, prefix, ElevationFileName, settings, ElevationOf, "0-9 or A-F", report);
        ushort[] region = ReadRegions(folder, prefix, settings, report);
        byte[] deposit = ReadDeposits(folder, prefix, settings, report);
        uint[] fertility = ReadFertility(folder, prefix, settings, report);

        if (terrain is not null)
        {
            CheckStartSite(settings, terrain, content, report);
        }

        if (report.HasErrors || terrain is null || riverWidth is null)
        {
            return null;
        }

        return new MapDef(
            mapId,
            width,
            height,
            ImmutableCollectionsMarshal.AsImmutableArray(terrain),
            ImmutableCollectionsMarshal.AsImmutableArray(elevation),
            ImmutableCollectionsMarshal.AsImmutableArray(riverWidth),
            ImmutableCollectionsMarshal.AsImmutableArray(region),
            ImmutableCollectionsMarshal.AsImmutableArray(deposit),
            ImmutableCollectionsMarshal.AsImmutableArray(fertility),
            ImmutableCollectionsMarshal.AsImmutableArray(features),
            settings.StartSite,
            settings.StartStock,
            settings.RegionNames);
    }

    // The last root that has the folder wins, as a later content root replaces whole items.
    private static string? FindFolder(IReadOnlyList<string> roots, string mapId)
    {
        for (int i = roots.Count - 1; i >= 0; i--)
        {
            string candidate = Path.Combine(roots[i], "maps", mapId);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void WarnAboutUnknownFiles(string folder, string prefix, MapReport report)
    {
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            MapFileName, TerrainFileName, RiversFileName, ElevationFileName, RegionsFileName, DepositsFileName,
        };
        foreach (string crop in ContentNames.NamesOf<CropKind>())
        {
            known.Add(FertilityFileName(crop));
        }

        string[] entries = [.. Directory.EnumerateFileSystemEntries(folder).Select(static p => Path.GetFileName(p))];
        Array.Sort(entries, StringComparer.Ordinal);
        foreach (string name in entries)
        {
            // Dot files (.DS_Store, .gitkeep) and Markdown notes are never content, as in the content loader.
            if (name.StartsWith('.') || name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || known.Contains(name))
            {
                continue;
            }

            report.Warning(DiagnosticCodes.Evl001, prefix + name, string.Empty, 0, 0, "this file is not part of the map format, so it is ignored");
        }
    }

    private static string FertilityFileName(string crop) => FertilityPrefix + crop + ".txt";

    private static TerrainId[]? ReadTerrain(string folder, string prefix, MapSettings settings, int tiles, MapReport report)
    {
        string file = prefix + TerrainFileName;
        string path = Path.Combine(folder, TerrainFileName);
        if (!File.Exists(path))
        {
            report.Error(DiagnosticCodes.Evl001, file, string.Empty, 0, 0, "the required file is missing");
            return null;
        }

        MapGrid? grid = MapGridReader.Read(
            path, file, settings.Width, settings.Height,
            c => c < 128 && (settings.TerrainBySymbol[c] is not null || settings.MissingTerrainBySymbol[c] is not null),
            settings.TerrainAlphabet, report);
        if (grid is not { Fits: true })
        {
            return null;
        }

        var terrain = new TerrainId[tiles];
        bool ok = true;
        for (int y = 0; y < settings.Height; y++)
        {
            for (int x = 0; x < settings.Width; x++)
            {
                char c = grid.At(x, y);
                if (settings.TerrainBySymbol[c] is { } id)
                {
                    terrain[(y * settings.Width) + x] = id;
                }
                else
                {
                    ok = false;
                    report.Error(
                        DiagnosticCodes.Evl002, file, string.Empty, y + 1, x + 1,
                        $"{MapGridReader.Describe(c)} stands for the terrain '{settings.MissingTerrainBySymbol[c]}' in the default legend, which the content does not define");
                }
            }
        }

        return ok ? terrain : null;
    }

    private static byte[]? ReadRivers(string folder, string prefix, MapSettings settings, TerrainId[]? terrain, MapTerrainFeatures[] features, MapReport report)
    {
        int width = settings.Width;
        string file = prefix + RiversFileName;
        string path = Path.Combine(folder, RiversFileName);
        bool exists = File.Exists(path);

        // The layer is required only when the terrain has a river, so find the first river tile.
        int firstRiver = -1;
        if (terrain is not null)
        {
            for (int i = 0; i < terrain.Length; i++)
            {
                if ((features[terrain[i].Value] & MapTerrainFeatures.River) != 0)
                {
                    firstRiver = i;
                    break;
                }
            }
        }

        byte[] widths = new byte[settings.Width * settings.Height];
        if (!exists)
        {
            if (firstRiver >= 0)
            {
                report.Error(
                    DiagnosticCodes.Evl001, file, string.Empty, 0, 0,
                    $"the file is required because the map has river tiles (the first is at line {(firstRiver / width) + 1}, column {(firstRiver % width) + 1} of {TerrainFileName}); write 1 for a narrow and 2 for a wide river, and '.' elsewhere");
                return null;
            }

            return widths;
        }

        MapGrid? grid = MapGridReader.Read(path, file, settings.Width, settings.Height, static c => c is '.' or '1' or '2', ". (none), 1 (narrow) or 2 (wide)", report);
        if (grid is not { Fits: true })
        {
            return null;
        }

        bool ok = true;
        for (int y = 0; y < settings.Height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                char c = grid.At(x, y);
                widths[i] = c == '.' ? (byte)0 : (byte)(c - '0');
                if (terrain is null)
                {
                    continue;
                }

                bool isRiver = (features[terrain[i].Value] & MapTerrainFeatures.River) != 0;
                if (isRiver && c == '.')
                {
                    ok = false;
                    report.Error(DiagnosticCodes.Evl001, file, string.Empty, y + 1, x + 1, $"this is a river tile in {TerrainFileName} and needs a width: write 1 (narrow) or 2 (wide)");
                }
                else if (!isRiver && c != '.')
                {
                    ok = false;
                    report.Error(DiagnosticCodes.Evl001, file, string.Empty, y + 1, x + 1, $"this is not a river tile in {TerrainFileName}, so its width must be '.'");
                }
            }
        }

        return ok ? widths : null;
    }

    private static byte[] ReadByteLayer(string folder, string prefix, string fileName, MapSettings settings, Func<char, int> valueOf, string alphabet, MapReport report)
    {
        byte[] values = new byte[settings.Width * settings.Height];
        string path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
        {
            return values;
        }

        MapGrid? grid = MapGridReader.Read(path, prefix + fileName, settings.Width, settings.Height, c => valueOf(c) >= 0, alphabet, report);
        if (grid is { Fits: true })
        {
            for (int y = 0; y < settings.Height; y++)
            {
                for (int x = 0; x < settings.Width; x++)
                {
                    values[(y * settings.Width) + x] = (byte)valueOf(grid.At(x, y));
                }
            }
        }

        return values;
    }

    private static int ElevationOf(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private static ushort[] ReadRegions(string folder, string prefix, MapSettings settings, MapReport report)
    {
        ushort[] regions = new ushort[settings.Width * settings.Height];
        string path = Path.Combine(folder, RegionsFileName);
        if (!File.Exists(path))
        {
            return regions;
        }

        MapGrid? grid = MapGridReader.Read(
            path, prefix + RegionsFileName, settings.Width, settings.Height, static c => MapJsonReader.TryRegionOf(c, out _), "0-9 or A-Z", report);
        if (grid is { Fits: true })
        {
            for (int y = 0; y < settings.Height; y++)
            {
                for (int x = 0; x < settings.Width; x++)
                {
                    MapJsonReader.TryRegionOf(grid.At(x, y), out regions[(y * settings.Width) + x]);
                }
            }
        }

        return regions;
    }

    private static byte[] ReadDeposits(string folder, string prefix, MapSettings settings, MapReport report)
    {
        byte[] deposits = new byte[settings.Width * settings.Height];
        string path = Path.Combine(folder, DepositsFileName);
        if (!File.Exists(path))
        {
            return deposits;
        }

        MapGrid? grid = MapGridReader.Read(
            path, prefix + DepositsFileName, settings.Width, settings.Height,
            c => c == '.' || (c < 128 && settings.DepositBySymbol[c] is not null),
            settings.DepositAlphabet, report);
        if (grid is { Fits: true })
        {
            for (int y = 0; y < settings.Height; y++)
            {
                for (int x = 0; x < settings.Width; x++)
                {
                    char c = grid.At(x, y);
                    deposits[(y * settings.Width) + x] = settings.DepositBySymbol[c] is { } kind ? (byte)((int)kind + 1) : (byte)0;
                }
            }
        }

        return deposits;
    }

    private static uint[] ReadFertility(string folder, string prefix, MapSettings settings, MapReport report)
    {
        uint[] fertility = new uint[settings.Width * settings.Height];
        foreach (CropKind crop in Enum.GetValues<CropKind>())
        {
            string fileName = FertilityFileName(ContentNames.NameOf(crop));
            string path = Path.Combine(folder, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            MapGrid? grid = MapGridReader.Read(path, prefix + fileName, settings.Width, settings.Height, static c => c is '.' or '#', ". (not fertile) or # (fertile)", report);
            if (grid is not { Fits: true })
            {
                continue;
            }

            uint bit = 1u << (int)crop;
            for (int y = 0; y < settings.Height; y++)
            {
                for (int x = 0; x < settings.Width; x++)
                {
                    if (grid.At(x, y) == '#')
                    {
                        fertility[(y * settings.Width) + x] |= bit;
                    }
                }
            }
        }

        return fertility;
    }

    // The Founders' Hall must fit at the start site: inside the map, on buildable ground.
    private static void CheckStartSite(MapSettings settings, TerrainId[] terrain, ContentDb content, MapReport report)
    {
        if (!settings.HasStartSite)
        {
            return;
        }

        MapStartSite start = settings.StartSite;
        Footprint footprint = content.Get(content.Required.FoundersHall).Footprint;
        bool swaps = (start.Rotation & 1) == 1;
        int footprintWidth = swaps ? footprint.Height : footprint.Width;
        int footprintHeight = swaps ? footprint.Width : footprint.Height;

        if (start.X + footprintWidth > settings.Width || start.Y + footprintHeight > settings.Height)
        {
            settings.Site.Error(
                DiagnosticCodes.Evl001, "/startSite", false,
                $"the Founders' Hall footprint of {footprintWidth} x {footprintHeight} tiles at ({start.X}, {start.Y}) leaves the {settings.Width} x {settings.Height} map");
            return;
        }

        for (int dy = 0; dy < footprintHeight; dy++)
        {
            for (int dx = 0; dx < footprintWidth; dx++)
            {
                TerrainId id = terrain[((start.Y + dy) * settings.Width) + start.X + dx];
                if (!content.Get(id).Buildable)
                {
                    settings.Site.Error(
                        DiagnosticCodes.Evl001, "/startSite", false,
                        $"the Founders' Hall footprint covers ({start.X + dx}, {start.Y + dy}), which is '{content.Terrains.NameOf(id)}' and not buildable");
                    return;
                }
            }
        }
    }
}
