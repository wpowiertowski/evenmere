using Evenmere.Game.Ui.Menus;

namespace Evenmere.Game.Tests;

/// <summary>M2-31: the pause menu's fullscreen button reads Fullscreen when windowed and Windowed when fullscreen.</summary>
public sealed class PauseMenuButtonTests
{
    [Fact]
    public void FullscreenButtonKey_Windowed_SaysFullscreen()
    {
        PauseMenu.FullscreenButtonKey(isFullscreen: false).ShouldBe("ui.pause.fullscreen");
    }

    [Fact]
    public void FullscreenButtonKey_Fullscreen_SaysWindowed()
    {
        PauseMenu.FullscreenButtonKey(isFullscreen: true).ShouldBe("ui.pause.windowed");
    }

    [Fact]
    public void FullscreenButtonTipKey_MatchesLabel()
    {
        PauseMenu.FullscreenButtonTipKey(isFullscreen: false).ShouldBe("ui.pause.fullscreen.tip");
        PauseMenu.FullscreenButtonTipKey(isFullscreen: true).ShouldBe("ui.pause.windowed.tip");
    }
}
