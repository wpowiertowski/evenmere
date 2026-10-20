using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Modifiers;
using Evenmere.Sim.Tests.Progression;

namespace Evenmere.Sim.Tests.Modifiers;

public sealed class ModifierIndexTests
{
    private static readonly EffectSource _a = EffectSource.ForTech(new TechId(1));
    private static readonly EffectSource _b = EffectSource.ForTech(new TechId(2));

    // ---- the acceptance cases --------------------------------------------------------------------------------------

    [Fact]
    public void BonusPercent_TwoPlus20Modifiers_Give40AndApplyOnce()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 20));
        harness.Apply(_b, Fx.Modifier(Stat.Productivity, 20));

        int percent = harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island);

        percent.ShouldBe(40);

        // Added first, then applied once: ×1.4, not ×1.2 × 1.2 = ×1.44.
        Ratio.FromBonusPercent(percent).Apply(Amount.FromUnits(10)).ShouldBe(Amount.FromUnits(14));
    }

    [Theory]
    [InlineData(Season.Spring, 0, false)]
    [InlineData(Season.Summer, 0, false)]
    [InlineData(Season.Autumn, 20, true)]
    [InlineData(Season.Winter, 0, false)]
    public void BonusPercent_AutumnOnlyModifier_IsActiveOnlyInAutumn(Season season, int expected, bool listedActive)
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 20, when: new SeasonCondition(Season.Autumn)));
        harness.SetSeason(season);

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(expected);

        var listed = new List<ModifierContribution>();
        harness.Queries.ListModifiers(Stat.Productivity, ModifierContext.Island, listed);
        listed.ShouldBe([new ModifierContribution(_a, 20, listedActive)]);
    }

    [Fact]
    public void BonusPercent_RealTraditionCard_BoostsFarmsInAutumnOnly()
    {
        var harness = ProgressionHarness.Create();
        TraditionId card = harness.Content.IdOf<TraditionId>("harvest_festival");
        harness.Apply(EffectSource.ForTradition(card), [.. harness.Content.Get(card).Effects]);
        var farm = ModifierContext.ForBuildingType(harness.Building("grain_farm"));
        var mill = ModifierContext.ForBuildingType(harness.Building("windmill"));

        harness.SetSeason(Season.Autumn);
        harness.Queries.BonusPercent(Stat.Productivity, farm).ShouldBe(20);
        harness.Queries.BonusPercent(Stat.Productivity, mill).ShouldBe(0);
        harness.SetSeason(Season.Winter);
        harness.Queries.BonusPercent(Stat.Productivity, farm).ShouldBe(0);
    }

    // ---- matching, by target kind -----------------------------------------------------------------------------------

    [Fact]
    public void BonusPercent_NoModifiers_IsZeroForEveryStat()
    {
        var harness = ProgressionHarness.Create();

        foreach (Stat stat in Enum.GetValues<Stat>())
        {
            harness.Queries.BonusPercent(stat, ModifierContext.Island).ShouldBe(0, stat.ToString());
        }
    }

    [Fact]
    public void BonusPercent_IslandWideModifier_MatchesEveryContext()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.ConstructionSpeed, 10));

        foreach (ModifierContext context in AllKindsOfContext(harness))
        {
            harness.Queries.BonusPercent(Stat.ConstructionSpeed, context).ShouldBe(10, context.ToString());
        }
    }

    [Fact]
    public void BonusPercent_BuildingTypeTarget_MatchesThatTypeWhateverTheEntity()
    {
        var harness = ProgressionHarness.Create();
        BuildingTypeId bakery = harness.Building("bakery");
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 15, new BuildingsTarget(new BuildingTypeSelector(bakery))));

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForBuildingType(bakery)).ShouldBe(15);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForBuilding(new EntityId(77), bakery)).ShouldBe(15);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForBuildingType(harness.Building("windmill"))).ShouldBe(0);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(0);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForTier(harness.Tier("settlers"))).ShouldBe(0);
    }

    [Fact]
    public void BonusPercent_TagTarget_MatchesEveryTypeCarryingTheTag()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 25, new BuildingsTarget(new BuildingTagSelector("processing"))));

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForBuildingType(harness.Building("bakery"))).ShouldBe(25);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForBuildingType(harness.Building("windmill"))).ShouldBe(25);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForBuildingType(harness.Building("grain_farm"))).ShouldBe(0);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForBuildingType(harness.Building("settler_house"))).ShouldBe(0, "a type with no tags never matches a tag");
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(0);
    }

    [Fact]
    public void BonusPercent_TagTarget_IsCaseSensitive()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 25, new BuildingsTarget(new BuildingTagSelector("Processing"))));

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForBuildingType(harness.Building("bakery"))).ShouldBe(0);
    }

    [Fact]
    public void BonusPercent_CategoryTarget_MatchesEveryTypeInTheCategory()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.ConstructionSpeed, -10, new BuildingsTarget(new BuildingCategorySelector(BuildingCategory.Production))));

        harness.Queries.BonusPercent(Stat.ConstructionSpeed, ModifierContext.ForBuildingType(harness.Building("bakery"))).ShouldBe(-10);
        harness.Queries.BonusPercent(Stat.ConstructionSpeed, ModifierContext.ForBuildingType(harness.Building("grain_farm"))).ShouldBe(-10);
        harness.Queries.BonusPercent(Stat.ConstructionSpeed, ModifierContext.ForBuildingType(harness.Building("scribes_hall"))).ShouldBe(0);
        harness.Queries.BonusPercent(Stat.ConstructionSpeed, ModifierContext.Island).ShouldBe(0);
    }

    [Fact]
    public void BonusPercent_TierTarget_MatchesThatTierWhateverTheGood()
    {
        var harness = ProgressionHarness.Create();
        TierId villagers = harness.Tier("villagers");
        harness.Apply(_a, Fx.Modifier(Stat.Capacity, 15, new TierTarget(villagers)));

        harness.Queries.BonusPercent(Stat.Capacity, ModifierContext.ForTier(villagers)).ShouldBe(15);
        harness.Queries.BonusPercent(Stat.Capacity, ModifierContext.ForTierAndGood(villagers, harness.Good("fish"))).ShouldBe(15);
        harness.Queries.BonusPercent(Stat.Capacity, ModifierContext.ForTier(harness.Tier("settlers"))).ShouldBe(0);
        harness.Queries.BonusPercent(Stat.Capacity, ModifierContext.ForGood(harness.Good("fish"))).ShouldBe(0);
        harness.Queries.BonusPercent(Stat.Capacity, ModifierContext.Island).ShouldBe(0);
    }

    [Fact]
    public void BonusPercent_GoodTarget_MatchesThatGoodWhateverTheTier()
    {
        var harness = ProgressionHarness.Create();
        GoodId bread = harness.Good("bread");
        harness.Apply(_a, Fx.Modifier(Stat.Consumption, 5, new GoodTarget(bread)));

        harness.Queries.BonusPercent(Stat.Consumption, ModifierContext.ForGood(bread)).ShouldBe(5);
        harness.Queries.BonusPercent(Stat.Consumption, ModifierContext.ForTierAndGood(harness.Tier("villagers"), bread)).ShouldBe(5);
        harness.Queries.BonusPercent(Stat.Consumption, ModifierContext.ForGood(harness.Good("fish"))).ShouldBe(0);
        harness.Queries.BonusPercent(Stat.Consumption, ModifierContext.ForTier(harness.Tier("villagers"))).ShouldBe(0);
    }

    [Fact]
    public void BonusPercent_DiscoveryTarget_MatchesNoContextYet()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.OutputCulture, 50, new DiscoveryTarget(DiscoveryKind.NaturalWonder)));

        foreach (ModifierContext context in AllKindsOfContext(harness))
        {
            harness.Queries.BonusPercent(Stat.OutputCulture, context).ShouldBe(0, context.ToString());
        }

        // The modifier is registered, so exploration (M2-06) only has to supply a context that can match it.
        harness.Modifiers.Count.ShouldBe(1);
    }

    [Fact]
    public void BonusPercent_ModifiersOfDifferentTargets_AddForTheContextsTheyShare()
    {
        var harness = ProgressionHarness.Create();
        BuildingTypeId farm = harness.Building("grain_farm");
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 10));
        harness.Apply(_b, Fx.Modifier(Stat.Productivity, 20, new BuildingsTarget(new BuildingTagSelector("farm"))));
        harness.Apply(EffectSource.ForCivic(new CivicId(1)), Fx.Modifier(Stat.Productivity, 5, new BuildingsTarget(new BuildingTypeSelector(farm))));
        harness.Apply(EffectSource.ForCivic(new CivicId(2)), Fx.Modifier(Stat.Productivity, -3, new BuildingsTarget(new BuildingCategorySelector(BuildingCategory.Production))));

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForBuildingType(farm)).ShouldBe(10 + 20 + 5 - 3);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForBuildingType(harness.Building("windmill"))).ShouldBe(10 - 3);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(10);
    }

    [Fact]
    public void BonusPercent_ModifiersOfOtherStats_DoNotMix()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 20), Fx.Modifier(Stat.Consumption, 7));

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(20);
        harness.Queries.BonusPercent(Stat.Consumption, ModifierContext.Island).ShouldBe(7);
        harness.Queries.BonusPercent(Stat.Capacity, ModifierContext.Island).ShouldBe(0);
    }

    [Fact]
    public void BonusPercent_NetPenalty_IsNegativeAndTheRatioFloorsAtZero()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, -60));
        harness.Apply(_b, Fx.Modifier(Stat.Productivity, -60));

        int percent = harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island);

        percent.ShouldBe(-120);
        Ratio.FromBonusPercent(percent).ShouldBe(Ratio.Zero);
    }

    // ---- conditions ---------------------------------------------------------------------------------------------

    [Fact]
    public void BonusPercent_ConditionOnStock_ReadsThePoolOnEveryQuery()
    {
        var harness = ProgressionHarness.Create();
        GoodId timber = harness.Good("timber");
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 30, when: new StockCondition(timber, Amount.FromUnits(40))));

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(0);
        harness.Fake.Queries.Storage.Set(timber, Amount.FromUnits(40));
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(30);
        harness.Fake.Queries.Storage.Set(timber, Amount.FromUnits(39));
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(0);
    }

    [Fact]
    public void BonusPercent_ConditionOnPopulation_ReadsThePopulationOnEveryQuery()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.SettlerArrival, 50, when: new PopulationCondition(null, 100)));

        harness.Queries.BonusPercent(Stat.SettlerArrival, ModifierContext.Island).ShouldBe(0);
        harness.Fake.Queries.Population.TotalResidents = 100;
        harness.Queries.BonusPercent(Stat.SettlerArrival, ModifierContext.Island).ShouldBe(50);
    }

    [Fact]
    public void BonusPercent_MixedFixedSeasonalAndDynamic_SumsEachByItsOwnRule()
    {
        var harness = ProgressionHarness.Create();
        GoodId timber = harness.Good("timber");
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 1));
        harness.Apply(_b, Fx.Modifier(Stat.Productivity, 10, when: new SeasonCondition(Season.Winter)));
        harness.Apply(EffectSource.ForCivic(new CivicId(1)), Fx.Modifier(Stat.Productivity, 100, when: new StockCondition(timber, Amount.FromUnits(1))));
        harness.Apply(
            EffectSource.ForCivic(new CivicId(2)),
            Fx.Modifier(Stat.Productivity, 1000, when: new AllCondition([new SeasonCondition(Season.Winter), new StockCondition(timber, Amount.FromUnits(1))])));

        harness.SetSeason(Season.Spring);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(1);
        harness.Fake.Queries.Storage.Set(timber, Amount.FromUnits(1));
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(101);
        harness.SetSeason(Season.Winter);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(1111);
        harness.Fake.Queries.Storage.Set(timber, Amount.Zero);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(11);
    }

    [Theory]
    [InlineData(Season.Spring, 5)]
    [InlineData(Season.Summer, 5)]
    [InlineData(Season.Autumn, 0)]
    [InlineData(Season.Winter, 5)]
    public void BonusPercent_NotAutumn_IsActiveInTheOtherThreeSeasons(Season season, int expected)
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 5, when: new NotCondition(new SeasonCondition(Season.Autumn))));
        harness.SetSeason(season);

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(expected);
    }

    // ---- the cache ----------------------------------------------------------------------------------------------

    [Fact]
    public void Cache_RepeatedQuery_IsAnsweredFromOneEntry()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 20));
        harness.Modifiers.Index.CachedAnswerCount.ShouldBe(0);

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(20);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(20);

        harness.Modifiers.Index.CachedAnswerCount.ShouldBe(1);
    }

    [Fact]
    public void Cache_ByStatTypeTierAndGood_KeepsSeparateEntries()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 20), Fx.Modifier(Stat.Consumption, 5));

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island);
        harness.Queries.BonusPercent(Stat.Consumption, ModifierContext.Island);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForBuildingType(harness.Building("bakery")));
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForTier(harness.Tier("villagers")));
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForGood(harness.Good("bread")));
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForTierAndGood(harness.Tier("villagers"), harness.Good("bread")));

        harness.Modifiers.Index.CachedAnswerCount.ShouldBe(6);
    }

    [Fact]
    public void Cache_StatWithNoModifiers_IsNotCached()
    {
        var harness = ProgressionHarness.Create();

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(0);

        harness.Modifiers.Index.CachedAnswerCount.ShouldBe(0);
    }

    [Fact]
    public void Cache_NewModifier_InvalidatesTheAnswersAlreadyGiven()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 20));
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(20);

        harness.Apply(_b, Fx.Modifier(Stat.Productivity, 20));

        harness.Modifiers.Index.CachedAnswerCount.ShouldBe(0);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(40);
    }

    [Fact]
    public void Cache_SeasonChange_GivesTheNewSeasonsAnswerAndKeepsTheOldOne()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 20, when: new SeasonCondition(Season.Autumn)));

        harness.SetSeason(Season.Summer);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(0);
        harness.SetSeason(Season.Autumn);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(20);
        harness.SetSeason(Season.Winter);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(0);
        harness.SetSeason(Season.Autumn);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(20, "back in autumn, the autumn entry is still right");

        harness.Modifiers.Index.CachedAnswerCount.ShouldBe(3);
    }

    [Fact]
    public void Cache_SeasonChangeWithinOneTickOfTheBoundary_IsNeverStale()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 20, when: new SeasonCondition(Season.Autumn)));

        harness.Fake.SetTick((2L * GameTime.TicksPerSeason) - 1);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(0);
        harness.Fake.SetTick(2L * GameTime.TicksPerSeason);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(20);
        harness.Fake.SetTick((3L * GameTime.TicksPerSeason) - 1);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(20);
        harness.Fake.SetTick(3L * GameTime.TicksPerSeason);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(0);
    }

    [Fact]
    public void Cache_SeasonModifierAddedAfterAnAnswerWasCached_IsSeenAtOnce()
    {
        var harness = ProgressionHarness.Create();
        harness.SetSeason(Season.Autumn);
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 10));
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(10);

        harness.Apply(_b, Fx.Modifier(Stat.Productivity, 20, when: new SeasonCondition(Season.Autumn)));

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(30);
    }

    // ---- ListModifiers ------------------------------------------------------------------------------------------

    [Fact]
    public void ListModifiers_ByApplicationOrder_StillListsInAscendingSourceOrder()
    {
        var harness = ProgressionHarness.Create();
        var happening = EffectSource.ForHappening(new HappeningId(1));
        var civic = EffectSource.ForCivic(new CivicId(0));
        var tech9 = EffectSource.ForTech(new TechId(9));
        var tech3 = EffectSource.ForTech(new TechId(3));
        harness.Apply(happening, Fx.Modifier(Stat.Productivity, 1));
        harness.Apply(tech9, Fx.Modifier(Stat.Productivity, 2));
        harness.Apply(civic, Fx.Modifier(Stat.Productivity, 3));
        harness.Apply(tech3, Fx.Modifier(Stat.Productivity, 4));

        var listed = new List<ModifierContribution>();
        harness.Queries.ListModifiers(Stat.Productivity, ModifierContext.Island, listed);

        listed.Select(static c => c.Source).ShouldBe([tech3, tech9, civic, happening]);
        listed.Select(static c => c.Percent).ShouldBe([4, 2, 3, 1]);
    }

    [Fact]
    public void ListModifiers_SameSourceTwice_KeepsApplicationOrder()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 7), Fx.Modifier(Stat.Productivity, 3), Fx.Modifier(Stat.Productivity, 5));

        var listed = new List<ModifierContribution>();
        harness.Queries.ListModifiers(Stat.Productivity, ModifierContext.Island, listed);

        listed.Select(static c => c.Percent).ShouldBe([7, 3, 5]);
    }

    [Fact]
    public void ListModifiers_OnlyMatchingModifiersOfTheStat_AreListed()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 1), Fx.Modifier(Stat.Consumption, 2));
        harness.Apply(_b, Fx.Modifier(Stat.Productivity, 4, new BuildingsTarget(new BuildingTagSelector("farm"))));

        var island = new List<ModifierContribution>();
        var farm = new List<ModifierContribution>();
        harness.Queries.ListModifiers(Stat.Productivity, ModifierContext.Island, island);
        harness.Queries.ListModifiers(Stat.Productivity, ModifierContext.ForBuildingType(harness.Building("grain_farm")), farm);

        island.Select(static c => c.Percent).ShouldBe([1]);
        farm.Select(static c => c.Percent).ShouldBe([1, 4]);
    }

    [Fact]
    public void ListModifiers_AppendsAfterWhatTheListAlreadyHolds()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 1));
        var existing = new ModifierContribution(_b, 99, false);
        var listed = new List<ModifierContribution> { existing };

        harness.Queries.ListModifiers(Stat.Productivity, ModifierContext.Island, listed);

        listed.Count.ShouldBe(2);
        listed[0].ShouldBe(existing);
        listed[1].ShouldBe(new ModifierContribution(_a, 1, true));
    }

    [Fact]
    public void ListModifiers_Contributions_AddUpToBonusPercentWhenActive()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_a, Fx.Modifier(Stat.Productivity, 20), Fx.Modifier(Stat.Productivity, 15, when: new SeasonCondition(Season.Autumn)));
        harness.Apply(_b, Fx.Modifier(Stat.Productivity, -5));

        foreach (Season season in Enum.GetValues<Season>())
        {
            harness.SetSeason(season);
            var listed = new List<ModifierContribution>();
            harness.Queries.ListModifiers(Stat.Productivity, ModifierContext.Island, listed);

            listed.Where(static c => c.IsActive).Sum(static c => c.Percent).ShouldBe(harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island), season.ToString());
        }
    }

    [Fact]
    public void ListModifiers_Null_Throws()
    {
        var harness = ProgressionHarness.Create();

        Should.Throw<ArgumentNullException>(() => harness.Queries.ListModifiers(Stat.Productivity, ModifierContext.Island, null!));
    }

    // ---- before the simulation is bound ------------------------------------------------------------------------------

    [Fact]
    public void BonusPercent_IndexNotAttachedToAnEnvironment_TreatsConditionsAsFalse()
    {
        var index = new ModifierIndex(ProgressionPack.Content);
        index.Add(new ActiveModifier { Source = _a, Stat = Stat.Productivity, Percent = 10 });
        index.Add(new ActiveModifier
        {
            Source = _b,
            Stat = Stat.Productivity,
            Percent = 20,
            When = ConditionCodec.Encode(new SeasonCondition(Season.Spring)),
        });

        index.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(10);
    }

    [Fact]
    public void Add_StatThatDoesNotExist_Throws()
    {
        var index = new ModifierIndex(ProgressionPack.Content);

        Should.Throw<InvalidDataException>(() => index.Add(new ActiveModifier { Source = _a, Stat = (Stat)99, Percent = 1 }));
        index.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(ModifierTargetKind.Building, 999, null)]
    [InlineData(ModifierTargetKind.Tier, 999, null)]
    [InlineData(ModifierTargetKind.Good, 999, null)]
    [InlineData(ModifierTargetKind.Category, 99, null)]
    [InlineData(ModifierTargetKind.Discovery, 99, null)]
    [InlineData(ModifierTargetKind.Tag, 0, null)]
    [InlineData(ModifierTargetKind.Tag, 0, "")]
    [InlineData((ModifierTargetKind)99, 0, null)]
    public void Add_TargetTheContentDoesNotHave_Throws(ModifierTargetKind kind, int value, string? tag)
    {
        var index = new ModifierIndex(ProgressionPack.Content);

        Should.Throw<InvalidDataException>(() => index.Add(
            new ActiveModifier { Source = _a, Stat = Stat.Productivity, TargetKind = kind, TargetValue = value, TargetTag = tag, Percent = 1 }));
    }

    private static ModifierContext[] AllKindsOfContext(ProgressionHarness harness) =>
    [
        ModifierContext.Island,
        ModifierContext.ForBuildingType(harness.Building("bakery")),
        ModifierContext.ForBuilding(new EntityId(5), harness.Building("grain_farm")),
        ModifierContext.ForTier(harness.Tier("villagers")),
        ModifierContext.ForGood(harness.Good("bread")),
        ModifierContext.ForTierAndGood(harness.Tier("settlers"), harness.Good("fish")),
    ];
}
