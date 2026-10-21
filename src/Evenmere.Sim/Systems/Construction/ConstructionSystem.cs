using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Construction.Handlers;

namespace Evenmere.Sim.Systems.Construction;

/// <summary>
/// The construction system: sites, building placement, demolition and in-place upgrades. Its cadence is
/// <see cref="Cadence.EveryTick"/>.
/// </summary>
/// <remarks>
/// <para>
/// A site (a new building, or an upgrade of a finished one) advances only while a storehouse reaches it. It draws its
/// bill of materials from the island pool in ten equal steps, each step before the tenth of the work it pays for, and waits
/// with <see cref="StallReason.MissingMaterial"/> when a step's goods are not in the pool. It first works on the tick after
/// it was placed, so a site completes exactly its build time after placement, and one with <c>buildDays</c> 0 on the next tick.
/// </para>
/// <para>
/// Work is counted in milli-ticks: a site gains <c>1000 + 10 × constructionSpeed%</c> per tick (at least 100) and needs
/// <c>buildDays × 120 × 1000</c>, so a site at +25% takes <c>ceil(buildDays × 120 × 1000 / 1250)</c> ticks, and a bonus that
/// arrives mid-build speeds up only the rest of the work.
/// </para>
/// </remarks>
/// <param name="state">The construction state, normally <c>WorldState.Construction</c>.</param>
/// <param name="content">The immutable content.</param>
internal sealed class ConstructionSystem(ConstructionState state, ContentDb content) : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Construction;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IConstructionQueries Queries { get; } = new ConstructionQueries(state, content);

    /// <summary>
    /// Creates a finished building without a site, for the world that new-game setup authors (the Founders' Hall). Only
    /// the registry's own checks apply (inside the map, nothing in the way): the placement rules are for the player, and the
    /// map is hand-made. The building is registered active, so the lifecycle listeners learn of it, and no event is emitted,
    /// because the view builds its first picture from the queries.
    /// </summary>
    /// <param name="ctx">The simulation context.</param>
    /// <param name="type">The building type; a road type is refused, because roads are not buildings.</param>
    /// <param name="x">The column of the footprint's top-left tile after rotation.</param>
    /// <param name="y">The row of the footprint's top-left tile after rotation.</param>
    /// <param name="rotation">The orientation.</param>
    /// <returns>The new building's entity ID.</returns>
    /// <exception cref="ArgumentException">The type is a road, or the footprint leaves the map or overlaps a building.</exception>
    internal static EntityId PlacePreBuilt(SimContext ctx, BuildingTypeId type, int x, int y, Rotation rotation)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.Content.Get(type).RoadCost is not null)
        {
            throw new ArgumentException($"{ctx.Content.NameOf(type)} is a road; roads are placed with PlaceRoad.", nameof(type));
        }

        return ctx.BuildingRegistry.Add(type, x, y, rotation, BuildingState.Active);
    }

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        router.Register(new PlaceBuildingHandler(state));
        router.Register(new DemolishHandler(state));
        router.Register(new UpgradeBuildingHandler(state));
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        IMapQueries map = ctx.Queries.Map;
        foreach (IdTable<SiteRecord>.Entry entry in state.Sites)
        {
            EntityId id = entry.Id;
            ref SiteRecord site = ref entry.Value;

            // A record whose building is gone or no longer in the state it needs is stale; dropping it keeps the table honest.
            if (!map.TryGetBuilding(id, out BuildingInfo building) || !IsExpectedState(site.Kind, building.State))
            {
                state.Sites.Remove(id);
                continue;
            }

            if (Advance(ctx, id, ref site))
            {
                Complete(ctx, id, site, building);
                state.Sites.Remove(id);
            }
        }
    }

    private static bool IsExpectedState(SiteKind kind, BuildingState state) =>
        kind == SiteKind.Build ? state == BuildingState.Site : state is BuildingState.Active or BuildingState.Paused;

    // Gains this tick's work, drawing each step's materials just before the work it pays for. Returns true when the last step is done.
    private static bool Advance(SimContext ctx, EntityId id, ref SiteRecord site)
    {
        // The tick a site is placed in is spent on the command; work starts on the next one.
        if (site.PlacedTick >= ctx.Tick)
        {
            return false;
        }

        if (!ctx.Queries.Roads.IsInStorehouseReach(id))
        {
            site.Stall = StallReason.NoStorehouseReach;
            site.WaitingFor = default;
            return false;
        }

        BuildingDef target = ctx.Content.Get(site.Target);
        long workTotal = SiteMath.WorkTotal(target);
        int bonus = ctx.Queries.Progression.BonusPercent(Stat.ConstructionSpeed, ModifierContext.ForBuilding(id, site.Target));
        long budget = SiteMath.SpeedFor(bonus);

        site.Stall = StallReason.None;
        site.WaitingFor = default;
        while (site.StepsDone < SiteMath.Steps)
        {
            int step = site.StepsDone + 1;
            if (site.StepsDrawn < step && !TryDraw(ctx, target, step, ref site))
            {
                site.Stall = StallReason.MissingMaterial;
                break;
            }

            // What is left of the tick carries into the next step, so the total time is exact however the steps round.
            long needed = SiteMath.StepBoundary(workTotal, step) - site.Work;
            if (needed > budget)
            {
                site.Work += budget;
                break;
            }

            site.Work += needed;
            budget -= needed;
            site.StepsDone = step;
            if (site.Kind == SiteKind.Build && step < SiteMath.Steps)
            {
                ctx.BuildingRegistry.SetStage(id, step);
            }
        }

        return site.StepsDone == SiteMath.Steps;
    }

    // Takes one step's share of every good in the bill, or nothing: the first good the pool lacks is remembered for the inspector.
    private static bool TryDraw(SimContext ctx, BuildingDef target, int step, ref SiteRecord site)
    {
        IStorageService storage = ctx.Storage;
        foreach (GoodAmount line in target.Cost.Goods)
        {
            if (storage.Get(line.Good) < SiteMath.StepAmount(line.Amount, step))
            {
                site.WaitingFor = line.Good;
                return false;
            }
        }

        foreach (GoodAmount line in target.Cost.Goods)
        {
            if (!storage.TryTake(line.Good, SiteMath.StepAmount(line.Amount, step)))
            {
                throw new InvalidOperationException($"The pool held the goods for step {step} a moment ago but could not give them.");
            }
        }

        // TODO(M3-01): charge CostSpec.Coin once the coin economy exists; Era I buildings cost goods only.
        site.StepsDrawn = step;
        return true;
    }

    private static void Complete(SimContext ctx, EntityId id, in SiteRecord site, in BuildingInfo building)
    {
        if (site.Kind == SiteKind.Build)
        {
            // Reset the stage first so the listeners that OnActivated reaches see a finished building, not a site stuck at stage 9.
            ctx.BuildingRegistry.SetStage(id, 0);
            ctx.BuildingRegistry.SetState(id, BuildingState.Active);
            ctx.Events.Emit(new ConstructionCompleted(ctx.Tick, id, site.Target));
            return;
        }

        ctx.BuildingRegistry.SetType(id, site.Target);
        ctx.Events.Emit(new BuildingUpgraded(ctx.Tick, id, building.Type, site.Target));
    }
}
