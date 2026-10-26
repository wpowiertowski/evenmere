using Evenmere.Core.Ids;
using Evenmere.Game.Data.Visuals;
using Evenmere.Game.Host;
using Evenmere.Game.Rendering.Camera;
using Evenmere.Game.Rendering.Overlays;
using Evenmere.Game.Rendering.Terrain;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Rendering.Effects;

/// <summary>
/// Chimney smoke (M2-24): one pooled <see cref="GpuParticles3D"/> emitter per active production building, capped by
/// <see cref="SmokeBudget"/> and thinned with zoom. Buildings are re-read twice a second in ascending ID order, so
/// the emitter choice is deterministic; no sim state is ever held. Smoke is translucent, so its material draws at
/// the after-look priority (<see cref="OverlayRenderer.AfterLookPriority"/>): the look's post pass erases blended
/// transparents drawn before it. A debug-only <c>--no-smoke</c> flag disables it for the bench comparison.
/// </summary>
public sealed partial class SmokeRenderer : Node3D
{
    private const string NoSmokeFlag = "--no-smoke";

    // Near-white warm grey, fading in and out over each puff's life through the process material's colour
    // ramp; the albedo carries the base translucency. Effective peak is about 0.16: a wisp, not a column.
    private static readonly Color _smokeColour = new(0.87f, 0.85f, 0.82f, 0.6f);

    private readonly List<GpuParticles3D> _pool = new();
    private readonly List<EntityId> _scratch = new();

    private SimHost? _host;
    private CameraRig? _rig;
    private float _timer;
    private bool _disabled;

    /// <summary>Gets how many emitters are smoking now, for the bench evidence.</summary>
    public int ActiveEmitters { get; private set; }

    /// <summary>Gets the live particle count now (emitters × puffs), for the bench evidence.</summary>
    public int ActiveParticles => ActiveEmitters * SmokeBudget.PuffsPerEmitter;

    /// <inheritdoc />
    public override void _Ready()
    {
        Node? scene = GetTree().CurrentScene;
        _host = scene?.GetNodeOrNull<SimHost>("SimHost");
        _rig = scene?.GetNodeOrNull<CameraRig>("CameraRig");
        if (OS.IsDebugBuild())
        {
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == NoSmokeFlag)
                {
                    _disabled = true;
                    GD.Print("[Evenmere] Smoke: disabled by --no-smoke.");
                }
            }
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_disabled || _host is not { IsRunning: true })
        {
            return;
        }

        _timer -= (float)delta;
        if (_timer > 0f)
        {
            return;
        }

        _timer = SmokeBudget.RefreshSeconds;
        Refresh();
    }

    private void Refresh()
    {
        if (_host is null)
        {
            return;
        }

        _scratch.Clear();
        foreach (BuildingInfo building in _host.Query.Map.ActiveBuildings)
        {
            if (building.State == BuildingState.Active && _host.Query.Production.TryGet(building.Id, out _))
            {
                _scratch.Add(building.Id);
            }
        }

        float zoom = _rig?.Zoom ?? 40f;
        int wanted = SmokeBudget.ActiveEmitterCount(_scratch.Count, zoom);
        while (_pool.Count < wanted)
        {
            _pool.Add(CreateEmitter());
        }

        for (int i = 0; i < _pool.Count; i++)
        {
            GpuParticles3D emitter = _pool[i];
            if (i < wanted)
            {
                emitter.Position = Chimney(_scratch[i]);
                emitter.Visible = true;
                emitter.Emitting = true;
            }
            else
            {
                emitter.Emitting = false;
                emitter.Visible = false;
            }
        }

        if (wanted != ActiveEmitters)
        {
            ActiveEmitters = wanted;
            GD.Print($"[Evenmere] Smoke: {wanted} emitter(s), {ActiveParticles} particle(s) against the {SmokeBudget.TotalBudget} budget (zoom {zoom:F0}).");
        }
    }

    // The chimney rides the footprint centre at the pad top plus a fixed lift, so smoke leaves the roof, not the ground.
    private Vector3 Chimney(EntityId id)
    {
        if (_host is null || !_host.Query.Map.TryGetBuilding(id, out BuildingInfo building))
        {
            return Vector3.Zero;
        }

        GroundSampler.FootprintHeights(
            _host.Query.Map,
            _host.Content,
            building.Origin.X,
            building.Origin.Y,
            building.Width,
            building.Height,
            out float highest,
            out _);
        return new Vector3(
            building.Origin.X + (building.Width / 2f),
            highest + SmokeBudget.ChimneyHeight,
            building.Origin.Y + (building.Height / 2f));
    }

    private GpuParticles3D CreateEmitter()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(_smokeColour.R, _smokeColour.G, _smokeColour.B, 0f));
        gradient.AddPoint(0.25f, new Color(_smokeColour.R, _smokeColour.G, _smokeColour.B, 0.6f));
        gradient.AddPoint(0.7f, new Color(_smokeColour.R, _smokeColour.G, _smokeColour.B, 0.35f));
        gradient.SetColor(1, new Color(_smokeColour.R, _smokeColour.G, _smokeColour.B, 0f));
        var ramp = new GradientTexture1D { Gradient = gradient };
        // Puffs grow a little as they rise: the scale curve multiplies the puff size over its life.
        var growth = new Curve();
        growth.AddPoint(new Vector2(0f, 0.7f));
        growth.AddPoint(new Vector2(1f, 1.3f));
        var growthTexture = new CurveXyzTexture { CurveX = growth, CurveY = growth, CurveZ = growth };
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Point,
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 10f,
            InitialVelocityMin = 0.35f,
            InitialVelocityMax = 0.6f,
            Gravity = new Vector3(0.05f, 0.3f, 0f),
            DampingMin = 0.1f,
            DampingMax = 0.3f,
            ScaleMin = 0.5f,
            ScaleMax = 0.8f,
            ScaleCurve = growthTexture,
            ColorRamp = ramp,
        };
        var material = new StandardMaterial3D
        {
            AlbedoColor = _smokeColour,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            RenderPriority = OverlayRenderer.AfterLookPriority,
        };
        // Round puffs, not quads: an unshaded low-poly sphere has a soft round silhouette with no texture,
        // so no hard square edges ever show against roofs or snow. Spheres need no billboarding.
        var puff = new SphereMesh
        {
            Radius = 0.11f,
            Height = 0.22f,
            RadialSegments = 8,
            Rings = 4,
            Material = material,
        };
        var emitter = new GpuParticles3D
        {
            Name = $"Smoke_{_pool.Count}",
            Amount = SmokeBudget.PuffsPerEmitter,
            Lifetime = 2.2f,
            Preprocess = 2.2f,
            Explosiveness = 0f,
            Randomness = 0.5f,
            LocalCoords = false,
            ProcessMaterial = process,
            DrawPass1 = puff,
            VisibilityAabb = new Aabb(new Vector3(-2f, -1f, -2f), new Vector3(4f, 7f, 4f)),
        };
        AddChild(emitter);
        return emitter;
    }
}
