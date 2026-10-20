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

public sealed class ModifierSaveTests
{
    private static ModifierContext[] Contexts(ProgressionHarness harness) =>
    [
        ModifierContext.Island,
        ModifierContext.ForBuildingType(harness.Building("grain_farm")),
        ModifierContext.ForBuildingType(harness.Building("bakery")),
        ModifierContext.ForBuildingType(harness.Building("scribes_hall")),
        ModifierContext.ForBuildingType(harness.Building("windmill")),
        ModifierContext.ForTier(harness.Tier("villagers")),
        ModifierContext.ForGood(harness.Good("bread")),
        ModifierContext.ForTierAndGood(harness.Tier("villagers"), harness.Good("bread")),
    ];

    [Fact]
    public void Reload_AfterEveryKindOfModifier_RebuildsTheIndexWithTheSameAnswers()
    {
        var harness = ProgressionHarness.Create();
        harness.ApplyTech("kitchen_tech");
        harness.ApplyTech("milling");
        harness.Apply(
            EffectSource.ForCivic(new CivicId(0)),
            Fx.Modifier(Stat.Productivity, 12, new BuildingsTarget(new BuildingCategorySelector(BuildingCategory.Production)), new NotCondition(new SeasonCondition(Season.Winter))),
            Fx.Modifier(Stat.Productivity, 3, when: new AnyCondition([new SeasonCondition(Season.Spring), new SeasonCondition(Season.Summer)])),
            Fx.Modifier(Stat.Capacity, 8, new TierTarget(harness.Tier("villagers")), new AllCondition([new SeasonCondition(Season.Summer), new PopulationCondition(null, 5)])));
        harness.Fake.Queries.Population.TotalResidents = 10;
        harness.Modifiers.Count.ShouldBe(10);

        var reloaded = ProgressionHarness.Reload(harness);

        reloaded.Modifiers.Count.ShouldBe(10);
        reloaded.Fake.Queries.Population.TotalResidents = 10;
        foreach (Season season in Enum.GetValues<Season>())
        {
            harness.SetSeason(season);
            reloaded.SetSeason(season);
            foreach (Stat stat in Enum.GetValues<Stat>())
            {
                foreach (ModifierContext context in Contexts(harness))
                {
                    reloaded.Queries.BonusPercent(stat, context).ShouldBe(harness.Queries.BonusPercent(stat, context), $"{stat} in {season} for {context}");
                }
            }
        }
    }

    [Fact]
    public void Reload_ListModifiers_GivesTheSameContributionsInTheSameOrder()
    {
        var harness = ProgressionHarness.Create();
        harness.ApplyTech("kitchen_tech");
        harness.Apply(EffectSource.ForCivic(new CivicId(3)), Fx.Modifier(Stat.Productivity, 4));
        harness.Apply(EffectSource.ForTech(new TechId(7)), Fx.Modifier(Stat.Productivity, 2));
        var reloaded = ProgressionHarness.Reload(harness);

        foreach (Season season in new[] { Season.Spring, Season.Autumn })
        {
            harness.SetSeason(season);
            reloaded.SetSeason(season);
            var before = new List<ModifierContribution>();
            var after = new List<ModifierContribution>();
            harness.Queries.ListModifiers(Stat.Productivity, ModifierContext.ForBuildingType(harness.Building("grain_farm")), before);
            reloaded.Queries.ListModifiers(Stat.Productivity, ModifierContext.ForBuildingType(reloaded.Building("grain_farm")), after);

            after.ShouldBe(before, season.ToString());
            after.Count.ShouldBe(3);
        }
    }

    [Fact]
    public void Reload_TwoPlus20_StillGive40()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(EffectSource.ForTech(new TechId(1)), Fx.Modifier(Stat.Productivity, 20));
        harness.Apply(EffectSource.ForTech(new TechId(2)), Fx.Modifier(Stat.Productivity, 20));
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(40);

        var reloaded = ProgressionHarness.Reload(harness);

        reloaded.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(40);
    }

    [Fact]
    public void Reload_AfterAQuery_DoesNotSaveTheCache()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(EffectSource.ForTech(new TechId(1)), Fx.Modifier(Stat.Productivity, 20));
        byte[] before = harness.Save();

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.ForBuildingType(harness.Building("bakery")));

        harness.Save().ShouldBe(before);
        ProgressionHarness.Reload(harness).Modifiers.Index.CachedAnswerCount.ShouldBe(0);
    }

    [Fact]
    public void Reload_ModifiersAddedAfterwards_JoinTheRebuiltIndex()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(EffectSource.ForTech(new TechId(1)), Fx.Modifier(Stat.Productivity, 20));
        var reloaded = ProgressionHarness.Reload(harness);

        reloaded.Apply(EffectSource.ForTech(new TechId(2)), Fx.Modifier(Stat.Productivity, 5));

        reloaded.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(25);
        ProgressionHarness.Reload(reloaded).Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(25);
    }

    [Fact]
    public void Save_ModifierRecord_HoldsTheTargetAndTheEncodedCondition()
    {
        var harness = ProgressionHarness.Create();

        harness.Apply(
            EffectSource.ForTradition(new TraditionId(2)),
            Fx.Modifier(Stat.Productivity, 20, new BuildingsTarget(new BuildingTagSelector("farm")), new SeasonCondition(Season.Autumn)));

        ActiveModifier saved = harness.Fake.World.Modifiers.Active.ShouldHaveSingleItem();
        saved.Source.ShouldBe(EffectSource.ForTradition(new TraditionId(2)));
        saved.Stat.ShouldBe(Stat.Productivity);
        saved.TargetKind.ShouldBe(ModifierTargetKind.Tag);
        saved.TargetTag.ShouldBe("farm");
        saved.Percent.ShouldBe(20);
        saved.When.ShouldBe([14, 2]);
    }

    [Fact]
    public void Save_UnconditionalIslandModifier_HasNoConditionAndNoTag()
    {
        var harness = ProgressionHarness.Create();

        harness.Apply(EffectSource.ForTech(new TechId(1)), Fx.Modifier(Stat.DeclineGrace, 50));

        ActiveModifier saved = harness.Fake.World.Modifiers.Active.ShouldHaveSingleItem();
        saved.TargetKind.ShouldBe(ModifierTargetKind.Island);
        saved.TargetTag.ShouldBeNull();
        saved.When.ShouldBeNull();
    }
}
