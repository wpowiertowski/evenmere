using System.Collections.Immutable;

namespace Evenmere.Content;

/// <summary>The kinds of content file the loader reads. Each one lives in <c>content/&lt;directory&gt;/</c>.</summary>
public enum ContentType
{
    /// <summary>Goods, in <c>content/goods/</c>.</summary>
    Goods = 0,

    /// <summary>Buildings, in <c>content/buildings/</c>.</summary>
    Buildings = 1,

    /// <summary>Production recipes, in <c>content/recipes/</c>.</summary>
    Recipes = 2,

    /// <summary>Population tiers, in <c>content/tiers/</c>.</summary>
    Tiers = 3,

    /// <summary>Technologies, in <c>content/techs/</c>.</summary>
    Techs = 4,

    /// <summary>Civics, in <c>content/civics/</c>.</summary>
    Civics = 5,

    /// <summary>Traditions, in <c>content/traditions/</c>.</summary>
    Traditions = 6,

    /// <summary>Festivals, in <c>content/festivals/</c>.</summary>
    Festivals = 7,

    /// <summary>Great-work types, in <c>content/greatworks/</c>.</summary>
    GreatWorks = 8,

    /// <summary>Monuments and wonders, in <c>content/monuments/</c>.</summary>
    Monuments = 9,

    /// <summary>Happenings, in <c>content/happenings/</c>.</summary>
    Happenings = 10,

    /// <summary>Eras, in <c>content/eras/</c>.</summary>
    Eras = 11,

    /// <summary>Terrains, in <c>content/terrains/</c>.</summary>
    Terrains = 12,

    /// <summary>The single tunables file <c>content/rules/core.json</c>; its item ID is <c>core</c>.</summary>
    Rules = 13,
}

/// <summary>Names and lookups for <see cref="ContentType"/>.</summary>
public static class ContentTypes
{
    /// <summary>The ID of the only item of type <see cref="ContentType.Rules"/>, which is also its file name.</summary>
    public const string RulesId = "core";

    private static readonly ImmutableArray<string> _directories =
    [
        "goods", "buildings", "recipes", "tiers", "techs", "civics", "traditions",
        "festivals", "greatworks", "monuments", "happenings", "eras", "terrains", "rules",
    ];

    private static readonly ImmutableArray<string> _singulars =
    [
        "good", "building", "recipe", "tier", "tech", "civic", "tradition",
        "festival", "greatwork", "monument", "happening", "era", "terrain", "rules",
    ];

    /// <summary>Gets every content type, in declaration order.</summary>
    public static ImmutableArray<ContentType> All { get; } =
    [
        ContentType.Goods, ContentType.Buildings, ContentType.Recipes, ContentType.Tiers, ContentType.Techs,
        ContentType.Civics, ContentType.Traditions, ContentType.Festivals, ContentType.GreatWorks,
        ContentType.Monuments, ContentType.Happenings, ContentType.Eras, ContentType.Terrains, ContentType.Rules,
    ];

    /// <summary>Gets the directory under the content root that holds a type's files, for example <c>buildings</c>.</summary>
    /// <param name="type">The content type.</param>
    /// <returns>The directory name.</returns>
    public static string DirectoryName(this ContentType type) => _directories[(int)type];

    /// <summary>
    /// Gets the singular name used in messages and as the locale-key prefix, for example <c>building</c>
    /// (so <c>building.bakery.name</c>).
    /// </summary>
    /// <param name="type">The content type.</param>
    /// <returns>The singular name.</returns>
    public static string SingularName(this ContentType type) => _singulars[(int)type];

    /// <summary>Finds the content type whose directory has the given name.</summary>
    /// <param name="directory">The directory name, such as <c>goods</c>.</param>
    /// <param name="type">The content type, when found.</param>
    /// <returns><see langword="true"/> if the directory belongs to a content type.</returns>
    public static bool TryFromDirectory(string directory, out ContentType type)
    {
        ArgumentNullException.ThrowIfNull(directory);
        for (int i = 0; i < _directories.Length; i++)
        {
            if (string.Equals(_directories[i], directory, StringComparison.Ordinal))
            {
                type = (ContentType)i;
                return true;
            }
        }

        type = default;
        return false;
    }
}
