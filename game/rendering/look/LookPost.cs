using Godot;

namespace Evenmere.Game.Rendering.Look;

/// <summary>
/// The hand-drawn look pass (M2-29): a quad that rides in front of the camera and composites ink outlines, paper
/// grain and a warm grade over the finished frame (see <c>look_post.gdshader</c>). It exists only in the hand-drawn
/// look, so classic runs never pay for the pass. The ink colour
/// comes from the shared palette (<c>palette.tres</c>, token <c>ink.strong</c>); the ART-04 texture slots stay on
/// their procedural fallbacks until the painted textures land.
/// </summary>
public sealed partial class LookPost : MeshInstance3D
{
    private static readonly string _shaderPath = "res://rendering/look/look_post.gdshader";
    private static readonly string _palettePath = "res://data/visuals/palette.tres";
    private static readonly string _paperGrainPath = "res://assets/textures/grain/paper_grain.png";
    private static readonly string _washPath = "res://assets/textures/grain/wash.png";

    /// <summary>How far in front of the camera the quad rides, in world units: past the near plane, inside the far plane.</summary>
    public const float CoverDistance = 1f;

    private readonly QuadMesh _quad = new();
    private ShaderMaterial? _material;
    private float _lastWidth = -1f;
    private float _lastHeight = -1f;

    private float _lineWeightPx = 2.0f;

    /// <summary>Gets or sets the building ink line weight in pixels at the design resolution: the shader samples at
    /// three quarters of the weight, so 2 px draws about a 2 px line with a soft edge. Ground lines sample at their
    /// own finer radius.</summary>
    [Export(PropertyHint.Range, "0.5,4,0.1,suffix:px")]
    public float LineWeightPx
    {
        get => _lineWeightPx;
        set
        {
            _lineWeightPx = value;
            _material?.SetShaderParameter("building_radius_px", value * 0.75f);
        }
    }

    /// <summary>
    /// Attaches the look pass to a camera when the hand-drawn look is active. Otherwise it does nothing and
    /// reports <see langword="false"/>, so classic runs never pay for the pass.
    /// </summary>
    /// <param name="camera">The camera to composite over.</param>
    /// <param name="look">The active look, read once by the caller.</param>
    /// <returns><see langword="true"/> when the pass was attached.</returns>
    public static bool AttachIfEnabled(Camera3D camera, LookMode look)
    {
        if (look != LookMode.HandDrawn)
        {
            return false;
        }

        Shader? shader = GD.Load<Shader>(_shaderPath);
        if (shader is null)
        {
            GD.PushWarning("[Evenmere] Look: missing shader 'res://rendering/look/look_post.gdshader'.");
            return false;
        }

        var material = new ShaderMaterial { Shader = shader, RenderPriority = 10 };
        if (GD.Load<Resource>(_palettePath) is LookPalette palette)
        {
            material.SetShaderParameter("ink_color", palette.ColourOf("ink.strong"));
        }
        else
        {
            GD.PushWarning("[Evenmere] Look: missing palette 'res://data/visuals/palette.tres'; using the shader default ink.");
        }

        // ART-04 painted grain: bound when present, with a flag so the shader falls back to procedural noise.
        // The masks are mid-grey data read through the standard sRGB sampling path (textures.md section 6).
        material.SetShaderParameter("has_paper_tex", BindMask(material, _paperGrainPath, "paper_grain_tex"));
        material.SetShaderParameter("has_wash_tex", BindMask(material, _washPath, "wash_tex"));

        var post = new LookPost { MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        post._material = material;
        post.Mesh = post._quad;
        post.Position = new Vector3(0f, 0f, -CoverDistance);
        camera.AddChild(post);
        GD.Print("[Evenmere] Look: hand-drawn pass on (ink, grain, warm grade).");
        return true;
    }

    // Loads a greyscale mask texture for a shader slot, reporting whether it bound. A missing file warns once
    // and leaves the slot on its procedural fallback.
    private static float BindMask(ShaderMaterial material, string path, string parameter)
    {
        if (GD.Load<Texture2D>(path) is not { } texture)
        {
            GD.PushWarning($"[Evenmere] Look: missing texture '{path}'; using the procedural fallback.");
            return 0f;
        }

        material.SetShaderParameter(parameter, texture);
        return 1f;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (GetParent() is not Camera3D camera)
        {
            QueueFree();
            return;
        }

        Vector2 view = GetViewport().GetVisibleRect().Size;
        float height;
        if (camera.Projection == Camera3D.ProjectionType.Orthogonal)
        {
            height = camera.Size;
        }
        else
        {
            height = 2f * CoverDistance * MathF.Tan(camera.Fov * 0.5f * (MathF.PI / 180f));
        }

        float width = height * (view.X / Math.Max(view.Y, 1f));
        if (MathF.Abs(width - _lastWidth) > 0.001f || MathF.Abs(height - _lastHeight) > 0.001f)
        {
            _quad.Size = new Vector2(width, height);
            _material?.SetShaderParameter("pixel_size", new Vector2(1f / view.X, 1f / view.Y));
            _lastWidth = width;
            _lastHeight = height;
        }
    }
}
