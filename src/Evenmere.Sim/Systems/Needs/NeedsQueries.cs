using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Population;

namespace Evenmere.Sim.Systems.Needs;

/// <summary>
/// Implements <see cref="INeedsQueries"/> over the needs state and the house records. Everything it reports was stored by
/// the system, at the start of the day or on the house's last daily pass, so it needs no simulation context and is valid
/// between ticks.
/// </summary>
/// <param name="state">The needs slice of the world state.</param>
/// <param name="population">The population slice, which holds each house's need masks.</param>
/// <param name="content">The loaded content, which defines the tiers' needs.</param>
internal sealed class NeedsQueries(NeedsState state, PopulationState population, ContentDb content) : INeedsQueries
{
    /// <inheritdoc />
    public Ratio Fulfilment(GoodId good) => good.Value < state.Shares.Fulfilment.Length ? state.Shares.Fulfilment[good.Value] : Ratio.One;

    /// <inheritdoc />
    public Amount DemandYesterday(GoodId good) => good.Value < state.Shares.Demand.Length ? state.Shares.Demand[good.Value] : Amount.Zero;

    /// <inheritdoc />
    public bool TryGetHouseNeeds(EntityId house, out HouseNeedsInfo needs)
    {
        if (!population.Houses.TryGet(house, out HouseRecord record))
        {
            needs = null!;
            return false;
        }

        TierDef tier = content.Get(record.Tier);
        ImmutableArray<NeedStatus>.Builder statuses = ImmutableArray.CreateBuilder<NeedStatus>(tier.BasicGoods.Length + tier.BasicServices.Length + tier.ComfortGoods.Length);

        for (int i = 0; i < tier.BasicGoods.Length; i++)
        {
            GoodId good = tier.BasicGoods[i].Good;
            bool met = Has(record.NeedsMetMask, i);
            statuses.Add(new NeedStatus(NeedClass.Basic, good, null, met, Has(record.EverMetMask, i), Fulfilment(good), GoodReason(met, in record)));
        }

        for (int j = 0; j < tier.BasicServices.Length; j++)
        {
            int bit = tier.BasicGoods.Length + j;
            bool met = Has(record.NeedsMetMask, bit);
            statuses.Add(new NeedStatus(
                NeedClass.Basic,
                null,
                tier.BasicServices[j],
                met,
                Has(record.EverMetMask, bit),
                met ? Ratio.One : Ratio.Zero,
                met ? NeedUnmetReason.None : NeedUnmetReason.ServiceNotCovered));
        }

        // Comfort needs never cause decline, so "ever met" is not tracked for them and reports today's state.
        for (int k = 0; k < tier.ComfortGoods.Length; k++)
        {
            GoodId good = tier.ComfortGoods[k].Good;
            bool met = Has(record.ComfortMetMask, k);
            statuses.Add(new NeedStatus(NeedClass.Comfort, good, null, met, met, Fulfilment(good), GoodReason(met, in record)));
        }

        needs = new HouseNeedsInfo(house, record.MarketCovered, statuses.MoveToImmutable());
        return true;
    }

    private static bool Has(uint mask, int bit) => (mask & (1u << bit)) != 0;

    // Outside every market a house draws no goods at all; inside one, a good is unmet because there is too little of it to go round.
    private NeedUnmetReason GoodReason(bool met, in HouseRecord record) =>
        met ? NeedUnmetReason.None
        : !record.MarketCovered && content.Rules.MarketGating ? NeedUnmetReason.NoMarketCoverage
        : NeedUnmetReason.LowFulfilment;
}
