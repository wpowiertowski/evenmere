using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Happenings;
using Evenmere.Sim.Systems.Happenings.Commands;

namespace Evenmere.Sim.Tests.Happenings;

/// <summary>Answering a pending happening: every validation rule, and what choosing applies.</summary>
public sealed class ChooseHappeningOptionTests
{
    private static HappeningsHarness PendingFeast()
    {
        HappeningsHarness h = HappeningsHarness.Create().EligibleOnly("test_feast");
        h.Run();
        h.Events();
        return h;
    }

    [Fact]
    public void Choose_UnknownHappening_IsRejected()
    {
        var h = HappeningsHarness.Create();
        var unknown = HappeningId.From(checked((ushort)h.Content.Happenings.Count));

        h.Send(new ChooseHappeningOption(unknown, 0));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.Invalid);
        h.State.Pending.ShouldBeNull();
    }

    [Fact]
    public void Choose_NothingPending_IsRejected()
    {
        var h = HappeningsHarness.Create();

        h.Send(new ChooseHappeningOption(h.Happening("test_feast"), 0));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.Invalid);
    }

    [Fact]
    public void Choose_OtherHappeningThanPending_IsRejected()
    {
        HappeningsHarness h = PendingFeast();

        h.Send(new ChooseHappeningOption(h.Happening("test_storm"), 0));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.Invalid);
        h.State.Pending.ShouldBe(new PendingHappening(h.Happening("test_feast"), 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Choose_OptionOutOfRange_IsRejected(int option)
    {
        HappeningsHarness h = PendingFeast();

        h.Send(new ChooseHappeningOption(h.Happening("test_feast"), option));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.Invalid);
        h.State.Pending.ShouldBe(new PendingHappening(h.Happening("test_feast"), 0));
    }

    [Fact]
    public void Choose_GrantOption_ResolvesThroughApplyEffectsClearsPendingStartsCooldownAndEmitsResolved()
    {
        HappeningsHarness h = PendingFeast();

        h.Send(new ChooseHappeningOption(h.Happening("test_feast"), 0));

        h.Events().ShouldHaveSingleItem().ShouldBe(new HappeningResolved(0, h.Happening("test_feast"), 0));
        h.State.Pending.ShouldBeNull();
        // The fake service records instead of applying; the grant landing in the pool is covered by the
        // full-simulation integration tests. Here the option's effects must reach the service under the
        // happening's source.
        var source = EffectSource.ForHappening(h.Happening("test_feast"));
        h.Sim.Services.ProgressionService.Calls.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            call => call.Source.ShouldBe(source),
            call => call.Effects.ShouldHaveSingleItem().ShouldBeOfType<GrantGoodsEffect>().ShouldSatisfyAllConditions(
                e => e.Goods.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                    g => g.Good.ShouldBe(h.Good("fish")),
                    g => g.Amount.ShouldBe(Amount.FromUnits(10)))));
        h.State.CooldownUntil[h.Happening("test_feast").Value].ShouldBe(30 * 120L);
        h.Queries.IsOnCooldown(h.Happening("test_feast")).ShouldBeTrue();
        h.State.TempModifiers.ShouldBeEmpty();
    }

    [Fact]
    public void Choose_ModifierOption_AppliesWithHappeningSourceAndRecordsExpiry()
    {
        HappeningsHarness h = PendingFeast();

        h.Send(new ChooseHappeningOption(h.Happening("test_feast"), 1));

        var source = EffectSource.ForHappening(h.Happening("test_feast"));
        h.Sim.Services.ProgressionService.Calls.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            call => call.Source.ShouldBe(source),
            call => call.Effects.ShouldHaveSingleItem().ShouldBeOfType<ModifierEffect>().ShouldSatisfyAllConditions(
                e => e.Stat.ShouldBe(Stat.Productivity),
                e => e.Percent.ShouldBe(10)));
        h.State.TempModifiers.ShouldHaveSingleItem().ShouldBe(new TempModifierRecord(h.Happening("test_feast"), 10 * 120L));
    }

    [Fact]
    public void Choose_FestivalRequest_AppliesAFestivalCostDiscountWithHappeningSource()
    {
        HappeningsHarness h = HappeningsHarness.Create().EligibleOnly("test_request");
        h.Run();
        h.Events();

        h.Send(new ChooseHappeningOption(h.Happening("test_request"), 0));

        var source = EffectSource.ForHappening(h.Happening("test_request"));
        h.Sim.Services.ProgressionService.Calls.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            call => call.Source.ShouldBe(source),
            call => call.Effects.ShouldHaveSingleItem().ShouldBeOfType<ModifierEffect>().ShouldSatisfyAllConditions(
                e => e.Stat.ShouldBe(Stat.FestivalCost),
                e => e.Percent.ShouldBe(-25),
                e => e.Target.ShouldBeOfType<IslandTarget>()));
        h.Events().ShouldHaveSingleItem().ShouldBe(new HappeningResolved(0, h.Happening("test_request"), 0));
    }

    [Fact]
    public void Choose_RemovesTheSourceFirstSoARepeatOccurrenceAppliesAgain()
    {
        HappeningsHarness h = PendingFeast();

        h.Send(new ChooseHappeningOption(h.Happening("test_feast"), 0));

        h.Sim.Services.ProgressionService.RemovedSources.ShouldContain(EffectSource.ForHappening(h.Happening("test_feast")));
        h.Sim.Services.ProgressionService.Calls.ShouldHaveSingleItem();
    }
}
