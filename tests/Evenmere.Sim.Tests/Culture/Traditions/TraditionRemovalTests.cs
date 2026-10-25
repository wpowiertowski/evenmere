using Evenmere.Content.Definitions;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Culture.Traditions.Commands;

namespace Evenmere.Sim.Tests.Culture.Traditions;

/// <summary>Unslotting removes exactly the unslotted card's modifiers, and slotted cards survive a save round trip.</summary>
public sealed class TraditionRemovalTests
{
    [Fact]
    public void Unslot_RemovesOnlyThatSourcesModifiers()
    {
        var h = TraditionsHarness.Create();
        h.Arrange(
            ["common_hearth", "test_wild_song"],
            (TraditionSlotType.Hearth, 1),
            (TraditionSlotType.Wild, 1));
        h.Send(new SetTradition(TraditionSlotType.Hearth, 0, h.Tradition("common_hearth")));
        h.Send(new SetTradition(TraditionSlotType.Wild, 0, h.Tradition("test_wild_song")));
        h.Events();

        h.Send(new SetTradition(TraditionSlotType.Hearth, 0, null));

        var settlers = ModifierContext.ForTier(h.Tier("settlers"));
        h.Progression.BonusPercent(Stat.Capacity, settlers).ShouldBe(0);
        h.Progression.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(10);

        List<ModifierContribution> listed = [];
        h.Progression.ListModifiers(Stat.Capacity, settlers, listed);
        listed.ShouldBeEmpty();
        h.Progression.ListModifiers(Stat.Productivity, ModifierContext.Island, listed);
        listed.Count.ShouldBe(1);
        listed[0].Source.ShouldBe(EffectSource.ForTradition(h.Tradition("test_wild_song")));
        listed[0].Percent.ShouldBe(10);

        // The source record is gone too, so the card can be slotted again.
        h.Send(new SetTradition(TraditionSlotType.Hearth, 0, h.Tradition("common_hearth")));
        h.Progression.BonusPercent(Stat.Capacity, settlers).ShouldBe(15);
    }

    [Fact]
    public void SlottedCards_SaveRoundTrip_ContinuesDeterministically()
    {
        var h = TraditionsHarness.Create();
        h.Arrange(
            ["common_hearth", "test_wild_song"],
            (TraditionSlotType.Hearth, 1),
            (TraditionSlotType.Wild, 1));
        h.Send(new SetTradition(TraditionSlotType.Hearth, 0, h.Tradition("common_hearth")));
        h.Send(new SetTradition(TraditionSlotType.Wild, 0, h.Tradition("test_wild_song")));
        h.Events();

        var reloaded = TraditionsHarness.Reload(h);
        reloaded.Hash().ShouldBe(h.Hash());

        // Removal works from the rebuilt index, and both worlds stay in lockstep.
        reloaded.Send(new SetTradition(TraditionSlotType.Hearth, 0, null));
        h.Send(new SetTradition(TraditionSlotType.Hearth, 0, null));
        reloaded.Hash().ShouldBe(h.Hash());
        reloaded.Progression.BonusPercent(Stat.Capacity, ModifierContext.ForTier(reloaded.Tier("settlers"))).ShouldBe(0);
        reloaded.Progression.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(10);
    }
}
