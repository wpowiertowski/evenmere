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

public sealed class ExplainBuildingTests
{
    private static FakeSim CreateSim()
    {
        return FakeSim.Create(SliceContent.Db);
    }

    private static ExplainService Explain(FakeSim sim)
    {
        return new ExplainService(sim.Content, () => sim.Queries, () => sim.World.Clock);
    }

    private static ProductionInfo Producer(
        EntityId building,
        ContentDb content,
        string recipe,
        StallReason stall,
        int productivity = 800,
        int fill = 1000,
        bool inputsReady = true,
        int areaYield = 1000,
        int bonus = 25)
    {
        RecipeId recipeId = content.IdOf<RecipeId>(recipe);
        RecipeDef def = content.Get(recipeId);
        return new ProductionInfo(
            building,
            recipeId,
            stall,
            Ratio.FromPerMille(productivity),
            Ratio.FromPerMille(fill),
            inputsReady,
            Ratio.FromPerMille(areaYield),
            bonus,
            Ratio.FromPerMille(500),
            [.. def.Inputs.Select(static need => new GoodAmount(need.Good, Amount.FromUnits(1)))],
            [.. def.Outputs.Select(static need => new GoodAmount(need.Good, Amount.FromUnits(1)))]);
    }

    [Fact]
    public void ExplainBuilding_Producer_BreaksDownProductivityStallBuffersAndReach()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        ContentDb content = sim.Content;

        BuildingTypeId bakeryType = content.IdOf<BuildingTypeId>("bakery");
        EntityId bakery = sim.Services.BuildingRegistry.Add(bakeryType, 0, 0, Rotation.Deg0, BuildingState.Active);
        EntityId storehouse = sim.Services.BuildingRegistry.Add(content.IdOf<BuildingTypeId>("storehouse"), 10, 0, Rotation.Deg0, BuildingState.Active);

        sim.Queries.Production.SetProducer(Producer(bakery, content, "bake_bread", StallReason.MissingInput, productivity: 0, inputsReady: false));
        sim.Queries.Roads.GrantRoadAccess(bakery);
        sim.Queries.Roads.SetReach(storehouse, bakery, 3000);

        TechId weaving = content.IdOf<TechId>("weaving");
        sim.Queries.Progression.AddModifier(
            Stat.Productivity,
            new ModifierContribution(EffectSource.ForTech(weaving), 25, true));

        Explanation explanation = explain.ExplainBuilding(bakery);

        explanation.Root.Key.ShouldBe("explain.building.status");
        explanation.Root.Args[0].ShouldBe(ExplainArg.Of(bakeryType));
        explanation.Root.Args[1].ShouldBe(ExplainArg.Of(StallReason.MissingInput));

        ExplanationLine productivity = explanation.Root.Children.Single(c => c.Key == "explain.building.productivity");
        productivity.Children.ShouldContain(c => c.Key == "explain.building.workforce");
        productivity.Children.ShouldContain(c => c.Key == "explain.building.inputs.missing");
        productivity.Children.ShouldContain(c => c.Key == "explain.building.area_yield");
        productivity.Children.ShouldContain(c =>
            c.Key == "explain.building.modifier" && c.Args[1].Equals(ExplainArg.Of(25)));
        productivity.Children.ShouldContain(c => c.Key == "explain.building.site_bonus");
        productivity.Children.ShouldContain(c => c.Key == "explain.building.season");

        ExplanationLine stall = explanation.Root.Children.Single(c => c.Key == "explain.building.stall");
        stall.Children.Single().Key.ShouldBe("explain.fix.missing_input");

        explanation.Root.Children.ShouldContain(c => c.Key == "explain.building.input");
        explanation.Root.Children.ShouldContain(c => c.Key == "explain.building.output");

        ExplanationLine reach = explanation.Root.Children.Single(c => c.Key == "explain.building.reach");
        reach.Args[0].ShouldBe(ExplainArg.Of(storehouse));
        reach.Args[1].ShouldBe(ExplainArg.Of(3000));
    }

    [Fact]
    public void ExplainBuilding_Site_ShowsProgressWaitingForAndReach()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        ContentDb content = sim.Content;

        BuildingTypeId bakeryType = content.IdOf<BuildingTypeId>("bakery");
        GoodId timber = content.IdOf<GoodId>("timber");
        EntityId site = sim.Services.BuildingRegistry.Add(bakeryType, 0, 0, Rotation.Deg0, BuildingState.Site);
        EntityId storehouse = sim.Services.BuildingRegistry.Add(content.IdOf<BuildingTypeId>("storehouse"), 10, 0, Rotation.Deg0, BuildingState.Active);

        sim.Queries.Construction.SetSite(new SiteInfo(site, 3, 10, Ratio.FromPerMille(300), StallReason.MissingMaterial, timber));
        sim.Queries.Roads.SetReach(storehouse, site, 2000);

        Explanation explanation = explain.ExplainBuilding(site);

        explanation.Root.Key.ShouldBe("explain.building.site");
        explanation.Root.Args[0].ShouldBe(ExplainArg.Of(bakeryType));
        explanation.Root.Args[1].ShouldBe(ExplainArg.Of(3));

        explanation.Root.Children.ShouldContain(c =>
            c.Key == "explain.building.waiting_for" && c.Args[0].Equals(ExplainArg.Of(timber)));
        explanation.Root.Children.ShouldContain(c => c.Key == "explain.building.reach");
    }

    [Fact]
    public void ExplainBuilding_Unknown_ReturnsUnknownLine()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);

        Explanation explanation = explain.ExplainBuilding(new EntityId(9999));

        explanation.Root.Key.ShouldBe("explain.building.unknown");
    }
}
