using System.Numerics;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Needs;

namespace Evenmere.Sim.Systems.Population;

/// <summary>
/// The population system: houses, residents and tiers. Its cadence is <see cref="Cadence.DailyBucketed"/>: on every tick it
/// visits the houses whose bucket (<c>id % 120</c>) is the current tick of the day, so each house is handled once a day
/// (ADR-0003). It runs right after <see cref="NeedsSystem"/>, so on a house's bucket tick the need masks it reads were
/// written moments before, for the house's current tier.
/// </summary>
/// <remarks>
/// <para>For a house on its bucket tick, in this order (progression.md, How needs work):</para>
/// <list type="number">
/// <item><b>Occupancy:</b> <c>target = max(1, capacity × basic needs met ÷ basic needs total)</c>, with <c>capacity</c> the
/// tier's capacity scaled by the <c>capacity</c> modifiers. Residents move one step towards it.</item>
/// <item><b>Outputs:</b> the tier's knowledge and culture per 1,000 residents are credited to the points ledger.</item>
/// <item><b>Advance:</b> every basic need met for <c>advanceStreakDays</c> days in a row, the next tier unlocked, the house
/// not locked and the upgrade materials in the pool: the materials are taken and the tier changes.</item>
/// <item><b>Decline:</b> the decline timer counts days while a basic need that was met at this tier is unmet. At
/// <c>declineGraceDays</c> (scaled by the <c>declineGrace</c> modifiers) the house steps down one tier.</item>
/// </list>
/// <para>Once a day, at the start of the day, it also tells Workforce how many workers each tier supplies.</para>
/// <para>It keeps a live per-tier resident total in the state (ADR-0014): rebuilt at day start and adjusted whenever a
/// house fills, empties, advances, declines or is removed, so each house's advance check sees the houses handled
/// earlier the same day.</para>
/// <para>It listens to the building lifecycle: a residence gets its <see cref="HouseRecord"/> when it becomes active (the
/// lowest tier, one resident) and loses it when the building is removed. M1-13 subscribes it.</para>
/// </remarks>
internal sealed class PopulationSystem : ISimSystem, IBuildingLifecycleListener
{
    private readonly PopulationState _state;
    private readonly ContentDb _content;
    private readonly TierId?[] _nextTier;

    /// <summary>Initializes a new instance of the <see cref="PopulationSystem"/> class.</summary>
    /// <param name="state">The population slice of the world state.</param>
    /// <param name="content">The loaded content, which defines the tiers and the rules.</param>
    /// <exception cref="InvalidDataException">A tier has more needs than a house's masks can hold.</exception>
    public PopulationSystem(PopulationState state, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        NeedLayout.Validate(content);

        _state = state;
        _content = content;
        Queries = new PopulationQueries(state);

        // A tier's definition names the tier below it. Walking in rank order means the lowest rank wins if two tiers claim the same one.
        _nextTier = new TierId?[content.Tiers.Count];
        foreach (TierId id in content.TiersByOrder)
        {
            if (content.Get(id).AdvanceFrom is { } below && _nextTier[below.Value] is null)
            {
                _nextTier[below.Value] = id;
            }
        }
    }

    /// <inheritdoc />
    public string Name => SystemNames.Population;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IPopulationQueries Queries { get; }

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        router.Register(new SetHouseUpgradeLockHandler(this));
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // Before any house is handled today, so the supply is the population as yesterday left it.
        if (ctx.Clock.IsDayStart)
        {
            PublishSupply(ctx);
        }

        foreach (IdTable<HouseRecord>.Entry entry in _state.Houses)
        {
            if (ctx.IsInBucket(entry.Id))
            {
                Process(ctx, entry.Id, ref entry.Value);
            }
        }
    }

    /// <inheritdoc />
    public void OnActivated(SimContext ctx, BuildingInfo building)
    {
        if (_content.Get(building.Type).Category != BuildingCategory.Residence
            || _content.TiersByOrder.IsEmpty
            || _state.Houses.Contains(building.Id))
        {
            return;
        }

        TierId start = _content.TiersByOrder[0];
        var house = new HouseRecord
        {
            Tier = start,
            Type = building.Type,
            Residents = 1,
            TargetResidents = 1,
            AdvanceBlocker = AdvanceBlocker.NeedsUnmet,
        };
        house.Capacity = CapacityOf(ctx, building.Id, in house, _content.Get(start));
        _state.Houses.Add(building.Id, house);
        _state.EnsureTierSlots(_content.Tiers.Count);
        _state.AddTierResidents(start, 1);
    }

    /// <inheritdoc />
    public void OnRemoved(SimContext ctx, BuildingInfo building)
    {
        if (_state.Houses.TryGet(building.Id, out HouseRecord record))
        {
            _state.AddTierResidents(record.Tier, -record.Residents);
            _state.Houses.Remove(building.Id);
        }
    }

    /// <inheritdoc />
    public void OnTypeChanged(SimContext ctx, BuildingInfo building, BuildingTypeId previousType)
    {
        bool isResidence = _content.Get(building.Type).Category == BuildingCategory.Residence;
        if (!_state.Houses.Contains(building.Id))
        {
            if (isResidence && building.IsComplete)
            {
                OnActivated(ctx, building);
            }
        }
        else if (isResidence)
        {
            _state.Houses.GetRef(building.Id).Type = building.Type;
        }
        else
        {
            OnRemoved(ctx, building);
        }
    }

    /// <summary>Tests whether a building is a house, that is a residence that has become active.</summary>
    /// <param name="building">The building.</param>
    /// <returns><see langword="true"/> if the building has a house record.</returns>
    internal bool IsHouse(EntityId building) => _state.Houses.Contains(building);

    /// <summary>Locks or unlocks a house against advancing, and refreshes what the inspector says blocks it.</summary>
    /// <param name="ctx">The simulation context.</param>
    /// <param name="building">The house, which must exist.</param>
    /// <param name="locked"><see langword="true"/> to lock.</param>
    internal void SetUpgradeLock(SimContext ctx, EntityId building, bool locked)
    {
        ref HouseRecord house = ref _state.Houses.GetRef(building);
        house.UpgradeLocked = locked;
        house.AdvanceBlocker = BlockerOf(ctx, in house, _content.Get(house.Tier));
    }

    private static ModifierContext ContextOf(EntityId id, in HouseRecord house) => new(house.Type, id, house.Tier, null);

    // Tells Workforce how many workers each tier supplies, and rebuilds the live per-tier totals from the houses, so a
    // restored save (whose totals array may be missing or stale) agrees with the houses again. Workforce keeps the
    // figure until the next call, so an island that has never had a house has nothing to say, and the call is skipped
    // until the first house exists.
    private void PublishSupply(SimContext ctx)
    {
        _state.EnsureTierSlots(_content.Tiers.Count);
        Array.Clear(_state.TierResidents);
        foreach (IdTable<HouseRecord>.Entry entry in _state.Houses)
        {
            _state.TierResidents[entry.Value.Tier.Value] += entry.Value.Residents;
        }

        if (_state.Houses.Count == 0 && Array.TrueForAll(_state.SuppliedWorkers, static workers => workers == 0))
        {
            return;
        }

        if (_state.SuppliedWorkers.Length != _state.TierResidents.Length)
        {
            _state.SuppliedWorkers = new int[_state.TierResidents.Length];
        }

        for (int tier = 0; tier < _state.TierResidents.Length; tier++)
        {
            ctx.WorkforceSupply.SetSupply(TierId.From((ushort)tier), _state.TierResidents[tier]);
            _state.SuppliedWorkers[tier] = _state.TierResidents[tier];
        }

        AssignUnderemployment(ctx);
    }

    // Morale (ADR-0016): WorkingBelowTier[T] counts the tier-T residents doing lower-tier jobs. Assign them to houses of
    // tier T in ascending house ID, filling each house's residents, so the first houses carry the underemployment. The
    // allocation ran earlier the same tick (pipeline step 3 before step 8), so the counts are today's.
    private void AssignUnderemployment(SimContext ctx)
    {
        int tiers = _state.TierResidents.Length;
        int[] remaining = new int[tiers];
        for (int tier = 0; tier < tiers; tier++)
        {
            remaining[tier] = ctx.Queries.Workforce.WorkingBelowTier(TierId.From(checked((ushort)tier)));
        }

        foreach (IdTable<HouseRecord>.Entry entry in _state.Houses)
        {
            ref HouseRecord house = ref entry.Value;
            int take = 0;
            if ((uint)house.Tier.Value < (uint)remaining.Length)
            {
                take = Math.Min(remaining[house.Tier.Value], house.Residents);
                remaining[house.Tier.Value] -= take;
            }

            house.Underemployed = take;
        }
    }

    /// <summary>Reads the live resident total of a tier: the sum over its houses including today's changes so far.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The residents of the tier's houses; 0 when the tier has no slot yet.</returns>
    internal int LiveTierTotal(TierId tier) =>
        tier.Value < _state.TierResidents.Length ? _state.TierResidents[tier.Value] : 0;

    private void Process(SimContext ctx, EntityId id, ref HouseRecord house)
    {
        TierDef tier = _content.Get(house.Tier);
        int total = NeedLayout.BasicCount(tier);
        uint met = house.NeedsMetMask & NeedLayout.MaskOf(total);
        int metCount = BitOperations.PopCount(met);

        // Occupancy: one step a day towards the target. A tier with no basic needs is always fully served.
        int capacity = CapacityOf(ctx, id, in house, tier);
        int target = total == 0 ? capacity : (int)Math.Max(1L, (long)capacity * metCount / total);
        int before = house.Residents;
        int after = before < target ? before + 1 : before > target ? before - 1 : before;
        house.Capacity = capacity;
        house.TargetResidents = target;
        house.Residents = after;
        if (after != before)
        {
            _state.AddTierResidents(house.Tier, after - before);
        }

        // Underemployment never exceeds the residents: a house that empties mid-day keeps only what still fits.
        if (house.Underemployed > house.Residents)
        {
            house.Underemployed = house.Residents;
        }

        if (after > before)
        {
            ctx.Events.Emit(new ResidentsMovedIn(ctx.Tick, id, after - before));
        }

        CreditOutputs(ctx, id, in house, tier);

        // Advance: the streak counts days on which every basic need was met, and holds at the threshold until the house can afford to move up.
        int threshold = _content.Rules.AdvanceStreakDays;
        house.AdvanceStreakDays = metCount == total ? Math.Min(house.AdvanceStreakDays + 1, threshold) : 0;
        AdvanceBlocker blocker = BlockerOf(ctx, in house, tier);
        house.AdvanceBlocker = blocker;
        if (blocker == AdvanceBlocker.None && house.AdvanceStreakDays >= threshold)
        {
            Advance(ctx, id, ref house);
            return;
        }

        RunDeclineTimer(ctx, id, ref house, tier, met);
    }

    // Decline: only a basic need that was met at this tier and is unmet now counts; a need the house never had only holds occupancy down.
    private void RunDeclineTimer(SimContext ctx, EntityId id, ref HouseRecord house, TierDef tier, uint met)
    {
        // There is nothing below the lowest tier to step down to, so the timer has nothing to count towards.
        if (tier.AdvanceFrom is not { } lower)
        {
            house.DeclineTimerDays = 0;
            return;
        }

        // A festival pauses the timer: it neither counts nor resets until the festival ends.
        if (_state.FestivalActive)
        {
            return;
        }

        uint lost = house.EverMetMask & ~met & NeedLayout.MaskOf(NeedLayout.BasicCount(tier));
        if (lost == 0)
        {
            house.DeclineTimerDays = 0;
            return;
        }

        house.DeclineTimerDays++;
        if (house.DeclineTimerDays < GraceDays(ctx, id, in house))
        {
            return;
        }

        TierId from = house.Tier;
        ChangeTier(ctx, id, ref house, lower);
        ctx.Events.Emit(new HouseTierDeclined(ctx.Tick, id, from, lower));
    }

    private void Advance(SimContext ctx, EntityId id, ref HouseRecord house)
    {
        TierId from = house.Tier;
        if (_nextTier[from.Value] is not { } to)
        {
            throw new InvalidOperationException($"{id} has no tier above {from} to advance to; BlockerOf should have said so.");
        }

        // BlockerOf checked the pool, so the takes below all succeed.
        foreach (GoodAmount cost in _content.Get(to).UpgradeCost.Goods)
        {
            if (!ctx.Storage.TryTake(cost.Good, cost.Amount))
            {
                throw new InvalidOperationException($"The pool lost {cost.Amount} of {_content.NameOf(cost.Good)} between the check and the take.");
            }
        }

        ChangeTier(ctx, id, ref house, to);
        ctx.Events.Emit(new HouseTierAdvanced(ctx.Tick, id, from, to));
    }

    // The tier changes in place and the building does not. Everything that described the old tier's needs is cleared: the new
    // tier's needs are evaluated for the first time on the house's next bucket tick, and only then can one count as ever met.
    private void ChangeTier(SimContext ctx, EntityId id, ref HouseRecord house, TierId tierId)
    {
        TierDef tier = _content.Get(tierId);
        TierId from = house.Tier;
        _state.AddTierResidents(from, -house.Residents);
        house.Tier = tierId;
        house.NeedsMetMask = 0;
        house.EverMetMask = 0;
        house.ComfortMetMask = 0;
        house.AdvanceStreakDays = 0;
        house.DeclineTimerDays = 0;
        house.Underemployed = 0;
        house.Capacity = CapacityOf(ctx, id, in house, tier);
        house.Residents = Math.Clamp(house.Residents, 1, house.Capacity);
        _state.AddTierResidents(tierId, house.Residents);
        house.TargetResidents = house.Residents;
        house.AdvanceBlocker = BlockerOf(ctx, in house, tier);
    }

    private AdvanceBlocker BlockerOf(SimContext ctx, in HouseRecord house, TierDef tier)
    {
        uint all = NeedLayout.MaskOf(NeedLayout.BasicCount(tier));
        if ((house.NeedsMetMask & all) != all)
        {
            return AdvanceBlocker.NeedsUnmet;
        }

        if (_nextTier[house.Tier.Value] is not { } next)
        {
            return AdvanceBlocker.NoNextTier;
        }

        if (!ctx.Queries.Progression.IsUnlocked(next))
        {
            return AdvanceBlocker.NextTierLocked;
        }

        if (house.UpgradeLocked)
        {
            return AdvanceBlocker.UpgradeLocked;
        }

        CostSpec cost = _content.Get(next).UpgradeCost;

        // Coin is spent by the economy (M3-01); until it exists a tier that costs coin cannot be afforded.
        if (cost.Coin > Amount.Zero)
        {
            return AdvanceBlocker.MissingMaterials;
        }

        foreach (GoodAmount good in cost.Goods)
        {
            if (ctx.Storage.Get(good.Good) < good.Amount)
            {
                return AdvanceBlocker.MissingMaterials;
            }
        }

        // Morale (ADR-0016), checked after the materials: a house with residents working below their tier may not
        // advance. It waits for enough workers of the lower tier instead, which also keeps the post-Villager tiers
        // from unlocking while Villagers are still covering Settler jobs.
        if (house.Underemployed > 0)
        {
            return AdvanceBlocker.Underemployed;
        }

        // The workforce hold (ADR-0014, stricter in ADR-0016), checked last: with everything else met, a house of tier T
        // advances only if the workers left after it leaves still cover tier T's current jobs, the jobs at its
        // construction sites (planned demand), and a reserve of reservePercent over both, rounded up. With no jobs for
        // the tier, nothing is held.
        int demand = ctx.Queries.Workforce.Demand(house.Tier);
        int planned = ctx.Queries.Workforce.PlannedDemand(house.Tier);
        int need = demand + planned;
        if (need > 0)
        {
            long reserve = ((long)_content.Rules.WorkforceReservePercent * need + 99) / 100;
            if ((long)LiveTierTotal(house.Tier) - house.Residents < need + reserve)
            {
                return AdvanceBlocker.HeldForWorkforce;
            }
        }

        return AdvanceBlocker.None;
    }

    private static int CapacityOf(SimContext ctx, EntityId id, in HouseRecord house, TierDef tier)
    {
        ModifierContext context = ContextOf(id, in house);
        int percent = ctx.Queries.Progression.BonusPercent(Stat.Capacity, in context);
        long scaled = (long)tier.Capacity * Ratio.FromBonusPercent(percent).PerMille / 1000;
        return (int)Math.Max(1L, scaled);
    }

    // The grace period is declineGraceDays scaled by the declineGrace modifiers, so a bonus lengthens it; it is never shorter than a day.
    private int GraceDays(SimContext ctx, EntityId id, in HouseRecord house)
    {
        ModifierContext context = ContextOf(id, in house);
        int percent = ctx.Queries.Progression.BonusPercent(Stat.DeclineGrace, in context);
        long days = (long)_content.Rules.DeclineGraceDays * Ratio.FromBonusPercent(percent).PerMille / 1000;
        return (int)Math.Max(1L, days);
    }

    // The tier's outputs are per 1,000 residents per day. Each comfort good met adds 25% (plus the comfortBonus modifiers),
    // and the output modifier of each kind applies once, so everything is multiplied out before the single division.
    private static void CreditOutputs(SimContext ctx, EntityId id, in HouseRecord house, TierDef tier)
    {
        OutputsSpec outputs = tier.Outputs;

        // Coin belongs to the economy (M3-01), and the ledger has no kind for creativity (great works arrive in M2).
        if (outputs.Knowledge <= Amount.Zero && outputs.Culture <= Amount.Zero)
        {
            return;
        }

        ModifierContext context = ContextOf(id, in house);
        IProgressionQueries progression = ctx.Queries.Progression;
        int comfortMet = BitOperations.PopCount(house.ComfortMetMask & NeedLayout.MaskOf(tier.ComfortGoods.Length));
        long comfortMultiplier = 1000;
        if (comfortMet > 0)
        {
            long perGood = Math.Max(0L, 250L + (10L * progression.BonusPercent(Stat.ComfortBonus, in context)));
            comfortMultiplier += perGood * comfortMet;
        }

        Credit(ctx, PointKind.Knowledge, outputs.Knowledge, progression.BonusPercent(Stat.OutputKnowledge, in context), house.Residents, comfortMultiplier);
        Credit(ctx, PointKind.Culture, outputs.Culture, progression.BonusPercent(Stat.OutputCulture, in context), house.Residents, comfortMultiplier);
    }

    private static void Credit(SimContext ctx, PointKind kind, Amount per1000, int bonusPercent, int residents, long comfortMultiplier)
    {
        if (per1000 <= Amount.Zero)
        {
            return;
        }

        long scale = residents * comfortMultiplier * Ratio.FromBonusPercent(bonusPercent).PerMille;
        Amount amount = per1000.MulDiv(scale, 1_000_000_000L);
        if (amount > Amount.Zero)
        {
            ctx.PointsLedger.Credit(kind, amount);
        }
    }
}
