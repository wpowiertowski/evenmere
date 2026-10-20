using System.Collections.Immutable;
using System.Text;
using Evenmere.Core.Ids;

namespace Evenmere.Content.Loading;

/// <summary>The ID grammar: lowercase snake_case.</summary>
internal static class Ids
{
    private const int MaxLength = 64;

    /// <summary>Tests an ID against <c>[a-z][a-z0-9]*(_[a-z0-9]+)*</c>, at most 64 characters.</summary>
    public static bool IsValid(string id)
    {
        if (id.Length is 0 or > MaxLength || !char.IsAsciiLetterLower(id[0]))
        {
            return false;
        }

        bool previousUnderscore = false;
        for (int i = 1; i < id.Length; i++)
        {
            char c = id[i];
            if (c == '_')
            {
                if (previousUnderscore)
                {
                    return false;
                }

                previousUnderscore = true;
            }
            else if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c))
            {
                previousUnderscore = false;
            }
            else
            {
                return false;
            }
        }

        return !previousUnderscore;
    }
}

/// <summary>RFC 6901 JSON pointers.</summary>
internal static class JsonPointer
{
    /// <summary>Appends one reference token (a property name) to a pointer, escaping <c>~</c> and <c>/</c>.</summary>
    public static string Append(string pointer, string token)
    {
        var text = new StringBuilder(pointer.Length + token.Length + 1);
        text.Append(pointer).Append('/');
        foreach (char c in token)
        {
            switch (c)
            {
                case '~':
                    text.Append("~0");
                    break;
                case '/':
                    text.Append("~1");
                    break;
                default:
                    text.Append(c);
                    break;
            }
        }

        return text.ToString();
    }

    /// <summary>Appends an array index to a pointer.</summary>
    public static string Append(string pointer, int index) =>
        pointer + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// The string IDs of one content type and the typed IDs they intern to: sorted ordinally, then numbered from 0.
/// </summary>
internal sealed class InternTable<TId>
    where TId : struct, IContentId<TId>
{
    private readonly Dictionary<string, ushort> _byName;

    public InternTable(string singular, ImmutableArray<string> sortedNames)
    {
        Singular = singular;
        Names = sortedNames;
        _byName = new Dictionary<string, ushort>(sortedNames.Length, StringComparer.Ordinal);
        for (int i = 0; i < sortedNames.Length; i++)
        {
            _byName.Add(sortedNames[i], (ushort)i);
        }
    }

    /// <summary>Gets the singular name of the content type, for messages.</summary>
    public string Singular { get; }

    /// <summary>Gets the names in ID order.</summary>
    public ImmutableArray<string> Names { get; }

    public bool TryGet(string name, out TId id)
    {
        if (_byName.TryGetValue(name, out ushort value))
        {
            id = TId.From(value);
            return true;
        }

        id = default;
        return false;
    }
}

/// <summary>The intern tables of every content type, which references resolve against.</summary>
internal sealed class ResolveTables
{
    private ResolveTables()
    {
    }

    public required InternTable<GoodId> Goods { get; init; }

    public required InternTable<BuildingTypeId> Buildings { get; init; }

    public required InternTable<RecipeId> Recipes { get; init; }

    public required InternTable<TierId> Tiers { get; init; }

    public required InternTable<TechId> Techs { get; init; }

    public required InternTable<CivicId> Civics { get; init; }

    public required InternTable<TraditionId> Traditions { get; init; }

    public required InternTable<FestivalId> Festivals { get; init; }

    public required InternTable<GreatWorkTypeId> GreatWorks { get; init; }

    public required InternTable<MonumentId> Monuments { get; init; }

    public required InternTable<HappeningId> Happenings { get; init; }

    public required InternTable<EraId> Eras { get; init; }

    public required InternTable<TerrainId> Terrains { get; init; }

    /// <summary>Gets the era numbers that some era defines. Filled once the eras have been read; used to check <c>era</c> fields.</summary>
    public HashSet<int> EraOrders { get; } = [];

    /// <summary>Interns the IDs of every item in <paramref name="raw"/>.</summary>
    public static ResolveTables Create(RawContent raw, List<ContentDiagnostic> diagnostics)
    {
        InternTable<TId> Table<TId>(ContentType type)
            where TId : struct, IContentId<TId>
        {
            // Items are already sorted by type and then by ordinal ID, so the names are in intern order.
            ImmutableArray<string>.Builder names = ImmutableArray.CreateBuilder<string>();
            foreach (RawItem item in raw.OfType(type))
            {
                if (names.Count > ushort.MaxValue)
                {
                    diagnostics.Add(new ContentDiagnostic(
                        DiagnosticCodes.Evl001,
                        DiagnosticSeverity.Error,
                        item.File,
                        string.Empty,
                        $"more than {ushort.MaxValue + 1} {type.SingularName()} items; IDs are 16-bit"));
                    break;
                }

                names.Add(item.Id);
            }

            return new InternTable<TId>(type.SingularName(), names.ToImmutable());
        }

        return new ResolveTables
        {
            Goods = Table<GoodId>(ContentType.Goods),
            Buildings = Table<BuildingTypeId>(ContentType.Buildings),
            Recipes = Table<RecipeId>(ContentType.Recipes),
            Tiers = Table<TierId>(ContentType.Tiers),
            Techs = Table<TechId>(ContentType.Techs),
            Civics = Table<CivicId>(ContentType.Civics),
            Traditions = Table<TraditionId>(ContentType.Traditions),
            Festivals = Table<FestivalId>(ContentType.Festivals),
            GreatWorks = Table<GreatWorkTypeId>(ContentType.GreatWorks),
            Monuments = Table<MonumentId>(ContentType.Monuments),
            Happenings = Table<HappeningId>(ContentType.Happenings),
            Eras = Table<EraId>(ContentType.Eras),
            Terrains = Table<TerrainId>(ContentType.Terrains),
        };
    }
}
