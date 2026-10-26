using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;

namespace Evenmere.Game.Ui.Chains;

/// <summary>One good in a production chain graph: the good, the recipes that produce it, and its grid cell.
/// Rows run from the selected root (row 0) down to the raw inputs; columns spread the goods of one depth
/// in ascending ID order.</summary>
/// <param name="Good">The good.</param>
/// <param name="ProducedBy">The recipes that produce the good, in ascending recipe-ID order; empty for a raw input.</param>
/// <param name="Depth">How many recipe steps the longest path from a raw input takes; 0 for a good no recipe produces.</param>
/// <param name="Column">The grid column within the depth, in ascending good-ID order.</param>
/// <param name="Row">The grid row from the root downward.</param>
public sealed record ChainGoodNode(
    GoodId Good,
    ImmutableArray<RecipeId> ProducedBy,
    int Depth,
    int Column,
    int Row);

/// <summary>One link in a production chain graph: an input good flows into an output good through a recipe.</summary>
/// <param name="From">The input good.</param>
/// <param name="To">The output good.</param>
/// <param name="Via">The recipe that consumes <see cref="From"/> and produces <see cref="To"/>.</param>
public sealed record ChainEdge(GoodId From, GoodId To, RecipeId Via);

/// <summary>One recipe used by a production chain graph, with the building types that run it.</summary>
/// <param name="Recipe">The recipe.</param>
/// <param name="Buildings">The building types whose definition runs the recipe, in ascending ID order.</param>
public sealed record ChainRecipeUse(RecipeId Recipe, ImmutableArray<BuildingTypeId> Buildings);

/// <summary>
/// The upstream production graph of one good, built only from <see cref="ContentDb"/> recipes (M2-21 adds no sim
/// surface). Starting at the root, every recipe that produces an included good is followed to its inputs until goods
/// no recipe produces (the raw inputs). The graph is a DAG over content, so it is identical for every world on the
/// same content pack; live numbers are annotated later by <see cref="ChainViewModel"/>.
/// </summary>
/// <param name="Root">The selected good the graph was built for.</param>
/// <param name="Goods">Every good in the graph, ordered by (row, column).</param>
/// <param name="Edges">Every input-to-output link, ordered by (output, input, recipe).</param>
/// <param name="Recipes">Every recipe used by the graph, in ascending recipe-ID order.</param>
public sealed record ChainGraph(
    GoodId Root,
    ImmutableArray<ChainGoodNode> Goods,
    ImmutableArray<ChainEdge> Edges,
    ImmutableArray<ChainRecipeUse> Recipes)
{
    /// <summary>Builds the upstream graph of a good from the content recipes.</summary>
    /// <param name="content">The content database, which names every recipe's inputs and outputs.</param>
    /// <param name="root">The good to explain.</param>
    /// <returns>The graph; a good no recipe produces gives a single node with no edges.</returns>
    public static ChainGraph Build(ContentDb content, GoodId root)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Index the recipes both ways. Counts are small (a dozen recipes in Era I), so plain arrays do.
        var producedBy = new List<RecipeId>[content.Goods.Count];
        for (int i = 0; i < producedBy.Length; i++)
        {
            producedBy[i] = new List<RecipeId>(1);
        }

        var inputsOf = new ImmutableArray<GoodId>[content.Recipes.Count];
        for (int r = 0; r < content.Recipes.Count; r++)
        {
            var recipe = RecipeId.From(checked((ushort)r));
            RecipeDef def = content.Get(recipe);
            var inputs = new List<GoodId>(def.Inputs.Length);
            foreach (GoodAmount input in def.Inputs)
            {
                inputs.Add(input.Good);
            }

            inputsOf[r] = [.. inputs];
            foreach (GoodAmount output in def.Outputs)
            {
                producedBy[output.Good.Value].Add(recipe);
            }
        }

        // Walk upstream from the root in ascending ID order, so the same content always gives the same graph.
        bool[] included = new bool[content.Goods.Count];
        var order = new List<GoodId> { root };
        included[root.Value] = true;
        for (int cursor = 0; cursor < order.Count; cursor++)
        {
            GoodId current = order[cursor];
            foreach (RecipeId recipe in producedBy[current.Value])
            {
                foreach (GoodId input in inputsOf[recipe.Value])
                {
                    if (!included[input.Value])
                    {
                        included[input.Value] = true;
                        order.Add(input);
                    }
                }
            }
        }

        order.Sort(static (a, b) => a.Value.CompareTo(b.Value));

        // Depth is the longest path from a raw input: 0 when no recipe produces the good. A recipe cycle would
        // recurse forever, so an in-progress mark breaks it (content is a DAG; the guard only fires on bad data).
        int[] depths = new int[content.Goods.Count];
        bool[] done = new bool[content.Goods.Count];
        bool[] busy = new bool[content.Goods.Count];
        foreach (GoodId good in order)
        {
            depths[good.Value] = DepthOf(producedBy, inputsOf, included, depths, done, busy, good);
        }

        var recipeSet = new List<RecipeId>();
        foreach (GoodId good in order)
        {
            foreach (RecipeId recipe in producedBy[good.Value])
            {
                if (!recipeSet.Contains(recipe))
                {
                    recipeSet.Add(recipe);
                }
            }
        }

        recipeSet.Sort(static (a, b) => a.Value.CompareTo(b.Value));

        var edges = new List<ChainEdge>();
        foreach (RecipeId recipe in recipeSet)
        {
            RecipeDef def = content.Get(recipe);
            foreach (GoodAmount output in def.Outputs)
            {
                if (!included[output.Good.Value])
                {
                    continue;
                }

                foreach (GoodId input in inputsOf[recipe.Value])
                {
                    edges.Add(new ChainEdge(input, output.Good, recipe));
                }
            }
        }

        edges.Sort(static (a, b) =>
        {
            int order = a.To.Value.CompareTo(b.To.Value);
            if (order != 0)
            {
                return order;
            }

            order = a.From.Value.CompareTo(b.From.Value);
            return order != 0 ? order : a.Via.Value.CompareTo(b.Via.Value);
        });

        // Buildings that run each used recipe, in ascending type order.
        var uses = new List<ChainRecipeUse>(recipeSet.Count);
        foreach (RecipeId recipe in recipeSet)
        {
            var runners = new List<BuildingTypeId>();
            for (int b = 0; b < content.Buildings.Count; b++)
            {
                var type = BuildingTypeId.From(checked((ushort)b));
                if (content.Get(type).Recipe == recipe)
                {
                    runners.Add(type);
                }
            }

            uses.Add(new ChainRecipeUse(recipe, [.. runners]));
        }

        // Grid layout: rows from the root (row 0) down to the raw inputs, columns by ID within a depth.
        // Nodes are ordered by (row, column).
        int maxDepth = 0;
        foreach (GoodId good in order)
        {
            maxDepth = Math.Max(maxDepth, depths[good.Value]);
        }

        var columnInDepth = new Dictionary<int, int>();
        var nodes = new List<ChainGoodNode>(order.Count);
        foreach (GoodId good in order)
        {
            int depth = depths[good.Value];
            int column = columnInDepth.TryGetValue(depth, out int used) ? used : 0;
            columnInDepth[depth] = column + 1;
            nodes.Add(new ChainGoodNode(good, [.. producedBy[good.Value]], depth, column, maxDepth - depth));
        }

        nodes.Sort(static (a, b) =>
        {
            int row = a.Row.CompareTo(b.Row);
            return row != 0 ? row : a.Column.CompareTo(b.Column);
        });

        return new ChainGraph(root, [.. nodes], [.. edges], [.. uses]);
    }

    private static int DepthOf(
        List<RecipeId>[] producedBy,
        ImmutableArray<GoodId>[] inputsOf,
        bool[] included,
        int[] depths,
        bool[] done,
        bool[] busy,
        GoodId good)
    {
        if (done[good.Value])
        {
            return depths[good.Value];
        }

        if (busy[good.Value])
        {
            return 0;
        }

        busy[good.Value] = true;
        int depth = 0;
        bool produced = false;
        foreach (RecipeId recipe in producedBy[good.Value])
        {
            produced = true;
            int inputs = 0;
            foreach (GoodId input in inputsOf[recipe.Value])
            {
                if (included[input.Value])
                {
                    inputs = Math.Max(inputs, DepthOf(producedBy, inputsOf, included, depths, done, busy, input) + 1);
                }
            }

            depth = Math.Max(depth, inputs);
        }

        busy[good.Value] = false;
        done[good.Value] = true;
        depths[good.Value] = produced ? depth : 0;
        return depths[good.Value];
    }
}
