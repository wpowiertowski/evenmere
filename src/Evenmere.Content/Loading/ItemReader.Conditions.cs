using System.Collections.Immutable;
using System.Text.Json;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;

namespace Evenmere.Content.Loading;

// The single-key condition union.
internal sealed partial class ItemReader
{
    private const int MaxCount = 1_000_000_000;

    /// <summary>Reads a condition: an object with exactly one key. An unknown key is a diagnostic.</summary>
    public Condition? ReadCondition(JsonElement element, string pointer)
    {
        if (!TrySingleKey(element, pointer, "a condition", out string kind, out JsonElement value, out string at))
        {
            return null;
        }

        switch (kind)
        {
            case "count":
                return ReadCount(value, at);
            case "produced":
                return ReadGoodQuantity(value, at, static (good, atLeast) => new ProducedCondition(good, atLeast));
            case "consumed":
                return ReadGoodQuantity(value, at, static (good, atLeast) => new ConsumedCondition(good, atLeast));
            case "stock":
                return ReadGoodQuantity(value, at, static (good, atLeast) => new StockCondition(good, atLeast));
            case "population":
                return WithObject(value, at, o =>
                {
                    TierId? tier = o.OptRef(Tables.Tiers, "tier");
                    return new PopulationCondition(tier, o.ReqInt("atLeast", 1, MaxCount));
                });
            case "roadTiles":
                return WithObject(value, at, o =>
                {
                    BuildingTypeId? road = o.OptRef(Tables.Buildings, "road");
                    return new RoadTilesCondition(road, o.ReqInt("atLeast", 1, MaxCount));
                });
            case "expeditions":
                return WithObject(value, at, static o => new ExpeditionsCondition(o.ReqInt("atLeast", 1, MaxCount)));
            case "festivalsHeld":
                return WithObject(value, at, static o => new FestivalsHeldCondition(o.ReqInt("atLeast", 1, MaxCount)));
            case "visitorsPerDay":
                return WithObject(value, at, static o => new VisitorsPerDayCondition(o.ReqInt("atLeast", 1, MaxCount)));
            case "greatWorks":
                return WithObject(value, at, o =>
                {
                    GreatWorkTypeId? type = o.OptRef(Tables.GreatWorks, "type");
                    bool displayed = o.OptBool("displayed", false);
                    return new GreatWorksCondition(type, displayed, o.ReqInt("atLeast", 1, MaxCount));
                });
            case "researched":
                return TryRef(value, at, Tables.Techs, out TechId tech) ? new ResearchedCondition(tech) : null;
            case "adopted":
                return TryRef(value, at, Tables.Civics, out CivicId civic) ? new AdoptedCondition(civic) : null;
            case "discovered":
                return ReadDiscovered(value, at);
            case "season":
                return TryEnum(value, at, out Season season) ? new SeasonCondition(season) : null;
            case "era":
                return TryRef(value, at, Tables.Eras, out EraId era) ? new EraCondition(era) : null;
            case "all":
                return ReadConditionList(value, at) is { } all ? new AllCondition(all) : null;
            case "any":
                return ReadConditionList(value, at) is { } any ? new AnyCondition(any) : null;
            case "not":
                return ReadCondition(value, at) is { } negated ? new NotCondition(negated) : null;
            default:
                Error(at, $"unknown condition '{kind}'; expected one of: {string.Join(", ", ContentNames.ConditionKinds)}");
                return null;
        }
    }

    /// <summary>Opens the object, runs the reader and returns its result only if no error was reported on the way.</summary>
    private T? WithObject<T>(JsonElement value, string pointer, Func<ObjectView, T?> read)
        where T : class
    {
        using ObjectView? o = OpenObject(value, pointer);
        if (o is null)
        {
            return null;
        }

        int errorsBefore = ErrorCount;
        T? result = read(o);
        return ErrorCount == errorsBefore ? result : null;
    }

    private Condition? ReadCount(JsonElement value, string pointer) =>
        WithObject<Condition>(value, pointer, o =>
        {
            BuildingSelector? selector = ReadSelector(o, out bool selectorGiven);
            TerrainId? terrain = o.OptRef(Tables.Terrains, "terrain");
            int atLeast = o.ReqInt("atLeast", 1, MaxCount);
            if (!selectorGiven && !o.TryGet("terrain", out _))
            {
                Error(o.Pointer, "a count needs 'building', 'tag' or 'category', or a 'terrain' to count every building on it");
            }

            return new CountCondition(selector, terrain, atLeast);
        });

    /// <summary>Reads at most one of <c>building</c>, <c>tag</c> or <c>category</c> from an object; none means every building.</summary>
    private BuildingSelector? ReadSelector(ObjectView o, out bool given)
    {
        bool hasBuilding = o.TryGet("building", out JsonElement building);
        bool hasTag = o.TryGet("tag", out JsonElement tag);
        bool hasCategory = o.TryGet("category", out JsonElement category);
        given = hasBuilding || hasTag || hasCategory;

        if ((hasBuilding ? 1 : 0) + (hasTag ? 1 : 0) + (hasCategory ? 1 : 0) > 1)
        {
            Error(o.Pointer, "use only one of 'building', 'tag' or 'category'");
            return null;
        }

        if (hasBuilding)
        {
            return TryRef(building, o.Child("building"), Tables.Buildings, out BuildingTypeId id) ? new BuildingTypeSelector(id) : null;
        }

        if (hasTag)
        {
            return TryTag(tag, o.Child("tag"), out string name) ? new BuildingTagSelector(name) : null;
        }

        if (hasCategory)
        {
            return TryEnum(category, o.Child("category"), out BuildingCategory c) ? new BuildingCategorySelector(c) : null;
        }

        return null;
    }

    private Condition? ReadGoodQuantity(JsonElement value, string pointer, Func<GoodId, Amount, Condition> create) =>
        WithObject<Condition>(value, pointer, o =>
        {
            GoodId good = o.ReqRef(Tables.Goods, "good");
            Amount atLeast = o.ReqAmount("atLeast", QuantityRange.Positive);
            return create(good, atLeast);
        });

    private Condition? ReadDiscovered(JsonElement value, string pointer) =>
        WithObject<Condition>(value, pointer, o =>
        {
            DiscoveryKind? kind = o.OptEnum<DiscoveryKind>("kind");
            TerrainId? region = o.OptRef(Tables.Terrains, "region");
            if (o.TryGet("kind", out _) == o.TryGet("region", out _))
            {
                Error(pointer, "exactly one of 'kind' or 'region' is required");
            }

            return new DiscoveredCondition(kind, region, o.ReqInt("atLeast", 1, MaxCount));
        });

    private ImmutableArray<Condition>? ReadConditionList(JsonElement array, string pointer)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            Error(pointer, $"expected an array of conditions but found {Describe(array)}");
            return null;
        }

        int errorsBefore = ErrorCount;
        ImmutableArray<Condition>.Builder conditions = ImmutableArray.CreateBuilder<Condition>();
        int index = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            Condition? condition = ReadCondition(item, JsonPointer.Append(pointer, index));
            if (condition is not null)
            {
                conditions.Add(condition);
            }

            index++;
        }

        if (index == 0)
        {
            Error(pointer, "a list of conditions must not be empty");
        }

        return ErrorCount == errorsBefore ? conditions.ToImmutable() : null;
    }
}
