using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Game.Ui.Research;

namespace Evenmere.Game.Tests;

/// <summary>
/// The graph layout behind the research tree: era columns, depth layers, barycentre order, crossing-free
/// curves and era spans. The layout is pure math with no Godot types, so these tests run without the engine.
/// </summary>
public sealed class TreeLayoutTests
{
    private const float Tolerance = 0.5f;

    [Fact]
    public void SliceDepths_LayersRunLeftToRight()
    {
        TreeLayoutResult result = TreeLayout.Compute(SliceNodes());

        float animalHusbandry = result.Positions[0].X;
        float cultivation = result.Positions[1].X;
        float milling = result.Positions[2].X;
        float weaving = result.Positions[3].X;
        float netWeaving = result.Positions[4].X;

        // Depth 0 (no prerequisites in the era) at the left, each dependent one layer to the right.
        animalHusbandry.ShouldBe(cultivation);
        milling.ShouldBe(weaving);
        animalHusbandry.ShouldBeLessThan(milling);
        milling.ShouldBeLessThan(netWeaving);
    }

    [Fact]
    public void SliceLayout_NoCardsOverlap()
    {
        TreeLayoutResult result = TreeLayout.Compute(SliceNodes());
        NoOverlap(result, SliceNodes().Count).ShouldBeTrue();
    }

    [Fact]
    public void CrossingFixture_BarycentreRemovesTheCrossing()
    {
        // Layer 0: A, B. Layer 1 in content order: X (needs B), Y (needs A) — the straight edges cross.
        var nodes = new List<LayoutNode>
        {
            new(1, "Era", [], false),
            new(1, "Era", [], false),
            new(1, "Era", [1], false),
            new(1, "Era", [0], false),
        };

        TreeLayoutResult result = TreeLayout.Compute(nodes);
        List<(int From, int To)> edges = EdgeList(nodes);

        CountCrossings(NaiveSlots(nodes), edges).ShouldBeGreaterThan(0);
        CountCrossings(SlotsOf(result), edges).ShouldBe(0);
        NoOverlap(result, nodes.Count).ShouldBeTrue();
    }

    [Fact]
    public void LongJump_ChannelEdgeNeverPassesBehindACard()
    {
        // E needs A (depth 0) and D (depth 2): the A → E edge jumps a layer and travels the channel.
        var nodes = new List<LayoutNode>
        {
            new(1, "Era", [], false),
            new(1, "Era", [0], false),
            new(1, "Era", [1], false),
            new(1, "Era", [0, 2], true),
        };

        TreeLayoutResult result = TreeLayout.Compute(nodes);
        result.Edges.Count.ShouldBe(4);

        foreach (LayoutEdge edge in result.Edges)
        {
            edge.Points[0].X.ShouldBeLessThan(edge.Points[^1].X - Tolerance);
            foreach (LayoutPoint point in edge.Points)
            {
                point.X.ShouldBeInRange(
                    Math.Min(edge.Points[0].X, edge.Points[^1].X) - Tolerance,
                    Math.Max(edge.Points[0].X, edge.Points[^1].X) + Tolerance);
            }
        }

        NoPointBehindCards(result).ShouldBeTrue();
    }

    [Fact]
    public void SliceEdges_RunLeftToRightBetweenTheRightCards()
    {
        List<LayoutNode> nodes = SliceNodes();
        TreeLayoutResult result = TreeLayout.Compute(nodes);

        // Milling needs Cultivation, Weaving needs Animal Husbandry, Net-weaving needs Weaving.
        result.Edges.Count.ShouldBe(3);
        foreach (LayoutEdge edge in result.Edges)
        {
            LayoutPoint start = edge.Points[0];
            LayoutPoint end = edge.Points[^1];
            start.X.ShouldBeLessThan(end.X - Tolerance);
            OnBorder(start, CardOf(result, edge.From)).ShouldBeTrue();
            OnBorder(end, CardOf(result, edge.To)).ShouldBeTrue();
        }

        NoPointBehindCards(result).ShouldBeTrue();
    }

    [Fact]
    public void EraOneTechs_NoCardsOverlap()
    {
        // The full Era I tech tree (14 nodes): every card must keep its own rect.
        List<LayoutNode> nodes = EraOneTechs();
        nodes.Count.ShouldBe(14);

        TreeLayoutResult result = TreeLayout.Compute(nodes);

        NoOverlap(result, nodes.Count).ShouldBeTrue();
    }

    [Fact]
    public void EraOneCivics_NoCardsOverlap()
    {
        // The full Era I civic tree (8 nodes): every card must keep its own rect.
        List<LayoutNode> nodes = EraOneCivics();
        nodes.Count.ShouldBe(8);

        TreeLayoutResult result = TreeLayout.Compute(nodes);

        NoOverlap(result, nodes.Count).ShouldBeTrue();
    }

    [Fact]
    public void EraOneCivics_HasMoreEdgesThanNodes()
    {
        // The civics graph holds more prerequisite edges than cards (12 on 8): TreeView._Draw keeps one
        // done-flag per edge rather than per node, because indexing a node-sized array by edge threw every frame.
        List<LayoutNode> nodes = EraOneCivics();
        nodes.Count.ShouldBe(8);

        TreeLayoutResult result = TreeLayout.Compute(nodes);

        result.Edges.Count.ShouldBeGreaterThan(result.Positions.Count);
    }

    [Fact]
    public void EraOneGraphs_NoHiddenEdges()
    {
        // No edge sample may land inside a card it does not touch: with the full Era I content every
        // long jump travels the channel below the cards, and direct edges stay inside their own gap.
        foreach (List<LayoutNode> nodes in new[] { EraOneTechs(), EraOneCivics() })
        {
            TreeLayoutResult result = TreeLayout.Compute(nodes);

            result.Edges.Count.ShouldBeGreaterThan(0);
            NoPointBehindCards(result).ShouldBeTrue();
        }
    }

    [Fact]
    public void EraOneGraphs_NoOverlapAtAnySupportedZoom()
    {
        // The view used to shrink the card rect without shrinking its fixed-size content, so neighbours
        // overlapped at 50% zoom. Zoom now scales each card as a whole, so the scaled rects stay disjoint
        // and every edge still meets its cards' scaled borders at every supported zoom.
        foreach (float zoom in new[] { 0.5f, 1f, 2f })
        {
            foreach (List<LayoutNode> nodes in new[] { EraOneTechs(), EraOneCivics() })
            {
                TreeLayoutResult result = TreeLayout.Compute(nodes);

                ScaledNoOverlap(result, zoom).ShouldBeTrue();
                ScaledNoPointBehindCards(result, zoom).ShouldBeTrue();
                ScaledEdgesOnBorders(result, zoom).ShouldBeTrue();
            }
        }
    }

    [Fact]
    public void SameContent_SameLayout()
    {
        TreeLayoutResult first = TreeLayout.Compute(SliceNodes());
        TreeLayoutResult second = TreeLayout.Compute(SliceNodes());

        second.Positions.ShouldBe(first.Positions);
        second.Size.ShouldBe(first.Size);
        second.Eras.ShouldBe(first.Eras);
        for (int i = 0; i < first.Edges.Count; i++)
        {
            second.Edges[i].From.ShouldBe(first.Edges[i].From);
            second.Edges[i].To.ShouldBe(first.Edges[i].To);
            second.Edges[i].Points.ShouldBe(first.Edges[i].Points);
        }
    }

    [Fact]
    public void Gateway_WithManyRequires_LandsDeepest()
    {
        var nodes = new List<LayoutNode>
        {
            new(1, "Era", [], false),
            new(1, "Era", [], false),
            new(1, "Era", [0, 1], true),
        };

        TreeLayoutResult result = TreeLayout.Compute(nodes);

        result.Positions[2].X.ShouldBeGreaterThan(result.Positions[0].X);
        result.Positions[2].X.ShouldBeGreaterThan(result.Positions[1].X);
        result.Positions[2].X.ShouldBe(result.Positions.Select(static point => point.X).Max());
    }

    [Fact]
    public void TwoEras_HaveOrderedSpansWithHeaders()
    {
        var nodes = new List<LayoutNode>
        {
            new(1, "Founding", [], false),
            new(2, "Township", [0], false),
        };

        TreeLayoutResult result = TreeLayout.Compute(nodes);

        result.Eras.Count.ShouldBe(2);
        result.Eras[0].Name.ShouldBe("Founding");
        result.Eras[1].Name.ShouldBe("Township");
        result.Eras[0].MaxX.ShouldBeLessThan(result.Eras[1].MinX);
        result.Positions[1].X.ShouldBeGreaterThan(result.Positions[0].X);
    }

    private static List<LayoutNode> SliceNodes() =>
    [
            new(1, "Founding", [], false),
            new(1, "Founding", [], false),
            new(1, "Founding", [1], false),
            new(1, "Founding", [0], false),
            new(1, "Founding", [3], false),
        ];

    // The real Era I tech tree, in content order: prerequisites address cards by their position here,
    // exactly as the research view-model builds them.
    private static List<LayoutNode> EraOneTechs()
    {
        ContentDb content = TestContent.Db;
        var indexById = new Dictionary<TechId, int>(content.Techs.Count);
        for (int i = 0; i < content.Techs.Count; i++)
        {
            indexById[TechId.From(checked((ushort)i))] = i;
        }

        var nodes = new List<LayoutNode>(content.Techs.Count);
        for (int i = 0; i < content.Techs.Count; i++)
        {
            TechDef def = content.Get(TechId.From(checked((ushort)i)));
            var requires = new List<int>(def.Requires.Length);
            foreach (TechId require in def.Requires)
            {
                if (indexById.TryGetValue(require, out int at))
                {
                    requires.Add(at);
                }
            }

            nodes.Add(new LayoutNode(def.Era, EraContentName(content, def.Era), requires, def.Gateway));
        }

        return nodes;
    }

    // The real Era I civic tree, in content order, parallel to the civics view-model's graph.
    private static List<LayoutNode> EraOneCivics()
    {
        ContentDb content = TestContent.Db;
        var indexById = new Dictionary<CivicId, int>(content.Civics.Count);
        for (int i = 0; i < content.Civics.Count; i++)
        {
            indexById[CivicId.From(checked((ushort)i))] = i;
        }

        var nodes = new List<LayoutNode>(content.Civics.Count);
        for (int i = 0; i < content.Civics.Count; i++)
        {
            CivicDef def = content.Get(CivicId.From(checked((ushort)i)));
            var requires = new List<int>(def.Requires.Length);
            foreach (CivicId require in def.Requires)
            {
                if (indexById.TryGetValue(require, out int at))
                {
                    requires.Add(at);
                }
            }

            nodes.Add(new LayoutNode(def.Era, EraContentName(content, def.Era), requires, def.Gateway));
        }

        return nodes;
    }

    // The layout only draws this in the era headers, so the content name (not the display text) is enough.
    private static string EraContentName(ContentDb content, int order)
    {
        foreach (EraId era in content.ErasByOrder)
        {
            if (content.Get(era).Order == order)
            {
                return content.Eras.NameOf(era);
            }
        }

        return $"era-{order}";
    }

    private static (float X, float Y, float Width, float Height) ScaledCard(TreeLayoutResult result, int index, float zoom)
    {
        LayoutPoint at = result.Positions[index];
        return (at.X * zoom, at.Y * zoom, TreeLayout.NodeWidth * zoom, TreeLayout.NodeHeight * zoom);
    }

    private static bool ScaledNoOverlap(TreeLayoutResult result, float zoom)
    {
        const float slack = 0.01f;
        for (int i = 0; i < result.Positions.Count; i++)
        {
            (float X, float Y, float Width, float Height) left = ScaledCard(result, i, zoom);
            for (int j = i + 1; j < result.Positions.Count; j++)
            {
                (float X, float Y, float Width, float Height) right = ScaledCard(result, j, zoom);
                bool separated = left.X + left.Width <= right.X + slack
                    || right.X + right.Width <= left.X + slack
                    || left.Y + left.Height <= right.Y + slack
                    || right.Y + right.Height <= left.Y + slack;
                if (!separated)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ScaledNoPointBehindCards(TreeLayoutResult result, float zoom)
    {
        for (int e = 0; e < result.Edges.Count; e++)
        {
            LayoutEdge edge = result.Edges[e];
            for (int n = 0; n < result.Positions.Count; n++)
            {
                if (n == edge.From || n == edge.To)
                {
                    continue;
                }

                (float X, float Y, float Width, float Height) card = ScaledCard(result, n, zoom);
                foreach (LayoutPoint point in edge.Points)
                {
                    float x = point.X * zoom;
                    float y = point.Y * zoom;
                    bool inside = x > card.X && x < card.X + card.Width
                        && y > card.Y && y < card.Y + card.Height;
                    if (inside)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool ScaledEdgesOnBorders(TreeLayoutResult result, float zoom)
    {
        float tolerance = Tolerance * zoom + 0.01f;
        foreach (LayoutEdge edge in result.Edges)
        {
            LayoutPoint start = edge.Points[0];
            LayoutPoint end = edge.Points[^1];
            if (!ScaledOnBorder(start.X * zoom, start.Y * zoom, ScaledCard(result, edge.From, zoom), tolerance))
            {
                return false;
            }

            if (!ScaledOnBorder(end.X * zoom, end.Y * zoom, ScaledCard(result, edge.To, zoom), tolerance))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ScaledOnBorder(float x, float y, (float X, float Y, float Width, float Height) card, float tolerance)
    {
        bool nearLeft = Math.Abs(x - card.X) <= tolerance;
        bool nearRight = Math.Abs(x - (card.X + card.Width)) <= tolerance;
        bool nearTop = Math.Abs(y - card.Y) <= tolerance;
        bool nearBottom = Math.Abs(y - (card.Y + card.Height)) <= tolerance;
        bool withinX = x >= card.X - tolerance && x <= card.X + card.Width + tolerance;
        bool withinY = y >= card.Y - tolerance && y <= card.Y + card.Height + tolerance;
        return ((nearLeft || nearRight) && withinY) || ((nearTop || nearBottom) && withinX);
    }

    private static (float X, float Y, float Width, float Height) CardOf(TreeLayoutResult result, int index)
    {
        LayoutPoint at = result.Positions[index];
        return (at.X, at.Y, TreeLayout.NodeWidth, TreeLayout.NodeHeight);
    }

    private static bool NoOverlap(TreeLayoutResult result, int count)
    {
        for (int i = 0; i < count; i++)
        {
            (float X, float Y, float Width, float Height) left = CardOf(result, i);
            for (int j = i + 1; j < count; j++)
            {
                (float X, float Y, float Width, float Height) right = CardOf(result, j);
                bool separated = left.X + left.Width <= right.X
                    || right.X + right.Width <= left.X
                    || left.Y + left.Height <= right.Y
                    || right.Y + right.Height <= left.Y;
                if (!separated)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool OnBorder(LayoutPoint point, (float X, float Y, float Width, float Height) card)
    {
        bool nearLeft = Math.Abs(point.X - card.X) <= Tolerance;
        bool nearRight = Math.Abs(point.X - (card.X + card.Width)) <= Tolerance;
        bool nearTop = Math.Abs(point.Y - card.Y) <= Tolerance;
        bool nearBottom = Math.Abs(point.Y - (card.Y + card.Height)) <= Tolerance;
        bool withinX = point.X >= card.X - Tolerance && point.X <= card.X + card.Width + Tolerance;
        bool withinY = point.Y >= card.Y - Tolerance && point.Y <= card.Y + card.Height + Tolerance;
        return ((nearLeft || nearRight) && withinY) || ((nearTop || nearBottom) && withinX);
    }

    private static bool NoPointBehindCards(TreeLayoutResult result)
    {
        for (int e = 0; e < result.Edges.Count; e++)
        {
            LayoutEdge edge = result.Edges[e];
            for (int n = 0; n < result.Positions.Count; n++)
            {
                if (n == edge.From || n == edge.To)
                {
                    continue;
                }

                (float X, float Y, float Width, float Height) card = CardOf(result, n);
                foreach (LayoutPoint point in edge.Points)
                {
                    bool inside = point.X > card.X && point.X < card.X + card.Width
                        && point.Y > card.Y && point.Y < card.Y + card.Height;
                    if (inside)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static Dictionary<int, (int Layer, int Slot)> SlotsOf(TreeLayoutResult result)
    {
        var slots = new Dictionary<int, (int Layer, int Slot)>();
        for (int i = 0; i < result.Positions.Count; i++)
        {
            int layer = (int)Math.Round((result.Positions[i].X - TreeLayout.Margin) / (TreeLayout.NodeWidth + TreeLayout.LayerGap));
            int slot = (int)Math.Round((result.Positions[i].Y - TreeLayout.Margin - TreeLayout.HeaderHeight) / (TreeLayout.NodeHeight + TreeLayout.RowGap));
            slots[i] = (layer, slot);
        }

        return slots;
    }

    private static Dictionary<int, (int Layer, int Slot)> NaiveSlots(List<LayoutNode> nodes)
    {
        // The pre-barycentre order: depths as layers, content order as slots.
        var depths = new Dictionary<int, int>();
        foreach (int node in Enumerable.Range(0, nodes.Count))
        {
            int depth = 0;
            foreach (int require in nodes[node].Requires)
            {
                depths.TryGetValue(require, out int parent);
                depth = Math.Max(depth, parent + 1);
            }

            depths[node] = depth;
        }

        var slots = new Dictionary<int, (int Layer, int Slot)>();
        var taken = new Dictionary<int, int>();
        for (int node = 0; node < nodes.Count; node++)
        {
            int layer = depths[node];
            taken.TryGetValue(layer, out int slot);
            slots[node] = (layer, slot);
            taken[layer] = slot + 1;
        }

        return slots;
    }

    private static List<(int From, int To)> EdgeList(List<LayoutNode> nodes)
    {
        var edges = new List<(int From, int To)>();
        for (int i = 0; i < nodes.Count; i++)
        {
            foreach (int require in nodes[i].Requires)
            {
                if (require >= 0 && require < nodes.Count && require != i)
                {
                    edges.Add((require, i));
                }
            }
        }

        return edges;
    }

    private static int CountCrossings(
        Dictionary<int, (int Layer, int Slot)> slots,
        List<(int From, int To)> edges)
    {
        int crossings = 0;
        for (int i = 0; i < edges.Count; i++)
        {
            for (int j = i + 1; j < edges.Count; j++)
            {
                if (edges[i].From == edges[j].From || edges[i].From == edges[j].To
                    || edges[i].To == edges[j].From || edges[i].To == edges[j].To)
                {
                    continue;
                }

                (int Layer, int Slot) p1 = slots[edges[i].From];
                (int Layer, int Slot) p2 = slots[edges[i].To];
                (int Layer, int Slot) p3 = slots[edges[j].From];
                (int Layer, int Slot) p4 = slots[edges[j].To];
                if (Orient(p1, p2, p3) * Orient(p1, p2, p4) < 0
                    && Orient(p3, p4, p1) * Orient(p3, p4, p2) < 0)
                {
                    crossings++;
                }
            }
        }

        return crossings;
    }

    private static int Orient((int Layer, int Slot) a, (int Layer, int Slot) b, (int Layer, int Slot) c) =>
        Math.Sign((b.Layer - a.Layer) * (c.Slot - a.Slot) - (b.Slot - a.Slot) * (c.Layer - a.Layer));
}
