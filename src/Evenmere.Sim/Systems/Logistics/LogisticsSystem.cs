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

namespace Evenmere.Sim.Systems.Logistics;

/// <summary>
/// The logistics system: carts and transport jobs (ADR-0005). Its cadence is <see cref="Cadence.EveryTick"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every active building with a <c>storage</c> spec owns its spec's carts. Each tick the system reads the open requests
/// from <see cref="IProductionBuffers"/> in (request tick, building ID) order and serves each with the nearest
/// storehouse that has the building in reach and a free cart (ties go to the lower storehouse ID). A request that
/// already has a job — keyed by (building, good, direction) — never starts a second one, because requests stay listed
/// until the goods move.
/// </para>
/// <para>
/// A pickup job runs out, load (<see cref="IProductionBuffers.TryTakeOutput"/>), back, unload (into the pool); a
/// delivery job reserves its goods from the pool at assignment, then runs out, unload (<see cref="IProductionBuffers.AddInput"/>),
/// back. One leg takes <see cref="TravelTicks"/> ticks, at least one; loading and unloading each take
/// <see cref="HandlingTicks"/> ticks. A delivery whose reservation fails waits until the next day before it retries, so
/// a shortage does not retry every tick.
/// </para>
/// <para>
/// The system listens to the building lifecycle: when a job's building or storehouse is removed (or a storehouse loses
/// its storage spec), the job finishes its current phase and returns what it carries to the pool, so no goods are lost.
/// M1-13 subscribes it, in pipeline order.
/// </para>
/// </remarks>
internal sealed class LogisticsSystem : ISimSystem, IBuildingLifecycleListener
{
    /// <summary>
    /// How many ticks a cart stands still to load at a producer or unload at either end: one second at four ticks a
    /// second. A pickup round trip is therefore exactly 2 × leg ticks + twice this.
    /// </summary>
    internal const int HandlingTicks = 4;

    private readonly LogisticsState _state;
    private readonly ContentDb _content;
    private readonly LogisticsQueries _queries;
    private readonly List<TransportRequest> _requests = [];
    private readonly List<long> _served = [];
    private readonly List<StorehouseCandidate> _storehouses = [];

    /// <summary>Initializes a new instance of the <see cref="LogisticsSystem"/> class.</summary>
    /// <param name="state">The logistics slice of the world state; a state read from a save continues where it left off.</param>
    /// <param name="content">The loaded content, for the rules and the carts each storehouse owns.</param>
    /// <param name="queries">
    /// A late-bound accessor for the query facade. The roster builds this system before the facade exists, and queries
    /// carry no <see cref="SimContext"/>, so the map is reached through this. It is called only when a query arrives,
    /// never during construction.
    /// </param>
    public LogisticsSystem(LogisticsState state, ContentDb content, Func<ISimQueries> queries)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(queries);

        _state = state;
        _content = content;
        _queries = new LogisticsQueries(state, content, queries);
    }

    /// <inheritdoc />
    public string Name => SystemNames.Logistics;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public ILogisticsQueries Queries => _queries;

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // Logistics runs on production's requests alone and owns no commands.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        AdvanceJobs(ctx);
        AssignJobs(ctx);
        _queries.Update();
    }

    /// <inheritdoc />
    public void OnActivated(SimContext ctx, BuildingInfo building)
    {
        // Storehouses are discovered every tick from the finished buildings, so activation needs no bookkeeping.
    }

    /// <inheritdoc />
    public void OnRemoved(SimContext ctx, BuildingInfo building)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // The hall's carts go with it. Every job at either end finishes its current phase and returns what it carries
        // to the pool (see FinishInterrupted), so no goods are lost.
        _state.BusyCarts.Remove(building.Id);
        foreach (IdTable<TransportJobRecord>.Entry entry in _state.Jobs)
        {
            if (entry.Value.Storehouse == building.Id || entry.Value.Building == building.Id)
            {
                entry.Value.Interrupted = true;
            }
        }
    }

    /// <inheritdoc />
    public void OnTypeChanged(SimContext ctx, BuildingInfo building, BuildingTypeId previousType)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // An upgrade can take a hall's carts away. Jobs already out finish their phase and return their goods to the pool.
        if (_content.Get(building.Type).Storage is null)
        {
            _state.BusyCarts.Remove(building.Id);
            foreach (IdTable<TransportJobRecord>.Entry entry in _state.Jobs)
            {
                if (entry.Value.Storehouse == building.Id)
                {
                    entry.Value.Interrupted = true;
                }
            }
        }
    }

    /// <summary>
    /// Computes how many ticks one leg takes: the road cost in cost units (1000 per dirt tile) times the cart ticks per
    /// tile, reduced by the island-wide cart-speed bonus, rounded up. The bonus is a percentage (as
    /// <see cref="IProgressionQueries.BonusPercent"/> returns), so it is scaled by ten against the per-mille speed and
    /// the speed never drops below 100 per-mille, no matter how large the bonus. A leg always takes at least one tick.
    /// </summary>
    /// <param name="cost">The road cost from the storehouse to the building, in cost units.</param>
    /// <param name="cartTicksPerTile">The game rule: ticks per tile of road cost 1000.</param>
    /// <param name="cartSpeedBonusPercent">The island-wide cart-speed bonus in percent; positive is faster.</param>
    /// <returns>The leg ticks, at least 1.</returns>
    internal static int TravelTicks(int cost, int cartTicksPerTile, int cartSpeedBonusPercent)
    {
        // A storehouse serving its own footprint still hitches and unhitches the cart.
        if (cost <= 0)
        {
            return 1;
        }

        long speed = 1000L - 10L * cartSpeedBonusPercent;
        if (speed < 100L)
        {
            speed = 100L;
        }

        long ticks = (checked((long)cost * cartTicksPerTile * speed) + 999_999L) / 1_000_000L;
        return ticks < 1 ? 1 : (int)ticks;
    }

    private void AdvanceJobs(SimContext ctx)
    {
        long tick = ctx.Tick;
        foreach (IdTable<TransportJobRecord>.Entry entry in _state.Jobs)
        {
            TransportJobRecord job = entry.Value;
            if (tick < job.PhaseStartTick + job.PhaseTicks)
            {
                continue;
            }

            if (job.Interrupted)
            {
                FinishInterrupted(ctx, entry.Id, job);
                continue;
            }

            switch (job.Phase)
            {
                case TransportPhase.TravelOut:
                    // A pickup arrives empty and starts loading; a delivery arrives loaded and starts unloading.
                    job.Phase = job.Direction == TransportDirection.Pickup ? TransportPhase.Load : TransportPhase.Unload;
                    job.PhaseStartTick = tick;
                    job.PhaseTicks = HandlingTicks;
                    break;
                case TransportPhase.Load:
                    // Pickup only: take the goods into the cart, all or nothing. A cart that finds the buffer short
                    // still drives back and unloads nothing; the goods stay where they are.
                    job.Carried = ctx.ProductionBuffers.TryTakeOutput(job.Building, job.Good, job.Amount)
                        ? job.Amount
                        : Amount.Zero;
                    job.Phase = TransportPhase.TravelBack;
                    job.PhaseStartTick = tick;
                    job.PhaseTicks = job.LegTicks;
                    break;
                case TransportPhase.TravelBack:
                    if (job.Direction == TransportDirection.Pickup)
                    {
                        job.Phase = TransportPhase.Unload;
                        job.PhaseStartTick = tick;
                        job.PhaseTicks = HandlingTicks;
                    }
                    else
                    {
                        // A delivery already unloaded at the building; the cart is home.
                        CompleteJob(ctx, entry.Id, job);
                    }

                    break;
                case TransportPhase.Unload:
                    if (job.Direction == TransportDirection.Pickup)
                    {
                        // The cart is home: the load joins the island pool and the cart is free again.
                        ctx.Storage.Add(job.Good, job.Carried);
                        job.Carried = Amount.Zero;
                        CompleteJob(ctx, entry.Id, job);
                    }
                    else
                    {
                        // The cart is at the consumer: it unloads into the input buffer, then drives home empty.
                        ctx.ProductionBuffers.AddInput(job.Building, job.Good, job.Carried);
                        job.Carried = Amount.Zero;
                        job.Phase = TransportPhase.TravelBack;
                        job.PhaseStartTick = tick;
                        job.PhaseTicks = job.LegTicks;
                    }

                    break;
                default:
                    throw new InvalidDataException($"A transport job is in phase {job.Phase}, which has no next step.");
            }
        }
    }

    private void AssignJobs(SimContext ctx)
    {
        _requests.Clear();
        ctx.ProductionBuffers.ListRequests(_requests);
        if (_requests.Count == 0)
        {
            SweepRetries(ctx.Clock.Day);
            return;
        }

        _served.Clear();
        foreach (IdTable<TransportJobRecord>.Entry entry in _state.Jobs)
        {
            _served.Add(RequestKey(entry.Value.Building, entry.Value.Good, entry.Value.Direction));
        }

        // Every active building with a storage spec owns its spec's carts; paused halls and sites send none out.
        // The list is in ascending ID order, so equal road costs keep the lower storehouse ID.
        _storehouses.Clear();
        foreach (BuildingInfo building in ctx.Queries.Map.ActiveBuildings)
        {
            if (building.State != BuildingState.Active)
            {
                continue;
            }

            if (_content.Get(building.Type).Storage is not { } storage)
            {
                continue;
            }

            int busy = _state.BusyCarts.TryGet(building.Id, out int held) ? held : 0;
            int free = storage.Carts - busy;
            if (free > 0)
            {
                _storehouses.Add(new StorehouseCandidate(building.Id, free));
            }
        }

        ModifierContext island = ModifierContext.Island;
        int bonus = ctx.Queries.Progression.BonusPercent(Stat.CartSpeed, in island);
        int cartTicksPerTile = _content.Rules.CartTicksPerTile;
        long today = ctx.Clock.Day;

        foreach (TransportRequest request in _requests)
        {
            if (!ctx.Queries.Map.TryGetBuilding(request.Building, out _))
            {
                // The building is gone; its request is stale. Production drops it with the buffers.
                continue;
            }

            long key = RequestKey(request.Building, request.Good, request.Direction);
            if (_served.Contains(key))
            {
                // The request stays listed until the goods move; a job is already moving them.
                continue;
            }

            if (request.Direction == TransportDirection.Delivery && IsPostponed(key, today))
            {
                continue;
            }

            int bestIndex = -1;
            int bestCost = 0;
            for (int i = 0; i < _storehouses.Count; i++)
            {
                StorehouseCandidate candidate = _storehouses[i];
                if (candidate.Free <= 0)
                {
                    continue;
                }

                if (!ctx.Queries.Roads.TryGetStorehouseCost(candidate.Storehouse, request.Building, out int cost))
                {
                    continue;
                }

                if (bestIndex < 0 || cost < bestCost)
                {
                    bestIndex = i;
                    bestCost = cost;
                }
            }

            if (bestIndex < 0)
            {
                // No storehouse with a free cart reaches the building; the request waits.
                continue;
            }

            if (request.Direction == TransportDirection.Delivery)
            {
                // The reservation rides out on the cart, so a delivery never takes what the pool does not hold.
                if (!ctx.Storage.TryTake(request.Good, request.Amount))
                {
                    Postpone(key, today + 1);
                    continue;
                }

                ClearPostponement(key);
            }

            EntityId storehouse = _storehouses[bestIndex].Storehouse;
            int leg = TravelTicks(bestCost, cartTicksPerTile, bonus);
            EntityId id = ctx.World.EntityIds.Allocate();
            _state.Jobs.Add(id, new TransportJobRecord
            {
                Storehouse = storehouse,
                Building = request.Building,
                Good = request.Good,
                Amount = request.Amount,
                Direction = request.Direction,
                Phase = TransportPhase.TravelOut,
                PhaseStartTick = ctx.Tick,
                PhaseTicks = leg,
                LegTicks = leg,
                Carried = request.Direction == TransportDirection.Delivery ? request.Amount : Amount.Zero,
                Interrupted = false,
            });
            TakeCart(storehouse);

            StorehouseCandidate chosen = _storehouses[bestIndex];
            chosen.Free -= 1;
            _storehouses[bestIndex] = chosen;

            _served.Add(key);
            ctx.Events.Emit(new TransportJobStarted(
                ctx.Tick, id, storehouse, request.Building, request.Good, request.Amount, request.Direction));
        }

        SweepRetries(today);
    }

    private void CompleteJob(SimContext ctx, EntityId id, TransportJobRecord job)
    {
        _state.Jobs.Remove(id);
        ReleaseCart(job.Storehouse);
        ctx.Events.Emit(new TransportJobCompleted(
            ctx.Tick, id, job.Storehouse, job.Building, job.Good, job.Amount, job.Direction));
    }

    private void FinishInterrupted(SimContext ctx, EntityId id, TransportJobRecord job)
    {
        // Whatever the cart carries goes back to the island pool: a pickup's load, or a delivery's reservation.
        ctx.Storage.Add(job.Good, job.Carried);
        job.Carried = Amount.Zero;
        _state.Jobs.Remove(id);

        // The storehouse is usually gone (its busy entry went with it); a surviving one gets its cart back.
        ReleaseCart(job.Storehouse);
        ctx.Events.Emit(new TransportJobCompleted(
            ctx.Tick, id, job.Storehouse, job.Building, job.Good, job.Amount, job.Direction));
    }

    private void TakeCart(EntityId storehouse)
    {
        if (_state.BusyCarts.TryGet(storehouse, out int busy))
        {
            ref int held = ref _state.BusyCarts.GetRef(storehouse);
            held = busy + 1;
        }
        else
        {
            _state.BusyCarts.Add(storehouse, 1);
        }
    }

    private void ReleaseCart(EntityId storehouse)
    {
        if (!_state.BusyCarts.TryGet(storehouse, out int busy))
        {
            return;
        }

        if (busy <= 1)
        {
            _state.BusyCarts.Remove(storehouse);
        }
        else
        {
            ref int held = ref _state.BusyCarts.GetRef(storehouse);
            held = busy - 1;
        }
    }

    private static long RequestKey(EntityId building, GoodId good, TransportDirection direction) =>
        ((long)building.Value << 32) | (((long)good.Value << 1) | (long)direction);

    private bool IsPostponed(long key, long today)
    {
        int index = Array.BinarySearch(_state.RetryKeys, key);
        return index >= 0 && _state.RetryDays[index] > today;
    }

    private void Postpone(long key, long day)
    {
        int index = Array.BinarySearch(_state.RetryKeys, key);
        if (index >= 0)
        {
            _state.RetryDays[index] = day;
            return;
        }

        int at = ~index;
        long[] keys = new long[_state.RetryKeys.Length + 1];
        long[] days = new long[_state.RetryDays.Length + 1];
        Array.Copy(_state.RetryKeys, keys, at);
        Array.Copy(_state.RetryDays, days, at);
        keys[at] = key;
        days[at] = day;
        Array.Copy(_state.RetryKeys, at, keys, at + 1, _state.RetryKeys.Length - at);
        Array.Copy(_state.RetryDays, at, days, at + 1, _state.RetryDays.Length - at);
        _state.RetryKeys = keys;
        _state.RetryDays = days;
    }

    private void ClearPostponement(long key)
    {
        int index = Array.BinarySearch(_state.RetryKeys, key);
        if (index >= 0)
        {
            RemoveRetryAt(index);
        }
    }

    private void SweepRetries(long today)
    {
        // Entries whose day has passed belong to requests that are gone; drop them so the list stays bounded.
        for (int i = 0; i < _state.RetryDays.Length;)
        {
            if (_state.RetryDays[i] < today)
            {
                RemoveRetryAt(i);
            }
            else
            {
                i++;
            }
        }
    }

    private void RemoveRetryAt(int index)
    {
        long[] keys = new long[_state.RetryKeys.Length - 1];
        long[] days = new long[_state.RetryDays.Length - 1];
        Array.Copy(_state.RetryKeys, keys, index);
        Array.Copy(_state.RetryDays, days, index);
        Array.Copy(_state.RetryKeys, index + 1, keys, index, keys.Length - index);
        Array.Copy(_state.RetryDays, index + 1, days, index, days.Length - index);
        _state.RetryKeys = keys;
        _state.RetryDays = days;
    }

    private struct StorehouseCandidate
    {
        public StorehouseCandidate(EntityId storehouse, int free)
        {
            Storehouse = storehouse;
            Free = free;
        }

        public EntityId Storehouse;
        public int Free;
    }
}
