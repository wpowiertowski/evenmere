using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Culture.Festivals;
using Evenmere.Sim.Systems.Culture.Traditions;
using Evenmere.Sim.Systems.Culture.Traditions.Commands;
using Evenmere.Tools.Balance.EraOne;

namespace Evenmere.Tools.Tests.Balance;

/// <summary>Plans tradition slottings from the unlocked set and the granted slots, highest priority first.</summary>
public sealed class TraditionHeuristicsTests
{
    [Fact]
    public void PlanSlottings_EmptySlots_PlansNothing()
    {
        var queries = new StubCulture([], [], null);

        TraditionHeuristics.PlanSlottings(BotWorld.Content, queries).ShouldBeEmpty();
    }

    [Fact]
    public void PlanSlottings_OneHearthSlot_SlotsKnowledgeFirst()
    {
        var queries = new StubCulture(
            [new TraditionSlotView(TraditionSlotType.Hearth, 0, null)],
            [Id("common_hearth"), Id("fireside_tales")],
            null);

        List<SetTradition> planned = TraditionHeuristics.PlanSlottings(BotWorld.Content, queries);

        planned.Count.ShouldBe(1);
        planned[0].Slot.ShouldBe(TraditionSlotType.Hearth);
        planned[0].Tradition.ShouldBe(Id("fireside_tales"));
    }

    [Fact]
    public void PlanSlottings_BestAlreadySlotted_PlansNothing()
    {
        var queries = new StubCulture(
            [new TraditionSlotView(TraditionSlotType.Craft, 0, Id("many_hands"))],
            [Id("many_hands"), Id("village_crafts")],
            null);

        TraditionHeuristics.PlanSlottings(BotWorld.Content, queries).ShouldBeEmpty();
    }

    [Fact]
    public void PlanSlottings_LockedCard_SlotsNextBest()
    {
        // Only the capacity card is unlocked: the knowledge card is skipped, not planned.
        var queries = new StubCulture(
            [new TraditionSlotView(TraditionSlotType.Hearth, 0, null)],
            [Id("common_hearth")],
            null);

        List<SetTradition> planned = TraditionHeuristics.PlanSlottings(BotWorld.Content, queries);

        planned.Count.ShouldBe(1);
        planned[0].Tradition.ShouldBe(Id("common_hearth"));
    }

    private static TraditionId Id(string name) => BotWorld.Content.Traditions.IdOf(name);

    private sealed class StubCulture(
        ImmutableArray<TraditionSlotView> slots,
        ImmutableArray<TraditionId> unlocked,
        TraditionId? style) : ICultureQueries
    {
        public CivicId? Target => null;

        public ImmutableArray<CivicId> Queue => [];

        public Amount Progress(CivicId civic) => Amount.Zero;

        public Amount Cost(CivicId civic) => Amount.Zero;

        public bool IsDone(CivicId civic) => false;

        public bool IsAvailable(CivicId civic) => false;

        public Amount PointsPerDay => Amount.Zero;

        public bool IsInspired(CivicId civic) => false;

        public ImmutableArray<TraditionSlotView> Slots() => slots;

        public ImmutableArray<TraditionId> UnlockedTraditions() => unlocked;

        public TraditionId? ActiveStyle() => style;

        public bool IsFestivalRunning() => false;

        public FestivalRun? ActiveFestival() => null;

        public FestivalRunView? ActiveFestivalView() => null;

        public int FestivalsHeldInYear(FestivalId festival, int year) => 0;
    }
}
