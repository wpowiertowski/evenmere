using Evenmere.Game.Rendering.Effects;

namespace Evenmere.Game.Tests;

/// <summary>M2-24: smoke has a hard particle budget that thins with zoom.</summary>
public sealed class SmokeBudgetTests
{
    [Fact]
    public void ActiveEmitterCount_CapsAtMaxEmitters()
    {
        SmokeBudget.ActiveEmitterCount(1000, 20f).ShouldBe(SmokeBudget.MaxEmitters);
    }

    [Fact]
    public void ActiveEmitterCount_PassesThroughSmallCounts()
    {
        SmokeBudget.ActiveEmitterCount(7, 20f).ShouldBe(7);
    }

    [Fact]
    public void ActiveEmitterCount_HalvesPastMidZoom()
    {
        SmokeBudget.ActiveEmitterCount(40, SmokeBudget.MidZoom + 1f).ShouldBe(20);
    }

    [Fact]
    public void ActiveEmitterCount_HidesPastFarZoom()
    {
        SmokeBudget.ActiveEmitterCount(40, SmokeBudget.FarZoom + 1f).ShouldBe(0);
        SmokeBudget.ActiveEmitterCount(40, SmokeBudget.FarZoom).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ActiveEmitterCount_ZeroWithoutBuildings()
    {
        SmokeBudget.ActiveEmitterCount(0, 20f).ShouldBe(0);
    }

    [Fact]
    public void PuffsPerEmitter_IsAWispPerChimney()
    {
        SmokeBudget.PuffsPerEmitter.ShouldBeInRange(3, 5);
        SmokeBudget.TotalBudget.ShouldBe(SmokeBudget.MaxEmitters * SmokeBudget.PuffsPerEmitter);
    }

    [Fact]
    public void ParticleCount_NeverExceedsTheBudget()
    {
        SmokeBudget.ParticleCount(SmokeBudget.MaxEmitters).ShouldBe(SmokeBudget.TotalBudget);
        SmokeBudget.ParticleCount(1000).ShouldBe(SmokeBudget.TotalBudget);
        SmokeBudget.ParticleCount(0).ShouldBe(0);
    }
}
