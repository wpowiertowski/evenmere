using Evenmere.Game.Rendering.Camera;

namespace Evenmere.Game.Tests;

/// <summary>M2-31: wheel, trackpad scroll and pinch zoom the map only when the pointer is over no blocking control.</summary>
public sealed class CameraZoomGuardTests
{
    [Fact]
    public void ShouldPointerZoom_OverMap_Allows()
    {
        CameraMath.ShouldPointerZoom(pointerOverBlockingControl: false).ShouldBeTrue();
    }

    [Fact]
    public void ShouldPointerZoom_OverPanel_Blocks()
    {
        CameraMath.ShouldPointerZoom(pointerOverBlockingControl: true).ShouldBeFalse();
    }
}
