using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Workforce.Commands;

namespace Evenmere.Sim.Systems.Workforce;

/// <summary>
/// The workforce system: each tier's worker supply, allocated daily to the workplaces that ask for it. Its cadence is
/// <see cref="Cadence.Daily"/>.
/// </summary>
/// <remarks>
/// <para>
/// Allocation runs at day start in pipeline step 3, before Population (step 8). It reads the latest supply Population
/// published through <see cref="IWorkforceSupply"/>: Population publishes every tier once a day at day start, so the
/// allocation on a day-start tick sees the publication from the previous day-start tick. The lag is exactly one day and
/// deterministic, because allocation reads only committed state and iterates buildings in ascending ID order.
/// </para>
/// <para>
/// Within a tier, high-priority workplaces fill first in ascending ID order, each up to its full count. The remainder is
/// shared across the other workplaces at an equal per-mille fill, floored; leftover single workers go one each to the
/// lowest IDs. The per-building assigned workers are stored in the state, so <see cref="IWorkforceQueries.FillOf"/>
/// answers between allocations too.
/// </para>
/// <para>
/// Afterwards, idle workers step down (ADR-0016): for tiers from lowest to highest, a tier's unfilled jobs draw idle
/// workers from the next higher tier, then the one above, and so on. The same priority and fair-share rules apply
/// within the tier being filled, so high-priority buildings fill first. The per-tier <c>WorkingBelow</c> and
/// <c>FilledFromAbove</c> totals, and each workplace's substitutes, are stored in the state for the morale, advisor,
/// hold and explain queries.
/// </para>
/// <para>
/// The system listens to the building lifecycle (<see cref="IBuildingLifecycleListener"/>): a record is dropped when its
/// building is removed, which also clears its priority flag. M1-13 subscribes it, in pipeline order.
/// </para>
/// </remarks>
internal sealed class WorkforceSystem : ISimSystem, IBuildingLifecycleListener
{
    private readonly WorkforceState _state;
    private readonly ContentDb _content;

    /// <summary>Initializes a new instance of the <see cref="WorkforceSystem"/> class.</summary>
    /// <param name="state">The workforce state to run over; a state read from a save continues where it left off.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="queries">
    /// A late-bound accessor for the query facade. The roster builds this system before the facade exists, and queries
    /// carry no <see cref="SimContext"/>, so the queries reach the map and progression through this. It is called only
    /// when a query or an allocation runs, never during construction.
    /// </param>
    public WorkforceSystem(WorkforceState state, ContentDb content, Func<ISimQueries> queries)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(queries);

        _state = state;
        _content = content;
        state.EnsureTierSlots(content.Tiers.Count);
        Queries = new WorkforceQueries(state, content, queries);
        Supply = new WorkforceSupply(state, content);
    }

    /// <inheritdoc />
    public string Name => SystemNames.Workforce;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IWorkforceQueries Queries { get; }

    /// <summary>Gets the supply service Population writes through; the composition root hands this to the write services.</summary>
    public IWorkforceSupply Supply { get; }

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);

        router.Register(new SetWorkforcePriorityHandler(_state, _content));
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        Allocate(ctx);
    }

    /// <inheritdoc />
    public void OnActivated(SimContext ctx, BuildingInfo building)
    {
        // Records are created lazily by the allocation and the priority command, so a newly activated workplace simply
        // reports zero fill until the next day start. Nothing to do here.
    }

    /// <inheritdoc />
    public void OnRemoved(SimContext ctx, BuildingInfo building)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // Dropping the record also clears the priority flag, so a later building that reuses nothing (IDs are never
        // reused) starts unflagged; more importantly, a removed workplace keeps no stale allocation.
        _state.Workplaces.Remove(building.Id);
    }

    /// <inheritdoc />
    public void OnTypeChanged(SimContext ctx, BuildingInfo building, BuildingTypeId previousType)
    {
        // The priority flag survives an in-place change of type; the assigned workers are refreshed at the next day
        // start, at most a day away. Nothing to do here.
    }

    private void Allocate(SimContext ctx)
    {
        int tierCount = _content.Tiers.Count;
        _state.EnsureTierSlots(tierCount);

        // The demanders in ascending ID order: ActiveBuildings already walks that way, and every later pass keeps it, so
        // leftover workers always land on the lowest IDs. Each workplace asks for its effective crew (ADR-0015): the
        // content count reduced by the building's <c>crew</c> modifiers, so techs and civics that trim crews flow into
        // the allocation, the stepping down and the hold exactly as the queries report them.
        var demand = new List<WorkplaceDemand>();
        foreach (BuildingInfo building in ctx.Queries.Map.ActiveBuildings)
        {
            if (building.State != BuildingState.Active)
            {
                continue;
            }

            WorkforceSpec? spec = _content.Get(building.Type).Workforce;
            if (spec is null)
            {
                continue;
            }

            demand.Add(new WorkplaceDemand(building.Id, spec.Tier.Value, EffectiveCrewOf(ctx, building, spec.Count)));
        }

        // Stale allocations (a workplace paused, turned into a site, or changed to a type that needs no workers) read as
        // zero until the allocation below refills the demanded ones. No records are added while this runs, so the
        // enumeration stays valid.
        foreach (IdTable<WorkplaceRecord>.Entry entry in _state.Workplaces)
        {
            entry.Value.Allocated = 0;
            entry.Value.Substituted = 0;
            entry.Value.SubstituteTier = -1;
        }

        Array.Clear(_state.WorkingBelow);
        Array.Clear(_state.FilledFromAbove);

        // Phase one: every tier staffs its own workplaces first, so same-tier workers are always used before substitutes.
        int[] used = new int[tierCount];
        for (int tier = 0; tier < tierCount; tier++)
        {
            used[tier] = AllocateTier(ctx, tier, demand);
        }

        // Phase two: stepping down, lowest rank first. A tier's unfilled jobs draw idle workers from the next higher
        // tier in rank order (TiersByOrder, because tier IDs are in string order), then the one above, so the nearest
        // higher tier goes first. Used counts the tier's own placements plus what it already gave to lower tiers, so
        // each idle worker is spent once.
        ImmutableArray<TierId> order = _content.TiersByOrder;
        for (int rank = 0; rank < order.Length; rank++)
        {
            FillFromAbove(ctx, order, rank, demand, used);
        }
    }

    /// <summary>Computes a workplace's effective crew: its content count reduced by its <c>crew</c> modifiers.</summary>
    /// <param name="ctx">The simulation context, for the modifier queries.</param>
    /// <param name="building">The workplace.</param>
    /// <param name="count">The content workforce count.</param>
    /// <returns>The workers the workplace asks for, at least 1.</returns>
    private static int EffectiveCrewOf(SimContext ctx, BuildingInfo building, int count) => WorkforceMath.EffectiveCrew(
        count,
        ctx.Queries.Progression.BonusPercent(Stat.Crew, ModifierContext.ForBuilding(building.Id, building.Type)));

    /// <summary>Computes a tier's effective supply: the published raw supply scaled by the workforce modifiers.</summary>
    /// <param name="ctx">The simulation context, for the modifier queries.</param>
    /// <param name="tier">The tier's index.</param>
    /// <returns>The workers the tier can place.</returns>
    private int EffectiveSupplyOf(SimContext ctx, int tier) => WorkforceMath.EffectiveSupply(
        tier < _state.RawSupply.Length ? _state.RawSupply[tier] : 0,
        ctx.Queries.Progression.BonusPercent(Stat.Workforce, ModifierContext.ForTier(TierId.From(checked((ushort)tier)))));

    private int AllocateTier(SimContext ctx, int tier, List<WorkplaceDemand> demand)
    {
        int remaining = EffectiveSupplyOf(ctx, tier);

        // High-priority workplaces fill first, in ascending ID order, each up to its full count.
        foreach (WorkplaceDemand demander in demand)
        {
            if (demander.Tier != tier || !IsHighPriority(demander.Id))
            {
                continue;
            }

            int give = Math.Min(demander.Count, remaining);
            SetAllocated(demander.Id, give);
            remaining -= give;
        }

        int totalDemand = 0;
        int normalCount = 0;
        foreach (WorkplaceDemand demander in demand)
        {
            if (demander.Tier == tier && !IsHighPriority(demander.Id))
            {
                totalDemand += demander.Count;
                normalCount++;
            }
        }

        if (normalCount == 0)
        {
            return PlacedOf(tier, demand);
        }

        if (remaining >= totalDemand)
        {
            foreach (WorkplaceDemand demander in demand)
            {
                if (demander.Tier == tier && !IsHighPriority(demander.Id))
                {
                    SetAllocated(demander.Id, demander.Count);
                }
            }

            return PlacedOf(tier, demand);
        }

        int fill = WorkforceMath.SharedFill(remaining, totalDemand);
        int assigned = 0;
        foreach (WorkplaceDemand demander in demand)
        {
            if (demander.Tier != tier || IsHighPriority(demander.Id))
            {
                continue;
            }

            int share = (int)((long)demander.Count * fill / 1000);
            SetAllocated(demander.Id, share);
            assigned += share;
        }

        // Leftover single workers go one each to the lowest IDs, never past a full count. The demand list is in ID
        // order, so the first pass covers the common case where fewer than one worker per workplace is left.
        int leftover = remaining - assigned;
        while (leftover > 0)
        {
            bool placed = false;
            foreach (WorkplaceDemand demander in demand)
            {
                if (leftover == 0)
                {
                    break;
                }

                if (demander.Tier != tier || IsHighPriority(demander.Id))
                {
                    continue;
                }

                if (_state.Workplaces.TryGet(demander.Id, out WorkplaceRecord? record) && record.Allocated < demander.Count)
                {
                    record.Allocated++;
                    leftover--;
                    placed = true;
                }
            }

            if (!placed)
            {
                break;
            }
        }

        return PlacedOf(tier, demand);
    }

    /// <summary>Fills a tier's jobs left unfilled by its own workers with idle workers from higher tiers, nearest first.</summary>
    /// <param name="ctx">The simulation context, for the modifier queries.</param>
    /// <param name="order">The tiers from the lowest to the highest rank.</param>
    /// <param name="rank">The rank of the tier being filled.</param>
    /// <param name="demand">Every active workplace's ask, in ascending ID order.</param>
    /// <param name="used">Per tier, the workers placed so far: the tier's own placements plus what it already gave to lower tiers.</param>
    private void FillFromAbove(SimContext ctx, ImmutableArray<TierId> order, int rank, List<WorkplaceDemand> demand, int[] used)
    {
        int tier = order[rank].Value;
        int unfilledHigh = 0;
        int unfilledNormal = 0;
        int normalCount = 0;
        foreach (WorkplaceDemand demander in demand)
        {
            if (demander.Tier != tier)
            {
                continue;
            }

            int shortfall = demander.Count - AllocatedOf(demander.Id);
            if (shortfall <= 0)
            {
                continue;
            }

            if (IsHighPriority(demander.Id))
            {
                unfilledHigh += shortfall;
            }
            else
            {
                unfilledNormal += shortfall;
                normalCount++;
            }
        }

        if (unfilledHigh == 0 && (unfilledNormal == 0 || normalCount == 0))
        {
            return;
        }

        for (int above = rank + 1; above < order.Length && (unfilledHigh > 0 || unfilledNormal > 0); above++)
        {
            int source = order[above].Value;
            int idle = EffectiveSupplyOf(ctx, source) - used[source];
            if (idle <= 0)
            {
                continue;
            }

            // High-priority workplaces fill first, in ascending ID order, each up to its shortfall.
            foreach (WorkplaceDemand demander in demand)
            {
                if (idle == 0 || unfilledHigh == 0)
                {
                    break;
                }

                if (demander.Tier != tier || !IsHighPriority(demander.Id))
                {
                    continue;
                }

                int shortfall = demander.Count - AllocatedOf(demander.Id);
                if (shortfall <= 0)
                {
                    continue;
                }

                int give = Math.Min(shortfall, idle);
                GiveFromAbove(tier, source, demander.Id, give);
                idle -= give;
                used[source] += give;
                unfilledHigh -= give;
            }

            if (idle == 0 || unfilledNormal == 0 || normalCount == 0)
            {
                continue;
            }

            if (idle >= unfilledNormal)
            {
                foreach (WorkplaceDemand demander in demand)
                {
                    if (demander.Tier != tier || IsHighPriority(demander.Id))
                    {
                        continue;
                    }

                    int shortfall = demander.Count - AllocatedOf(demander.Id);
                    if (shortfall > 0)
                    {
                        GiveFromAbove(tier, source, demander.Id, shortfall);
                        idle -= shortfall;
                        used[source] += shortfall;
                        unfilledNormal -= shortfall;
                    }
                }

                continue;
            }

            // The same shared fill as within a tier, over the shortfalls: an equal per-mille share, floored, then
            // leftover single workers one each to the lowest IDs, never past a full count.
            int fill = WorkforceMath.SharedFill(idle, unfilledNormal);
            int assigned = 0;
            foreach (WorkplaceDemand demander in demand)
            {
                if (demander.Tier != tier || IsHighPriority(demander.Id))
                {
                    continue;
                }

                int shortfall = demander.Count - AllocatedOf(demander.Id);
                if (shortfall <= 0)
                {
                    continue;
                }

                int share = (int)((long)shortfall * fill / 1000);
                GiveFromAbove(tier, source, demander.Id, share);
                idle -= share;
                used[source] += share;
                assigned += share;
                unfilledNormal -= share;
            }

            int leftover = idle;
            while (leftover > 0)
            {
                bool placed = false;
                foreach (WorkplaceDemand demander in demand)
                {
                    if (leftover == 0)
                    {
                        break;
                    }

                    if (demander.Tier != tier || IsHighPriority(demander.Id))
                    {
                        continue;
                    }

                    if (AllocatedOf(demander.Id) < demander.Count)
                    {
                        GiveFromAbove(tier, source, demander.Id, 1);
                        leftover--;
                        used[source]++;
                        unfilledNormal--;
                        placed = true;
                    }
                }

                if (!placed)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Gives idle workers of a higher tier to a lower tier's workplace and records the substitution.</summary>
    /// <param name="tier">The tier being filled.</param>
    /// <param name="source">The higher tier the workers come from.</param>
    /// <param name="id">The workplace.</param>
    /// <param name="give">How many workers step down.</param>
    private void GiveFromAbove(int tier, int source, EntityId id, int give)
    {
        if (give == 0)
        {
            return;
        }

        if (!_state.Workplaces.TryGet(id, out WorkplaceRecord? record))
        {
            record = new WorkplaceRecord();
            _state.Workplaces.Add(id, record);
        }

        record.Allocated += give;
        record.Substituted += give;
        if (record.SubstituteTier < 0)
        {
            record.SubstituteTier = source;
        }

        _state.WorkingBelow[source] += give;
        _state.FilledFromAbove[tier] += give;
    }

    /// <summary>Sums what the allocation placed at a tier's workplaces so far.</summary>
    /// <param name="tier">The tier.</param>
    /// <param name="demand">Every active workplace's ask.</param>
    /// <returns>The placed workers.</returns>
    private int PlacedOf(int tier, List<WorkplaceDemand> demand)
    {
        int placed = 0;
        foreach (WorkplaceDemand demander in demand)
        {
            if (demander.Tier == tier)
            {
                placed += AllocatedOf(demander.Id);
            }
        }

        return placed;
    }

    private int AllocatedOf(EntityId id) =>
        _state.Workplaces.TryGet(id, out WorkplaceRecord? record) ? record.Allocated : 0;

    private bool IsHighPriority(EntityId id) =>
        _state.Workplaces.TryGet(id, out WorkplaceRecord? record) && record.HighPriority;

    private void SetAllocated(EntityId id, int allocated)
    {
        if (!_state.Workplaces.TryGet(id, out WorkplaceRecord? record))
        {
            record = new WorkplaceRecord();
            _state.Workplaces.Add(id, record);
        }

        record.Allocated = allocated;
    }

    private readonly record struct WorkplaceDemand(EntityId Id, int Tier, int Count);
}
