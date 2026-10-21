using System.Text.Json;
using System.Text.RegularExpressions;

namespace Evenmere.Game.Data.Visuals;

/// <summary>
/// The placeholder shapes <c>PlaceholderMeshFactory</c> can build from a footprint. The names match the
/// <c>shape</c> values in <c>game/data/visuals/era1-slice.json</c>.
/// </summary>
public enum BuildingShape : byte
{
    /// <summary>A small body with a gable roof and no chimney.</summary>
    Hut = 0,

    /// <summary>A body with a gable roof and a chimney.</summary>
    House = 1,

    /// <summary>A large body with a hip roof.</summary>
    Hall = 2,

    /// <summary>Low field strips across the footprint.</summary>
    Farm = 3,

    /// <summary>A tower with sails.</summary>
    Mill = 4,

    /// <summary>A body with a canopy roof on posts.</summary>
    Market = 5,

    /// <summary>A tall body with a steep gable roof.</summary>
    Shrine = 6,

    /// <summary>A wide body with a gable roof and double doors.</summary>
    Storehouse = 7,

    /// <summary>Road strips; only used for the road colour.</summary>
    Road = 8,

    /// <summary>A plain box. The fallback for a building ID with no mapping, and never authored in JSON.</summary>
    Box = 9,
}

/// <summary>The placeholder look of one building type: its palette colour, shape, optional real scene and icon.</summary>
/// <param name="Colour">The sRGB hex colour (<c>#RRGGBB</c>), as authored in JSON.</param>
/// <param name="Shape">The placeholder shape built from the footprint.</param>
/// <param name="Scene">The real scene path, when one exists; <see langword="null"/> while placeholders are used.</param>
/// <param name="Icon">The placeholder icon glyph or path, for later menus.</param>
public sealed record BuildingVisual(string Colour, BuildingShape Shape, string? Scene, string Icon);

/// <summary>
/// The parsed <c>era1-slice.json</c> mapping, keyed by content building ID. Godot-free on purpose, so the mapping can be
/// validated without a GPU (the smoke test loads it on its path).
/// </summary>
public sealed class BuildingVisualMap
{
    private static readonly Regex _colourPattern = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    private readonly Dictionary<string, BuildingVisual> _entries;

    /// <summary>Initializes a new instance of the <see cref="BuildingVisualMap"/> class.</summary>
    /// <param name="entries">The validated entries, keyed by content building ID.</param>
    public BuildingVisualMap(Dictionary<string, BuildingVisual> entries)
    {
        _entries = entries;
    }

    /// <summary>Gets how many building IDs have a mapping.</summary>
    public int Count => _entries.Count;

    /// <summary>Finds the mapping for a content building ID.</summary>
    /// <param name="typeName">The content building ID, such as <c>bakery</c>.</param>
    /// <param name="visual">The mapping, when one exists.</param>
    /// <returns><see langword="true"/> when the building ID is mapped.</returns>
    public bool TryGet(string typeName, out BuildingVisual visual) => _entries.TryGetValue(typeName, out visual!);

    /// <summary>
    /// Parses and validates the mapping JSON. Bad entries are skipped with one warning each through
    /// <paramref name="warn"/>, so one typo never blanks the whole island.
    /// </summary>
    /// <param name="json">The file text.</param>
    /// <param name="warn">Receives each validation warning.</param>
    /// <returns>The map of the valid entries.</returns>
    public static BuildingVisualMap Load(string json, Action<string> warn)
    {
        var entries = new Dictionary<string, BuildingVisual>(StringComparer.Ordinal);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            warn($"buildings: file is not valid JSON ({ex.Message}); no placeholder mapping loaded.");
            return new BuildingVisualMap(entries);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("buildings", out JsonElement buildings)
                || buildings.ValueKind != JsonValueKind.Object)
            {
                warn("buildings: missing 'buildings' object; no placeholder mapping loaded.");
                return new BuildingVisualMap(entries);
            }

            foreach (JsonProperty property in buildings.EnumerateObject())
            {
                BuildingVisual? visual = ParseEntry(property.Name, property.Value, warn);
                if (visual is not null)
                {
                    entries[property.Name] = visual;
                }
            }
        }

        return new BuildingVisualMap(entries);
    }

    private static BuildingVisual? ParseEntry(string typeName, JsonElement element, Action<string> warn)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            warn($"{typeName}: entry must be an object; skipped.");
            return null;
        }

        string colour = element.TryGetProperty("colour", out JsonElement colourElement)
            && colourElement.ValueKind == JsonValueKind.String
            ? colourElement.GetString() ?? string.Empty
            : string.Empty;
        if (!_colourPattern.IsMatch(colour))
        {
            warn($"{typeName}: colour '{colour}' must be #RRGGBB; skipped.");
            return null;
        }

        string shapeText = element.TryGetProperty("shape", out JsonElement shapeElement)
            && shapeElement.ValueKind == JsonValueKind.String
            ? shapeElement.GetString() ?? string.Empty
            : string.Empty;
        if (!TryParseShape(shapeText, out BuildingShape shape) || shape == BuildingShape.Box)
        {
            warn($"{typeName}: shape '{shapeText}' is not a placeholder shape (hut, house, hall, farm, mill, market, shrine, storehouse, road); skipped.");
            return null;
        }

        string? scene = element.TryGetProperty("scene", out JsonElement sceneElement)
            && sceneElement.ValueKind == JsonValueKind.String
            ? sceneElement.GetString()
            : null;
        if (scene is not null && string.IsNullOrWhiteSpace(scene))
        {
            scene = null;
        }

        string icon = element.TryGetProperty("icon", out JsonElement iconElement)
            && iconElement.ValueKind == JsonValueKind.String
            ? iconElement.GetString() ?? string.Empty
            : string.Empty;

        return new BuildingVisual(colour, shape, scene, icon);
    }

    private static bool TryParseShape(string text, out BuildingShape shape)
    {
        foreach (BuildingShape candidate in Enum.GetValues<BuildingShape>())
        {
            if (string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                shape = candidate;
                return true;
            }
        }

        shape = default;
        return false;
    }
}
