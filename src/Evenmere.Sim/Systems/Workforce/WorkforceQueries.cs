using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Workforce;

/// <summary>
/// Implements <see cref="IWorkforceQueries"/> over the workforce state. Supply is the published supply scaled by the
/// <c>workforce</c> modifiers; demand is read live from the building registry, so it answers between allocations too.
/// </summary>
internal sealed class WorkforceQueries(WorkforceState state, ContentDb content, Func<ISimQueries> queries) : IWorkforceQueries
{
    private readonly WorkforceState _state = state;
    private readonly ContentDb _content = content;
    private readonly Func<ISimQueries> _queries = queries;

    /// <inheritdoc />
    public int Supply(TierId tier) => WorkforceMath.EffectiveSupply(RawSupply(tier), BonusPercent(tier));

    /// <inheritdoc />
    public int Demand(TierId tier)
    {
        int total = 0;
        foreach (BuildingInfo building in _queries().Map.ActiveBuildings)
        {
            // Paused buildings are a registry state (ADR-0011): they demand no workers. Sites and ruins are not finished, so they ask for none either.
            // The old staffing of a building under in-place upgrade keeps working until the upgrade completes, so it still counts here.
            if (building.State != BuildingState.Active)
            {
                continue;
            }

            if (_content.Get(building.Type).Workforce is { } spec && spec.Tier == tier)
            {
                total += EffectiveCrewOf(building, spec.Count);
            }
        }

        return total;
    }

    /// <inheritdoc />
    public int PlannedDemand(TierId tier)
    {
        int total = 0;
        foreach (BuildingInfo building in _queries().Map.Buildings)
        {
            // New buildings are sites in the registry with their target type; their workforce asks only once they are
            // finished, so they are planned rather than current demand.
            if (building.State != BuildingState.Site)
            {
                continue;
            }

            if (_content.Get(building.Type).Workforce is { } spec && spec.Tier == tier)
            {
                total += EffectiveCrewOf(building, spec.Count);
            }
        }

        // An in-place upgrade keeps working as its old type, so its old tier's Demand still covers it, and its site
        // counts the new staffing for the new tier here while it builds (M2-30, recording the ADR-0016 decision):
        // the hold then waits for the workers the building will need, not just the ones it needs now.
        foreach (BuildingInfo building in _queries().Map.ActiveBuildings)
        {
            if (building.State != BuildingState.Active)
            {
                continue;
            }

            if (!_queries().Construction.TryGetUpgradeTarget(building.Id, out BuildingTypeId target))
            {
                continue;
            }

            if (_content.Get(target).Workforce is { } spec && spec.Tier == tier)
            {
                total += EffectiveCrewOf(building, spec.Count);
            }
        }

        return total;
    }

    /// <inheritdoc />
    public int WorkingBelowTier(TierId tier) =>
        tier.Value < _state.WorkingBelow.Length ? _state.WorkingBelow[tier.Value] : 0;

    /// <inheritdoc />
    public int FilledFromAbove(TierId tier) =>
        tier.Value < _state.FilledFromAbove.Length ? _state.FilledFromAbove[tier.Value] : 0;

    /// <inheritdoc />
    public int AllocatedWorkers(EntityId building) =>
        _state.Workplaces.TryGet(building, out WorkplaceRecord? record) ? record.Allocated : 0;

    /// <inheritdoc />
    public int SubstitutedWorkers(EntityId building) =>
        _state.Workplaces.TryGet(building, out WorkplaceRecord? record) ? record.Substituted : 0;

    /// <inheritdoc />
    public TierId SubstituteSourceTier(EntityId building) =>
        _state.Workplaces.TryGet(building, out WorkplaceRecord? record) && record.SubstituteTier >= 0
            ? TierId.From(checked((ushort)record.SubstituteTier))
            : default;

    /// <inheritdoc />
    public Ratio FillOf(EntityId building)
    {
        if (!_queries().Map.TryGetBuilding(building, out BuildingInfo info))
        {
            return Ratio.Zero;
        }

        if (_content.Get(info.Type).Workforce is not { } spec)
        {
            return Ratio.One;
        }

        int need = EffectiveCrewOf(info, spec.Count);
        if (need <= 0)
        {
            return Ratio.One;
        }

        int allocated = _state.Workplaces.TryGet(building, out WorkplaceRecord? record) ? record.Allocated : 0;
        int perMille = (int)Math.Min((long)allocated * 1000 / need, 1000);
        return Ratio.FromPerMille(Math.Max(perMille, 0));
    }

    /// <inheritdoc />
    public bool IsHighPriority(EntityId building) =>
        _state.Workplaces.TryGet(building, out WorkplaceRecord? record) && record.HighPriority;

    private int RawSupply(TierId tier) => tier.Value < _state.RawSupply.Length ? _state.RawSupply[tier.Value] : 0;

    private int BonusPercent(TierId tier) =>
        _queries().Progression.BonusPercent(Stat.Workforce, ModifierContext.ForTier(tier));

    // A workplace's effective crew (ADR-0015): its content count reduced by its own <c>crew</c> modifiers. The context
    // names the building as well as its type, so building-targeted modifiers match exactly as they do for productivity.
    private int EffectiveCrewOf(BuildingInfo building, int count) => WorkforceMath.EffectiveCrew(
        count,
        _queries().Progression.BonusPercent(Stat.Crew, ModifierContext.ForBuilding(building.Id, building.Type)));
}
