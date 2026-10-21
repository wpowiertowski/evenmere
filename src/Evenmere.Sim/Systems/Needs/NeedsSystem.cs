using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Population;

namespace Evenmere.Sim.Systems.Needs;

/// <summary>
/// The needs system: what houses consume and which services cover them. Its cadence is <see cref="Cadence.DailyBucketed"/>:
/// on every tick it visits the houses whose bucket (<c>id % 120</c>) is the current tick of the day, so each house is
/// evaluated once a day (ADR-0003). It runs right before <see cref="PopulationSystem"/>, which reads the masks it writes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Day-start ordering.</b> The fair shares are computed at the very start of <see cref="Run"/> on the first tick of a day,
/// before the bucket loop of that same tick. That tick also belongs to bucket 0, but no house has been touched today yet:
/// Needs runs before Population in the pipeline, and a house's residents change only on its own bucket tick. So the
/// populations the demand is summed over are exactly those yesterday ended with, every house sees the same fair shares
/// whatever its ID or bucket, and a house whose bucket falls late in the day is not shortchanged by the earlier ones.
/// </para>
/// <para>
/// <b>Fair shares.</b> <c>demand(good) = Σ residents × rate ÷ 1000</c> over the houses whose tier needs the good (basic or
/// comfort), and <c>fulfilment(good) = min(1, (stock + producedYesterday) ÷ demand)</c>, or 1 when nothing needs the good.
/// </para>
/// <para>
/// <b>Per house.</b> A good is drawn only inside a market's coverage when the rules gate goods on markets. The house takes
/// <c>residents × rate ÷ 1000 × fulfilment × (1000 + consumption bonus) ÷ 1000</c> of it from the pool, or whatever is left
/// if the pool holds less. The need is met when the share it actually got is at least 50% of what it wanted. A service need
/// is met when the house is covered by that service.
/// </para>
/// </remarks>
internal sealed class NeedsSystem : ISimSystem
{
    // A basic need counts as met from this fulfilment up (progression.md, How needs work).
    private const int MetThreshold = 500;

    private readonly NeedsState _state;
    private readonly PopulationState _population;
    private readonly ContentDb _content;
    private readonly long[] _scaledDemand;

    /// <summary>Initializes a new instance of the <see cref="NeedsSystem"/> class.</summary>
    /// <param name="state">The needs slice of the world state.</param>
    /// <param name="population">
    /// The population slice, whose house records carry the need masks this system writes. Needs and Population are two stages
    /// of one daily pass over the same houses, so they share the record rather than copy it.
    /// </param>
    /// <param name="content">The loaded content, which defines the tiers and the rules.</param>
    /// <exception cref="InvalidDataException">A tier has more needs than a house's masks can hold.</exception>
    public NeedsSystem(NeedsState state, PopulationState population, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(content);
        NeedLayout.Validate(content);

        _state = state;
        _population = population;
        _content = content;
        _scaledDemand = new long[content.Goods.Count];
        Queries = new NeedsQueries(state, population, content);
    }

    /// <inheritdoc />
    public string Name => SystemNames.Needs;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public INeedsQueries Queries { get; }

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // Needs has no commands of its own: what a house consumes follows from its tier.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // The very first run can fall mid-day (the clock starts at tick 0 and the first tick run is tick 1), so it computes the shares too.
        if (ctx.Clock.IsDayStart || _state.Shares.Fulfilment.Length != _scaledDemand.Length)
        {
            ComputeFairShares(ctx);
        }

        foreach (IdTable<HouseRecord>.Entry entry in _population.Houses)
        {
            if (ctx.IsInBucket(entry.Id))
            {
                Evaluate(ctx, entry.Id, ref entry.Value);
            }
        }
    }

    private void ComputeFairShares(SimContext ctx)
    {
        long[] scaled = _scaledDemand;
        Array.Clear(scaled);
        foreach (IdTable<HouseRecord>.Entry entry in _population.Houses)
        {
            TierDef tier = _content.Get(entry.Value.Tier);
            long residents = entry.Value.Residents;
            foreach (GoodAmount need in tier.BasicGoods)
            {
                scaled[need.Good.Value] += residents * need.Amount.Milli;
            }

            foreach (GoodAmount need in tier.ComfortGoods)
            {
                scaled[need.Good.Value] += residents * need.Amount.Milli;
            }
        }

        FairShares shares = _state.Shares;
        if (shares.Demand.Length != scaled.Length)
        {
            shares.Demand = new Amount[scaled.Length];
            shares.Fulfilment = new Ratio[scaled.Length];
        }

        for (int i = 0; i < scaled.Length; i++)
        {
            // The sum is residents × (milli-units per 1,000 residents), so a thousandth of it is the day's demand in milli-units.
            shares.Demand[i] = Amount.FromMilli(scaled[i] / 1000);
            if (scaled[i] == 0)
            {
                shares.Fulfilment[i] = Ratio.One;
                continue;
            }

            // Only goods somebody needs are looked up, so an island without houses asks Production and Storage nothing.
            var good = GoodId.From((ushort)i);
            Amount supply = ctx.Queries.Storage.Stock(good) + ctx.Queries.Production.ProducedYesterday(good);

            // supply ÷ demand in per-mille, with demand = scaled ÷ 1000, divided once so a small demand is not rounded away first.
            long perMille = supply.MulDiv(1_000_000L, scaled[i]).Milli;
            shares.Fulfilment[i] = Ratio.FromPerMille((int)Math.Min(perMille, 1000L));
        }
    }

    private void Evaluate(SimContext ctx, EntityId id, ref HouseRecord house)
    {
        TierDef tier = _content.Get(house.Tier);
        IRoadsQueries roads = ctx.Queries.Roads;

        bool marketCovered = roads.IsCovered(id, ServiceKind.Market);
        bool mayDraw = marketCovered || !_content.Rules.MarketGating;

        uint met = 0;
        for (int i = 0; i < tier.BasicGoods.Length; i++)
        {
            if (Draw(ctx, id, in house, tier.BasicGoods[i], mayDraw) >= MetThreshold)
            {
                met |= 1u << i;
            }
        }

        for (int j = 0; j < tier.BasicServices.Length; j++)
        {
            ServiceKind service = tier.BasicServices[j];
            if (service == ServiceKind.Market ? marketCovered : roads.IsCovered(id, service))
            {
                met |= 1u << (tier.BasicGoods.Length + j);
            }
        }

        // Comfort goods are drawn with the same maths and only raise the tier's output; they never count towards the basic masks.
        uint comfort = 0;
        for (int k = 0; k < tier.ComfortGoods.Length; k++)
        {
            if (Draw(ctx, id, in house, tier.ComfortGoods[k], mayDraw) >= MetThreshold)
            {
                comfort |= 1u << k;
            }
        }

        house.NeedsMetMask = met;
        house.EverMetMask |= met;
        house.ComfortMetMask = comfort;
        house.MarketCovered = marketCovered;
    }

    // Takes one day's share of a good for a house and returns the fulfilment the house actually got, in per-mille.
    private int Draw(SimContext ctx, EntityId id, in HouseRecord house, GoodAmount need, bool mayDraw)
    {
        if (!mayDraw)
        {
            return 0;
        }

        int fair = FulfilmentOf(need.Good).PerMille;
        var context = new ModifierContext(house.Type, id, house.Tier, need.Good);
        int bonusPercent = ctx.Queries.Progression.BonusPercent(Stat.Consumption, in context);

        // residents × rate ÷ 1000 × fulfilment ÷ 1000 × (1000 + bonus) ÷ 1000, rounded down once at the end.
        long scale = house.Residents * (long)fair * Ratio.FromBonusPercent(bonusPercent).PerMille;
        Amount wanted = need.Amount.MulDiv(scale, 1_000_000_000L);
        if (wanted <= Amount.Zero)
        {
            return fair;
        }

        // The pool never goes negative: a house that finds less than its share takes what there is.
        var taken = Amount.Min(wanted, ctx.Storage.Get(need.Good));
        if (taken > Amount.Zero)
        {
            if (!ctx.Storage.TryTake(need.Good, taken))
            {
                throw new InvalidOperationException($"The pool lost {taken} of {_content.NameOf(need.Good)} between the read and the take.");
            }

            ctx.Counters.Add(CounterKey.Consumed(need.Good), taken.Milli);
        }

        return taken >= wanted ? fair : (int)(fair * taken.Milli / wanted.Milli);
    }

    private Ratio FulfilmentOf(GoodId good) => good.Value < _state.Shares.Fulfilment.Length ? _state.Shares.Fulfilment[good.Value] : Ratio.One;
}
