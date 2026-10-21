using Evenmere.Game.Host;
using Evenmere.Game.Rendering.Camera;
using Godot;

namespace Evenmere.Game.Rendering.Life;

/// <summary>
/// The life layer (M1-22): carts replaying transport jobs and walkers ambling between houses, markets and workplaces.
/// Purely visual: it reads queries between ticks, holds no authoritative state, and is driven by <see cref="SimHost"/>
/// after it ticks. One node in <c>Main.tscn</c>; the cart and walker layers are children created here.
/// </summary>
public sealed partial class LifeRenderer : Node3D
{
    /// <summary>Past this zoom walkers hide and carts shrink to dots.</summary>
    public const float FarZoom = 120f;

    /// <summary>Past this zoom (but below <see cref="FarZoom"/>) walkers thin to this many.</summary>
    public const float MidZoom = 60f;

    /// <summary>How many walkers stay visible in the mid zoom band.</summary>
    public const int MidZoomWalkers = 500;

    private readonly GoodColours _colours = new();

    private SimHost? _host;
    private CameraRig? _rig;
    private CartRenderer? _carts;
    private WalkerRenderer? _walkers;
    private bool _initialised;
    private long _lastTick = -1;

    /// <summary>Gets how many carts are drawn now, for the debug evidence.</summary>
    public int CartCount => _carts?.CartCount ?? 0;

    /// <summary>Gets how many walkers the town holds (before zoom thinning), for the debug evidence.</summary>
    public int TotalWalkers => _walkers?.TotalWalkers ?? 0;

    /// <inheritdoc />
    public override void _Ready()
    {
        Node? scene = GetTree().CurrentScene;
        _host = scene?.GetNodeOrNull<SimHost>("SimHost");
        _rig = scene?.GetNodeOrNull<CameraRig>("CameraRig");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_host is not { IsRunning: true })
        {
            return;
        }

        if (!_initialised)
        {
            _colours.Load();
            _carts = new CartRenderer();
            _walkers = new WalkerRenderer();
            AddChild(_carts);
            AddChild(_walkers);
            _carts.Initialise(_host, _colours);
            _walkers.Initialise(_host);
            _initialised = true;
        }

        if (_carts is null || _walkers is null)
        {
            return;
        }

        long tick = _host.CurrentTick;
        if (tick != _lastTick)
        {
            _lastTick = tick;
            _carts.Refresh();
            _walkers.Refresh();
        }

        float zoom = _rig?.Zoom ?? 40f;
        bool far = zoom > FarZoom;
        int walkers = zoom > FarZoom ? 0 : zoom > MidZoom ? Math.Min(_walkers.TotalWalkers, MidZoomWalkers) : Math.Min(_walkers.TotalWalkers, WalkerRenderer.MaxVisible);
        double alpha = _host.InterpolationAlpha;
        _carts.UpdateFrame(tick, alpha, far);
        _walkers.UpdateFrame(tick, alpha, walkers);
    }
}
