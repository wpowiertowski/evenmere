using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Game.Data.Visuals;
using Evenmere.Game.Host;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction;
using Evenmere.Sim.Systems.Roads;
using Godot;

namespace Evenmere.Game.Rendering.Buildings;

/// <summary>
/// Draws every building as a palette-tinted placeholder mesh, instanced one <see cref="MultiMeshInstance3D"/> per
/// (building type, chunk), keyed by the building's origin chunk. Finished buildings share meshes and differ only by
/// per-instance colour (paused buildings are desaturated); sites are individual nodes with scaffolding and a body scaled
/// by build progress; ruins use a grey, low, roofless variant. Foundations are one plinth multimesh per chunk from a
/// shared unit skirt, so sloped pads never float and never sink.
/// </summary>
/// <remarks>
/// <para>
/// Updates are event-driven and scoped: <see cref="BuildingPlaced"/>, <see cref="ConstructionCompleted"/>,
/// <see cref="BuildingDemolished"/> and <see cref="BuildingUpgraded"/> rebuild only the affected (type, chunk)
/// multimeshes. Pause and resume emit no event, so a cheap 0.4 s re-read of the cached states picks them up by updating
/// instance colours in place (no rebuild). The sim's dirty-chunk queue is left to the terrain renderer untouched.
/// </para>
/// <para>
/// Placement on the terrain: each building sits on a flat pad at the highest corner height of its footprint (from
/// <see cref="GroundSampler"/>, which mirrors the terrain renderer), with a plinth down to the lowest corner.
/// </para>
/// </remarks>
public sealed partial class BuildingRenderer : Node3D
{
    private static readonly Color _activeColour = new(1f, 1f, 1f);
    // Paused buildings are washed out through per-instance colour: the multiplier lifts every channel and cools the
    // balance, blending about 65% of the way toward a light neutral grey. It reads as "switched off" next to the
    // active twin at any zoom, while ruins stay darker and roofless, so the two never read alike.
    private static readonly Color _pausedColour = new(2.8f, 3.0f, 3.4f);

    /// <summary>How often the cached states are re-read for pause and resume, which emit no event, in seconds.</summary>
    public const float StateRefreshSeconds = 0.4f;

    private readonly struct GroupKey : IEquatable<GroupKey>
    {
        public readonly int Type;
        public readonly int ChunkX;
        public readonly int ChunkY;
        public readonly bool Ruin;

        public GroupKey(int type, int chunkX, int chunkY, bool ruin)
        {
            Type = type;
            ChunkX = chunkX;
            ChunkY = chunkY;
            Ruin = ruin;
        }

        public bool Equals(GroupKey other) => Type == other.Type && ChunkX == other.ChunkX && ChunkY == other.ChunkY && Ruin == other.Ruin;
        public override bool Equals(object? obj) => obj is GroupKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Type, ChunkX, ChunkY, Ruin);
        public override string ToString() => $"(type {Type}, Chunk({ChunkX}, {ChunkY}){(Ruin ? ", ruin" : string.Empty)})";
    }

    private sealed class Group
    {
        public readonly List<EntityId> Members = new();
        public MultiMeshInstance3D? Node;
    }

    private sealed class PlinthGroup
    {
        public readonly List<EntityId> Members = new();
        public MultiMeshInstance3D? Node;
    }

    private sealed record CachedBuilding(
        BuildingTypeId Type,
        int ChunkX,
        int ChunkY,
        bool IsRuin,
        BuildingState State,
        int OriginX,
        int OriginY,
        int Width,
        int Height,
        Rotation Rotation,
        float PadY);

    private readonly PlaceholderMeshFactory _factory = new();
    private readonly Dictionary<GroupKey, Group> _groups = new();
    private readonly Dictionary<ChunkCoord, PlinthGroup> _plinths = new();
    private readonly Dictionary<EntityId, CachedBuilding> _cache = new();
    private readonly Dictionary<EntityId, (MeshInstance3D Body, MeshInstance3D Scaffold)> _sites = new();

    private SimHost? _host;
    private bool _initialised;
    private float _refreshTimer;

    /// <summary>Gets how many multimeshes the last event batch rebuilt, for the card's debug evidence.</summary>
    public int RebuiltLastBatch { get; private set; }

    /// <summary>Gets which events the last batch handled, for the card's debug evidence.</summary>
    public string LastBatchEvents { get; private set; } = string.Empty;

    /// <summary>Gets how many multimeshes have been rebuilt in all, for the card's debug evidence.</summary>
    public int TotalRebuilt { get; private set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        _host = GetTree().CurrentScene?.GetNodeOrNull<SimHost>("SimHost");
        if (_host is not null)
        {
            _host.EventsDrained += OnEvents;
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_host is not null)
        {
            _host.EventsDrained -= OnEvents;
        }

        _factory.Dispose();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!_initialised)
        {
            if (_host is { IsRunning: true })
            {
                _factory.LoadMapping();
                FullBuild();
                _initialised = true;
            }

            return;
        }

        if (_host is not { IsRunning: true })
        {
            return;
        }

        RefreshSiteBodies();
        _refreshTimer += (float)delta;
        if (_refreshTimer >= StateRefreshSeconds)
        {
            _refreshTimer = 0f;
            RefreshStates();
        }
    }

    private void OnEvents(IReadOnlyList<SimEvent> events)
    {
        if (!_initialised || _host is null || events.Count == 0)
        {
            return;
        }

        var affected = new List<GroupKey>();
        var affectedPlinths = new List<ChunkCoord>();
        var names = new List<string>();
        foreach (SimEvent ev in events)
        {
            switch (ev)
            {
                case BuildingPlaced placed:
                    AddPlaced(placed.Building, affectedPlinths);
                    names.Add(nameof(BuildingPlaced));
                    break;
                case ConstructionCompleted completed:
                    CompleteSite(completed.Building, affected, affectedPlinths);
                    names.Add(nameof(ConstructionCompleted));
                    break;
                case BuildingDemolished demolished:
                    RemoveBuilding(demolished.Building, affected, affectedPlinths);
                    names.Add(nameof(BuildingDemolished));
                    break;
                case BuildingUpgraded upgraded:
                    ChangeType(upgraded.Building, upgraded.ToType, affected);
                    names.Add(nameof(BuildingUpgraded));
                    break;
                default:
                    break;
            }
        }

        int rebuilt = 0;
        foreach (GroupKey key in affected)
        {
            RebuildGroup(key);
            rebuilt++;
        }

        int plinths = 0;
        foreach (ChunkCoord chunk in affectedPlinths)
        {
            RebuildPlinths(chunk);
            plinths++;
        }

        RebuiltLastBatch = rebuilt;
        LastBatchEvents = string.Join(",", names.Distinct());
        TotalRebuilt += rebuilt;
        if (rebuilt > 0 || plinths > 0)
        {
            GD.Print($"[Evenmere] Buildings: {LastBatchEvents} rebuilt {rebuilt} multimesh(es) and {plinths} plinth chunk(s) in {Describe(affected)} (total {TotalRebuilt})");
        }
    }

    private string Describe(List<GroupKey> affected)
    {
        var parts = new List<string>();
        foreach (GroupKey key in affected)
        {
            string name = _host is not null ? _host.Content.Buildings.NameOf(BuildingTypeId.From((ushort)key.Type)) : key.Type.ToString();
            parts.Add($"({name}, Chunk({key.ChunkX}, {key.ChunkY}){(key.Ruin ? ", ruin" : string.Empty)})");
        }

        return $"[{string.Join(", ", parts)}]";
    }

    private void FullBuild()
    {
        if (_host is null)
        {
            return;
        }

        foreach (Group group in _groups.Values)
        {
            group.Node?.QueueFree();
        }

        foreach (PlinthGroup plinth in _plinths.Values)
        {
            plinth.Node?.QueueFree();
        }

        foreach ((_, (MeshInstance3D body, MeshInstance3D scaffold)) in _sites)
        {
            body.QueueFree();
            scaffold.QueueFree();
        }

        _groups.Clear();
        _plinths.Clear();
        _cache.Clear();
        _sites.Clear();

        int count = 0;
        foreach (BuildingInfo building in _host.Query.Map.Buildings)
        {
            AddToCache(building);
            count++;
        }

        foreach (GroupKey key in _groups.Keys)
        {
            RebuildGroup(key);
            TotalRebuilt++;
        }

        foreach (ChunkCoord chunk in _plinths.Keys)
        {
            RebuildPlinths(chunk);
        }

        GD.Print($"[Evenmere] Buildings: built {count} building(s) in {_groups.Count} multimesh(es) on load");
    }

    private void AddToCache(BuildingInfo building)
    {
        if (_host is null)
        {
            return;
        }

        var chunk = ChunkCoord.Of(building.Origin);
        GroundSampler.FootprintHeights(
            _host.Query.Map, _host.Content, building.Origin.X, building.Origin.Y, building.Width, building.Height,
            out float highest, out float lowest);
        var cached = new CachedBuilding(
            building.Type, chunk.X, chunk.Y, building.State == BuildingState.Ruin, building.State,
            building.Origin.X, building.Origin.Y, building.Width, building.Height, building.Rotation, highest);
        _cache[building.Id] = cached;

        if (building.State == BuildingState.Site)
        {
            AddSiteNodes(building.Id, cached);
        }
        else
        {
            var key = new GroupKey(building.Type.Value, chunk.X, chunk.Y, cached.IsRuin);
            if (!_groups.TryGetValue(key, out Group? group))
            {
                group = new Group();
                _groups[key] = group;
            }

            group.Members.Add(building.Id);
        }

        AddPlinth(building.Id, cached, lowest);
    }

    private void AddPlaced(EntityId id, List<ChunkCoord> affectedPlinths)
    {
        if (_host is null || !_host.Query.Map.TryGetBuilding(id, out BuildingInfo building))
        {
            return;
        }

        AddToCache(building);
        var chunk = ChunkCoord.Of(building.Origin);
        if (!affectedPlinths.Contains(chunk))
        {
            affectedPlinths.Add(chunk);
        }
    }

    private void CompleteSite(EntityId id, List<GroupKey> affected, List<ChunkCoord> affectedPlinths)
    {
        if (!_cache.TryGetValue(id, out CachedBuilding? cached))
        {
            return;
        }

        if (_sites.TryGetValue(id, out (MeshInstance3D Body, MeshInstance3D Scaffold) nodes))
        {
            nodes.Body.QueueFree();
            nodes.Scaffold.QueueFree();
            _sites.Remove(id);
        }

        CachedBuilding updated = cached with { State = BuildingState.Active };
        _cache[id] = updated;
        GroupKey key = new(updated.Type.Value, updated.ChunkX, updated.ChunkY, false);
        if (!_groups.TryGetValue(key, out Group? group))
        {
            group = new Group();
            _groups[key] = group;
        }

        if (!group.Members.Contains(id))
        {
            group.Members.Add(id);
        }

        if (!affected.Contains(key))
        {
            affected.Add(key);
        }

        ChunkCoord chunk = new(updated.ChunkX, updated.ChunkY);
        if (!affectedPlinths.Contains(chunk))
        {
            affectedPlinths.Add(chunk);
        }
    }

    private void RemoveBuilding(EntityId id, List<GroupKey> affected, List<ChunkCoord> affectedPlinths)
    {
        if (!_cache.TryGetValue(id, out CachedBuilding? cached))
        {
            return;
        }

        _cache.Remove(id);
        if (_sites.TryGetValue(id, out (MeshInstance3D Body, MeshInstance3D Scaffold) nodes))
        {
            nodes.Body.QueueFree();
            nodes.Scaffold.QueueFree();
            _sites.Remove(id);
        }

        var key = new GroupKey(cached.Type.Value, cached.ChunkX, cached.ChunkY, cached.IsRuin);
        if (_groups.TryGetValue(key, out Group? group))
        {
            group.Members.Remove(id);
            if (!affected.Contains(key))
            {
                affected.Add(key);
            }
        }

        ChunkCoord chunk = new(cached.ChunkX, cached.ChunkY);
        if (_plinths.TryGetValue(chunk, out PlinthGroup? plinths))
        {
            plinths.Members.Remove(id);
            if (!affectedPlinths.Contains(chunk))
            {
                affectedPlinths.Add(chunk);
            }
        }
    }

    private void ChangeType(EntityId id, BuildingTypeId toType, List<GroupKey> affected)
    {
        if (!_cache.TryGetValue(id, out CachedBuilding? cached))
        {
            return;
        }

        var oldKey = new GroupKey(cached.Type.Value, cached.ChunkX, cached.ChunkY, cached.IsRuin);
        if (_groups.TryGetValue(oldKey, out Group? oldGroup))
        {
            oldGroup.Members.Remove(id);
            if (!affected.Contains(oldKey))
            {
                affected.Add(oldKey);
            }
        }

        CachedBuilding updated = cached with { Type = toType };
        _cache[id] = updated;
        GroupKey newKey = new(toType.Value, updated.ChunkX, updated.ChunkY, updated.IsRuin);
        if (!_groups.TryGetValue(newKey, out Group? newGroup))
        {
            newGroup = new Group();
            _groups[newKey] = newGroup;
        }

        if (!newGroup.Members.Contains(id))
        {
            newGroup.Members.Add(id);
        }

        if (!affected.Contains(newKey))
        {
            affected.Add(newKey);
        }
    }

    private void AddSiteNodes(EntityId id, CachedBuilding cached)
    {
        if (_host is null)
        {
            return;
        }

        string typeName = _host.Content.Buildings.NameOf(cached.Type);
        BuildingDef def = _host.Content.Buildings.Get(cached.Type);
        ArrayMesh bodyMesh = _factory.BodyFor(typeName, def.Footprint.Width, def.Footprint.Height);
        // The scaffold stands at the finished height: the kit mesh height when a spec exists, else the estimate.
        float height = _factory.FinishedHeightFor(typeName, def.Footprint.Width, def.Footprint.Height);
        ArrayMesh scaffoldMesh = _factory.ScaffoldFor(typeName, cached.Width, cached.Height, height);

        (float cx, float cz) = CentreOf(cached);
        var yaw = new Basis(Vector3.Up, -(float)(byte)cached.Rotation * MathF.PI / 2f);
        float progress = _host.Query.Construction.TryGetSite(id, out SiteInfo site)
            ? Math.Max(0.06f, site.Progress.PerMille / 1000f)
            : 0.06f;

        var body = new MeshInstance3D
        {
            Mesh = bodyMesh,
            Transform = new Transform3D(yaw.Scaled(new Vector3(1f, progress, 1f)), new Vector3(cx, cached.PadY, cz)),
        };
        var scaffold = new MeshInstance3D
        {
            Mesh = scaffoldMesh,
            Transform = new Transform3D(Basis.Identity, new Vector3(cx, cached.PadY, cz)),
        };
        AddChild(body);
        AddChild(scaffold);
        _sites[id] = (body, scaffold);
    }

    private void AddPlinth(EntityId id, CachedBuilding cached, float lowest)
    {
        ChunkCoord chunk = new(cached.ChunkX, cached.ChunkY);
        if (!_plinths.TryGetValue(chunk, out PlinthGroup? group))
        {
            group = new PlinthGroup();
            _plinths[chunk] = group;
        }

        if (!group.Members.Contains(id))
        {
            group.Members.Add(id);
        }
    }

    private void RebuildGroup(GroupKey key)
    {
        if (_host is null || !_groups.TryGetValue(key, out Group? group))
        {
            return;
        }

        if (group.Members.Count == 0)
        {
            group.Node?.QueueFree();
            group.Node = null;
            return;
        }

        string typeName = _host.Content.Buildings.NameOf(BuildingTypeId.From((ushort)key.Type));
        BuildingDef def = _host.Content.Buildings.Get(BuildingTypeId.From((ushort)key.Type));
        ArrayMesh mesh = key.Ruin
            ? _factory.RuinFor(typeName, def.Footprint.Width, def.Footprint.Height)
            : _factory.BodyFor(typeName, def.Footprint.Width, def.Footprint.Height);

        group.Node ??= CreateInstanceNode();
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
        };
        multimesh.InstanceCount = group.Members.Count;
        for (int i = 0; i < group.Members.Count; i++)
        {
            EntityId id = group.Members[i];
            if (!_cache.TryGetValue(id, out CachedBuilding? cached))
            {
                continue;
            }

            (float cx, float cz) = CentreOf(cached);
            var yaw = new Basis(Vector3.Up, -(float)(byte)cached.Rotation * MathF.PI / 2f);
            multimesh.SetInstanceTransform(i, new Transform3D(yaw, new Vector3(cx, cached.PadY, cz)));
            multimesh.SetInstanceColor(i, cached.State == BuildingState.Paused ? _pausedColour : _activeColour);
        }

        group.Node.Multimesh = multimesh;
    }

    private void RebuildPlinths(ChunkCoord chunk)
    {
        if (_host is null || !_plinths.TryGetValue(chunk, out PlinthGroup? group))
        {
            return;
        }

        if (group.Members.Count == 0)
        {
            group.Node?.QueueFree();
            group.Node = null;
            return;
        }

        ArrayMesh mesh = _factory.PlinthUnit();
        group.Node ??= CreateInstanceNode();
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
        };

        int placed = 0;
        var scratch = new Transform3D[group.Members.Count];
        foreach (EntityId id in group.Members)
        {
            if (!_cache.TryGetValue(id, out CachedBuilding? cached))
            {
                continue;
            }

            GroundSampler.FootprintHeights(
                _host.Query.Map, _host.Content, cached.OriginX, cached.OriginY, cached.Width, cached.Height,
                out float highest, out float lowest);
            float drop = highest - lowest;
            if (drop < 0.02f)
            {
                continue;
            }

            BuildingDef def = _host.Content.Buildings.Get(cached.Type);
            (float cx, float cz) = CentreOf(cached);
            var basis = Basis.FromScale(new Vector3(def.Footprint.Width + 0.12f, drop, def.Footprint.Height + 0.12f));
            scratch[placed++] = new Transform3D(basis, new Vector3(cx, highest, cz));
        }

        if (placed == 0)
        {
            group.Node?.QueueFree();
            group.Node = null;
            return;
        }

        multimesh.InstanceCount = placed;
        for (int i = 0; i < placed; i++)
        {
            multimesh.SetInstanceTransform(i, scratch[i]);
            multimesh.SetInstanceColor(i, _activeColour);
        }

        group.Node.Multimesh = multimesh;
    }

    private MultiMeshInstance3D CreateInstanceNode()
    {
        var node = new MultiMeshInstance3D();
        AddChild(node);
        return node;
    }

    private static (float X, float Z) CentreOf(CachedBuilding cached) =>
        (cached.OriginX + (cached.Width / 2f), cached.OriginY + (cached.Height / 2f));

    // Sites grow every tick: rescale the body from its pad base while the scaffolding stands at full height.
    private void RefreshSiteBodies()
    {
        if (_host is null)
        {
            return;
        }

        foreach ((EntityId id, (MeshInstance3D body, _)) in _sites)
        {
            if (!_cache.TryGetValue(id, out CachedBuilding? cached))
            {
                continue;
            }

            if (!_host.Query.Construction.TryGetSite(id, out SiteInfo site))
            {
                continue;
            }

            float progress = Math.Max(0.06f, site.Progress.PerMille / 1000f);
            (float cx, float cz) = CentreOf(cached);
            var yaw = new Basis(Vector3.Up, -(float)(byte)cached.Rotation * MathF.PI / 2f);
            body.Transform = new Transform3D(yaw.Scaled(new Vector3(1f, progress, 1f)), new Vector3(cx, cached.PadY, cz));
        }
    }

    // Pause and resume emit no event: re-read the cached states a few times a second and retint in place.
    private void RefreshStates()
    {
        if (_host is null)
        {
            return;
        }

        foreach (BuildingInfo building in _host.Query.Map.Buildings)
        {
            if (!_cache.TryGetValue(building.Id, out CachedBuilding? cached))
            {
                continue;
            }

            if (cached.State == building.State)
            {
                continue;
            }

            _cache[building.Id] = cached with { State = building.State };
            var key = new GroupKey(cached.Type.Value, cached.ChunkX, cached.ChunkY, cached.IsRuin);
            if (_groups.TryGetValue(key, out Group? group) && group.Node?.Multimesh is { } multimesh)
            {
                int index = group.Members.IndexOf(building.Id);
                if (index >= 0)
                {
                    multimesh.SetInstanceColor(index, building.State == BuildingState.Paused ? _pausedColour : _activeColour);
                }
            }
        }
    }
}
