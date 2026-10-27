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

/// Reactive rules: shortages, coverage repair, road advice, derelict cleanup and bridge demand.
public static partial class EraOneBot
{
    private sealed partial class Runner
    {

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

                if (!_siteSeen.TryGetValue(building.Id, out long seen))
                {
                    _siteSeen[building.Id] = _simulation.CurrentTick;
                    continue;
                }

                if (!_simulation.Query.Roads.HasRoadAccess(building.Id))
                {
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

                    string roadlessName = _content.Buildings.Names[building.Type.Value];
                    Send(new Demolish(building.Id));
                    _banned.Add((roadlessName, building.Origin.X, building.Origin.Y, building.Rotation));
                    _notes.Add($"{roadlessName} at ({building.Origin.X}, {building.Origin.Y}) never gained road access and was removed.");
                    return;
                }

                // A site the builders can reach but that waits on storehouse coverage never completes until a
                // storehouse covers it; on laced ground none can (seed 2's storehouse waited all game and
                // blocked every later one). Give coverage twice the roadless grace to arrive, then clear the
                // site and ban the spot so the rules retry on ground a storehouse can actually cover.
                if (_simulation.CurrentTick - seen < StalledSiteTicks)
                {
                    continue;
                }

                if (_simulation.Query.Construction.TryGetSite(building.Id, out SiteInfo stalled)
                    && stalled.Stall == StallReason.NoStorehouseReach)
                {
                    string stalledName = _content.Buildings.Names[building.Type.Value];
                    Send(new Demolish(building.Id));
                    _banned.Add((stalledName, building.Origin.X, building.Origin.Y, building.Rotation));
                    _notes.Add($"{stalledName} at ({building.Origin.X}, {building.Origin.Y}) never gained storehouse coverage and was removed.");
                    return;
                }
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

            foreach (BotPendingPlacement active in _reactive)
            {
                if (active.CoverHouseId != EntityId.None && !active.Dead)
                {
                    return;
                }
            }

            // The repair prefers a spot that already covers the needy house over today's roads; otherwise it takes
            // the nearest free spot, connects it with a road, and lets the completion check decide. A repair off
            // the network is only placed when it has a link now: roadless fallback services never complete
            // (seed 2's market loop placed and lost 14) and each one burns a workforce the houses need.
            BuildingDef serviceDef = _content.Buildings[service.Value];
            (int X, int Y, int Width, int Height) anchor = (house.Origin.X, house.Origin.Y, house.Width, house.Height);
            PlacementCandidate? repair = FindServiceSpot(service.Value, serviceDef, anchor, 10, needy);
            bool fallback = repair is null;
            repair ??= FindFallbackSpot(service.Value, serviceDef, anchor, 10);
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
            if (repairFound is null && fallback)
            {
                // No road reaches the fallback yet: wait for the network (or bridges) instead of planting a
                // site the builders cannot reach. A longer cooldown than the 600-tick retry keeps the rule from
                // re-proposing the same unreachable ground every few minutes.
                if (NeedsBridgeFor(repair.X, repair.Y, width, height, anchor))
                {
                    _needBridges = true;
                    _notes.Add($"house #{needy.Value} lacks {need} coverage: {_content.Buildings.Names[service.Value.Value]} at ({repair.X}, {repair.Y}) needs a bridge.");
                }
                else
                {
                    _notes.Add($"house #{needy.Value} lacks {need} coverage: {_content.Buildings.Names[service.Value.Value]} at ({repair.X}, {repair.Y}) has no road link yet, waiting.");
                }

                _cooldowns.Add(($"cover:{need}", _simulation.CurrentTick + CoverageBackoffTicks));
                return;
            }

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
            _reactive.Add(new BotPendingPlacement(service.Value, _content.Buildings.Names[service.Value.Value], repair.X, repair.Y, repair.Rotation, place, _simulation.CurrentTick)
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
                maxRadius, int.MaxValue, isExcluded: (x, y, r) => IsBanned(typeName, x, y) || IsReservedForOther(typeName, x, y, r, def) || BlocksClaim(x, y, r, def) || OffBank(x, y, r, def));
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
                maxRadius, int.MaxValue, isExcluded: (x, y, r) => IsBanned(typeName, x, y) || IsReservedForOther(typeName, x, y, r, def) || BlocksClaim(x, y, r, def) || OffBank(x, y, r, def));
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
                    // nothing can be queued, instead of a site that waits forever on missing stone. One pending
                    // storehouse at a time: re-adding every cooldown while the first never connects (seed 2's
                    // woodcutter drew 52) only burns labour and clogs the same unreachable ground.
                    BuildingTypeId? storehouse = CheapestQueueableStorehouse();
                    if (storehouse is null)
                    {
                        _notes.Add($"{advice.Kind} persists for #{building.Id.Value}: no affordable storehouse, skipped.");
                        continue;
                    }

                    if (HasIncomplete(storehouse.Value))
                    {
                        _notes.Add($"{advice.Kind} persists for #{building.Id.Value}: a {_content.Buildings.Names[storehouse.Value.Value]} is already being built, waiting.");
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
                isExcluded: (x, y, r) => IsBanned(typeName, x, y) || IsReservedForOther(typeName, x, y, r, def) || BlocksClaim(x, y, r, def) || OffBank(x, y, r, def));
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
            _reactive.Add(new BotPendingPlacement(type, typeName, candidate.X, candidate.Y, candidate.Rotation, place, _simulation.CurrentTick));
        }

        // Era I: a real player plans the orchard belt before the houses sprawl, so the bot reserves it. Orcharding
        // unlocks late, after the settlement has covered every nearby fertile block, and without a reserve the
        // orchard (and cider comfort) never happens. Two apple-fertile 4x4 blocks near the hall are kept free of
        // buildings until the orchard lands; roads may still cross them (the orchard needs a road beside it).
        // The mechanism is generic (reserved rects kept out of every other placement); only the orchard uses it
        // in Era I (grain farms land early on free ground, and rotation farms upgrade in place).

        private bool UpdateRepair(BotPendingPlacement pending)
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
    }
}
