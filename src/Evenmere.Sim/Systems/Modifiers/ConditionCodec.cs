using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;

namespace Evenmere.Sim.Systems.Modifiers;

/// <summary>
/// Turns a content <see cref="Condition"/> into a flat <see cref="long"/> array and back, so a modifier's <c>when</c> can be
/// saved without the content record types being serialisable. The form is prefix notation: an operation number, then its
/// operands, with sub-conditions following inline. The operation numbers are part of the save format; add new ones at the
/// end. Optional IDs are stored as the raw ID plus one, with 0 meaning "none". A building tag is stored as its length and then
/// one value per character.
/// </summary>
internal static class ConditionCodec
{
    // Deeper than any hand-written condition, and a bound on the recursion a damaged save can ask for.
    private const int MaxDepth = 32;

    private const long OpCount = 1;
    private const long OpProduced = 2;
    private const long OpConsumed = 3;
    private const long OpStock = 4;
    private const long OpPopulation = 5;
    private const long OpRoadTiles = 6;
    private const long OpExpeditions = 7;
    private const long OpFestivalsHeld = 8;
    private const long OpGreatWorks = 9;
    private const long OpVisitorsPerDay = 10;
    private const long OpResearched = 11;
    private const long OpAdopted = 12;
    private const long OpDiscovered = 13;
    private const long OpSeason = 14;
    private const long OpEra = 15;
    private const long OpAll = 16;
    private const long OpAny = 17;
    private const long OpNot = 18;

    private const long SelectorNone = 0;
    private const long SelectorBuilding = 1;
    private const long SelectorTag = 2;
    private const long SelectorCategory = 3;

    /// <summary>Encodes a condition.</summary>
    /// <param name="condition">The condition.</param>
    /// <returns>The flat form.</returns>
    public static long[] Encode(Condition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var output = new List<long>();
        Write(condition, output);
        return [.. output];
    }

    /// <summary>Decodes a condition, checking every ID against the content.</summary>
    /// <param name="data">The flat form.</param>
    /// <param name="content">The content the IDs must belong to.</param>
    /// <returns>The condition.</returns>
    /// <exception cref="InvalidDataException">The data is not a well-formed condition for this content.</exception>
    public static Condition Decode(long[] data, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(content);

        var reader = new Reader(data, content);
        Condition condition = reader.ReadCondition(0);
        reader.ExpectEnd();
        return condition;
    }

    private static void Write(Condition condition, List<long> o)
    {
        switch (condition)
        {
            case CountCondition c:
                o.Add(OpCount);
                WriteSelector(c.Selector, o);
                o.Add(c.Terrain is { } terrain ? terrain.Value + 1L : 0L);
                o.Add(c.AtLeast);
                break;
            case ProducedCondition c:
                o.AddRange([OpProduced, c.Good.Value, c.AtLeast.Milli]);
                break;
            case ConsumedCondition c:
                o.AddRange([OpConsumed, c.Good.Value, c.AtLeast.Milli]);
                break;
            case StockCondition c:
                o.AddRange([OpStock, c.Good.Value, c.AtLeast.Milli]);
                break;
            case PopulationCondition c:
                o.AddRange([OpPopulation, c.Tier is { } tier ? tier.Value + 1L : 0L, c.AtLeast]);
                break;
            case RoadTilesCondition c:
                o.AddRange([OpRoadTiles, c.Road is { } road ? road.Value + 1L : 0L, c.AtLeast]);
                break;
            case ExpeditionsCondition c:
                o.AddRange([OpExpeditions, c.AtLeast]);
                break;
            case FestivalsHeldCondition c:
                o.AddRange([OpFestivalsHeld, c.AtLeast]);
                break;
            case GreatWorksCondition c:
                o.AddRange([OpGreatWorks, c.Type is { } type ? type.Value + 1L : 0L, c.Displayed ? 1L : 0L, c.AtLeast]);
                break;
            case VisitorsPerDayCondition c:
                o.AddRange([OpVisitorsPerDay, c.AtLeast]);
                break;
            case ResearchedCondition c:
                o.AddRange([OpResearched, c.Tech.Value]);
                break;
            case AdoptedCondition c:
                o.AddRange([OpAdopted, c.Civic.Value]);
                break;
            case DiscoveredCondition c:
                o.AddRange([OpDiscovered, c.Kind is { } kind ? (int)kind + 1L : 0L, c.Region is { } region ? region.Value + 1L : 0L, c.AtLeast]);
                break;
            case SeasonCondition c:
                o.AddRange([OpSeason, (long)c.Season]);
                break;
            case EraCondition c:
                o.AddRange([OpEra, c.Era.Value]);
                break;
            case AllCondition c:
                WriteList(OpAll, c.Conditions, o);
                break;
            case AnyCondition c:
                WriteList(OpAny, c.Conditions, o);
                break;
            case NotCondition c:
                o.Add(OpNot);
                Write(c.Condition, o);
                break;
            default:
                throw new ArgumentException($"{condition.GetType().Name} is not a known condition kind.", nameof(condition));
        }
    }

    private static void WriteList(long op, ImmutableArray<Condition> conditions, List<long> o)
    {
        o.Add(op);
        o.Add(conditions.Length);
        foreach (Condition child in conditions)
        {
            Write(child, o);
        }
    }

    private static void WriteSelector(BuildingSelector? selector, List<long> o)
    {
        switch (selector)
        {
            case null:
                o.Add(SelectorNone);
                break;
            case BuildingTypeSelector s:
                o.AddRange([SelectorBuilding, s.Building.Value]);
                break;
            case BuildingTagSelector s:
                o.AddRange([SelectorTag, s.Tag.Length]);
                foreach (char c in s.Tag)
                {
                    o.Add(c);
                }

                break;
            case BuildingCategorySelector s:
                o.AddRange([SelectorCategory, (long)s.Category]);
                break;
            default:
                throw new ArgumentException($"{selector.GetType().Name} is not a known building selector.", nameof(selector));
        }
    }

    private sealed class Reader(long[] data, ContentDb content)
    {
        private int _position;

        public void ExpectEnd()
        {
            if (_position != data.Length)
            {
                throw Bad("it has values left over after the condition");
            }
        }

        public Condition ReadCondition(int depth)
        {
            if (depth > MaxDepth)
            {
                throw Bad($"it nests deeper than {MaxDepth} levels");
            }

            long op = Next();
            switch (op)
            {
                case OpCount:
                    BuildingSelector? selector = ReadSelector();
                    TerrainId? terrain = OptionalId<TerrainId>(content.Terrains.Count, "terrain");
                    return new CountCondition(selector, terrain, NextInt());
                case OpProduced:
                    return new ProducedCondition(Id<GoodId>(content.Goods.Count, "good"), Amount.FromMilli(Next()));
                case OpConsumed:
                    return new ConsumedCondition(Id<GoodId>(content.Goods.Count, "good"), Amount.FromMilli(Next()));
                case OpStock:
                    return new StockCondition(Id<GoodId>(content.Goods.Count, "good"), Amount.FromMilli(Next()));
                case OpPopulation:
                    TierId? populationTier = OptionalId<TierId>(content.Tiers.Count, "tier");
                    return new PopulationCondition(populationTier, NextInt());
                case OpRoadTiles:
                    BuildingTypeId? road = OptionalId<BuildingTypeId>(content.Buildings.Count, "road type");
                    return new RoadTilesCondition(road, NextInt());
                case OpExpeditions:
                    return new ExpeditionsCondition(NextInt());
                case OpFestivalsHeld:
                    return new FestivalsHeldCondition(NextInt());
                case OpGreatWorks:
                    GreatWorkTypeId? workType = OptionalId<GreatWorkTypeId>(content.GreatWorks.Count, "great-work type");
                    bool displayed = NextFlag();
                    return new GreatWorksCondition(workType, displayed, NextInt());
                case OpVisitorsPerDay:
                    return new VisitorsPerDayCondition(NextInt());
                case OpResearched:
                    return new ResearchedCondition(Id<TechId>(content.Techs.Count, "tech"));
                case OpAdopted:
                    return new AdoptedCondition(Id<CivicId>(content.Civics.Count, "civic"));
                case OpDiscovered:
                    DiscoveryKind? kind = OptionalEnum<DiscoveryKind>("discovery kind");
                    TerrainId? region = OptionalId<TerrainId>(content.Terrains.Count, "terrain");
                    return new DiscoveredCondition(kind, region, NextInt());
                case OpSeason:
                    return new SeasonCondition(NextEnum<Season>("season"));
                case OpEra:
                    return new EraCondition(Id<EraId>(content.Eras.Count, "era"));
                case OpAll:
                    return new AllCondition(ReadList(depth));
                case OpAny:
                    return new AnyCondition(ReadList(depth));
                case OpNot:
                    return new NotCondition(ReadCondition(depth + 1));
                default:
                    throw Bad($"{op} is not a condition operation");
            }
        }

        private static InvalidDataException Bad(string why) => new($"A saved modifier condition is malformed: {why}.");

        private ImmutableArray<Condition> ReadList(int depth)
        {
            long count = Next();
            if (count < 1 || count > data.Length - _position)
            {
                throw Bad($"a combinator lists {count} conditions");
            }

            ImmutableArray<Condition>.Builder children = ImmutableArray.CreateBuilder<Condition>((int)count);
            for (long i = 0; i < count; i++)
            {
                children.Add(ReadCondition(depth + 1));
            }

            return children.MoveToImmutable();
        }

        private BuildingSelector? ReadSelector()
        {
            long kind = Next();
            switch (kind)
            {
                case SelectorNone:
                    return null;
                case SelectorBuilding:
                    return new BuildingTypeSelector(Id<BuildingTypeId>(content.Buildings.Count, "building"));
                case SelectorTag:
                    long length = Next();
                    if (length < 0 || length > data.Length - _position)
                    {
                        throw Bad($"a tag is {length} characters long");
                    }

                    char[] characters = new char[length];
                    for (int i = 0; i < characters.Length; i++)
                    {
                        long character = Next();
                        if (character is < 0 or > char.MaxValue)
                        {
                            throw Bad("a tag holds a value that is not a character");
                        }

                        characters[i] = (char)character;
                    }

                    return new BuildingTagSelector(new string(characters));
                case SelectorCategory:
                    return new BuildingCategorySelector(NextEnum<BuildingCategory>("building category"));
                default:
                    throw Bad($"{kind} is not a building selector");
            }
        }

        private long Next()
        {
            if (_position >= data.Length)
            {
                throw Bad("it ends in the middle of a condition");
            }

            return data[_position++];
        }

        private int NextInt()
        {
            long value = Next();
            return value is >= int.MinValue and <= int.MaxValue ? (int)value : throw Bad($"{value} does not fit a count");
        }

        private bool NextFlag()
        {
            long value = Next();
            return value switch
            {
                0 => false,
                1 => true,
                _ => throw Bad($"{value} is not a flag"),
            };
        }

        private TEnum NextEnum<TEnum>(string what)
            where TEnum : struct, Enum
        {
            long value = Next();
            return TryEnum(value, out TEnum result) ? result : throw Bad($"{value} is not a {what}");
        }

        private TEnum? OptionalEnum<TEnum>(string what)
            where TEnum : struct, Enum
        {
            long value = Next();
            if (value == 0)
            {
                return null;
            }

            return TryEnum(value - 1, out TEnum result) ? result : throw Bad($"{value - 1} is not a {what}");
        }

        private static bool TryEnum<TEnum>(long value, out TEnum result)
            where TEnum : struct, Enum
        {
            result = default;
            if (value is < int.MinValue or > int.MaxValue)
            {
                return false;
            }

            result = (TEnum)Enum.ToObject(typeof(TEnum), (int)value);
            return Enum.IsDefined(result);
        }

        private TId Id<TId>(int count, string what)
            where TId : struct, IContentId<TId>
        {
            long raw = Next();
            return raw >= 0 && raw < count ? TId.From((ushort)raw) : throw Bad($"{what} {raw} is not in the content");
        }

        private TId? OptionalId<TId>(int count, string what)
            where TId : struct, IContentId<TId>
        {
            long raw = Next();
            if (raw == 0)
            {
                return null;
            }

            return raw >= 1 && raw <= count ? TId.From((ushort)(raw - 1)) : throw Bad($"{what} {raw - 1} is not in the content");
        }
    }
}
