using Evenmere.Game.Host;
using Evenmere.Game.Host.Debug;

namespace Evenmere.Game.Tests;

public sealed class LaunchOptionsTests
{
    [Fact]
    public void Parse_Defaults()
    {
        var options = LaunchOptions.Parse([]);
        options.Seed.ShouldBe(LaunchOptions.DefaultSeed);
        options.Map.ShouldBe(LaunchOptions.DefaultMap);
        options.SmokeTicks.ShouldBeNull();
        options.SmokeSave.ShouldBeNull();
        options.SmokeLog.ShouldBeNull();
    }

    [Fact]
    public void Parse_SmokeV2Flags()
    {
        var options = LaunchOptions.Parse(["--smoke-ticks=2400", "--smoke-save=a.evsave", "--smoke-log=a.evlog"]);
        options.SmokeTicks.ShouldBe(2400);
        options.SmokeSave.ShouldBe("a.evsave");
        options.SmokeLog.ShouldBe("a.evlog");
        LaunchOptions.RequestsSmoke(["--smoke-ticks=2400"]).ShouldBeTrue();
    }

    [Fact]
    public void RequestsSmoke_FalseWithoutFlag()
    {
        LaunchOptions.RequestsSmoke(["--seed=1"]).ShouldBeFalse();
    }

    [Fact]
    public void Parse_BadSmokeTicks_Throws()
    {
        Should.Throw<FormatException>(() => LaunchOptions.Parse(["--smoke-ticks=abc"]));
    }

    [Fact]
    public void Parse_EmptySmokeSave_Throws()
    {
        Should.Throw<FormatException>(() => LaunchOptions.Parse(["--smoke-ticks=10", "--smoke-save=", "--smoke-log=x"]));
    }

    [Fact]
    public void DebugLaunchOptions_DefaultsAreEmpty()
    {
        var options = DebugLaunchOptions.Parse([]);
        options.BenchFrames.ShouldBeNull();
        options.CameraView.ShouldBe(string.Empty);
        options.StaticCamera.ShouldBeFalse();
    }

    [Fact]
    public void DebugLaunchOptions_ParsesBenchAndCamera()
    {
        var options = DebugLaunchOptions.Parse(["--bench-frames=60", "--camera-view=overview", "--static-camera"]);
        options.BenchFrames.ShouldBe(60);
        options.CameraView.ShouldBe("overview");
        options.StaticCamera.ShouldBeTrue();
    }

    [Fact]
    public void DebugLaunchOptions_BadBench_Throws()
    {
        Should.Throw<FormatException>(() => DebugLaunchOptions.Parse(["--bench-frames=many"]));
    }
}
