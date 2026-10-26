using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Systems.Culture.Festivals.Commands;
using Evenmere.Sim.Systems.Culture.Traditions.Commands;
using Evenmere.Sim.Systems.Exploration.Commands;
using Evenmere.Sim.Systems.Happenings;
using Evenmere.Sim.Systems.Happenings.Commands;
using Evenmere.Sim.Systems.Population.Commands;
using Evenmere.Sim.Systems.Research.Commands;
using Evenmere.Sim.Systems.Roads.Commands;

namespace Evenmere.Tools.Balance.EraOne;

/// <summary>Whether the Era I run reached the Township era.</summary>
/// <param name="Reached">Whether Era II began before the tick budget ran out.</param>
/// <param name="ReachedTick">The tick Era II began, or <see langword="null"/>.</param>
/// <param name="ResidentsAtEra">Total residents when Era II began, or <see langword="null"/>.</param>
/// <param name="VillagersAtEra">Villager residents when Era II began, or <see langword="null"/>.</param>
/// <param name="ResidentsAtFirst">Total residents when the first Villager appeared, or <see langword="null"/>.</param>
/// <param name="ResidentsAtHundred">Total residents when the 100th Villager appeared, or <see langword="null"/>.</param>
public sealed record EraOneEraResult(bool Reached, long? ReachedTick, int? ResidentsAtEra, int? VillagersAtEra, int? ResidentsAtFirst, int? ResidentsAtHundred)
{
    /// <summary>Gets the game minutes when Era II began, or <see langword="null"/>.</summary>
    public double? ReachedMinutes => ReachedTick is long tick ? tick / (double)BotScript.TicksPerGameMinute : null;
}

/// <summary>
/// What the Era I bot saw: the v1 report as its base plus the Township-era milestone. The <c>deterministic</c>
/// section is byte-identical for one seed and script; the <c>timings</c> section is not.
/// </summary>
/// <param name="Base">The underlying bot report: milestones, stalls, commands, residents, stock, notes and hash.</param>
/// <param name="Era">Whether and when the Township era began, with residents then.</param>
/// <param name="TechGatewayDone">Whether the tech gateway was complete at the end.</param>
/// <param name="CivicGatewayDone">Whether the civic gateway was complete at the end.</param>
public sealed record EraOneReport(
    BalanceReport Base,
    EraOneEraResult Era,
    bool TechGatewayDone,
    bool CivicGatewayDone)
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    /// <summary>Serialises the deterministic section: everything except the wall-clock timings.</summary>
    /// <returns>The JSON with two-space indent and one trailing newline, byte-identical for the same run.</returns>
    public string ToDeterministicJson() => DeterministicNode().ToJsonString(_json) + "\n";
    /// <summary>Serialises the whole report: the deterministic section plus the wall-clock timings.</summary>
    /// <returns>The JSON with two-space indent and one trailing newline.</returns>
    public string ToJson()
    {
        var deterministic = JsonNode.Parse(DeterministicNode().ToJsonString());
        var root = new JsonObject
        {
            ["deterministic"] = deterministic,
            ["timings"] = new JsonObject
            {
                ["ticksRun"] = Base.Timings.TicksRun,
                ["meanMsPerTick"] = Base.Timings.MeanMsPerTick,
                ["peakMsPerTick"] = Base.Timings.PeakMsPerTick,
            },
        };
        return root.ToJsonString(_json) + "\n";
    }

    /// <summary>Renders the human-readable text report.</summary>
    /// <returns>The text.</returns>
    public string ToText()
    {
        var text = new StringBuilder(Base.ToText());
        text.Append(CultureInfo.InvariantCulture, $"era II: {(Era.Reached ? $"began at tick {Era.ReachedTick} ({Era.ReachedMinutes:F1} min) with {Era.ResidentsAtEra} residents ({Era.VillagersAtEra} villagers)" : "not reached")}\n");
        text.Append(CultureInfo.InvariantCulture, $"gateways: tech {(TechGatewayDone ? "done" : "open")}, civic {(CivicGatewayDone ? "done" : "open")}\n");
        return text.ToString();
    }

    private JsonObject DeterministicNode()
    {
        var baseNode = JsonNode.Parse(Base.ToDeterministicJson());
        JsonObject root = baseNode?.AsObject() ?? [];
        root["era"] = new JsonObject
        {
            ["reached"] = Era.Reached,
            ["reachedTick"] = Era.ReachedTick is long tick ? JsonValue.Create(tick) : null,
            ["residentsAtEra"] = Era.ResidentsAtEra is int residents ? JsonValue.Create(residents) : null,
            ["villagersAtEra"] = Era.VillagersAtEra is int villagers ? JsonValue.Create(villagers) : null,
            ["residentsAtFirst"] = Era.ResidentsAtFirst is int first ? JsonValue.Create(first) : null,
            ["residentsAtHundred"] = Era.ResidentsAtHundred is int hundred ? JsonValue.Create(hundred) : null,
            ["techGatewayDone"] = TechGatewayDone,
            ["civicGatewayDone"] = CivicGatewayDone,
        };
        return root;
    }
}

/// <summary>
/// The Era I island bot: an auto-player that plays a <see cref="BotScript"/> through Era I to both gateways using only
/// <see cref="ISimulation"/> and its queries, the way a player uses the UI. It reuses the v1 bot's search
/// (<see cref="PlacementSearch"/>), roads (<see cref="RoadPlanner"/>), script (<see cref="BotScript"/>) and report
/// (<see cref="BalanceReport"/>) pieces; only the orchestration loop is its own, because the Era I behaviours live
/// here: expeditions from idle lodges, tradition slotting from <see cref="TraditionHeuristics"/>, the Harvest Fair
/// when affordable, happening answers on day 5, affordable-only reactive buildings, and era tracking to Era II.
/// The run is deterministic for one seed and script, except for the wall-clock <see cref="BalanceTimings"/>.
/// </summary>
public static class EraOneBot
{
    /// <summary>Runs the script to Era II.</summary>
    /// <param name="content">The loaded content.</param>
    /// <param name="map">The island to settle.</param>
    /// <param name="script">The goals, already validated against <paramref name="content"/>.</param>
    /// <param name="options">The seed and the cap on ticks.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static EraOneReport Run(ContentDb content, MapDef map, BotScript script, BalanceOptions options) =>
        RunDetailed(content, map, script, options).Report;

    /// <summary>Runs the script and keeps the final simulation, so tests and tools can inspect the end state.</summary>
    /// <param name="content">The loaded content.</param>
    /// <param name="map">The island to settle.</param>
    /// <param name="script">The goals, already validated against <paramref name="content"/>.</param>
    /// <param name="options">The seed and the cap on ticks.</param>
    /// <returns>The report and the simulation at the last tick.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static (EraOneReport Report, ISimulation Simulation) RunDetailed(ContentDb content, MapDef map, BotScript script, BalanceOptions options)
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
        private const int RetryDelayTicks = 120;
        private const int ShortageCooldownTicks = 600;
        private const int AdviceCooldownTicks = 600;
        private const int PlacementRetryTicks = 600;
        private const int TraditionCooldownTicks = 600;
        private const int DerelictTicks = 2400;

        private readonly ContentDb _content;
        private readonly BotScript _script;
        private readonly BalanceOptions _options;
        private readonly Simulation _simulation;
        private readonly BuildingTypeId _roadType;
        private readonly EraId _township;
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
        private readonly List<(string Type, int X, int Y, Rotation Rotation)> _linkBanned = [];
        private int _roadsSeen;
        private bool _bansCleared;        private readonly List<(int X, int Y, int Width, int Height, long Tick)> _claims = [];
        private readonly List<(int X, int Y, int Width, int Height)> _reservedFarms = [];
        private readonly bool[] _bank;
        private readonly BuildingTypeId _bridgeType;
        private bool _farmsReserved;
        private readonly Dictionary<EntityId, long> _siteSeen = [];
        private ImmutableArray<string>? _queueOverride;
        private bool _needBridges;
        private readonly HashSet<EntityId> _expeditionSent = [];
        private readonly HashSet<int> _expeditionRegions = [];

        private readonly GoalState[] _states;
        private int _goalIndex;
        private long? _firstTick;
        private long? _fiftyTick;
        private long? _hundredTick;
        private long? _eraTwoTick;
        private int? _residentsAtFirst;
        private int? _residentsAtHundred;
        private int? _residentsAtEraTwo;
        private int? _villagersAtEraTwo;
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
            _bridgeType = content.Buildings.TryGet("timber_bridge", out BuildingTypeId bridge) ? bridge : default;
            _township = content.Eras.TryGet("township", out EraId township) ? township : content.ErasByOrder[^1];
            _states = new GoalState[script.Goals.Length];
            for (int i = 0; i < _states.Length; i++)
            {
                _states[i] = new GoalState();
            }
            _bank = ComputeBank();

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

        internal EraOneReport Run()
        {
            // A real player plans the orchard belt before the houses sprawl: reserve it at tick 0 while the
            // ground is still free (see ReserveFarmSites). Deterministic: the island is exactly as generated.
            ReserveFarmSites();
            for (long i = 0; i < _options.MaxTicks; i++)
            {
                bool dayBoundary = _simulation.CurrentTick > 0 && _simulation.CurrentTick % GameTime.TicksPerDay == 0;
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
                if (_eraTwoTick is not null)
                {
                    break;
                }

                if (_goalIndex >= _states.Length)
                {
                    // The script is done but Era II has not begun: the reactive rules keep playing (festivals,
                    // expeditions, traditions, shortages) until the gateways complete or the budget runs out.
                    // Unresolved reactive sites stay sites in the final hash.
                    continue;
                }
            }

            return Assemble();
        }

        private void RunGoals()
        {
            // Research and civic goals set their target and queue, then track completion in the background while
            // later goals proceed: knowledge and culture accrue over days, and blocking every building on them would
            // idle the island. Every other goal blocks in order until it holds or its timeout passes.
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
                    Finish(goal, _goalIndex, state, BalanceGoalStatus.Timeout, $"timed out ({state.Detail ?? "waiting"})");
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
            // Four spare roofs keep the island growing while a supply chain comes online.
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

            ImmutableArray<string> wanted = _queueOverride ?? goal.Queue;
            bool queueWanted = wanted.Length > 0;
            bool queueMatches = queueWanted && ResearchQueueMatches(wanted);
            if (queueWanted && !queueMatches)
            {
                if (state.SentQueue is null)
                {
                    var command = new SetResearchQueue([.. wanted.Select(name => _content.Techs.IdOf(name))]);
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
            // March past unusable spots within this tick (up to 8 tries): bans are permanent until bridges open,
            // so retrying later would just re-propose the same failures. The loop ends at the first linkable spot
            // or when nothing is left, and it is deterministic (spiral order, same bans).
            PlacementCandidate? candidate = null;
            int linkWidth = 0;
            int linkHeight = 0;
            (TileCoord[] Road, TileCoord[] Bridge)? found = null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                candidate = def.Service is not null && AnyResidence()
                    ? FindServiceSpot(type, def, anchor, PlacementSearch.DefaultMaxRadius, EntityId.None)
                        ?? PlacementSearch.Find(
                            _simulation, type, anchor.X, anchor.Y, def.Footprint.Width, def.Footprint.Height,
                            isExcluded: (x, y, r) => IsBanned(typeName, x, y) || IsReservedForOther(typeName, x, y, r, def) || BlocksClaim(x, y, r, def))
                    : PlacementSearch.Find(
                        _simulation, type, anchor.X, anchor.Y, def.Footprint.Width, def.Footprint.Height);
                if (candidate is null)
                {
                    break;
                }

                linkWidth = candidate.Rotation.SwapsAxes() ? def.Footprint.Height : def.Footprint.Width;
                linkHeight = candidate.Rotation.SwapsAxes() ? def.Footprint.Width : def.Footprint.Height;
                if (SeversNetwork(candidate.X, candidate.Y, linkWidth, linkHeight))
                {
                    _banned.Add((typeName, candidate.X, candidate.Y, candidate.Rotation));
                    _notes.Add($"{typeName} at ({candidate.X}, {candidate.Y}) would seal the road network, skipped.");
                    candidate = null;
                    continue;
                }

                // Era I: lay the connector when one exists; a roadless placement still goes in (the advisor
                // retries the link as the network grows), and the derelict rule removes it if it never connects.
                found = FindLink(candidate.X, candidate.Y, linkWidth, linkHeight, anchor);
                if (found is null)
                {
                    _notes.Add($"{typeName} at ({candidate.X}, {candidate.Y}) has no road link yet, placing roadless.");
                    if (NeedsBridgeFor(candidate.X, candidate.Y, linkWidth, linkHeight, anchor))
                    {
                        _needBridges = true;
                        _notes.Add($"{typeName} at ({candidate.X}, {candidate.Y}) needs a bridge.");
                    }
                }

                break;
            }
            if (candidate is null)
            {
                state.Detail = $"no site for {typeName}";
                state.RetryTick = _simulation.CurrentTick + PlacementRetryTicks;
                return;
            }

            int width = linkWidth;
            int height = linkHeight;
            if (candidate is null)
            {
                state.Detail = $"no site for {typeName}";
                state.RetryTick = _simulation.CurrentTick + PlacementRetryTicks;
                return;
            }

            if (found is not null)
            {
                if (found.Value.Road.Length > 0)
                {
                    Send(new PlaceRoad(found.Value.Road, _roadType));
                }

                if (found.Value.Bridge.Length > 0)
                {
                    Send(new PlaceRoad(found.Value.Bridge, _bridgeType));
                }
            }

            var place = new PlaceBuilding(type, candidate.X, candidate.Y, candidate.Rotation);
            Send(place);
            ClaimFor(type, candidate.X, candidate.Y, candidate.Rotation);
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
            if (BridgesOpen() && !_bansCleared)
            {
                // New era of movement: every ban was earned on dirt alone, so re-evaluate everything with bridges.
                _banned.Clear();
                _linkBanned.Clear();
                _bansCleared = true;
                _notes.Add("bridges open: placement bans cleared.");
            }

            int roads = CountRoads();
            if (roads > _roadsSeen + 25)
            {
                // The network moved: linkless spots may link now, so re-evaluate them.
                _roadsSeen = roads;
                if (_linkBanned.Count > 0)
                {
                    _linkBanned.Clear();
                    _notes.Add($"road network grew to {roads} tiles: link bans cleared.");
                }
            }
            else if (_roadsSeen == 0)
            {
                _roadsSeen = roads;
            }

            RunShortages();
            RunAdvisorRoads();
            RunCoverageRepair();
            RunDerelictCleanup();
            RunBridgePrioritization();
            RunExpeditions();
            RunTraditions();
            RunFestivals();
            RunHappenings();
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

        // Era I: a construction site that cannot get road access within six days never completes (builders and
        // materials need the road), so keeping it only blocks its ground and its goal. Demolish the oldest such
        // site (one per day, lowest ID first), ban the spot, and let the goal or the shortage rule retry elsewhere.
        // Deterministic: ascending ID order, one demolition per day.
        private void RunDerelictCleanup()
        {
            List<EntityId> gone = [];
            foreach ((EntityId id, _) in _siteSeen)
            {
                if (!_simulation.Query.Map.TryGetBuilding(id, out BuildingInfo known) || known.State != BuildingState.Site)
                {
                    gone.Add(id);
                }
            }

            foreach (EntityId id in gone)
            {
                _siteSeen.Remove(id);
            }

            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (building.State != BuildingState.Site)
                {
                    continue;
                }

                if (_simulation.Query.Roads.HasRoadAccess(building.Id))
                {
                    continue;
                }

                if (!_siteSeen.TryGetValue(building.Id, out long seen))
                {
                    _siteSeen[building.Id] = _simulation.CurrentTick;
                    continue;
                }

                if (_simulation.CurrentTick - seen < DerelictTicks)
                {
                    continue;
                }

                (int X, int Y, int Width, int Height) box = (building.Origin.X, building.Origin.Y, building.Width, building.Height);
                if (FindLink(building.Origin.X, building.Origin.Y, building.Width, building.Height, box) is { } link
                    && (link.Road.Length > 0 || link.Bridge.Length > 0))
                {
                    continue;
                }

                string typeName = _content.Buildings.Names[building.Type.Value];
                Send(new Demolish(building.Id));
                _banned.Add((typeName, building.Origin.X, building.Origin.Y, building.Rotation));
                _notes.Add($"{typeName} at ({building.Origin.X}, {building.Origin.Y}) never gained road access and was removed.");
                return;
            }
        }

        // Whether only rivers stand between this footprint and the road network: the dirt link fails but a
        // bridgeable path exists. Sticky demand for bridges (see RunBridgePrioritization).
        private bool NeedsBridgeFor(int x, int y, int width, int height, (int X, int Y, int Width, int Height) anchor)
        {
            if (BridgesOpen())
            {
                return false;
            }

            if (RoadPlanner.FindConnector(
                _simulation, _content, _roadType, x, y, width, height,
                anchor.X, anchor.Y, anchor.Width, anchor.Height,
                avoid: [.. ClaimedTiles().Select(index => new TileCoord(index % _simulation.Query.Map.Width, index / _simulation.Query.Map.Width))]) is not null)
            {
                return false;
            }

            TileCoord[]? wet = SearchWithRivers(x, y, width, height, anchor, ClaimedTiles());
            if (wet is null)
            {
                return false;
            }

            List<TileCoord> bridges = [];
            foreach (TileCoord tile in wet)
            {
                if (_simulation.Query.Map.GetTile(tile.X, tile.Y).Has(TileFeatures.River))
                {
                    bridges.Add(tile);
                }
            }

            return bridges.Count > 0 && IsSingleNarrowRun(bridges);
        }

        // Era I: prioritise bridge-building only when the island needs it (a placement failed for bank reasons),
        // never globally — an early bridge tech costs research the starter never needs. Moves the tech to the front
        // of the research queue once its prerequisite is done; the queue then runs on as scripted.
        private void RunBridgePrioritization()
        {
            if (!_needBridges || _queueOverride is not null || BridgesOpen())
            {
                return;
            }

            if (!_content.Techs.TryGet("bridge_building", out TechId bridge) || !_content.Techs.TryGet("pathfinding", out TechId pathfinding))
            {
                return;
            }

            if (_simulation.Query.Research.IsDone(bridge))
            {
                return;
            }

            if (!_simulation.Query.Research.IsDone(pathfinding))
            {
                // The tech is still locked behind pathfinding: wait for the scripted queue to get there.
                return;
            }

            // Rebuild the script queue with the bridge first (deduplicated, done techs and the current target
            // dropped — the target stays the target and the queue follows it).
            TechId? current = _simulation.Query.Research.Target;
            List<string> queue = ["bridge_building"];
            foreach (BotGoal goal in _script.Goals)
            {
                if (goal is ResearchGoal research)
                {
                    foreach (string tech in research.Queue.Prepend(research.Tech))
                    {
                        if (tech != "bridge_building" && !queue.Contains(tech)
                            && _content.Techs.TryGet(tech, out TechId id) && !_simulation.Query.Research.IsDone(id)
                            && current != id)
                        {
                            queue.Add(tech);
                        }
                    }
                }
            }

            _queueOverride = [.. queue];
            Send(new SetResearchQueue([.. queue.Select(name => _content.Techs.IdOf(name))]));
            _notes.Add("bank exhausted: prioritising bridge_building.");
        }

        // Era I: an idle Scout's Lodge opens the neighbouring fog. One expedition per lodge at a time; the planner
        // names the lowest idle lodge and the lowest adjacent fogged region, so the choice is deterministic.
        private void RunExpeditions()
        {
            _expeditionSent.Clear();
            _expeditionRegions.Clear();
            while (true)
            {
                (EntityId Lodge, ushort Region)? next = FindUnsentExpedition();
                if (next is null)
                {
                    return;
                }

                Send(new StartExpedition(next.Value.Lodge, next.Value.Region));
                _expeditionSent.Add(next.Value.Lodge);
                _expeditionRegions.Add(next.Value.Region);
                _notes.Add($"expedition: lodge #{next.Value.Lodge.Value} sails to region {next.Value.Region}.");
            }
        }

        private (EntityId Lodge, ushort Region)? FindUnsentExpedition()
        {
            (EntityId Lodge, ushort Region)? found = ExpeditionPlanner.FindExpedition(_content, _simulation);
            if (found is null)
            {
                return null;
            }

            // The planner does not know what this tick already sent (commands apply on the next tick), so sent
            // lodges and targeted regions are skipped here; anything else means the planner has nothing new.
            if (_expeditionSent.Contains(found.Value.Lodge) || _expeditionRegions.Contains(found.Value.Region))
            {
                return null;
            }

            return found;
        }

        // Era I: re-slot traditions toward the heuristic whenever the unlocked set or the granted slots change.
        // Slotting is free, so the only throttle is a cooldown after a rejection (a lock race with the queue).
        private void RunTraditions()
        {
            List<SetTradition> planned;
            try
            {
                planned = TraditionHeuristics.PlanSlottings(_content, _simulation.Query.Culture);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or IndexOutOfRangeException)
            {
                return;
            }

            foreach (SetTradition command in planned)
            {
                string key = $"tradition:{command.Slot}:{command.Index}";
                if (OnCooldown(key))
                {
                    continue;
                }

                Send(command);
                _cooldowns.Add((key, _simulation.CurrentTick + TraditionCooldownTicks));
                _notes.Add(command.Tradition is null
                    ? $"tradition: cleared {command.Slot} slot {command.Index}."
                    : $"tradition: slotted {_content.Traditions.Names[command.Tradition.Value.Value]} in {command.Slot} slot {command.Index}.");
            }
        }

        // Era I: hold the Harvest Fair (or any unlocked festival) when the island can pay. One festival runs at a
        // time and each type once a year; the first affordable festival in content order wins, so the choice is
        // deterministic.
        private void RunFestivals()
        {
            if (_simulation.Query.Culture.IsFestivalRunning())
            {
                return;
            }

            EntityId ground = FirstFestivalGround();
            if (ground == EntityId.None)
            {
                return;
            }

            long year = new GameClock(_simulation.CurrentTick).Year;
            Season season = new GameClock(_simulation.CurrentTick).Season;
            foreach (FestivalDef def in _content.Festivals.Items)
            {
                if (!_simulation.Query.Progression.IsFestivalUnlocked(def.Id))
                {
                    continue;
                }

                if (_simulation.Query.Culture.FestivalsHeldInYear(def.Id, (int)year) > 0)
                {
                    continue;
                }

                if (def.Seasons.Length > 0 && !def.Seasons.Contains(season))
                {
                    continue;
                }

                if (!CanAfford(def.Cost))
                {
                    continue;
                }

                Send(new HoldFestival(ground, def.Id));
                _notes.Add($"festival: holding {_content.Festivals.Names[def.Id.Value]} at ground #{ground.Value}.");
                return;
            }
        }

        private EntityId FirstFestivalGround()
        {
            EntityId best = EntityId.None;
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (!building.IsComplete)
                {
                    continue;
                }

                bool isGround = false;
                foreach (string tag in _content.Get(building.Type).Tags)
                {
                    if (tag == "festival_ground")
                    {
                        isGround = true;
                        break;
                    }
                }

                if (!isGround)
                {
                    continue;
                }

                if (best == EntityId.None || building.Id < best)
                {
                    best = building.Id;
                }
            }

            return best;
        }

        // Era I: answer a pending happening on day 5, with its first option. Every option is gentle by lint
        // (EVL012), so the first one is always safe; the choice is deterministic.
        private void RunHappenings()
        {
            PendingHappening? pending = _simulation.Query.Happenings.PendingHappening();
            if (pending is null)
            {
                return;
            }

            if (_simulation.CurrentTick - pending.Value.RaisedTick < 5 * GameTime.TicksPerDay)
            {
                return;
            }

            string key = $"happening:{_content.Happenings.Names[pending.Value.Happening.Value]}";
            if (OnCooldown(key))
            {
                return;
            }

            Send(new ChooseHappeningOption(pending.Value.Happening, 0));
            _cooldowns.Add((key, _simulation.CurrentTick + ShortageCooldownTicks));
            _notes.Add($"happening: answered {_content.Happenings.Names[pending.Value.Happening.Value]} with option 0.");
        }

        private int CountRoads()
        {
            int count = 0;
            int mapWidth = _simulation.Query.Map.Width;
            int mapHeight = _simulation.Query.Map.Height;
            for (int y = 0; y < mapHeight; y++)
            {
                for (int x = 0; x < mapWidth; x++)
                {
                    if (_simulation.Query.Roads.RoadAt(x, y) is not null)
                    {
                        count++;
                    }
                }
            }

            return count;
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

                if (!ShouldQueue(_content.Buildings[producer.Value].Cost))
                {
                    // Era I: skip only when nothing is coming (no production of a missing good and no producer
                    // site on the way); otherwise queue it and let the materials arrive.
                    _notes.Add($"{name} short: {_content.Buildings.Names[producer.Value.Value]} has no production coming, skipped.");
                    continue;
                }

                string producerName = _content.Buildings.Names[producer.Value.Value];
                _notes.Add($"{name} short: adding {producerName}.");
                StartReactive(producer.Value, producerName, AnchorFor("founders_hall"));
            }
        }

        // A building the pool cannot pay for yet is still worth queueing when its missing goods are being
        // produced or have funded construction (a producer site) on the way: the materials arrive and the site
        // completes. Skip only when the island has no production of a missing good and nothing coming, which is
        // the stone-0 trap (a site waiting forever on goods that never arrive). Coin costs (none in Era I) count
        // as unqueueable until the bot reads the treasury. Festivals keep the strict check (CanAfford): their
        // goods leave the pool the moment they are held.
        private bool ShouldQueue(CostSpec cost)
        {
            if (cost.Coin != Amount.Zero)
            {
                return false;
            }

            foreach (GoodAmount need in cost.Goods)
            {
                if (_simulation.Query.Storage.Stock(need.Good).Milli >= need.Amount.Milli)
                {
                    continue;
                }

                if (_simulation.Query.Production.ProducedYesterday(need.Good).Milli > 0)
                {
                    continue;
                }

                if (HasProducerSite(need.Good))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        // Whether any producer site for a good stands (funded construction on the way). Complete buildings that
        // produced nothing yesterday do not count: a deadlocked producer is not evidence of income.
        private bool HasProducerSite(GoodId good)        {
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (building.State != BuildingState.Site)
                {
                    continue;
                }

                if (_content.Buildings[building.Type].Recipe is not RecipeId recipe)
                {
                    continue;
                }

                if (_content.Recipes[recipe].Outputs.Any(output => output.Good == good))
                {
                    return true;
                }
            }

            return false;
        }

        // Whether the pool pays a cost in full right now. Festivals take their goods at once, so they keep this
        // strict check; reactive construction uses ShouldQueue (goods on the way count).
        private bool CanAfford(CostSpec cost)
        {
            if (cost.Coin != Amount.Zero)
            {
                return false;
            }

            foreach (GoodAmount need in cost.Goods)
            {
                if (_simulation.Query.Storage.Stock(need.Good).Milli < need.Amount.Milli)
                {
                    return false;
                }
            }

            return true;
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

            if (!ShouldQueue(_content.Buildings[service.Value].Cost))
            {
                _cooldowns.Add(($"cover:{need}", _simulation.CurrentTick + ShortageCooldownTicks));
                _notes.Add($"house #{needy.Value} lacks {need} coverage: {_content.Buildings.Names[service.Value.Value]} has no production coming, skipped.");
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
            if (SeversNetwork(repair.X, repair.Y, width, height))
            {
                _banned.Add((_content.Buildings.Names[service.Value.Value], repair.X, repair.Y, repair.Rotation));
                _cooldowns.Add(($"cover:{need}", _simulation.CurrentTick + ShortageCooldownTicks));
                _notes.Add($"house #{needy.Value} lacks {need} coverage: {_content.Buildings.Names[service.Value.Value]} at ({repair.X}, {repair.Y}) would seal the road network, skipped.");
                return;
            }
            (TileCoord[] Road, TileCoord[] Bridge)? repairFound = FindLink(repair.X, repair.Y, width, height, anchor);
            if (repairFound is not null)
            {
                if (repairFound.Value.Road.Length > 0)
                {
                    Send(new PlaceRoad(repairFound.Value.Road, _roadType));
                }

                if (repairFound.Value.Bridge.Length > 0)
                {
                    Send(new PlaceRoad(repairFound.Value.Bridge, _bridgeType));
                }
            }

            var place = new PlaceBuilding(service.Value, repair.X, repair.Y, repair.Rotation);
            Send(place);
            ClaimFor(service.Value, repair.X, repair.Y, repair.Rotation);
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
                maxRadius, int.MaxValue, isExcluded: (x, y, r) => IsBanned(typeName, x, y) || IsReservedForOther(typeName, x, y, r, def) || BlocksClaim(x, y, r, def));
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
                maxRadius, int.MaxValue, isExcluded: (x, y, r) => IsBanned(typeName, x, y) || IsReservedForOther(typeName, x, y, r, def) || BlocksClaim(x, y, r, def));
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
                if (FindLink(
                    building.Origin.X, building.Origin.Y, building.Width, building.Height,
                    (building.Origin.X, building.Origin.Y, building.Width, building.Height)) is { } adviceLink)
                {
                    if (adviceLink.Road.Length > 0)
                    {
                        Send(new PlaceRoad(adviceLink.Road, _roadType));
                    }

                    if (adviceLink.Bridge.Length > 0)
                    {
                        Send(new PlaceRoad(adviceLink.Bridge, _bridgeType));
                    }

                    if (adviceLink.Road.Length > 0 || adviceLink.Bridge.Length > 0)
                    {
                        continue;
                    }
                }

                if (advice.Kind == AdviceKind.OutsideStorehouseReach && _simulation.Query.Roads.HasRoadAccess(building.Id))
                {
                    // Era I: prefer a storehouse the pool pays now or whose goods are coming; log the skip when
                    // nothing can be queued, instead of a site that waits forever on missing stone.
                    BuildingTypeId? storehouse = CheapestQueueableStorehouse();
                    if (storehouse is null)
                    {
                        _notes.Add($"{advice.Kind} persists for #{building.Id.Value}: no affordable storehouse, skipped.");
                        continue;
                    }

                    string storeName = _content.Buildings.Names[storehouse.Value.Value];
                    _notes.Add($"{advice.Kind} persists for #{building.Id.Value}: adding {storeName}.");
                    StartReactive(storehouse.Value, storeName, (building.Origin.X, building.Origin.Y, building.Width, building.Height));
                }
            }
        }

        // Era I: the cheapest storehouse worth queueing, preferring start-unlocked types, or null when none can
        // be queued. A site that waits forever on missing goods is worse than no site.
        private BuildingTypeId? CheapestQueueableStorehouse()
        {
            BuildingTypeId? best = null;
            long bestCost = long.MaxValue;
            bool bestStart = false;
            foreach (BuildingDef def in _content.Buildings.Items)
            {
                if (def.Unique || def.Storage is null || !_simulation.Query.Progression.IsUnlocked(def.Id))
                {
                    continue;
                }

                if (!ShouldQueue(def.Cost))
                {
                    continue;
                }

                long cost = def.Cost.Goods.Sum(static need => need.Amount.Milli);
                if (best is null || (def.Start && !bestStart) || (def.Start == bestStart && (cost < bestCost || (cost == bestCost && def.Id.Value < best.Value.Value))))
                {
                    best = def.Id;
                    bestCost = cost;
                    bestStart = def.Start;
                }
            }

            return best;
        }

        private void PostTick()
        {
            _claims.RemoveAll(claim => claim.Tick < _simulation.CurrentTick - 1);
            foreach (SimEvent raised in _simulation.DrainEvents())
            {
                if (raised is CommandRejected rejected)
                {
                    // The key carries the command and the handler's detail, so a validate-yes/apply-no pair can be
                    // told apart (same-tick overlaps, stale validations, or a real sim inconsistency).
                    string key = $"{rejected.Command.GetType().Name}({rejected.Code}): {DescribeCommand(rejected.Command)}: {rejected.Detail}";
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
                _residentsAtFirst = total;
            }

            if (residents >= 50 && _fiftyTick is null)
            {
                _fiftyTick = _simulation.CurrentTick;
            }

            if (residents >= 100 && _hundredTick is null)
            {
                _hundredTick = _simulation.CurrentTick;
                _residentsAtHundred = total;
            }

            if (total > _bestResidents)
            {
                _bestResidents = total;
                _bestTick = _simulation.CurrentTick;
            }
            else if (_simulation.CurrentTick - _bestTick >= GameTime.TicksPerDay * GameTime.DaysPerSeason)
            {
                _stalls.Add(new BalanceStall(_bestTick, _simulation.CurrentTick, _bestResidents, total, TopAdvice()));
                _bestResidents = total;
                _bestTick = _simulation.CurrentTick;
            }

            if (_eraTwoTick is null && IsEraTwoBegun())
            {
                _eraTwoTick = _simulation.CurrentTick;
                _residentsAtEraTwo = total;
                _villagersAtEraTwo = _simulation.Query.Population.Residents(_milestoneTierId);
            }
        }

        private bool IsEraTwoBegun() =>
            _content.Get(_simulation.Query.Progression.CurrentEra).Order >= _content.Get(_township).Order;

        // One line per rejected command for the report: type, target and detail, all deterministic.
        private string DescribeCommand(Command command) => command switch
        {
            PlaceBuilding place => $"{_content.Buildings.Names[place.Type.Value]} at ({place.X},{place.Y})",
            PlaceRoad road => road.Path.Length == 0
                ? $"{_content.Buildings.Names[road.RoadType.Value]} empty path"
                : $"{_content.Buildings.Names[road.RoadType.Value]} {road.Path.Length} tiles from ({road.Path[0].X},{road.Path[0].Y})",
            SetResearchTarget target => $"research target {_content.Techs.Names[target.Tech.Value]}",
            SetResearchQueue queue => $"research queue {queue.Queue.Length} techs",
            SetCivicTarget target => $"civic target {_content.Civics.Names[target.Civic.Value]}",
            SetCivicQueue queue => $"civic queue {queue.Queue.Length} civics",
            SetTradition tradition => $"tradition {tradition.Slot} slot {tradition.Index}",
            StartExpedition expedition => $"expedition lodge #{expedition.Lodge.Value} to region {expedition.Region}",
            HoldFestival festival => $"festival {_content.Festivals.Names[festival.Festival.Value]} at ground #{festival.Ground.Value}",
            ChooseHappeningOption happening => $"happening {_content.Happenings.Names[happening.Happening.Value]} option {happening.OptionIndex}",
            SetHouseUpgradeLock house => $"house lock #{house.House.Value}={house.Locked}",
            Demolish demolish => $"demolish #{demolish.Building.Value}",
            _ => command.GetType().Name,
        };

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
                isExcluded: (x, y, r) => IsBanned(typeName, x, y) || IsReservedForOther(typeName, x, y, r, def) || BlocksClaim(x, y, r, def));
            if (candidate is null)
            {
                _notes.Add($"no site for reactive {typeName}.");
                return;
            }

            int width = candidate.Rotation.SwapsAxes() ? def.Footprint.Height : def.Footprint.Width;
            int height = candidate.Rotation.SwapsAxes() ? def.Footprint.Width : def.Footprint.Height;
            if (SeversNetwork(candidate.X, candidate.Y, width, height))
            {
                _banned.Add((typeName, candidate.X, candidate.Y, candidate.Rotation));
                _notes.Add($"reactive {typeName} at ({candidate.X}, {candidate.Y}) would seal the road network, skipped.");
                return;
            }
            (TileCoord[] Road, TileCoord[] Bridge)? reactiveFound = FindLink(candidate.X, candidate.Y, width, height, anchor);
            if (reactiveFound is null && NeedsBridgeFor(candidate.X, candidate.Y, width, height, anchor))
            {
                _needBridges = true;
                _notes.Add($"reactive {typeName} at ({candidate.X}, {candidate.Y}) needs a bridge.");
            }

            if (reactiveFound is not null)
            {
                if (reactiveFound.Value.Road.Length > 0)
                {
                    Send(new PlaceRoad(reactiveFound.Value.Road, _roadType));
                }

                if (reactiveFound.Value.Bridge.Length > 0)
                {
                    Send(new PlaceRoad(reactiveFound.Value.Bridge, _bridgeType));
                }
            }

            var place = new PlaceBuilding(type, candidate.X, candidate.Y, candidate.Rotation);
            Send(place);
            ClaimFor(type, candidate.X, candidate.Y, candidate.Rotation);
            _reactive.Add(new PendingPlacement(type, typeName, candidate.X, candidate.Y, candidate.Rotation, place, _simulation.CurrentTick));
        }

        // Era I: a real player plans the orchard belt before the houses sprawl, so the bot reserves it. Orcharding
        // unlocks late, after the settlement has covered every nearby fertile block, and without a reserve the
        // orchard (and cider comfort) never happens. Two apple-fertile 4x4 blocks near the hall are kept free of
        // buildings until the orchard lands; roads may still cross them (the orchard needs a road beside it).
        // The mechanism is generic (reserved rects kept out of every other placement); only the orchard uses it
        // in Era I (grain farms land early on free ground, and rotation farms upgrade in place).
        private void ReserveFarmSites()
        {
            if (_farmsReserved)
            {
                return;
            }

            _farmsReserved = true;
            if (!_content.Buildings.TryGet("orchard", out BuildingTypeId orchard))
            {
                return;
            }

            BuildingDef def = _content.Buildings[orchard];
            int width = def.Footprint.Width;
            int height = def.Footprint.Height;
            BuildingTypeId hallType = _content.Required.FoundersHall;
            TileCoord hall = default;
            int hallWidth = 0;
            int hallHeight = 0;
            bool found = false;
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (building.Type == hallType)
                {
                    hall = building.Origin;
                    hallWidth = building.Width;
                    hallHeight = building.Height;
                    found = true;
                    break;
                }
            }

            if (!found || !_content.Terrains.TryGet("plains", out TerrainId plains))
            {
                return;
            }

            // Orcharding needs a quarter of the footprint fertile; the reserve asks the same.
            int fertileNeeded = Math.Max(1, (width * height * 25 + 99) / 100);
            int reach = _content.Buildings[hallType].Storage?.Reach ?? int.MaxValue;
            int mapWidth = _simulation.Query.Map.Width;
            int mapHeight = _simulation.Query.Map.Height;
            for (int ring = 0; ring <= PlacementSearch.DefaultMaxRadius && _reservedFarms.Count < 2; ring++)
            {
                for (int dy = -ring; dy <= ring && _reservedFarms.Count < 2; dy++)
                {
                    for (int dx = -ring; dx <= ring && _reservedFarms.Count < 2; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                        {
                            continue;
                        }

                        int x = hall.X + dx;
                        int y = hall.Y + dy;
                        if (x < 0 || y < 0 || x + width > mapWidth || y + height > mapHeight)
                        {
                            continue;
                        }

                        if (ChebyshevTo(x, y, width, height, hall, hallWidth, hallHeight) > reach)
                        {
                            continue;
                        }

                        int fertile = 0;
                        bool ok = true;
                        for (int ty = y; ty < y + height && ok; ty++)
                        {
                            for (int tx = x; tx < x + width; tx++)
                            {
                                if (!_bank[ty * mapWidth + tx])
                                {
                                    ok = false;
                                    break;
                                }

                                TileInfo tile = _simulation.Query.Map.GetTile(tx, ty);
                                if (tile.Terrain != plains || tile.Has(TileFeatures.River)
                                    || !_simulation.Query.Exploration.IsRevealed(tile.Region)
                                    || _simulation.Query.Map.BuildingAt(tx, ty) != EntityId.None)
                                {
                                    ok = false;
                                    break;
                                }

                                if (tile.IsFertileFor(CropKind.Apples))
                                {
                                    fertile++;
                                }
                            }
                        }

                        if (ok && fertile >= fertileNeeded)
                        {
                            _reservedFarms.Add((x, y, width, height));
                            _notes.Add($"reserved orchard site at ({x}, {y}).");
                        }
                    }
                }
            }

            if (_reservedFarms.Count == 0)
            {
                _notes.Add("no orchard site to reserve near the hall.");
            }
        }

        // Era I: the hall's river bank — every tile reachable from the hall without crossing a river. Until
        // bridges open, the whole early settlement must stay on this bank (a building across the water can never
        // be connected, as seed 2 proved: its first huts sat roadless all game). Computed once: rivers never move.
        // Fog is not consulted: placement validation already refuses fogged tiles.
        private bool[] ComputeBank()
        {
            int width = _simulation.Query.Map.Width;
            int height = _simulation.Query.Map.Height;
            bool[] bank = new bool[width * height];
            BuildingTypeId hallType = _content.Required.FoundersHall;
            TileCoord hall = default;
            bool found = false;
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (building.Type == hallType)
                {
                    hall = building.Origin;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Array.Fill(bank, true);
                return bank;
            }

            int[] stack = new int[width * height];
            int top = 0;
            stack[top++] = hall.Y * width + hall.X;
            bank[hall.Y * width + hall.X] = true;
            while (top > 0)
            {
                int at = stack[--top];
                int ax = at % width;
                int ay = at / width;
                TryAddBank(bank, width, ax - 1, ay, stack, ref top);
                TryAddBank(bank, width, ax + 1, ay, stack, ref top);
                TryAddBank(bank, width, ax, ay - 1, stack, ref top);
                TryAddBank(bank, width, ax, ay + 1, stack, ref top);
            }

            return bank;
        }

        private void TryAddBank(bool[] bank, int width, int x, int y, int[] stack, ref int top)
        {
            int height = bank.Length / width;
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return;
            }

            int index = y * width + x;
            if (bank[index])
            {
                return;
            }

            TileInfo tile = _simulation.Query.Map.GetTile(x, y);
            if (tile.Has(TileFeatures.River))
            {
                return;
            }

            // The bank is exactly the ground a dirt road can cross: buildable, not road-blocking, with its
            // access open. Water, locked marsh and highlands stop the flood the way they stop roads, or the
            // bank leaks around every river mouth and never constrains anything (seed 2).
            if (!BankWalkable(tile))
            {
                return;
            }

            bank[index] = true;
            stack[top++] = index;
        }

        private bool BankWalkable(TileInfo tile)
        {
            TerrainDef terrain = _content.Get(tile.Terrain);
            if (!terrain.Buildable || terrain.BlocksRoads)
            {
                return false;
            }

            return terrain.Access is not { } access || _simulation.Query.Progression.HasAccess(access);
        }

        // Whether the island can cross narrow rivers: the bridge type is unlocked and the access is open
        // (one tech grants both). While closed, only dirt connectors are laid.
        private bool BridgesOpen() =>
            _bridgeType != default
            && _simulation.Query.Progression.IsUnlocked(_bridgeType)
            && _simulation.Query.Progression.HasAccess(TerrainAccess.NarrowRivers);

        // Whether occupying this footprint would strand an existing building: some building whose ring the hall's
        // road network reaches today would lose every route to it. Checked before committing (with a ban-and-retry
        // at the call site), so the bot never walls in its own settlement — seed 2's woodcutter plugging the only
        // river-tip gap. Deterministic: fixed neighbour order, ascending building order.
        private bool SeversNetwork(int x, int y, int width, int height)
        {
            bool[] before = ReachFromHall(null);
            bool[] after = ReachFromHall((x, y, width, height));
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (RingReachable(building, before) && !RingReachable(building, after))
                {
                    return true;
                }
            }

            return false;
        }

        private bool[] ReachFromHall((int X, int Y, int Width, int Height)? blocked)
        {
            int mapWidth = _simulation.Query.Map.Width;
            int mapHeight = _simulation.Query.Map.Height;
            bool[] reached = new bool[mapWidth * mapHeight];
            BuildingTypeId hallType = _content.Required.FoundersHall;
            TileCoord hall = default;
            int hallWidth = 0;
            int hallHeight = 0;
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (building.Type == hallType)
                {
                    hall = building.Origin;
                    hallWidth = building.Width;
                    hallHeight = building.Height;
                    break;
                }
            }

            if (hallWidth <= 0)
            {
                return reached;
            }

            int[] queue = new int[mapWidth * mapHeight];
            int head = 0;
            int tail = 0;
            for (int ty = hall.Y - 1; ty <= hall.Y + hallHeight; ty++)
            {
                for (int tx = hall.X - 1; tx <= hall.X + hallWidth; tx++)
                {
                    bool inX = tx >= hall.X && tx < hall.X + hallWidth;
                    bool inY = ty >= hall.Y && ty < hall.Y + hallHeight;
                    if (!((tx == hall.X - 1 || tx == hall.X + hallWidth) ? inY : (ty == hall.Y - 1 || ty == hall.Y + hallHeight) && inX))
                    {
                        continue;
                    }

                    if (tx < 0 || ty < 0 || tx >= mapWidth || ty >= mapHeight || reached[ty * mapWidth + tx])
                    {
                        continue;
                    }

                    if (!NetworkWalkable(tx, ty, blocked))
                    {
                        continue;
                    }

                    reached[ty * mapWidth + tx] = true;
                    queue[tail++] = ty * mapWidth + tx;
                }
            }

            ReadOnlySpan<int> dx = [0, -1, 1, 0];
            ReadOnlySpan<int> dy = [-1, 0, 0, 1];
            while (head < tail)
            {
                int at = queue[head++];
                int cx = at % mapWidth;
                int cy = at / mapWidth;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + dx[d];
                    int ny = cy + dy[d];
                    if (nx < 0 || ny < 0 || nx >= mapWidth || ny >= mapHeight || reached[ny * mapWidth + nx])
                    {
                        continue;
                    }

                    if (!NetworkWalkable(nx, ny, blocked))
                    {
                        continue;
                    }

                    reached[ny * mapWidth + nx] = true;
                    queue[tail++] = ny * mapWidth + nx;
                }
            }

            return reached;
        }

        private bool NetworkWalkable(int x, int y, (int X, int Y, int Width, int Height)? blocked)
        {
            if (blocked is { } rect
                && x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height)
            {
                return false;
            }

            if (_simulation.Query.Map.BuildingAt(x, y) != EntityId.None)
            {
                return false;
            }

            if (_simulation.Query.Roads.RoadAt(x, y) is not null)
            {
                return true;
            }

            return BankWalkable(_simulation.Query.Map.GetTile(x, y));
        }

        private bool RingReachable(BuildingInfo building, bool[] reached)
        {
            int mapWidth = _simulation.Query.Map.Width;
            int mapHeight = _simulation.Query.Map.Height;
            for (int ty = building.Origin.Y - 1; ty <= building.Origin.Y + building.Height; ty++)
            {
                for (int tx = building.Origin.X - 1; tx <= building.Origin.X + building.Width; tx++)
                {
                    bool inX = tx >= building.Origin.X && tx < building.Origin.X + building.Width;
                    bool inY = ty >= building.Origin.Y && ty < building.Origin.Y + building.Height;
                    if (!((tx == building.Origin.X - 1 || tx == building.Origin.X + building.Width) ? inY : (ty == building.Origin.Y - 1 || ty == building.Origin.Y + building.Height) && inX))
                    {
                        continue;
                    }

                    if (tx >= 0 && ty >= 0 && tx < mapWidth && ty < mapHeight && reached[ty * mapWidth + tx])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // Whether a footprint lies off the hall's bank while bridges are closed: such placements wait (or time
        // out) instead of stranding a building across the water.
        // The road tiles plus the bridge tiles joining a footprint to the network, or null when no link exists.
        // Prefers the ordinary dirt connector; when that fails and bridges are open, plans a link that crosses
        // narrow rivers on timber bridges. Deterministic: the breadth-first search runs in fixed neighbour order.
        private (TileCoord[] Road, TileCoord[] Bridge)? FindLink(
            int x, int y, int width, int height, (int X, int Y, int Width, int Height) anchor)
        {
            HashSet<int> avoid = ClaimedTiles();
            TileCoord[]? road = RoadPlanner.FindConnector(
                _simulation, _content, _roadType,
                x, y, width, height,
                anchor.X, anchor.Y, anchor.Width, anchor.Height,
                avoid: [.. avoid.Select(index => new TileCoord(index % _simulation.Query.Map.Width, index / _simulation.Query.Map.Width))]);
            if (road is not null)
            {
                return (road, []);
            }

            if (!BridgesOpen())
            {
                return null;
            }

            TileCoord[]? path = SearchWithRivers(x, y, width, height, anchor, avoid);
            if (path is null)
            {
                return null;
            }

            List<TileCoord> roads = [];
            List<TileCoord> bridges = [];
            foreach (TileCoord tile in path)
            {
                if (_simulation.Query.Map.GetTile(tile.X, tile.Y).Has(TileFeatures.River))
                {
                    bridges.Add(tile);
                }
                else
                {
                    roads.Add(tile);
                }
            }

            if (bridges.Count > 0 && !IsSingleNarrowRun(bridges))
            {
                return null;
            }

            if (_simulation.Query.Placement.ValidateRoad([.. roads], _roadType).CanPlace != true
                || (bridges.Count > 0 && _simulation.Query.Placement.ValidateRoad([.. bridges], _bridgeType).CanPlace != true))
            {
                return null;
            }

            return ([.. roads], [.. bridges]);
        }

        // Every tile of every same-tick footprint the simulation has not applied yet, by tile index. Roads routed
        // through them would validate free and be refused on apply, stranding the building the road was for.
        private HashSet<int> ClaimedTiles()
        {
            int mapWidth = _simulation.Query.Map.Width;
            HashSet<int> tiles = [];
            foreach ((int x, int y, int width, int height, _) in _claims)
            {
                for (int ty = y; ty < y + height; ty++)
                {
                    for (int tx = x; tx < x + width; tx++)
                    {
                        if (tx >= 0 && ty >= 0 && tx < mapWidth && ty < _simulation.Query.Map.Height)
                        {
                            tiles.Add(ty * mapWidth + tx);
                        }
                    }
                }
            }

            return tiles;
        }

        // Breadth-first search from a footprint's ring to the road network, wading through narrow rivers: river
        // tiles are enterable, everything else follows the road validator's ground rules. North, west, east, south.
        private TileCoord[]? SearchWithRivers(
            int x, int y, int width, int height, (int X, int Y, int Width, int Height) anchor, HashSet<int>? avoid = null)
        {
            int mapWidth = _simulation.Query.Map.Width;
            int mapHeight = _simulation.Query.Map.Height;
            bool hasRoads = false;
            for (int ry = 0; ry < mapHeight && !hasRoads; ry++)
            {
                for (int rx = 0; rx < mapWidth; rx++)
                {
                    if (_simulation.Query.Roads.RoadAt(rx, ry) is not null)
                    {
                        hasRoads = true;
                        break;
                    }
                }
            }

            List<int> starts = [];
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

                    TileCoord tile = new(tx, ty);
                    if (tile.IsInside(mapWidth, mapHeight) && LinkWalkable(tile, x, y, width, height)
                        && (avoid is null || !avoid.Contains(tile.ToIndex(mapWidth))))
                    {
                        starts.Add(tile.ToIndex(mapWidth));
                    }
                }
            }

            if (starts.Count == 0)
            {
                return null;
            }

            if (hasRoads)
            {
                foreach (int start in starts)
                {
                    if (_simulation.Query.Roads.RoadAt(start % mapWidth, start / mapWidth) is not null)
                    {
                        return [];
                    }
                }
            }

            int[] previous = new int[mapWidth * mapHeight];
            Array.Fill(previous, -2);
            Queue<int> queue = new();
            foreach (int start in starts)
            {
                previous[start] = -1;
                queue.Enqueue(start);
            }

            ReadOnlySpan<int> dx = [0, -1, 1, 0];
            ReadOnlySpan<int> dy = [-1, 0, 0, 1];
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int cx = current % mapWidth;
                int cy = current / mapWidth;
                TileCoord tile = new(cx, cy);
                if (IsLinkTarget(tile, hasRoads, anchor))
                {
                    return BuildLinkPath(previous, current, mapWidth);
                }

                for (int d = 0; d < 4; d++)
                {
                    TileCoord next = new(cx + dx[d], cy + dy[d]);
                    if (!next.IsInside(mapWidth, mapHeight))
                    {
                        continue;
                    }

                    int index = next.ToIndex(mapWidth);
                    if (previous[index] != -2 || (avoid is not null && avoid.Contains(index)))
                    {
                        continue;
                    }

                    if (!IsLinkTarget(next, hasRoads, anchor) && !LinkWalkable(next, x, y, width, height))
                    {
                        continue;
                    }

                    previous[index] = current;
                    queue.Enqueue(index);
                }
            }

            return null;
        }

        private bool IsLinkTarget(TileCoord tile, bool hasRoads, (int X, int Y, int Width, int Height) anchor)
        {
            if (hasRoads)
            {
                return _simulation.Query.Roads.RoadAt(tile.X, tile.Y) is not null;
            }

            return tile.X >= anchor.X - 1 && tile.X <= anchor.X + anchor.Width
                && tile.Y >= anchor.Y - 1 && tile.Y <= anchor.Y + anchor.Height
                && !(tile.X >= anchor.X && tile.X < anchor.X + anchor.Width
                    && tile.Y >= anchor.Y && tile.Y < anchor.Y + anchor.Height);
        }

        private bool LinkWalkable(TileCoord tile, int footprintX, int footprintY, int footprintWidth, int footprintHeight)
        {
            if (tile.X >= footprintX && tile.X < footprintX + footprintWidth
                && tile.Y >= footprintY && tile.Y < footprintY + footprintHeight)
            {
                return false;
            }

            if (_simulation.Query.Map.BuildingAt(tile.X, tile.Y) != EntityId.None)
            {
                return false;
            }

            if (_simulation.Query.Roads.RoadAt(tile.X, tile.Y) is not null)
            {
                return true;
            }

            TileInfo info = _simulation.Query.Map.GetTile(tile.X, tile.Y);
            if (info.Has(TileFeatures.River))
            {
                return info.RiverWidth == RiverWidthClass.Narrow;
            }

            TerrainDef terrain = _content.Get(info.Terrain);
            if (terrain.Access is { } access && !_simulation.Query.Progression.HasAccess(access))
            {
                return false;
            }

            return terrain.Buildable && !terrain.BlocksRoads;
        }

        // Whether bridge tiles form one straight run over narrow water within the bridge's span: the only
        // crossing the bot lays (wide rivers and bends wait for Stone Bridges).
        private bool IsSingleNarrowRun(List<TileCoord> bridges)
        {
            if (bridges.Count == 0)
            {
                return false;
            }

            int span = _content.Buildings[_bridgeType].BridgeSpan ?? 0;
            if (bridges.Count > span)
            {
                return false;
            }

            bool sameX = true;
            bool sameY = true;
            for (int i = 1; i < bridges.Count; i++)
            {
                sameX &= bridges[i].X == bridges[0].X;
                sameY &= bridges[i].Y == bridges[0].Y;
            }

            return sameX || sameY;
        }

        private static TileCoord[]? BuildLinkPath(int[] previous, int target, int mapWidth)
        {
            if (previous[target] == -1)
            {
                return [new TileCoord(target % mapWidth, target / mapWidth)];
            }

            List<int> reversed = [];
            int current = previous[target];
            while (current >= 0)
            {
                reversed.Add(current);
                current = previous[current];
            }

            if (reversed.Count == 0)
            {
                return null;
            }

            reversed.Reverse();
            var path = new TileCoord[reversed.Count];
            for (int i = 0; i < reversed.Count; i++)
            {
                path[i] = new TileCoord(reversed[i] % mapWidth, reversed[i] / mapWidth);
            }

            return path;
        }

        // Whether a building other than the orchard may not cover this footprint: it overlaps a reserve.
        private bool IsReservedForOther(string typeName, int x, int y, Rotation rotation, BuildingDef def)
        {
            if (_reservedFarms.Count == 0 || typeName == "orchard")
            {
                return false;
            }

            int width = rotation.SwapsAxes() ? def.Footprint.Height : def.Footprint.Width;
            int height = rotation.SwapsAxes() ? def.Footprint.Width : def.Footprint.Height;
            foreach ((int reservedX, int reservedY, int reservedWidth, int reservedHeight) in _reservedFarms)
            {
                if (x < reservedX + reservedWidth && reservedX < x + width
                    && y < reservedY + reservedHeight && reservedY < y + height)
                {
                    return true;
                }
            }

            return false;
        }

        private static int ChebyshevTo(int x, int y, int width, int height, TileCoord hall, int hallWidth, int hallHeight)
        {
            int dx = 0;
            if (x + width - 1 < hall.X)
            {
                dx = hall.X - (x + width - 1);
            }
            else if (x > hall.X + hallWidth - 1)
            {
                dx = x - (hall.X + hallWidth - 1);
            }

            int dy = 0;
            if (y + height - 1 < hall.Y)
            {
                dy = hall.Y - (y + height - 1);
            }
            else if (y > hall.Y + hallHeight - 1)
            {
                dy = y - (hall.Y + hallHeight - 1);
            }

            return Math.Max(dx, dy);
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
            List<EntityId> ids = [];
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

        // The everyday road the bot lays between buildings: the cheapest unlocked-from-start road by goods cost.
        // Bridges are never the default: a bridge type only paves river tiles in straight runs.
        private static BuildingTypeId CheapestRoad(ContentDb content)
        {
            BuildingTypeId best = default;
            long bestCost = long.MaxValue;
            BuildingTypeId bestStart = default;
            long bestStartCost = long.MaxValue;
            foreach (BuildingDef def in content.Buildings.Items)
            {
                if (def.RoadCost is null || def.BridgeSpan is not null)
                {
                    continue;
                }

                long cost = 0;
                foreach (GoodAmount need in def.Cost.Goods)
                {
                    cost += need.Amount.Milli;
                }

                if (cost < bestCost || (cost == bestCost && (best == default || def.Id.Value < best.Value)))
                {
                    best = def.Id;
                    bestCost = cost;
                }

                if (def.Start && (cost < bestStartCost || (cost == bestStartCost && (bestStart == default || def.Id.Value < bestStart.Value))))
                {
                    bestStart = def.Id;
                    bestStartCost = cost;
                }
            }

            return bestStart != default ? bestStart
                : best != default ? best
                : throw new BotScriptException("invalid content for the balance bot: no road types.");
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

        // Footprints the bot already sent this tick but the simulation has not applied yet: Validate would still
        // call them free, so a second placement the same tick could overlap them and be refused on apply (the
        // validate-yes/apply-no pair). Claims close the one-tick window; applied buildings block via occupancy.
        private void ClaimFor(BuildingTypeId type, int x, int y, Rotation rotation)
        {
            BuildingDef def = _content.Buildings[type];
            int width = rotation.SwapsAxes() ? def.Footprint.Height : def.Footprint.Width;
            int height = rotation.SwapsAxes() ? def.Footprint.Width : def.Footprint.Height;
            _claims.Add((x, y, width, height, _simulation.CurrentTick));
        }

        private bool BlocksClaim(int x, int y, Rotation rotation, BuildingDef def)
        {
            int width = rotation.SwapsAxes() ? def.Footprint.Height : def.Footprint.Width;
            int height = rotation.SwapsAxes() ? def.Footprint.Width : def.Footprint.Height;
            foreach ((int claimedX, int claimedY, int claimedWidth, int claimedHeight, long tick) in _claims)
            {
                if (tick < _simulation.CurrentTick - 1)
                {
                    continue;
                }

                if (x < claimedX + claimedWidth && claimedX < x + width
                    && y < claimedY + claimedHeight && claimedY < y + height)
                {
                    return true;
                }
            }

            return false;
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

            // A linkless spot stays banned until the road network moves (see RunReactive): retrying every tick
            // would just re-propose the same failures, but new roads may open a link.
            foreach ((string bannedType, int bannedX, int bannedY, _) in _linkBanned)
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

        private EraOneReport Assemble()
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
            GatewayStatus gateway = _simulation.Query.Progression.GatewayStatus(_township);
            _goalResults.Sort(static (left, right) => left.Index.CompareTo(right.Index));
            var @base = new BalanceReport(
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
            return new EraOneReport(
                @base,
                new EraOneEraResult(_eraTwoTick is not null, _eraTwoTick, _residentsAtEraTwo, _villagersAtEraTwo, _residentsAtFirst, _residentsAtHundred),
                gateway.TechDone,
                gateway.CivicDone);
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