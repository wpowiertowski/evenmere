using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Explain;
using Evenmere.Sim.Queries.Types;
using Evenmere.Content.Definitions;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Explain;

internal static class SliceContent
{
    private static readonly Lazy<ContentDb> _db = new(static () =>
        ContentLoader.Load(Path.Combine(WorldBuilder.RepoRoot, "content")).GetDbOrThrow());

    public static ContentDb Db => _db.Value;
}

/// <summary>
/// A copy of the slice content with two extra goods that make each other, so the cycle guard has a real cycle
/// to stop on. Built once in a temporary directory, leaving the real content untouched.
/// </summary>
internal static class CycleContent
{
    private const string GoodA = "{ \"id\": \"cycle_a\", \"category\": \"intermediate\", \"era\": 1 }";
    private const string GoodB = "{ \"id\": \"cycle_b\", \"category\": \"intermediate\", \"era\": 1 }";
    private const string RecipeA = "{ \"id\": \"make_cycle_a\", \"inputs\": { \"cycle_b\": 1 }, \"outputs\": { \"cycle_a\": 1 }, \"cycleSeconds\": 30 }";
    private const string RecipeB = "{ \"id\": \"make_cycle_b\", \"inputs\": { \"cycle_a\": 1 }, \"outputs\": { \"cycle_b\": 1 }, \"cycleSeconds\": 30 }";
    private const string HutA = "{ \"id\": \"cycle_a_hut\", \"category\": \"production\", \"era\": 1, \"footprint\": [2, 2], \"cost\": { \"timber\": 1 }, \"buildDays\": 1, \"workforce\": { \"tier\": \"settlers\", \"count\": 1 }, \"recipe\": \"make_cycle_a\" }";
    private const string HutB = "{ \"id\": \"cycle_b_hut\", \"category\": \"production\", \"era\": 1, \"footprint\": [2, 2], \"cost\": { \"timber\": 1 }, \"buildDays\": 1, \"workforce\": { \"tier\": \"settlers\", \"count\": 1 }, \"recipe\": \"make_cycle_b\" }";

    private static readonly Lazy<ContentDb> _db = new(Load);

    public static ContentDb Db => _db.Value;

    private static ContentDb Load()
    {
        DirectoryInfo copy = Directory.CreateTempSubdirectory("evenmere-cycle-content-");
        try
        {
            CopyDirectory(Path.Combine(WorldBuilder.RepoRoot, "content"), copy.FullName);
            File.WriteAllText(Path.Combine(copy.FullName, "goods", "cycle_a.json"), GoodA);
            File.WriteAllText(Path.Combine(copy.FullName, "goods", "cycle_b.json"), GoodB);
            File.WriteAllText(Path.Combine(copy.FullName, "recipes", "make_cycle_a.json"), RecipeA);
            File.WriteAllText(Path.Combine(copy.FullName, "recipes", "make_cycle_b.json"), RecipeB);
            File.WriteAllText(Path.Combine(copy.FullName, "buildings", "cycle_a_hut.json"), HutA);
            File.WriteAllText(Path.Combine(copy.FullName, "buildings", "cycle_b_hut.json"), HutB);
            return ContentLoader.Load(copy.FullName).GetDbOrThrow();
        }
        finally
        {
            copy.Delete(recursive: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }
}

public sealed class ExplainGoodTests
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
        int productivity = 1000,
        int fill = 1000,
        bool inputsReady = true,
        int areaYield = 1000,
        int bonus = 0,
        GoodAmount[]? inputs = null,
        GoodAmount[]? outputs = null)
    {
        RecipeId recipeId = content.IdOf<RecipeId>(recipe);
        RecipeDef def = content.Get(recipeId);
        ImmutableArray<GoodAmount> inBuffer = inputs is null
            ? [.. def.Inputs.Select(static need => new GoodAmount(need.Good, Amount.Zero))]
            : [.. inputs];
        ImmutableArray<GoodAmount> outBuffer = outputs is null
            ? [.. def.Outputs.Select(static need => new GoodAmount(need.Good, Amount.Zero))]
            : [.. outputs];

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
            inBuffer,
            outBuffer);
    }

    [Fact]
    public void ExplainGood_SupplyLine_CarriesProducedDemandedStockAndFulfilment()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        GoodId bread = sim.Content.IdOf<GoodId>("bread");

        sim.Queries.Storage.Set(bread, Amount.FromUnits(30));
        sim.Queries.Production.SetProducedYesterday(bread, Amount.FromUnits(8));
        sim.Queries.Needs.SetDemandYesterday(bread, Amount.FromUnits(12));
        sim.Queries.Needs.SetFulfilment(bread, Ratio.FromPerMille(500));

        Explanation explanation = explain.ExplainGood(bread);

        explanation.Root.Key.ShouldBe("explain.good.supply");
        explanation.Root.Args.Length.ShouldBe(5);
        explanation.Root.Args[0].ShouldBe(ExplainArg.Of(bread));
        explanation.Root.Args[1].ShouldBe(ExplainArg.Of(Amount.FromUnits(8)));
        explanation.Root.Args[2].ShouldBe(ExplainArg.Of(Amount.FromUnits(12)));
        explanation.Root.Args[3].ShouldBe(ExplainArg.Of(Amount.FromUnits(30)));
        explanation.Root.Args[4].ShouldBe(ExplainArg.Of(Ratio.FromPerMille(500)));
        explanation.Root.Subjects.ShouldBe([SubjectLink.ForGood(bread)]);
    }

    [Fact]
    public void ExplainGood_ProducerTypes_GroupByTypeWithAverageAndStalls()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        GoodId bread = sim.Content.IdOf<GoodId>("bread");
        BuildingTypeId bakery = sim.Content.IdOf<BuildingTypeId>("bakery");

        EntityId first = sim.Services.BuildingRegistry.Add(bakery, 0, 0, Rotation.Deg0, BuildingState.Active);
        EntityId second = sim.Services.BuildingRegistry.Add(bakery, 4, 0, Rotation.Deg0, BuildingState.Active);

        sim.Queries.Production.SetProducer(Producer(first, sim.Content, "bake_bread", StallReason.None, productivity: 1000));
        sim.Queries.Production.SetProducer(Producer(second, sim.Content, "bake_bread", StallReason.MissingInput, productivity: 0, inputsReady: false));

        Explanation explanation = explain.ExplainGood(bread);

        ExplanationLine producer = explanation.Root.Children.Single(c => c.Key == "explain.good.producer");
        producer.Args[0].ShouldBe(ExplainArg.Of(bakery));
        producer.Args[1].ShouldBe(ExplainArg.Of(2));
        producer.Args[2].ShouldBe(ExplainArg.Of(Ratio.FromPerMille(500)));
        producer.Subjects.ShouldBe([SubjectLink.ForBuildingType(bakery)]);
        producer.Children.ShouldContain(c =>
            c.Key == "explain.good.producer.stall"
            && c.Args[0].Equals(ExplainArg.Of(StallReason.MissingInput))
            && c.Args[1].Equals(ExplainArg.Of(1)));
    }

    [Fact]
    public void ExplainGood_ThreeStepShortage_TracesBreadFlourWorkforceToRoot()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        ContentDb content = sim.Content;

        GoodId bread = content.IdOf<GoodId>("bread");
        GoodId flour = content.IdOf<GoodId>("flour");
        GoodId grain = content.IdOf<GoodId>("grain");
        BuildingTypeId bakeryType = content.IdOf<BuildingTypeId>("bakery");
        BuildingTypeId windmillType = content.IdOf<BuildingTypeId>("windmill");
        TierId settlers = content.IdOf<TierId>("settlers");

        // The bakeries stall on flour; the windmills stall on workers; the island is short of settlers.
        EntityId bakery = sim.Services.BuildingRegistry.Add(bakeryType, 0, 0, Rotation.Deg0, BuildingState.Active);
        EntityId windmill = sim.Services.BuildingRegistry.Add(windmillType, 6, 0, Rotation.Deg0, BuildingState.Active);

        sim.Queries.Production.SetProducer(Producer(
            bakery, content, "bake_bread", StallReason.MissingInput, productivity: 0, fill: 1000, inputsReady: false,
            inputs: [new GoodAmount(flour, Amount.Zero)]));
        sim.Queries.Production.SetProducer(Producer(
            windmill, content, "grind_flour", StallReason.NoWorkforce, productivity: 0, fill: 0,
            inputs: [new GoodAmount(grain, Amount.FromUnits(1))]));

        sim.Queries.Workforce.SetSupply(settlers, 40);
        sim.Queries.Workforce.SetDemand(settlers, 55);

        Explanation explanation = explain.ExplainGood(bread);

        // Bread's causes name flour.
        ExplanationLine flourCause = explanation.Root.Children.Single(c =>
            c.Key == "explain.good.cause.missing_input" && c.Args[0].Equals(ExplainArg.Of(flour)));
        flourCause.Subjects.ShouldBe([SubjectLink.ForGood(flour)]);

        // Flour's own supply line is nested, with windmills as its producers.
        ExplanationLine flourNode = flourCause.Children.Single(n => n.Key == "explain.good.supply" && n.Args[0].Equals(ExplainArg.Of(flour)));
        flourNode.Children.ShouldContain(c =>
            c.Key == "explain.good.producer" && c.Args[0].Equals(ExplainArg.Of(windmillType)));

        // Flour's causes name the workforce root: settlers supply 40 against demand 55.
        ExplanationLine workforce = flourNode.Children.Single(c => c.Key == "explain.good.cause.workforce");
        workforce.Args[0].ShouldBe(ExplainArg.Of(settlers));
        workforce.Args[1].ShouldBe(ExplainArg.Of(40));
        workforce.Args[2].ShouldBe(ExplainArg.Of(55));
        workforce.Subjects.ShouldBe([SubjectLink.ForTier(settlers)]);
    }

    [Fact]
    public void ExplainGood_Cycle_DoesNotExpandAgain()
    {
        // The slice production graph has no cycles, so this stages one in a copy of the content with two
        // goods that make each other: cycle_a needs cycle_b and cycle_b needs cycle_a.
        ContentDb content = CycleContent.Db;
        var sim = FakeSim.Create(content);
        var explain = new ExplainService(content, () => sim.Queries, () => sim.World.Clock);

        GoodId cycleA = content.IdOf<GoodId>("cycle_a");
        GoodId cycleB = content.IdOf<GoodId>("cycle_b");
        BuildingTypeId hutA = content.IdOf<BuildingTypeId>("cycle_a_hut");
        BuildingTypeId hutB = content.IdOf<BuildingTypeId>("cycle_b_hut");

        EntityId first = sim.Services.BuildingRegistry.Add(hutA, 0, 0, Rotation.Deg0, BuildingState.Active);
        EntityId second = sim.Services.BuildingRegistry.Add(hutB, 4, 0, Rotation.Deg0, BuildingState.Active);

        sim.Queries.Production.SetProducer(Producer(
            first, content, "make_cycle_a", StallReason.MissingInput, productivity: 0, inputsReady: false,
            inputs: [new GoodAmount(cycleB, Amount.Zero)],
            outputs: [new GoodAmount(cycleA, Amount.Zero)]));
        sim.Queries.Production.SetProducer(Producer(
            second, content, "make_cycle_b", StallReason.MissingInput, productivity: 0, inputsReady: false,
            inputs: [new GoodAmount(cycleA, Amount.Zero)],
            outputs: [new GoodAmount(cycleB, Amount.Zero)]));

        Explanation explanation = explain.ExplainGood(cycleA);

        // cycle_a names cycle_b as its missing input and nests cycle_b's line once.
        ExplanationLine bCause = explanation.Root.Children.Single(c =>
            c.Key == "explain.good.cause.missing_input" && c.Args[0].Equals(ExplainArg.Of(cycleB)));
        ExplanationLine bNode = bCause.Children.Single(n => n.Key == "explain.good.supply");

        // cycle_b names cycle_a back, but the nested cycle_a cause carries no children: the cycle stops.
        ExplanationLine back = bNode.Children.Single(c =>
            c.Key == "explain.good.cause.missing_input" && c.Args[0].Equals(ExplainArg.Of(cycleA)));
        back.Children.ShouldBeEmpty();
    }

    [Fact]
    public void ExplainGood_UnknownGood_ReturnsUnknownLine()
    {
        FakeSim sim = CreateSim();
        ExplainService explain = Explain(sim);
        var unknown = GoodId.From(checked((ushort)(sim.Content.Goods.Count + 5)));

        Explanation explanation = explain.ExplainGood(unknown);

        explanation.Root.Key.ShouldBe("explain.good.unknown");
    }
}
