using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Time;
using Evenmere.Game.Ui.Common;

namespace Evenmere.Game.Ui.Progress;

/// <summary>
/// Resolves the names a condition sentence needs. The formatter asks for display names only, so tests can
/// feed it fixed strings while the game reads them from content and locale.
/// </summary>
public interface IConditionNames
{
    /// <summary>Gets the display name of a good.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The name.</returns>
    string Good(GoodId good);

    /// <summary>Gets the display name of a building type.</summary>
    /// <param name="building">The building type.</param>
    /// <returns>The name.</returns>
    string Building(BuildingTypeId building);

    /// <summary>Gets the display name of a population tier.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The name.</returns>
    string Tier(TierId tier);

    /// <summary>Gets the display name of a technology.</summary>
    /// <param name="tech">The tech.</param>
    /// <returns>The name.</returns>
    string Tech(TechId tech);

    /// <summary>Gets the display name of a civic.</summary>
    /// <param name="civic">The civic.</param>
    /// <returns>The name.</returns>
    string Civic(CivicId civic);

    /// <summary>Gets the display name of an era.</summary>
    /// <param name="era">The era.</param>
    /// <returns>The name.</returns>
    string Era(EraId era);

    /// <summary>Gets the display name of a terrain.</summary>
    /// <param name="terrain">The terrain.</param>
    /// <returns>The name.</returns>
    string Terrain(TerrainId terrain);

    /// <summary>Gets the plural display name of a building tag, such as <c>house</c>.</summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The name, or the tag itself when no text names it.</returns>
    string Tag(string tag);

    /// <summary>Gets the plural display name of a building category.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The name.</returns>
    string Category(BuildingCategory category);

    /// <summary>Gets the display name of a season.</summary>
    /// <param name="season">The season.</param>
    /// <returns>The name.</returns>
    string Season(Season season);

    /// <summary>Gets the display name of a kind of discovery.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The name.</returns>
    string Discovery(DiscoveryKind kind);

    /// <summary>Gets the display name of a great-work type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The name.</returns>
    string GreatWorkType(GreatWorkTypeId type);
}

/// <summary>
/// Reads condition names from the content database and the locale table, exactly as the rows show them.
/// </summary>
public sealed class ContentConditionNames : IConditionNames
{
    private readonly ContentDb _content;
    private readonly UiStrings _strings;

    /// <summary>Initializes a new instance of the <see cref="ContentConditionNames"/> class.</summary>
    /// <param name="content">The content database.</param>
    /// <param name="strings">The display text.</param>
    public ContentConditionNames(ContentDb content, UiStrings strings)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(strings);
        _content = content;
        _strings = strings;
    }

    /// <inheritdoc />
    public string Good(GoodId good) => _strings.GoodName(_content.NameOf(good));

    /// <inheritdoc />
    public string Building(BuildingTypeId building) => _strings.Get($"building.{_content.NameOf(building)}.name");

    /// <inheritdoc />
    public string Tier(TierId tier) => _strings.TierName(_content.NameOf(tier));

    /// <inheritdoc />
    public string Tech(TechId tech) => _strings.Get($"tech.{_content.NameOf(tech)}.name");

    /// <inheritdoc />
    public string Civic(CivicId civic) => _strings.Get($"civic.{_content.NameOf(civic)}.name");

    /// <inheritdoc />
    public string Era(EraId era) => _strings.Get($"era.{_content.NameOf(era)}.name");

    /// <inheritdoc />
    public string Terrain(TerrainId terrain) => _strings.Get($"terrain.{_content.NameOf(terrain)}.name");

    /// <inheritdoc />
    public string Tag(string tag) => WithFallback($"ui.condition.tag.{tag}", tag);

    /// <inheritdoc />
    public string Category(BuildingCategory category) =>
        WithFallback($"ui.condition.category.{category.ToString().ToLowerInvariant()}", category.ToString());

    /// <inheritdoc />
    public string Season(Season season) => _strings.Get($"ui.hud.season.{season.ToString().ToLowerInvariant()}");

    /// <inheritdoc />
    public string Discovery(DiscoveryKind kind) => _strings.Get($"ui.condition.discovery.{ToSnake(kind.ToString())}");

    /// <inheritdoc />
    public string GreatWorkType(GreatWorkTypeId type) => _strings.Get($"greatwork.{_content.NameOf(type)}.name");

    private string WithFallback(string key, string fallback)
    {
        string text = _strings.Get(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? fallback : text;
    }

    private static string ToSnake(string name)
    {
        var snake = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                snake.Append('_');
            }

            snake.Append(char.ToLowerInvariant(c));
        }

        return snake.ToString();
    }
}
