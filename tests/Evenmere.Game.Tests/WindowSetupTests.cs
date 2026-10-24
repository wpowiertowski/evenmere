using Evenmere.Game.Host;

namespace Evenmere.Game.Tests;

public sealed class WindowSetupTests
{
    [Fact]
    public void ShouldMaximize_NormalLaunch_True()
    {
        WindowSetup.ShouldMaximize([]).ShouldBeTrue();
    }

    [Fact]
    public void ShouldMaximize_SeedAndMap_True()
    {
        WindowSetup.ShouldMaximize(["--seed=1", "--map=starter"]).ShouldBeTrue();
    }

    [Fact]
    public void ShouldMaximize_Smoke_False()
    {
        WindowSetup.ShouldMaximize(["--smoke-ticks=2400", "--smoke-save=a.evsave", "--smoke-log=a.evlog"]).ShouldBeFalse();
        WindowSetup.FixedSizeReason(["--smoke-ticks=2400"]).ShouldBe("smoke");
    }

    [Fact]
    public void ShouldMaximize_Screenshot_False()
    {
        WindowSetup.ShouldMaximize(["--seed=1", "--screenshot=/tmp/shot.png"]).ShouldBeFalse();
        WindowSetup.ShouldMaximize(["--screenshot-after-frames=60"]).ShouldBeFalse();
        WindowSetup.ShouldMaximize(["--screenshot-focus=Hud/HudRoot"]).ShouldBeFalse();
        WindowSetup.FixedSizeReason(["--screenshot=/tmp/shot.png"]).ShouldBe("screenshot");
    }

    [Fact]
    public void ShouldMaximize_Bench_False()
    {
        WindowSetup.ShouldMaximize(["--bench-frames=120"]).ShouldBeFalse();
        WindowSetup.FixedSizeReason(["--bench-frames=120"]).ShouldBe("bench");
    }

    [Fact]
    public void FixedSizeReason_NormalLaunch_NamesIt()
    {
        WindowSetup.FixedSizeReason([]).ShouldBe("normal launch");
        WindowSetup.FixedSizeReason(["--seed=1"]).ShouldBe("normal launch");
    }

    [Fact]
    public void ShouldMaximize_DebugCameraFlags_True()
    {
        // The debug camera presets frame screenshots the operator takes; only the capture and bench flags pin the size.
        WindowSetup.ShouldMaximize(["--camera-view=overview", "--static-camera", "--demo-buildings"]).ShouldBeTrue();
    }
}
