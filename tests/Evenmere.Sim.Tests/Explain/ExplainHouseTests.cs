using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Explain;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Explain;

public sealed class ExplainHouseTests
{
    private static FakeSim CreateSim()
    {
        return FakeSim.Create(SliceContent.Db);
    }

    private static ExplainService Explain(FakeSim sim)
    {
        return new ExplainService(sim.Content, () => sim.Queries, () => sim.World.Clock);
    }

    private static void ArrangeSettlerHouse(FakeSim sim, EntityId house, bool marketCovered, int residents = 4)
    {
        ContentDb content = sim.Content;
        TierId settlers = content.IdOf<TierId>("settlers");
        GoodId fish = content.IdOf<GoodId>("fish");
        GoodId wood = content.IdOf<GoodId>("wood");

        sim.Queries.Population.SetHouse(new HouseInfo(
            house,
            settlers,
            residents,
            residents,
            6,
            2,
            0,
            false,
            false,
            AdvanceBlocker.NeedsUnmet));

        NeedStatus[] needs = marketCovered
            ? new NeedStatus[]
            {
                new(NeedClass.Basic, fish, null, true, true, Ratio.FromPerMille(800), NeedUnmetReason.None),
                new(NeedClass.Basic, wood, null, false, true, Ratio.FromPerMille(200), NeedUnmetReason.LowFulfilment),
                new(NeedClass.Basic, null, ServiceKind.Market, true, true, Ratio.One, NeedUnmetReason.None),
            }
            : new NeedStatus[]
            {
                new(NeedClass.Basic, fish, null, false, true, Ratio.FromPerMille(800), NeedUnmetReason.NoMarketCoverage),
                new(NeedClass.Basic, wood, null, false, true, Ratio.FromPerMille(200), NeedUnmetReason.NoMarketCoverage),
                new(NeedClass.Basic, null, ServiceKind.Market, false, true, Ratio.Zero, NeedUnmetReason.ServiceNotCovered),
            };
        sim.Queries.Needs.SetHouseNeeds(new HouseNeedsInfo(house, marketCovered, [.. needs]));
        sim.Queries.Needs.SetFulfilment(fish, Ratio.FromPerMille(800));
        sim.Queries.Needs.SetFulfilment(wood, Ratio.FromPerMille(200));
    }

    [Fact]
    public void ExplainHouse_Needs_CarryMetAndReason()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        ContentDb content = sim.Content;

        TierId settlers = content.IdOf<TierId>("settlers");
        BuildingTypeId houseType = content.IdOf<BuildingTypeId>("settler_house");
        GoodId fish = content.IdOf<GoodId>("fish");
        GoodId wood = content.IdOf<GoodId>("wood");

        EntityId house = sim.Services.BuildingRegistry.Add(houseType, 0, 0, Rotation.Deg0, BuildingState.Active);
        ArrangeSettlerHouse(sim, house, marketCovered: true);

        Explanation explanation = explain.ExplainHouse(house);

        explanation.Root.Key.ShouldBe("explain.house.status");
        explanation.Root.Args[0].ShouldBe(ExplainArg.Of(settlers));

        explanation.Root.Children.ShouldContain(c =>
            c.Key == "explain.house.need.good.met" && c.Args[0].Equals(ExplainArg.Of(fish)));
        explanation.Root.Children.ShouldContain(c =>
            c.Key == "explain.house.need.good.shortage" && c.Args[0].Equals(ExplainArg.Of(wood)));
        explanation.Root.Children.ShouldContain(c => c.Key == "explain.house.need.service.met");
        explanation.Root.Children.ShouldContain(c => c.Key == "explain.house.occupancy");
        explanation.Root.Children.ShouldContain(c => c.Key == "explain.house.advance");
        explanation.Root.Children.ShouldContain(c => c.Key == "explain.house.decline");
    }

    [Fact]
    public void ExplainHouse_NoMarket_MarksGoodsWithNoMarket()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);

        BuildingTypeId houseType = sim.Content.IdOf<BuildingTypeId>("settler_house");
        EntityId house = sim.Services.BuildingRegistry.Add(houseType, 0, 0, Rotation.Deg0, BuildingState.Active);
        ArrangeSettlerHouse(sim, house, marketCovered: false);

        Explanation explanation = explain.ExplainHouse(house);

        explanation.Root.Children.ShouldContain(c => c.Key == "explain.house.need.good.no_market");
        explanation.Root.Children.ShouldContain(c => c.Key == "explain.house.need.service.missing");
    }

    [Fact]
    public void ExplainHouse_AdvanceAndDecline_ShowMathsAndBlocker()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        ContentDb content = sim.Content;

        TierId settlers = content.IdOf<TierId>("settlers");
        BuildingTypeId houseType = content.IdOf<BuildingTypeId>("settler_house");
        EntityId house = sim.Services.BuildingRegistry.Add(houseType, 0, 0, Rotation.Deg0, BuildingState.Active);

        sim.Queries.Population.SetHouse(new HouseInfo(
            house, settlers, 4, 4, 6, 2, 7, false, false, AdvanceBlocker.MissingMaterials));
        sim.Queries.Needs.SetHouseNeeds(new HouseNeedsInfo(
            house,
            true,
            [
                new NeedStatus(NeedClass.Basic, content.IdOf<GoodId>("fish"), null, true, true, Ratio.FromPerMille(900), NeedUnmetReason.None),
                new NeedStatus(NeedClass.Basic, content.IdOf<GoodId>("wood"), null, false, true, Ratio.FromPerMille(100), NeedUnmetReason.LowFulfilment),
                new NeedStatus(NeedClass.Basic, null, ServiceKind.Market, true, true, Ratio.One, NeedUnmetReason.None),
            ]));

        GoodId timber = content.IdOf<GoodId>("timber");
        sim.Queries.Storage.Set(timber, Amount.FromUnits(1));

        Explanation explanation = explain.ExplainHouse(house);

        ExplanationLine advance = explanation.Root.Children.Single(c => c.Key == "explain.house.advance");
        advance.Args[0].ShouldBe(ExplainArg.Of(2));
        advance.Args[1].ShouldBe(ExplainArg.Of(content.Rules.AdvanceStreakDays));
        ExplanationLine blocker = advance.Children.Single();
        blocker.Key.ShouldBe("explain.house.blocker.materials");
        blocker.Children.ShouldContain(c =>
            c.Key == "explain.house.blocker.material" && c.Args[0].Equals(ExplainArg.Of(timber)));

        ExplanationLine decline = explanation.Root.Children.Single(c => c.Key == "explain.house.decline");
        decline.Args[0].ShouldBe(ExplainArg.Of(7));
        decline.Args[1].ShouldBe(ExplainArg.Of(content.Rules.DeclineGraceDays));
        decline.Children.ShouldContain(c => c.Key == "explain.house.decline.need.good");
    }

    [Fact]
    public void ExplainHouse_HeldForWorkforce_NamesTierDemandAndSpare()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        ContentDb content = sim.Content;

        TierId settlers = content.IdOf<TierId>("settlers");
        BuildingTypeId houseType = content.IdOf<BuildingTypeId>("settler_house");
        EntityId house = sim.Services.BuildingRegistry.Add(houseType, 0, 0, Rotation.Deg0, BuildingState.Active);

        sim.Queries.Population.SetHouse(new HouseInfo(
            house, settlers, 6, 6, 6, 3, 0, false, false, AdvanceBlocker.HeldForWorkforce));
        sim.Queries.Needs.SetHouseNeeds(new HouseNeedsInfo(
            house,
            true,
            [
                new NeedStatus(NeedClass.Basic, content.IdOf<GoodId>("fish"), null, true, true, Ratio.FromPerMille(900), NeedUnmetReason.None),
                new NeedStatus(NeedClass.Basic, content.IdOf<GoodId>("wood"), null, true, true, Ratio.FromPerMille(900), NeedUnmetReason.None),
                new NeedStatus(NeedClass.Basic, null, ServiceKind.Market, true, true, Ratio.One, NeedUnmetReason.None),
            ]));
        sim.Queries.Population.SetResidents(settlers, 10);
        sim.Queries.Workforce.SetDemand(settlers, 7);

        Explanation explanation = explain.ExplainHouse(house);

        ExplanationLine advance = explanation.Root.Children.Single(c => c.Key == "explain.house.advance");
        ExplanationLine blocker = advance.Children.Single();
        blocker.Key.ShouldBe("explain.house.blocker.held_workforce");
        blocker.Args.ShouldBe([ExplainArg.Of(settlers), ExplainArg.Of(7), ExplainArg.Of(4)]);
    }

    [Fact]
    public void ExplainHouse_Unknown_ReturnsUnknownLine()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);

        Explanation explanation = explain.ExplainHouse(new EntityId(9999));

        explanation.Root.Key.ShouldBe("explain.house.unknown");
    }
}
