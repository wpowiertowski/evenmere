using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Evenmere.Content;

/// <summary>Overrides the JSON spelling of an enum member, which is otherwise the member name in <c>snake_case</c>.</summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class ContentNameAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="ContentNameAttribute"/> class.</summary>
    /// <param name="name">The exact JSON string for the member.</param>
    public ContentNameAttribute(string name)
    {
        Name = name;
    }

    /// <summary>Gets the exact JSON string for the member.</summary>
    public string Name { get; }
}

/// <summary>
/// The JSON spelling of every member of an enum used in content files: the member name in <c>snake_case</c>
/// (<c>ConcertHall</c> becomes <c>concert_hall</c>) unless a <see cref="ContentNameAttribute"/> says otherwise.
/// The tables are built from the enum itself, so a new member is accepted by the loader without further edits;
/// the schemas (M0-04) read <see cref="NamesOf{T}"/> to list the allowed values.
/// </summary>
public static class ContentNames
{
    /// <summary>Gets the keys of the single-key effect objects, such as <c>unlockBuilding</c>. Each has one sealed record in <c>Evenmere.Content.Effects</c>.</summary>
    public static ImmutableArray<string> EffectKinds { get; } =
    [
        "unlockBuilding", "unlockRecipe", "unlockTier", "unlockTradition", "unlockFestival", "unlockTerrainAccess",
        "unlockFeature", "traditionSlot", "modifier", "grantGoods", "grantPoints", "revealCoastline", "requestFestival",
    ];

    /// <summary>Gets the keys of the single-key condition objects, such as <c>produced</c>. Each has one sealed record in <c>Evenmere.Content.Conditions</c>.</summary>
    public static ImmutableArray<string> ConditionKinds { get; } =
    [
        "count", "produced", "consumed", "stock", "population", "roadTiles", "expeditions", "festivalsHeld", "greatWorks",
        "visitorsPerDay", "researched", "adopted", "discovered", "season", "era", "all", "any", "not",
    ];

    /// <summary>
    /// Gets the keys of a modifier <c>target</c> (or aura target) object. Leaving a modifier's target out means island-wide.
    /// </summary>
    public static ImmutableArray<string> ModifierTargetKinds { get; } = ["building", "tag", "category", "tier", "good", "discovery"];

    /// <summary>Gets the keys of a building <c>siteBonus.when</c> object.</summary>
    public static ImmutableArray<string> SiteConditionKinds { get; } = ["terrain", "near"];

    /// <summary>Gets the values a modifier's <c>discovery</c> target accepts: the discoveries that sit on the map.</summary>
    public static ImmutableArray<string> DiscoveryTargetKinds { get; } = ["natural_wonder", "ruin", "shipwreck"];

    /// <summary>Gets every JSON name of an enum, in the order its members are declared.</summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <returns>The names.</returns>
    public static ImmutableArray<string> NamesOf<T>()
        where T : struct, Enum => Cache<T>.Names;

    /// <summary>Finds the member with a JSON name.</summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="name">The JSON string.</param>
    /// <param name="value">The member, when found.</param>
    /// <returns><see langword="true"/> if the name is known; the comparison is ordinal.</returns>
    public static bool TryParse<T>(string name, out T value)
        where T : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(name);
        return Cache<T>.ByName.TryGetValue(name, out value);
    }

    /// <summary>Gets the JSON name of a member.</summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="value">The member.</param>
    /// <returns>The JSON string.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not a declared member.</exception>
    public static string NameOf<T>(T value)
        where T : struct, Enum
    {
        foreach ((string name, T candidate) in Cache<T>.Entries)
        {
            if (EqualityComparer<T>.Default.Equals(candidate, value))
            {
                return name;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, "Not a declared member of " + typeof(T).Name + ".");
    }

    private static string ToSnakeCase(string pascal)
    {
        StringBuilder text = new(pascal.Length + 4);
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    text.Append('_');
                }

                text.Append(char.ToLowerInvariant(c));
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    private static class Cache<T>
        where T : struct, Enum
    {
        public static readonly ImmutableArray<(string Name, T Value)> Entries = Build();

        // Only ever used for TryGetValue; nothing iterates it, so its order never leaks (ADR-0003).
        public static readonly Dictionary<string, T> ByName = BuildLookup();

        public static readonly ImmutableArray<string> Names = [.. Entries.Select(static e => e.Name)];

        private static ImmutableArray<(string Name, T Value)> Build()
        {
            ImmutableArray<(string, T)>.Builder builder = ImmutableArray.CreateBuilder<(string, T)>();
            foreach (T value in Enum.GetValues<T>().OrderBy(static v => Convert.ToInt64(v, CultureInfo.InvariantCulture)))
            {
                string memberName = value.ToString();
                FieldInfo? field = typeof(T).GetField(memberName, BindingFlags.Public | BindingFlags.Static);
                string name = field?.GetCustomAttribute<ContentNameAttribute>()?.Name ?? ToSnakeCase(memberName);
                builder.Add((name, value));
            }

            return builder.ToImmutable();
        }

        private static Dictionary<string, T> BuildLookup()
        {
            Dictionary<string, T> lookup = new(StringComparer.Ordinal);
            foreach ((string name, T value) in Entries)
            {
                lookup.Add(name, value);
            }

            return lookup;
        }
    }
}
