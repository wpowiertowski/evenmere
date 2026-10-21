using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Systems.Culture.Commands;

namespace Evenmere.Sim.Tests.Culture;

public sealed class CultureAccrualTests
{
    [Fact]
    public void Accrue_FortyCultureAtFourADay_CompletesOnDayTen()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        h.Send(new SetCivicTarget(custom));
        h.Events();

        for (int day = 1; day <= 9; day++)
        {
            h.CreditCulture(4);
            h.Run();

            h.Queries.IsDone(custom).ShouldBeFalse($"day {day}");
            h.Queries.Progress(custom).ShouldBe(Amount.FromUnits(4 * day), $"day {day}");
        }

        h.CreditCulture(4);
        h.Run();

        h.Queries.IsDone(custom).ShouldBeTrue();
        h.Queries.Progress(custom).ShouldBe(Amount.FromUnits(40));
        h.Queries.Target.ShouldBeNull();
        h.Queries.PointsPerDay.ShouldBe(Amount.FromUnits(4));
        h.Events().ShouldBe([new CivicAdopted(h.Sim.Context.Tick, custom)]);
    }

    [Fact]
    public void Accrue_WithNoTarget_BanksPointsForTheNextTarget()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");

        h.CreditCulture(4);
        h.Run();

        h.Queries.Progress(custom).ShouldBe(Amount.Zero);
        h.State.Banked.ShouldBe(Amount.FromUnits(4));

        h.Send(new SetCivicTarget(custom));
        h.Run();

        h.Queries.Progress(custom).ShouldBe(Amount.FromUnits(4));
        h.State.Banked.ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Accrue_Overflow_CarriesIntoTheQueuedCivic()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        CivicId custom2 = h.Civic("test_custom2");
        h.Send(new SetCivicTarget(custom));
        h.Send(new SetCivicQueue([custom2]));
        h.Events();

        h.CreditCulture(50);
        h.Run();

        h.Queries.IsDone(custom).ShouldBeTrue();
        h.Queries.Progress(custom2).ShouldBe(Amount.FromUnits(10));
        h.Queries.Target.ShouldBe(custom2);
        h.Queries.Queue.ShouldBeEmpty();
        h.Events().ShouldBe([new CivicAdopted(h.Sim.Context.Tick, custom)]);
    }
}
