using System.Text.Json;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.StarterMap;

/// <summary>
/// The acceptance tests for M1-03: the hand-authored starter island guarantees its comfortable start.
/// Every test loads the map through the real loader against the real content and counts through the real
/// simulation queries, so a broken guarantee fails here with the count attached.
/// </summary>
public sealed class StarterMapTests
{
    private static readonly StarterWorld _world = StarterWorld.Load();

    private static TerrainId Terrain(string id) => _world.Content.IdOf<TerrainId>(id);

    private static bool IsOpenWater(TileInfo tile) =>
        tile.Terrain == Terrain("sea") || tile.Terrain == Terrain("deep_water") || tile.Terrain == Terrain("reef");

    private static int CountTiles(Func<int, int, TileInfo, bool> fits, int maxDistance = 24)
    {
        int count = 0;
        for (int y = 0; y < _world.Map.Height; y++)
        {
            for (int x = 0; x < _world.Map.Width; x++)
            {
                if (_world.DistanceToHall(x, y) <= maxDistance && fits(x, y, _world.Queries.GetTile(x, y)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    [Fact]
    public void Load_StarterMap_HasZeroDiagnostics()
    {
        // The shared world already loaded it with GetMapOrThrow, which throws on any diagnostic.
        _world.Map.Id.ShouldBe("starter");
        _world.Map.Width.ShouldBe(128);
        _world.Map.Height.ShouldBe(128);
    }

    [Fact]
    public void Map_LiesInOpenSea()
    {
        // Every edge tile of the 128 x 128 map is open water: the island never touches the map border.
        for (int x = 0; x < _world.Map.Width; x++)
        {
            IsOpenWater(_world.Queries.GetTile(x, 0)).ShouldBeTrue($"northern edge tile ({x}, 0)");
            IsOpenWater(_world.Queries.GetTile(x, _world.Map.Height - 1)).ShouldBeTrue($"southern edge tile ({x}, 127)");
        }

        for (int y = 0; y < _world.Map.Height; y++)
        {
            IsOpenWater(_world.Queries.GetTile(0, y)).ShouldBeTrue($"western edge tile (0, {y})");
            IsOpenWater(_world.Queries.GetTile(_world.Map.Width - 1, y)).ShouldBeTrue($"eastern edge tile (127, {y})");
        }
    }

    [Fact]
    public void BorderRing_IsOnlySeaAndDeepWater()
    {
        // The outer two-tile ring holds no land, reef or river: rivers end at coastal mouths inland.
        for (int y = 0; y < _world.Map.Height; y++)
        {
            for (int x = 0; x < _world.Map.Width; x++)
            {
                if (x >= 2 && x < _world.Map.Width - 2 && y >= 2 && y < _world.Map.Height - 2)
                {
                    continue;
                }

                TileInfo tile = _world.Queries.GetTile(x, y);
                bool water = tile.Terrain == Terrain("sea") || tile.Terrain == Terrain("deep_water");
                water.ShouldBeTrue($"border tile ({x}, {y}) is {tile.Terrain}");
            }
        }
    }

    [Fact]
    public void Rivers_StandOnLand()
    {
        // Every river tile stands on the island: at least one 4-neighbour is land or river.
        for (int y = 0; y < _world.Map.Height; y++)
        {
            for (int x = 0; x < _world.Map.Width; x++)
            {
                if (!_world.Queries.GetTile(x, y).Has(TileFeatures.River))
                {
                    continue;
                }

                bool grounded = false;
                foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx < 0 || nx >= _world.Map.Width || ny < 0 || ny >= _world.Map.Height)
                    {
                        continue;
                    }

                    TileInfo neighbour = _world.Queries.GetTile(nx, ny);
                    grounded |= neighbour.Has(TileFeatures.River) || !IsOpenWater(neighbour);
                }

                grounded.ShouldBeTrue($"river tile ({x}, {y}) sits in open water");
            }
        }

    }

    [Fact]
    public void Rivers_FormTwoSeparateSourceToSeaComponents()
    {
        // The rivers are two 4-connected components that never touch: a narrow one and a wide one.
        // Each rises at the highland core (its farthest tile from the sea stands by high ground)
        // and each touches the sea: the wide river west, the narrow river south.
        List<HashSet<TileCoord>> components = RiverComponents();
        components.Count.ShouldBe(2, $"found {components.Count} river components, not two");

        foreach (HashSet<TileCoord> component in components)
        {
            component.Select(tile => _world.Queries.GetTile(tile.X, tile.Y).RiverWidth).Distinct().Count()
                .ShouldBe(1, "a river mixes narrow and wide width classes");
        }

        HashSet<TileCoord> narrow = components.Single(component =>
            _world.Queries.GetTile(component.First().X, component.First().Y).RiverWidth == RiverWidthClass.Narrow);
        HashSet<TileCoord> wide = components.Single(component =>
            _world.Queries.GetTile(component.First().X, component.First().Y).RiverWidth == RiverWidthClass.Wide);

        MouthsOf(wide).ShouldNotBeEmpty("the wide river never touches the sea");
        MouthsOf(narrow).ShouldNotBeEmpty("the narrow river never touches the sea");
        MouthsOf(wide).Any(mouth => mouth.X <= 20).ShouldBeTrue("the wide river mouth is not on the west coast");
        MouthsOf(narrow).Any(mouth => mouth.Y >= 100).ShouldBeTrue("the narrow river mouth is not on the south coast");

        IsHighlandNear(SourceOf(wide)).ShouldBeTrue("the wide river does not rise at the highlands");
        IsHighlandNear(SourceOf(narrow)).ShouldBeTrue("the narrow river does not rise at the highlands");
    }

    [Fact]
    public void Rivers_RunDownhillToTheirMouths()
    {
        // From every river tile, water reaches the mouth without climbing: each tile above a mouth has
        // a 4-neighbour river tile one step closer whose elevation is at most its own.
        foreach (HashSet<TileCoord> component in RiverComponents())
        {
            Dictionary<TileCoord, int> distance = DistancesToSea(component);
            foreach (TileCoord tile in component)
            {
                int d = distance[tile];
                if (d == 0)
                {
                    continue;
                }

                int elevation = _world.Queries.GetTile(tile.X, tile.Y).Elevation;
                bool falls = false;
                foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var neighbour = new TileCoord(tile.X + dx, tile.Y + dy);
                    if (distance.TryGetValue(neighbour, out int closer) && closer == d - 1
                        && _world.Queries.GetTile(neighbour.X, neighbour.Y).Elevation <= elevation)
                    {
                        falls = true;
                        break;
                    }
                }

                falls.ShouldBeTrue($"river tile {tile} cannot run downhill to the sea");
            }
        }
    }

    [Fact]
    public void StartSite_StandsOnBuildablePlainsNearTheSouthWestCoast()
    {
        MapStartSite site = _world.Map.StartSite;
        site.Rotation.ShouldBe(0);
        _world.HallWidth.ShouldBe(4);
        _world.HallHeight.ShouldBe(4);

        // South-west quadrant of the map.
        site.X.ShouldBeLessThan(64, "the hall stands near the west coast");
        site.Y.ShouldBeGreaterThanOrEqualTo(64, "the hall stands near the south coast");

        TerrainId plains = Terrain("plains");
        for (int dy = 0; dy < _world.HallHeight; dy++)
        {
            for (int dx = 0; dx < _world.HallWidth; dx++)
            {
                TileInfo tile = _world.Queries.GetTile(site.X + dx, site.Y + dy);
                tile.Terrain.ShouldBe(plains, $"hall tile ({site.X + dx}, {site.Y + dy})");
                _world.Content.Get(tile.Terrain).Buildable.ShouldBeTrue($"hall tile ({site.X + dx}, {site.Y + dy})");
            }
        }

        // Open water within a day's walk: the fishing huts this start needs are close.
        int nearSea = CountTiles((_, _, tile) => IsOpenWater(tile));
        nearSea.ShouldBeGreaterThan(0, "no open water within 24 tiles of the hall");
    }

    [Fact]
    public void StartRegion_HasAForestBelt()
    {
        TerrainId forest = Terrain("forest");
        int count = CountTiles((_, _, tile) => tile.Terrain == forest);
        count.ShouldBeGreaterThanOrEqualTo(80, $"only {count} forest tiles within 24 of the hall");
    }

    [Fact]
    public void StartRegion_HasThreeGrainFertile5x5Placements()
    {
        TerrainId plains = Terrain("plains");
        List<(int X, int Y)> found = _world.GreedyPlacements(
            5,
            (x, y) =>
            {
                TileInfo tile = _world.Queries.GetTile(x, y);
                return tile.Terrain == plains && tile.IsFertileFor(CropKind.Grain);
            });
        found.Count.ShouldBeGreaterThanOrEqualTo(
            3,
            $"only {found.Count} non-overlapping 5x5 fully-plains fully-grain-fertile placements within 24 of the hall");
    }

    [Fact]
    public void StartRegion_HasFourPlains4x4Placements()
    {
        TerrainId plains = Terrain("plains");
        List<(int X, int Y)> found = _world.GreedyPlacements(
            4,
            (x, y) => _world.Queries.GetTile(x, y).Terrain == plains);
        found.Count.ShouldBeGreaterThanOrEqualTo(
            4,
            $"only {found.Count} non-overlapping 4x4 fully-plains placements within 24 of the hall");
    }

    [Fact]
    public void StartRegion_HasFortyCoastlineTiles()
    {
        int count = CountTiles((_, _, tile) => tile.Has(TileFeatures.Coast));
        count.ShouldBeGreaterThanOrEqualTo(40, $"only {count} coast tiles within 24 of the hall");
    }

    [Fact]
    public void StartRegion_HasStoneOnHills()
    {
        TerrainId hills = Terrain("hills");
        int count = CountTiles(
            (_, _, tile) => tile.Terrain == hills && tile.Deposit == DepositKind.Stone);
        count.ShouldBeGreaterThanOrEqualTo(6, $"only {count} stone-on-hills tiles within 24 of the hall");
    }

    [Fact]
    public void StartRegion_HasClayOnTheRiverbanks()
    {
        int count = CountTiles((x, y, tile) =>
            tile.Deposit == DepositKind.Clay && HasRiverNeighbour(x, y));
        count.ShouldBeGreaterThanOrEqualTo(6, $"only {count} clay tiles beside a river within 24 of the hall");
    }

    /// <summary>Gets a hill beyond the wide river: open pasture north of its middle course.</summary>
    private static TileCoord NorthPasture => new(25, 68);

    /// <summary>Gets a hill beyond the wide river: the westford slope below the highmoor.</summary>
    private static TileCoord WestfordSlope => new(39, 60);

    /// <summary>Gets a hill beyond the narrow river: the eastvale rise above the clay banks.</summary>
    private static TileCoord EastvaleRise => new(60, 80);

    /// <summary>Gets a hill beyond the narrow river: the first ironhill knoll.</summary>
    private static TileCoord IronhillKnoll => new(80, 80);

    [Fact]
    public void Rivers_EncloseTheLanding()
    {
        // Highlands and peaks always block, alongside open water. With rivers blocking too, the hall
        // reaches nothing beyond either river: neither the named hills nor any tile of the far zones.
        // Both the four- and the eight-directional walk must fail: no gap is sealed by a single tile,
        // not even diagonally.
        foreach (bool eightDir in new[] { false, true })
        {
            string walk = eightDir ? "eight-directional" : "four-directional";
            bool[,] reached = FloodFromHall(blockRivers: true, eightDir: eightDir);
            foreach (TileCoord target in new[] { NorthPasture, WestfordSlope, EastvaleRise, IronhillKnoll })
            {
                reached[target.X, target.Y].ShouldBeFalse($"the hall reaches {target} past the rivers ({walk})");
            }

            for (int y = 0; y <= 70; y++)
            {
                for (int x = 0; x <= 50; x++)
                {
                    reached[x, y].ShouldBeFalse($"the hall reaches ({x}, {y}) beyond the wide river ({walk})");
                }
            }

            for (int y = 78; y < _world.Map.Height; y++)
            {
                for (int x = 60; x < _world.Map.Width; x++)
                {
                    reached[x, y].ShouldBeFalse($"the hall reaches ({x}, {y}) beyond the narrow river ({walk})");
                }
            }

            reached[66, 40].ShouldBeFalse($"the hall reaches the highland interior past the rivers ({walk})");
        }
    }

    [Fact]
    public void Rivers_FordedRiversReachTheHillsBeyond()
    {
        // With rivers fordable (highlands and peaks still blocking), the hills beyond each river open up.
        bool[,] reached = FloodFromHall(blockRivers: false);
        foreach (TileCoord target in new[] { NorthPasture, WestfordSlope, EastvaleRise, IronhillKnoll })
        {
            _world.Queries.GetTile(target.X, target.Y).Terrain.ShouldBe(
                Terrain("hills"), $"target {target} is not a hill");
            reached[target.X, target.Y].ShouldBeTrue($"the hall cannot ford the rivers to {target}");
        }
    }

    [Fact]
    public void Rivers_WideRiverIsWideAndNarrowRiverIsNarrow()
    {
        // The wide river needs Stone Bridges somewhere: one column holds 4 or more wide-class river tiles.
        int widestWideColumn = 0;
        for (int x = 0; x < _world.Map.Width; x++)
        {
            int run = 0;
            for (int y = 0; y < _world.Map.Height; y++)
            {
                TileInfo tile = _world.Queries.GetTile(x, y);
                run = tile.RiverWidth == RiverWidthClass.Wide ? run + 1 : 0;
                widestWideColumn = Math.Max(widestWideColumn, run);
            }
        }

        widestWideColumn.ShouldBeGreaterThanOrEqualTo(4, "no 4-tile-wide stretch of wide river");

        // The narrow river stays fordable by the first bridge: no row holds 4 narrow-class river tiles in a row.
        for (int y = 0; y < _world.Map.Height; y++)
        {
            int run = 0;
            for (int x = 0; x < _world.Map.Width; x++)
            {
                run = _world.Queries.GetTile(x, y).RiverWidth == RiverWidthClass.Narrow ? run + 1 : 0;
                run.ShouldBeLessThanOrEqualTo(3, $"the narrow river is 4 wide in row {y}");
            }
        }
    }

    [Fact]
    public void Rivers_HaveBridgingWidths()
    {
        // The crossing width of a river tile is the smaller of its vertical and horizontal runs of
        // same-class river tiles, which handles bends. The wide river stays 4-8 across everywhere so
        // only Stone Bridges span it; the narrow river stays 1-3 so Bridge-building spans it.
        foreach (HashSet<TileCoord> component in RiverComponents())
        {
            RiverWidthClass width = _world.Queries.GetTile(component.First().X, component.First().Y).RiverWidth;
            int min = int.MaxValue;
            int max = int.MinValue;
            foreach (TileCoord tile in component)
            {
                int crossing = Math.Min(RunLength(tile, 0, 1), RunLength(tile, 1, 0));
                min = Math.Min(min, crossing);
                max = Math.Max(max, crossing);
                if (width == RiverWidthClass.Wide)
                {
                    crossing.ShouldBeGreaterThanOrEqualTo(4, $"wide river tile {tile} is narrower than 4");
                    crossing.ShouldBeLessThanOrEqualTo(8, $"wide river tile {tile} is wider than 8");
                }
                else
                {
                    crossing.ShouldBeGreaterThanOrEqualTo(1, $"narrow river tile {tile} has no width");
                    crossing.ShouldBeLessThanOrEqualTo(3, $"narrow river tile {tile} is wider than 3");
                }
            }
        }
    }

    [Fact]
    public void BeyondTheRivers_LaterErasHaveTheirGround()
    {
        // Far from the hall (over 30 tiles away): marsh, iron hills, the highland interior, peaks and an offshore reef.
        int marsh = CountTiles((_, _, tile) => tile.Has(TileFeatures.Marsh), maxDistance: int.MaxValue);
        int farMarsh = CountFarTiles((_, _, tile) => tile.Has(TileFeatures.Marsh));
        farMarsh.ShouldBeGreaterThanOrEqualTo(30, $"only {farMarsh} marsh tiles beyond the rivers ({marsh} in all)");

        TerrainId hills = Terrain("hills");
        TerrainId highlands = Terrain("highlands");
        TerrainId peaks = Terrain("peaks");
        int farIron = CountFarTiles((_, _, tile) => tile.Terrain == hills && tile.Deposit == DepositKind.Iron);
        farIron.ShouldBeGreaterThanOrEqualTo(4, $"only {farIron} iron-on-hills tiles beyond the rivers");

        int farCoal = CountFarTiles(
            (_, _, tile) => tile.Terrain == highlands && tile.Deposit == DepositKind.Coal);
        farCoal.ShouldBeGreaterThanOrEqualTo(4, $"only {farCoal} coal tiles beyond the rivers");

        int farCopper = CountFarTiles(
            (_, _, tile) => tile.Terrain == highlands && tile.Deposit == DepositKind.Copper);
        farCopper.ShouldBeGreaterThanOrEqualTo(4, $"only {farCopper} copper tiles beyond the rivers");

        int farPeaks = CountFarTiles((_, _, tile) => tile.Terrain == peaks);
        farPeaks.ShouldBeGreaterThanOrEqualTo(10, $"only {farPeaks} peak tiles beyond the rivers");

        TerrainId reef = Terrain("reef");
        int reefCount = 0;
        for (int y = 0; y < _world.Map.Height; y++)
        {
            for (int x = 0; x < _world.Map.Width; x++)
            {
                if (_world.Queries.GetTile(x, y).Terrain == reef)
                {
                    reefCount++;
                }
            }
        }

        reefCount.ShouldBeGreaterThanOrEqualTo(10, $"only {reefCount} reef tiles offshore");
    }

    [Fact]
    public void Regions_AreEightToTwelveNamedStarterRegions()
    {
        _world.Map.RegionNames.Length.ShouldBeGreaterThanOrEqualTo(8, "fewer than 8 regions");
        _world.Map.RegionNames.Length.ShouldBeLessThanOrEqualTo(12, "more than 12 regions");

        foreach (MapRegionName region in _world.Map.RegionNames)
        {
            region.LocaleKey.StartsWith("region.starter.", StringComparison.Ordinal)
                .ShouldBeTrue($"region {region.Region} is not a region.starter key");
        }

        // Every tile belongs to a named region.
        for (int y = 0; y < _world.Map.Height; y++)
        {
            for (int x = 0; x < _world.Map.Width; x++)
            {
                _world.Queries.GetTile(x, y).Region.ShouldBeLessThan((ushort)_world.Map.RegionNames.Length);
            }
        }

        // Every region name has display text.
        string localePath = Path.Combine(StarterWorld.ContentRoot, "locale", "en", "maps-starter.json");
        File.Exists(localePath).ShouldBeTrue("content/locale/en/maps-starter.json is missing");
        using var locale = JsonDocument.Parse(File.ReadAllText(localePath));
        foreach (MapRegionName region in _world.Map.RegionNames)
        {
            locale.RootElement.TryGetProperty(region.LocaleKey + ".name", out JsonElement name)
                .ShouldBeTrue($"locale key {region.LocaleKey}.name is missing");
            name.GetString().ShouldNotBeNullOrWhiteSpace($"locale key {region.LocaleKey}.name is empty");
        }
    }

    [Fact]
    public void StartStock_Is40Timber20FishAnd10Wood()
    {
        StockOf("timber").ShouldBe(Amount.FromUnits(40));
        StockOf("fish").ShouldBe(Amount.FromUnits(20));
        StockOf("wood").ShouldBe(Amount.FromUnits(10));
    }

    [Fact]
    public void Readme_HasADownscaledPreviewAndALegend()
    {
        string path = Path.Combine(StarterWorld.ContentRoot, "maps", "starter", "README.md");
        File.Exists(path).ShouldBeTrue("content/maps/starter/README.md is missing");
        string text = File.ReadAllText(path);
        text.Contains("Legend").ShouldBeTrue("the preview needs a legend");
        text.Contains("64").ShouldBeTrue("the preview should state its downscaled size");
    }

    [Fact]
    public void Composition_LeavesRoomToGrowBeyondTheLanding()
    {
        // Over all land tiles (everything except sea, deep water and reef): a compact highland
        // core for Era III, and plenty of plains, hills, forest and marsh for Eras I-II.
        TerrainId plains = Terrain("plains");
        TerrainId hills = Terrain("hills");
        TerrainId highlands = Terrain("highlands");
        TerrainId peaks = Terrain("peaks");
        TerrainId forest = Terrain("forest");
        int land = 0;
        int high = 0;
        int plainsCount = 0;
        int hillsCount = 0;
        int forestCount = 0;
        int marshCount = 0;
        for (int y = 0; y < _world.Map.Height; y++)
        {
            for (int x = 0; x < _world.Map.Width; x++)
            {
                TileInfo tile = _world.Queries.GetTile(x, y);
                if (IsOpenWater(tile))
                {
                    continue;
                }

                land++;
                high += tile.Terrain == highlands || tile.Terrain == peaks ? 1 : 0;
                plainsCount += tile.Terrain == plains ? 1 : 0;
                hillsCount += tile.Terrain == hills ? 1 : 0;
                forestCount += tile.Terrain == forest ? 1 : 0;
                marshCount += tile.Has(TileFeatures.Marsh) ? 1 : 0;
            }
        }

        double percent = (double)high / land;
        percent.ShouldBeLessThanOrEqualTo(0.25, $"highlands plus peaks cover {percent:P1} of the land");
        percent = (double)plainsCount / land;
        percent.ShouldBeGreaterThanOrEqualTo(0.25, $"plains cover only {percent:P1} of the land");
        percent = (double)hillsCount / land;
        percent.ShouldBeGreaterThanOrEqualTo(0.10, $"hills cover only {percent:P1} of the land");
        percent = (double)forestCount / land;
        percent.ShouldBeGreaterThanOrEqualTo(0.10, $"forest covers only {percent:P1} of the land");
        percent = (double)marshCount / land;
        percent.ShouldBeGreaterThanOrEqualTo(0.03, $"marsh covers only {percent:P1} of the land");
    }

    private static int CountFarTiles(Func<int, int, TileInfo, bool> fits)
    {
        int count = 0;
        for (int y = 0; y < _world.Map.Height; y++)
        {
            for (int x = 0; x < _world.Map.Width; x++)
            {
                if (_world.DistanceToHall(x, y) > 30 && fits(x, y, _world.Queries.GetTile(x, y)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool HasRiverNeighbour(int x, int y)
    {
        foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int nx = x + dx;
            int ny = y + dy;
            if (nx >= 0 && nx < _world.Map.Width && ny >= 0 && ny < _world.Map.Height
                && _world.Queries.GetTile(nx, ny).Has(TileFeatures.River))
            {
                return true;
            }
        }

        return false;
    }

    private static bool[,] FloodFromHall(bool blockRivers, bool eightDir = false)
    {
        // Four- or eight-directional walk from the hall tile: open water, highlands and peaks always
        // block, rivers block only when asked.
        int width = _world.Map.Width;
        int height = _world.Map.Height;
        bool[,] seen = new bool[width, height];
        (int Dx, int Dy)[] steps = eightDir
            ? [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)]
            : [(1, 0), (-1, 0), (0, 1), (0, -1)];
        var queue = new Queue<TileCoord>();
        TileCoord start = new(_world.Map.StartSite.X, _world.Map.StartSite.Y - 1);
        queue.Enqueue(start);
        seen[start.X, start.Y] = true;
        while (queue.Count > 0)
        {
            TileCoord at = queue.Dequeue();
            foreach ((int dx, int dy) in steps)
            {
                int nx = at.X + dx;
                int ny = at.Y + dy;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height || seen[nx, ny])
                {
                    continue;
                }

                TileInfo tile = _world.Queries.GetTile(nx, ny);
                if (IsOpenWater(tile) || IsHigh(tile))
                {
                    continue;
                }

                if (blockRivers && tile.Has(TileFeatures.River))
                {
                    continue;
                }

                seen[nx, ny] = true;
                queue.Enqueue(new TileCoord(nx, ny));
            }
        }

        return seen;
    }

    private static bool IsHigh(TileInfo tile) =>
        tile.Terrain == Terrain("highlands") || tile.Terrain == Terrain("peaks");

    private static int RunLength(TileCoord tile, int dx, int dy)
    {
        // Consecutive same-class river tiles through a tile along an axis, counting both directions.
        RiverWidthClass width = _world.Queries.GetTile(tile.X, tile.Y).RiverWidth;
        int run = 1;
        for (int sign = -1; sign <= 1; sign += 2)
        {
            int nx = tile.X + (dx * sign);
            int ny = tile.Y + (dy * sign);
            while (nx >= 0 && nx < _world.Map.Width && ny >= 0 && ny < _world.Map.Height
                && _world.Queries.GetTile(nx, ny).RiverWidth == width
                && _world.Queries.GetTile(nx, ny).Has(TileFeatures.River))
            {
                run++;
                nx += dx * sign;
                ny += dy * sign;
            }
        }

        return run;
    }

    private static List<HashSet<TileCoord>> RiverComponents()
    {
        // The 4-connected components of river tiles: two separate rivers that never touch.
        var components = new List<HashSet<TileCoord>>();
        bool[,] seen = new bool[_world.Map.Width, _world.Map.Height];
        for (int y = 0; y < _world.Map.Height; y++)
        {
            for (int x = 0; x < _world.Map.Width; x++)
            {
                if (seen[x, y] || !_world.Queries.GetTile(x, y).Has(TileFeatures.River))
                {
                    continue;
                }

                var component = new HashSet<TileCoord>();
                var queue = new Queue<TileCoord>();
                queue.Enqueue(new TileCoord(x, y));
                seen[x, y] = true;
                while (queue.Count > 0)
                {
                    TileCoord at = queue.Dequeue();
                    component.Add(at);
                    foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = at.X + dx;
                        int ny = at.Y + dy;
                        if (nx < 0 || nx >= _world.Map.Width || ny < 0 || ny >= _world.Map.Height
                            || seen[nx, ny] || !_world.Queries.GetTile(nx, ny).Has(TileFeatures.River))
                        {
                            continue;
                        }

                        seen[nx, ny] = true;
                        queue.Enqueue(new TileCoord(nx, ny));
                    }
                }

                components.Add(component);
            }
        }

        return components;
    }

    private static List<TileCoord> MouthsOf(HashSet<TileCoord> component)
    {
        // The river tiles beside open water: where the river ends in the sea.
        var mouths = new List<TileCoord>();
        foreach (TileCoord tile in component)
        {
            foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = tile.X + dx;
                int ny = tile.Y + dy;
                if (nx >= 0 && nx < _world.Map.Width && ny >= 0 && ny < _world.Map.Height
                    && IsOpenWater(_world.Queries.GetTile(nx, ny)))
                {
                    mouths.Add(tile);
                    break;
                }
            }
        }

        return mouths;
    }

    private static Dictionary<TileCoord, int> DistancesToSea(HashSet<TileCoord> component)
    {
        // Breadth-first distances over the component from its mouths.
        var distance = new Dictionary<TileCoord, int>();
        var queue = new Queue<TileCoord>();
        foreach (TileCoord mouth in MouthsOf(component))
        {
            distance[mouth] = 0;
            queue.Enqueue(mouth);
        }

        while (queue.Count > 0)
        {
            TileCoord at = queue.Dequeue();
            foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var neighbour = new TileCoord(at.X + dx, at.Y + dy);
                if (component.Contains(neighbour) && !distance.ContainsKey(neighbour))
                {
                    distance[neighbour] = distance[at] + 1;
                    queue.Enqueue(neighbour);
                }
            }
        }

        return distance;
    }

    private static TileCoord SourceOf(HashSet<TileCoord> component)
    {
        // The river's source: its tile farthest from the sea.
        Dictionary<TileCoord, int> distance = DistancesToSea(component);
        TileCoord source = component.First();
        foreach (TileCoord tile in component)
        {
            if (distance[tile] > distance[source])
            {
                source = tile;
            }
        }

        return source;
    }

    private static bool IsHighlandNear(TileCoord tile)
    {
        // Whether a highlands or peaks tile stands 8-adjacent: the river rises at high ground.
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = tile.X + dx;
                int ny = tile.Y + dy;
                if (nx >= 0 && nx < _world.Map.Width && ny >= 0 && ny < _world.Map.Height
                    && IsHigh(_world.Queries.GetTile(nx, ny)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Amount StockOf(string good)
    {
        GoodId id = _world.Content.IdOf<GoodId>(good);
        return _world.Map.StartStock.Single(s => s.Good == id).Amount;
    }
}
