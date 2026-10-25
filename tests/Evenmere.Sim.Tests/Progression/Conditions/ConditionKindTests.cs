using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Progression;
using Evenmere.Sim.Tests.Progression;

namespace Evenmere.Sim.Tests.Progression.Conditions;

/// <summary>
/// Truth tables for the condition kinds M2-09 implements: per-type road counts, buildings on a terrain, expeditions,
/// festivals held, great works, visitors per day and discoveries. Each kind is tested below its threshold, at it and
/// above it, plus the memoisation and dependency-index behaviour the change-driven evaluation rests on.
/// </summary>
public sealed class ConditionKindTests
{
    // ---- roadTiles with a road type -------------------------------------------------------------------------------

    [Theory]
    [InlineData(39, false)]
    [InlineData(40, true)]
    [InlineData(41, true)]
    public void RoadTiles_OfAType_HoldsAtTheThreshold(long laid, bool expected)
    {
        var harness = ProgressionHarness.Create();
        harness.Fake.Queries.Roads.SetRoadTiles(harness.Building("dirt_road"), laid);

        harness.Queries.Evaluate(new RoadTilesCondition(harness.Building("dirt_road"), 40)).ShouldBe(expected);
    }

    [Fact]
    public void RoadTiles_OfAnUnlaidType_ReadsZero()
    {
        var harness = ProgressionHarness.Create();
        harness.Fake.Queries.Roads.SetRoadTiles(harness.Building("dirt_road"), 40);

        // The bakery is no road at all, so its count reads zero whatever the total is.
        harness.Queries.Evaluate(new RoadTilesCondition(harness.Building("bakery"), 0)).ShouldBeTrue();
        harness.Queries.Evaluate(new RoadTilesCondition(harness.Building("bakery"), 1)).ShouldBeFalse();
    }

    // ---- count on a terrain -----------------------------------------------------------------------------------------

    [Fact]
    public void Count_ByCategoryOnATerrain_CountsOnlyThatCategoryThere()
    {
        var harness = ProgressionHarness.Create();
        TerrainId hills = harness.Content.IdOf<TerrainId>("hills");
        harness.Fake.Queries.Map.AddBuilding(new EntityId(10), harness.Building("scribes_hall"), 0, 0);
        harness.Fake.Queries.Map.AddBuilding(new EntityId(11), harness.Building("bakery"), 4, 0);
        harness.Fake.Queries.Map.SetTile(0, 0, new TileInfo(hills, 0, SlopeClass.Flat, TileFeatures.None, RiverWidthClass.None, 0, null, 0));

        var condition = new CountCondition(new BuildingCategorySelector(BuildingCategory.Knowledge), hills, 1);
        harness.Queries.Evaluate(condition).ShouldBeTrue("the scribe's hall stands on hills");
        harness.Queries.Evaluate(new CountCondition(new BuildingCategorySelector(BuildingCategory.Production), hills, 1)).ShouldBeFalse();
    }

    [Fact]
    public void Count_OnTheWrongTerrain_DoesNotHold()
    {
        var harness = ProgressionHarness.Create();
        TerrainId hills = harness.Content.IdOf<TerrainId>("hills");
        TerrainId marsh = harness.Content.IdOf<TerrainId>("marsh");
        harness.Fake.Queries.Map.AddBuilding(new EntityId(10), harness.Building("grain_farm"), 0, 0);
        harness.Fake.Queries.Map.SetTile(0, 0, new TileInfo(marsh, 0, SlopeClass.Flat, TileFeatures.None, RiverWidthClass.None, 0, null, 0));

        harness.Queries.Evaluate(new CountCondition(new BuildingTagSelector("farm"), hills, 1)).ShouldBeFalse();
        harness.Queries.Evaluate(new CountCondition(new BuildingTagSelector("farm"), marsh, 1)).ShouldBeTrue();
    }

    [Fact]
    public void Count_WithNoSelectorAndATerrain_CountsEveryCompletedBuildingThere()
    {
        var harness = ProgressionHarness.Create();
        TerrainId hills = harness.Content.IdOf<TerrainId>("hills");
        harness.Fake.Queries.Map.AddBuilding(new EntityId(10), harness.Building("bakery"), 0, 0);
        harness.Fake.Queries.Map.AddBuilding(new EntityId(11), harness.Building("windmill"), 4, 0, state: BuildingState.Site);
        harness.Fake.Queries.Map.SetTile(0, 0, new TileInfo(hills, 0, SlopeClass.Flat, TileFeatures.None, RiverWidthClass.None, 0, null, 0));
        harness.Fake.Queries.Map.SetTile(4, 0, new TileInfo(hills, 0, SlopeClass.Flat, TileFeatures.None, RiverWidthClass.None, 0, null, 0));

        // The windmill is still a site, so only the bakery counts.
        harness.Queries.Evaluate(new CountCondition(null, hills, 1)).ShouldBeTrue();
        harness.Queries.Evaluate(new CountCondition(null, hills, 2)).ShouldBeFalse();
    }

    // ---- expeditions, festivals held ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public void Expeditions_CumulativeCounter_HoldsAtTheThreshold(int completed, bool expected)
    {
        var harness = ProgressionHarness.Create();
        harness.Context.Counters.Add(CounterKey.Expeditions, completed);

        harness.Queries.Evaluate(new ExpeditionsCondition(4)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void FestivalsHeld_CumulativeCounter_HoldsAtTheThreshold(int held, bool expected)
    {
        var harness = ProgressionHarness.Create();
        harness.Context.Counters.Add(CounterKey.FestivalsHeld, held);

        harness.Queries.Evaluate(new FestivalsHeldCondition(2)).ShouldBe(expected);
    }

    // ---- great works ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void GreatWorks_OfAType_HoldsAtTheThreshold(int created, bool expected)
    {
        var harness = ProgressionHarness.Create();
        GreatWorkTypeId relic = harness.Content.IdOf<GreatWorkTypeId>("relic");
        harness.Context.Counters.Add(CounterKey.GreatWorksCreated(relic), created);

        harness.Queries.Evaluate(new GreatWorksCondition(relic, false, 2)).ShouldBe(expected);
    }

    [Fact]
    public void GreatWorks_CreatedAndDisplayed_AreSeparateCounts()
    {
        var harness = ProgressionHarness.Create();
        GreatWorkTypeId relic = harness.Content.IdOf<GreatWorkTypeId>("relic");
        harness.Context.Counters.Add(CounterKey.GreatWorksCreated(relic), 2);
        harness.Context.Counters.Add(CounterKey.GreatWorksDisplayed(relic), 1);

        harness.Queries.Evaluate(new GreatWorksCondition(relic, false, 2)).ShouldBeTrue();
        harness.Queries.Evaluate(new GreatWorksCondition(relic, true, 2)).ShouldBeFalse();
        harness.Queries.Evaluate(new GreatWorksCondition(relic, true, 1)).ShouldBeTrue();
    }

    [Fact]
    public void GreatWorks_WithoutAType_SumsEveryType()
    {
        var harness = ProgressionHarness.Create();
        GreatWorkTypeId relic = harness.Content.IdOf<GreatWorkTypeId>("relic");
        GreatWorkTypeId writing = harness.Content.IdOf<GreatWorkTypeId>("writing");
        harness.Context.Counters.Add(CounterKey.GreatWorksCreated(relic), 1);
        harness.Context.Counters.Add(CounterKey.GreatWorksCreated(writing), 1);

        harness.Queries.Evaluate(new GreatWorksCondition(null, false, 2)).ShouldBeTrue();
        harness.Queries.Evaluate(new GreatWorksCondition(null, false, 3)).ShouldBeFalse();
        harness.Queries.Evaluate(new GreatWorksCondition(relic, false, 2)).ShouldBeFalse("one relic is not two");
    }

    // ---- visitors per day ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(199, false)]
    [InlineData(200, true)]
    [InlineData(201, true)]
    public void VisitorsPerDay_Gauge_HoldsAtTheThreshold(long visitors, bool expected)
    {
        var harness = ProgressionHarness.Create();
        harness.State.VisitorsPerDay = visitors;

        harness.Queries.Evaluate(new VisitorsPerDayCondition(200)).ShouldBe(expected);
    }

    // ---- discovered -----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void Discovered_OfAKind_HoldsAtTheThreshold(int found, bool expected)
    {
        var harness = ProgressionHarness.Create();
        harness.Context.Counters.Add(CounterKey.Discovered(DiscoveryKind.Ruin), found);

        harness.Queries.Evaluate(new DiscoveredCondition(DiscoveryKind.Ruin, null, 1)).ShouldBe(expected);
    }

    [Fact]
    public void Discovered_OfAKind_IgnoresOtherKinds()
    {
        var harness = ProgressionHarness.Create();
        harness.Context.Counters.Add(CounterKey.Discovered(DiscoveryKind.Shipwreck), 5);

        harness.Queries.Evaluate(new DiscoveredCondition(DiscoveryKind.Ruin, null, 1)).ShouldBeFalse();
        harness.Queries.Evaluate(new DiscoveredCondition(DiscoveryKind.Shipwreck, null, 5)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void Discovered_OfARegion_HoldsAtTheThreshold(int revealed, bool expected)
    {
        var harness = ProgressionHarness.Create();
        TerrainId hills = harness.Content.IdOf<TerrainId>("hills");
        harness.Context.Counters.Add(CounterKey.RegionsRevealed(hills), revealed);

        harness.Queries.Evaluate(new DiscoveredCondition(null, hills, 3)).ShouldBe(expected);
    }

    [Fact]
    public void Discovered_OfARegion_IgnoresOtherTerrains()
    {
        var harness = ProgressionHarness.Create();
        TerrainId hills = harness.Content.IdOf<TerrainId>("hills");
        TerrainId marsh = harness.Content.IdOf<TerrainId>("marsh");
        harness.Context.Counters.Add(CounterKey.RegionsRevealed(marsh), 5);

        harness.Queries.Evaluate(new DiscoveredCondition(null, hills, 1)).ShouldBeFalse();
        harness.Queries.Evaluate(new DiscoveredCondition(null, marsh, 5)).ShouldBeTrue();
    }

    // ---- change-driven evaluation -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Evaluate_PureCounterConditions_AreMemoisedUntilACounterChanges()
    {
        var harness = ProgressionHarness.Create();
        GoodId grain = harness.Good("grain");
        var condition = new ProducedCondition(grain, Amount.FromUnits(30));

        harness.Queries.Evaluate(condition).ShouldBeFalse();
        harness.Queries.Evaluator.CachedAnswerCount.ShouldBe(1);

        // A repeat evaluation hits the cache: no new entry, same answer.
        harness.Queries.Evaluate(condition).ShouldBeFalse();
        harness.Queries.Evaluator.CachedAnswerCount.ShouldBe(1);

        // Adding to any counter dirties the cache, so the next evaluation recomputes.
        harness.Context.Counters.Add(CounterKey.Produced(grain), Amount.FromUnits(30).Milli);
        harness.Queries.Evaluate(condition).ShouldBeTrue();
        harness.Queries.Evaluator.CachedAnswerCount.ShouldBe(1, "the recomputed answer overwrites the stale entry");
    }

    [Fact]
    public void Evaluate_SeasonConditions_RecomputeWhenTheSeasonChanges()
    {
        var harness = ProgressionHarness.Create();
        var condition = new SeasonCondition(Season.Summer);

        harness.Queries.Evaluate(condition).ShouldBeFalse();
        harness.Queries.Evaluate(condition).ShouldBeFalse();
        harness.Queries.Evaluator.CachedAnswerCount.ShouldBe(1);

        harness.SetSeason(Season.Summer);

        harness.Queries.Evaluate(condition).ShouldBeTrue("the cached spring answer must not survive the season change");
    }

    [Fact]
    public void Evaluate_EraConditions_RecomputeWhenTheEraChanges()
    {
        var harness = ProgressionHarness.Create();
        harness.ApplyStartUnlocks();
        EraId township = harness.Content.IdOf<EraId>("township");
        var condition = new EraCondition(township);

        harness.Queries.Evaluate(condition).ShouldBeFalse();
        harness.State.Era = township.Value;

        harness.Queries.Evaluate(condition).ShouldBeTrue("the cached earlier-era answer must not survive the era change");
    }

    [Fact]
    public void Evaluate_MixedTrees_CacheOnlyTheirPureParts()
    {
        var harness = ProgressionHarness.Create();
        GoodId grain = harness.Good("grain");
        BuildingTypeId bakery = harness.Building("bakery");
        var produced = new ProducedCondition(grain, Amount.FromUnits(1));
        var count = new CountCondition(new BuildingTypeSelector(bakery), null, 1);
        var all = new AllCondition([produced, count]);

        harness.Queries.Evaluate(all).ShouldBeFalse("nothing produced, nothing built");
        harness.Context.Counters.Add(CounterKey.Produced(grain), Amount.FromUnits(1).Milli);

        // The count half still misses, so the whole tree stays false; building the bakery then flips it even though
        // the produced half is cached, which proves the count half was evaluated fresh.
        harness.Queries.Evaluate(all).ShouldBeFalse();
        harness.Fake.Queries.Map.AddBuilding(new EntityId(10), bakery, 0, 0);
        harness.Queries.Evaluate(all).ShouldBeTrue();
    }

    [Fact]
    public void Evaluate_VolatileKinds_AreNeverCached()
    {
        var harness = ProgressionHarness.Create();
        GoodId timber = harness.Good("timber");
        var condition = new StockCondition(timber, Amount.FromUnits(40));

        harness.Queries.Evaluate(condition).ShouldBeFalse();
        harness.Fake.Queries.Storage.Set(timber, Amount.FromUnits(40));

        // Stock reads the live pool, so no cache may hide the new stock: the entry count stays zero.
        harness.Queries.Evaluate(condition).ShouldBeTrue();
        harness.Queries.Evaluator.CachedAnswerCount.ShouldBe(0);
    }

    // ---- dependency index -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Dependencies_CounterKinds_ListTheirCounters()
    {
        var harness = ProgressionHarness.Create();
        GoodId grain = harness.Good("grain");
        GreatWorkTypeId relic = harness.Content.IdOf<GreatWorkTypeId>("relic");
        TerrainId hills = harness.Content.IdOf<TerrainId>("hills");
        ConditionEvaluator evaluator = harness.Queries.Evaluator;

        evaluator.Dependencies(new ProducedCondition(grain, Amount.FromUnits(1))).ShouldBe([CounterKey.Produced(grain)]);
        evaluator.Dependencies(new ConsumedCondition(grain, Amount.FromUnits(1))).ShouldBe([CounterKey.Consumed(grain)]);
        evaluator.Dependencies(new RoadTilesCondition(null, 1)).ShouldBe([CounterKey.RoadTilesLaid]);
        evaluator.Dependencies(new ExpeditionsCondition(1)).ShouldBe([CounterKey.Expeditions]);
        evaluator.Dependencies(new FestivalsHeldCondition(1)).ShouldBe([CounterKey.FestivalsHeld]);
        evaluator.Dependencies(new GreatWorksCondition(relic, false, 1)).ShouldBe([CounterKey.GreatWorksCreated(relic)]);
        evaluator.Dependencies(new GreatWorksCondition(relic, true, 1)).ShouldBe([CounterKey.GreatWorksDisplayed(relic)]);
        evaluator.Dependencies(new DiscoveredCondition(DiscoveryKind.Ruin, null, 1)).ShouldBe([CounterKey.Discovered(DiscoveryKind.Ruin)]);
        evaluator.Dependencies(new DiscoveredCondition(null, hills, 1)).ShouldBe([CounterKey.RegionsRevealed(hills)]);
    }

    [Fact]
    public void Dependencies_NonCounterKinds_MapToTheEmptySet()
    {
        var harness = ProgressionHarness.Create();
        ConditionEvaluator evaluator = harness.Queries.Evaluator;

        evaluator.Dependencies(new CountCondition(null, null, 1)).ShouldBeEmpty();
        evaluator.Dependencies(new StockCondition(harness.Good("timber"), Amount.FromUnits(1))).ShouldBeEmpty();
        evaluator.Dependencies(new SeasonCondition(Season.Spring)).ShouldBeEmpty();
        evaluator.Dependencies(new EraCondition(harness.Content.IdOf<EraId>("founding"))).ShouldBeEmpty();
        evaluator.Dependencies(new RoadTilesCondition(harness.Building("dirt_road"), 1)).ShouldBeEmpty("typed counts are live state, not counters");
        evaluator.Dependencies(new VisitorsPerDayCondition(1)).ShouldBeEmpty("the visitors gauge is written outside the counters");
    }

    [Fact]
    public void Dependencies_Combinators_UnionTheirChildrenInOrderWithoutDuplicates()
    {
        var harness = ProgressionHarness.Create();
        GoodId grain = harness.Good("grain");
        ConditionEvaluator evaluator = harness.Queries.Evaluator;
        var condition = new AllCondition([
            new ProducedCondition(grain, Amount.FromUnits(1)),
            new AnyCondition([
                new ProducedCondition(grain, Amount.FromUnits(2)),
                new ExpeditionsCondition(1),
            ]),
            new NotCondition(new SeasonCondition(Season.Winter)),
        ]);

        evaluator.Dependencies(condition).ShouldBe([CounterKey.Produced(grain), CounterKey.Expeditions]);
    }

    [Fact]
    public void Dependencies_UntypedGreatWorks_ListEveryType()
    {
        var harness = ProgressionHarness.Create();
        ConditionEvaluator evaluator = harness.Queries.Evaluator;

        CounterKey[] created = evaluator.Dependencies(new GreatWorksCondition(null, false, 1));
        CounterKey[] displayed = evaluator.Dependencies(new GreatWorksCondition(null, true, 1));

        created.Length.ShouldBe(harness.Content.GreatWorks.Count);
        displayed.Length.ShouldBe(harness.Content.GreatWorks.Count);
        created.ShouldAllBe(static key => key.Kind == CounterKind.GreatWorksCreated);
        displayed.ShouldAllBe(static key => key.Kind == CounterKind.GreatWorksDisplayed);
    }
}
