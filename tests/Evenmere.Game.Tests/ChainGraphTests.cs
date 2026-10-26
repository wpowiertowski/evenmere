using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Game.Ui.Chains;

namespace Evenmere.Game.Tests;

/// <summary>The bread chain builds from content recipes alone: no simulation surface is involved.</summary>
public sealed class ChainGraphTests
{
    private static ContentDb Db => TestContent.Db;

    [Fact]
    public void BuildBread_UpstreamGoodsAndDepths()
    {
        GoodId bread = Db.Goods.IdOf("bread");
        GoodId flour = Db.Goods.IdOf("flour");
        GoodId grain = Db.Goods.IdOf("grain");

        var graph = ChainGraph.Build(Db, bread);

        graph.Root.ShouldBe(bread);
        graph.Goods.Select(static n => n.Good).ShouldBe([bread, flour, grain]);
        Node(graph, grain).Depth.ShouldBe(0);
        Node(graph, flour).Depth.ShouldBe(1);
        Node(graph, bread).Depth.ShouldBe(2);
        // The linear chain hangs in a single column: the root first, the raw input last.
        graph.Goods.Select(static n => (n.Row, n.Column)).ShouldBe([(0, 0), (1, 0), (2, 0)]);
    }

    [Fact]
    public void BuildBread_EdgesLinkInputsToOutputs()
    {
        GoodId bread = Db.Goods.IdOf("bread");
        GoodId flour = Db.Goods.IdOf("flour");
        GoodId grain = Db.Goods.IdOf("grain");
        RecipeId bake = Db.Recipes.IdOf("bake_bread");
        RecipeId grind = Db.Recipes.IdOf("grind_flour");

        var graph = ChainGraph.Build(Db, bread);

        graph.Edges.ShouldBe(
        [
            new ChainEdge(flour, bread, bake),
            new ChainEdge(grain, flour, grind),
        ]);
    }

    [Fact]
    public void BuildBread_RecipesNameProducers()
    {
        GoodId bread = Db.Goods.IdOf("bread");
        RecipeId bake = Db.Recipes.IdOf("bake_bread");
        RecipeId grind = Db.Recipes.IdOf("grind_flour");
        RecipeId grow = Db.Recipes.IdOf("grow_grain");
        BuildingTypeId bakery = Db.Buildings.IdOf("bakery");

        var graph = ChainGraph.Build(Db, bread);

        Node(graph, bread).ProducedBy.ShouldBe([bake]);
        Node(graph, Db.Goods.IdOf("flour")).ProducedBy.ShouldBe([grind]);
        Node(graph, Db.Goods.IdOf("grain")).ProducedBy.ShouldBe([grow]);
        graph.Recipes.Select(static r => r.Recipe).ShouldBe([bake, grind, grow]);
        graph.Recipes.Single(r => r.Recipe == bake).Buildings.ShouldContain(bakery);
    }

    [Fact]
    public void BuildBread_LayoutRunsRootFirst()
    {
        var graph = ChainGraph.Build(Db, Db.Goods.IdOf("bread"));

        // The root is the first row; the raw inputs hang below it, so the selected good
        // is visible without scrolling in the narrow docked panel.
        graph.Goods.Select(static n => (n.Row, n.Column)).ShouldBe([(0, 0), (1, 0), (2, 0)]);
    }

    [Fact]
    public void BuildEveryGood_GraphIsClosedAndDepthsDescend()
    {
        for (int g = 0; g < Db.Goods.Count; g++)
        {
            var root = GoodId.From(checked((ushort)g));
            var graph = ChainGraph.Build(Db, root);

            var goods = new HashSet<GoodId>(graph.Goods.Select(static n => n.Good));
            goods.ShouldContain(root);
            foreach (ChainEdge edge in graph.Edges)
            {
                goods.ShouldContain(edge.From);
                goods.ShouldContain(edge.To);
                Depth(graph, edge.From).ShouldBeLessThan(Depth(graph, edge.To));
            }
        }
    }

    [Fact]
    public void BuildTwice_SameGraph()
    {
        // Nodes hold ImmutableArrays, which compare by reference, so determinism is checked field by field.
        GoodId bread = Db.Goods.IdOf("bread");
        var first = ChainGraph.Build(Db, bread);
        var second = ChainGraph.Build(Db, bread);

        first.Root.ShouldBe(second.Root);
        first.Goods.Length.ShouldBe(second.Goods.Length);
        for (int i = 0; i < first.Goods.Length; i++)
        {
            first.Goods[i].Good.ShouldBe(second.Goods[i].Good);
            first.Goods[i].ProducedBy.ShouldBe(second.Goods[i].ProducedBy);
            first.Goods[i].Depth.ShouldBe(second.Goods[i].Depth);
            first.Goods[i].Column.ShouldBe(second.Goods[i].Column);
            first.Goods[i].Row.ShouldBe(second.Goods[i].Row);
        }

        first.Edges.ShouldBe(second.Edges);
        first.Recipes.Length.ShouldBe(second.Recipes.Length);
        for (int i = 0; i < first.Recipes.Length; i++)
        {
            first.Recipes[i].Recipe.ShouldBe(second.Recipes[i].Recipe);
            first.Recipes[i].Buildings.ShouldBe(second.Recipes[i].Buildings);
        }
    }

    private static ChainGoodNode Node(ChainGraph graph, GoodId good) =>
        graph.Goods.Single(n => n.Good == good);

    private static int Depth(ChainGraph graph, GoodId good) => Node(graph, good).Depth;
}
