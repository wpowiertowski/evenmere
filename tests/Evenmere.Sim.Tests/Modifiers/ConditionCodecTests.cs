using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.Systems.Modifiers;
using Evenmere.Sim.Tests.Progression;

namespace Evenmere.Sim.Tests.Modifiers;

public sealed class ConditionCodecTests
{
    private static ContentDb Content => ProgressionPack.Content;

    public static TheoryData<Condition> EveryKind() => new()
    {
        new CountCondition(new BuildingTypeSelector(Content.IdOf<BuildingTypeId>("bakery")), null, 2),
        new CountCondition(new BuildingTagSelector("farm"), Content.IdOf<TerrainId>("hills"), 3),
        new CountCondition(new BuildingCategorySelector(BuildingCategory.Knowledge), null, 1),
        new CountCondition(null, Content.IdOf<TerrainId>("hills"), 10),
        new ProducedCondition(Content.IdOf<GoodId>("grain"), Amount.FromUnits(30)),
        new ConsumedCondition(Content.IdOf<GoodId>("bread"), Amount.Parse("12.5")),
        new StockCondition(Content.IdOf<GoodId>("timber"), Amount.FromMilli(long.MaxValue)),
        new PopulationCondition(Content.IdOf<TierId>("villagers"), 50),
        new PopulationCondition(null, 1500),
        new RoadTilesCondition(null, 40),
        new RoadTilesCondition(Content.IdOf<BuildingTypeId>("dirt_road"), 60),
        new ExpeditionsCondition(4),
        new FestivalsHeldCondition(2),
        new GreatWorksCondition(Content.IdOf<GreatWorkTypeId>("writing"), true, 3),
        new GreatWorksCondition(null, false, 1),
        new VisitorsPerDayCondition(200),
        new ResearchedCondition(Content.IdOf<TechId>("net_weaving")),
        new AdoptedCondition(Content.IdOf<CivicId>("village_moot")),
        new DiscoveredCondition(DiscoveryKind.IronDeposit, null, 1),
        new DiscoveredCondition(null, Content.IdOf<TerrainId>("marsh"), 3),
        new SeasonCondition(Season.Winter),
        new EraCondition(Content.IdOf<EraId>("township")),
        new AllCondition([new SeasonCondition(Season.Autumn), new NotCondition(new ExpeditionsCondition(1))]),
        new AnyCondition([new SeasonCondition(Season.Spring), new AllCondition([new SeasonCondition(Season.Summer)])]),
        new NotCondition(new NotCondition(new SeasonCondition(Season.Spring))),
        new CountCondition(new BuildingTagSelector(string.Empty), null, 0),
        new CountCondition(new BuildingTagSelector("tag with spaces and ünïcode"), null, -5),
    };

    public static TheoryData<string, long[]> Malformed() => new()
    {
        { "empty", [] },
        { "unknown operation", [99] },
        { "zero operation", [0] },
        { "truncated produced", [2, 0] },
        { "good outside the content", [2, 9999, 1000] },
        { "negative good", [4, -1, 1000] },
        { "tier outside the content", [5, 9999, 1] },
        { "tier id equal to the count", [5, Content.Tiers.Count + 1L, 1] },
        { "season outside the enum", [14, 9] },
        { "negative season", [14, -1] },
        { "leftover values", [14, 0, 14, 0] },
        { "count with unknown selector", [1, 9, 0, 1] },
        { "count with category outside the enum", [1, 3, 99, 0, 1] },
        { "count with tag longer than the data", [1, 2, 50, 65, 0, 1] },
        { "count with tag holding a non-character", [1, 2, 1, -3, 0, 1] },
        { "count with building outside the content", [1, 1, 9999, 0, 1] },
        { "count with terrain outside the content", [1, 0, 9999, 1] },
        { "count with threshold that is not an int", [1, 0, 0, long.MaxValue] },
        { "all with no conditions", [16, 0] },
        { "all with negative length", [16, -2, 14, 0] },
        { "any longer than the data", [17, 5, 14, 0] },
        { "not with nothing inside", [18] },
        { "greatWorks with a flag of 2", [9, 0, 2, 1] },
        { "discovered with unknown kind", [13, 99, 0, 1] },
        { "era outside the content", [15, 9999] },
    };

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void Decode_OfEncode_GivesTheSameCondition(Condition condition)
    {
        long[] encoded = ConditionCodec.Encode(condition);

        Condition decoded = ConditionCodec.Decode(encoded, Content);

        Same(decoded, condition).ShouldBeTrue($"decoded {decoded} is not {condition}");
        ConditionCodec.Encode(decoded).ShouldBe(encoded);
    }

    [Fact]
    public void Decode_ConditionsFromTheRealContent_RoundTrip()
    {
        // Every condition the slice content writes: inspirations of techs and civics.
        var conditions = new List<Condition>();
        conditions.AddRange(ProgressionPack.Real.Techs.Items.Select(static t => t.Inspiration).OfType<Condition>());
        conditions.AddRange(ProgressionPack.Real.Civics.Items.Select(static c => c.Inspiration).OfType<Condition>());
        conditions.ShouldNotBeEmpty();

        foreach (Condition condition in conditions)
        {
            Same(ConditionCodec.Decode(ConditionCodec.Encode(condition), ProgressionPack.Real), condition).ShouldBeTrue(condition.ToString());
        }
    }

    [Fact]
    public void Encode_Season_IsAnOperationAndTheSeasonNumber()
    {
        // The flat form is part of the save format, so its shape is pinned.
        ConditionCodec.Encode(new SeasonCondition(Season.Autumn)).ShouldBe([14, 2]);
        ConditionCodec.Encode(new NotCondition(new SeasonCondition(Season.Winter))).ShouldBe([18, 14, 3]);
        ConditionCodec.Encode(new AllCondition([new SeasonCondition(Season.Spring), new SeasonCondition(Season.Summer)])).ShouldBe([16, 2, 14, 0, 14, 1]);
        ConditionCodec.Encode(new ProducedCondition(new GoodId(3), Amount.FromUnits(2))).ShouldBe([2, 3, 2000]);
        ConditionCodec.Encode(new PopulationCondition(null, 5)).ShouldBe([5, 0, 5]);
        ConditionCodec.Encode(new PopulationCondition(new TierId(0), 5)).ShouldBe([5, 1, 5]);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Decode_MalformedData_ThrowsInvalidData(string what, long[] data)
    {
        Should.Throw<InvalidDataException>(() => ConditionCodec.Decode(data, Content), what);
    }

    [Fact]
    public void Decode_NestingBeyondTheLimit_Throws()
    {
        // 'not' written 40 times around a season: deeper than any real condition, and a stack bound for a damaged save.
        long[] data = [.. Enumerable.Repeat(18L, 40), 14, 0];

        Should.Throw<InvalidDataException>(() => ConditionCodec.Decode(data, Content));
    }

    [Fact]
    public void Decode_NestingWithinTheLimit_Works()
    {
        long[] data = [.. Enumerable.Repeat(18L, 20), 14, 0];

        Condition decoded = ConditionCodec.Decode(data, Content);

        decoded.ShouldBeOfType<NotCondition>();
    }

    [Fact]
    public void EncodeAndDecode_Null_Throw()
    {
        Should.Throw<ArgumentNullException>(() => ConditionCodec.Encode(null!));
        Should.Throw<ArgumentNullException>(() => ConditionCodec.Decode(null!, Content));
        Should.Throw<ArgumentNullException>(() => ConditionCodec.Decode([14, 0], null!));
    }

    // Records compare their ImmutableArray members by reference, so structural equality is written out here.
    private static bool Same(Condition left, Condition right) => (left, right) switch
    {
        (CountCondition a, CountCondition b) => a.Terrain == b.Terrain && a.AtLeast == b.AtLeast && SameSelector(a.Selector, b.Selector),
        (AllCondition a, AllCondition b) => SameList(a.Conditions, b.Conditions),
        (AnyCondition a, AnyCondition b) => SameList(a.Conditions, b.Conditions),
        (NotCondition a, NotCondition b) => Same(a.Condition, b.Condition),
        _ => left.GetType() == right.GetType() && left.Equals(right),
    };

    private static bool SameList(ImmutableArray<Condition> left, ImmutableArray<Condition> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (!Same(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameSelector(BuildingSelector? left, BuildingSelector? right) => (left, right) switch
    {
        (null, null) => true,
        (BuildingTagSelector a, BuildingTagSelector b) => a.Tag == b.Tag,
        (null, _) or (_, null) => false,
        _ => left.GetType() == right.GetType() && left.Equals(right),
    };
}
