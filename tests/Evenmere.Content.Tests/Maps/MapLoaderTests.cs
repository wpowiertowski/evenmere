using System.Text;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Content.Tests.Support;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Content.Tests.Maps;

public sealed class MapLoaderTests
{
    private static int At(MapDef map, int x, int y) => (y * map.Width) + x;

    private static TerrainId Terrain(string id) => MapFixtures.Content.IdOf<TerrainId>(id);

    private static ContentDiagnostic Single(MapLoadResult result, string code, string file) =>
        result.Diagnostics.Where(d => d.Code == code && d.File == file).ToArray() is [var only]
            ? only
            : throw new ShouldAssertException($"Expected one {code} in {file}.\n{DiagnosticAssertions.Dump(result.Diagnostics)}");

    // ---- the fixture maps

    [Fact]
    public void Load_SixteenFixture_ReadsEveryLayer()
    {
        MapDef map = MapFixtures.LoadClean("test_16");

        map.Id.ShouldBe("test_16");
        map.Width.ShouldBe(16);
        map.Height.ShouldBe(16);
        map.TileCount.ShouldBe(256);
        map.Terrain[At(map, 0, 15)].ShouldBe(Terrain("sea"));
        map.Terrain[At(map, 0, 0)].ShouldBe(Terrain("deep_water"));
        map.Terrain[At(map, 15, 14)].ShouldBe(Terrain("reef"));
        map.Terrain[At(map, 1, 5)].ShouldBe(Terrain("beach"));
        map.Terrain[At(map, 4, 4)].ShouldBe(Terrain("forest"));
        map.Terrain[At(map, 10, 4)].ShouldBe(Terrain("hills"));
        map.Terrain[At(map, 3, 10)].ShouldBe(Terrain("marsh"));
        map.Terrain[At(map, 5, 10)].ShouldBe(Terrain("fen"));
        map.Terrain[At(map, 11, 7)].ShouldBe(Terrain("highlands"));
        map.Terrain[At(map, 12, 8)].ShouldBe(Terrain("peaks"));
        map.Terrain[At(map, 7, 5)].ShouldBe(Terrain("river"));
        map.Terrain[At(map, 8, 5)].ShouldBe(Terrain("plains"));
    }

    [Fact]
    public void Load_SixteenFixture_ReadsElevationRiversRegionsDepositsAndFertility()
    {
        MapDef map = MapFixtures.LoadClean("test_16");

        map.Elevation[At(map, 12, 8)].ShouldBe((byte)15);
        map.Elevation[At(map, 9, 3)].ShouldBe((byte)3);
        map.Elevation[At(map, 1, 5)].ShouldBe((byte)1);
        map.RiverWidth[At(map, 7, 5)].ShouldBe((byte)1);
        map.RiverWidth[At(map, 7, 10)].ShouldBe((byte)2);
        map.RiverWidth[At(map, 8, 5)].ShouldBe((byte)0);
        map.Region[At(map, 2, 2)].ShouldBe((ushort)0);
        map.Region[At(map, 9, 2)].ShouldBe((ushort)1);
        map.Region[At(map, 11, 7)].ShouldBe((ushort)10);
        map.Deposit[At(map, 9, 3)].ShouldBe((byte)((int)DepositKind.Stone + 1));
        map.Deposit[At(map, 6, 5)].ShouldBe((byte)((int)DepositKind.Clay + 1));
        map.Deposit[At(map, 10, 4)].ShouldBe((byte)((int)DepositKind.Iron + 1));
        map.Deposit[At(map, 11, 7)].ShouldBe((byte)((int)DepositKind.Coal + 1));
        map.Deposit[At(map, 12, 7)].ShouldBe((byte)((int)DepositKind.Copper + 1));
        map.Deposit[At(map, 2, 2)].ShouldBe((byte)0);
        map.Fertility[At(map, 10, 10)].ShouldBe(1u << (int)CropKind.Grain);
        map.Fertility[At(map, 3, 7)].ShouldBe(1u << (int)CropKind.Flax);
        map.Fertility[At(map, 2, 2)].ShouldBe(0u);
    }

    [Fact]
    public void Load_SixteenFixture_ReadsStartSiteStockAndRegionNames()
    {
        MapDef map = MapFixtures.LoadClean("test_16");

        map.StartSite.ShouldBe(new MapStartSite(9, 9, 0));
        GoodId timber = MapFixtures.Content.IdOf<GoodId>("timber");
        GoodId wood = MapFixtures.Content.IdOf<GoodId>("wood");
        map.StartStock.ShouldBe(
            new[] { new GoodAmount(timber, Amount.Parse("10.5")), new GoodAmount(wood, Amount.FromUnits(20)) }.OrderBy(g => g.Good.Value));
        map.RegionNames.ShouldBe([new MapRegionName(0, "region.test16.west"), new MapRegionName(1, "region.test16.east"), new MapRegionName(10, "region.test16.heights")]);
    }

    [Fact]
    public void Load_ThirtyTwoFixture_LoadsCleanlyWithARotatedStartSite()
    {
        MapDef map = MapFixtures.LoadClean("test_32");

        map.Width.ShouldBe(32);
        map.Height.ShouldBe(32);
        map.StartSite.ShouldBe(new MapStartSite(12, 12, 1));
        map.RiverWidth[At(map, 16, 10)].ShouldBe((byte)1);
    }

    [Fact]
    public void Load_Fixture_KnowsWhatEveryTerrainMeans()
    {
        MapDef map = MapFixtures.LoadClean("test_16");

        map.TerrainFeatures.Length.ShouldBe(MapFixtures.Content.Terrains.Count);
        map.TerrainFeatures[Terrain("sea").Value].ShouldBe(MapTerrainFeatures.Water);
        map.TerrainFeatures[Terrain("deep_water").Value].ShouldBe(MapTerrainFeatures.Water | MapTerrainFeatures.DeepWater);
        map.TerrainFeatures[Terrain("reef").Value].ShouldBe(MapTerrainFeatures.Water | MapTerrainFeatures.Reef);
        map.TerrainFeatures[Terrain("river").Value].ShouldBe(MapTerrainFeatures.River);
        map.TerrainFeatures[Terrain("beach").Value].ShouldBe(MapTerrainFeatures.Beach);
        map.TerrainFeatures[Terrain("marsh").Value].ShouldBe(MapTerrainFeatures.Marsh);
        map.TerrainFeatures[Terrain("plains").Value].ShouldBe(MapTerrainFeatures.None);
    }

    [Fact]
    public void DefaultLegend_RealContent_CoversEveryTerrainExactlyOnce()
    {
        ContentDb real = ContentLoader.Load(Path.Combine(TestPaths.RepoRoot, "content")).GetDbOrThrow();

        string[] legendTerrains = [.. MapTerrainRules.DefaultLegend.Select(e => e.Terrain).Order(StringComparer.Ordinal)];

        legendTerrains.ShouldBe([.. real.Terrains.Names]);
        MapTerrainRules.DefaultLegend.Select(e => e.Symbol).Distinct().Count().ShouldBe(MapTerrainRules.DefaultLegend.Length);
        MapTerrainRules.DefaultLegend.Length.ShouldBe(12);
    }

    [Fact]
    public void DefaultDepositLegend_NamesEveryDepositKind()
    {
        MapTerrainRules.DefaultDepositLegend.Select(e => e.Deposit).Order(StringComparer.Ordinal)
            .ShouldBe([.. ContentNames.NamesOf<DepositKind>().Order(StringComparer.Ordinal)]);
    }

    // ---- malformed fixtures: file, line and column

    [Fact]
    public void Load_ShortRow_ReportsTheLineAndColumn()
    {
        MapLoadResult result = MapLoader.Load(MapFixtures.ContentRoots, "bad_row_length", MapFixtures.Content);

        result.Map.ShouldBeNull();
        ContentDiagnostic problem = Single(result, DiagnosticCodes.Evl001, "maps/bad_row_length/terrain.txt");
        problem.Severity.ShouldBe(DiagnosticSeverity.Error);
        problem.Message.ShouldStartWith("line 5, column 16: ");
        problem.Message.ShouldContain("15 characters long but the map is 16 wide");
    }

    [Fact]
    public void Load_UnknownCharacter_ReportsTheLineAndColumn()
    {
        MapLoadResult result = MapLoader.Load(MapFixtures.ContentRoots, "bad_character", MapFixtures.Content);

        result.Map.ShouldBeNull();
        ContentDiagnostic problem = Single(result, DiagnosticCodes.Evl001, "maps/bad_character/terrain.txt");
        problem.Message.ShouldStartWith("line 3, column 7: 'x' is not valid here");
    }

    [Fact]
    public void Load_MissingRows_ReportsWhereTheFileEnds()
    {
        MapLoadResult result = MapLoader.Load(MapFixtures.ContentRoots, "bad_row_count", MapFixtures.Content);

        ContentDiagnostic problem = Single(result, DiagnosticCodes.Evl001, "maps/bad_row_count/terrain.txt");
        problem.Message.ShouldStartWith("line 15, column 1: the file has 14 rows but the map is 16 tall");
    }

    [Fact]
    public void Load_ErrorDiagnostic_PrintsFileLineAndColumn()
    {
        MapLoadResult result = MapLoader.Load(MapFixtures.ContentRoots, "bad_character", MapFixtures.Content);

        result.Diagnostics[0].ToString().ShouldStartWith("EVL001 error maps/bad_character/terrain.txt: line 3, column 7:");
    }

    // ---- layers

    [Theory]
    [InlineData("elevation.txt", '0', 'G', "0-9 or A-F")]
    [InlineData("regions.txt", '0', 'a', "0-9 or A-Z")]
    [InlineData("deposits.txt", '.', 'q', "deposit legend")]
    [InlineData("fertility-grain.txt", '.', 'x', "fertile")]
    [InlineData("rivers.txt", '.', '3', "narrow")]
    public void Load_BadCharacterInALayer_NamesTheFileLineAndColumn(string file, char fill, char bad, string alphabetHint)
    {
        using var temp = TempMap.Plain();
        temp.Write(file, TempMap.With(TempMap.Rows(8, 8, fill), 5, 4, bad));

        MapLoadResult result = temp.Load();

        result.Map.ShouldBeNull();
        ContentDiagnostic problem = Single(result, DiagnosticCodes.Evl001, $"maps/temp_map/{file}");
        problem.Message.ShouldStartWith($"line 5, column 6: '{bad}' is not valid here");
        problem.Message.ShouldContain(alphabetHint);
    }

    [Fact]
    public void Load_AllOptionalLayersMissing_DefaultsToFlatDryUnassignedGround()
    {
        using var temp = TempMap.Plain();

        MapDef map = temp.Load().GetMapOrThrow();

        map.Elevation.ShouldAllBe(e => e == 0);
        map.Region.ShouldAllBe(r => r == 0);
        map.Deposit.ShouldAllBe(d => d == 0);
        map.Fertility.ShouldAllBe(f => f == 0);
        map.RiverWidth.ShouldAllBe(w => w == 0);
        map.StartStock.ShouldBeEmpty();
        map.RegionNames.ShouldBeEmpty();
    }

    [Fact]
    public void Load_CrlfLineEndings_AreAccepted()
    {
        using var temp = TempMap.Plain();
        temp.Write("terrain.txt", TempMap.Rows(8, 8, '.').Replace("\n", "\r\n"));

        temp.Load().HasErrors.ShouldBeFalse();
    }

    [Fact]
    public void Load_ByteOrderMark_IsAnError()
    {
        using var temp = TempMap.Plain();
        temp.WriteBytes("terrain.txt", [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. Encoding.ASCII.GetBytes(TempMap.Rows(8, 8, '.'))]);

        MapLoadResult result = temp.Load();

        Single(result, DiagnosticCodes.Evl001, "maps/temp_map/terrain.txt").Message.ShouldStartWith("line 1, column 1: the file starts with a UTF-8 byte order mark");
    }

    [Fact]
    public void Load_NonAsciiCharacter_IsReportedByCodePoint()
    {
        using var temp = TempMap.Plain();
        temp.Write("terrain.txt", TempMap.With(TempMap.Rows(8, 8, '.'), 2, 1, 'é'));

        MapLoadResult result = temp.Load();

        Single(result, DiagnosticCodes.Evl001, "maps/temp_map/terrain.txt").Message.ShouldStartWith("line 2, column 3: U+00E9 is not valid here");
    }

    [Fact]
    public void Load_ManyProblemsInOneFile_ReportsTheFirstTwentyAndACount()
    {
        using var temp = TempMap.Plain();
        temp.Write("terrain.txt", TempMap.Rows(8, 8, 'x'));

        MapLoadResult result = temp.Load();

        ContentDiagnostic[] inFile = [.. result.Diagnostics.Where(d => d.File == "maps/temp_map/terrain.txt")];
        inFile.Length.ShouldBe(21);
        inFile[^1].Message.ShouldBe("44 more problems in this file are not shown");
        inFile[0].Message.ShouldStartWith("line 1, column 1:");
    }

    // ---- rivers

    [Fact]
    public void Load_RiverWithoutRiversFile_ExplainsWhere()
    {
        using var temp = TempMap.Plain();
        temp.Write("terrain.txt", TempMap.With(TempMap.Rows(8, 8, '.'), 4, 3, '='));

        MapLoadResult result = temp.Load();

        result.Map.ShouldBeNull();
        ContentDiagnostic problem = Single(result, DiagnosticCodes.Evl001, "maps/temp_map/rivers.txt");
        problem.Message.ShouldContain("line 4, column 5 of terrain.txt");
    }

    [Fact]
    public void Load_RiverTileWithoutWidthAndWidthOnDryTile_AreBothReported()
    {
        using var temp = TempMap.Plain();
        temp.Write("terrain.txt", TempMap.With(TempMap.Rows(8, 8, '.'), 4, 3, '='));
        temp.Write("rivers.txt", TempMap.With(TempMap.Rows(8, 8, '.'), 1, 5, '2'));

        MapLoadResult result = temp.Load();

        ContentDiagnostic[] problems = [.. result.Diagnostics.Where(d => d.File == "maps/temp_map/rivers.txt")];
        problems.Length.ShouldBe(2);
        problems[0].Message.ShouldStartWith("line 4, column 5: this is a river tile");
        problems[1].Message.ShouldStartWith("line 6, column 2: this is not a river tile");
    }

    // ---- map.json

    [Fact]
    public void Load_InvalidJson_ReportsLineAndColumn()
    {
        using var temp = TempMap.Plain();
        temp.Write("map.json", "{\n  \"width\": 8,\n  \"height\": 8,\n  \"startSite\": { \"x\": 2, \"y\": 2, },\n}\n");

        MapLoadResult result = temp.Load();

        ContentDiagnostic problem = Single(result, DiagnosticCodes.Evl001, "maps/temp_map/map.json");
        problem.Message.ShouldStartWith("line 4, column ");
        problem.Message.ShouldContain("invalid JSON");
    }

    [Fact]
    public void Load_UnknownProperty_PointsAtTheKey()
    {
        using var temp = TempMap.Plain();
        temp.Write("map.json", "{\n  \"width\": 8,\n  \"height\": 8,\n  \"startSite\": { \"x\": 2, \"y\": 2 },\n  \"sze\": 3\n}\n");

        ContentDiagnostic problem = Single(temp.Load(), DiagnosticCodes.Evl001, "maps/temp_map/map.json");

        problem.JsonPointer.ShouldBe("/sze");
        problem.Message.ShouldStartWith("line 5, column 3: unknown property 'sze'");
    }

    [Fact]
    public void Load_LegendNamesAnUndefinedTerrain_IsEvl002AtTheEntry()
    {
        using var temp = TempMap.Plain();
        temp.Write("map.json", "{\n  \"width\": 8,\n  \"height\": 8,\n  \"legend\": { \".\": \"plains\", \"Z\": \"lava\" },\n  \"startSite\": { \"x\": 2, \"y\": 2 }\n}\n");

        ContentDiagnostic problem = Single(temp.Load(), DiagnosticCodes.Evl002, "maps/temp_map/map.json");

        problem.JsonPointer.ShouldBe("/legend/Z");
        problem.Message.ShouldStartWith("line 4, column 35: 'lava' is not a defined terrain");
    }

    [Fact]
    public void Load_ExplicitLegend_ReplacesTheDefault()
    {
        using var temp = TempMap.Plain();
        temp.Write("map.json", """{ "width": 8, "height": 8, "legend": { "a": "plains", "b": "forest" }, "startSite": { "x": 2, "y": 2 } }""");
        temp.Write("terrain.txt", TempMap.Rows(8, 8, 'a').Remove(0, 1).Insert(0, "b"));

        MapDef map = temp.Load().GetMapOrThrow();
        map.Terrain[0].ShouldBe(Terrain("forest"));
        map.Terrain[1].ShouldBe(Terrain("plains"));

        temp.Write("terrain.txt", TempMap.Rows(8, 8, '.'));
        temp.Load().Diagnostics[0].Message.ShouldContain("'.' is not valid here; expected one of the legend characters (a b)");
    }

    [Fact]
    public void Load_DefaultLegendTerrainMissingFromContent_IsEvl002OnlyWhereTheMapUsesIt()
    {
        // A content pack with only the minimal terrains: 'T' (forest) exists, '^' (hills) does not.
        ContentDb minimal = ContentLoader.Load(TestPaths.MinimalPack).GetDbOrThrow();
        using var temp = TempMap.Plain();
        MapLoadResult fine = MapLoader.Load([TestPaths.MinimalPack, temp.Root], temp.Id, minimal);
        fine.HasErrors.ShouldBeFalse(DiagnosticAssertions.Dump(fine.Diagnostics));

        temp.Write("terrain.txt", TempMap.With(TempMap.Rows(8, 8, '.'), 1, 1, '^'));
        MapLoadResult result = MapLoader.Load([TestPaths.MinimalPack, temp.Root], temp.Id, minimal);

        ContentDiagnostic problem = Single(result, DiagnosticCodes.Evl002, "maps/temp_map/terrain.txt");
        problem.Message.ShouldStartWith("line 2, column 2: '^' stands for the terrain 'hills' in the default legend");
    }

    [Theory]
    [InlineData("""{ "width": 0, "height": 8, "startSite": { "x": 0, "y": 0 } }""", "/width", "out of range")]
    [InlineData("""{ "width": 8, "height": 2000, "startSite": { "x": 0, "y": 0 } }""", "/height", "out of range")]
    [InlineData("""{ "width": 8.5, "height": 8, "startSite": { "x": 0, "y": 0 } }""", "/width", "expected a whole number")]
    [InlineData("""{ "width": 8, "height": 8, "startSite": { "x": 9, "y": 0 } }""", "/startSite/x", "out of range")]
    [InlineData("""{ "width": 8, "height": 8, "startSite": { "x": 0, "y": 0, "rotation": 4 } }""", "/startSite/rotation", "out of range")]
    [InlineData("""{ "width": 8, "height": 8, "startSite": { "x": 0 } }""", "/startSite", "missing required property 'y'")]
    [InlineData("""{ "width": 8, "height": 8, "startSite": { "x": 0, "y": 0 }, "startStock": { "wood": 1.2345 } }""", "/startStock/wood", "more than 3 decimal places")]
    [InlineData("""{ "width": 8, "height": 8, "startSite": { "x": 0, "y": 0 }, "startStock": { "wood": 0 } }""", "/startStock/wood", "greater than zero")]
    [InlineData("""{ "width": 8, "height": 8, "startSite": { "x": 0, "y": 0 }, "regions": { "ab": "region.x" } }""", "/regions/ab", "base-36")]
    [InlineData("""{ "width": 8, "height": 8, "startSite": { "x": 0, "y": 0 }, "regions": { "a": "region.x" } }""", "/regions/a", "base-36")]
    [InlineData("""{ "width": 8, "height": 8, "startSite": { "x": 0, "y": 0 }, "depositLegend": { ".": "stone" } }""", "/depositLegend/.", "no deposit")]
    [InlineData("""{ "width": 8, "height": 8, "startSite": { "x": 0, "y": 0 }, "legend": {} }""", "/legend", "legend is empty")]
    public void Load_BadMapJson_ReportsEvl001AtThePointer(string json, string jsonPointer, string messagePart)
    {
        using var temp = TempMap.Plain();
        temp.Write("map.json", json);

        MapLoadResult result = temp.Load();

        result.Map.ShouldBeNull();
        ContentDiagnostic[] atPointer = [.. result.Diagnostics.Where(d => d.File == "maps/temp_map/map.json" && d.JsonPointer == jsonPointer)];
        atPointer.ShouldNotBeEmpty(DiagnosticAssertions.Dump(result.Diagnostics));
        atPointer[0].Code.ShouldBe(DiagnosticCodes.Evl001);
        atPointer[0].Message.ShouldContain(messagePart);
        atPointer[0].Message.ShouldStartWith("line ");
    }

    [Fact]
    public void Load_StartStockOfAnUndefinedGood_IsEvl002()
    {
        using var temp = TempMap.Plain();
        temp.Write("map.json", """{ "width": 8, "height": 8, "startSite": { "x": 2, "y": 2 }, "startStock": { "gold": 3 } }""");

        ContentDiagnostic problem = Single(temp.Load(), DiagnosticCodes.Evl002, "maps/temp_map/map.json");

        problem.JsonPointer.ShouldBe("/startStock/gold");
    }

    [Fact]
    public void Load_DepositLegendWithAnUnknownDeposit_IsEvl002()
    {
        using var temp = TempMap.Plain();
        temp.Write("map.json", """{ "width": 8, "height": 8, "startSite": { "x": 2, "y": 2 }, "depositLegend": { "g": "gold" } }""");

        ContentDiagnostic problem = Single(temp.Load(), DiagnosticCodes.Evl002, "maps/temp_map/map.json");

        problem.JsonPointer.ShouldBe("/depositLegend/g");
        problem.Message.ShouldContain("stone, clay, iron, coal, copper");
    }

    [Fact]
    public void Load_CustomDepositLegend_IsUsed()
    {
        using var temp = TempMap.Plain();
        temp.Write("map.json", """{ "width": 8, "height": 8, "startSite": { "x": 2, "y": 2 }, "depositLegend": { "g": "coal" } }""");
        temp.Write("deposits.txt", "g.......\n" + TempMap.Rows(8, 7, '.'));

        MapDef map = temp.Load().GetMapOrThrow();

        map.Deposit[0].ShouldBe((byte)((int)DepositKind.Coal + 1));
    }

    // ---- start site

    [Fact]
    public void Load_StartSiteFootprintLeavesTheMap_IsReported()
    {
        using var temp = TempMap.Plain();
        temp.Write("map.json", """{ "width": 8, "height": 8, "startSite": { "x": 6, "y": 2 } }""");

        ContentDiagnostic problem = Single(temp.Load(), DiagnosticCodes.Evl001, "maps/temp_map/map.json");

        problem.JsonPointer.ShouldBe("/startSite");
        problem.Message.ShouldContain("4 x 4 tiles at (6, 2) leaves the 8 x 8 map");
    }

    [Fact]
    public void Load_StartSiteOnUnbuildableGround_IsReported()
    {
        using var temp = TempMap.Plain();
        temp.Write("terrain.txt", TempMap.With(TempMap.Rows(8, 8, '.'), 3, 3, '~'));

        ContentDiagnostic problem = Single(temp.Load(), DiagnosticCodes.Evl001, "maps/temp_map/map.json");

        problem.Message.ShouldContain("(3, 3), which is 'sea' and not buildable");
    }

    [Fact]
    public void Load_RotatedStartSite_SwapsTheFootprintSides()
    {
        // The minimal Founders' Hall is square, so only the bounds check can tell; a 4 x 4 hall fits at x = 4 either way.
        using var temp = TempMap.Plain();
        temp.Write("map.json", """{ "width": 8, "height": 8, "startSite": { "x": 4, "y": 4, "rotation": 3 } }""");

        temp.Load().HasErrors.ShouldBeFalse();
    }

    // ---- finding maps

    [Fact]
    public void Load_UnknownMap_IsEvl002()
    {
        MapLoadResult result = MapLoader.Load(MapFixtures.ContentRoots, "no_such_map", MapFixtures.Content);

        result.Map.ShouldBeNull();
        result.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe(DiagnosticCodes.Evl002);
    }

    [Theory]
    [InlineData("../test_16")]
    [InlineData("Test16")]
    [InlineData("")]
    [InlineData("a/b")]
    public void Load_InvalidMapId_IsRejectedWithoutTouchingTheDisk(string id)
    {
        MapLoadResult result = MapLoader.Load(MapFixtures.ContentRoots, id, MapFixtures.Content);

        result.Map.ShouldBeNull();
        result.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe(DiagnosticCodes.Evl001);
    }

    [Fact]
    public void Load_MissingTerrainOrMapFile_IsReported()
    {
        using var temp = TempMap.Plain();
        File.Delete(Path.Combine(temp.Root, "maps", temp.Id, "terrain.txt"));
        Single(temp.Load(), DiagnosticCodes.Evl001, "maps/temp_map/terrain.txt").Message.ShouldBe("the required file is missing");

        temp.Write("terrain.txt", TempMap.Rows(8, 8, '.'));
        File.Delete(Path.Combine(temp.Root, "maps", temp.Id, "map.json"));
        Single(temp.Load(), DiagnosticCodes.Evl001, "maps/temp_map/map.json").Message.ShouldBe("the required file is missing");
    }

    [Fact]
    public void Load_LastRootWithTheMapWins_AsAWhole()
    {
        using var temp = TempMap.Plain("test_16");

        MapDef map = temp.Load().GetMapOrThrow();

        map.Width.ShouldBe(8);
    }

    [Fact]
    public void Load_UnknownFileInTheFolder_IsAWarningAndTheMapStillLoads()
    {
        using var temp = TempMap.Plain();
        temp.Write("elevations.txt", TempMap.Rows(8, 8, '0'));
        temp.Write("notes.md", "free text");
        temp.Write(".DS_Store", "junk");

        MapLoadResult result = temp.Load();

        result.Map.ShouldNotBeNull();
        ContentDiagnostic warning = result.Diagnostics.ShouldHaveSingleItem();
        warning.Severity.ShouldBe(DiagnosticSeverity.Warning);
        warning.File.ShouldBe("maps/temp_map/elevations.txt");
    }

    [Fact]
    public void Load_SameInputTwice_GivesEqualMaps()
    {
        MapDef first = MapFixtures.LoadClean("test_16");
        MapDef second = MapFixtures.LoadClean("test_16");

        second.Terrain.ShouldBe(first.Terrain);
        second.Elevation.ShouldBe(first.Elevation);
        second.Fertility.ShouldBe(first.Fertility);
        second.StartStock.ShouldBe(first.StartStock);
    }

    [Fact]
    public void GetMapOrThrow_FailedLoad_ThrowsWithTheDiagnostics()
    {
        MapLoadResult result = MapLoader.Load(MapFixtures.ContentRoots, "bad_character", MapFixtures.Content);

        MapLoadException thrown = Should.Throw<MapLoadException>(() => result.GetMapOrThrow());

        thrown.Diagnostics.ShouldBe(result.Diagnostics);
        thrown.Message.ShouldContain("line 3, column 7");
    }
}
