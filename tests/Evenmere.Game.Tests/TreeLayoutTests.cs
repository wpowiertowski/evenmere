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
