using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Explain;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Explain;

/// <summary>ADR-0016: buildings filled from above and low-morale houses explain themselves.</summary>
public sealed class ExplainWorkforceMoraleTests
{
    private static FakeSim CreateSim()
    {
        return FakeSim.Create(SliceContent.Db);
    }

    private static ExplainService Explain(FakeSim sim)
    {
        return new ExplainService(sim.Content, () => sim.Queries, () => sim.World.Clock);
    }

    private static void ArrangeBakery(FakeSim sim, EntityId bakery)
    {
        ContentDb content = sim.Content;
        RecipeId recipe = content.IdOf<RecipeId>("bake_bread");
        RecipeDef def = content.Get(recipe);
        sim.Queries.Production.SetProducer(new ProductionInfo(
            bakery,
            recipe,
            StallReason.None,
            Ratio.One,
            Ratio.FromPerMille(1000),
            true,
            Ratio.FromPerMille(1000),
            0,
            Ratio.FromPerMille(1000),
            [.. def.Inputs.Select(static need => new GoodAmount(need.Good, Amount.FromUnits(1)))],
            [.. def.Outputs.Select(static need => new GoodAmount(need.Good, Amount.FromUnits(1)))]));
        sim.Queries.Roads.GrantRoadAccess(bakery);
    }

    [Fact]
    public void ExplainBuilding_SubstitutedWorkforce_NamesBothTiers()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        ContentDb content = sim.Content;

        // A bakery asks for four settlers; three work it and one villager steps down.
        BuildingTypeId bakeryType = content.IdOf<BuildingTypeId>("bakery");
        EntityId bakery = sim.Services.BuildingRegistry.Add(bakeryType, 0, 0, Rotation.Deg0, BuildingState.Active);
        ArrangeBakery(sim, bakery);
        TierId settlers = content.IdOf<TierId>("settlers");
        TierId villagers = content.IdOf<TierId>("villagers");
        sim.Queries.Workforce.SetAllocated(bakery, 4);
        sim.Queries.Workforce.SetSubstituted(bakery, 1, villagers);

        Explanation explanation = explain.ExplainBuilding(bakery);

        ExplanationLine line = explanation.Root.Children.Single(c => c.Key == "explain.building.workforce.substituted");
        line.Args.ShouldBe([ExplainArg.Of(settlers), ExplainArg.Of(3), ExplainArg.Of(villagers), ExplainArg.Of(1)]);
        line.Subjects.ShouldBe([SubjectLink.ForTier(settlers), SubjectLink.ForTier(villagers)]);
    }

    [Fact]
    public void ExplainBuilding_FullyOwnWorkforce_ShowsNoSubstitutionLine()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        BuildingTypeId bakeryType = sim.Content.IdOf<BuildingTypeId>("bakery");
        EntityId bakery = sim.Services.BuildingRegistry.Add(bakeryType, 0, 0, Rotation.Deg0, BuildingState.Active);
        ArrangeBakery(sim, bakery);
        sim.Queries.Workforce.SetAllocated(bakery, 4);

        Explanation explanation = explain.ExplainBuilding(bakery);

        explanation.Root.Children.ShouldNotContain(c => c.Key == "explain.building.workforce.substituted");
    }

    [Fact]
    public void ExplainHouse_LowMoraleWithAnotherBlocker_ShowsTheMoraleLine()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        ContentDb content = sim.Content;

        // A villager house with three of six residents below their tier, held by the locked next tier.
        TierId settlers = content.IdOf<TierId>("settlers");
        TierId villagers = content.IdOf<TierId>("villagers");
        EntityId house = sim.Services.BuildingRegistry.Add(content.IdOf<BuildingTypeId>("settler_house"), 0, 0, Rotation.Deg0, BuildingState.Active);
        sim.Queries.Population.SetHouse(new HouseInfo(house, villagers, 6, 6, 10, 6, 0, false, false, AdvanceBlocker.NextTierLocked, 500));
        sim.Queries.Needs.SetHouseNeeds(new HouseNeedsInfo(house, true, []));

        Explanation explanation = explain.ExplainHouse(house);

        ExplanationLine line = explanation.Root.Children.Single(c => c.Key == "explain.house.morale");
        line.Args.ShouldBe([ExplainArg.Of(50), ExplainArg.Of(3), ExplainArg.Of(settlers)]);
        line.Subjects.ShouldBe([SubjectLink.ForTier(settlers)]);
    }

    [Fact]
    public void ExplainHouse_UnderemployedBlocker_ReusesTheMoraleLineOnce()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        ContentDb content = sim.Content;

        TierId villagers = content.IdOf<TierId>("villagers");
        EntityId house = sim.Services.BuildingRegistry.Add(content.IdOf<BuildingTypeId>("settler_house"), 0, 0, Rotation.Deg0, BuildingState.Active);
        sim.Queries.Population.SetHouse(new HouseInfo(house, villagers, 6, 6, 10, 6, 0, false, false, AdvanceBlocker.Underemployed, 500));
        sim.Queries.Needs.SetHouseNeeds(new HouseNeedsInfo(house, true, []));

        Explanation explanation = explain.ExplainHouse(house);

        // The advance section carries the morale line as the blocker; no second copy follows.
        ExplanationLine advance = explanation.Root.Children.Single(c => c.Key == "explain.house.advance");
        advance.Children.ShouldHaveSingleItem().Key.ShouldBe("explain.house.morale");
        explanation.Root.Children.Count(c => c.Key == "explain.house.morale").ShouldBe(0);
    }

    [Fact]
    public void ExplainHouse_FullMorale_ShowsNoMoraleLine()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        ContentDb content = sim.Content;

        TierId settlers = content.IdOf<TierId>("settlers");
        EntityId house = sim.Services.BuildingRegistry.Add(content.IdOf<BuildingTypeId>("settler_house"), 0, 0, Rotation.Deg0, BuildingState.Active);
        sim.Queries.Population.SetHouse(new HouseInfo(house, settlers, 6, 6, 6, 6, 0, false, false, AdvanceBlocker.None, 1000));
        sim.Queries.Needs.SetHouseNeeds(new HouseNeedsInfo(house, true, []));

        Explanation explanation = explain.ExplainHouse(house);

        explanation.Root.Children.ShouldNotContain(c => c.Key == "explain.house.morale");
    }
}
