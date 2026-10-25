using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Explain;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Explain;

/// <summary>M1-38: the capped occupancy line names the lower tier and every missing basic need.</summary>
public sealed class ExplainOccupancyCapTests
{
    private static FakeSim CreateSim() => FakeSim.Create(SliceContent.Db);

    private static ExplainService Explain(FakeSim sim) => new(sim.Content, () => sim.Queries, () => sim.World.Clock);

    private static void ArrangeVillagerHouse(FakeSim sim, EntityId house, int residents = 6, int target = 6)
    {
        ContentDb content = sim.Content;
        TierId villagers = content.IdOf<TierId>("villagers");
        sim.Queries.Population.SetHouse(new HouseInfo(
            house, villagers, residents, target, 10, 0, 0, false, false, AdvanceBlocker.NeedsUnmet, 1000));

        GoodId fish = content.IdOf<GoodId>("fish");
        GoodId homespun = content.IdOf<GoodId>("homespun");
        GoodId bread = content.IdOf<GoodId>("bread");
        var needs = new NeedStatus[]
        {
            new(NeedClass.Basic, fish, null, true, true, Ratio.FromPerMille(900), NeedUnmetReason.None),
            new(NeedClass.Basic, homespun, null, false, true, Ratio.FromPerMille(100), NeedUnmetReason.LowFulfilment),
            new(NeedClass.Basic, bread, null, true, true, Ratio.FromPerMille(900), NeedUnmetReason.None),
            new(NeedClass.Basic, null, ServiceKind.Market, true, true, Ratio.One, NeedUnmetReason.None),
            new(NeedClass.Basic, null, ServiceKind.Shrine, true, true, Ratio.One, NeedUnmetReason.None),
        };
        sim.Queries.Needs.SetHouseNeeds(new HouseNeedsInfo(house, true, [.. needs]));
    }

    [Fact]
    public void ExplainHouse_CappedOccupancy_NamesLowerTierAndMissingNeeds()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        ContentDb content = sim.Content;

        BuildingTypeId houseType = content.IdOf<BuildingTypeId>("settler_house");
        EntityId house = sim.Services.BuildingRegistry.Add(houseType, 0, 0, Rotation.Deg0, BuildingState.Active);
        ArrangeVillagerHouse(sim, house);

        Explanation explanation = explain.ExplainHouse(house);

        ExplanationLine occupancy = explanation.Root.Children.Single(c => c.Key == "explain.house.occupancy.capped");
        occupancy.Args.ShouldContain(ExplainArg.Of(content.IdOf<TierId>("villagers")));
        occupancy.Args.ShouldContain(ExplainArg.Of(content.IdOf<TierId>("settlers")));
        occupancy.Subjects.ShouldContain(SubjectLink.ForGood(content.IdOf<GoodId>("homespun")));
        occupancy.Children.ShouldContain(c =>
            c.Key == "explain.house.occupancy.capped.missing.good" && c.Args[0].Equals(ExplainArg.Of(content.IdOf<GoodId>("homespun"))));
    }

    [Fact]
    public void ExplainHouse_AllNeedsMet_KeepsPlainOccupancyLine()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        ContentDb content = sim.Content;

        BuildingTypeId houseType = content.IdOf<BuildingTypeId>("settler_house");
        EntityId house = sim.Services.BuildingRegistry.Add(houseType, 0, 0, Rotation.Deg0, BuildingState.Active);

        TierId villagers = content.IdOf<TierId>("villagers");
        sim.Queries.Population.SetHouse(new HouseInfo(
            house, villagers, 10, 10, 10, 6, 0, false, false, AdvanceBlocker.NoNextTier, 1000));
        GoodId fish = content.IdOf<GoodId>("fish");
        GoodId homespun = content.IdOf<GoodId>("homespun");
        GoodId bread = content.IdOf<GoodId>("bread");
        sim.Queries.Needs.SetHouseNeeds(new HouseNeedsInfo(
            house,
            true,
            [
                new NeedStatus(NeedClass.Basic, fish, null, true, true, Ratio.One, NeedUnmetReason.None),
                new NeedStatus(NeedClass.Basic, homespun, null, true, true, Ratio.One, NeedUnmetReason.None),
                new NeedStatus(NeedClass.Basic, bread, null, true, true, Ratio.One, NeedUnmetReason.None),
                new NeedStatus(NeedClass.Basic, null, ServiceKind.Market, true, true, Ratio.One, NeedUnmetReason.None),
                new NeedStatus(NeedClass.Basic, null, ServiceKind.Shrine, true, true, Ratio.One, NeedUnmetReason.None),
            ]));

        Explanation explanation = explain.ExplainHouse(house);

        explanation.Root.Children.ShouldContain(c => c.Key == "explain.house.occupancy");
        explanation.Root.Children.ShouldNotContain(c => c.Key == "explain.house.occupancy.capped");
    }
}
