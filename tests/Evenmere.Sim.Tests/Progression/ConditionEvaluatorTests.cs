using System.Collections.Immutable;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Modifiers;
using Evenmere.Sim.Systems.Progression;

namespace Evenmere.Sim.Tests.Progression;

public sealed class ConditionEvaluatorTests
{
    // Atoms for the combinator tables: the harness clock starts in spring.
    private static readonly Condition _true = new SeasonCondition(Season.Spring);
    private static readonly Condition _false = new SeasonCondition(Season.Winter);

    // ---- count ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 3, false)]
    [InlineData(0, 0, true)]
    [InlineData(2, 3, false)]
    [InlineData(3, 3, true)]
    [InlineData(4, 3, true)]
    public void Count_ByBuildingType_HoldsAtTheThreshold(int built, int atLeast, bool expected)
    {
        var harness = ProgressionHarness.Create();
        for (int i = 0; i < built; i++)
        {
            harness.Fake.Queries.Map.AddBuilding(new EntityId(10 + i), harness.Building("bakery"), i * 3, 0);
        }

        var condition = new CountCondition(new BuildingTypeSelector(harness.Building("bakery")), null, atLeast);

        harness.Queries.Evaluate(condition).ShouldBe(expected);
    }

    [Fact]
    public void Count_ByBuildingType_IgnoresOtherTypes()
    {
        var harness = ProgressionHarness.Create();
        harness.Fake.Queries.Map.AddBuilding(new EntityId(10), harness.Building("windmill"), 0, 0);

        harness.Queries.Evaluate(new CountCondition(new BuildingTypeSelector(harness.Building("bakery")), null, 1)).ShouldBeFalse();
    }

    [Fact]
    public void Count_ByTag_CountsEveryTypeThatCarriesTheTag()
    {
        var harness = ProgressionHarness.Create();
        harness.Fake.Queries.Map.AddBuilding(new EntityId(10), harness.Building("bakery"), 0, 0);
        harness.Fake.Queries.Map.AddBuilding(new EntityId(11), harness.Building("windmill"), 4, 0);
        harness.Fake.Queries.Map.AddBuilding(new EntityId(12), harness.Building("grain_farm"), 8, 0);

        harness.Queries.Evaluate(new CountCondition(new BuildingTagSelector("processing"), null, 2)).ShouldBeTrue();
        harness.Queries.Evaluate(new CountCondition(new BuildingTagSelector("processing"), null, 3)).ShouldBeFalse();
        harness.Queries.Evaluate(new CountCondition(new BuildingTagSelector("farm"), null, 1)).ShouldBeTrue();
        harness.Queries.Evaluate(new CountCondition(new BuildingTagSelector("nonexistent"), null, 1)).ShouldBeFalse();
    }

    [Fact]
    public void Count_ByCategory_CountsEveryTypeInTheCategory()
    {
        var harness = ProgressionHarness.Create();
        harness.Fake.Queries.Map.AddBuilding(new EntityId(10), harness.Building("scribes_hall"), 0, 0);
        harness.Fake.Queries.Map.AddBuilding(new EntityId(11), harness.Building("bakery"), 4, 0);
        harness.Fake.Queries.Map.AddBuilding(new EntityId(12), harness.Building("windmill"), 8, 0);

        harness.Queries.Evaluate(new CountCondition(new BuildingCategorySelector(BuildingCategory.Knowledge), null, 1)).ShouldBeTrue();
        harness.Queries.Evaluate(new CountCondition(new BuildingCategorySelector(BuildingCategory.Knowledge), null, 2)).ShouldBeFalse();
        harness.Queries.Evaluate(new CountCondition(new BuildingCategorySelector(BuildingCategory.Production), null, 2)).ShouldBeTrue();
        harness.Queries.Evaluate(new CountCondition(new BuildingCategorySelector(BuildingCategory.Residence), null, 1)).ShouldBeFalse();
    }

    [Fact]
    public void Count_OnlyCountsCompletedBuildings()
    {
        var harness = ProgressionHarness.Create();
        BuildingTypeId bakery = harness.Building("bakery");
        harness.Fake.Queries.Map.AddBuilding(new EntityId(10), bakery, 0, 0, state: BuildingState.Site);
        harness.Fake.Queries.Map.AddBuilding(new EntityId(11), bakery, 4, 0, state: BuildingState.Ruin);
        var condition = new CountCondition(new BuildingTypeSelector(bakery), null, 1);
        harness.Queries.Evaluate(condition).ShouldBeFalse();

        harness.Fake.Queries.Map.AddBuilding(new EntityId(12), bakery, 8, 0, state: BuildingState.Paused);

        harness.Queries.Evaluate(condition).ShouldBeTrue("a paused building still stands");
    }

    [Fact]
    public void Count_WithATerrain_NeverHoldsUntilM209()
    {
        var harness = ProgressionHarness.Create();
        harness.Fake.Queries.Map.AddBuilding(new EntityId(10), harness.Building("grain_farm"), 0, 0);
        TerrainId hills = harness.Content.IdOf<TerrainId>("hills");

        harness.Queries.Evaluate(new CountCondition(new BuildingTagSelector("farm"), hills, 1)).ShouldBeFalse();
        harness.Queries.Evaluate(new CountCondition(null, hills, 0)).ShouldBeFalse();
    }

    [Fact]
    public void Count_WithNoSelectorAndNoTerrain_CountsEveryCompletedBuilding()
    {
        var harness = ProgressionHarness.Create();
        harness.Fake.Queries.Map.AddBuilding(new EntityId(10), harness.Building("bakery"), 0, 0);
        harness.Fake.Queries.Map.AddBuilding(new EntityId(11), harness.Building("windmill"), 4, 0);

        harness.Queries.Evaluate(new CountCondition(null, null, 2)).ShouldBeTrue();
        harness.Queries.Evaluate(new CountCondition(null, null, 3)).ShouldBeFalse();
    }

    // ---- produced, consumed, stock ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(29_999, false)]
    [InlineData(30_000, true)]
    [InlineData(30_001, true)]
    public void Produced_CumulativeCounter_HoldsAtTheThreshold(long milli, bool expected)
    {
        var harness = ProgressionHarness.Create();
        GoodId grain = harness.Good("grain");
        harness.Context.Counters.Add(CounterKey.Produced(grain), milli);

        harness.Queries.Evaluate(new ProducedCondition(grain, Amount.FromUnits(30))).ShouldBe(expected);
    }

    [Fact]
    public void Produced_OtherGoodsAndConsumption_DoNotCount()
    {
        var harness = ProgressionHarness.Create();
        harness.Context.Counters.Add(CounterKey.Produced(harness.Good("grain")), 99_000);
        harness.Context.Counters.Add(CounterKey.Consumed(harness.Good("bread")), 99_000);
        harness.Context.Counters.Add(CounterKey.Consumed(harness.Good("grain")), 99_000);

        harness.Queries.Evaluate(new ProducedCondition(harness.Good("bread"), Amount.FromUnits(1))).ShouldBeFalse();
    }

    [Theory]
    [InlineData(12_499, false)]
    [InlineData(12_500, true)]
    public void Consumed_CumulativeCounter_HoldsAtTheThreshold(long milli, bool expected)
    {
        var harness = ProgressionHarness.Create();
        GoodId bread = harness.Good("bread");
        harness.Context.Counters.Add(CounterKey.Consumed(bread), milli);

        harness.Queries.Evaluate(new ConsumedCondition(bread, Amount.Parse("12.5"))).ShouldBe(expected);
    }

    [Fact]
    public void Consumed_ProductionOfTheSameGood_DoesNotCount()
    {
        var harness = ProgressionHarness.Create();
        GoodId bread = harness.Good("bread");
        harness.Context.Counters.Add(CounterKey.Produced(bread), 50_000);

        harness.Queries.Evaluate(new ConsumedCondition(bread, Amount.FromUnits(1))).ShouldBeFalse();
    }

    [Theory]
    [InlineData(39_999, false)]
    [InlineData(40_000, true)]
    [InlineData(80_000, true)]
    public void Stock_PoolNow_HoldsAtTheThreshold(long milli, bool expected)
    {
        var harness = ProgressionHarness.Create();
        GoodId timber = harness.Good("timber");
        harness.Fake.Queries.Storage.Set(timber, Amount.FromMilli(milli));

        harness.Queries.Evaluate(new StockCondition(timber, Amount.FromUnits(40))).ShouldBe(expected);
    }

    [Fact]
    public void Stock_FollowsThePoolDown_NotTheCumulativeProduction()
    {
        var harness = ProgressionHarness.Create();
        GoodId timber = harness.Good("timber");
        harness.Context.Counters.Add(CounterKey.Produced(timber), 100_000);
        harness.Fake.Queries.Storage.Set(timber, Amount.FromUnits(50));
        var condition = new StockCondition(timber, Amount.FromUnits(40));
        harness.Queries.Evaluate(condition).ShouldBeTrue();

        harness.Context.Storage.TryTake(timber, Amount.FromUnits(20)).ShouldBeTrue();

        harness.Queries.Evaluate(condition).ShouldBeFalse();
    }

    // ---- population, roadTiles ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(51, true)]
    public void Population_OfATier_HoldsAtTheThreshold(int residents, bool expected)
    {
        var harness = ProgressionHarness.Create();
        TierId villagers = harness.Tier("villagers");
        harness.Fake.Queries.Population.SetResidents(villagers, residents);
        harness.Fake.Queries.Population.SetResidents(harness.Tier("settlers"), 1000);

        harness.Queries.Evaluate(new PopulationCondition(villagers, 50)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1499, false)]
    [InlineData(1500, true)]
    public void Population_Total_HoldsAtTheThreshold(int total, bool expected)
    {
        var harness = ProgressionHarness.Create();
        harness.Fake.Queries.Population.TotalResidents = total;

        harness.Queries.Evaluate(new PopulationCondition(null, 1500)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(39, false)]
    [InlineData(40, true)]
    [InlineData(41, true)]
    public void RoadTiles_AnyRoadType_HoldsAtTheThreshold(int laid, bool expected)
    {
        var harness = ProgressionHarness.Create();
        harness.Context.Counters.Add(CounterKey.RoadTilesLaid, laid);

        harness.Queries.Evaluate(new RoadTilesCondition(null, 40)).ShouldBe(expected);
    }

    [Fact]
    public void RoadTiles_WithARoadType_NeverHoldsUntilM209()
    {
        var harness = ProgressionHarness.Create();
        harness.Context.Counters.Add(CounterKey.RoadTilesLaid, 500);

        harness.Queries.Evaluate(new RoadTilesCondition(harness.Building("dirt_road"), 1)).ShouldBeFalse();
    }

    // ---- researched, adopted --------------------------------------------------------------------------------------

    [Fact]
    public void Researched_ReadsTheResearchQueries()
    {
        var harness = ProgressionHarness.Create();
        TechId netWeaving = harness.Content.IdOf<TechId>("net_weaving");
        TechId milling = harness.Content.IdOf<TechId>("milling");
        harness.Fake.Queries.Research.SetTech(netWeaving, Amount.FromUnits(50), done: true);
        harness.Fake.Queries.Research.SetTech(milling, Amount.FromUnits(60), progress: Amount.FromUnits(59));

        harness.Queries.Evaluate(new ResearchedCondition(netWeaving)).ShouldBeTrue();
        harness.Queries.Evaluate(new ResearchedCondition(milling)).ShouldBeFalse();
    }

    [Fact]
    public void Adopted_ReadsTheCultureQueries()
    {
        var harness = ProgressionHarness.Create();
        CivicId moot = harness.Content.IdOf<CivicId>("village_moot");
        harness.Queries.Evaluate(new AdoptedCondition(moot)).ShouldBeFalse();

        harness.Fake.Queries.Culture.SetCivic(moot, Amount.FromUnits(140), done: true);

        harness.Queries.Evaluate(new AdoptedCondition(moot)).ShouldBeTrue();
    }

    // ---- season, era --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Season.Spring, Season.Spring, true)]
    [InlineData(Season.Spring, Season.Summer, false)]
    [InlineData(Season.Summer, Season.Summer, true)]
    [InlineData(Season.Summer, Season.Autumn, false)]
    [InlineData(Season.Autumn, Season.Autumn, true)]
    [InlineData(Season.Autumn, Season.Winter, false)]
    [InlineData(Season.Winter, Season.Winter, true)]
    [InlineData(Season.Winter, Season.Spring, false)]
    public void Season_AgainstTheClock_HoldsInThatSeasonOnly(Season now, Season asked, bool expected)
    {
        var harness = ProgressionHarness.Create();
        harness.SetSeason(now);

        harness.Queries.Evaluate(new SeasonCondition(asked)).ShouldBe(expected);
    }

    [Fact]
    public void Season_LastTickOfOneSeasonAndFirstOfTheNext_Differ()
    {
        var harness = ProgressionHarness.Create();
        var condition = new SeasonCondition(Season.Summer);

        harness.Fake.SetTick(GameTime.TicksPerSeason - 1);
        harness.Queries.Evaluate(condition).ShouldBeFalse();
        harness.Fake.SetTick(GameTime.TicksPerSeason);
        harness.Queries.Evaluate(condition).ShouldBeTrue();
    }

    [Fact]
    public void Era_ComparesByOrderNotById()
    {
        var harness = ProgressionHarness.Create();
        EraId awakening = harness.Content.IdOf<EraId>("awakening");
        EraId founding = harness.Content.IdOf<EraId>("founding");
        EraId township = harness.Content.IdOf<EraId>("township");
        harness.ApplyStartUnlocks();

        harness.Queries.Evaluate(new EraCondition(founding)).ShouldBeTrue();
        harness.Queries.Evaluate(new EraCondition(township)).ShouldBeFalse();
        harness.Queries.Evaluate(new EraCondition(awakening)).ShouldBeFalse("awakening has ID 0 but is the third era");

        harness.State.Era = township.Value;

        harness.Queries.Evaluate(new EraCondition(founding)).ShouldBeTrue("an earlier era has begun");
        harness.Queries.Evaluate(new EraCondition(township)).ShouldBeTrue();
        harness.Queries.Evaluate(new EraCondition(awakening)).ShouldBeFalse();
    }

    // ---- all, any, not ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void All_TwoConditions_IsTheirConjunction(bool left, bool right, bool expected)
    {
        var harness = ProgressionHarness.Create();

        harness.Queries.Evaluate(new AllCondition([Atom(left), Atom(right)])).ShouldBe(expected);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void Any_TwoConditions_IsTheirDisjunction(bool left, bool right, bool expected)
    {
        var harness = ProgressionHarness.Create();

        harness.Queries.Evaluate(new AnyCondition([Atom(left), Atom(right)])).ShouldBe(expected);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Not_OneCondition_IsItsNegation(bool inner, bool expected)
    {
        var harness = ProgressionHarness.Create();

        harness.Queries.Evaluate(new NotCondition(Atom(inner))).ShouldBe(expected);
    }

    [Fact]
    public void Combinators_Nested_EvaluateRecursively()
    {
        var harness = ProgressionHarness.Create();
        var nested = new AllCondition([
            new AnyCondition([_false, new NotCondition(_false)]),
            new NotCondition(new AllCondition([_true, _false])),
        ]);

        harness.Queries.Evaluate(nested).ShouldBeTrue();
        harness.Queries.Evaluate(new NotCondition(nested)).ShouldBeFalse();
    }

    [Fact]
    public void Combinators_OverRealKinds_MixTheSources()
    {
        var harness = ProgressionHarness.Create();
        harness.Fake.Queries.Storage.Set(harness.Good("timber"), Amount.FromUnits(40));
        harness.Context.Counters.Add(CounterKey.Produced(harness.Good("grain")), 30_000);
        var all = new AllCondition([
            new StockCondition(harness.Good("timber"), Amount.FromUnits(40)),
            new ProducedCondition(harness.Good("grain"), Amount.FromUnits(30)),
            new SeasonCondition(Season.Spring),
        ]);

        harness.Queries.Evaluate(all).ShouldBeTrue();
        harness.SetSeason(Season.Winter);
        harness.Queries.Evaluate(all).ShouldBeFalse();
    }

    // ---- kinds M1 does not implement ---------------------------------------------------------------------------------

    public static TheoryData<Condition> UnimplementedKinds() => new()
    {
        new ExpeditionsCondition(0),
        new FestivalsHeldCondition(0),
        new GreatWorksCondition(null, false, 0),
        new VisitorsPerDayCondition(0),
        new DiscoveredCondition(DiscoveryKind.IronDeposit, null, 0),
    };

    [Theory]
    [MemberData(nameof(UnimplementedKinds))]
    public void Evaluate_KindsNotInTheSlice_AreFalseEvenWithAZeroThreshold(Condition condition)
    {
        var harness = ProgressionHarness.Create();

        harness.Queries.Evaluate(condition).ShouldBeFalse();
        harness.Queries.Evaluate(new NotCondition(condition)).ShouldBeTrue();
    }

    // ---- plumbing -----------------------------------------------------------------------------------------------

    [Fact]
    public void Evaluate_Null_Throws()
    {
        var harness = ProgressionHarness.Create();

        Should.Throw<ArgumentNullException>(() => harness.Queries.Evaluate(null!));
    }

    [Fact]
    public void Evaluate_WithoutAContext_Throws()
    {
        var modifiers = new ModifiersSystem(new ModifiersState(), ProgressionPack.Content);
        var system = new ProgressionSystem(new ProgressionState(), ProgressionPack.Content, modifiers);

        Should.Throw<InvalidOperationException>(() => system.Queries.Evaluate(_true));
    }

    [Fact]
    public void Evaluate_EmptyCombinators_FollowTheLogicalIdentities()
    {
        // The loader requires at least one sub-condition, but the evaluator must not misbehave on an empty list.
        var harness = ProgressionHarness.Create();

        harness.Queries.Evaluate(new AllCondition(ImmutableArray<Condition>.Empty)).ShouldBeTrue();
        harness.Queries.Evaluate(new AnyCondition(ImmutableArray<Condition>.Empty)).ShouldBeFalse();
    }

    private static Condition Atom(bool value) => value ? _true : _false;
}
