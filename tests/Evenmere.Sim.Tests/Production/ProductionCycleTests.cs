using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Production;

public sealed class ProductionCycleTests
{
    [Fact]
    public void Cycle_ThirtySecondRecipeAtFullProductivity_YieldsOneUnitEvery120Ticks()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);

        h.RunTicks(119);
        h.Output(farm, "wool").ShouldBe(Amount.Zero);

        h.RunTicks(1);
        h.Output(farm, "wool").ShouldBe(Amount.FromUnits(1));

        h.RunTicks(119);
        h.Output(farm, "wool").ShouldBe(Amount.FromUnits(1));

        h.RunTicks(1);
        h.Output(farm, "wool").ShouldBe(Amount.FromUnits(2));
    }

    [Fact]
    public void Cycle_HalfWorkforce_TakesTwiceAsLong()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);
        h.SetFill(farm, 500);

        h.RunTicks(239);
        h.Output(farm, "wool").ShouldBe(Amount.Zero);

        h.RunTicks(1);
        h.Output(farm, "wool").ShouldBe(Amount.FromUnits(1));
        h.Info(farm).Productivity.PerMille.ShouldBe(500);
        h.Info(farm).WorkforceFill.PerMille.ShouldBe(500);
    }

    [Fact]
    public void Cycle_LeftoverProgress_CarriesIntoTheNextCycle()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);

        // 700 per tick against a target of 120,000: the first cycle ends on tick 172 with 400 to spare, and the second needs
        // 171 more ticks, not 172, because of the spare.
        h.SetFill(farm, 700);
        h.RunTicks(171);
        h.Output(farm, "wool").ShouldBe(Amount.Zero);
        h.RunTicks(1);
        h.Output(farm, "wool").ShouldBe(Amount.FromUnits(1));

        h.RunTicks(170);
        h.Output(farm, "wool").ShouldBe(Amount.FromUnits(1));
        h.RunTicks(1);
        h.Output(farm, "wool").ShouldBe(Amount.FromUnits(2));
    }

    [Fact]
    public void Cycle_Completing_ConsumesInputsAndAddsToTheProducedCounter()
    {
        var h = ProductionHarness.Create();
        EntityId mill = h.AddReady("windmill", 2, 2);
        h.Context.ProductionBuffers.AddInput(mill, h.Good("grain"), Amount.FromUnits(2));

        h.RunTicks(120);
        h.Input(mill, "grain").ShouldBe(Amount.FromUnits(1));
        h.Output(mill, "flour").ShouldBe(Amount.FromUnits(1));
        h.Sim.Queries.Progression.Counter(CounterKey.Produced(h.Good("flour"))).ShouldBe(1000);

        h.RunTicks(120);
        h.Input(mill, "grain").ShouldBe(Amount.Zero);
        h.Output(mill, "flour").ShouldBe(Amount.FromUnits(2));
        h.Sim.Queries.Progression.Counter(CounterKey.Produced(h.Good("flour"))).ShouldBe(2000);
    }

    [Fact]
    public void Info_AWorkingProducer_ReportsEachFactorOfItsProductivity()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);
        h.SetFill(farm, 800);

        h.RunTicks(60);

        ProductionInfo info = h.Info(farm);
        info.Building.ShouldBe(farm);
        info.Recipe.ShouldBe(h.Content.IdOf<RecipeId>("shear_wool"));
        info.Stall.ShouldBe(StallReason.None);
        info.WorkforceFill.PerMille.ShouldBe(800);
        info.InputsReady.ShouldBeTrue();
        info.AreaYield.PerMille.ShouldBe(1000);
        info.BonusPercent.ShouldBe(0);
        info.Productivity.PerMille.ShouldBe(800);
        info.CycleProgress.PerMille.ShouldBe(400);
        info.InputBuffer.ShouldBeEmpty();
        info.OutputBuffer.ShouldBe([new GoodAmount(h.Good("wool"), Amount.Zero)]);
    }

    [Fact]
    public void ProducedYesterday_DuringTheFirstDay_IsZero()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);
        h.Sim.Queries.Progression.AddModifier(Stat.Productivity, new ModifierContribution(default, 100, true));

        h.RunTicks(119);

        h.Output(farm, "wool").ShouldBe(Amount.FromUnits(1));
        h.System.Queries.ProducedYesterday(h.Good("wool")).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void ProducedYesterday_AfterAWholeDay_IsThatDaysTotal()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);

        // +100% makes a cycle take 60 ticks. Day 0 is ticks 0 to 119 and holds one cycle (tick 60); day 1 is ticks 120 to 239
        // and holds two (ticks 120 and 180).
        h.Sim.Queries.Progression.AddModifier(Stat.Productivity, new ModifierContribution(default, 100, true));

        h.RunTicks(239);
        h.System.Queries.ProducedYesterday(h.Good("wool")).ShouldBe(Amount.FromUnits(1));
        h.Output(farm, "wool").ShouldBe(Amount.FromUnits(3));

        // Tick 240 starts day 2, so yesterday is day 1 with its two units, and the cycle that ends this tick counts for day 2.
        h.RunTicks(1);
        h.System.Queries.ProducedYesterday(h.Good("wool")).ShouldBe(Amount.FromUnits(2));
        h.System.Queries.ProducedYesterday(h.Good("fish")).ShouldBe(Amount.Zero);
    }
}
