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
    public void ShouldMaximize_WindowSize_False()
    {
        WindowSetup.ShouldMaximize(["--window-size=1280x800"]).ShouldBeFalse();
        WindowSetup.FixedSizeReason(["--window-size=2560x1600"]).ShouldBe("window-size");
    }

    [Fact]
    public void TryParseWindowSize_Valid_ReadsPoints()
    {
        WindowSetup.TryParseWindowSize(["--window-size=1280x800"], out int width, out int height).ShouldBeTrue();
        width.ShouldBe(1280);
        height.ShouldBe(800);
    }

    [Fact]
    public void TryParseWindowSize_LastValidWins()
    {
        WindowSetup.TryParseWindowSize(["--window-size=1280x800", "--window-size=2560x1600"], out int width, out int height).ShouldBeTrue();
        width.ShouldBe(2560);
        height.ShouldBe(1600);
    }

    [Fact]
    public void TryParseWindowSize_Invalid_Ignored()
    {
        WindowSetup.TryParseWindowSize(["--window-size=big"], out _, out _).ShouldBeFalse();
        WindowSetup.TryParseWindowSize(["--window-size=0x800"], out _, out _).ShouldBeFalse();
        WindowSetup.TryParseWindowSize(["--seed=1"], out _, out _).ShouldBeFalse();
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
