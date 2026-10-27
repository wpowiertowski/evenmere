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

/// Island topology: the hall bank, severance checks and the road/bridge link search.
public static partial class EraOneBot
{
    private sealed partial class Runner
    {

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
            // Only buildings the road network reaches today count: on river-laced ground the flood
            // reads every roadless site as stranded, so protecting those (seed 2's woodcutter, the coverage
            // repairs waiting on a link) bans the very spots that would connect them. A building without
            // road access cannot be stranded further; the bank check and the link search still constrain
            // where the bot settles. Deterministic: fixed query order for one seed and tick.
            bool[] before = ReachFromHall(null);
            bool[] after = ReachFromHall((x, y, width, height));
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (RingReachable(building, before) && !RingReachable(building, after)
                    && _simulation.Query.Roads.HasRoadAccess(building.Id))
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

        // Whether a footprint lies off the hall's bank while bridges are closed: the whole early settlement
        // must stay on ground a dirt road can reach from the hall (seed 2's first huts sat roadless all game).
        // Checked in every building search, so off-bank spots are never proposed until bridge spans exist.
        private bool OffBank(int x, int y, Rotation rotation, BuildingDef def)
        {
            if (BridgesOpen())
            {
                return false;
            }

            int width = rotation.SwapsAxes() ? def.Footprint.Height : def.Footprint.Width;
            int height = rotation.SwapsAxes() ? def.Footprint.Width : def.Footprint.Height;
            int mapWidth = _simulation.Query.Map.Width;
            for (int ty = y; ty < y + height; ty++)
            {
                for (int tx = x; tx < x + width; tx++)
                {
                    if (!_bank[ty * mapWidth + tx])
                    {
                        return true;
                    }
                }
            }

            return false;
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
    }
}
