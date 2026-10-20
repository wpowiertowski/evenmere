using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.Tests.Architecture;
using Evenmere.Sim.Tests.Fakes;
using MessagePack;

namespace Evenmere.Sim.Tests.Contracts;

/// <summary>The shared value types of M1-00 (<c>Queries/Types</c>): coordinates, tile and building copies, results, keys and the explanation tree.</summary>
public sealed class TypeContractTests
{
    // ---- TileCoord, ChunkCoord, Rotation ----------------------------------------------------------------------

    [Fact]
    public void TileCoord_IndexAndBack_RoundTripsRowsTopToBottom()
    {
        var tile = new TileCoord(5, 3);

        tile.ToIndex(16).ShouldBe((3 * 16) + 5);
        TileCoord.FromIndex(tile.ToIndex(16), 16).ShouldBe(tile);
        TileCoord.FromIndex(0, 16).ShouldBe(new TileCoord(0, 0));
        TileCoord.FromIndex(16, 16).ShouldBe(new TileCoord(0, 1));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(15, 7, true)]
    [InlineData(16, 7, false)]
    [InlineData(15, 8, false)]
    [InlineData(-1, 0, false)]
    [InlineData(0, -1, false)]
    public void TileCoord_IsInside_ChecksBothAxesAgainstTheMapSize(int x, int y, bool inside)
    {
        new TileCoord(x, y).IsInside(16, 8).ShouldBe(inside);
    }

    [Fact]
    public void TileCoord_Offset_MovesRightAndDown()
    {
        new TileCoord(2, 2).Offset(3, -1).ShouldBe(new TileCoord(5, 1));
        new TileCoord(2, 2).ToString().ShouldBe("(2, 2)");
    }

    [Fact]
    public void ChunkCoord_Of_SplitsAtMultiplesOf32()
    {
        ChunkCoord.Of(0, 0).ShouldBe(new ChunkCoord(0, 0));
        ChunkCoord.Of(31, 31).ShouldBe(new ChunkCoord(0, 0));
        ChunkCoord.Of(32, 31).ShouldBe(new ChunkCoord(1, 0));
        ChunkCoord.Of(new TileCoord(63, 64)).ShouldBe(new ChunkCoord(1, 2));
    }

    [Theory]
    [InlineData(Rotation.Deg0, false)]
    [InlineData(Rotation.Deg90, true)]
    [InlineData(Rotation.Deg180, false)]
    [InlineData(Rotation.Deg270, true)]
    public void Rotation_SwapsAxes_OnlyOnQuarterAndThreeQuarterTurns(Rotation rotation, bool swaps)
    {
        rotation.SwapsAxes().ShouldBe(swaps);
        rotation.Rotate(new Footprint(3, 2)).ShouldBe(swaps ? new Footprint(2, 3) : new Footprint(3, 2));
    }

    [Fact]
    public void Rotation_NextAndPrevious_WrapAroundTheFourTurns()
    {
        Rotation.Deg270.Next().ShouldBe(Rotation.Deg0);
        Rotation.Deg0.Previous().ShouldBe(Rotation.Deg270);
        Rotation.Deg90.Next().Next().ShouldBe(Rotation.Deg270);
        ((int)Rotation.Deg0).ShouldBe(0);
        ((int)Rotation.Deg270).ShouldBe(3);
        ((Rotation)4).IsValid().ShouldBeFalse();
        Rotation.Deg180.IsValid().ShouldBeTrue();
    }

    // ---- Serialisation (state types may hold a TileCoord) ---------------------------------------------------------

    [Fact]
    public void TileCoord_MessagePack_RoundTripsThroughTheSimResolverWithIntegerKeys()
    {
        TileCoord[] tiles = [new(0, 0), new(7, -3), new(255, 1024)];

        byte[] bytes = MessagePackSerializer.Serialize(tiles, SimResolver.Options, TestContext.Current.CancellationToken);
        TileCoord[] read = MessagePackSerializer.Deserialize<TileCoord[]>(bytes, SimResolver.Options, TestContext.Current.CancellationToken);

        read.ShouldBe(tiles);
        MessagePackKeyRules.Check(typeof(TileCoord)).ShouldBeEmpty();
        MessagePackKeyRules.CheckContiguousFromZero(typeof(TileCoord)).ShouldBeEmpty();
    }

    [Fact]
    public void KeyTypes_MessagePack_RoundTripThroughTheSimResolverWithIntegerKeys()
    {
        // The small key types a system may keep in its state are serialisable as they stand: integer keys, no formatter to add.
        RoundTrip(new StatKey(StatKind.Residents, 3));
        RoundTrip(new CounterKey(CounterKind.Built, 9));
        RoundTrip(EffectSource.ForTech(new TechId(4)));
        RoundTrip(SubjectLink.ForGood(new GoodId(2)));
        RoundTrip(new TileCoord(-5, 99));

        foreach (Type type in new[] { typeof(TileCoord), typeof(StatKey), typeof(CounterKey), typeof(EffectSource), typeof(SubjectLink) })
        {
            MessagePackKeyRules.Check(type).ShouldBeEmpty(type.Name);
            MessagePackKeyRules.CheckContiguousFromZero(type).ShouldBeEmpty(type.Name);
        }
    }

    private static void RoundTrip<T>(T value)
    {
        byte[] bytes = MessagePackSerializer.Serialize(value, SimResolver.Options, TestContext.Current.CancellationToken);
        MessagePackSerializer.Deserialize<T>(bytes, SimResolver.Options, TestContext.Current.CancellationToken).ShouldBe(value);
    }

    // ---- TileInfo, BuildingInfo, BuildingSequence ---------------------------------------------------------------

    [Fact]
    public void TileInfo_Fertility_UsesOneBitPerCrop()
    {
        uint mask = TileInfo.FertilityBit(CropKind.Grain) | TileInfo.FertilityBit(CropKind.Hops);
        var tile = new TileInfo(new TerrainId(1), 4, SlopeClass.Gentle, TileFeatures.River | TileFeatures.Coast, RiverWidthClass.Narrow, 3, DepositKind.Clay, mask);

        tile.IsFertileFor(CropKind.Grain).ShouldBeTrue();
        tile.IsFertileFor(CropKind.Hops).ShouldBeTrue();
        tile.IsFertileFor(CropKind.Flax).ShouldBeFalse();
        TileInfo.FertilityBit(CropKind.Grain).ShouldBe(1u);
        TileInfo.FertilityBit(CropKind.SugarBeet).ShouldBe(1u << 7);
    }

    [Fact]
    public void TileInfo_Has_RequiresEveryFeatureAsked()
    {
        var tile = new TileInfo(default, 0, SlopeClass.Flat, TileFeatures.River | TileFeatures.Coast, RiverWidthClass.None, 0, null, 0);

        tile.Has(TileFeatures.River).ShouldBeTrue();
        tile.Has(TileFeatures.River | TileFeatures.Coast).ShouldBeTrue();
        tile.Has(TileFeatures.River | TileFeatures.Marsh).ShouldBeFalse();
        tile.Has(TileFeatures.None).ShouldBeTrue();
    }

    [Fact]
    public void BuildingInfo_CoversAndIsComplete_FollowTheFootprintAndState()
    {
        var info = new BuildingInfo(new EntityId(4), new BuildingTypeId(1), new TileCoord(10, 20), Rotation.Deg90, 2, 3, BuildingState.Site, 4);

        info.Covers(10, 20).ShouldBeTrue();
        info.Covers(11, 22).ShouldBeTrue();
        info.Covers(12, 20).ShouldBeFalse();
        info.Covers(10, 23).ShouldBeFalse();
        info.Covers(9, 20).ShouldBeFalse();
        info.IsComplete.ShouldBeFalse();
        (info with { State = BuildingState.Active }).IsComplete.ShouldBeTrue();
        (info with { State = BuildingState.Paused }).IsComplete.ShouldBeTrue();
        (info with { State = BuildingState.Ruin }).IsComplete.ShouldBeFalse();
    }

    [Fact]
    public void BuildingSequence_Enumeration_IsInAscendingIdOrderAndFiltersByState()
    {
        var map = new FakeMapQueries();
        map.AddBuilding(new EntityId(7), new BuildingTypeId(0), 0, 0, state: BuildingState.Active);
        map.AddBuilding(new EntityId(3), new BuildingTypeId(0), 2, 0, state: BuildingState.Site);
        map.AddBuilding(new EntityId(5), new BuildingTypeId(0), 4, 0, state: BuildingState.Paused);
        map.AddBuilding(new EntityId(9), new BuildingTypeId(0), 6, 0, state: BuildingState.Ruin);

        var all = new List<int>();
        foreach (BuildingInfo building in map.Buildings)
        {
            all.Add(building.Id.Value);
        }

        var complete = new List<int>();
        foreach (BuildingInfo building in map.ActiveBuildings)
        {
            complete.Add(building.Id.Value);
        }

        all.ShouldBe([3, 5, 7, 9]);
        complete.ShouldBe([5, 7]);
    }

    [Fact]
    public void BuildingSequence_RemovingWhileEnumerating_StillVisitsTheRest()
    {
        var map = new FakeMapQueries();
        foreach (int id in new[] { 1, 2, 3, 4 })
        {
            map.AddBuilding(new EntityId(id), new BuildingTypeId(0), id, 0);
        }

        var visited = new List<int>();
        foreach (BuildingInfo building in map.ActiveBuildings)
        {
            visited.Add(building.Id.Value);
            map.RemoveBuilding(building.Id);
        }

        visited.ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public void BuildingSequence_Default_IsEmpty()
    {
        int count = 0;
        foreach (BuildingInfo unused in default(BuildingSequence))
        {
            count++;
            unused.Id.ShouldBe(EntityId.None);
        }

        count.ShouldBe(0);
    }

    // ---- PlacementResult ------------------------------------------------------------------------------------------

    [Fact]
    public void PlacementResult_Ok_AllowsPlacementWithNothingToReport()
    {
        PlacementResult.Ok.CanPlace.ShouldBeTrue();
        PlacementResult.Ok.Has(PlacementIssue.Occupied).ShouldBeFalse();
    }

    [Fact]
    public void PlacementResult_Errors_BlockPlacementAndWarningsDoNot()
    {
        var occupied = new PlacementFinding(PlacementIssue.Occupied, [new TileCoord(1, 1)]);
        var noRoad = new PlacementFinding(PlacementIssue.NoRoadAccess, [new TileCoord(1, 1), new TileCoord(2, 1)]);

        var blocked = new PlacementResult([occupied], [noRoad]);
        var warned = new PlacementResult([], [noRoad]);

        blocked.CanPlace.ShouldBeFalse();
        blocked.Has(PlacementIssue.Occupied).ShouldBeTrue();
        blocked.Has(PlacementIssue.NoRoadAccess).ShouldBeTrue();
        warned.CanPlace.ShouldBeTrue();
        warned.Has(PlacementIssue.Occupied).ShouldBeFalse();
    }

    [Fact]
    public void PlacementResult_SameFindings_AreEqualEvenWithSeparateArrays()
    {
        PlacementResult Make() => new(
            [new PlacementFinding(PlacementIssue.SlopeTooSteep, [new TileCoord(3, 4), new TileCoord(4, 4)])],
            [new PlacementFinding(PlacementIssue.LowYield, [])]);

        Make().ShouldBe(Make());
        Make().GetHashCode().ShouldBe(Make().GetHashCode());
        Make().ShouldNotBe(new PlacementResult([], []));
        Make().ShouldNotBe(Make() with { Warnings = [] });
    }

    [Fact]
    public void PlacementIssue_Codes_SeparateErrorsFromWarnings()
    {
        // Errors are below 100 and warnings from 100, so a view can tell them apart without a table.
        PlacementIssue[] errors =
        [
            PlacementIssue.OutOfBounds, PlacementIssue.Occupied, PlacementIssue.TerrainNotAllowed, PlacementIssue.TerrainAccessLocked,
            PlacementIssue.SlopeTooSteep, PlacementIssue.NotUnlocked, PlacementIssue.UniqueAlreadyBuilt, PlacementIssue.NeedsCoastline,
            PlacementIssue.NeedsDeposit, PlacementIssue.FertilityTooLow, PlacementIssue.NeedsRiverside,
        ];
        PlacementIssue[] warnings = [PlacementIssue.NoRoadAccess, PlacementIssue.OutsideStorehouseReach, PlacementIssue.LowYield];

        errors.ShouldAllBe(static i => (int)i > 0 && (int)i < 100);
        warnings.ShouldAllBe(static i => (int)i >= 100);
    }

    // ---- Houses, jobs, production -----------------------------------------------------------------------------------

    [Fact]
    public void HouseNeedsInfo_Counts_SplitBasicFromComfortAndMetFromUnmet()
    {
        var info = new HouseNeedsInfo(
            new EntityId(1),
            true,
            [
                Need(NeedClass.Basic, met: true),
                Need(NeedClass.Basic, met: false),
                Need(NeedClass.Basic, met: true),
                Need(NeedClass.Comfort, met: true),
                Need(NeedClass.Comfort, met: false),
            ]);

        info.BasicTotal.ShouldBe(3);
        info.BasicMet.ShouldBe(2);
        info.ComfortMet.ShouldBe(1);
    }

    [Fact]
    public void HouseNeedsInfo_SameNeeds_AreEqualInValue()
    {
        HouseNeedsInfo Make() => new(new EntityId(2), false, [Need(NeedClass.Basic, met: false)]);

        Make().ShouldBe(Make());
        Make().GetHashCode().ShouldBe(Make().GetHashCode());
        Make().ShouldNotBe(Make() with { HasMarketCoverage = true });
    }

    [Fact]
    public void ProductionInfo_SameContents_AreEqualInValue()
    {
        ProductionInfo Make(Amount stock) => new(
            new EntityId(8), new RecipeId(1), StallReason.MissingInput, Ratio.Zero, Ratio.One, false, Ratio.FromPerMille(500), 25, Ratio.FromPerMille(300),
            [new GoodAmount(new GoodId(2), stock)],
            []);

        Make(Amount.FromUnits(1)).ShouldBe(Make(Amount.FromUnits(1)));
        Make(Amount.FromUnits(1)).ShouldNotBe(Make(Amount.FromUnits(2)));
    }

    [Fact]
    public void TransportJobView_PhaseEndTick_IsStartPlusLength()
    {
        var job = new TransportJobView(
            new EntityId(1), new EntityId(2), new EntityId(3), new GoodId(0), Amount.FromUnits(2),
            TransportDirection.Pickup, TransportPhase.TravelOut, 100, 40);

        job.PhaseEndTick.ShouldBe(140);
    }

    // ---- Keys and sources -------------------------------------------------------------------------------------------

    [Fact]
    public void StatKey_Factories_NameDistinctSeries()
    {
        var good = new GoodId(3);
        StatKey[] keys =
        [
            StatKey.Stock(good), StatKey.Produced(good), StatKey.Consumed(good), StatKey.Demand(good), StatKey.Residents(new TierId(3)),
            StatKey.KnowledgePerDay, StatKey.CulturePerDay, StatKey.ActiveJobs,
        ];

        keys.Distinct().Count().ShouldBe(keys.Length);
        StatKey.Stock(good).ShouldBe(StatKey.Stock(new GoodId(3)));
        StatKey.Stock(good).ShouldNotBe(StatKey.Stock(new GoodId(4)));
    }

    [Fact]
    public void CounterKey_Factories_NameDistinctCounters()
    {
        var good = new GoodId(3);
        CounterKey[] keys = [CounterKey.Produced(good), CounterKey.Consumed(good), CounterKey.Built(new BuildingTypeId(3)), CounterKey.RoadTilesLaid];

        keys.Distinct().Count().ShouldBe(keys.Length);
    }

    [Fact]
    public void EffectSource_ForAndAs_RoundTripEveryKind()
    {
        EffectSource.ForTech(new TechId(4)).AsTech().ShouldBe(new TechId(4));
        EffectSource.ForCivic(new CivicId(5)).AsCivic().ShouldBe(new CivicId(5));
        EffectSource.ForTradition(new TraditionId(6)).AsTradition().ShouldBe(new TraditionId(6));
        EffectSource.ForFestival(new FestivalId(7)).AsFestival().ShouldBe(new FestivalId(7));
        EffectSource.ForMonument(new MonumentId(8)).AsMonument().ShouldBe(new MonumentId(8));
        EffectSource.ForHappening(new HappeningId(9)).AsHappening().ShouldBe(new HappeningId(9));
        EffectSource.ForBuilding(new EntityId(10)).AsBuilding().ShouldBe(new EntityId(10));
        EffectSource.ForTech(new TechId(4)).ShouldNotBe(EffectSource.ForCivic(new CivicId(4)));
    }

    [Fact]
    public void ModifierContext_Factories_FillOnlyWhatTheyName()
    {
        ModifierContext.Island.ShouldBe(default);
        ModifierContext.ForBuilding(new EntityId(2), new BuildingTypeId(3)).ShouldBe(new ModifierContext(new BuildingTypeId(3), new EntityId(2), null, null));
        ModifierContext.ForBuildingType(new BuildingTypeId(3)).Building.ShouldBe(EntityId.None);
        ModifierContext.ForTier(new TierId(1)).ShouldBe(new ModifierContext(null, EntityId.None, new TierId(1), null));
        ModifierContext.ForGood(new GoodId(4)).Good.ShouldBe(new GoodId(4));
        ModifierContext.ForTierAndGood(new TierId(1), new GoodId(4)).ShouldBe(new ModifierContext(null, EntityId.None, new TierId(1), new GoodId(4)));
    }

    // ---- Explanation ----------------------------------------------------------------------------------------------

    [Fact]
    public void ExplainArg_OfAndAs_RoundTripEveryKind()
    {
        ExplainArg.Of(-7).AsNumber().ShouldBe(-7);
        ExplainArg.Of(Amount.Parse("-12.345")).AsAmount().ShouldBe(Amount.Parse("-12.345"));
        ExplainArg.Of(Ratio.FromPerMille(750)).AsRatio().ShouldBe(Ratio.FromPerMille(750));
        ExplainArg.Of(new GoodId(2)).AsGood().ShouldBe(new GoodId(2));
        ExplainArg.Of(new BuildingTypeId(3)).AsBuildingType().ShouldBe(new BuildingTypeId(3));
        ExplainArg.Of(new TierId(1)).AsTier().ShouldBe(new TierId(1));
        ExplainArg.Of(new RecipeId(4)).AsRecipe().ShouldBe(new RecipeId(4));
        ExplainArg.Of(new TechId(5)).AsTech().ShouldBe(new TechId(5));
        ExplainArg.Of(new CivicId(6)).AsCivic().ShouldBe(new CivicId(6));
        ExplainArg.Of(new EntityId(70000)).AsEntity().ShouldBe(new EntityId(70000));
        ExplainArg.Of(ServiceKind.Shrine).AsService().ShouldBe(ServiceKind.Shrine);
        ExplainArg.Of(Season.Autumn).AsSeason().ShouldBe(Season.Autumn);
        ExplainArg.Of(StallReason.OutputFull).AsStall().ShouldBe(StallReason.OutputFull);
    }

    [Fact]
    public void ExplainArg_Kinds_AreRecordedWithTheValue()
    {
        ExplainArg.Of(new GoodId(2)).Kind.ShouldBe(ExplainArgKind.Good);
        ExplainArg.Of(new TierId(2)).Kind.ShouldBe(ExplainArgKind.Tier);
        ExplainArg.Of(new GoodId(2)).ShouldNotBe(ExplainArg.Of(new TierId(2)));
        ExplainArg.Of(Amount.FromUnits(1)).ShouldNotBe(ExplainArg.Of(1000));
    }

    [Fact]
    public void SubjectLink_ForAndAs_RoundTripEveryKind()
    {
        SubjectLink.ForBuilding(new EntityId(11)).AsBuilding().ShouldBe(new EntityId(11));
        SubjectLink.ForGood(new GoodId(2)).AsGood().ShouldBe(new GoodId(2));
        SubjectLink.ForTier(new TierId(1)).AsTier().ShouldBe(new TierId(1));
        SubjectLink.ForBuildingType(new BuildingTypeId(3)).AsBuildingType().ShouldBe(new BuildingTypeId(3));
        SubjectLink.None.Kind.ShouldBe(SubjectKind.None);
    }

    private static ExplanationLine SampleLine() =>
        ExplanationLine.Leaf("explain.good.summary", ExplainArg.Of(Amount.FromUnits(8)), ExplainArg.Of(Amount.FromUnits(12)))
            .WithSubjects(SubjectLink.ForGood(new GoodId(1)))
            .WithChildren(
                ExplanationLine.Leaf("explain.good.producer", ExplainArg.Of(3), ExplainArg.Of(new BuildingTypeId(2)))
                    .WithSubjects(SubjectLink.ForBuildingType(new BuildingTypeId(2)))
                    .WithChildren(ExplanationLine.Leaf("explain.stall", ExplainArg.Of(StallReason.MissingInput))),
                ExplanationLine.Leaf("explain.good.note"));

    [Fact]
    public void Explanation_SameTree_IsEqualInValueAcrossSeparateInstances()
    {
        var first = new Explanation(SampleLine());
        var second = new Explanation(SampleLine());

        first.ShouldNotBeSameAs(second);
        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Explanation_ADifferenceAnywhereInTheTree_IsNotEqual()
    {
        ExplanationLine baseline = SampleLine();
        ExplanationLine deepChange = baseline with
        {
            Children = [baseline.Children[0] with { Children = [ExplanationLine.Leaf("explain.stall", ExplainArg.Of(StallReason.NoWorkforce))] }, baseline.Children[1]],
        };

        new Explanation(baseline).ShouldNotBe(new Explanation(deepChange));
        baseline.ShouldNotBe(baseline with { Key = "explain.other" });
        baseline.ShouldNotBe(baseline with { Subjects = [] });
        baseline.ShouldNotBe(baseline with { Args = [] });
    }

    [Fact]
    public void ExplanationLine_DefaultArrays_CountAsEmpty()
    {
        var withDefaults = new ExplanationLine("k", default, default, default);
        var withEmpty = new ExplanationLine("k", [], [], []);

        withDefaults.ShouldBe(withEmpty);
        withDefaults.GetHashCode().ShouldBe(withEmpty.GetHashCode());
    }

    [Fact]
    public void ExplanationLine_Builders_ReplaceInsteadOfAppend()
    {
        ExplanationLine line = ExplanationLine.Leaf("k").WithChildren(ExplanationLine.Leaf("a")).WithChildren(ExplanationLine.Leaf("b"));

        line.Children.ShouldHaveSingleItem().Key.ShouldBe("b");
        ExplanationLine.Leaf("k", ExplainArg.Of(1), ExplainArg.Of(2)).Args.Length.ShouldBe(2);
        ExplanationLine.Leaf("k").Args.IsEmpty.ShouldBeTrue();
    }

    private static NeedStatus Need(NeedClass needClass, bool met) =>
        new(needClass, new GoodId(0), null, met, met, met ? Ratio.One : Ratio.Zero, met ? NeedUnmetReason.None : NeedUnmetReason.LowFulfilment);
}
