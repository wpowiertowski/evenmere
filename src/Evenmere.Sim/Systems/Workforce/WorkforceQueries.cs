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
            if (building.State != BuildingState.Active)
            {
                continue;
            }

            if (_content.Get(building.Type).Workforce is { } spec && spec.Tier == tier)
            {
                total += spec.Count;
            }
        }

        return total;
    }

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

        if (spec.Count <= 0)
        {
            return Ratio.One;
        }

        int allocated = _state.Workplaces.TryGet(building, out WorkplaceRecord? record) ? record.Allocated : 0;
        int perMille = (int)Math.Min((long)allocated * 1000 / spec.Count, 1000);
        return Ratio.FromPerMille(Math.Max(perMille, 0));
    }

    /// <inheritdoc />
    public bool IsHighPriority(EntityId building) =>
        _state.Workplaces.TryGet(building, out WorkplaceRecord? record) && record.HighPriority;

    private int RawSupply(TierId tier) => tier.Value < _state.RawSupply.Length ? _state.RawSupply[tier.Value] : 0;

    private int BonusPercent(TierId tier) =>
        _queries().Progression.BonusPercent(Stat.Workforce, ModifierContext.ForTier(tier));
}
