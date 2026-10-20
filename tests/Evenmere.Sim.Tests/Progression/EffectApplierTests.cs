using System.Collections.Immutable;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Modifiers;
using Evenmere.Sim.Systems.Progression;

namespace Evenmere.Sim.Tests.Progression;

public sealed class EffectApplierTests
{
    private static readonly EffectSource _tech = EffectSource.ForTech(new TechId(3));
    private static readonly EffectSource _otherTech = EffectSource.ForTech(new TechId(4));

    [Fact]
    public void ApplyEffects_TechWithBuildingUnlocks_UnlocksThoseBuildings()
    {
        var harness = ProgressionHarness.Create();
        harness.Queries.IsUnlocked(harness.Building("windmill")).ShouldBeFalse();
        harness.Queries.IsUnlocked(harness.Building("bakery")).ShouldBeFalse();

        harness.ApplyTech("milling");

        harness.Queries.IsUnlocked(harness.Building("windmill")).ShouldBeTrue();
        harness.Queries.IsUnlocked(harness.Building("bakery")).ShouldBeTrue();
        harness.Queries.IsUnlocked(harness.Building("scribes_hall")).ShouldBeFalse();
    }

    [Fact]
    public void ApplyEffects_RealMilling_UnlocksTheWindmillAndTheBakery()
    {
        var harness = ProgressionHarness.Create(ProgressionPack.Real);
        harness.ApplyStartUnlocks();
        harness.Queries.IsUnlocked(harness.Building("windmill")).ShouldBeFalse();

        harness.ApplyTech("milling");

        harness.Queries.IsUnlocked(harness.Building("windmill")).ShouldBeTrue();
        harness.Queries.IsUnlocked(harness.Building("bakery")).ShouldBeTrue();
    }

    [Fact]
    public void ApplyEffects_EveryNodeOfTheRealContent_LeavesNoBuildingTierTraditionOrFestivalLocked()
    {
        // The linter promises that every item is a start item or is unlocked by exactly one node. Applying every node must
        // therefore open them all, which shows the applier handles every effect the real content uses.
        var harness = ProgressionHarness.Create(ProgressionPack.Real);
        harness.ApplyStartUnlocks();

        for (int i = 0; i < harness.Content.Techs.Count; i++)
        {
            var id = TechId.From((ushort)i);
            harness.Context.ProgressionService.ApplyEffects(EffectSource.ForTech(id), harness.Content.Get(id).Effects);
        }

        for (int i = 0; i < harness.Content.Civics.Count; i++)
        {
            var id = CivicId.From((ushort)i);
            harness.Context.ProgressionService.ApplyEffects(EffectSource.ForCivic(id), harness.Content.Get(id).Effects);
        }

        for (int i = 0; i < harness.Content.Buildings.Count; i++)
        {
            harness.Queries.IsUnlocked(BuildingTypeId.From((ushort)i)).ShouldBeTrue(harness.Content.Buildings.Names[i]);
        }

        for (int i = 0; i < harness.Content.Tiers.Count; i++)
        {
            harness.Queries.IsUnlocked(TierId.From((ushort)i)).ShouldBeTrue(harness.Content.Tiers.Names[i]);
        }

        for (int i = 0; i < harness.Content.Traditions.Count; i++)
        {
            harness.Queries.IsUnlocked(TraditionId.From((ushort)i)).ShouldBeTrue(harness.Content.Traditions.Names[i]);
        }

        for (int i = 0; i < harness.Content.Festivals.Count; i++)
        {
            harness.Queries.IsUnlocked(FestivalId.From((ushort)i)).ShouldBeTrue(harness.Content.Festivals.Names[i]);
        }
    }

    [Fact]
    public void ApplyEffects_UnlockRecipe_UnlocksOnlyThatRecipe()
    {
        var harness = ProgressionHarness.Create();
        RecipeId bake = harness.Content.IdOf<RecipeId>("bake_bread");

        harness.Apply(_tech, new UnlockRecipeEffect(bake));

        harness.Queries.IsUnlocked(bake).ShouldBeTrue();
        harness.Queries.IsUnlocked(harness.Content.IdOf<RecipeId>("grow_grain")).ShouldBeFalse();
    }

    [Fact]
    public void ApplyEffects_UnlockTier_UnlocksOnlyThatTier()
    {
        var harness = ProgressionHarness.Create();

        harness.Apply(_tech, new UnlockTierEffect(harness.Tier("townsfolk")));

        harness.Queries.IsUnlocked(harness.Tier("townsfolk")).ShouldBeTrue();
        harness.Queries.IsUnlocked(harness.Tier("villagers")).ShouldBeFalse();
    }

    [Fact]
    public void ApplyEffects_UnlockTraditionAndFestival_RecordThemForLaterSystems()
    {
        var harness = ProgressionHarness.Create();
        TraditionId tradition = harness.Content.IdOf<TraditionId>("harvest_festival");
        FestivalId festival = harness.Content.IdOf<FestivalId>("harvest_fair");

        harness.Apply(_tech, new UnlockTraditionEffect(tradition), new UnlockFestivalEffect(festival));

        harness.Queries.IsUnlocked(tradition).ShouldBeTrue();
        harness.Queries.IsUnlocked(festival).ShouldBeTrue();
        harness.Queries.IsUnlocked(harness.Content.IdOf<TraditionId>("common_hearth")).ShouldBeFalse();
        harness.Queries.IsUnlocked(harness.Content.IdOf<FestivalId>("midsummer_fair")).ShouldBeFalse();
    }

    [Theory]
    [InlineData(TerrainAccess.NarrowRivers)]
    [InlineData(TerrainAccess.WideRivers)]
    [InlineData(TerrainAccess.HillFarming)]
    [InlineData(TerrainAccess.MarshDrainage)]
    [InlineData(TerrainAccess.Highlands)]
    [InlineData(TerrainAccess.DeepWater)]
    [InlineData(TerrainAccess.Reefs)]
    public void ApplyEffects_UnlockTerrainAccess_OpensOnlyThatKindOfGround(TerrainAccess access)
    {
        var harness = ProgressionHarness.Create();

        harness.Apply(_tech, new UnlockTerrainAccessEffect(access));

        foreach (TerrainAccess other in Enum.GetValues<TerrainAccess>())
        {
            harness.Queries.HasAccess(other).ShouldBe(other == access, other.ToString());
        }
    }

    [Theory]
    [InlineData(FeatureKind.Expeditions)]
    [InlineData(FeatureKind.Coin)]
    [InlineData(FeatureKind.Visitors)]
    public void ApplyEffects_UnlockFeature_SwitchesOnlyThatFeatureOn(FeatureKind feature)
    {
        var harness = ProgressionHarness.Create();
        harness.Queries.HasFeature(feature).ShouldBeFalse();

        harness.Apply(_tech, new UnlockFeatureEffect(feature, null));

        harness.Queries.HasFeature(feature).ShouldBeTrue();
        foreach (FeatureKind other in new[] { FeatureKind.Expeditions, FeatureKind.Coin, FeatureKind.Visitors })
        {
            harness.Queries.HasFeature(other).ShouldBe(other == feature, other.ToString());
        }

        harness.Queries.HasFeature(FeatureKind.GreatWorks).ShouldBeFalse();
    }

    [Fact]
    public void ApplyEffects_GreatWorksFeature_IsPerType()
    {
        var harness = ProgressionHarness.Create();
        GreatWorkTypeId writing = harness.Content.IdOf<GreatWorkTypeId>("writing");
        GreatWorkTypeId relic = harness.Content.IdOf<GreatWorkTypeId>("relic");
        harness.Queries.HasFeature(FeatureKind.GreatWorks, writing).ShouldBeFalse();
        harness.Queries.HasFeature(FeatureKind.GreatWorks).ShouldBeFalse();

        harness.Apply(_tech, new UnlockFeatureEffect(FeatureKind.GreatWorks, writing));

        harness.Queries.HasFeature(FeatureKind.GreatWorks, writing).ShouldBeTrue();
        harness.Queries.HasFeature(FeatureKind.GreatWorks, relic).ShouldBeFalse();
        harness.Queries.HasFeature(FeatureKind.GreatWorks).ShouldBeTrue("without a type, any switched-on type counts");
        harness.Queries.HasFeature(FeatureKind.Coin, writing).ShouldBeFalse("the type is ignored for other features");
    }

    [Fact]
    public void ApplyEffects_GreatWorksFeatureWithoutAType_Throws()
    {
        var harness = ProgressionHarness.Create();

        Should.Throw<ArgumentException>(() => harness.Apply(_tech, new UnlockFeatureEffect(FeatureKind.GreatWorks, null)));
    }

    [Fact]
    public void ApplyEffects_TraditionSlot_IsRecordedPerSlotType()
    {
        var harness = ProgressionHarness.Create();

        harness.Apply(_tech, new TraditionSlotEffect(TraditionSlotType.Craft), new TraditionSlotEffect(TraditionSlotType.Craft), new TraditionSlotEffect(TraditionSlotType.Wild));

        harness.Queries.TraditionSlots(TraditionSlotType.Craft).ShouldBe(2);
        harness.Queries.TraditionSlots(TraditionSlotType.Wild).ShouldBe(1);
        harness.Queries.TraditionSlots(TraditionSlotType.Hearth).ShouldBe(0);
        harness.Queries.TraditionSlots(TraditionSlotType.Heritage).ShouldBe(0);
    }

    [Fact]
    public void ApplyEffects_Modifier_RegistersItWithItsSource()
    {
        var harness = ProgressionHarness.Create();

        harness.Apply(_tech, Fx.Modifier(Stat.Productivity, 20));

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(20);
        var listed = new List<ModifierContribution>();
        harness.Queries.ListModifiers(Stat.Productivity, ModifierContext.Island, listed);
        listed.ShouldBe([new ModifierContribution(_tech, 20, true)]);
        harness.Modifiers.Count.ShouldBe(1);
    }

    [Fact]
    public void ApplyEffects_GrantGoods_AddsThemToTheIslandPool()
    {
        var harness = ProgressionHarness.Create();
        GoodId timber = harness.Good("timber");
        GoodId stone = harness.Good("stone");
        harness.Fake.Queries.Storage.Set(timber, Amount.FromUnits(5));

        harness.Apply(_tech, new GrantGoodsEffect([new GoodAmount(timber, Amount.FromUnits(10)), new GoodAmount(stone, Amount.Parse("5.5"))]));

        harness.Fake.Queries.Storage.Stock(timber).ShouldBe(Amount.FromUnits(15));
        harness.Fake.Queries.Storage.Stock(stone).ShouldBe(Amount.Parse("5.5"));
    }

    [Fact]
    public void ApplyEffects_GrantPoints_CreditsTheLedger()
    {
        var harness = ProgressionHarness.Create();

        harness.Apply(_tech, new GrantPointsEffect([new PointAmount(PointKind.Knowledge, Amount.FromUnits(50)), new PointAmount(PointKind.Coin, Amount.Parse("2.5"))]));

        harness.Progression.Ledger.Peek(PointKind.Knowledge).ShouldBe(Amount.FromUnits(50));
        harness.Progression.Ledger.Peek(PointKind.Coin).ShouldBe(Amount.Parse("2.5"));
        harness.Progression.Ledger.Peek(PointKind.Culture).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void ApplyEffects_RevealCoastline_RecordsAFlagForExploration()
    {
        var harness = ProgressionHarness.Create();
        harness.Queries.CoastlineRevealed.ShouldBeFalse();

        harness.Apply(_tech, new RevealCoastlineEffect());

        harness.Queries.CoastlineRevealed.ShouldBeTrue();
    }

    [Fact]
    public void ApplyEffects_RequestFestival_ChangesNothingButTheAppliedSource()
    {
        var harness = ProgressionHarness.Create();
        var before = ProgressionHarness.Create();
        before.Apply(_tech);

        harness.Apply(_tech, new RequestFestivalEffect(harness.Content.IdOf<FestivalId>("harvest_fair"), 25));

        harness.Save().ShouldBe(before.Save());
    }

    [Fact]
    public void ApplyEffects_KitchenTech_AppliesEveryEffectKindOfTheContent()
    {
        var harness = ProgressionHarness.Create();
        GoodId timber = harness.Good("timber");

        harness.ApplyTech("kitchen_tech");

        harness.Queries.IsUnlocked(harness.Building("scribes_hall")).ShouldBeTrue();
        harness.Queries.IsUnlocked(harness.Content.IdOf<RecipeId>("bake_bread")).ShouldBeTrue();
        harness.Queries.IsUnlocked(harness.Tier("townsfolk")).ShouldBeTrue();
        harness.Queries.IsUnlocked(harness.Content.IdOf<TraditionId>("harvest_festival")).ShouldBeTrue();
        harness.Queries.IsUnlocked(harness.Content.IdOf<FestivalId>("harvest_fair")).ShouldBeTrue();
        harness.Queries.HasAccess(TerrainAccess.NarrowRivers).ShouldBeTrue();
        harness.Queries.HasFeature(FeatureKind.Coin).ShouldBeTrue();
        harness.Queries.HasFeature(FeatureKind.GreatWorks, harness.Content.IdOf<GreatWorkTypeId>("writing")).ShouldBeTrue();
        harness.Queries.TraditionSlots(TraditionSlotType.Craft).ShouldBe(2);
        harness.Modifiers.Count.ShouldBe(7);
        harness.Fake.Queries.Storage.Stock(timber).ShouldBe(Amount.FromUnits(10));
        harness.Progression.Ledger.Peek(PointKind.Knowledge).ShouldBe(Amount.FromUnits(50));
        harness.Queries.CoastlineRevealed.ShouldBeTrue();
    }

    [Fact]
    public void ApplyEffects_SameSourceTwice_IsANoOp()
    {
        var harness = ProgressionHarness.Create();
        GoodId timber = harness.Good("timber");
        ImmutableArray<Effect> effects = Fx.Of(
            new UnlockBuildingEffect(harness.Building("bakery")),
            new TraditionSlotEffect(TraditionSlotType.Hearth),
            Fx.Modifier(Stat.Productivity, 20),
            new GrantGoodsEffect([new GoodAmount(timber, Amount.FromUnits(10))]),
            new GrantPointsEffect([new PointAmount(PointKind.Culture, Amount.FromUnits(7))]));

        harness.Context.ProgressionService.ApplyEffects(_tech, effects);
        byte[] once = harness.Save();
        harness.Context.ProgressionService.ApplyEffects(_tech, effects);

        harness.Save().ShouldBe(once);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(20);
        harness.Queries.TraditionSlots(TraditionSlotType.Hearth).ShouldBe(1);
        harness.Fake.Queries.Storage.Stock(timber).ShouldBe(Amount.FromUnits(10));
        harness.Progression.Ledger.Peek(PointKind.Culture).ShouldBe(Amount.FromUnits(7));
    }

    [Fact]
    public void ApplyEffects_SameSourceAgainWithOtherEffects_IsStillANoOp()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_tech, new UnlockBuildingEffect(harness.Building("bakery")));

        harness.Apply(_tech, new UnlockBuildingEffect(harness.Building("windmill")));

        harness.Queries.IsUnlocked(harness.Building("windmill")).ShouldBeFalse();
    }

    [Fact]
    public void ApplyEffects_NoEffects_StillRecordsTheSource()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_tech);

        harness.Apply(_tech, new UnlockBuildingEffect(harness.Building("bakery")));

        harness.State.AppliedSources.ShouldBe([_tech]);
        harness.Queries.IsUnlocked(harness.Building("bakery")).ShouldBeFalse();
    }

    [Fact]
    public void ApplyEffects_TwoSourcesWithTheSameModifier_AddUp()
    {
        var harness = ProgressionHarness.Create();

        harness.Apply(_tech, Fx.Modifier(Stat.Productivity, 20));
        harness.Apply(_otherTech, Fx.Modifier(Stat.Productivity, 20));

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(40);
    }

    [Fact]
    public void ApplyEffects_SourcesOfDifferentKindsWithTheSameValue_AreDistinct()
    {
        var harness = ProgressionHarness.Create();

        harness.Apply(EffectSource.ForTech(new TechId(1)), Fx.Modifier(Stat.Productivity, 10));
        harness.Apply(EffectSource.ForCivic(new CivicId(1)), Fx.Modifier(Stat.Productivity, 10));
        harness.Apply(EffectSource.ForHappening(new HappeningId(1)), Fx.Modifier(Stat.Productivity, 10));

        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(30);
    }

    [Fact]
    public void ApplyEffects_SourcesInAnyOrder_AreKeptSortedByKindThenValue()
    {
        var harness = ProgressionHarness.Create();
        var civic1 = EffectSource.ForCivic(new CivicId(1));
        var tech5 = EffectSource.ForTech(new TechId(5));
        var tech2 = EffectSource.ForTech(new TechId(2));
        var building = EffectSource.ForBuilding(new EntityId(9));

        foreach (EffectSource source in new[] { building, tech5, civic1, tech2 })
        {
            harness.Apply(source);
        }

        harness.State.AppliedSources.ShouldBe([tech2, tech5, civic1, building]);
    }

    [Fact]
    public void ApplyEffects_SameUnlocksInAnyOrder_GiveTheSameBytes()
    {
        var first = ProgressionHarness.Create();
        var second = ProgressionHarness.Create();
        BuildingTypeId bakery = first.Building("bakery");
        BuildingTypeId windmill = first.Building("windmill");

        first.Apply(_tech, new UnlockBuildingEffect(bakery));
        first.Apply(_otherTech, new UnlockBuildingEffect(windmill));
        second.Apply(_otherTech, new UnlockBuildingEffect(windmill));
        second.Apply(_tech, new UnlockBuildingEffect(bakery));

        second.Save().ShouldBe(first.Save());
    }

    [Fact]
    public void ApplyEffects_DefaultArray_Throws()
    {
        var harness = ProgressionHarness.Create();

        Should.Throw<ArgumentException>(() => harness.Context.ProgressionService.ApplyEffects(_tech, default));
    }

    [Fact]
    public void ApplyEffects_UnlockEffectsWithoutAContext_Work()
    {
        // Only the goods grant needs the simulation context, so the rest of the applier works on a bare system.
        var state = new ProgressionState();
        var modifiers = new ModifiersSystem(new ModifiersState(), ProgressionPack.Content);
        var system = new ProgressionSystem(state, ProgressionPack.Content, modifiers);
        BuildingTypeId bakery = ProgressionPack.Content.IdOf<BuildingTypeId>("bakery");

        system.Service.ApplyEffects(_tech, [new UnlockBuildingEffect(bakery)]);

        system.Queries.IsUnlocked(bakery).ShouldBeTrue();
    }

    [Fact]
    public void ApplyEffects_GrantGoodsWithoutAContext_Throws()
    {
        var modifiers = new ModifiersSystem(new ModifiersState(), ProgressionPack.Content);
        var system = new ProgressionSystem(new ProgressionState(), ProgressionPack.Content, modifiers);
        GoodId timber = ProgressionPack.Content.IdOf<GoodId>("timber");

        Should.Throw<InvalidOperationException>(
            () => system.Service.ApplyEffects(_tech, [new GrantGoodsEffect([new GoodAmount(timber, Amount.FromUnits(1))])]));
    }

    [Fact]
    public void ApplyEffects_SeasonModifier_FollowsTheClock()
    {
        var harness = ProgressionHarness.Create();
        harness.Apply(_tech, Fx.Modifier(Stat.Productivity, 20, when: new SeasonCondition(Season.Autumn)));

        harness.SetSeason(Season.Spring);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(0);
        harness.SetSeason(Season.Autumn);
        harness.Queries.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(20);
    }
}
