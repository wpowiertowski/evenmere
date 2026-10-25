using Evenmere.Content.Definitions;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Culture.Traditions;
using Evenmere.Sim.Systems.Culture.Traditions.Commands;

namespace Evenmere.Sim.Tests.Culture.Traditions;

/// <summary>Slotting tradition cards through the full simulation: validation, events, queries and the bonus change.</summary>
public sealed class SetTraditionTests
{
    [Fact]
    public void Slot_UnlockedCard_ChangesBonusAndEmitsEvent()
    {
        var h = TraditionsHarness.Create();
        h.Arrange(["common_hearth"], (TraditionSlotType.Hearth, 1));
        h.Events();
        var settlers = ModifierContext.ForTier(h.Tier("settlers"));
        h.Progression.BonusPercent(Stat.Capacity, settlers).ShouldBe(0);

        h.Send(new SetTradition(TraditionSlotType.Hearth, 0, h.Tradition("common_hearth")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<TraditionChanged>()
            .ShouldSatisfyAllConditions(
                e => e.Slot.ShouldBe(TraditionSlotType.Hearth),
                e => e.Index.ShouldBe(0),
                e => e.Old.ShouldBeNull(),
                e => e.New.ShouldBe(h.Tradition("common_hearth")));
        h.Progression.BonusPercent(Stat.Capacity, settlers).ShouldBe(15);
        h.Culture.Slots().ShouldBe([new TraditionSlotView(TraditionSlotType.Hearth, 0, h.Tradition("common_hearth"))]);
        h.Culture.UnlockedTraditions().ShouldContain(h.Tradition("common_hearth"));
        h.Culture.ActiveStyle().ShouldBeNull();
    }

    [Fact]
    public void Slot_LockedCard_IsRejected()
    {
        var h = TraditionsHarness.Create();
        h.Arrange([], (TraditionSlotType.Hearth, 1));
        h.Events();

        h.Send(new SetTradition(TraditionSlotType.Hearth, 0, h.Tradition("common_hearth")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.TraditionNotUnlocked);
        h.Culture.Slots().ShouldBe([new TraditionSlotView(TraditionSlotType.Hearth, 0, null)]);
    }

    [Fact]
    public void Slot_MissingSlot_IsRejected()
    {
        var h = TraditionsHarness.Create();
        h.Arrange(["common_hearth"], (TraditionSlotType.Hearth, 1));
        h.Events();

        h.Send(new SetTradition(TraditionSlotType.Hearth, 3, h.Tradition("common_hearth")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.SlotMismatch);
    }

    [Fact]
    public void Slot_WildCardInTypedSlot_IsRejected()
    {
        var h = TraditionsHarness.Create();
        h.Arrange(["test_wild_song"], (TraditionSlotType.Hearth, 1));
        h.Events();

        h.Send(new SetTradition(TraditionSlotType.Hearth, 0, h.Tradition("test_wild_song")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.SlotMismatch);
        h.Progression.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(0);
    }

    [Fact]
    public void Slot_TypedCardInWildSlot_IsAccepted()
    {
        var h = TraditionsHarness.Create();
        h.Arrange(["common_hearth"], (TraditionSlotType.Wild, 1));
        h.Events();

        h.Send(new SetTradition(TraditionSlotType.Wild, 0, h.Tradition("common_hearth")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<TraditionChanged>();
        h.Progression.BonusPercent(Stat.Capacity, ModifierContext.ForTier(h.Tier("settlers"))).ShouldBe(15);
    }

    [Fact]
    public void Slot_CardSlottedElsewhere_IsRejected()
    {
        var h = TraditionsHarness.Create();
        h.Arrange(["common_hearth"], (TraditionSlotType.Hearth, 2));
        h.Send(new SetTradition(TraditionSlotType.Hearth, 0, h.Tradition("common_hearth")));
        h.Events();

        h.Send(new SetTradition(TraditionSlotType.Hearth, 1, h.Tradition("common_hearth")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.AlreadySlotted);
        h.Culture.Slots().ShouldBe([
            new TraditionSlotView(TraditionSlotType.Hearth, 0, h.Tradition("common_hearth")),
            new TraditionSlotView(TraditionSlotType.Hearth, 1, null),
        ]);
    }

    [Fact]
    public void Slot_SecondStyle_IsRejected()
    {
        var h = TraditionsHarness.Create();
        h.Arrange(["test_style_a", "test_style_b"], (TraditionSlotType.Heritage, 2));
        h.Send(new SetTradition(TraditionSlotType.Heritage, 0, h.Tradition("test_style_a")));
        h.Events();

        h.Send(new SetTradition(TraditionSlotType.Heritage, 1, h.Tradition("test_style_b")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.StyleAlreadySlotted);
        h.Culture.ActiveStyle().ShouldBe(h.Tradition("test_style_a"));
    }

    [Fact]
    public void Swap_Cards_ChangesBonusTheNextTick()
    {
        var h = TraditionsHarness.Create();
        h.Arrange(["test_style_a", "test_style_b"], (TraditionSlotType.Heritage, 1));
        h.Send(new SetTradition(TraditionSlotType.Heritage, 0, h.Tradition("test_style_a")));
        h.Events();
        h.Progression.BonusPercent(Stat.Attractiveness, ModifierContext.Island).ShouldBe(5);

        h.Send(new SetTradition(TraditionSlotType.Heritage, 0, h.Tradition("test_style_b")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<TraditionChanged>()
            .ShouldSatisfyAllConditions(
                e => e.Old.ShouldBe(h.Tradition("test_style_a")),
                e => e.New.ShouldBe(h.Tradition("test_style_b")));
        h.Progression.BonusPercent(Stat.Attractiveness, ModifierContext.Island).ShouldBe(7);
        h.Culture.ActiveStyle().ShouldBe(h.Tradition("test_style_b"));
    }

    [Fact]
    public void Unslot_Card_RemovesBonusAndEmitsEvent()
    {
        var h = TraditionsHarness.Create();
        h.Arrange(["common_hearth"], (TraditionSlotType.Hearth, 1));
        h.Send(new SetTradition(TraditionSlotType.Hearth, 0, h.Tradition("common_hearth")));
        h.Events();

        h.Send(new SetTradition(TraditionSlotType.Hearth, 0, null));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<TraditionChanged>()
            .ShouldSatisfyAllConditions(
                e => e.Old.ShouldBe(h.Tradition("common_hearth")),
                e => e.New.ShouldBeNull());
        h.Progression.BonusPercent(Stat.Capacity, ModifierContext.ForTier(h.Tier("settlers"))).ShouldBe(0);
        h.Culture.Slots().ShouldBe([new TraditionSlotView(TraditionSlotType.Hearth, 0, null)]);
    }

    [Fact]
    public void Slot_SameCardInSameSlot_IsSilentNoOp()
    {
        var h = TraditionsHarness.Create();
        h.Arrange(["common_hearth"], (TraditionSlotType.Hearth, 1));
        h.Send(new SetTradition(TraditionSlotType.Hearth, 0, h.Tradition("common_hearth")));
        h.Events();

        h.Send(new SetTradition(TraditionSlotType.Hearth, 0, h.Tradition("common_hearth")));

        h.Events().ShouldBeEmpty();
        h.Progression.BonusPercent(Stat.Capacity, ModifierContext.ForTier(h.Tier("settlers"))).ShouldBe(15);
    }

    [Fact]
    public void Slots_WithoutCards_GroupsGrantedSlotsByType()
    {
        var h = TraditionsHarness.Create();
        h.Arrange([], (TraditionSlotType.Hearth, 2), (TraditionSlotType.Wild, 1));

        h.Culture.Slots().ShouldBe([
            new TraditionSlotView(TraditionSlotType.Hearth, 0, null),
            new TraditionSlotView(TraditionSlotType.Hearth, 1, null),
            new TraditionSlotView(TraditionSlotType.Wild, 0, null),
        ]);
        h.Culture.UnlockedTraditions().ShouldBeEmpty();
        h.Culture.ActiveStyle().ShouldBeNull();
    }
}
