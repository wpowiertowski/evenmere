using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;

namespace Evenmere.Game.Ui.Exploration;

/// <summary>
/// Where an expedition target or discovery gets its display name (M2-23, round 2): the map's own region
/// names first, then the generated <c>region.generated.N</c> keys, then a numbered fallback. It is pure
/// over a string lookup so the fallback chain is unit-tested; the panels feed it <see cref="UiStrings"/>
/// and the map's keys.
/// </summary>
internal static class RegionNames
{
    /// <summary>The locale key of the numbered fallback; <c>{region}</c> takes the region number.</summary>
    public const string FallbackKey = "ui.exploration.region.fallback";

    /// <summary>The prefix of the generated-island name keys (M2-13b's <c>names.json</c>).</summary>
    public const string GeneratedPrefix = "region.generated.";

    /// <summary>
    /// Resolves a region's display name: the map's locale key when it names something, then the generated
    /// key, then the numbered fallback. A key that looks up to itself counts as missing, so absent
    /// <c>names.json</c> entries fall through with no error.
    /// </summary>
    /// <param name="lookup">The display text for a locale key, or the key itself when it has none.</param>
    /// <param name="mapKeys">The map's region-number-to-locale-key table, if the island ships one.</param>
    /// <param name="region">The region number.</param>
    /// <returns>The display name.</returns>
    internal static string Resolve(Func<string, string> lookup, IReadOnlyDictionary<ushort, string>? mapKeys, ushort region)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        // The map stores the key prefix (for example <c>region.starter.landing</c>); the display text lives
        // under <c>.name</c>, like every other locale entry. Generated keys are tried whole and with the
        // suffix, so either names.json convention resolves with no error.
        if (mapKeys is not null && mapKeys.TryGetValue(region, out string? key))
        {
            string named = lookup(key + ".name");
            if (!string.Equals(named, key + ".name", StringComparison.Ordinal))
            {
                return named;
            }
        }

        string generated = GeneratedPrefix + region.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (string candidate in (string[])[generated, generated + ".name"])
        {
            string generatedName = lookup(candidate);
            if (!string.Equals(generatedName, candidate, StringComparison.Ordinal))
            {
                return generatedName;
            }
        }

        return lookup(FallbackKey).Replace(
            "{region}",
            region.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Loads the running island's region-number-to-locale-key table, if its map ships one. Generated
    /// islands have no map folder, so their ids fail to load and read <see langword="null"/>: callers then
    /// fall through to the generated keys. Results are cached by map id; the game thread is the only caller.
    /// </summary>
    /// <param name="host">The running simulation host.</param>
    /// <returns>The table, or <see langword="null"/> when the island names no regions.</returns>
    internal static IReadOnlyDictionary<ushort, string>? ForMap(SimHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        string mapId = host.MapId;
        if (_cache.TryGetValue(mapId, out IReadOnlyDictionary<ushort, string>? cached))
        {
            return cached;
        }

        IReadOnlyDictionary<ushort, string>? keys = null;
        try
        {
            Evenmere.Content.Maps.MapDef map = Evenmere.Content.Maps.MapLoader.Load(
                [ContentPaths.Resolve()], mapId, host.Content).GetMapOrThrow();
            if (map.RegionNames.Length > 0)
            {
                var table = new Dictionary<ushort, string>();
                foreach (Evenmere.Content.Maps.MapRegionName entry in map.RegionNames)
                {
                    table[entry.Region] = entry.LocaleKey;
                }

                keys = table;
            }
        }
        catch (Exception)
        {
            // Generated islands (and saves whose map folder is gone) name no regions here; the generated
            // keys and the numbered fallback cover them. One line in the log, then silence.
            Godot.GD.Print($"[Evenmere] regions: no shipped names for map '{mapId}'; using generated keys.");
        }

        _cache[mapId] = keys;
        return keys;
    }

    private static readonly Dictionary<string, IReadOnlyDictionary<ushort, string>?> _cache = new(StringComparer.Ordinal);
}
