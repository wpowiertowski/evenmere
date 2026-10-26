using Evenmere.Game.Host.Persistence;

namespace Evenmere.Game.Tests.Persistence;

/// <summary>The saves root (M2-26): normal launches keep the player's saves; every debug or tooling launch is redirected.</summary>
public sealed class SaveSlotsRootTests
{
    [Fact]
    public void RootFor_NormalLaunch_PlayerSaves()
    {
        SaveSlots.RootFor([]).ShouldBe("user://saves");
        SaveSlots.RootFor(["--seed=7", "--map=starter"]).ShouldBe("user://saves");
    }

    [Fact]
    public void RootFor_FixedSizeRuns_DebugSaves()
    {
        SaveSlots.RootFor(["--smoke-ticks=2400"]).ShouldBe("user://saves-debug");
        SaveSlots.RootFor(["--screenshot=/tmp/shot.png"]).ShouldBe("user://saves-debug");
        SaveSlots.RootFor(["--bench-frames=120"]).ShouldBe("user://saves-debug");
        SaveSlots.RootFor(["--window-size=1280x800"]).ShouldBe("user://saves-debug");
    }

    [Fact]
    public void RootFor_WalkAndDemoFlags_DebugSaves()
    {
        SaveSlots.RootFor(["--focus-walk"]).ShouldBe("user://saves-debug");
        SaveSlots.RootFor(["--demo-notifications"]).ShouldBe("user://saves-debug");
        SaveSlots.RootFor(["--demo-feed=history:all"]).ShouldBe("user://saves-debug");
        SaveSlots.RootFor(["--demo-buildings"]).ShouldBe("user://saves-debug");
    }

    [Fact]
    public void RootFor_ToolFlags_DebugSaves()
    {
        SaveSlots.RootFor(["--tool-build=sawpit"]).ShouldBe("user://saves-debug");
        SaveSlots.RootFor(["--tool-road=dirt_road"]).ShouldBe("user://saves-debug");
        SaveSlots.RootFor(["--tool-demolish"]).ShouldBe("user://saves-debug");
    }

    [Fact]
    public void RootFor_ShowAndLoadFlags_DebugSaves()
    {
        SaveSlots.RootFor(["--show-debug-overlay"]).ShouldBe("user://saves-debug");
        SaveSlots.RootFor(["--show-cheat-console=grant timber 10"]).ShouldBe("user://saves-debug");
        SaveSlots.RootFor(["--load-save=slot.evsave"]).ShouldBe("user://saves-debug");
        SaveSlots.RootFor(["--debug-save-and-quit=shot"]).ShouldBe("user://saves-debug");
    }

    [Fact]
    public void RootFor_SeedWithDebugFlag_DebugSaves()
    {
        SaveSlots.RootFor(["--seed=7", "--show-debug-overlay"]).ShouldBe("user://saves-debug");
    }
}
