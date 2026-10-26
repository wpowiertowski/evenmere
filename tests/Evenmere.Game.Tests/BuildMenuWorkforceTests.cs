using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.BuildMenu;
using Evenmere.Game.Ui.Common;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Game.Tests;

/// <summary>M1-31 and M1-37: each build-menu entry's workforce line names the free workers of its tier; when the tier
/// itself has none, it names the nearest higher tier that can fill in, and only warns when nobody can.</summary>
public sealed class BuildMenuWorkforceTests
{
    [Fact]
    public void FreeWorkers_ShowFreeCount()
    {
        BuildMenuViewModel.BuildEntry entry = EntryFor(supply: 9, demand: 3);
        entry.WorkforceText.ShouldBe("3 Settlers (6 free)");
        entry.WorkforceShort.ShouldBeFalse();
    }

    [Fact]
    public void NoFreeWorkers_ShowNoneFreeWithWarning()
    {
        // Neither the Settlers nor the Villagers have a free worker, so the tint stays.
        BuildMenuViewModel.BuildEntry entry = EntryFor(supply: 0, demand: 3);
        entry.WorkforceText.ShouldBe("3 Settlers (none free)");
        entry.WorkforceShort.ShouldBeTrue();
    }

    [Fact]
    public void HigherTierFree_NamesHelperWithoutWarning()
    {
        BuildMenuViewModel.BuildEntry entry = EntryFor(supply: 0, demand: 3, higherSupply: 10, higherDemand: 2);
        entry.WorkforceText.ShouldBe("3 Settlers (none free; Villagers can fill in)");
        entry.WorkforceShort.ShouldBeFalse();
    }

    [Fact]
    public void HigherTierBusy_NoHelperWithWarning()
    {
        BuildMenuViewModel.BuildEntry entry = EntryFor(supply: 0, demand: 3, higherSupply: 4, higherDemand: 4);
        entry.WorkforceText.ShouldBe("3 Settlers (none free)");
        entry.WorkforceShort.ShouldBeTrue();
    }

    [Fact]
    public void OwnFree_IgnoresHigherTier()
    {
        // The tier has a free worker of its own: the line counts it even though Villagers are idle too.
        BuildMenuViewModel.BuildEntry entry = EntryFor(supply: 9, demand: 3, higherSupply: 10, higherDemand: 2);
        entry.WorkforceText.ShouldBe("3 Settlers (6 free)");
        entry.WorkforceShort.ShouldBeFalse();
    }

    [Fact]
    public void HouseWithoutWorkforce_HasNoWorkforceLine()
    {
        BuildMenuViewModel model = ModelFor(supply: 9, demand: 3);
        BuildMenuViewModel.BuildEntry house = model.Tabs
            .SelectMany(static tab => tab.Entries)
            .First(static entry => entry.Key == "settler_house");
        house.WorkforceText.ShouldBeNull();
        house.WorkforceShort.ShouldBeFalse();
    }

    private static BuildMenuViewModel.BuildEntry EntryFor(int supply, int demand, int higherSupply = 0, int higherDemand = 0) =>
        ModelFor(supply, demand, higherSupply, higherDemand).Tabs
            .SelectMany(static tab => tab.Entries)
            .First(static entry => entry.Key == "woodcutters_hut");

    private static BuildMenuViewModel ModelFor(int supply, int demand, int higherSupply = 0, int higherDemand = 0)
    {
        ContentDb content = TestContent.Db;
        TierId settlers = content.TiersByOrder[0];
        TierId villagers = content.TiersByOrder[1];
        var model = new BuildMenuViewModel(
            content,
            new UiStrings(TestLocales.Table()),
            new AllUnlockedProgression(),
            new FakeWorkforce(
                new Dictionary<TierId, int> { [settlers] = supply, [villagers] = higherSupply },
                new Dictionary<TierId, int> { [settlers] = demand, [villagers] = higherDemand }),
            new Dictionary<string, BuildMenuViewModel.BuildingVisualRef>(),
            UnlockIndex.Build(content));
        model.Advance(10.0);
        return model;
    }

    private sealed class FakeWorkforce(
        Dictionary<TierId, int> supply,
        Dictionary<TierId, int> demand) : IWorkforceQueries
    {
        public int Supply(TierId tier) => supply.TryGetValue(tier, out int count) ? count : 0;

        public int Demand(TierId tier) => demand.TryGetValue(tier, out int count) ? count : 0;

        public int PlannedDemand(TierId tier) => 0;

        public int WorkingBelowTier(TierId tier) => 0;

        public int FilledFromAbove(TierId tier) => 0;

        public int AllocatedWorkers(EntityId building) => 0;

        public int SubstitutedWorkers(EntityId building) => 0;

        public TierId SubstituteSourceTier(EntityId building) => default;

        public Ratio FillOf(EntityId building) => Ratio.One;

        public bool IsHighPriority(EntityId building) => false;
    }

    private sealed class AllUnlockedProgression : IProgressionQueries
    {
        public bool IsUnlocked(BuildingTypeId building) => true;

        public bool IsUnlocked(RecipeId recipe) => true;

        public bool IsUnlocked(TierId tier) => true;

        public bool HasAccess(TerrainAccess access) => true;

        public bool HasFeature(FeatureKind feature, GreatWorkTypeId? greatWorkType = null) => false;

        public EraId CurrentEra => EraId.From(0);

        public int BonusPercent(Stat stat, in ModifierContext context) => 0;

        public void ListModifiers(Stat stat, in ModifierContext context, List<ModifierContribution> contributions)
        {
        }

        public long Counter(CounterKey key) => 0;

        public bool Evaluate(Condition condition) => true;

        bool IProgressionQueries.IsTraditionUnlocked(TraditionId tradition) => true;

        bool IProgressionQueries.IsFestivalUnlocked(FestivalId festival) => true;

        int IProgressionQueries.TraditionSlotCount(TraditionSlotType slot) => 1;
    }
}
