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

/// Placement bookkeeping: anchors, bans, claims, cooldowns and command sending.
public static partial class EraOneBot
{
    private sealed partial class Runner
    {

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
    }
}
