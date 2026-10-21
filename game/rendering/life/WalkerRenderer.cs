using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Game.Host;
using Evenmere.Game.Host.Debug;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Rendering.Life;

/// <summary>
/// Purely visual townsfolk (architecture.md, Rendering: life): per house, <c>residents / 4</c> walkers rounded down and
/// at least one while anyone lives there, seeded deterministically from the house entity ID. Each walker ambles a
/// cached road route between its house, the nearest market and a workplace, forth and back. One MultiMesh draws them
/// all; the coordinator caps the visible count by zoom.
/// </summary>
/// <remarks>
/// Route finding is budgeted (<see cref="MaxResolvesPerRefresh"/> per refresh) and cached per (house, destination),
/// so pathfinding never runs per frame. Walkers without a route yet stand by their house door. Fill order is ascending
/// house ID, so zoom thinning always keeps the oldest streets.
/// </remarks>
internal sealed partial class WalkerRenderer : Node3D
{
    /// <summary>How many walkers one house spawns per resident.</summary>
    public const int ResidentsPerWalker = 4;

    /// <summary>How many walkers are drawn at most, whatever the town holds.</summary>
    public const int MaxVisible = 1500;

    /// <summary>How many routes one refresh may compute; the rest wait for the next refresh.</summary>
    public const int MaxResolvesPerRefresh = 8;

    private readonly record struct RouteKey(EntityId From, EntityId To);

    private sealed class WalkerSlot
    {
        public EntityId House;
        public EntityId Destination;
        public float DoorX;
        public float DoorZ;
        public uint Phase;
    }

    private static readonly Color[] _tunics =
    [
        Color.Color8(150, 110, 80).SrgbToLinear(),
        Color.Color8(110, 130, 120).SrgbToLinear(),
        Color.Color8(140, 100, 110).SrgbToLinear(),
        Color.Color8(120, 120, 140).SrgbToLinear(),
        Color.Color8(100, 140, 110).SrgbToLinear(),
        Color.Color8(160, 140, 100).SrgbToLinear(),
    ];

    private readonly Dictionary<RouteKey, List<TileCoord>> _routes = new();
    private readonly Dictionary<RouteKey, long> _retryAfter = new();
    private readonly Stack<List<TileCoord>> _pool = new();
    private readonly List<EntityId> _markets = new();
    private readonly List<EntityId> _workplaces = new();

    private SimHost? _host;
    private MultiMeshInstance3D? _walkers;
    private WalkerSlot[] _slots = [];
    private readonly List<EntityId> _anchors = new();
    private int _total;
    private int _shown = -1;
    private int _syntheticWalkers;

    /// <summary>Gets how many walkers the town holds (before zoom thinning).</summary>
    public int TotalWalkers => _total;

    /// <summary>Builds the mesh and node once the world is running.</summary>
    /// <param name="host">The running simulation host.</param>
    public void Initialise(SimHost host)
    {
        _host = host;
        // Debug-only bench override: synthetic walkers spread over the existing buildings, ignoring the residents
        // rule but using the same routes, caching and MultiMesh path. Zero keeps the rule.
        _syntheticWalkers = Math.Max(0, DebugLaunchOptions.Parse(Godot.OS.GetCmdlineUserArgs()).LifeWalkers);
        _walkers = new MultiMeshInstance3D
        {
            Name = "Walkers",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = LifeMeshBuilder.BuildWalker(),
            },
        };
        AddChild(_walkers);
    }

    /// <summary>
    /// Re-reads the houses after a tick batch into reused slots (ascending house ID) and resolves a few missing
    /// routes. Instance colours are set here; transforms move in <see cref="UpdateFrame"/>.
    /// </summary>
    public void Refresh()
    {
        if (_host is null || _walkers is null)
        {
            return;
        }

        _markets.Clear();
        _workplaces.Clear();
        foreach (BuildingInfo building in _host.Query.Map.ActiveBuildings)
        {
            if (building.State is not (BuildingState.Active or BuildingState.Paused))
            {
                continue;
            }

            BuildingDef def = _host.Content.Buildings.Get(building.Type);
            if (def.Service?.Kind == ServiceKind.Market)
            {
                _markets.Add(building.Id);
            }
            else if (def.Workforce is not null && !_host.Query.Population.TryGetHouse(building.Id, out _))
            {
                _workplaces.Add(building.Id);
            }
        }

        int needed = 0;
        if (_syntheticWalkers > 0)
        {
            needed = Math.Min(_syntheticWalkers, MaxVisible);
        }
        else
        {
            foreach (BuildingInfo building in _host.Query.Map.ActiveBuildings)
            {
                if (!_host.Query.Population.TryGetHouse(building.Id, out HouseInfo house) || house.Residents <= 0)
                {
                    continue;
                }

                int walkers = house.Residents / ResidentsPerWalker;
                if (walkers <= 0)
                {
                    walkers = 1;
                }

                needed += walkers;
                if (needed >= MaxVisible)
                {
                    needed = MaxVisible;
                    break;
                }
            }
        }

        if (_slots.Length < needed)
        {
            int grown = Math.Max(needed, 32);
            Array.Resize(ref _slots, grown);
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] ??= new WalkerSlot();
            }
        }

        _total = 0;
        if (_syntheticWalkers > 0)
        {
            FillSynthetic(needed);
        }
        else
        {
            foreach (BuildingInfo building in _host.Query.Map.ActiveBuildings)
            {
                if (_total >= MaxVisible)
                {
                    break;
                }

                if (!_host.Query.Population.TryGetHouse(building.Id, out HouseInfo house) || house.Residents <= 0)
                {
                    continue;
                }

                int walkers = house.Residents / ResidentsPerWalker;
                if (walkers <= 0)
                {
                    walkers = 1;
                }

                EntityId market = Nearest(_markets, building.Origin);
                EntityId workplace = Nearest(_workplaces, building.Origin);
                (float doorX, float doorZ) = LifeGround.FootprintCentre(building);

                for (int w = 0; w < walkers && _total < MaxVisible; w++)
                {
                    AddWalker(building.Id, market, workplace, doorX, doorZ, w);
                }
            }
        }

        ResolveSome(_host.CurrentTick);
        if (_walkers.Multimesh.InstanceCount != Math.Max(_total, 0))
        {
            _walkers.Multimesh.InstanceCount = _total;
        }

        for (int i = 0; i < _total; i++)
        {
            _walkers.Multimesh.SetInstanceColor(i, _tunics[_slots[i].Phase % (uint)_tunics.Length]);
        }

        _walkers.Visible = _total > 0;
    }

    /// <summary>
    /// Rewrites the visible walker transforms for the current frame: forth-and-back ambles along the cached routes,
    /// with a small step bob. Walkers without a route stand by their house door. No allocations.
    /// </summary>
    /// <param name="tick">The completed tick.</param>
    /// <param name="alpha">The interpolation alpha toward the next tick, from 0 to 1.</param>
    /// <param name="visible">How many of the slots to draw (zoom thinning, from the front).</param>
    public void UpdateFrame(long tick, double alpha, int visible)
    {
        if (_host is null || _walkers is null || _total == 0)
        {
            return;
        }

        int shown = Math.Min(visible, _total);
        if (_shown != shown)
        {
            // The multimesh draws the first N instances, which are the oldest streets in ascending house-ID order,
            // so zoom thinning always keeps the same walkers.
            _shown = shown;
            _walkers.Multimesh.InstanceCount = Math.Max(shown, 0);
            _walkers.Visible = shown > 0;
        }

        if (shown == 0)
        {
            return;
        }

        for (int i = 0; i < shown; i++)
        {
            WalkerSlot slot = _slots[i];
            float tileX;
            float tileZ;
            float yaw;
            if (RouteOf(slot) is { Count: >= 2 } route)
            {
                // A slow amble: one full forth-and-back over roughly 24 ticks per road tile.
                float period = Math.Max(16f, route.Count * 24f);
                float along = (tick + (float)alpha + (slot.Phase % 1024)) % period / period;
                float forth = along < 0.5f ? along * 2f : (1f - along) * 2f;
                LifeGround.WalkOnPath(route, forth, out tileX, out tileZ, out yaw);
                if (along >= 0.5f)
                {
                    yaw += MathF.PI;
                }
            }
            else
            {
                tileX = slot.DoorX;
                tileZ = slot.DoorZ;
                yaw = (slot.Phase % 628) / 100f;
            }

            float height = LifeGround.RoadHeight(_host.Query.Map, _host.Content, tileX, tileZ);
            height += MathF.Abs(MathF.Sin(((tick + (float)alpha) * 1.1f) + i)) * 0.02f;
            _walkers.Multimesh.SetInstanceTransform(i, new Transform3D(new Basis(Vector3.Up, yaw), new Vector3(tileX, height, tileZ)));
        }
    }

    private List<TileCoord>? RouteOf(WalkerSlot slot)
    {
        var key = new RouteKey(slot.House, slot.Destination);
        return _routes.TryGetValue(key, out List<TileCoord>? route) && route.Count >= 2 ? route : null;
    }

    // Fills synthetic bench walkers round-robin over the finished buildings, through the same slot, route and
    // colour path as rule-based walkers.
    private void FillSynthetic(int needed)
    {
        if (_host is null)
        {
            return;
        }

        _anchors.Clear();
        foreach (BuildingInfo building in _host.Query.Map.ActiveBuildings)
        {
            _anchors.Add(building.Id);
        }

        if (_anchors.Count == 0)
        {
            return;
        }

        for (int i = 0; i < needed && _total < MaxVisible; i++)
        {
            EntityId anchor = _anchors[i % _anchors.Count];
            if (!_host.Query.Map.TryGetBuilding(anchor, out BuildingInfo building))
            {
                continue;
            }

            EntityId market = Nearest(_markets, building.Origin);
            EntityId workplace = Nearest(_workplaces, building.Origin);
            (float doorX, float doorZ) = LifeGround.FootprintCentre(building);
            AddWalker(anchor, market, workplace, doorX, doorZ, i);
        }
    }

    // Adds one walker slot for a house (or bench anchor): destination cycles market, workplace, home.
    private void AddWalker(EntityId house, EntityId market, EntityId workplace, float doorX, float doorZ, int index)
    {
        uint seed = LifeRandom.Seed(house, (uint)index * 7919u + 13u);
        uint phase = seed;
        LifeRandom.Next(ref phase);

        EntityId destination = (index % 3) switch
        {
            0 when !market.IsNone => market,
            1 when !workplace.IsNone => workplace,
            _ => house,
        };

        WalkerSlot slot = _slots[_total];
        slot.House = house;
        slot.Destination = destination;
        slot.DoorX = doorX + (((seed % 100) / 100f) - 0.5f);
        slot.DoorZ = doorZ + ((((seed / 100) % 100) / 100f) - 0.5f);
        slot.Phase = phase;
        _total++;
    }

    // Computes a few missing routes per refresh: road routes house to destination through the view pathfinder, or a
    // small loop around the footprint for stay-at-home walkers. Failures wait 120 ticks before a retry.
    private void ResolveSome(long tick)
    {
        if (_host is null)
        {
            return;
        }

        int resolved = 0;
        for (int i = 0; i < _total && resolved < MaxResolvesPerRefresh; i++)
        {
            WalkerSlot slot = _slots[i];
            var key = new RouteKey(slot.House, slot.Destination);
            if (_routes.ContainsKey(key))
            {
                continue;
            }

            if (_retryAfter.TryGetValue(key, out long waitUntil) && tick < waitUntil)
            {
                continue;
            }

            if (slot.Destination == slot.House)
            {
                _routes[key] = LoopAround(slot);
                resolved++;
                continue;
            }

            List<TileCoord> path = _pool.Count > 0 ? _pool.Pop() : new List<TileCoord>(32);
            path.Clear();
            if (_host.Query.Roads.TryFindViewPath(slot.House, slot.Destination, path) && path.Count >= 2)
            {
                _routes[key] = path;
                _retryAfter.Remove(key);
                resolved++;
            }
            else
            {
                path.Clear();
                _pool.Push(path);
                _retryAfter[key] = tick + 120;
            }
        }
    }

    // A small closed loop around the house footprint for stay-at-home walkers: no pathfinding, just the tiles hugging
    // the walls, so they pace around their own block.
    private List<TileCoord> LoopAround(WalkerSlot slot)
    {
        List<TileCoord> loop = _pool.Count > 0 ? _pool.Pop() : new List<TileCoord>(16);
        loop.Clear();
        if (_host is not null && _host.Query.Map.TryGetBuilding(slot.House, out BuildingInfo building))
        {
            for (int x = building.Origin.X - 1; x <= building.Origin.X + building.Width; x++)
            {
                loop.Add(new TileCoord(x, building.Origin.Y - 1));
            }

            for (int y = building.Origin.Y; y <= building.Origin.Y + building.Height; y++)
            {
                loop.Add(new TileCoord(building.Origin.X + building.Width, y));
            }

            for (int x = building.Origin.X + building.Width - 1; x >= building.Origin.X - 1; x--)
            {
                loop.Add(new TileCoord(x, building.Origin.Y + building.Height));
            }

            for (int y = building.Origin.Y + building.Height - 1; y >= building.Origin.Y; y--)
            {
                loop.Add(new TileCoord(building.Origin.X - 1, y));
            }
        }

        if (loop.Count < 2)
        {
            // No room for a loop (the house is gone): cache the empty list so it is not rebuilt every refresh.
            loop.Clear();
        }

        return loop;
    }

    private EntityId Nearest(List<EntityId> candidates, TileCoord from)
    {
        EntityId best = EntityId.None;
        int bestDistance = int.MaxValue;
        if (_host is null)
        {
            return best;
        }

        foreach (EntityId id in candidates)
        {
            if (!_host.Query.Map.TryGetBuilding(id, out BuildingInfo building))
            {
                continue;
            }

            int distance = Math.Abs(building.Origin.X - from.X) + Math.Abs(building.Origin.Y - from.Y);
            if (distance < bestDistance || (distance == bestDistance && id < best))
            {
                bestDistance = distance;
                best = id;
            }
        }

        return best;
    }
}
