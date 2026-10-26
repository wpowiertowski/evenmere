namespace Evenmere.Game.Ui.Research;

/// <summary>
/// The display state of one graph node: what the node shows and how its card is framed.
/// </summary>
public enum GraphNodeState
{
    /// <summary>The node cannot be researched yet; its missing prerequisites are named.</summary>
    Locked,

    /// <summary>The node can be chosen as the research target.</summary>
    Available,

    /// <summary>The node waits behind the target, in queue order.</summary>
    Queued,

    /// <summary>The node receives today's knowledge.</summary>
    Target,

    /// <summary>The node is researched.</summary>
    Done,
}

/// <summary>
/// One unlock a node grants: a building, recipe, tier or bonus, with the placeholder glyph the menus use.
/// </summary>
/// <param name="Glyph">The visuals glyph for the unlocked building, or empty when the unlock has no icon.</param>
/// <param name="Text">The short display text, already resolved through the locale table.</param>
public sealed record GraphUnlock(string Glyph, string Text);

/// <summary>
/// One tech-tree node as the graph shows it: content facts and query answers, already formatted. It carries no
/// Godot types, so the layout and the view-model stay testable without the engine.
/// </summary>
/// <param name="Index">The position in the graph's node list; edges address nodes by it.</param>
/// <param name="Name">The display name, with a star already prepended for gateways.</param>
/// <param name="EraName">The display name of the node's era.</param>
/// <param name="Gateway">Whether completing the node is one half of an era transition.</param>
/// <param name="State">The display state.</param>
/// <param name="QueuePosition">The position in the queue, or -1 when the node is not queued.</param>
/// <param name="StateText">The state line, such as the queue position or the group name.</param>
/// <param name="CostText">The progress and ETA line, such as <c>12 of 30 · 6 days</c>.</param>
/// <param name="Fraction">How full the progress bar is, from 0 to 1.</param>
/// <param name="InspirationText">The inspiration sentence with its met or unmet mark.</param>
/// <param name="Inspired">Whether the inspiration has triggered.</param>
/// <param name="InspiredBadge">The inspiration badge text, or empty when the node is not inspired.</param>
/// <param name="MissingText">The missing prerequisites, or empty when none are missing.</param>
/// <param name="Unlocks">The unlock list entries, in content order.</param>
/// <param name="Tooltip">The hover and focus tip.</param>
public sealed record GraphNode(
    int Index,
    string Name,
    string EraName,
    bool Gateway,
    GraphNodeState State,
    int QueuePosition,
    string StateText,
    string CostText,
    double Fraction,
    string InspirationText,
    bool Inspired,
    string InspiredBadge,
    string MissingText,
    IReadOnlyList<GraphUnlock> Unlocks,
    string Tooltip);

/// <summary>
/// One node as the layout reads it: its era column and era name, its prerequisites by node index, and whether
/// it is a gateway (which, requiring the most, lands in its era's deepest layer).
/// </summary>
/// <param name="EraOrder">The era's rank; nodes of one era share a column.</param>
/// <param name="EraName">The display name of the node's era, for the column header.</param>
/// <param name="Requires">The indices of the prerequisite nodes, in file order.</param>
/// <param name="Gateway">Whether the node is an era gateway.</param>
public sealed record LayoutNode(int EraOrder, string EraName, IReadOnlyList<int> Requires, bool Gateway);

/// <summary>
/// A point on the graph canvas, in layout units before the view's zoom.
/// </summary>
/// <param name="X">The horizontal position.</param>
/// <param name="Y">The vertical position.</param>
public readonly record struct LayoutPoint(float X, float Y);

/// <summary>
/// One prerequisite edge, already sampled into a polyline the view draws as a curve. Direct edges run from the
/// prerequisite card's right edge to the dependent card's left edge; longer jumps travel a channel below every
/// card, so no edge ever passes behind a card.
/// </summary>
/// <param name="From">The index of the prerequisite node.</param>
/// <param name="To">The index of the dependent node.</param>
/// <param name="Points">The sampled path, from the source card's edge to the target card's edge.</param>
public sealed record LayoutEdge(int From, int To, IReadOnlyList<LayoutPoint> Points);

/// <summary>
/// One era's column: its header text and horizontal span on the canvas.
/// </summary>
/// <param name="EraOrder">The era's rank.</param>
/// <param name="Name">The display name of the era.</param>
/// <param name="MinX">The left edge of the era's first layer.</param>
/// <param name="MaxX">The right edge of the era's last layer.</param>
public sealed record EraSpan(int EraOrder, string Name, float MinX, float MaxX);

/// <summary>
/// The layout answer: one card origin per input node, in input order, the sampled prerequisite edges, the
/// canvas size that holds every card, and the era spans for headers and dividers.
/// </summary>
/// <param name="Positions">The card origins, in the same order as the layout input.</param>
/// <param name="Edges">The prerequisite edges.</param>
/// <param name="Size">The canvas size.</param>
/// <param name="Eras">The era spans, in rank order.</param>
public sealed record TreeLayoutResult(
    IReadOnlyList<LayoutPoint> Positions,
    IReadOnlyList<LayoutEdge> Edges,
    LayoutPoint Size,
    IReadOnlyList<EraSpan> Eras);

/// <summary>
/// The cards and layout inputs of one tree, in the same node order, as the graph view shows them.
/// </summary>
/// <param name="Nodes">The cards, in display order.</param>
/// <param name="Layout">The layout inputs, parallel to <paramref name="Nodes"/>.</param>
public sealed record TreeGraph(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<LayoutNode> Layout);

/// <summary>
/// The graph behind the research and civics trees (M2-18, reused by M2-19): columns by era, and inside each era
/// one layer per prerequisite depth running left to right, with a deterministic barycentre pass over the content
/// order to reduce edge crossings. It is pure arithmetic with no Godot types, so the game test project can
/// exercise it on its own.
/// </summary>
public static class TreeLayout
{
    /// <summary>Gets the card width in layout units; the node views match it times the zoom.</summary>
    public const float NodeWidth = 204f;

    /// <summary>Gets the card height in layout units; the node views match it times the zoom.</summary>
    public const float NodeHeight = 192f;

    /// <summary>Gets the gap between depth layers (and the minimum edge corridor).</summary>
    public const float LayerGap = 30f;

    /// <summary>Gets the gap between cards stacked in one layer.</summary>
    public const float RowGap = 24f;

    /// <summary>Gets the margin around the whole graph.</summary>
    public const float Margin = 16f;

    /// <summary>Gets the gap between era columns, which carries the soft divider.</summary>
    public const float EraGap = 48f;

    /// <summary>Gets the header strip above the cards, which carries the era names.</summary>
    public const float HeaderHeight = 32f;

    /// <summary>Gets how far below the lowest card the channel for long edges runs.</summary>
    public const float ChannelGap = 40f;

    /// <summary>Gets the corner radius of the channel path, in layout units.</summary>
    public const float CornerRadius = 20f;

    /// <summary>Gets how many points a direct edge curve is sampled into.</summary>
    public const int EdgeSamples = 16;

    /// <summary>Gets how many points each rounded channel corner is sampled into.</summary>
    public const int CornerSamples = 6;

    /// <summary>
    /// Places the nodes: one column per era in rank order, and inside each era one layer per prerequisite
    /// depth (relative to the era's shallowest node) running left to right. Nodes of a layer stack vertically in
    /// barycentre order, so edges cross as little as plain content order allows. Edges between neighbouring
    /// layers run straight across the gap; longer jumps travel a channel below every card.
    /// </summary>
    /// <param name="nodes">The nodes, in the order the graph addresses them.</param>
    /// <returns>The card origins, the edges, the canvas size and the era spans.</returns>
    public static TreeLayoutResult Compute(IReadOnlyList<LayoutNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        if (nodes.Count == 0)
        {
            return new TreeLayoutResult([], [], new LayoutPoint(Margin * 2f, Margin * 2f + HeaderHeight), []);
        }

        int[] depths = DepthOf(nodes);
        int[] eraSlots = EraSlotsOf(nodes, out int[] eraOrders, out string[] eraNames);

        // Layers are relative within each era, so every era starts at its own left edge.
        var eraMinDepth = new Dictionary<int, int>();
        for (int i = 0; i < nodes.Count; i++)
        {
            int era = eraSlots[i];
            if (!eraMinDepth.TryGetValue(era, out int floor) || depths[i] < floor)
            {
                eraMinDepth[era] = depths[i];
            }
        }

        int[] layers = new int[nodes.Count];
        for (int i = 0; i < layers.Length; i++)
        {
            layers[i] = depths[i] - eraMinDepth[eraSlots[i]];
        }

        int[][] order = OrderLayers(nodes, eraSlots, layers);
        int[] slots = new int[nodes.Count];
        for (int layer = 0; layer < order.Length; layer++)
        {
            for (int slot = 0; slot < order[layer].Length; slot++)
            {
                slots[order[layer][slot]] = slot;
            }
        }

        // One x per (era, layer); eras placed left to right with a divider gap between them.
        var layerX = new Dictionary<(int Era, int Layer), float>();
        var eraSpans = new List<EraSpan>();
        float x = Margin;
        for (int era = 0; era < eraOrders.Length; era++)
        {
            int top = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (eraSlots[i] == era && layers[i] > top)
                {
                    top = layers[i];
                }
            }

            float eraLeft = x;
            for (int layer = 0; layer <= top; layer++)
            {
                layerX[(era, layer)] = x;
                x += NodeWidth + LayerGap;
            }

            x += EraGap - LayerGap;
            eraSpans.Add(new EraSpan(eraOrders[era], eraNames[era], eraLeft, x - EraGap));
        }

        float width = x - EraGap + Margin;
        var positions = new LayoutPoint[nodes.Count];
        int tallest = 0;
        for (int layer = 0; layer < order.Length; layer++)
        {
            tallest = Math.Max(tallest, order[layer].Length);
        }

        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = new LayoutPoint(
                layerX[(eraSlots[i], layers[i])],
                Margin + HeaderHeight + slots[i] * (NodeHeight + RowGap));
        }

        float contentBottom = Margin + HeaderHeight + tallest * NodeHeight + (tallest - 1) * RowGap;
        var edges = new List<LayoutEdge>();
        bool channel = false;
        for (int i = 0; i < nodes.Count; i++)
        {
            foreach (int require in nodes[i].Requires)
            {
                if (require < 0 || require >= nodes.Count || require == i)
                {
                    continue;
                }

                // Direct edges stay inside one gap: neighbouring layers, or the divider between eras.
                // Anything further jumps at least one card column, so it travels the channel instead.
                if (positions[i].X - (positions[require].X + NodeWidth) <= EraGap + 0.5f
                    && positions[i].X > positions[require].X)
                {
                    edges.Add(DirectEdge(require, i, positions[require], positions[i]));
                }
                else
                {
                    channel = true;
                }
            }
        }

        float channelY = contentBottom + ChannelGap;
        if (channel)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                foreach (int require in nodes[i].Requires)
                {
                    if (require < 0 || require >= nodes.Count || require == i)
                    {
                        continue;
                    }

                    if (positions[i].X - (positions[require].X + NodeWidth) > EraGap + 0.5f
                        || positions[i].X <= positions[require].X)
                    {
                        edges.Add(ChannelEdge(require, i, positions[require], positions[i], channelY));
                    }
                }
            }
        }

        // Keep the edge order deterministic: content order of the dependent, then file order of requires.
        edges.Sort(static (left, right) =>
        {
            int to = left.To.CompareTo(right.To);
            return to != 0 ? to : left.From.CompareTo(right.From);
        });

        float height = (channel ? channelY + Margin : contentBottom + Margin);
        return new TreeLayoutResult(positions, edges, new LayoutPoint(width, height), eraSpans);
    }

    private static int[] DepthOf(IReadOnlyList<LayoutNode> nodes)
    {
        int[] depths = new int[nodes.Count];

        // Content trees are acyclic, but a cycle must not hang the panel: cap the passes at the node count,
        // which is enough for the longest simple path, then stop.
        for (int pass = 0; pass < nodes.Count; pass++)
        {
            bool changed = false;
            for (int i = 0; i < nodes.Count; i++)
            {
                foreach (int require in nodes[i].Requires)
                {
                    if (require < 0 || require >= nodes.Count || require == i)
                    {
                        continue;
                    }

                    if (depths[i] < depths[require] + 1)
                    {
                        depths[i] = depths[require] + 1;
                        changed = true;
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return depths;
    }

    private static int[] EraSlotsOf(IReadOnlyList<LayoutNode> nodes, out int[] eraOrders, out string[] eraNames)
    {
        var eras = new List<int>();
        var names = new List<string>();
        foreach (LayoutNode node in nodes)
        {
            if (!eras.Contains(node.EraOrder))
            {
                eras.Add(node.EraOrder);
                names.Add(node.EraName);
            }
        }

        var rank = new List<int>(eras);
        rank.Sort();

        int[] slots = new int[nodes.Count];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = rank.IndexOf(nodes[i].EraOrder);
        }

        eraOrders = [.. rank];
        eraNames = new string[rank.Count];
        for (int i = 0; i < rank.Count; i++)
        {
            eraNames[i] = names[eras.IndexOf(rank[i])];
        }

        return slots;
    }

    private static int[][] OrderLayers(
        IReadOnlyList<LayoutNode> nodes,
        int[] eraSlots,
        int[] layers)
    {
        // Layer keys in left-to-right order: era rank first, then depth within the era.
        var keys = new List<(int Era, int Layer)>();
        for (int i = 0; i < nodes.Count; i++)
        {
            (int Era, int Layer) key = (eraSlots[i], layers[i]);
            if (!keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        keys.Sort(static (left, right) =>
        {
            int era = left.Era.CompareTo(right.Era);
            return era != 0 ? era : left.Layer.CompareTo(right.Layer);
        });

        var keyIndex = new Dictionary<(int Era, int Layer), int>();
        for (int i = 0; i < keys.Count; i++)
        {
            keyIndex[keys[i]] = i;
        }

        var order = new List<int>[keys.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = new List<int>();
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            order[keyIndex[(eraSlots[i], layers[i])]].Add(i);
        }

        // Two barycentre sweeps over the content order: left to right against prerequisites, then right to
        // left against dependents. Ties keep content order (the node index), so the result is deterministic.
        for (int sweep = 0; sweep < 2; sweep++)
        {
            if (sweep == 0)
            {
                for (int layer = 0; layer < order.Length; layer++)
                {
                    order[layer] = SortByNeighbours(order[layer], nodes, order, keyIndex, eraSlots, layers, forward: true);
                }
            }
            else
            {
                for (int layer = order.Length - 1; layer >= 0; layer--)
                {
                    order[layer] = SortByNeighbours(order[layer], nodes, order, keyIndex, eraSlots, layers, forward: false);
                }
            }
        }

        return order.Select(static layer => layer.ToArray()).ToArray();
    }

    private static List<int> SortByNeighbours(
        List<int> layer,
        IReadOnlyList<LayoutNode> nodes,
        List<int>[] order,
        Dictionary<(int Era, int Layer), int> keyIndex,
        int[] eraSlots,
        int[] layers,
        bool forward)
    {
        var position = new Dictionary<int, int>();
        for (int i = 0; i < order.Length; i++)
        {
            for (int slot = 0; slot < order[i].Count; slot++)
            {
                position[order[i][slot]] = slot;
            }
        }

        var barycentre = new Dictionary<int, double>();
        foreach (int node in layer)
        {
            double sum = 0;
            int count = 0;
            if (forward)
            {
                foreach (int require in nodes[node].Requires)
                {
                    if (require >= 0 && require < nodes.Count && position.TryGetValue(require, out int slot))
                    {
                        sum += slot;
                        count++;
                    }
                }
            }
            else
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodes[i].Requires.Contains(node) && position.TryGetValue(i, out int slot))
                    {
                        sum += slot;
                        count++;
                    }
                }
            }

            barycentre[node] = count > 0 ? sum / count : position[node];
        }

        // LINQ's OrderBy is stable, and the node index is the content order, so ties stay deterministic.
        return layer
            .OrderBy(node => barycentre[node])
            .ThenBy(node => node)
            .ToList();
    }

    private static LayoutEdge DirectEdge(int from, int to, LayoutPoint source, LayoutPoint target)
    {
        float dx = target.X - (source.X + NodeWidth);
        float reach = Math.Min(48f, dx * 0.5f);
        var start = new LayoutPoint(source.X + NodeWidth, source.Y + NodeHeight * 0.5f);
        var end = new LayoutPoint(target.X, target.Y + NodeHeight * 0.5f);
        var points = new LayoutPoint[EdgeSamples + 1];
        for (int s = 0; s <= EdgeSamples; s++)
        {
            float t = s / (float)EdgeSamples;
            float u = 1f - t;
            points[s] = new LayoutPoint(
                u * u * u * start.X
                    + 3f * u * u * t * (start.X + reach)
                    + 3f * u * t * t * (end.X - reach)
                    + t * t * t * end.X,
                u * u * u * start.Y
                    + 3f * u * u * t * start.Y
                    + 3f * u * t * t * end.Y
                    + t * t * t * end.Y);
        }

        return new LayoutEdge(from, to, points);
    }

    private static LayoutEdge ChannelEdge(int from, int to, LayoutPoint source, LayoutPoint target, float channelY)
    {
        var down = new LayoutPoint(source.X + NodeWidth * 0.5f, source.Y + NodeHeight);
        var up = new LayoutPoint(target.X + NodeWidth * 0.5f, target.Y + NodeHeight);
        LayoutPoint[] waypoints = new[]
        {
            down,
            new LayoutPoint(down.X, channelY),
            new LayoutPoint(up.X, channelY),
            up,
        };
        return new LayoutEdge(from, to, RoundedPath(waypoints));
    }

    private static LayoutPoint[] RoundedPath(LayoutPoint[] waypoints)
    {
        var points = new List<LayoutPoint>();
        points.Add(waypoints[0]);
        for (int i = 1; i < waypoints.Length - 1; i++)
        {
            LayoutPoint before = waypoints[i - 1];
            LayoutPoint corner = waypoints[i];
            LayoutPoint after = waypoints[i + 1];

            float inLength = Distance(before, corner);
            float outLength = Distance(corner, after);
            float radius = Math.Min(CornerRadius, Math.Min(inLength, outLength) * 0.5f);

            LayoutPoint entry = Lerp(corner, before, radius / Math.Max(inLength, 0.001f));
            LayoutPoint exit = Lerp(corner, after, radius / Math.Max(outLength, 0.001f));

            points.Add(entry);
            for (int s = 1; s <= CornerSamples; s++)
            {
                float t = s / (float)(CornerSamples + 1);
                float u = 1f - t;
                points.Add(new LayoutPoint(
                    u * u * entry.X + 2f * u * t * corner.X + t * t * exit.X,
                    u * u * entry.Y + 2f * u * t * corner.Y + t * t * exit.Y));
            }

            points.Add(exit);
        }

        points.Add(waypoints[^1]);
        return [.. points];
    }

    private static float Distance(LayoutPoint left, LayoutPoint right)
    {
        float dx = right.X - left.X;
        float dy = right.Y - left.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static LayoutPoint Lerp(LayoutPoint from, LayoutPoint to, float t) =>
        new(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
}
