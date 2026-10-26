using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Culture;
using Evenmere.Game.Ui.Progress;
using Evenmere.Game.Ui.Research;
using Evenmere.Sim;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Systems.Culture.Festivals;
using Evenmere.Sim.Systems.Culture.Traditions;

namespace Evenmere.Game.Tests;

/// <summary>
/// The civics graph behind the culture panel: card states, unlock lines and layout inputs from
/// <see cref="CivicGraphBuilder"/> and <see cref="CultureViewModel"/>, over the real slice content.
/// </summary>
public sealed class CivicGraphTests
{
    [Fact]
    public void BuildGraph_MapsCivicState()
    {
        (CultureViewModel model, CivicId civic, string name) = ModelWith(available: true);
        var strings = new UiStrings(TestLocales.Table());

        TreeGraph graph = model.BuildGraph(static _ => null);
        int at = NodeIndex(graph, name);

        graph.Nodes.Count.ShouldBe(model.Snapshot.Rows.Count);
        graph.Layout.Count.ShouldBe(graph.Nodes.Count);
        graph.Nodes[at].State.ShouldBe(GraphNodeState.Available);
        graph.Nodes[at].Gateway.ShouldBeFalse();
        graph.Nodes[at].Unlocks.Count.ShouldBe(2);
        graph.Nodes[at].Unlocks.ShouldContain(u => u.Text == strings.Get("building.shrine.name"));
        graph.Nodes[at].Unlocks.ShouldContain(u => u.Text == strings.Get("building.communal_oven.name"));
        graph.Layout[at].Requires.ShouldBeEmpty();
        model.GraphCount.ShouldBe(graph.Nodes.Count);
        model.GraphId(at).ShouldBe(civic);
    }

    [Fact]
    public void BuildGraph_LockedCivicShowsLocked()
    {
        (CultureViewModel model, CivicId _, string name) = ModelWith(available: false);

        TreeGraph graph = model.BuildGraph(static _ => null);

        graph.Nodes[NodeIndex(graph, name)].State.ShouldBe(GraphNodeState.Locked);
    }

    [Fact]
    public void BuildGraph_AdoptedCivicShowsAdopted()
    {
        (CultureViewModel model, CivicId _, string name) = ModelWith(available: false, done: true);

        TreeGraph graph = model.BuildGraph(static _ => null);
        int at = NodeIndex(graph, name);

        graph.Nodes[at].State.ShouldBe(GraphNodeState.Done);
        graph.Nodes[at].StateText.ShouldBe("Adopted");
    }

    [Fact]
    public void BuildGraph_InspiredCivicShowsBadge()
    {
        (CultureViewModel model, CivicId _, string name) = ModelWith(available: true, inspired: true);
        var strings = new UiStrings(TestLocales.Table());

        TreeGraph graph = model.BuildGraph(static _ => null);
        int at = NodeIndex(graph, name);

        graph.Nodes[at].Inspired.ShouldBeTrue();
        graph.Nodes[at].InspiredBadge.ShouldBe(strings.Get("ui.tree.row.inspired"));
    }

    [Fact]
    public void DescribeEffects_TraditionSlot()
    {
        ContentDb content = TestContent.Db;
        var strings = new UiStrings(TestLocales.Table());

        IReadOnlyList<GraphUnlock> unlocks = CivicGraphBuilder.DescribeEffects(
            content,
            strings,
            [new TraditionSlotEffect(TraditionSlotType.Hearth)],
            static _ => null);

        unlocks.Count.ShouldBe(1);
        unlocks[0].Text.ShouldBe(strings.Get("ui.traditions.slot.hearth"));
    }

    [Fact]
    public void DescribeEffects_Modifier()
    {
        ContentDb content = TestContent.Db;
        var strings = new UiStrings(TestLocales.Table());

        IReadOnlyList<GraphUnlock> unlocks = CivicGraphBuilder.DescribeEffects(
            content,
            strings,
            [new ModifierEffect(Stat.Productivity, new IslandTarget(), 20, null)],
            static _ => null);

        unlocks.Count.ShouldBe(1);
        unlocks[0].Text.ShouldBe("+20% productivity");
    }

    [Fact]
    public void SlotName_ComesFromLocale()
    {
        var strings = new UiStrings(TestLocales.Table());

        CivicGraphBuilder.SlotName(strings, TraditionSlotType.Wild)
            .ShouldBe(strings.Get("ui.traditions.slot.wild"));
    }

    [Fact]
    public void TargetCommand_SetsCivicTarget()
    {
        (CultureViewModel model, CivicId civic, string _) = ModelWith(available: true);

        Command command = model.TargetCommand(civic);

        command.ShouldBeOfType<SetCivicTarget>();
    }

    private static (CultureViewModel Model, CivicId Civic, string Name) ModelWith(bool available, bool inspired = false, bool done = false)
    {
        ContentDb content = TestContent.Db;
        var strings = new UiStrings(TestLocales.Table());
        CivicId civic = content.IdOf<CivicId>("hearth_and_shrine");
        var culture = new StubCulture();
        culture.SetCivic(civic, Amount.FromUnits(20), Amount.Zero, available: available, inspired: inspired, done: done);
        var progression = new StubProgression(content.Eras.IdOf("founding"));
        var model = new CultureViewModel(content, strings, culture, progression);
        model.Advance(10.0);
        model.Snapshot.Rows.ShouldContain(row => row.Id.Equals(civic));
        return (model, civic, strings.Get("civic.hearth_and_shrine.name"));
    }

    private static int NodeIndex(TreeGraph graph, string name)
    {
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            if (graph.Nodes[i].Name == name)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"The graph has no card named '{name}'. " +
            "The civic under test must keep its content name.");
    }

    private sealed class StubCulture : ICultureQueries
    {
        private readonly Dictionary<CivicId, (Amount Cost, bool Available, bool Inspired, bool Done)> _civics = new();

        public void SetCivic(CivicId civic, Amount cost, Amount progress, bool available, bool inspired, bool done) =>
            _civics[civic] = (cost, available, inspired, done);

        public CivicId? Target => null;
        public ImmutableArray<CivicId> Queue => [];
        public Amount PointsPerDay => Amount.FromUnits(1);
        public Amount Progress(CivicId civic) => Amount.Zero;
        public Amount Cost(CivicId civic) => _civics.TryGetValue(civic, out (Amount Cost, bool Available, bool Inspired, bool Done) found) ? found.Cost : Amount.Zero;
        public bool IsDone(CivicId civic) => _civics.TryGetValue(civic, out (Amount Cost, bool Available, bool Inspired, bool Done) found) && found.Done;
        public bool IsAvailable(CivicId civic) => _civics.TryGetValue(civic, out (Amount Cost, bool Available, bool Inspired, bool Done) found) && found.Available;
        public bool IsInspired(CivicId civic) => _civics.TryGetValue(civic, out (Amount Cost, bool Available, bool Inspired, bool Done) found) && found.Inspired;
        public ImmutableArray<TraditionSlotView> Slots() => [];
        public ImmutableArray<TraditionId> UnlockedTraditions() => [];
        public TraditionId? ActiveStyle() => null;
        public bool IsFestivalRunning() => false;
        public FestivalRun? ActiveFestival() => null;
        public int FestivalsHeldInYear(FestivalId festival, int year) => 0;
    }

    private sealed class StubProgression(EraId era) : IProgressionQueries
    {
        public bool IsUnlocked(BuildingTypeId building) => true;
        public bool IsUnlocked(RecipeId recipe) => true;
        public bool IsUnlocked(TierId tier) => true;
        public bool HasAccess(TerrainAccess access) => true;
        public bool HasFeature(FeatureKind feature, GreatWorkTypeId? greatWorkType = null) => true;
        public EraId CurrentEra => era;
        public int BonusPercent(Stat stat, in ModifierContext context) => 0;
        public void ListModifiers(Stat stat, in ModifierContext context, List<ModifierContribution> contributions) { }
        public long Counter(CounterKey key) => 0;
        public bool Evaluate(Condition condition) => false;
        public bool IsTraditionUnlocked(TraditionId tradition) => false;
        public bool IsFestivalUnlocked(FestivalId festival) => false;
        public int TraditionSlotCount(TraditionSlotType slot) => 0;
        public GatewayStatus GatewayStatus(EraId query) => new(false, false);
    }
}
