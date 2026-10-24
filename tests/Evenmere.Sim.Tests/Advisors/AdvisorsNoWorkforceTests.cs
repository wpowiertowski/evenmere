using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Advisors;

/// <summary>ADR-0016: NoWorkforce fires only for jobs that stay unfilled after stepping down.</summary>
public sealed class AdvisorsNoWorkforceTests
{
    [Fact]
    public void NoWorkforce_SubstitutesCoverTheGap_StaysSilent()
    {
        var h = AdvisorsHarness.Create();
        TierId settlers = h.Tier("settlers");
        h.Sim.Queries.Workforce.SetSupply(settlers, 2);
        h.Sim.Queries.Workforce.SetDemand(settlers, 5);
        h.Sim.Queries.Workforce.SetFilledFromAbove(settlers, 3);

        // Two bad days would raise the note, but the three substitutes cover the gap.
        h.Run();
        h.AdvanceDays(1);

        h.Active().ShouldBeEmpty();
        h.Events().ShouldBeEmpty();
    }

    [Fact]
    public void NoWorkforce_PartialCover_StillRaises()
    {
        var h = AdvisorsHarness.Create();
        TierId settlers = h.Tier("settlers");
        h.Sim.Queries.Workforce.SetSupply(settlers, 2);
        h.Sim.Queries.Workforce.SetDemand(settlers, 5);
        h.Sim.Queries.Workforce.SetFilledFromAbove(settlers, 2);

        // One job stays unfilled after stepping down: the note raises on the second day.
        h.Run();
        h.Active().ShouldBeEmpty();
        h.AdvanceDays(1);

        AdviceView[] active = h.Active();
        active.ShouldHaveSingleItem();
        active[0].Kind.ShouldBe(AdviceKind.NoWorkforce);
        active[0].Subject.ShouldBe(SubjectLink.ForTier(settlers));

        // Covering the last job clears it on the next evaluation.
        h.Sim.Queries.Workforce.SetFilledFromAbove(settlers, 3);
        h.AdvanceDays(1);
        h.Active().ShouldBeEmpty();
    }
}
