using Evenmere.Game.Rendering.Camera;

namespace Evenmere.Game.Tests;

/// <summary>M2-37: the orthographic camera hangs far back at a constant remove, so nothing between it and the focus
/// ever crosses the near plane and clips, while the framing stays identical; the perspective camera lifts off the
/// terrain and building tops instead of sitting inside them at street zoom.</summary>
public sealed class CameraBackoffTests
{
    private const float Tight = 1e-4f;

    [Fact]
    public void OrthoCameraOffset_PointsAlongCloseCameraDirection()
    {
        // The framing is identical apart from the missing clipping: in the rig's local space the far camera
        // sits on the same view ray as a close camera at the same pitch. The rig node carries the yaw.
        foreach (float pitchDegrees in new[] { 30f, 40f, 50f })
        {
            float pitch = pitchDegrees * (MathF.PI / 180f);
            System.Numerics.Vector3 far = CameraMath.OrthoCameraOffset(pitch);
            System.Numerics.Vector3 near = CameraMath.CameraOffset(8f, pitch, 0f);
            far.X.ShouldBe(0f, Tight);
            near.X.ShouldBe(0f, Tight);
            System.Numerics.Vector3 farDir = far / far.Length();
            System.Numerics.Vector3 nearDir = near / near.Length();
            farDir.Y.ShouldBe(nearDir.Y, Tight);
            farDir.Z.ShouldBe(nearDir.Z, Tight);
        }
    }

    [Fact]
    public void OrthoCamera_StaysAboveTallestSceneAtEveryZoom()
    {
        // The camera height above the focus never depends on the zoom, and it clears the tallest terrain plus the
        // tallest building with room to spare, at the lowest street pitch and the highest island pitch.
        float sceneTop = CameraMath.MaxTerrainHeight + 4f + 1f;
        foreach (float pitchDegrees in new[] { 30f, 50f })
        {
            float pitch = pitchDegrees * (MathF.PI / 180f);
            CameraMath.OrthoCameraOffset(pitch).Y.ShouldBeGreaterThan(sceneTop);
        }
    }

    [Fact]
    public void OrthoDepthRange_HoldsLargestMap()
    {
        // The whole island sits between the near and far planes from any focus on the largest map, so the rig never
        // moves the depth range and the ink never shifts.
        float sceneTop = CameraMath.MaxTerrainHeight + 4f + 1f;
        foreach (float pitchDegrees in new[] { 30f, 50f })
        {
            float pitch = pitchDegrees * (MathF.PI / 180f);
            float span = CameraMath.DepthSpan(384f, sceneTop, pitch);
            (CameraMath.OrthoBackoffDistance - span).ShouldBeGreaterThan(CameraMath.OrthoNear);
            (CameraMath.OrthoBackoffDistance + span).ShouldBeLessThan(CameraMath.OrthoFar);
        }
    }

    [Fact]
    public void PerspectiveZoomForClearance_ClearCamera_KeepsZoom()
    {
        // Height 20 at a 30-degree pitch clears the ground plus the building clearance, so nothing moves.
        float pitch = 30f * (MathF.PI / 180f);
        CameraMath.PerspectiveZoomForClearance(20f, pitch, 0f, 0f, CameraMath.PerspectiveClearance).ShouldBe(20f, Tight);
    }

    [Fact]
    public void PerspectiveZoomForClearance_BuriedCamera_LiftsAboveGroundPlusClearance()
    {
        // A street-zoom camera that would sit inside a hill is pushed back along its arm just far enough.
        float pitch = 30f * (MathF.PI / 180f);
        float zoom = CameraMath.PerspectiveZoomForClearance(8f, pitch, 0f, 7.5f, CameraMath.PerspectiveClearance);
        zoom.ShouldBeGreaterThan(8f);
        (MathF.Sin(pitch) * zoom).ShouldBe(7.5f + CameraMath.PerspectiveClearance, Tight);
    }

    [Fact]
    public void PerspectiveZoomForClearance_HighCamera_NeverPullsCloser()
    {
        float pitch = 50f * (MathF.PI / 180f);
        CameraMath.PerspectiveZoomForClearance(190f, pitch, 2f, 7.5f, CameraMath.PerspectiveClearance).ShouldBe(190f, Tight);
    }
}
