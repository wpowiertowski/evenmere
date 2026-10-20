using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Time;

namespace Evenmere.Content.Tests;

/// <summary>
/// Binds the code to the vocabularies written in architecture.md, so a doc change or a code change that
/// drifts apart fails a test instead of silently diverging.
/// </summary>
public sealed class VocabularyDocTests
{
    [Fact]
    public void Effects_AreExactlyTheKindsListedInTheArchitecturePlan()
    {
        // architecture.md, "Effects vocabulary": unlocks (7), traditionSlot, modifier, grants (2), revealCoastline, requestFestival.
        Type[] effectTypes = [.. typeof(Effect).Assembly.GetTypes().Where(static t => t.IsSubclassOf(typeof(Effect)))];

        effectTypes.Select(static t => t.Name).Order(StringComparer.Ordinal).ShouldBe(
        [
            nameof(GrantGoodsEffect), nameof(GrantPointsEffect), nameof(ModifierEffect), nameof(RequestFestivalEffect),
            nameof(RevealCoastlineEffect), nameof(TraditionSlotEffect), nameof(UnlockBuildingEffect), nameof(UnlockFeatureEffect),
            nameof(UnlockFestivalEffect), nameof(UnlockRecipeEffect), nameof(UnlockTerrainAccessEffect), nameof(UnlockTierEffect),
            nameof(UnlockTraditionEffect),
        ]);
        ContentNames.EffectKinds.Length.ShouldBe(effectTypes.Length);
        effectTypes.ShouldAllBe(static t => t.IsSealed);
        typeof(Effect).IsAbstract.ShouldBeTrue();
    }

    [Fact]
    public void Conditions_AreExactlyTheKindsListedInTheArchitecturePlan()
    {
        // architecture.md, "Conditions vocabulary": counts (9, with stock), flags (5), combinators (3).
        Type[] conditionTypes = [.. typeof(Condition).Assembly.GetTypes().Where(static t => t.IsSubclassOf(typeof(Condition)))];

        conditionTypes.Select(static t => t.Name).Order(StringComparer.Ordinal).ShouldBe(
        [
            nameof(AdoptedCondition), nameof(AllCondition), nameof(AnyCondition), nameof(ConsumedCondition),
            nameof(CountCondition), nameof(DiscoveredCondition), nameof(EraCondition), nameof(ExpeditionsCondition),
            nameof(FestivalsHeldCondition), nameof(GreatWorksCondition), nameof(NotCondition), nameof(PopulationCondition),
            nameof(ProducedCondition), nameof(ResearchedCondition), nameof(RoadTilesCondition), nameof(SeasonCondition),
            nameof(StockCondition), nameof(VisitorsPerDayCondition),
        ]);
        ContentNames.ConditionKinds.Length.ShouldBe(conditionTypes.Length);
        conditionTypes.ShouldAllBe(static t => t.IsSealed);
        typeof(Condition).IsAbstract.ShouldBeTrue();
    }

    [Fact]
    public void ModifierTargets_AreTheSelectorsListedInTheArchitecturePlan()
    {
        // building, tag, category (via BuildingsTarget), tier, good, discovery, or none for island-wide.
        Type[] targets = [.. typeof(ModifierTarget).Assembly.GetTypes().Where(static t => t.IsSubclassOf(typeof(ModifierTarget)))];

        targets.Select(static t => t.Name).Order(StringComparer.Ordinal).ShouldBe(
            [nameof(BuildingsTarget), nameof(DiscoveryTarget), nameof(GoodTarget), nameof(IslandTarget), nameof(TierTarget)]);
        targets.ShouldAllBe(static t => t.IsSealed);
        ContentNames.ModifierTargetKinds.ShouldBe(["building", "tag", "category", "tier", "good", "discovery"]);
    }

    [Fact]
    public void SiteConditions_AreTerrainAndNear()
    {
        Type[] sites = [.. typeof(SiteCondition).Assembly.GetTypes().Where(static t => t.IsSubclassOf(typeof(SiteCondition)))];

        sites.Select(static t => t.Name).Order(StringComparer.Ordinal).ShouldBe([nameof(NearSite), nameof(TerrainSite)]);
        sites.ShouldAllBe(static t => t.IsSealed);
        typeof(SiteCondition).IsAbstract.ShouldBeTrue();
        ContentNames.SiteConditionKinds.ShouldBe(["terrain", "near"]);
    }

    [Fact]
    public void DiscoveryTargets_AreTheMapDiscoveriesOnly()
    {
        ContentNames.DiscoveryTargetKinds.ShouldBe(["natural_wonder", "ruin", "shipwreck"]);
        ContentNames.DiscoveryTargetKinds.ShouldBe(
            [ContentNames.NameOf(DiscoveryKind.NaturalWonder), ContentNames.NameOf(DiscoveryKind.Ruin), ContentNames.NameOf(DiscoveryKind.Shipwreck)]);
    }

    [Fact]
    public void Stats_AreExactlyTheModifierStatsListedInTheArchitecturePlan() =>
        ContentNames.NamesOf<Stat>().ShouldBe(
        [
            "productivity", "consumption", "constructionSpeed", "capacity", "workforce", "output.knowledge",
            "output.culture", "output.coin", "attractiveness", "visitors", "cartSpeed", "materialCost", "declineGrace",
            "expeditionSpeed", "comfortBonus", "festivalCost", "festivalDuration", "settlerArrival",
        ]);

    [Fact]
    public void TerrainAccess_IsExactlyTheFlagsListedInTheArchitecturePlan() =>
        ContentNames.NamesOf<TerrainAccess>().ShouldBe(
            ["narrow_rivers", "wide_rivers", "hill_farming", "marsh_drainage", "highlands", "deep_water", "reefs"]);

    [Fact]
    public void Features_AreExactlyTheFeaturesListedInTheArchitecturePlan() =>
        ContentNames.NamesOf<FeatureKind>().ShouldBe(["expeditions", "coin", "visitors", "great_works"]);

    [Fact]
    public void TraditionSlots_AreExactlyTheSlotsListedInTheArchitecturePlan() =>
        ContentNames.NamesOf<TraditionSlotType>().ShouldBe(["hearth", "craft", "heritage", "wild"]);

    [Fact]
    public void ContentTypes_AreExactlyTheDirectoriesListedInTheArchitecturePlan()
    {
        ContentTypes.All.Where(static t => t != ContentType.Rules).Select(static t => t.DirectoryName()).ShouldBe(
        [
            "goods", "buildings", "recipes", "tiers", "techs", "civics", "traditions", "festivals", "greatworks",
            "monuments", "happenings", "eras", "terrains",
        ]);
        ContentType.Rules.DirectoryName().ShouldBe("rules");
        ContentTypes.RulesId.ShouldBe("core");
    }

    [Theory]
    [InlineData("goods", ContentType.Goods)]
    [InlineData("greatworks", ContentType.GreatWorks)]
    [InlineData("rules", ContentType.Rules)]
    public void ContentTypes_TryFromDirectory_FindsTheType(string directory, ContentType expected)
    {
        ContentTypes.TryFromDirectory(directory, out ContentType type).ShouldBeTrue();
        type.ShouldBe(expected);
    }

    [Theory]
    [InlineData("good")]
    [InlineData("Goods")]
    [InlineData("locale")]
    public void ContentTypes_TryFromDirectory_RejectsOtherNames(string directory) =>
        ContentTypes.TryFromDirectory(directory, out _).ShouldBeFalse();

    [Fact]
    public void ContentNames_NameOfEveryMemberParsesBack()
    {
        AssertRoundTrip<GoodCategory>();
        AssertRoundTrip<BuildingCategory>();
        AssertRoundTrip<ServiceKind>();
        AssertRoundTrip<DepositKind>();
        AssertRoundTrip<CropKind>();
        AssertRoundTrip<TerrainAccess>();
        AssertRoundTrip<FeatureKind>();
        AssertRoundTrip<TraditionSlotType>();
        AssertRoundTrip<Stat>();
        AssertRoundTrip<PointKind>();
        AssertRoundTrip<DiscoveryKind>();
        AssertRoundTrip<AreaYieldKind>();
        AssertRoundTrip<GreatWorkSource>();
        AssertRoundTrip<Season>();
    }

    [Fact]
    public void ContentNames_UseSnakeCaseUnlessOverridden()
    {
        ContentNames.NameOf(ServiceKind.ConcertHall).ShouldBe("concert_hall");
        ContentNames.NameOf(CropKind.SugarBeet).ShouldBe("sugar_beet");
        ContentNames.NameOf(DiscoveryKind.IronDeposit).ShouldBe("iron_deposit");
        ContentNames.NameOf(AreaYieldKind.FootprintFertility).ShouldBe("footprint_fertility");
        ContentNames.NameOf(Season.Autumn).ShouldBe("autumn");
        ContentNames.NameOf(Stat.OutputKnowledge).ShouldBe("output.knowledge");
    }

    [Fact]
    public void ContentNames_Parsing_IsCaseSensitiveAndRejectsUnknownNames()
    {
        ContentNames.TryParse("Autumn", out Season _).ShouldBeFalse();
        ContentNames.TryParse("monsoon", out Season _).ShouldBeFalse();
        ContentNames.TryParse("autumn", out Season season).ShouldBeTrue();
        season.ShouldBe(Season.Autumn);
        Should.Throw<ArgumentOutOfRangeException>(() => ContentNames.NameOf((Season)99));
    }

    private static void AssertRoundTrip<T>()
        where T : struct, Enum
    {
        ContentNames.NamesOf<T>().Length.ShouldBe(Enum.GetValues<T>().Length);
        ContentNames.NamesOf<T>().Distinct(StringComparer.Ordinal).Count().ShouldBe(ContentNames.NamesOf<T>().Length);

        foreach (T value in Enum.GetValues<T>())
        {
            string name = ContentNames.NameOf(value);
            ContentNames.TryParse(name, out T parsed).ShouldBeTrue();
            parsed.ShouldBe(value);
        }
    }
}
