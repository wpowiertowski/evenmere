using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Advisors.Commands;

namespace Evenmere.Sim.Systems.Advisors;

/// <summary>
/// The advisors system: gentle, explained notes about problems, each linked to its subject and cause. Its cadence is
/// <see cref="Cadence.Daily"/>: once a day it re-evaluates every rule, confirms the records whose problem is still
/// present, drops the ones whose problem stopped, and fills free showing slots from the queue.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rules.</b> The rules run in a fixed order (the order the task card lists them), with subjects visited in
/// ascending ID order, so new record IDs are deterministic. Building rules reuse the construction ghost's exemptions:
/// a building whose type is a road (<c>RoadCost &gt; 0</c>) or whose placement needs no road is exempt from both
/// road rules, and a storehouse is always exempt from the reach rule (it is in its own reach). Only finished
/// (active) buildings and construction sites are candidates; paused buildings are deliberately off and ruins are gone.
/// </para>
/// <para>
/// <b>Records.</b> One record per live (kind, subject), found by binary search in the sorted array. Confirming a
/// record updates only its last-seen tick. A sweep then removes every record not seen this tick, emitting
/// <see cref="AdviceCleared"/> for the ones that were showing; queued records that clear emit nothing. Showing is
/// sticky: free slots (at most <see cref="MaxShowing"/> showing) fill from the queued records by priority, then
/// first-seen tick, then ID, each newly showing record emitting <see cref="AdviceRaised"/>.
/// </para>
/// <para>
/// <b>Streaks.</b> <see cref="AdviceKind.GoodShortage"/> and <see cref="AdviceKind.NoWorkforce"/> need two consecutive
/// daily evaluations, tracked in the state's streak arrays (capped at 2). The streaks update from the queries every
/// day whether or not a record or suppression exists, so a problem that outlasts its suppression raises a new record
/// on the first evaluation after the suppression ends.
/// </para>
/// </remarks>
internal sealed class AdvisorsSystem : ISimSystem
{
    /// <summary>The most advisor notes showing at once; the rest queue.</summary>
    internal const int MaxShowing = 5;

    /// <summary>A good whose fulfilment is below this (per-mille) counts as short for the day.</summary>
    private const int ShortageBelowPerMille = 800;

    /// <summary>How many consecutive bad daily evaluations raise a shortage or workforce note.</summary>
    private const int StreakNeeded = 2;

    private readonly AdvisorsState _state;
    private readonly ContentDb _content;

    /// <summary>Initializes a new instance of the <see cref="AdvisorsSystem"/> class.</summary>
    /// <param name="state">The advisors slice of the world state.</param>
    /// <param name="content">The loaded content, which sizes the streak arrays and defines the building types.</param>
    public AdvisorsSystem(AdvisorsState state, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);

        _state = state;
        _content = content;
        Queries = new AdvisorsQueries(state);
    }

    /// <inheritdoc />
    public string Name => SystemNames.Advisors;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IAdvisorsQueries Queries { get; }

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);

        router.Register(new DismissAdviceHandler(this));
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        _state.EnsureSlots(_content.Goods.Count, _content.Tiers.Count);
        PruneSuppressions(ctx.Tick);
        EvaluateBuildings(ctx);
        EvaluateGoods(ctx);
        EvaluateHouses(ctx);
        EvaluateTiers(ctx);
        EvaluateFallbacks(ctx);
        EvaluateIdle(ctx);
        SweepStale(ctx);
        RefillShowing(ctx);
    }

    /// <summary>Tests whether an advice ID names a live record.</summary>
    /// <param name="adviceId">The ID.</param>
    /// <returns><see langword="true"/> if a record has that ID.</returns>
    internal bool HasAdvice(int adviceId) => IndexOfId(_state.Records, adviceId) >= 0;

    /// <summary>
    /// Dismisses a record: removes it (emitting <see cref="AdviceCleared"/> when it was showing) and suppresses its
    /// (kind, subject) for exactly one season. Queued records are not promoted here; the next daily run refills.
    /// </summary>
    /// <param name="ctx">The simulation context.</param>
    /// <param name="adviceId">The record's ID, which <see cref="HasAdvice"/> accepted.</param>
    internal void Dismiss(SimContext ctx, int adviceId)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        int index = IndexOfId(_state.Records, adviceId);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(adviceId), $"No advice has ID {adviceId}.");
        }

        AdviceRecord record = _state.Records[index];
        RemoveAt(_state, index);
        if (record.Showing)
        {
            ctx.Events.Emit(new AdviceCleared(ctx.Tick, record.Id, record.Kind));
        }

        UpsertSuppression(_state, new Suppression
        {
            Kind = record.Kind,
            Subject = record.Subject,
            ExpiresTick = ctx.Tick + GameTime.TicksPerSeason,
        });
    }

    // The two road rules share one pass over the buildings in ascending ID order: the road-access check first, then
    // the reach check, matching the fixed rule order. Both reuse the construction ghost's exemptions.
    private void EvaluateBuildings(SimContext ctx)
    {
        foreach (BuildingInfo building in ctx.Queries.Map.Buildings)
        {
            if (building.State is not (BuildingState.Active or BuildingState.Site))
            {
                continue;
            }

            BuildingDef def = _content.Get(building.Type);
            if (def.RoadCost is > 0 || !def.Placement.RoadAccess)
            {
                continue;
            }

            if (!ctx.Queries.Roads.HasRoadAccess(building.Id))
            {
                // Without a road the building cannot be in reach either, so only the road note is raised; the
                // reach check runs only once road access exists, mirroring the construction ghost.
                Confirm(ctx, AdviceKind.NoRoadAccess, SubjectLink.ForBuilding(building.Id));
            }
            else if (def.Storage is null && !ctx.Queries.Roads.IsInStorehouseReach(building.Id))
            {
                Confirm(ctx, AdviceKind.OutsideStorehouseReach, SubjectLink.ForBuilding(building.Id));
            }
        }
    }

    // A good is short for the day when anything wanted it yesterday and its fulfilment is below 800 per-mille. The
    // note needs two such days in a row; its streak updates even while a record or suppression lives, so the count a
    // re-raised note sees is the count the queries report.
    private void EvaluateGoods(SimContext ctx)
    {
        int goods = _content.Goods.Count;
        for (int g = 0; g < goods; g++)
        {
            var good = GoodId.From(checked((ushort)g));
            bool shortToday = ctx.Queries.Needs.DemandYesterday(good) > Amount.Zero
                && ctx.Queries.Needs.Fulfilment(good).PerMille < ShortageBelowPerMille;
            _state.GoodStreak[g] = shortToday ? Math.Min(_state.GoodStreak[g] + 1, StreakNeeded) : 0;
            if (shortToday && _state.GoodStreak[g] >= StreakNeeded)
            {
                Confirm(ctx, AdviceKind.GoodShortage, SubjectLink.ForGood(good));
            }
        }
    }

    // At most one HouseCannotAdvance note per tier, naming a house so the note still links to its subject. For
    // each tier the note stays with its subject house while that house is still blocked in the same tier, so the
    // note is stable when more houses join; otherwise it names the lowest-ID blocked house of the tier. A
    // demolished subject fails the lookups below, so its note lapses and the next lowest house takes over.
    // The lowest blocked house of every tier comes from a single pass over the buildings (the map walks in
    // ascending ID order, so the first blocked house seen for a tier is its lowest); the per-tier work after that
    // is just the stable-subject check and the confirm, in tier order, exactly as before.
    private void EvaluateHouses(SimContext ctx)
    {
        int tiers = _content.Tiers.Count;
        var lowest = new EntityId[tiers];
        foreach (BuildingInfo building in ctx.Queries.Map.Buildings)
        {
            if (ctx.Queries.Population.TryGetHouse(building.Id, out HouseInfo info)
                && info.AdvanceBlocker is AdvanceBlocker.MissingMaterials or AdvanceBlocker.NextTierLocked
                && (uint)info.Tier.Value < (uint)tiers
                && lowest[info.Tier.Value].IsNone)
            {
                lowest[info.Tier.Value] = building.Id;
            }
        }

        for (int t = 0; t < tiers; t++)
        {
            var tier = TierId.From(checked((ushort)t));
            EntityId subject = StableBlockedSubject(ctx, tier);
            if (subject.IsNone)
            {
                subject = lowest[t];
            }

            if (!subject.IsNone)
            {
                Confirm(ctx, AdviceKind.HouseCannotAdvance, SubjectLink.ForBuilding(subject));
            }
        }
    }

    // The subject of a live note that is still a blocked house of the tier, or none. Records are sorted by
    // subject, so the first match is the lowest-ID stable subject.
    private EntityId StableBlockedSubject(SimContext ctx, TierId tier)
    {
        foreach (AdviceRecord record in _state.Records)
        {
            if (record.Kind == AdviceKind.HouseCannotAdvance && IsBlockedHouseOfTier(ctx, record.Subject, tier))
            {
                return record.Subject.AsBuilding();
            }
        }

        return EntityId.None;
    }

    // Whether the subject still names a blocked house of the tier: the building is still there, still a house of
    // that tier, and still blocked on materials or a locked tier. The blocker already implies its needs are met,
    // so the rule reuses Population's answer instead of re-deriving it.
    private static bool IsBlockedHouseOfTier(SimContext ctx, SubjectLink subject, TierId tier)
    {
        if (subject.Kind != SubjectKind.Building)
        {
            return false;
        }

        EntityId house = subject.AsBuilding();
        return ctx.Queries.Map.TryGetBuilding(house, out _)
            && ctx.Queries.Population.TryGetHouse(house, out HouseInfo info)
            && info.Tier == tier
            && info.AdvanceBlocker is AdvanceBlocker.MissingMaterials or AdvanceBlocker.NextTierLocked;
    }

    // A tier is short of workers for the day when its jobs stay unfilled after stepping down: demand past supply that
    // even the substitutes from higher tiers do not cover, two days in a row (ADR-0016). Workers placed from above
    // fill the gap between supply and demand, so only the remainder counts.
    private void EvaluateTiers(SimContext ctx)
    {
        int tiers = _content.Tiers.Count;
        for (int t = 0; t < tiers; t++)
        {
            var tier = TierId.From(checked((ushort)t));
            bool shortToday = ctx.Queries.Workforce.Demand(tier)
                > ctx.Queries.Workforce.Supply(tier) + ctx.Queries.Workforce.FilledFromAbove(tier);
            _state.TierStreak[t] = shortToday ? Math.Min(_state.TierStreak[t] + 1, StreakNeeded) : 0;
            if (shortToday && _state.TierStreak[t] >= StreakNeeded)
            {
                Confirm(ctx, AdviceKind.NoWorkforce, SubjectLink.ForTier(tier));
            }
        }
    }

    // A tier whose every active maker of a basic need is staffed by the tier itself has upgraded away its fallback
    // (M2-30): one note per tier, so the player keeps a lower-tier fallback standing. Needs with no active maker at
    // all are not warnings: nothing was upgraded away, and the shortage rules cover missing production. Structural like
    // the road rules, so it shows on the first day without a streak.
    // One pass over the active buildings feeds every tier's flags: for each maker the pass marks the basic needs it
    // covers and whether a lower tier (or nobody) staffs it, then each tier confirms at most once, in tier order.
    // The confirms match the old per-need rescans exactly: a tier confirms if and only if some basic need has an
    // active maker but no lower-tier one.
    private void EvaluateFallbacks(SimContext ctx)
    {
        int tiers = _content.Tiers.Count;
        int goods = _content.Goods.Count;
        var tierDefs = new TierDef[tiers];
        bool[] eligible = new bool[tiers];
        int[][] goodNeed = new int[tiers][];
        bool[][] goodsAny = new bool[tiers][];
        bool[][] goodsLower = new bool[tiers][];
        bool[][] servicesAny = new bool[tiers][];
        bool[][] servicesLower = new bool[tiers][];
        for (int t = 0; t < tiers; t++)
        {
            TierDef def = _content.Tiers.Items[t];
            tierDefs[t] = def;
            int needGoods = def.BasicGoods.Length;
            int needServices = def.BasicServices.Length;
            int[] map = new int[goods];
            Array.Fill(map, -1);
            if (def.AdvanceFrom is not null)
            {
                // The first tier has nothing lower than it, so it can lose no fallback.
                eligible[t] = true;
                for (int i = 0; i < needGoods; i++)
                {
                    if ((uint)def.BasicGoods[i].Good.Value < (uint)goods)
                    {
                        map[def.BasicGoods[i].Good.Value] = i;
                    }
                }
            }

            goodNeed[t] = map;
            goodsAny[t] = new bool[needGoods];
            goodsLower[t] = new bool[needGoods];
            servicesAny[t] = new bool[needServices];
            servicesLower[t] = new bool[needServices];
        }

        foreach (BuildingInfo building in ctx.Queries.Map.ActiveBuildings)
        {
            if (building.State != BuildingState.Active)
            {
                continue;
            }

            BuildingDef bdef = _content.Get(building.Type);
            if (bdef.Recipe is { } recipe)
            {
                foreach (GoodAmount output in _content.Get(recipe).Outputs)
                {
                    if ((uint)output.Good.Value >= (uint)goods)
                    {
                        continue;
                    }

                    for (int t = 0; t < tiers; t++)
                    {
                        int need = goodNeed[t][output.Good.Value];
                        if (need < 0)
                        {
                            continue;
                        }

                        goodsAny[t][need] = true;
                        if (IsLowerOrNone(tierDefs[t], bdef))
                        {
                            goodsLower[t][need] = true;
                        }
                    }
                }
            }

            if (bdef.Service is { } provided)
            {
                for (int t = 0; t < tiers; t++)
                {
                    if (!eligible[t])
                    {
                        continue;
                    }

                    int need = tierDefs[t].BasicServices.IndexOf(provided.Kind);
                    if (need < 0)
                    {
                        continue;
                    }

                    servicesAny[t][need] = true;
                    if (IsLowerOrNone(tierDefs[t], bdef))
                    {
                        servicesLower[t][need] = true;
                    }
                }
            }
        }

        for (int t = 0; t < tiers; t++)
        {
            if (!eligible[t])
            {
                continue;
            }

            var tier = TierId.From(checked((ushort)t));
            if (FallbackLost(goodsAny[t], goodsLower[t], servicesAny[t], servicesLower[t]))
            {
                Confirm(ctx, AdviceKind.NoLowerTierFallback, SubjectLink.ForTier(tier));
            }
        }
    }

    // Whether some basic need has an active maker but no lower-tier one, from one tier's accumulated flags.
    private static bool FallbackLost(bool[] goodsAny, bool[] goodsLower, bool[] servicesAny, bool[] servicesLower)
    {
        for (int i = 0; i < goodsAny.Length; i++)
        {
            if (goodsAny[i] && !goodsLower[i])
            {
                return true;
            }
        }

        for (int i = 0; i < servicesAny.Length; i++)
        {
            if (servicesAny[i] && !servicesLower[i])
            {
                return true;
            }
        }

        return false;
    }

    private bool IsLowerOrNone(TierDef tier, BuildingDef building)
    {
        if (building.Workforce is not { } spec)
        {
            return true;
        }

        if (spec.Tier.Value >= _content.Tiers.Count)
        {
            // A dangling tier is the reference linter's problem, not this rule's.
            return true;
        }

        return _content.Get(spec.Tier).Order < tier.Order;
    }

    // Knowledge or culture is accruing with nothing left to choose. No streak: idleness shows on the first day.
    // When every tech or civic is done or locked, the points stay banked and no note appears; the sweep below
    // clears a live note the same way once its condition ends.
    private void EvaluateIdle(SimContext ctx)
    {
        if (ctx.Queries.Research.Target is null && ctx.Queries.Research.PointsPerDay > Amount.Zero && HasAvailableTech(ctx))
        {
            Confirm(ctx, AdviceKind.ResearchIdle, SubjectLink.None);
        }

        if (ctx.Queries.Culture.Target is null && ctx.Queries.Culture.PointsPerDay > Amount.Zero && HasAvailableCivic(ctx))
        {
            Confirm(ctx, AdviceKind.CivicIdle, SubjectLink.None);
        }
    }

    // Whether any tech can be chosen now, in ascending ID order, stopping at the first available one.
    private bool HasAvailableTech(SimContext ctx)
    {
        int count = _content.Techs.Count;
        for (int i = 0; i < count; i++)
        {
            if (ctx.Queries.Research.IsAvailable(TechId.From(checked((ushort)i))))
            {
                return true;
            }
        }

        return false;
    }

    // Whether any civic can be chosen now, in ascending ID order, stopping at the first available one.
    private bool HasAvailableCivic(SimContext ctx)
    {
        int count = _content.Civics.Count;
        for (int i = 0; i < count; i++)
        {
            if (ctx.Queries.Culture.IsAvailable(CivicId.From(checked((ushort)i))))
            {
                return true;
            }
        }

        return false;
    }

    // Confirms a live problem: touches the record's last-seen tick, or adds an unshowing record when the pair is
    // neither recorded nor suppressed. The ID comes from the counter and is never reused.
    private void Confirm(SimContext ctx, AdviceKind kind, SubjectLink subject)
    {
        int index = FindRecord(_state.Records, kind, subject);
        if (index >= 0)
        {
            _state.Records[index].LastSeenTick = ctx.Tick;
        }
        else if (!IsSuppressed(_state.Suppressions, kind, subject))
        {
            InsertRecord(_state, new AdviceRecord
            {
                Id = _state.NextId++,
                Kind = kind,
                Subject = subject,
                FirstSeenTick = ctx.Tick,
                LastSeenTick = ctx.Tick,
                Showing = false,
            });
        }
    }

    // Drops every record not seen this tick. A showing record emits AdviceCleared; a queued one clears silently.
    private void SweepStale(SimContext ctx)
    {
        AdviceRecord[] records = _state.Records;
        int kept = 0;
        for (int i = 0; i < records.Length; i++)
        {
            if (records[i].LastSeenTick == ctx.Tick)
            {
                records[kept++] = records[i];
            }
            else if (records[i].Showing)
            {
                ctx.Events.Emit(new AdviceCleared(ctx.Tick, records[i].Id, records[i].Kind));
            }
        }

        if (kept != records.Length)
        {
            Array.Resize(ref records, kept);
            _state.Records = records;
        }
    }

    // Fills free showing slots from the queued records by priority, then first-seen tick, then ID. Showing records
    // stay showing however low their priority has become; each promoted record emits AdviceRaised.
    private void RefillShowing(SimContext ctx)
    {
        int showing = 0;
        foreach (AdviceRecord record in _state.Records)
        {
            if (record.Showing)
            {
                showing++;
            }
        }

        while (showing < MaxShowing)
        {
            int best = -1;
            for (int i = 0; i < _state.Records.Length; i++)
            {
                if (_state.Records[i].Showing)
                {
                    continue;
                }

                if (best < 0 || CompareForShowing(_state.Records[i], _state.Records[best]) < 0)
                {
                    best = i;
                }
            }

            if (best < 0)
            {
                return;
            }

            _state.Records[best].Showing = true;
            AdviceRecord promoted = _state.Records[best];
            ctx.Events.Emit(new AdviceRaised(
                ctx.Tick,
                new AdviceView(promoted.Id, promoted.Kind, promoted.Subject, promoted.FirstSeenTick, promoted.LastSeenTick)));
            showing++;
        }
    }

    // Drops the suppressions whose season is over; the problems they covered raise new records from this run on.
    private void PruneSuppressions(long tick)
    {
        Suppression[] suppressions = _state.Suppressions;
        int kept = 0;
        for (int i = 0; i < suppressions.Length; i++)
        {
            if (suppressions[i].ExpiresTick > tick)
            {
                suppressions[kept++] = suppressions[i];
            }
        }

        if (kept != suppressions.Length)
        {
            Array.Resize(ref suppressions, kept);
            _state.Suppressions = suppressions;
        }
    }

    private static int CompareForShowing(AdviceRecord left, AdviceRecord right)
    {
        int priority = AdvicePriority.Of(left.Kind).CompareTo(AdvicePriority.Of(right.Kind));
        if (priority != 0)
        {
            return priority;
        }

        int firstSeen = left.FirstSeenTick.CompareTo(right.FirstSeenTick);
        if (firstSeen != 0)
        {
            return firstSeen;
        }

        return left.Id.CompareTo(right.Id);
    }

    // The sort key both arrays keep: kind, then subject kind, then subject value, packed into one long.
    private static long SortKey(AdviceKind kind, SubjectLink subject) =>
        ((long)(byte)kind << 40) | ((long)(byte)subject.Kind << 32) | (uint)subject.Value;

    private static int FindRecord(AdviceRecord[] records, AdviceKind kind, SubjectLink subject)
    {
        long key = SortKey(kind, subject);
        int low = 0;
        int high = records.Length - 1;
        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            long midKey = SortKey(records[mid].Kind, records[mid].Subject);
            if (midKey == key)
            {
                return mid;
            }

            if (midKey < key)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return ~low;
    }

    private static void InsertRecord(AdvisorsState state, AdviceRecord record)
    {
        int index = FindRecord(state.Records, record.Kind, record.Subject);
        if (index >= 0)
        {
            throw new InvalidOperationException($"There is already a record for {record.Kind} on {record.Subject}.");
        }

        int at = ~index;
        AdviceRecord[] records = state.Records;
        Array.Resize(ref records, records.Length + 1);
        Array.Copy(records, at, records, at + 1, records.Length - at - 1);
        records[at] = record;
        state.Records = records;
    }

    private static void RemoveAt(AdvisorsState state, int index)
    {
        AdviceRecord[] records = state.Records;
        Array.Copy(records, index + 1, records, index, records.Length - index - 1);
        Array.Resize(ref records, records.Length - 1);
        state.Records = records;
    }

    private static int IndexOfId(AdviceRecord[] records, int adviceId)
    {
        for (int i = 0; i < records.Length; i++)
        {
            if (records[i].Id == adviceId)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsSuppressed(Suppression[] suppressions, AdviceKind kind, SubjectLink subject)
    {
        long key = SortKey(kind, subject);
        int low = 0;
        int high = suppressions.Length - 1;
        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            long midKey = SortKey(suppressions[mid].Kind, suppressions[mid].Subject);
            if (midKey == key)
            {
                return true;
            }

            if (midKey < key)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return false;
    }

    private static void UpsertSuppression(AdvisorsState state, Suppression suppression)
    {
        Suppression[] suppressions = state.Suppressions;
        long key = SortKey(suppression.Kind, suppression.Subject);
        int low = 0;
        int high = suppressions.Length - 1;
        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            long midKey = SortKey(suppressions[mid].Kind, suppressions[mid].Subject);
            if (midKey == key)
            {
                suppressions[mid] = suppression;
                return;
            }

            if (midKey < key)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        Array.Resize(ref suppressions, suppressions.Length + 1);
        Array.Copy(suppressions, low, suppressions, low + 1, suppressions.Length - low - 1);
        suppressions[low] = suppression;
        state.Suppressions = suppressions;
    }
}
