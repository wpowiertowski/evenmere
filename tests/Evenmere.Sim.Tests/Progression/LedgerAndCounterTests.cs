using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Progression;

public sealed class LedgerAndCounterTests
{
    // ---- points ledger ----------------------------------------------------------------------------------------------

    [Fact]
    public void Credit_SeveralTimes_AddsUp()
    {
        var harness = ProgressionHarness.Create();

        harness.Context.PointsLedger.Credit(PointKind.Knowledge, Amount.FromUnits(2));
        harness.Context.PointsLedger.Credit(PointKind.Knowledge, Amount.Parse("0.5"));

        harness.Context.PointsLedger.TakeToday(PointKind.Knowledge).ShouldBe(Amount.Parse("2.5"));
    }

    [Fact]
    public void TakeToday_ReturnsTheTotalAndResetsIt()
    {
        var harness = ProgressionHarness.Create();
        harness.Context.PointsLedger.Credit(PointKind.Culture, Amount.FromUnits(9));

        harness.Context.PointsLedger.TakeToday(PointKind.Culture).ShouldBe(Amount.FromUnits(9));
        harness.Context.PointsLedger.TakeToday(PointKind.Culture).ShouldBe(Amount.Zero);

        harness.Context.PointsLedger.Credit(PointKind.Culture, Amount.FromUnits(1));
        harness.Context.PointsLedger.TakeToday(PointKind.Culture).ShouldBe(Amount.FromUnits(1));
    }

    [Fact]
    public void TakeToday_OnlyTakesTheKindAskedFor()
    {
        var harness = ProgressionHarness.Create();
        harness.Context.PointsLedger.Credit(PointKind.Knowledge, Amount.FromUnits(3));
        harness.Context.PointsLedger.Credit(PointKind.Culture, Amount.FromUnits(4));
        harness.Context.PointsLedger.Credit(PointKind.Coin, Amount.FromUnits(5));

        harness.Context.PointsLedger.TakeToday(PointKind.Culture).ShouldBe(Amount.FromUnits(4));

        harness.Context.PointsLedger.TakeToday(PointKind.Knowledge).ShouldBe(Amount.FromUnits(3));
        harness.Context.PointsLedger.TakeToday(PointKind.Coin).ShouldBe(Amount.FromUnits(5));
    }

    [Theory]
    [InlineData(PointKind.Knowledge)]
    [InlineData(PointKind.Culture)]
    [InlineData(PointKind.Coin)]
    public void TakeToday_NothingCredited_IsZero(PointKind kind)
    {
        var harness = ProgressionHarness.Create();

        harness.Context.PointsLedger.TakeToday(kind).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Credit_Zero_IsAllowed()
    {
        var harness = ProgressionHarness.Create();

        harness.Context.PointsLedger.Credit(PointKind.Knowledge, Amount.Zero);

        harness.Context.PointsLedger.TakeToday(PointKind.Knowledge).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Credit_NegativeAmount_Throws()
    {
        var harness = ProgressionHarness.Create();

        Should.Throw<ArgumentOutOfRangeException>(() => harness.Context.PointsLedger.Credit(PointKind.Knowledge, Amount.FromUnits(-1)));
        harness.Context.PointsLedger.TakeToday(PointKind.Knowledge).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Ledger_UnknownKind_Throws()
    {
        var harness = ProgressionHarness.Create();

        Should.Throw<ArgumentOutOfRangeException>(() => harness.Context.PointsLedger.Credit((PointKind)9, Amount.FromUnits(1)));
        Should.Throw<ArgumentOutOfRangeException>(() => harness.Context.PointsLedger.TakeToday((PointKind)9));
    }

    [Fact]
    public void Ledger_IsPartOfTheSavedState()
    {
        var harness = ProgressionHarness.Create();
        harness.Context.PointsLedger.Credit(PointKind.Knowledge, Amount.FromUnits(6));

        var reloaded = ProgressionHarness.Reload(harness);

        reloaded.Context.PointsLedger.TakeToday(PointKind.Knowledge).ShouldBe(Amount.FromUnits(6));
    }

    // ---- counters ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Counter_NothingAdded_IsZero()
    {
        var harness = ProgressionHarness.Create();

        harness.Queries.Counter(CounterKey.Produced(harness.Good("grain"))).ShouldBe(0);
        harness.Queries.Counter(CounterKey.Consumed(harness.Good("grain"))).ShouldBe(0);
        harness.Queries.Counter(CounterKey.Built(harness.Building("bakery"))).ShouldBe(0);
        harness.Queries.Counter(CounterKey.RoadTilesLaid).ShouldBe(0);
    }

    [Fact]
    public void Add_EachKind_IsReadBackForThatKeyOnly()
    {
        var harness = ProgressionHarness.Create();
        GoodId grain = harness.Good("grain");
        GoodId bread = harness.Good("bread");
        BuildingTypeId bakery = harness.Building("bakery");
        BuildingTypeId windmill = harness.Building("windmill");

        harness.Context.Counters.Add(CounterKey.Produced(grain), 1500);
        harness.Context.Counters.Add(CounterKey.Produced(grain), 500);
        harness.Context.Counters.Add(CounterKey.Consumed(grain), 700);
        harness.Context.Counters.Add(CounterKey.Built(bakery), 1);
        harness.Context.Counters.Add(CounterKey.Built(bakery), 1);
        harness.Context.Counters.Add(CounterKey.RoadTilesLaid, 12);
        harness.Context.Counters.Add(CounterKey.RoadTilesLaid, 8);

        harness.Queries.Counter(CounterKey.Produced(grain)).ShouldBe(2000);
        harness.Queries.Counter(CounterKey.Consumed(grain)).ShouldBe(700);
        harness.Queries.Counter(CounterKey.Built(bakery)).ShouldBe(2);
        harness.Queries.Counter(CounterKey.RoadTilesLaid).ShouldBe(20);
        harness.Queries.Counter(CounterKey.Produced(bread)).ShouldBe(0);
        harness.Queries.Counter(CounterKey.Built(windmill)).ShouldBe(0);
    }

    [Fact]
    public void Add_Zero_IsAllowedAndChangesNothing()
    {
        var harness = ProgressionHarness.Create();
        byte[] before = harness.Save();

        harness.Context.Counters.Add(CounterKey.RoadTilesLaid, 0);

        harness.Save().ShouldBe(before);
    }

    [Fact]
    public void Add_Negative_Throws()
    {
        var harness = ProgressionHarness.Create();
        harness.Context.Counters.Add(CounterKey.RoadTilesLaid, 5);

        Should.Throw<ArgumentOutOfRangeException>(() => harness.Context.Counters.Add(CounterKey.RoadTilesLaid, -1));
        harness.Queries.Counter(CounterKey.RoadTilesLaid).ShouldBe(5);
    }

    [Fact]
    public void Add_SubjectTheContentDoesNotHave_Throws()
    {
        var harness = ProgressionHarness.Create();

        Should.Throw<ArgumentOutOfRangeException>(() => harness.Context.Counters.Add(new CounterKey(CounterKind.Produced, 5000), 1));
        Should.Throw<ArgumentOutOfRangeException>(() => harness.Context.Counters.Add(new CounterKey(CounterKind.Built, -1), 1));
    }

    [Fact]
    public void Add_UnknownKind_Throws()
    {
        var harness = ProgressionHarness.Create();

        Should.Throw<ArgumentOutOfRangeException>(() => harness.Context.Counters.Add(new CounterKey((CounterKind)99, 0), 1));
    }

    [Fact]
    public void Counter_SubjectTheContentDoesNotHave_IsZero()
    {
        var harness = ProgressionHarness.Create();

        harness.Queries.Counter(new CounterKey(CounterKind.Produced, 5000)).ShouldBe(0);
        harness.Queries.Counter(new CounterKey((CounterKind)99, 0)).ShouldBe(0);
    }

    [Fact]
    public void Add_Overflow_Throws()
    {
        var harness = ProgressionHarness.Create();
        harness.Context.Counters.Add(CounterKey.RoadTilesLaid, long.MaxValue);

        Should.Throw<OverflowException>(() => harness.Context.Counters.Add(CounterKey.RoadTilesLaid, 1));
    }

    [Fact]
    public void Counters_ArePartOfTheSavedState()
    {
        var harness = ProgressionHarness.Create();
        harness.Context.Counters.Add(CounterKey.Produced(harness.Good("grain")), 4200);
        harness.Context.Counters.Add(CounterKey.Built(harness.Building("bakery")), 3);
        harness.Context.Counters.Add(CounterKey.RoadTilesLaid, 17);

        var reloaded = ProgressionHarness.Reload(harness);

        reloaded.Queries.Counter(CounterKey.Produced(reloaded.Good("grain"))).ShouldBe(4200);
        reloaded.Queries.Counter(CounterKey.Built(reloaded.Building("bakery"))).ShouldBe(3);
        reloaded.Queries.Counter(CounterKey.RoadTilesLaid).ShouldBe(17);
    }
}
