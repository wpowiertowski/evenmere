using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;

namespace Evenmere.Game.Rendering.Life;

/// <summary>
/// The load colours for goods, parsed from <c>game/data/visuals/goods.json</c> without touching the GPU: plain file
/// IO and JSON, so the smoke test's load path stays GPU-free. A good with no entry, or a bad file, falls back to a
/// neutral sack brown with one warning per good, so one typo never blanks every cart.
/// </summary>
internal sealed class GoodColours
{
    private static readonly string _mappingPath = "res://data/visuals/goods.json";
    private static readonly Regex _colourPattern = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly Color _fallback = Color.FromHtml("#9A7B52").SrgbToLinear();

    private readonly Dictionary<string, Color> _colours = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    /// <summary>Loads the mapping; missing or broken files warn and fall back entirely.</summary>
    public void Load()
    {
        _colours.Clear();

        string json = Godot.FileAccess.GetFileAsString(_mappingPath);
        if (string.IsNullOrEmpty(json))
        {
            GD.PushWarning($"[Evenmere] Life: missing goods mapping '{_mappingPath}'; every load falls back to brown.");
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("goods", out JsonElement goods)
                || goods.ValueKind != JsonValueKind.Object)
            {
                GD.PushWarning("[Evenmere] Life: goods mapping has no 'goods' object; every load falls back to brown.");
                return;
            }

            foreach (JsonProperty property in goods.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object
                    || !property.Value.TryGetProperty("colour", out JsonElement colourElement)
                    || colourElement.ValueKind != JsonValueKind.String)
                {
                    GD.PushWarning($"[Evenmere] Life: good '{property.Name}' has no colour; skipped.");
                    continue;
                }

                string colour = colourElement.GetString() ?? string.Empty;
                if (!_colourPattern.IsMatch(colour))
                {
                    GD.PushWarning($"[Evenmere] Life: good '{property.Name}' colour '{colour}' must be #RRGGBB; skipped.");
                    continue;
                }

                _colours[property.Name] = Color.FromHtml(colour).SrgbToLinear();
            }
        }
        catch (JsonException ex)
        {
            GD.PushWarning($"[Evenmere] Life: goods mapping is not valid JSON ({ex.Message}); every load falls back to brown.");
        }
    }

    /// <summary>Gets the linear load colour of a good by its content ID.</summary>
    /// <param name="goodName">The content good ID, such as <c>bread</c>.</param>
    /// <returns>The mapped colour, or the sack-brown fallback.</returns>
    public Color ColourFor(string goodName)
    {
        if (_colours.TryGetValue(goodName, out Color colour))
        {
            return colour;
        }

        if (_warned.Add(goodName))
        {
            GD.PushWarning($"[Evenmere] Life: no load colour for good '{goodName}'; using the fallback brown.");
        }

        return _fallback;
    }
}
