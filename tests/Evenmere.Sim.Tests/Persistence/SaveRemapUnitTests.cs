using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Advisors;
using Evenmere.Sim.Systems.Construction;
using Evenmere.Sim.Systems.Map;
using Evenmere.Sim.Systems.Modifiers;
using Evenmere.Sim.Systems.Population;
using Evenmere.Sim.Systems.Production;
using Evenmere.Sim.Systems.Progression;
using Evenmere.Sim.Systems.Roads;
using Evenmere.Sim.Systems.Statistics;
using Evenmere.Sim.Tests.Integration;

namespace Evenmere.Sim.Tests.Persistence;

/// <summary>
/// The remap pass on fabricated states: the new content is the real slice content and the save's tables are altered
/// copies of it, so every assertion names real items. Slice-level tests cover full saves; these pin each section down.
/// </summary>
public sealed class SaveRemapUnitTests
{
    private static ContentDb Content => SiteFinder.Content;

    [Fact]
    public void Remap_ExtraGoodFirst_StockLandsOnRightGoods()
    {
        (SaveTables tables, WorldState world) = ShiftedGoodsWorld();
        int oldBread = Array.IndexOf(tables.OldGoods, "bread");
        world.Storage.Stock[0] = Amount.FromMilli(999_000);
        world.Storage.Stock[oldBread] = Amount.FromMilli(1_000);

        var report = new SaveReport();
        RemapPass.Apply(world, tables, report);

        world.Storage.Stock[Content.IdOf<GoodId>("bread").Value].ShouldBe(Amount.FromMilli(1_000));
        world.Storage.Stock.Length.ShouldBe(Content.Goods.Count);
        report.Remapped.ShouldContain(line => line.Contains("goods") && line.Contains("bread"));
        report.Dropped.ShouldContain(line => line.Contains("aardvark"));
    }

    [Fact]
    public void Remap_UnknownTech_DropsProgressTargetQueueModifiersAndSources()
    {
        ContentDb content = Content;
        string[] oldTechs = [.. content.Techs.Names, "gone_tech"];
        SaveTables tables = TablesWith(content, techs: oldTechs);
        var world = WorldState.CreateNew(content, 1);

        int gone = oldTechs.Length - 1;
        world.Research.Progress = new Amount[oldTechs.Length];
        world.Research.Progress[gone] = Amount.FromMilli(60_000);
        world.Research.Done = new ulong[(oldTechs.Length + 63) >> 6];
        world.Research.Done[0] = 1UL << gone;
        world.Research.Target = TechId.From((ushort)gone);
        world.Research.Queue = [TechId.From((ushort)gone), content.IdOf<TechId>("cultivation")];
        world.Modifiers.Active =
        [
            new ActiveModifier
            {
                Source = EffectSource.ForTech(TechId.From((ushort)gone)),
                Stat = Stat.Productivity,
                TargetKind = ModifierTargetKind.Island,
                TargetValue = 0,
                Percent = 10,
                When = null,
            },
        ];
        world.Progression.AppliedSources = [EffectSource.ForTech(TechId.From((ushort)gone))];

        var report = new SaveReport();
        RemapPass.Apply(world, tables, report);

        world.Research.Progress.Length.ShouldBe(content.Techs.Count);
        world.Research.Target.ShouldBeNull();
        world.Research.Queue.ShouldBe([content.IdOf<TechId>("cultivation")]);
        world.Research.Done.ShouldBe([0UL]);
        world.Modifiers.Active.ShouldBeEmpty();
        world.Progression.AppliedSources.ShouldBeEmpty();
        report.Dropped.Count(line => line.Contains("gone_tech")).ShouldBe(1);
        report.Dropped.Single(line => line.Contains("gone_tech"))
            .ShouldContain("research target cleared");
    }

    [Fact]
    public void Remap_UnknownCivic_ClearsTargetAndBits()
    {
        ContentDb content = Content;
        string[] oldCivics = [.. content.Civics.Names, "gone_civic"];
        SaveTables tables = TablesWith(content, civics: oldCivics);
        var world = WorldState.CreateNew(content, 1);

        int gone = oldCivics.Length - 1;
        world.Culture.Progress = new Amount[oldCivics.Length];
        world.Culture.Progress[gone] = Amount.FromMilli(5_000);
        world.Culture.Done = new ulong[(oldCivics.Length + 63) >> 6];
        world.Culture.Target = CivicId.From((ushort)gone);

        var report = new SaveReport();
        RemapPass.Apply(world, tables, report);

        world.Culture.Progress.Length.ShouldBe(content.Civics.Count);
        world.Culture.Target.ShouldBeNull();
        report.Dropped.Count(line => line.Contains("gone_civic")).ShouldBe(1);
    }

    [Fact]
    public void Remap_UnknownBuildingType_BecomesRuinWithBufferRefund()
    {
        ContentDb content = Content;
        string[] oldBuildings = [.. content.Buildings.Names, "gone_building"];
        SaveTables tables = TablesWithBuildings(content, oldBuildings);
        WorldState world = NewSmallWorld(content);

        int gone = oldBuildings.Length - 1;
        EntityId id = world.EntityIds.Allocate();
        world.Map.Buildings.Add(id, new BuildingRecord
        {
            Type = BuildingTypeId.From((ushort)gone),
            Origin = new TileCoord(0, 0),
            Rotation = Rotation.Deg0,
            Width = 1,
            Height = 1,
            State = BuildingState.Active,
            Stage = 0,
        });
        world.Map.Occupancy[0] = id.Value;

        RecipeId grind = content.IdOf<RecipeId>("grind_flour");
        world.Production.Producers.Add(id, new ProducerRecord
        {
            Recipe = grind,
            Inputs = [Amount.FromMilli(5_000)],
            Outputs = [Amount.FromMilli(2_000)],
            InputRequestTicks = [ProducerRecord.NoRequest],
            OutputRequestTicks = [ProducerRecord.NoRequest],
            AreaYield = Ratio.One,
            InputsReady = true,
        });
        world.Population.Houses.Add(id, new HouseRecord { Tier = content.TiersByOrder[0], Type = content.IdOf<BuildingTypeId>("settler_house"), Residents = 3 });

        GoodId grain = content.IdOf<GoodId>("grain");
        GoodId flour = content.IdOf<GoodId>("flour");
        Amount grainBefore = world.Storage.Stock[grain.Value];
        Amount flourBefore = world.Storage.Stock[flour.Value];

        var report = new SaveReport();
        RemapPass.Apply(world, tables, report);

        world.Map.Buildings.TryGet(id, out BuildingRecord ruin).ShouldBeTrue();
        ruin.State.ShouldBe(BuildingState.Ruin);
        ruin.Stage.ShouldBe(0);
        ruin.Type.ShouldBe(content.Required.FoundersHall);
        world.Production.Producers.Contains(id).ShouldBeFalse();
        world.Population.Houses.Contains(id).ShouldBeFalse();
        world.Storage.Stock[grain.Value].ShouldBe(grainBefore + Amount.FromMilli(5_000));
        world.Storage.Stock[flour.Value].ShouldBe(flourBefore + Amount.FromMilli(2_000));
        report.Ruins.ShouldContain(line => line.Contains("gone_building") && line.Contains("refunded"));
    }

    [Fact]
    public void Remap_UnknownRoadType_DropsItsTiles()
    {
        ContentDb content = Content;
        string[] oldBuildings = [.. content.Buildings.Names, "gone_road"];
        SaveTables tables = TablesWithBuildings(content, oldBuildings);
        WorldState world = NewSmallWorld(content);

        int gone = oldBuildings.Length - 1;
        world.Roads = new RoadsState { Width = 2, Height = 2, Layer = [0, (ushort)(gone + 1), (ushort)(content.IdOf<BuildingTypeId>("dirt_road").Value + 1), 0] };

        var report = new SaveReport();
        RemapPass.Apply(world, tables, report);

        world.Roads.Layer[1].ShouldBe((ushort)0);
        world.Roads.Layer[2].ShouldBe((ushort)(content.IdOf<BuildingTypeId>("dirt_road").Value + 1));
        report.Dropped.ShouldContain(line => line.Contains("gone_road") && line.Contains("1 road tile"));
    }

    [Fact]
    public void Remap_UnknownTerrain_RemapsTilesToFirstTerrain()
    {
        ContentDb content = Content;
        string[] oldTerrains = [.. content.Terrains.Names, "gone_terrain"];
        SaveTables tables = TablesWithTerrains(content, oldTerrains);
        WorldState world = NewSmallWorld(content);

        int gone = oldTerrains.Length - 1;
        world.Map.Terrain = [0, (byte)gone, 0, 0];

        var report = new SaveReport();
        RemapPass.Apply(world, tables, report);

        world.Map.Terrain[1].ShouldBe((byte)0);
        report.Dropped.ShouldContain(line => line.Contains("gone_terrain"));
    }

    [Fact]
    public void Remap_ShiftedGood_RetryKeysFollowAndStaySorted()
    {
        (SaveTables tables, WorldState world) = ShiftedGoodsWorld();

        // Old flour (raw 3 in the shifted table) with direction 1 on building 7; old bread (raw 1) on building 5.
        world.Logistics.RetryKeys = [Pack(7, 3, 1), Pack(5, 1, 0)];
        world.Logistics.RetryDays = [40, 41];

        var report = new SaveReport();
        RemapPass.Apply(world, tables, report);

        // Flour 3->2, bread 1->0; sorted by key puts building 5 first.
        world.Logistics.RetryKeys.ShouldBe([Pack(5, 0, 0), Pack(7, 2, 1)]);
        world.Logistics.RetryDays.ShouldBe([41, 40]);
        report.IsClean.ShouldBeFalse();

        static long Pack(int building, int good, int direction) => ((long)building << 32) | ((long)(good * 2 + direction));
    }

    [Fact]
    public void Remap_UnknownGoodInRetryKey_DropsEntryAndDay()
    {
        (SaveTables tables, WorldState world) = ShiftedGoodsWorld();
        world.Logistics.RetryKeys = [(((long)7 << 32) | (0L << 1) | 1L)];
        world.Logistics.RetryDays = [40];

        var report = new SaveReport();
        RemapPass.Apply(world, tables, report);

        world.Logistics.RetryKeys.ShouldBeEmpty();
        world.Logistics.RetryDays.ShouldBeEmpty();
        report.Dropped.ShouldContain(line => line.Contains("aardvark"));
    }

    [Fact]
    public void Remap_ShiftedGoods_StatisticsSeriesFollowNames()
    {
        (SaveTables tables, WorldState world) = ShiftedGoodsWorld();
        ContentDb content = Content;

        int oldGoods = tables.OldGoods.Length;
        int oldTiers = content.Tiers.Count;
        int oldSeries = (oldGoods * 4) + oldTiers + 3;
        world.Statistics.GoodCount = oldGoods;
        world.Statistics.TierCount = oldTiers;
        world.Statistics.DaysSampled = 7;
        world.Statistics.Daily = new long[oldSeries * StatisticsState.DailyCapacity];
        world.Statistics.Seasonal = new long[oldSeries * StatisticsState.SeasonalCapacity];
        world.Statistics.LastConsumed = new long[oldGoods];

        // Old bread by name in the shifted table; old raw 0 is the gone aardvark.
        int oldBread = Array.IndexOf(tables.OldGoods, "bread");
        for (int s = 0; s < StatisticsState.DailyCapacity; s++)
        {
            world.Statistics.Daily[(oldBread * StatisticsState.DailyCapacity) + s] = 1000 + s;
        }

        world.Statistics.LastConsumed[oldBread] = 4242;

        var report = new SaveReport();
        RemapPass.Apply(world, tables, report);

        int bread = content.IdOf<GoodId>("bread").Value;
        world.Statistics.GoodCount.ShouldBe(content.Goods.Count);
        world.Statistics.DaysSampled.ShouldBe(7);
        for (int s = 0; s < StatisticsState.DailyCapacity; s++)
        {
            world.Statistics.Daily[(bread * StatisticsState.DailyCapacity) + s].ShouldBe(1000 + s);
        }

        world.Statistics.LastConsumed[bread].ShouldBe(4242);
        report.Remapped.ShouldContain(line => line.Contains("goods"));
    }

    [Fact]
    public void Remap_ShiftedGoods_ProducerBuffersFollowRecipeOrder()
    {
        (SaveTables tables, WorldState world) = ShiftedGoodsWorld();
        ContentDb content = Content;

        RecipeId grind = content.IdOf<RecipeId>("grind_flour");
        var record = new ProducerRecord
        {
            Recipe = grind,
            Inputs = [Amount.FromMilli(5_000)],
            Outputs = [Amount.FromMilli(2_000)],
            InputRequestTicks = [12],
            OutputRequestTicks = [ProducerRecord.NoRequest],
            AreaYield = Ratio.One,
            InputsReady = true,
        };

        world.Production.Producers.Add(new EntityId(9), record);

        var report = new SaveReport();
        RemapPass.Apply(world, tables, report);

        world.Production.Producers.TryGet(new EntityId(9), out ProducerRecord? after).ShouldBeTrue();
        after.ShouldNotBeNull();
        after.Recipe.ShouldBe(grind);
        after.Inputs.ShouldBe([Amount.FromMilli(5_000)]);
        after.Outputs.ShouldBe([Amount.FromMilli(2_000)]);
        after.InputRequestTicks.ShouldBe([12]);
        report.IsClean.ShouldBeFalse();
    }

    [Fact]
    public void Remap_AdviceAboutUnknownGood_DropsRecordAndKeepsOrder()
    {
        (SaveTables tables, WorldState world) = ShiftedGoodsWorld();
        ContentDb content = Content;

        // Old bread by name in the shifted table; old raw 0 is the gone aardvark.
        var oldBread = GoodId.From((ushort)Array.IndexOf(tables.OldGoods, "bread"));
        world.Advisors.Records =
        [
            new AdviceRecord { Id = 2, Kind = AdviceKind.GoodShortage, Subject = SubjectLink.ForGood(GoodId.From(0)), FirstSeenTick = 1, LastSeenTick = 2, Showing = true },
            new AdviceRecord { Id = 1, Kind = AdviceKind.GoodShortage, Subject = SubjectLink.ForGood(oldBread), FirstSeenTick = 1, LastSeenTick = 2, Showing = true },
        ];

        var report = new SaveReport();
        RemapPass.Apply(world, tables, report);

        // Old raw 0 is the gone aardvark; the bread note survives with its new ID.
        world.Advisors.Records.Length.ShouldBe(1);
        world.Advisors.Records[0].Subject.ShouldBe(SubjectLink.ForGood(content.IdOf<GoodId>("bread")));
        report.Dropped.ShouldContain(line => line.Contains("advice"));
    }

    [Fact]
    public void Remap_UnknownEra_ResetsToFirst()
    {
        ContentDb content = Content;
        string[] oldEras = [.. content.Eras.Names, "gone_era"];
        SaveTables tables = TablesWithEras(content, oldEras);
        var world = WorldState.CreateNew(content, 1);
        world.Progression.Era = oldEras.Length - 1;

        var report = new SaveReport();
        RemapPass.Apply(world, tables, report);

        world.Progression.Era.ShouldBe(0);
        report.Dropped.ShouldContain(line => line.Contains("gone_era"));
    }

    // A save one good wider than the content, with the extra good sorting first so every later ID shifts by one.
    private static (SaveTables Tables, WorldState World) ShiftedGoodsWorld()
    {
        ContentDb content = Content;
        string[] oldGoods = ["aardvark", .. content.Goods.Names];
        SaveTables tables = TablesWith(content, oldGoods, null);
        var world = WorldState.CreateNew(content, 1);
        world.Storage.Stock = new Amount[oldGoods.Length];
        return (tables, world);
    }

    private static SaveTables TablesWith(
        ContentDb content,
        string[]? goods = null,
        string[]? buildings = null,
        string[]? techs = null,
        string[]? civics = null,
        string[]? terrains = null,
        string[]? eras = null)
    {
        var header = new SaveHeader
        {
            Goods = goods ?? [.. content.Goods.Names],
            Buildings = buildings ?? [.. content.Buildings.Names],
            Recipes = [.. content.Recipes.Names],
            Tiers = [.. content.Tiers.Names],
            Techs = techs ?? [.. content.Techs.Names],
            Civics = civics ?? [.. content.Civics.Names],
            Traditions = [.. content.Traditions.Names],
            Festivals = [.. content.Festivals.Names],
            GreatWorks = [.. content.GreatWorks.Names],
            Monuments = [.. content.Monuments.Names],
            Happenings = [.. content.Happenings.Names],
            Eras = eras ?? [.. content.Eras.Names],
            Terrains = terrains ?? [.. content.Terrains.Names],
        };
        return SaveTables.Build(header, content);
    }

    private static SaveTables TablesWithBuildings(ContentDb content, string[] buildings) =>
        TablesWith(content, buildings: buildings);

    private static SaveTables TablesWithTerrains(ContentDb content, string[] terrains) =>
        TablesWith(content, terrains: terrains);

    private static SaveTables TablesWithEras(ContentDb content, string[] eras) =>
        TablesWith(content, eras: eras);

    // A 2x2 island with valid terrain, so MapState.Validate would pass after the remap.
    private static WorldState NewSmallWorld(ContentDb content)
    {
        var world = WorldState.CreateNew(content, 1);
        world.Map.Width = 2;
        world.Map.Height = 2;
        world.Map.Terrain = [0, 0, 0, 0];
        world.Map.Elevation = [0, 0, 0, 0];
        world.Map.Features = [0, 0, 0, 0];
        world.Map.RiverWidth = [0, 0, 0, 0];
        world.Map.Region = [0, 0, 0, 0];
        world.Map.Deposit = [0, 0, 0, 0];
        world.Map.Fertility = [0, 0, 0, 0];
        world.Map.Slope = [0, 0, 0, 0];
        world.Map.Occupancy = [0, 0, 0, 0];
        return world;
    }
}
