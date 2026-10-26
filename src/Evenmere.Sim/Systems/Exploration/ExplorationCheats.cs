using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Map;

namespace Evenmere.Sim.Systems.Exploration;

/// <summary>
/// The cheat path into fog (ADR-0012, M2-06): reveals regions through the same state the expeditions write, so a
/// tester lifting fog sees exactly what an expedition would reveal. The command handler calls this; it is not a ticked
/// system and draws no randomness.
/// </summary>
internal static class ExplorationCheats
{
    /// <summary>Reveals every region on the island.</summary>
    /// <param name="ctx">The simulation context.</param>
    public static void RevealAll(SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        ExplorationState state = ctx.World.Exploration;
        MapState map = ctx.World.Map;
        state.EnsureSlots(map.Discoveries.Length);

        int maxRegion = 0;
        foreach (ushort region in map.Region)
        {
            if (region > maxRegion)
            {
                maxRegion = region;
            }
        }

        state.RevealedRegions = ExpeditionMath.SizedToCover(state.RevealedRegions, maxRegion);
        var newly = new HashSet<ushort>();
        for (int i = 0; i < map.Region.Length; i++)
        {
            ushort region = map.Region[i];
            if (!ExpeditionMath.IsRevealedBit(state.RevealedRegions, region))
            {
                newly.Add(region);
            }

            ExpeditionMath.SetRevealedBit(state.RevealedRegions, region);
        }

        foreach (ushort region in newly.OrderBy(static r => r))
        {
            CountRegion(ctx, region);
            ctx.Events.Emit(new RegionRevealed(ctx.Tick, region));
        }

        ApplyAllDiscoveries(ctx);
    }

    /// <summary>Reveals one region by number.</summary>
    /// <param name="ctx">The simulation context.</param>
    /// <param name="region">The region to reveal.</param>
    public static void RevealOne(SimContext ctx, ushort region)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        ExplorationState state = ctx.World.Exploration;
        MapState map = ctx.World.Map;
        state.EnsureSlots(map.Discoveries.Length);
        state.RevealedRegions = ExpeditionMath.SizedToCover(state.RevealedRegions, region);

        bool already = ExpeditionMath.IsRevealedBit(state.RevealedRegions, region);
        ExpeditionMath.SetRevealedBit(state.RevealedRegions, region);
        if (!already)
        {
            CountRegion(ctx, region);
            ctx.Events.Emit(new RegionRevealed(ctx.Tick, region));
        }

        ApplyAllDiscoveries(ctx);
    }

    private static void CountRegion(SimContext ctx, ushort region)
    {
        MapState map = ctx.World.Map;
        TerrainId? dominant = DominantTerrain(map, region);
        if (dominant is { } terrain)
        {
            ctx.Counters.Add(CounterKey.RegionsRevealed(terrain), 1);
        }

        HashSet<DepositKind> seen = new();
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

    private static void ApplyAllDiscoveries(SimContext ctx)
    {
        ExplorationState state = ctx.World.Exploration;
        MapState map = ctx.World.Map;
        state.EnsureSlots(map.Discoveries.Length);
        for (int i = 0; i < map.Discoveries.Length; i++)
        {
            if (state.DiscoveriesApplied[i])
            {
                continue;
            }

            StoredDiscovery discovery = map.Discoveries[i];
            if (!ExpeditionMath.IsRevealedBit(state.RevealedRegions, discovery.Region))
            {
                continue;
            }

            state.DiscoveriesApplied[i] = true;
            ctx.Counters.Add(CounterKey.Discovered(discovery.Kind), 1);
            ctx.Events.Emit(new DiscoveryRevealed(ctx.Tick, discovery.Region, discovery.Kind, discovery.X, discovery.Y));
            GrantDiscovery(ctx, discovery.Kind);
        }
    }

    private static void GrantDiscovery(SimContext ctx, DiscoveryKind kind)
    {
        // Rewards are data in content/rules/core.json (M2-06): ruins pay knowledge, shipwrecks pay goods,
        // natural wonders pay nothing until Overlooks (M3-02). Absent rules mean no grants.
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

    private static TerrainId? DominantTerrain(MapState map, ushort region)
    {
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
