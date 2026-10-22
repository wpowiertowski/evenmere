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

/// <summary>M1-31: each build-menu entry's workforce line names the free workers of its tier, or "none free" in the warning tint when the tier is fully staffed.</summary>
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
        BuildMenuViewModel.BuildEntry entry = EntryFor(supply: 0, demand: 3);
        entry.WorkforceText.ShouldBe("3 Settlers (none free)");
        entry.WorkforceShort.ShouldBeTrue();
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

    private static BuildMenuViewModel.BuildEntry EntryFor(int supply, int demand) => ModelFor(supply, demand).Tabs
        .SelectMany(static tab => tab.Entries)
        .First(static entry => entry.Key == "woodcutters_hut");

    private static BuildMenuViewModel ModelFor(int supply, int demand)
    {
        ContentDb content = TestContent.Db;
        TierId settlers = content.TiersByOrder[0];
        var model = new BuildMenuViewModel(
            content,
            new UiStrings(TestLocales.Table()),
            new AllUnlockedProgression(),
            new FakeWorkforce(new Dictionary<TierId, int> { [settlers] = supply }, new Dictionary<TierId, int> { [settlers] = demand }),
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
    }
}
