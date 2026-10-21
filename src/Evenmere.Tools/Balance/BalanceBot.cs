using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Systems.Population.Commands;
using Evenmere.Sim.Systems.Research.Commands;
using Evenmere.Sim.Systems.Roads.Commands;

namespace Evenmere.Tools.Balance;

/// <summary>How the bot runs: the world seed and the cap on simulated time.</summary>
/// <param name="Seed">The world seed.</param>
/// <param name="MaxTicks">How many ticks to run at most; the bot stops earlier when every goal is done.</param>
public sealed record BalanceOptions(ulong Seed, long MaxTicks);

/// <summary>
/// The balance bot: an auto-player that follows a <see cref="BotScript"/> using only <see cref="ISimulation"/>
/// and its queries, the way a player uses the UI. It never touches <c>WorldState</c> or internal types.
/// Goals run in order, each blocking until it holds or its timeout passes; reactive rules answer shortages,
/// road warnings and coverage while the goals run. The run is deterministic for one seed and script, except
/// for the wall-clock <see cref="BalanceTimings"/>.
/// </summary>
public static class BalanceBot
{
    /// <summary>Gets how many ticks make one game day (120: 4 ticks a second, 30 seconds a day).</summary>
    public const int TicksPerDay = 120;

    /// <summary>Gets how many days make one season.</summary>
    public const int DaysPerSeason = 10;

    private const int RetryDelayTicks = 120;
    private const int CoverageGraceTicks = 120;
    private const int ShortageCooldownTicks = 600;
    private const int AdviceCooldownTicks = 600;
    private const int PlacementRetryTicks = 600;

    /// <summary>Runs the script.</summary>
    /// <param name="content">The loaded content.</param>
    /// <param name="map">The island to settle.</param>
    /// <param name="script">The goals, already validated against <paramref name="content"/>.</param>
    /// <param name="options">The seed and the cap on ticks.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static BalanceReport Run(ContentDb content, MapDef map, BotScript script, BalanceOptions options) =>
        RunDetailed(content, map, script, options).Report;

    /// <summary>Runs the script and keeps the final simulation, so tests and tools can inspect the end state.</summary>
    /// <param name="content">The loaded content.</param>
    /// <param name="map">The island to settle.</param>
    /// <param name="script">The goals, already validated against <paramref name="content"/>.</param>
    /// <param name="options">The seed and the cap on ticks.</param>
    /// <returns>The report and the simulation at the last tick.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static (BalanceReport Report, ISimulation Simulation) RunDetailed(ContentDb content, MapDef map, BotScript script, BalanceOptions options)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(options);
        var runner = new Runner(content, map, script, options);
        return (runner.Run(), runner.Final);
    }

    private sealed class Runner
    {
        private readonly ContentDb _content;
        private readonly BotScript _script;
        private readonly BalanceOptions _options;
        private readonly Simulation _simulation;
        private readonly BuildingTypeId _roadType;
        private readonly string _milestoneTier;
        private readonly TierId _milestoneTierId;
        private readonly string _targetTier;
        private readonly int _targetResidents;

        private readonly SortedDictionary<string, int> _issued = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, int> _rejected = new(StringComparer.Ordinal);
        private readonly List<string> _notes = [];
        private readonly List<BalanceStall> _stalls = [];
        private readonly List<BalanceGoalResult> _goalResults = [];
        private readonly Dictionary<GoodId, int> _shortageStreak = [];
        private readonly Dictionary<GoodId, int> _emptyStreak = [];
        private readonly List<(string Key, long Tick)> _cooldowns = [];
        private readonly List<PendingPlacement> _reactive = [];
        private readonly List<(string Type, int X, int Y, Rotation Rotation)> _banned = [];

        private readonly GoalState[] _states;
        private int _goalIndex;
        private long? _firstTick;
        private long? _fiftyTick;
        private long? _hundredTick;
        private int _bestResidents;
        private long _bestTick;
        private long _timingTotal;
        private long _timingPeak;

        internal ISimulation Final => _simulation;

        internal Runner(ContentDb content, MapDef map, BotScript script, BalanceOptions options)
        {
            _content = content;
            _script = script;
            _options = options;
            _simulation = Simulation.CreateNew(content, new NewGameOptions { Seed = options.Seed, Map = map });
            _roadType = CheapestRoad(content);
            _states = new GoalState[script.Goals.Length];
            for (int i = 0; i < _states.Length; i++)
            {
                _states[i] = new GoalState();
            }

            if (content.Tiers.TryGet("villagers", out TierId villagers))
            {
                _milestoneTier = "villagers";
                _milestoneTierId = villagers;
            }
            else
            {
                HousesUntilGoal? houses = null;
                foreach (BotGoal goal in script.Goals)
                {
                    if (goal is HousesUntilGoal h)
                    {
                        houses = h;
                    }
                }

                _milestoneTier = houses?.Tier ?? content.Tiers.Names[0];
                _milestoneTierId = content.Tiers.IdOf(_milestoneTier);
            }

            HousesUntilGoal? last = null;
            foreach (BotGoal goal in script.Goals)
            {
                if (goal is HousesUntilGoal h)
                {
                    last = h;
                }
            }

            _targetTier = last?.Tier ?? _milestoneTier;
            _targetResidents = last?.Residents ?? 100;
        }

        internal BalanceReport Run()
        {
            for (long i = 0; i < _options.MaxTicks; i++)
            {
                bool dayBoundary = _simulation.CurrentTick > 0 && _simulation.CurrentTick % TicksPerDay == 0;
                RunGoals();
                if (dayBoundary)
                {
                    RunReactive();
                }

                long start = Stopwatch.GetTimestamp();
                _simulation.Tick();
                long elapsed = Stopwatch.GetTimestamp() - start;
                _timingTotal += elapsed;
                _timingPeak = Math.Max(_timingPeak, elapsed);

                PostTick();
                if (_goalIndex >= _states.Length)
                {
                    // The script is done; reactive placements are best-effort and must not stretch the run past
                    // its target (and grow stalls after it). Unresolved reactive sites stay sites in the final hash.
                    break;
                }
            }

            return Assemble();
        }

        private void RunGoals()
        {
            // Research and civic goals set their target and queue, then track completion in the background while
            // later goals proceed: knowledge and culture accrue over days, and blocking every building on them would
            // idle the island (the reference build order researches and builds in parallel). Every other goal blocks
            // in order until it holds or its timeout passes.
            for (int i = 0; i < _states.Length; i++)
            {
                GoalState state = _states[i];
                if (state.Status is not null)
                {
                    continue;
                }

                switch (_script.Goals[i])
                {
                    case ResearchGoal research:
                        TrackResearch(research, i, state);
                        break;
                    case CivicGoal civic:
                        TrackCivic(civic, i, state);
                        break;
                }
            }

            while (_goalIndex < _states.Length)
            {
                GoalState state = _states[_goalIndex];
                BotGoal goal = _script.Goals[_goalIndex];
                if (state.Status is not null)
                {
                    Advance();
                    continue;
                }

                if (goal is ResearchGoal or CivicGoal)
                {
                    // Set-and-track: launched above, completes in the background.
                    Advance();
                    continue;
                }

                if (state.StartTick < 0)
                {
                    state.StartTick = _simulation.CurrentTick;
                }

                if (_simulation.CurrentTick - state.StartTick > goal.TimeoutTicks)
                {
                    Finish(goal, _goalIndex, state, BalanceGoalStatus.Timeout, "timed out");
                    continue;
                }

                switch (goal)
                {
                    case BuildGoal build:
                        RunBuild(build, state);
                        break;
                    case EnsureSupplyGoal supply:
                        if (_simulation.Query.Needs.Fulfilment(_content.Goods.IdOf(supply.Good)).PerMille >= supply.TargetPerMille)
                        {
                            Finish(goal, _goalIndex, state, BalanceGoalStatus.Done, $"fulfilment {supply.TargetPerMille / 10.0:F1}%");
                        }

                        break;
                    case HousesUntilGoal houses:
                        RunHouses(houses, state);
                        break;
                    case LockHousesGoal lockHouses:
                        RunLock(lockHouses, state);
                        break;
                    case UnlockHousesGoal unlockHouses:
                        RunUnlock(unlockHouses, state);
                        break;
                    case WaitUntilTickGoal wait:
                        if (_simulation.CurrentTick >= wait.Tick)
                        {
                            Finish(goal, _goalIndex, state, BalanceGoalStatus.Done, $"tick {wait.Tick}");
                        }
                        else
                        {
                            state.Detail = $"waiting for tick {wait.Tick} ({_simulation.CurrentTick})";
                        }

                        break;
                }

                // One blocking goal per tick: placing and checking need the next tick's state.
                break;
            }
        }

        private void Advance()
        {
            _goalIndex++;
            if (_goalIndex < _states.Length && _states[_goalIndex].StartTick < 0)
            {
                _states[_goalIndex].StartTick = _simulation.CurrentTick;
            }
        }

        private void RunBuild(BuildGoal goal, GoalState state)
        {
            BuildingTypeId type = _content.Buildings.IdOf(goal.Building);
            if (state.Pending is null && CountBuildings(type) >= goal.Count)
            {
                Finish(goal, _goalIndex, state, BalanceGoalStatus.Done, $"{goal.Building} x{goal.Count}");
                return;
            }

            (int X, int Y, int Width, int Height) anchor = AnchorFor(goal.Near);
            if (AnchorMissing(anchor))
            {
                state.Detail = $"waiting for {goal.Near}";
                return;
            }

            RunPlacement(state, type, goal.Building, anchor, checkCoverage: IsResidence(type));
        }

        private void RunHouses(HousesUntilGoal goal, GoalState state)
        {
            TierId tier = _content.Tiers.IdOf(goal.Tier);
            if (_simulation.Query.Population.Residents(tier) >= goal.Residents)
            {
                Finish(goal, _goalIndex, state, BalanceGoalStatus.Done, $"{goal.Residents} {goal.Tier}");
                return;
            }

            // Enough roofs for the target plus the locked workforce houses, then wait for growth: without a cap the
            // bot would pave the island, outrun market coverage, and demolish houses as fast as it builds them.
            // Four spare roofs keep the island growing while a supply chain comes online: a house takes days to
            // complete and fill, so building ahead bridges gaps (like the bread gap before milling) that would
            // otherwise stall growth for a season.
            int capacity = Math.Max(1, _content.Tiers[tier].Capacity);
            int wanted = ((goal.Residents + capacity - 1) / capacity) + LockedHouses() + 4;
            if (HouseBuildings() >= wanted)
            {
                state.Detail = $"waiting for growth ({_simulation.Query.Population.Residents(tier)}/{goal.Residents} {goal.Tier})";
                return;
            }

            BuildingTypeId? type = CheapestResidence();
            if (type is null)
            {
                state.Detail = "no unlocked residence";
                return;
            }

            RunPlacement(state, type.Value, _content.Buildings.Names[type.Value.Value], HouseAnchor(), checkCoverage: true);
        }

        private int LockedHouses()
        {
            int locked = 0;
            foreach (EntityId house in HouseIds())
            {
                if (_simulation.Query.Population.TryGetHouse(house, out HouseInfo info) && info.UpgradeLocked)
                {
                    locked++;
                }
            }

            return locked;
        }

        // Unlocks the newest locked houses that are already covered: unlocking a house lets it advance, and an
        // advance without shrine coverage drops its occupancy and poisons the growth baseline until bread arrives.
        private void RunUnlock(UnlockHousesGoal goal, GoalState state)
        {
            List<EntityId> locked = LockedHouseIds();
            if (locked.Count == 0)
            {
                Finish(goal, _goalIndex, state, BalanceGoalStatus.Done, "none locked");
                return;
            }

            // Newest first, so the oldest (the workforce reserve) stay locked. Progress accumulates across ticks
            // because a shrinking covered set may unlock only a few houses at a time.
            locked.Sort(static (left, right) => right.Value.CompareTo(left.Value));
            int coverable = 0;
            foreach (EntityId house in locked)
            {
                if (IsUnlockReady(house))
                {
                    coverable++;
                    if (state.Progress < goal.Count)
                    {
                        Send(new SetHouseUpgradeLock(house, false));
                        state.Progress++;
                    }
                }
            }

            if (state.Progress >= goal.Count)
            {
                Finish(goal, _goalIndex, state, BalanceGoalStatus.Done, $"{state.Progress} unlocked");
            }
            else
            {
                state.Detail = $"{state.Progress}/{goal.Count} unlocked ({coverable} covered)";
            }
        }

        private bool IsUnlockReady(EntityId house)
        {
            if (!MarketComplete() || !_simulation.Query.Roads.IsCovered(house, ServiceKind.Market))
            {
                return false;
            }

            // Without a finished shrine there is nothing to be covered by yet; the script orders the unlock after
            // the first shrine, so this only waits out its construction.
            return !ShrineComplete() || _simulation.Query.Roads.IsCovered(house, ServiceKind.Shrine);
        }

        private List<EntityId> LockedHouseIds()
        {
            List<EntityId> ids = [];
            foreach (EntityId house in HouseIds())
            {
                if (_simulation.Query.Population.TryGetHouse(house, out HouseInfo info) && info.UpgradeLocked)
                {
                    ids.Add(house);
                }
            }

            return ids;
        }

        // Locks the oldest houses as they complete, beating the three-day advance streak: a house locked at
        // completion never advances early and dips the total when its new tier's needs are missing.
        private void RunLock(LockHousesGoal goal, GoalState state)
        {
            List<EntityId> houses = HouseIds();
            int locked = 0;
            for (int i = 0; i < goal.Count && i < houses.Count; i++)
            {
                if (!_simulation.Query.Population.TryGetHouse(houses[i], out HouseInfo info))
                {
                    continue;
                }

                if (!info.UpgradeLocked)
                {
                    Send(new SetHouseUpgradeLock(houses[i], true));
                }
                else
                {
                    locked++;
                }
            }

            if (locked >= goal.Count)
            {
                state.Detail = $"{locked} locked";
                Finish(goal, _goalIndex, state, BalanceGoalStatus.Done, state.Detail ?? string.Empty);
            }
            else
            {
                state.Detail = $"{locked}/{goal.Count} locked";
            }
        }

        private void TrackResearch(ResearchGoal goal, int index, GoalState state)
        {
            if (state.StartTick < 0)
            {
                state.StartTick = _simulation.CurrentTick;
            }

            if (_simulation.CurrentTick - state.StartTick > goal.TimeoutTicks)
            {
                Finish(goal, index, state, BalanceGoalStatus.Timeout, "timed out");
                return;
            }

            TechId tech = _content.Techs.IdOf(goal.Tech);
            if (_simulation.Query.Research.IsDone(tech))
            {
                Finish(goal, index, state, BalanceGoalStatus.Done, goal.Tech);
                return;
            }

            if (_simulation.Query.Research.Target != tech)
            {
                if (state.SentTarget is null)
                {
                    var command = new SetResearchTarget(tech);
                    state.SentTarget = command;
                    Send(command);
                }
            }
            else
            {
                state.SentTarget = null;
            }

            bool queueWanted = goal.Queue.Length > 0;
            bool queueMatches = queueWanted && ResearchQueueMatches(goal.Queue);
            if (queueWanted && !queueMatches)
            {
                if (state.SentQueue is null)
                {
                    var command = new SetResearchQueue([.. goal.Queue.Select(name => _content.Techs.IdOf(name))]);
                    state.SentQueue = command;
                    Send(command);
                }
            }
            else
            {
                state.SentQueue = null;
            }

            state.Detail = goal.Tech;
        }

        private void TrackCivic(CivicGoal goal, int index, GoalState state)
        {
            if (state.StartTick < 0)
            {
                state.StartTick = _simulation.CurrentTick;
            }

            if (_simulation.CurrentTick - state.StartTick > goal.TimeoutTicks)
            {
                Finish(goal, index, state, BalanceGoalStatus.Timeout, "timed out");
                return;
            }

            CivicId civic = _content.Civics.IdOf(goal.Civic);
            if (_simulation.Query.Culture.IsDone(civic))
            {
                Finish(goal, index, state, BalanceGoalStatus.Done, goal.Civic);
                return;
            }

            if (_simulation.Query.Culture.Target != civic)
            {
                if (state.SentTarget is null)
                {
                    var command = new SetCivicTarget(civic);
                    state.SentTarget = command;
                    Send(command);
                }
            }
            else
            {
                state.SentTarget = null;
            }

            bool queueWanted = goal.Queue.Length > 0;
            bool queueMatches = queueWanted && CivicQueueMatches(goal.Queue);
            if (queueWanted && !queueMatches)
            {
                if (state.SentQueue is null)
                {
                    var command = new SetCivicQueue([.. goal.Queue.Select(name => _content.Civics.IdOf(name))]);
                    state.SentQueue = command;
                    Send(command);
                }
            }
            else
            {
                state.SentQueue = null;
            }

            state.Detail = goal.Civic;
        }

        // Shared place-and-connect for script and reactive builds. The anchor is a rectangle to spiral from;
        // the bot searches a spot, lays a road to the network, and places the building.
        private void RunPlacement(GoalState state, BuildingTypeId type, string typeName, (int X, int Y, int Width, int Height) anchor, bool checkCoverage)
        {
            if (state.Pending is PendingPlacement pending)
            {
                if (UpdatePending(pending, checkCoverage))
                {
                    // The placement resolved (kept or demolished); look for the next one.
                    state.Pending = null;
                }

                return;
            }

            if (_simulation.CurrentTick < state.RetryTick)
            {
                return;
            }

            if (!_simulation.Query.Progression.IsUnlocked(type))
            {
                state.Detail = $"{typeName} locked";
                return;
            }

            BuildingDef def = _content.Buildings[type];
            PlacementCandidate? candidate = def.Service is not null && AnyResidence()
                ? FindServiceSpot(type, def, anchor, PlacementSearch.DefaultMaxRadius, EntityId.None)
                    ?? PlacementSearch.Find(
                        _simulation, type, anchor.X, anchor.Y, def.Footprint.Width, def.Footprint.Height,
                        isExcluded: (x, y, _) => IsBanned(typeName, x, y))
                : PlacementSearch.Find(
                    _simulation, type, anchor.X, anchor.Y, def.Footprint.Width, def.Footprint.Height,
                    isExcluded: (x, y, _) => IsBanned(typeName, x, y));
            if (candidate is null)
            {
                state.Detail = $"no site for {typeName}";
                state.RetryTick = _simulation.CurrentTick + PlacementRetryTicks;
                return;
            }

            int width = candidate.Rotation.SwapsAxes() ? def.Footprint.Height : def.Footprint.Width;
            int height = candidate.Rotation.SwapsAxes() ? def.Footprint.Width : def.Footprint.Height;
            TileCoord[]? road = RoadPlanner.FindConnector(
                _simulation, _content, _roadType,
                candidate.X, candidate.Y, width, height,
                anchor.X, anchor.Y, anchor.Width, anchor.Height);
            if (road is { Length: > 0 })
            {
                Send(new PlaceRoad(road, _roadType));
            }

            var place = new PlaceBuilding(type, candidate.X, candidate.Y, candidate.Rotation);
            Send(place);
            state.Pending = new PendingPlacement(type, typeName, candidate.X, candidate.Y, candidate.Rotation, place, _simulation.CurrentTick);
            state.Detail = $"{typeName} at ({candidate.X}, {candidate.Y})";
        }

        // Returns true when the pending placement resolved and the caller should look for the next one.
        private bool UpdatePending(PendingPlacement pending, bool checkCoverage)
        {
            EntityId found = FindBuilding(pending.Type, pending.X, pending.Y);
            if (found == EntityId.None)
            {
                // The commands were refused, or the tick after enqueue has not run yet.
                if (_simulation.CurrentTick > pending.SeekTick + 1)
                {
                    pending.Dead = true;
                    return true;
                }

                return false;
            }

            pending.PlacedId = found;
            if (pending.CoverHouseId != EntityId.None)
            {
                return UpdateRepair(pending);
            }

            if (!checkCoverage)
            {
                return true;
            }

            if (_simulation.CurrentTick <= pending.SeekTick + 1)
            {
                return false;
            }

            if (pending.CoverageChecked)
            {
                return true;
            }

            pending.CoverageChecked = true;
            // Houses are never demolished for missing coverage here: a fringe house still counts its residents and
            // may gain coverage later when the repair rule extends the network. Demolishing on sight caused a churn
            // loop (rebuild the same fringe, demolish again), so uncovered houses stay put.
            return true;
        }

        private void RunReactive()
        {
            RunShortages();
            RunAdvisorRoads();
            RunCoverageRepair();
            for (int i = _reactive.Count - 1; i >= 0; i--)
            {
                if (_reactive[i].Dead)
                {
                    _reactive.RemoveAt(i);
                }
                else if (UpdatePending(_reactive[i], checkCoverage: false))
                {
                    _reactive.RemoveAt(i);
                }
            }
        }

        private void RunShortages()
        {
            foreach (GoodDef def in _content.Goods.Items)
            {
                GoodId good = def.Id;
                if (_simulation.Query.Needs.DemandYesterday(good).Milli <= 0)
                {
                    _shortageStreak[good] = 0;
                    _emptyStreak[good] = 0;
                    continue;
                }

                // A low fulfilment means the island cannot supply the need; an empty pool with a met fulfilment
                // means production only just covers consumption, so houses lose the intraday race for stock.
                bool low = _simulation.Query.Needs.Fulfilment(good).PerMille < 900;
                bool empty = _simulation.Query.Storage.Stock(good).Milli == 0;
                _shortageStreak[good] = low ? _shortageStreak.TryGetValue(good, out int streak) ? streak + 1 : 1 : 0;
                _emptyStreak[good] = empty ? _emptyStreak.TryGetValue(good, out int blanks) ? blanks + 1 : 1 : 0;
                if (_shortageStreak[good] < 2 && _emptyStreak[good] < 2)
                {
                    continue;
                }

                string name = _content.Goods.Names[good.Value];
                if (OnCooldown($"good:{name}"))
                {
                    continue;
                }

                _cooldowns.Add(($"good:{name}", _simulation.CurrentTick + ShortageCooldownTicks));
                GoodId bottleneck = FindBottleneck(good);
                BuildingTypeId? producer = FindProducer(bottleneck);
                if (producer is not null && AllMissingInput(producer.Value))
                {
                    // The bottleneck's producers all wait for the same input: growing more of them changes nothing,
                    // so grow the input's producer instead (bread -> flour -> windmill).
                    GoodId? input = FirstInput(producer.Value);
                    BuildingTypeId? inputProducer = input is null || input.Value == bottleneck ? null : FindProducer(input.Value);
                    if (input is not null && inputProducer is not null)
                    {
                        _notes.Add($"{name} short: {_content.Buildings.Names[producer.Value.Value]} waits for {_content.Goods.Names[input.Value.Value]}; adding {_content.Buildings.Names[inputProducer.Value.Value]}.");
                        bottleneck = input.Value;
                        producer = inputProducer;
                    }
                }

                if (producer is null)
                {
                    _notes.Add($"no unlocked producer for {_content.Goods.Names[bottleneck.Value]} (needed for {name}).");
                    continue;
                }

                if (HasIncomplete(producer.Value))
                {
                    _notes.Add($"{name} short: a {_content.Buildings.Names[producer.Value.Value]} is already being built.");
                    continue;
                }

                string producerName = _content.Buildings.Names[producer.Value.Value];
                _notes.Add($"{name} short: adding {producerName}.");
                StartReactive(producer.Value, producerName, AnchorFor("founders_hall"));
            }
        }

        // Houses the script placed before their service existed (or at its edge) can sit uncovered forever: each
        // day, cover the first one with a new service building nearby. Uses the coverage queries, not guesses.
        private void RunCoverageRepair()
        {
            if (!MarketComplete() && !ShrineComplete())
            {
                return;
            }

            EntityId needy = EntityId.None;
            ServiceKind need = ServiceKind.Market;
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (!building.IsComplete || !_simulation.Query.Population.TryGetHouse(building.Id, out HouseInfo houseInfo))
                {
                    continue;
                }

                // A settler house without shrine coverage is fine: only repair services its tier needs.
                ImmutableArray<ServiceKind> wants = _content.Tiers[houseInfo.Tier].BasicServices;
                if (MarketComplete() && wants.Contains(ServiceKind.Market) && !_simulation.Query.Roads.IsCovered(building.Id, ServiceKind.Market))
                {
                    needy = building.Id;
                    need = ServiceKind.Market;
                    break;
                }

                if (ShrineComplete() && wants.Contains(ServiceKind.Shrine) && !_simulation.Query.Roads.IsCovered(building.Id, ServiceKind.Shrine))
                {
                    needy = building.Id;
                    need = ServiceKind.Shrine;
                    break;
                }
            }

            if (needy == EntityId.None || OnCooldown($"cover:{need}"))
            {
                return;
            }

            BuildingTypeId? service = CheapestService(need);
            if (service is null || HasIncomplete(service.Value))
            {
                return;
            }

            if (!_simulation.Query.Map.TryGetBuilding(needy, out BuildingInfo house))
            {
                return;
            }

            foreach (PendingPlacement active in _reactive)
            {
                if (active.CoverHouseId != EntityId.None && !active.Dead)
                {
                    return;
                }
            }

            // The repair prefers a spot that already covers the needy house over today's roads; otherwise it takes
            // the nearest free spot, connects it with a road, and lets the completion check decide. A miss is
            // removed and banned, so the next attempt tries elsewhere.
            BuildingDef serviceDef = _content.Buildings[service.Value];
            (int X, int Y, int Width, int Height) anchor = (house.Origin.X, house.Origin.Y, house.Width, house.Height);
            PlacementCandidate? repair = FindServiceSpot(service.Value, serviceDef, anchor, 10, needy)
                ?? FindFallbackSpot(service.Value, serviceDef, anchor, 10);
            if (repair is null)
            {
                _cooldowns.Add(($"cover:{need}", _simulation.CurrentTick + ShortageCooldownTicks));
                _notes.Add($"house #{needy.Value} lacks {need} coverage: no covering site nearby.");
                return;
            }

            int width = repair.Rotation.SwapsAxes() ? serviceDef.Footprint.Height : serviceDef.Footprint.Width;
            int height = repair.Rotation.SwapsAxes() ? serviceDef.Footprint.Width : serviceDef.Footprint.Height;
            TileCoord[]? road = RoadPlanner.FindConnector(
                _simulation, _content, _roadType,
                repair.X, repair.Y, width, height,
                anchor.X, anchor.Y, anchor.Width, anchor.Height);
            if (road is { Length: > 0 })
            {
                Send(new PlaceRoad(road, _roadType));
            }

            var place = new PlaceBuilding(service.Value, repair.X, repair.Y, repair.Rotation);
            Send(place);
            _reactive.Add(new PendingPlacement(service.Value, _content.Buildings.Names[service.Value.Value], repair.X, repair.Y, repair.Rotation, place, _simulation.CurrentTick)
            {
                CoverHouseId = needy,
                CoverService = need,
            });
            _cooldowns.Add(($"cover:{need}", _simulation.CurrentTick + ShortageCooldownTicks));
            _notes.Add($"house #{needy.Value} lacks {need} coverage: adding {_content.Buildings.Names[service.Value.Value]} at ({repair.X}, {repair.Y}).");
        }

        // Scores service spots by the houses they would cover over today's roads, so markets and shrines land
        // where the roofs are. Candidates come from the spiral in order; the first 24 placeable ones are scored
        // and the best wins (ties go to the nearer one). With a priority house, only a spot covering it qualifies.
        private PlacementCandidate? FindServiceSpot(
            BuildingTypeId type, BuildingDef def, (int X, int Y, int Width, int Height) anchor, int maxRadius, EntityId priorityHouse)
        {
            int bound = def.Service?.Coverage ?? 0;
            string typeName = _content.Buildings.Names[type.Value];
            List<PlacementCandidate> candidates = PlacementSearch.FindAll(
                _simulation, type, anchor.X, anchor.Y, def.Footprint.Width, def.Footprint.Height,
                maxRadius, int.MaxValue, isExcluded: (x, y, _) => IsBanned(typeName, x, y));
            PlacementCandidate? best = null;
            int bestScore = -1;
            foreach (PlacementCandidate candidate in candidates)
            {
                int width = candidate.Rotation.SwapsAxes() ? def.Footprint.Height : def.Footprint.Width;
                int height = candidate.Rotation.SwapsAxes() ? def.Footprint.Width : def.Footprint.Height;
                List<TileCoord> adjacent = AdjacentRoads(candidate.X, candidate.Y, width, height);
                if (adjacent.Count == 0)
                {
                    continue;
                }

                int score = 0;
                bool coversPriority = false;
                foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
                {
                    if (_content.Buildings[building.Type].Category != BuildingCategory.Residence)
                    {
                        continue;
                    }

                    List<TileCoord> houseRoads = AdjacentRoads(building.Origin.X, building.Origin.Y, building.Width, building.Height);
                    if (houseRoads.Count == 0)
                    {
                        continue;
                    }

                    int? distance = RoadPlanner.RoadDistance(_simulation, houseRoads, adjacent);
                    if (distance is not null && distance <= bound)
                    {
                        score++;
                        coversPriority |= building.Id == priorityHouse;
                    }
                }

                if (priorityHouse != EntityId.None && !coversPriority)
                {
                    continue;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        // The nearest free spot in spiral order, whatever its roads: the connector lays the access and the
        // completion check keeps or removes the result. Banned spots (tried and removed) are skipped.
        private PlacementCandidate? FindFallbackSpot(
            BuildingTypeId type, BuildingDef def, (int X, int Y, int Width, int Height) anchor, int maxRadius)
        {
            string typeName = _content.Buildings.Names[type.Value];
            List<PlacementCandidate> candidates = PlacementSearch.FindAll(
                _simulation, type, anchor.X, anchor.Y, def.Footprint.Width, def.Footprint.Height,
                maxRadius, int.MaxValue, isExcluded: (x, y, _) => IsBanned(typeName, x, y));
            return candidates.Count == 0 ? null : candidates[0];
        }

        private List<TileCoord> AdjacentRoads(int x, int y, int width, int height)
        {
            List<TileCoord> roads = [];
            for (int ty = y - 1; ty <= y + height; ty++)
            {
                for (int tx = x - 1; tx <= x + width; tx++)
                {
                    bool inX = tx >= x && tx < x + width;
                    bool inY = ty >= y && ty < y + height;
                    if (!((tx == x - 1 || tx == x + width) ? inY : (ty == y - 1 || ty == y + height) && inX))
                    {
                        continue;
                    }

                    if (tx >= 0 && ty >= 0 && tx < _simulation.Query.Map.Width && ty < _simulation.Query.Map.Height
                        && _simulation.Query.Roads.RoadAt(tx, ty) is not null)
                    {
                        roads.Add(new TileCoord(tx, ty));
                    }
                }
            }

            return roads;
        }

        private bool AnyResidence()
        {
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (_content.Buildings[building.Type].Category == BuildingCategory.Residence)
                {
                    return true;
                }
            }

            return false;
        }

        private BuildingTypeId? CheapestService(ServiceKind need)
        {
            BuildingTypeId? best = null;
            long bestCost = long.MaxValue;
            foreach (BuildingDef def in _content.Buildings.Items)
            {
                if (def.Unique || def.Service?.Kind != need || !_simulation.Query.Progression.IsUnlocked(def.Id))
                {
                    continue;
                }

                long cost = def.Cost.Goods.Sum(static needCost => needCost.Amount.Milli);
                if (best is null || cost < bestCost || (cost == bestCost && def.Id.Value < best.Value.Value))
                {
                    best = def.Id;
                    bestCost = cost;
                }
            }

            return best;
        }

        private void RunAdvisorRoads()
        {
            foreach (AdviceView advice in _simulation.Query.Advisors.Active().ToArray())
            {
                if (advice.Kind is not AdviceKind.NoRoadAccess and not AdviceKind.OutsideStorehouseReach)
                {
                    continue;
                }

                if (advice.Subject.Kind != SubjectKind.Building || !_simulation.Query.Map.TryGetBuilding(advice.Subject.AsBuilding(), out BuildingInfo building))
                {
                    continue;
                }

                string key = $"advice:{advice.Kind}:{building.Id.Value}";
                if (OnCooldown(key))
                {
                    continue;
                }

                _cooldowns.Add((key, _simulation.CurrentTick + AdviceCooldownTicks));
                _notes.Add($"{advice.Kind} for {_content.Buildings.Names[building.Type.Value]} #{building.Id.Value}: connecting a road.");
                TileCoord[]? road = RoadPlanner.FindConnector(
                    _simulation, _content, _roadType,
                    building.Origin.X, building.Origin.Y, building.Width, building.Height,
                    building.Origin.X, building.Origin.Y, building.Width, building.Height);
                if (road is { Length: > 0 })
                {
                    Send(new PlaceRoad(road, _roadType));
                    continue;
                }

                if (advice.Kind == AdviceKind.OutsideStorehouseReach && _simulation.Query.Roads.HasRoadAccess(building.Id))
                {
                    BuildingTypeId? storehouse = CheapestStorehouse();
                    if (storehouse is not null)
                    {
                        string storeName = _content.Buildings.Names[storehouse.Value.Value];
                        _notes.Add($"{advice.Kind} persists for #{building.Id.Value}: adding {storeName}.");
                        StartReactive(storehouse.Value, storeName, (building.Origin.X, building.Origin.Y, building.Width, building.Height));
                    }
                }
            }
        }

        private void PostTick()
        {
            foreach (SimEvent raised in _simulation.DrainEvents())
            {
                if (raised is CommandRejected rejected)
                {
                    string key = $"{rejected.Command.GetType().Name}({rejected.Code})";
                    _rejected[key] = _rejected.TryGetValue(key, out int count) ? count + 1 : 1;
                    foreach (GoalState state in _states)
                    {
                        if (state.Pending is PendingPlacement pending && pending.SentBuilding.Equals(rejected.Command))
                        {
                            state.Pending = null;
                            state.RetryTick = _simulation.CurrentTick + RetryDelayTicks;
                        }

                        if (state.SentTarget is not null && state.SentTarget.Equals(rejected.Command))
                        {
                            state.SentTarget = null;
                        }

                        if (state.SentQueue is not null && state.SentQueue.Equals(rejected.Command))
                        {
                            state.SentQueue = null;
                        }
                    }

                    foreach (PendingPlacement reactive in _reactive)
                    {
                        if (reactive.SentBuilding.Equals(rejected.Command))
                        {
                            reactive.Dead = true;
                        }
                    }
                }
            }

            int total = _simulation.Query.Population.TotalResidents;
            int residents = _simulation.Query.Population.Residents(_milestoneTierId);
            if (residents > 0 && _firstTick is null)
            {
                _firstTick = _simulation.CurrentTick;
            }

            if (residents >= 50 && _fiftyTick is null)
            {
                _fiftyTick = _simulation.CurrentTick;
            }

            if (residents >= 100 && _hundredTick is null)
            {
                _hundredTick = _simulation.CurrentTick;
            }

            if (total > _bestResidents)
            {
                _bestResidents = total;
                _bestTick = _simulation.CurrentTick;
            }
            else if (_simulation.CurrentTick - _bestTick >= TicksPerDay * DaysPerSeason)
            {
                _stalls.Add(new BalanceStall(_bestTick, _simulation.CurrentTick, _bestResidents, total, TopAdvice()));
                _bestResidents = total;
                _bestTick = _simulation.CurrentTick;
            }
        }

        private string TopAdvice()
        {
            foreach (AdviceView advice in _simulation.Query.Advisors.Active().ToArray())
            {
                return $"{advice.Kind}:{DescribeSubject(advice.Subject)}";
            }

            return string.Empty;
        }

        private string DescribeSubject(SubjectLink subject) => subject.Kind switch
        {
            SubjectKind.Building => _simulation.Query.Map.TryGetBuilding(subject.AsBuilding(), out BuildingInfo building)
                ? $"{_content.Buildings.Names[building.Type.Value]} #{building.Id.Value}"
                : $"building #{subject.AsBuilding().Value}",
            SubjectKind.Good => _content.Goods.Names[subject.AsGood().Value],
            SubjectKind.Tier => _content.Tiers.Names[subject.AsTier().Value],
            SubjectKind.BuildingType => _content.Buildings.Names[subject.AsBuildingType().Value],
            _ => string.Empty,
        };

        // Walks the explanation tree for the good, pre-order, and returns the first other good it mentions:
        // the chained cause (for example bread -> flour) whose producer should grow instead.
        private GoodId FindBottleneck(GoodId shortGood)
        {
            var queue = new Queue<ExplanationLine>();
            queue.Enqueue(_simulation.Query.Explain.ExplainGood(shortGood).Root);
            while (queue.Count > 0)
            {
                ExplanationLine line = queue.Dequeue();
                foreach (ExplainArg arg in line.Args)
                {
                    if (arg.Kind == ExplainArgKind.Good && arg.AsGood() != shortGood)
                    {
                        return arg.AsGood();
                    }
                }

                foreach (ExplanationLine child in line.Children)
                {
                    queue.Enqueue(child);
                }
            }

            return shortGood;
        }

        private BuildingTypeId? FindProducer(GoodId good)
        {
            BuildingTypeId? best = null;
            long bestCost = long.MaxValue;
            foreach (BuildingDef def in _content.Buildings.Items)
            {
                if (def.Unique
                    || def.Recipe is null
                    || !_content.Recipes[def.Recipe.Value].Outputs.Any(output => output.Good == good))
                {
                    continue;
                }

                if (!_simulation.Query.Progression.IsUnlocked(def.Id))
                {
                    continue;
                }

                long cost = def.Cost.Goods.Sum(static need => need.Amount.Milli);
                if (best is null || cost < bestCost || (cost == bestCost && def.Id.Value < best.Value.Value))
                {
                    best = def.Id;
                    bestCost = cost;
                }
            }

            return best;
        }

        // Tests whether the bottleneck's active producers all stall on the same missing input, by reading their
        // explanations rather than guessing from the shortage alone.
        private bool AllMissingInput(BuildingTypeId type)
        {
            bool any = false;
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (building.Type != type || !building.IsComplete)
                {
                    continue;
                }

                any = true;
                if (StallOf(building.Id) != StallReason.MissingInput)
                {
                    return false;
                }
            }

            return any;
        }

        private StallReason StallOf(EntityId building)
        {
            var queue = new Queue<ExplanationLine>();
            queue.Enqueue(_simulation.Query.Explain.ExplainBuilding(building).Root);
            while (queue.Count > 0)
            {
                ExplanationLine line = queue.Dequeue();
                foreach (ExplainArg arg in line.Args)
                {
                    if (arg.Kind == ExplainArgKind.Stall)
                    {
                        return arg.AsStall();
                    }
                }

                foreach (ExplanationLine child in line.Children)
                {
                    queue.Enqueue(child);
                }
            }

            return StallReason.None;
        }

        private GoodId? FirstInput(BuildingTypeId type)
        {
            RecipeId? recipe = _content.Buildings[type].Recipe;
            if (recipe is null)
            {
                return null;
            }

            GoodId? best = null;
            foreach (GoodAmount input in _content.Recipes[recipe.Value].Inputs)
            {
                if (best is null || input.Good.Value < best.Value.Value)
                {
                    best = input.Good;
                }
            }

            return best;
        }

        private bool HasIncomplete(BuildingTypeId type)
        {
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (building.Type == type && building.State == BuildingState.Site)
                {
                    return true;
                }
            }

            return false;
        }

        private void StartReactive(BuildingTypeId type, string typeName, (int X, int Y, int Width, int Height) anchor)
        {
            // Reactive builds share the pending flow but are not tied to a goal; misses resolve through UpdatePending.
            BuildingDef def = _content.Buildings[type];
            PlacementCandidate? candidate = PlacementSearch.Find(
                _simulation, type, anchor.X, anchor.Y, def.Footprint.Width, def.Footprint.Height,
                isExcluded: (x, y, _) => IsBanned(typeName, x, y));
            if (candidate is null)
            {
                _notes.Add($"no site for reactive {typeName}.");
                return;
            }

            int width = candidate.Rotation.SwapsAxes() ? def.Footprint.Height : def.Footprint.Width;
            int height = candidate.Rotation.SwapsAxes() ? def.Footprint.Width : def.Footprint.Height;
            TileCoord[]? road = RoadPlanner.FindConnector(
                _simulation, _content, _roadType,
                candidate.X, candidate.Y, width, height,
                anchor.X, anchor.Y, anchor.Width, anchor.Height);
            if (road is { Length: > 0 })
            {
                Send(new PlaceRoad(road, _roadType));
            }

            var place = new PlaceBuilding(type, candidate.X, candidate.Y, candidate.Rotation);
            Send(place);
            _reactive.Add(new PendingPlacement(type, typeName, candidate.X, candidate.Y, candidate.Rotation, place, _simulation.CurrentTick));
        }

        private (int X, int Y, int Width, int Height) AnchorFor(string typeName)
        {
            BuildingTypeId type = _content.Buildings.IdOf(typeName);
            EntityId best = EntityId.None;
            BuildingInfo found = default;
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (building.Type == type && (best == EntityId.None || building.Id < best))
                {
                    best = building.Id;
                    found = building;
                }
            }

            if (best != EntityId.None)
            {
                return (found.Origin.X, found.Origin.Y, found.Width, found.Height);
            }

            // The anchor is not built yet (a later goal builds it); wait for it.
            return (0, 0, 0, 0);
        }

        private static bool AnchorMissing((int X, int Y, int Width, int Height) anchor) => anchor.Width <= 0 || anchor.Height <= 0;

        private (int X, int Y, int Width, int Height) HouseAnchor()
        {
            EntityId best = EntityId.None;
            BuildingInfo found = default;
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (building is not { State: BuildingState.Active or BuildingState.Paused })
                {
                    continue;
                }

                if (_content.Buildings[building.Type].Service?.Kind != ServiceKind.Market)
                {
                    continue;
                }

                if (best == EntityId.None || building.Id < best)
                {
                    best = building.Id;
                    found = building;
                }
            }

            if (best != EntityId.None)
            {
                return (found.Origin.X, found.Origin.Y, found.Width, found.Height);
            }

            return AnchorFor("founders_hall");
        }

        private List<EntityId> HouseIds()
        {
            var ids = new List<EntityId>();
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (_simulation.Query.Population.TryGetHouse(building.Id, out _))
                {
                    ids.Add(building.Id);
                }
            }

            return ids;
        }

        // Counts every residence building, complete or still a site: sites do not have houses yet, but they are
        // still roofs that count against the cap, or the bot would keep placing while earlier sites finish.
        private int HouseBuildings()
        {
            int count = 0;
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (_content.Buildings[building.Type].Category == BuildingCategory.Residence)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountBuildings(BuildingTypeId type)
        {
            int count = 0;
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (building.Type == type)
                {
                    count++;
                }
            }

            return count;
        }

        private EntityId FindBuilding(BuildingTypeId type, int x, int y)
        {
            EntityId best = EntityId.None;
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (building.Type == type && building.Covers(x, y) && (best == EntityId.None || building.Id < best))
                {
                    best = building.Id;
                }
            }

            return best;
        }

        private bool IsResidence(BuildingTypeId type) => _content.Buildings[type].Category == BuildingCategory.Residence;

        private bool MarketComplete()
        {
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (building.IsComplete && _content.Buildings[building.Type].Service?.Kind == ServiceKind.Market)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShrineComplete()
        {
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (building.IsComplete && _content.Buildings[building.Type].Service?.Kind == ServiceKind.Shrine)
                {
                    return true;
                }
            }

            return false;
        }

        private BuildingTypeId? CheapestResidence()
        {
            BuildingTypeId? best = null;
            long bestCost = long.MaxValue;
            foreach (BuildingDef def in _content.Buildings.Items)
            {
                if (def.Unique || def.Category != BuildingCategory.Residence || !_simulation.Query.Progression.IsUnlocked(def.Id))
                {
                    continue;
                }

                long cost = def.Cost.Goods.Sum(static need => need.Amount.Milli);
                if (best is null || cost < bestCost || (cost == bestCost && def.Id.Value < best.Value.Value))
                {
                    best = def.Id;
                    bestCost = cost;
                }
            }

            return best;
        }

        private BuildingTypeId? CheapestStorehouse()
        {
            BuildingTypeId? best = null;
            long bestCost = long.MaxValue;
            foreach (BuildingDef def in _content.Buildings.Items)
            {
                if (def.Unique || def.Storage is null || !_simulation.Query.Progression.IsUnlocked(def.Id))
                {
                    continue;
                }

                long cost = def.Cost.Goods.Sum(static need => need.Amount.Milli);
                if (best is null || cost < bestCost || (cost == bestCost && def.Id.Value < best.Value.Value))
                {
                    best = def.Id;
                    bestCost = cost;
                }
            }

            return best;
        }

        private static BuildingTypeId CheapestRoad(ContentDb content)
        {
            BuildingTypeId best = default;
            int bestCost = int.MaxValue;
            foreach (BuildingDef def in content.Buildings.Items)
            {
                if (def.RoadCost is not int cost)
                {
                    continue;
                }

                if (cost < bestCost || (cost == bestCost && (best == default || def.Id.Value < best.Value)))
                {
                    best = def.Id;
                    bestCost = cost;
                }
            }

            return best == default
                ? throw new BotScriptException("invalid content for the balance bot: no road types.")
                : best;
        }

        private bool ResearchQueueMatches(ImmutableArray<string> wanted)
        {
            ImmutableArray<TechId> current = _simulation.Query.Research.Queue;
            if (current.Length != wanted.Length)
            {
                return false;
            }

            for (int i = 0; i < wanted.Length; i++)
            {
                if (current[i] != _content.Techs.IdOf(wanted[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool CivicQueueMatches(ImmutableArray<string> wanted)
        {
            ImmutableArray<CivicId> current = _simulation.Query.Culture.Queue;
            if (current.Length != wanted.Length)
            {
                return false;
            }

            for (int i = 0; i < wanted.Length; i++)
            {
                if (current[i] != _content.Civics.IdOf(wanted[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsBanned(string type, int x, int y)
        {
            // A spot refused for coverage fails for every rotation, so the whole origin is banned, whatever was tried.
            foreach ((string bannedType, int bannedX, int bannedY, _) in _banned)
            {
                if (bannedType == type && bannedX == x && bannedY == y)
                {
                    return true;
                }
            }

            return false;
        }

        private bool OnCooldown(string key)
        {
            foreach ((string existing, long until) in _cooldowns)
            {
                if (existing == key && _simulation.CurrentTick < until)
                {
                    return true;
                }
            }

            return false;
        }

        private void Send(Command command)
        {
            string name = command.GetType().Name;
            _issued[name] = _issued.TryGetValue(name, out int count) ? count + 1 : 1;
            _simulation.Enqueue(command);
        }

        private void Finish(BotGoal goal, int index, GoalState state, BalanceGoalStatus status, string detail)
        {
            state.Status = status;
            state.Detail = detail;
            long start = state.StartTick < 0 ? _simulation.CurrentTick : state.StartTick;
            _goalResults.Add(new BalanceGoalResult(index, KindOf(goal), detail, status, _simulation.CurrentTick - start));
        }

        private static string KindOf(BotGoal goal) => goal switch
        {
            BuildGoal => "build",
            ResearchGoal => "research",
            CivicGoal => "civic",
            EnsureSupplyGoal => "ensureSupply",
            HousesUntilGoal => "housesUntil",
            LockHousesGoal => "lockHouses",
            UnlockHousesGoal => "unlockHouses",
            WaitUntilTickGoal => "wait",
            _ => "unknown",
        };

        private BalanceReport Assemble()
        {
            while (_goalIndex < _states.Length)
            {
                GoalState state = _states[_goalIndex];
                BotGoal goal = _script.Goals[_goalIndex];
                if (state.Status is null)
                {
                    state.Status = BalanceGoalStatus.Failed;
                    _goalResults.Add(new BalanceGoalResult(_goalIndex, KindOf(goal), state.Detail ?? "run ended", state.Status.Value, _simulation.CurrentTick - state.StartTick));
                }

                _goalIndex++;
            }

            ImmutableSortedDictionary<string, int>.Builder residents = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
            foreach (TierId tier in _content.TiersByOrder)
            {
                residents[_content.Tiers.Names[tier.Value]] = _simulation.Query.Population.Residents(tier);
            }

            ImmutableSortedDictionary<string, double>.Builder stock = ImmutableSortedDictionary.CreateBuilder<string, double>(StringComparer.Ordinal);
            foreach (GoodDef def in _content.Goods.Items)
            {
                Amount amount = _simulation.Query.Storage.Stock(def.Id);
                stock[_content.Goods.Names[def.Id.Value]] = amount.Milli / 1000.0;
            }

            ImmutableSortedDictionary<string, int>.Builder issued = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
            foreach ((string name, int count) in _issued)
            {
                issued[name] = count;
            }

            ImmutableSortedDictionary<string, int>.Builder rejected = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
            foreach ((string name, int count) in _rejected)
            {
                rejected[name] = count;
            }

            bool reached = _simulation.Query.Population.Residents(_content.Tiers.IdOf(_targetTier)) >= _targetResidents;
            long? reachedTick = reached ? _hundredTick ?? _simulation.CurrentTick : null;
            if (_targetTier == _milestoneTier && _hundredTick is not null)
            {
                reachedTick = _hundredTick;
            }

            double frequency = Stopwatch.Frequency;
            long ticks = _simulation.CurrentTick;
            _goalResults.Sort(static (left, right) => left.Index.CompareTo(right.Index));
            return new BalanceReport(
                _script.Name ?? "bot",
                _options.Seed,
                ticks,
                new BalanceTargetResult(_targetTier, _targetResidents, reached, reachedTick),
                new BalanceMilestones(_milestoneTier, _firstTick, _fiftyTick, _hundredTick),
                [.. _goalResults],
                [.. _stalls],
                issued.ToImmutable(),
                rejected.ToImmutable(),
                residents.ToImmutable(),
                stock.ToImmutable(),
                [.. _notes],
                _simulation.ComputeStateHash().ToString("x16", CultureInfo.InvariantCulture),
                new BalanceTimings(ticks, ticks == 0 ? 0 : (_timingTotal / frequency * 1000) / ticks, _timingPeak / frequency * 1000));
        }

        private sealed class GoalState
        {
            public long StartTick = -1;
            public long RetryTick;
            public int Progress;
            public PendingPlacement? Pending;
            public Command? SentTarget;
            public Command? SentQueue;
            public BalanceGoalStatus? Status;
            public string? Detail;
        }

        // A repair that the coverage check did not confirm is removed again, and its spot is banned, so the
        // next repair tries elsewhere instead of rebuilding the same failure.
        private bool UpdateRepair(PendingPlacement pending)
        {
            EntityId found = FindBuilding(pending.Type, pending.X, pending.Y);
            if (found == EntityId.None)
            {
                if (_simulation.CurrentTick > pending.SeekTick + 1)
                {
                    pending.Dead = true;
                    return true;
                }

                return false;
            }

            pending.PlacedId = found;
            if (!_simulation.Query.Map.TryGetBuilding(found, out BuildingInfo building) || !building.IsComplete)
            {
                if (_simulation.CurrentTick - pending.SeekTick > 2000)
                {
                    Send(new Demolish(found));
                    _banned.Add((pending.TypeName, pending.X, pending.Y, pending.Rotation));
                    _notes.Add($"{pending.TypeName} at ({pending.X}, {pending.Y}) never completed and was removed.");
                    pending.Dead = true;
                    return true;
                }

                return false;
            }

            if (_simulation.Query.Map.TryGetBuilding(pending.CoverHouseId, out _) && pending.CoverService is ServiceKind need
                && !_simulation.Query.Roads.IsCovered(pending.CoverHouseId, need))
            {
                Send(new Demolish(found));
                _banned.Add((pending.TypeName, pending.X, pending.Y, pending.Rotation));
                _notes.Add($"{pending.TypeName} at ({pending.X}, {pending.Y}) did not cover house #{pending.CoverHouseId.Value} and was removed.");
            }

            pending.Dead = true;
            return true;
        }

        private sealed class PendingPlacement
        {
            public PendingPlacement(
                BuildingTypeId type, string typeName, int x, int y,
                Rotation rotation, Command sentBuilding, long seekTick)
            {
                Type = type;
                TypeName = typeName;
                X = x;
                Y = y;
                Rotation = rotation;
                SentBuilding = sentBuilding;
                SeekTick = seekTick;
            }

            public BuildingTypeId Type { get; }
            public string TypeName { get; }
            public int X { get; }
            public int Y { get; }
            public Rotation Rotation { get; }
            public Command SentBuilding { get; }
            public long SeekTick { get; }
            public EntityId CoverHouseId { get; init; } = EntityId.None;
            public ServiceKind? CoverService { get; init; }
            public EntityId PlacedId { get; set; } = EntityId.None;
            public bool CoverageChecked { get; set; }
            public bool Dead { get; set; }
        }
    }
}
