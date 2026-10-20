using Godot;

namespace Evenmere.Game.Rendering.Camera;

/// <summary>
/// A flat checkerboard the size of the map, so the camera and tile picking can be judged by eye. It stands in for the
/// terrain until terrain rendering (M1-19) lands; hide or delete the <c>TestGround</c> node in <c>CameraRig.tscn</c> then.
/// </summary>
public sealed partial class TestGround : MeshInstance3D
{
    /// <inheritdoc />
    public override void _Ready()
    {
        MapBounds bounds = GetParentOrNull<CameraRig>()?.Bounds ?? MapBounds.Default;

        // Detached from the rig, which moves around; the ground stays where the map is.
        TopLevel = true;
        Position = new Vector3(bounds.WorldWidth * 0.5f, 0f, bounds.WorldDepth * 0.5f);
        Mesh = new PlaneMesh { Size = new Vector2(bounds.WorldWidth, bounds.WorldDepth) };
        MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://rendering/camera/test_ground.gdshader") };
        CastShadow = ShadowCastingSetting.Off;
    }
}
