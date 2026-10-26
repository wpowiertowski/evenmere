using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Map;

namespace Evenmere.Sim.Systems.Exploration;

/// <summary>
/// The exploration system: fog, expeditions and discoveries. Its cadence is <see cref="Cadence.EveryTick"/>.
/// It reveals the start region (plus neighbours) at new-game time through <c>NewGameSetup</c>, reveals every region
/// for saves written before fog (a load-time default in the constructor, so old worlds stay playable), ticks
/// expeditions down on day starts, and consumes the coastline flag to reveal coastal regions. (M2-06.)
/// </summary>
internal sealed class ExplorationSystem : ISimSystem
{
    private readonly ExplorationState _state;
    private readonly MapState _map;
    private readonly ContentDb _content;

    /// <summary>Initializes a new instance of the <see cref="ExplorationSystem"/> class.</summary>
    /// <param name="state">The exploration state.</param>
    /// <param name="map">The map state, which holds the regions and discoveries.</param>
    /// <param name="content">The loaded content, for lodge tags and grant goods.</param>
    public ExplorationSystem(ExplorationState state, MapState map, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(content);

        _state = state;
        _map = map;
        _content = content;
        state.EnsureSlots(map.Discoveries.Length);

        // Pre-fog saves are handled on the first run, not here: the constructor cannot tell a fresh world
        // (empty map now, filled by LoadMap later) from a restored one, and mutating state here would make
        // a save-and-restore round trip change the hash before either world ticks. Run detects the empty
        // bitset with a populated map and initializes it there, deterministically on both sides.

        Queries = new ExplorationQueries(state, map);
    }

    /// <inheritdoc />
    public string Name => SystemNames.Exploration;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IExplorationQueries Queries { get; }

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);

        router.Register(new StartExpeditionHandler(_state, _map));
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        _state.EnsureSlots(ctx.World.Map.Discoveries.Length);

        // First run with a populated map but an empty bitset: NewGameSetup already reveals synchronously, so this
        // is a fixture world without setup or a pre-fog save. The save format cannot tell a pre-fog save apart —
        // MessagePack leaves keys the old bytes never wrote at their initializers, so a pre-fog save and a fresh
        // world both arrive with the same empty bitset — so history tells them apart instead: a world with counted
        // production or construction (or a high tick) predates fog and is treated as fully explored; a fresh world
        // with a hall reveals the start and its neighbours; a bare map without one lifts all fog to preserve the
        // pre-fog behaviour. Pre-fog saves mark discoveries applied without granting (no windfall) and leave counters zero.
        if (IsEmpty(_state.RevealedRegions) && ctx.World.Map.Width > 0)
        {
            if (HasHistory(ctx))
            {
                RevealAllSilent();
            }
            else if (FindHallRegion(ctx) is { } hallRegion)
            {
                RevealStart(ctx, hallRegion);
            }
            else
            {
                RevealAllSilent();
            }
        }

        // The Great Lighthouse hook (data in M3, behaviour no-op besides revealing): while the flag is set, every
        // coastal region stays revealed. Idempotent, so setting the flag twice changes nothing.
        if (ctx.World.Progression.CoastlineRevealed)
        {
            RevealCoastline(ctx);
        }

        if (!ctx.Clock.IsDayStart)
        {
            return;
        }

        TickExpeditions(ctx);
    }

    /// <summary>
    /// Reveals the start region and its neighbours at new-game time: the smallest reveal rule true to the design
    /// ("the starting region and the coastline in view start revealed"). The hall stands in the start region, which
    /// the start guarantee made coastal, so its neighbours already include the coastline in view. Silent (no events),
    /// but the counters and discovery effects apply, so inspirations reading them see the start.
    /// </summary>
    /// <param name="ctx">The simulation context.</param>
    /// <param name="hallRegion">The region of the Founders' Hall origin tile.</param>
    internal void RevealStart(SimContext ctx, ushort hallRegion)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        _state.EnsureSlots(ctx.World.Map.Discoveries.Length);
        int maxRegion = MaxRegion(ctx.World.Map);
        _state.RevealedRegions = ExpeditionMath.SizedToCover(_state.RevealedRegions, maxRegion);
        ExpeditionMath.SetRevealedBit(_state.RevealedRegions, hallRegion);
        foreach (ushort neighbour in AdjacentRegions(ctx.World.Map, hallRegion))
        {
            ExpeditionMath.SetRevealedBit(_state.RevealedRegions, neighbour);
        }

        // Silent: no RegionRevealed/DiscoveryRevealed events at new-game time, so the M1 slice scenarios keep
        // their exact event counts. Counters and grants still apply, so inspirations see the start.
        foreach (ushort region in new[] { hallRegion }.Concat(AdjacentRegions(ctx.World.Map, hallRegion)))
        {
            CountRegion(ctx, region);
        }

        ApplyDiscoveriesIn(ctx, static _ => true, emitEvents: false);
    }

    private void TickExpeditions(SimContext ctx)
    {
        if (_state.Expeditions.Count == 0)
        {
            return;
        }

        // Ascending lodge-ID order (the table enumerates that way), so the reveal order is deterministic.
        var finished = new List<EntityId>();
        foreach (IdTable<ExpeditionRecord>.Entry entry in _state.Expeditions)
        {
            ExpeditionRecord record = entry.Value;
            int left = record.DaysLeft - 1;
            if (left <= 0)
            {
                finished.Add(entry.Id);
            }
            else
            {
                entry.Value.DaysLeft = left;
            }
        }

        finished.Sort(static (a, b) => a.Value.CompareTo(b.Value));
        foreach (EntityId lodge in finished)
        {
            if (_state.Expeditions.TryGet(lodge, out ExpeditionRecord record))
            {
                _state.Expeditions.Remove(lodge);
                RevealRegion(ctx, record.TargetRegion);
            }
        }
    }

    private void RevealRegion(SimContext ctx, ushort region)
    {
        int maxRegion = MaxRegion(ctx.World.Map);
        _state.RevealedRegions = ExpeditionMath.SizedToCover(_state.RevealedRegions, maxRegion);
        bool already = ExpeditionMath.IsRevealedBit(_state.RevealedRegions, region);
        ExpeditionMath.SetRevealedBit(_state.RevealedRegions, region);
        if (!already)
        {
            CountRegion(ctx, region);
        }

        ctx.Events.Emit(new RegionRevealed(ctx.Tick, region));

        ctx.Counters.Add(CounterKey.Expeditions, 1);
        ApplyDiscoveriesIn(ctx, d => d.Region == region, emitEvents: true);
    }

    private void RevealCoastline(SimContext ctx)
    {
        MapState map = ctx.World.Map;
        int maxRegion = MaxRegion(map);
        _state.RevealedRegions = ExpeditionMath.SizedToCover(_state.RevealedRegions, maxRegion);
        var coastal = new HashSet<ushort>();
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int i = (y * map.Width) + x;
                if ((map.Features[i] & (byte)TileFeatures.Coast) != 0)
                {
                    coastal.Add(map.Region[i]);
                }
            }
        }

        foreach (ushort region in coastal.OrderBy(static r => r))
        {
            if (!ExpeditionMath.IsRevealedBit(_state.RevealedRegions, region))
            {
                ExpeditionMath.SetRevealedBit(_state.RevealedRegions, region);
                CountRegion(ctx, region);
                ctx.Events.Emit(new RegionRevealed(ctx.Tick, region));
            }
        }

        ApplyDiscoveriesIn(ctx, d => coastal.Contains(d.Region), emitEvents: true);
    }

    private static void CountRegion(SimContext ctx, ushort region)
    {
        MapState map = ctx.World.Map;
        TerrainId? dominant = DominantTerrain(map, region);
        if (dominant is { } terrain)
        {
            ctx.Counters.Add(CounterKey.RegionsRevealed(terrain), 1);
        }

        // Deposit discoveries: each distinct deposit kind in the revealed region counts once, so
        // stone/iron conditions can fire. Regions reveal once (the bitset guards every path), so each counts once.
        var seen = new HashSet<DepositKind>();
        for (int i = 0; i < map.Region.Length; i++)
        {
            if (map.Region[i] != region)
            {
                continue;
            }

            byte deposit = map.Deposit[i];
            if (deposit == 0)
            {
                continue;
            }

            var kind = (DepositKind)(deposit - 1);
            if (seen.Add(kind) && kind is >= DepositKind.Stone and <= DepositKind.Copper)
            {
                ctx.Counters.Add(CounterKey.Discovered((DiscoveryKind)(int)kind), 1);
            }
        }
    }

    private void ApplyDiscoveriesIn(SimContext ctx, Func<StoredDiscovery, bool> matches, bool emitEvents)
    {
        MapState map = ctx.World.Map;
        _state.EnsureSlots(map.Discoveries.Length);
        for (int i = 0; i < map.Discoveries.Length; i++)
        {
            if (_state.DiscoveriesApplied[i])
            {
                continue;
            }

            StoredDiscovery discovery = map.Discoveries[i];
            if (!matches(discovery))
            {
                continue;
            }

            if (!ExpeditionMath.IsRevealedBit(_state.RevealedRegions, discovery.Region))
            {
                continue;
            }

            _state.DiscoveriesApplied[i] = true;
            ctx.Counters.Add(CounterKey.Discovered(discovery.Kind), 1);
            if (emitEvents)
            {
                ctx.Events.Emit(new DiscoveryRevealed(ctx.Tick, discovery.Region, discovery.Kind, discovery.X, discovery.Y));
            }

            GrantDiscovery(ctx, discovery.Kind);
        }
    }

    private static void GrantDiscovery(SimContext ctx, DiscoveryKind kind)
    {
        // Rewards are data in content/rules/core.json (M2-06), never constants: ruins pay knowledge now (a Relic
        // great work from M3-03), shipwrecks pay goods, natural wonders pay nothing until Overlooks (M3-02).
        DiscoveryRewards? rewards = ctx.Content.Rules.Discoveries;
        if (rewards is null)
        {
            return;
        }

        switch (kind)
        {
            case DiscoveryKind.Ruin:
                if (rewards.RuinKnowledge > Amount.Zero)
                {
                    ctx.PointsLedger.Credit(PointKind.Knowledge, rewards.RuinKnowledge);
                }

                break;
            case DiscoveryKind.Shipwreck:
                foreach (GoodAmount grant in rewards.ShipwreckGoods)
                {
                    ctx.Storage.Add(grant.Good, grant.Amount);
                }

                break;
            default:
                break;
        }
    }

    private void RevealAllSilent()
    {
        int maxRegion = MaxRegion(_map);
        _state.RevealedRegions = ExpeditionMath.SizedToCover(_state.RevealedRegions, maxRegion);
        for (int y = 0; y < _map.Height; y++)
        {
            for (int x = 0; x < _map.Width; x++)
            {
                ExpeditionMath.SetRevealedBit(_state.RevealedRegions, _map.Region[(y * _map.Width) + x]);
            }
        }

        _state.EnsureSlots(_map.Discoveries.Length);
        for (int i = 0; i < _state.DiscoveriesApplied.Length; i++)
        {
            _state.DiscoveriesApplied[i] = true;
        }
    }

    private static bool HasHistory(SimContext ctx)
    {
        // A world that produced, consumed or completed anything predates fog (M1 and early M2 saves tick to
        // 20000 with full chains); a fresh world at tick 0-1 counts nothing yet. The tick guard covers saves
        // whose counters are still zero.
        if (ctx.Tick > 1000)
        {
            return true;
        }

        Progression.ProgressionState progression = ctx.World.Progression;
        foreach (long made in progression.Produced)
        {
            if (made > 0)
            {
                return true;
            }
        }

        foreach (long ate in progression.Consumed)
        {
            if (ate > 0)
            {
                return true;
            }
        }

        foreach (long built in progression.Built)
        {
            if (built > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEmpty(ulong[] revealed)
    {
        foreach (ulong word in revealed)
        {
            if (word != 0)
            {
                return false;
            }
        }

        return true;
    }

    private ushort? FindHallRegion(SimContext ctx)
    {
        BuildingTypeId hall = _content.Required.FoundersHall;
        foreach (BuildingInfo building in ctx.Queries.Map.Buildings)
        {
            if (building.Type == hall)
            {
                if (building.Origin.IsInside(ctx.World.Map.Width, ctx.World.Map.Height))
                {
                    return ctx.World.Map.Region[(building.Origin.Y * ctx.World.Map.Width) + building.Origin.X];
                }

                return null;
            }
        }

        return null;
    }

    private static int MaxRegion(MapState map)
    {
        int max = 0;
        foreach (ushort region in map.Region)
        {
            if (region > max)
            {
                max = region;
            }
        }

        return max;
    }

    private static ushort[] AdjacentRegions(MapState map, ushort region)
    {
        var neighbours = new HashSet<ushort>();
        int width = map.Width;
        int height = map.Height;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (map.Region[(y * width) + x] != region)
                {
                    continue;
                }

                if (x > 0)
                {
                    ushort candidate = map.Region[(y * width) + x - 1];
                    if (candidate != region)
                    {
                        neighbours.Add(candidate);
                    }
                }

                if (x < width - 1)
                {
                    ushort candidate = map.Region[(y * width) + x + 1];
                    if (candidate != region)
                    {
                        neighbours.Add(candidate);
                    }
                }

                if (y > 0)
                {
                    ushort candidate = map.Region[((y - 1) * width) + x];
                    if (candidate != region)
                    {
                        neighbours.Add(candidate);
                    }
                }

                if (y < height - 1)
                {
                    ushort candidate = map.Region[((y + 1) * width) + x];
                    if (candidate != region)
                    {
                        neighbours.Add(candidate);
                    }
                }
            }
        }

        ushort[] sorted = [.. neighbours];
        Array.Sort(sorted);
        return sorted;
    }

    private static TerrainId? DominantTerrain(MapState map, ushort region)
    {
        // The most common terrain in the region, ties to the lowest ID, so region-biome conditions are stable.
        var counts = new Dictionary<ushort, int>();
        for (int i = 0; i < map.Region.Length; i++)
        {
            if (map.Region[i] != region)
            {
                continue;
            }

            ushort terrain = map.Terrain[i];
            counts[terrain] = counts.GetValueOrDefault(terrain) + 1;
        }

        if (counts.Count == 0)
        {
            return null;
        }

        ushort best = ushort.MaxValue;
        int bestCount = -1;
        foreach ((ushort terrain, int count) in counts)
        {
            if (count > bestCount || (count == bestCount && terrain < best))
            {
                best = terrain;
                bestCount = count;
            }
        }

        return TerrainId.From(best);
    }
}
