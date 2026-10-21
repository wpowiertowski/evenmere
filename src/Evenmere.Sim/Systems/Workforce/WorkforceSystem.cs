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
        // leftover workers always land on the lowest IDs.
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

            demand.Add(new WorkplaceDemand(building.Id, spec.Tier.Value, spec.Count));
        }

        // Stale allocations (a workplace paused, turned into a site, or changed to a type that needs no workers) read as
        // zero until the allocation below refills the demanded ones. No records are added while this runs, so the
        // enumeration stays valid.
        foreach (IdTable<WorkplaceRecord>.Entry entry in _state.Workplaces)
        {
            entry.Value.Allocated = 0;
        }

        for (int tier = 0; tier < tierCount; tier++)
        {
            AllocateTier(ctx, tier, demand);
        }
    }

    private void AllocateTier(SimContext ctx, int tier, List<WorkplaceDemand> demand)
    {
        int remaining = WorkforceMath.EffectiveSupply(
            tier < _state.RawSupply.Length ? _state.RawSupply[tier] : 0,
            ctx.Queries.Progression.BonusPercent(Stat.Workforce, ModifierContext.ForTier(TierId.From(checked((ushort)tier)))));

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
            return;
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

            return;
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
    }

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
