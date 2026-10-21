using Evenmere.Game.Host;

namespace Evenmere.Game.Tests;

public sealed class FixedStepperTests
{
    [Fact]
    public void Accumulate_QuarterSecondAtX1_OneTickDue()
    {
        var stepper = new FixedStepper();
        stepper.Accumulate(0.25, 1);
        stepper.TicksDue().ShouldBe(1);
    }

    [Fact]
    public void Accumulate_TenSecondsAtX1_EightTickCap()
    {
        var stepper = new FixedStepper();
        stepper.Accumulate(10.0, 1);
        stepper.TicksDue().ShouldBe(FixedStepper.MaxTicksPerFrame);
    }

    [Fact]
    public void Speeds_ScaleGameTime()
    {
        var atX1 = new FixedStepper();
        atX1.Accumulate(1.0, SimSpeed.X1.Multiplier());
        atX1.TicksDue().ShouldBe(4);

        var atX2 = new FixedStepper();
        atX2.Accumulate(0.5, SimSpeed.X2.Multiplier());
        atX2.TicksDue().ShouldBe(4);

        var atX4 = new FixedStepper();
        atX4.Accumulate(0.25, SimSpeed.X4.Multiplier());
        atX4.TicksDue().ShouldBe(4);
    }

    [Fact]
    public void Consume_DropsBacklogBeyondCap()
    {
        var stepper = new FixedStepper();
        stepper.Accumulate(10.0, 4);
        stepper.Consume(8);
        stepper.AccumulatorMicroseconds.ShouldBe(FixedStepper.MaxBacklogMicroseconds);
    }

    [Fact]
    public void DropBacklog_KeepsFractionForPause()
    {
        var stepper = new FixedStepper();
        stepper.Accumulate(0.3, 1);
        stepper.DropBacklog();
        stepper.AccumulatorMicroseconds.ShouldBeLessThan(FixedStepper.TickMicroseconds);
        stepper.TicksDue().ShouldBe(0);
    }

    [Fact]
    public void Paused_SpeedRunsNoTicks()
    {
        var stepper = new FixedStepper();
        stepper.Accumulate(1.0, SimSpeed.Paused.Multiplier());
        stepper.TicksDue().ShouldBe(0);
    }

    [Fact]
    public void Consume_MoreThanHeld_Throws()
    {
        var stepper = new FixedStepper();
        stepper.Accumulate(0.25, 1);
        Should.Throw<ArgumentOutOfRangeException>(() => stepper.Consume(2));
    }
}
