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

/// Script goals: the ordered build/research/civic/housing goals and their placement tracking.
public static partial class EraOneBot
{
    private sealed partial class Runner
    {

        private void RunGoals()
        {
            // Research and civic goals set their target and queue, then track completion in the background while
            // later goals proceed: knowledge and culture accrue over days, and blocking every building on them would
            // idle the island. Every other goal blocks in order until it holds or its timeout passes.
            for (int i = 0; i < _states.Length; i++)
            {
                BotGoalState state = _states[i];
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
                BotGoalState state = _states[_goalIndex];
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

        private void RunBuild(BuildGoal goal, BotGoalState state)
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

        private void RunHouses(HousesUntilGoal goal, BotGoalState state)
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
        private void RunUnlock(UnlockHousesGoal goal, BotGoalState state)
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
        private void RunLock(LockHousesGoal goal, BotGoalState state)
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

        private void TrackResearch(ResearchGoal goal, int index, BotGoalState state)
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

        private void TrackCivic(CivicGoal goal, int index, BotGoalState state)
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
        private void RunPlacement(BotGoalState state, BuildingTypeId type, string typeName, (int X, int Y, int Width, int Height) anchor, bool checkCoverage)
        {
            if (state.Pending is BotPendingPlacement pending)
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
                            isExcluded: (x, y, r) => IsBanned(typeName, x, y) || IsReservedForOther(typeName, x, y, r, def) || BlocksClaim(x, y, r, def) || OffBank(x, y, r, def))
                    : PlacementSearch.Find(
                        _simulation, type, anchor.X, anchor.Y, def.Footprint.Width, def.Footprint.Height,
                        isExcluded: (x, y, r) => IsBanned(typeName, x, y) || IsReservedForOther(typeName, x, y, r, def) || BlocksClaim(x, y, r, def) || OffBank(x, y, r, def));
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
            state.Pending = new BotPendingPlacement(type, typeName, candidate.X, candidate.Y, candidate.Rotation, place, _simulation.CurrentTick);
            state.Detail = $"{typeName} at ({candidate.X}, {candidate.Y})";
        }

        // Returns true when the pending placement resolved and the caller should look for the next one.
        private bool UpdatePending(BotPendingPlacement pending, bool checkCoverage)
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

        private void Finish(BotGoal goal, int index, BotGoalState state, BalanceGoalStatus status, string detail) =>
            BotGoals.Finish(_goalResults, _simulation.CurrentTick, goal, index, state, status, detail);
    }
}
