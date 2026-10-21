using Evenmere.Core.Ids;
using Evenmere.Game.Host;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Rendering.Life;

/// <summary>
/// Replays transport jobs as carts (ADR-0005, visual replay): one cached view path per (job, phase), positioned by
/// phase progress, drawn with two MultiMeshes (bodies plus good-coloured loads). The sim owns the jobs; this only
/// reads <c>ILogisticsQueries.ActiveJobs</c> between ticks and never decides anything.
/// </summary>
/// <remarks>
/// Refresh (paths, counts, colours) runs only when the tick changed; per-frame updates only rewrite instance
/// transforms from reused slots, with no allocations in steady state. Path lists come from a pool.
/// </remarks>
internal sealed partial class CartRenderer : Node3D
{
    private readonly record struct JobPhaseKey(EntityId Job, TransportPhase Phase);

    private sealed class CartSlot
    {
        public EntityId Id;
        public EntityId Storehouse;
        public EntityId Building;
        public GoodId Good;
        public TransportDirection Direction;
        public TransportPhase Phase;
        public long PhaseStartTick;
        public int PhaseTicks;
        public List<TileCoord>? Path;
        public bool Standing;
        public bool Carrying;
    }

    private static readonly Color _bodyTint = new(1f, 1f, 1f);
    private static readonly Basis _hiddenBasis = Basis.Identity.Scaled(new Vector3(0.0001f, 0.0001f, 0.0001f));

    private readonly Dictionary<JobPhaseKey, List<TileCoord>> _paths = new();
    private readonly Stack<List<TileCoord>> _pool = new();
    private readonly HashSet<JobPhaseKey> _liveKeys = new();
    private readonly List<JobPhaseKey> _dropScratch = new();

    private SimHost? _host;
    private GoodColours? _colours;
    private MultiMeshInstance3D? _bodies;
    private MultiMeshInstance3D? _loads;
    private CartSlot[] _slots = [];
    private int _count;
    private int _shown;

    /// <summary>Gets how many carts are drawn now.</summary>
    public int CartCount => _count;

    /// <summary>Builds the meshes and nodes once the world is running.</summary>
    /// <param name="host">The running simulation host.</param>
    /// <param name="colours">The loaded good colours.</param>
    public void Initialise(SimHost host, GoodColours colours)
    {
        _host = host;
        _colours = colours;

        _bodies = new MultiMeshInstance3D
        {
            Name = "Carts",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = LifeMeshBuilder.BuildCartBody(),
            },
        };
        _loads = new MultiMeshInstance3D
        {
            Name = "CartLoads",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = LifeMeshBuilder.BuildCartLoad(),
            },
        };
        AddChild(_bodies);
        AddChild(_loads);
    }

    /// <summary>
    /// Re-reads the active jobs after a tick batch: copies the views into reused slots, caches one view path per new
    /// (job, phase) and drops the paths of jobs that are gone. Travel phases pathfind storehouse to building (out) or
    /// back (return); waiting and loading phases stand still at the right end.
    /// </summary>
    public void Refresh()
    {
        if (_host is null || _bodies is null || _loads is null || _colours is null)
        {
            return;
        }

        ReadOnlySpan<TransportJobView> jobs = _host.Query.Logistics.ActiveJobs();
        if (_slots.Length < jobs.Length)
        {
            _slots = new CartSlot[Math.Max(jobs.Length, 4)];
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = new CartSlot();
            }
        }

        _liveKeys.Clear();
        for (int i = 0; i < jobs.Length; i++)
        {
            _liveKeys.Add(new JobPhaseKey(jobs[i].Id, jobs[i].Phase));
        }

        _dropScratch.Clear();
        foreach (JobPhaseKey key in _paths.Keys)
        {
            if (!_liveKeys.Contains(key))
            {
                _dropScratch.Add(key);
            }
        }

        foreach (JobPhaseKey key in _dropScratch)
        {
            List<TileCoord> path = _paths[key];
            _paths.Remove(key);
            path.Clear();
            _pool.Push(path);
        }

        _count = jobs.Length;
        if (_shown != _count)
        {
            _shown = _count;
            _bodies.Multimesh.InstanceCount = _count;
            _loads.Multimesh.InstanceCount = _count;
            _bodies.Visible = _count > 0;
            _loads.Visible = _count > 0;
        }

        for (int i = 0; i < jobs.Length; i++)
        {
            TransportJobView job = jobs[i];
            CartSlot slot = _slots[i];
            slot.Id = job.Id;
            slot.Storehouse = job.Storehouse;
            slot.Building = job.Building;
            slot.Good = job.Good;
            slot.Direction = job.Direction;
            slot.Phase = job.Phase;
            slot.PhaseStartTick = job.PhaseStartTick;
            slot.PhaseTicks = job.PhaseTicks;
            slot.Standing = job.Phase is TransportPhase.Load or TransportPhase.Unload;
            slot.Carrying = job.Direction == TransportDirection.Pickup
                ? job.Phase is TransportPhase.TravelBack or TransportPhase.Unload
                : job.Phase is TransportPhase.TravelOut or TransportPhase.Unload;
            slot.Path = slot.Standing ? null : PathFor(_host, job);

            string goodName = _host.Content.Goods.NameOf(job.Good);
            _loads.Multimesh.SetInstanceColor(i, _colours.ColourFor(goodName));
            _bodies.Multimesh.SetInstanceColor(i, _bodyTint);
        }
    }

    /// <summary>
    /// Rewrites every cart transform for the current frame: progress through the phase spread over the cached path,
    /// heights from the road surface, a gentle bob while rolling. No allocations.
    /// </summary>
    /// <param name="tick">The completed tick.</param>
    /// <param name="alpha">The interpolation alpha toward the next tick, from 0 to 1.</param>
    /// <param name="far">Whether the camera is past the LOD distance: carts shrink to dots and loads hide.</param>
    public void UpdateFrame(long tick, double alpha, bool far)
    {
        if (_host is null || _bodies is null || _loads is null || _count == 0)
        {
            return;
        }

        float lodScale = far ? 0.45f : 1f;
        for (int i = 0; i < _count; i++)
        {
            CartSlot slot = _slots[i];
            float progress = slot.PhaseTicks <= 0
                ? 1f
                : Math.Clamp(((tick - slot.PhaseStartTick) + (float)alpha) / slot.PhaseTicks, 0f, 1f);

            float tileX;
            float tileZ;
            float yaw;
            if (slot.Path is { Count: >= 2 })
            {
                LifeGround.WalkOnPath(slot.Path, progress, out tileX, out tileZ, out yaw);
            }
            else
            {
                StandOrLine(slot, progress, out tileX, out tileZ, out yaw);
            }

            float height = LifeGround.RoadHeight(_host.Query.Map, _host.Content, tileX, tileZ);
            if (!slot.Standing)
            {
                height += MathF.Abs(MathF.Sin(((tick + (float)alpha) * 0.9f) + (i * 1.7f))) * 0.025f;
            }

            Basis basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(lodScale, lodScale, lodScale));
            var transform = new Transform3D(basis, new Vector3(tileX, height, tileZ));
            _bodies.Multimesh.SetInstanceTransform(i, transform);
            _loads.Multimesh.SetInstanceTransform(i, slot.Carrying && !far ? transform : new Transform3D(_hiddenBasis, transform.Origin));
        }
    }

    // The cached path for a travel phase, or null when there is no road route (the cart then drives straight).
    private List<TileCoord>? PathFor(SimHost host, TransportJobView job)
    {
        var key = new JobPhaseKey(job.Id, job.Phase);
        if (_paths.TryGetValue(key, out List<TileCoord>? cached))
        {
            return cached.Count >= 2 ? cached : null;
        }

        List<TileCoord> path = _pool.Count > 0 ? _pool.Pop() : new List<TileCoord>(32);
        path.Clear();
        bool found = job.Phase == TransportPhase.TravelOut
            ? host.Query.Roads.TryFindViewPath(job.Storehouse, job.Building, path)
            : host.Query.Roads.TryFindViewPath(job.Building, job.Storehouse, path);
        if (!found || path.Count < 2)
        {
            path.Clear();
            _pool.Push(path);
            return null;
        }

        _paths[key] = path;
        return path;
    }

    // Where a cart without a road path goes: loading and unloading stand at the right end; pathless travel drives the
    // straight line between the footprint centres.
    private void StandOrLine(CartSlot slot, float progress, out float tileX, out float tileZ, out float yaw)
    {
        if (_host is null)
        {
            tileX = 0f;
            tileZ = 0f;
            yaw = 0f;
            return;
        }

        (float storeX, float storeZ) = BuildingCentre(slot.Storehouse);
        (float buildX, float buildZ) = BuildingCentre(slot.Building);

        float endX;
        float endZ;
        if (slot.Phase == TransportPhase.Load)
        {
            endX = buildX;
            endZ = buildZ;
        }
        else if (slot.Phase == TransportPhase.Unload)
        {
            bool atStore = slot.Direction == TransportDirection.Pickup;
            endX = atStore ? storeX : buildX;
            endZ = atStore ? storeZ : buildZ;
        }
        else if (slot.Phase == TransportPhase.TravelOut)
        {
            endX = storeX + ((buildX - storeX) * progress);
            endZ = storeZ + ((buildZ - storeZ) * progress);
            tileX = endX;
            tileZ = endZ;
            yaw = LifeGround.YawToward(storeX, storeZ, buildX, buildZ);
            return;
        }
        else
        {
            endX = buildX + ((storeX - buildX) * progress);
            endZ = buildZ + ((storeZ - buildZ) * progress);
            tileX = endX;
            tileZ = endZ;
            yaw = LifeGround.YawToward(buildX, buildZ, storeX, storeZ);
            return;
        }

        tileX = endX;
        tileZ = endZ;
        yaw = 0f;
    }

    private (float X, float Z) BuildingCentre(EntityId id)
    {
        if (_host is not null && _host.Query.Map.TryGetBuilding(id, out BuildingInfo building))
        {
            return LifeGround.FootprintCentre(building);
        }

        return (0f, 0f);
    }
}
