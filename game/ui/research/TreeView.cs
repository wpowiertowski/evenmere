using Godot;

namespace Evenmere.Game.Ui.Research;

/// <summary>
/// The tech-tree graph: a zoomable, pannable canvas of <see cref="TreeNodeView"/> cards placed by
/// <see cref="TreeLayout"/>, with prerequisite edges drawn as curves and era headers over each column.
/// Clicking a name makes it the target, Shift-clicking or the card's queue button appends it to the queue.
/// Cards are keyed by node index across refreshes, so keyboard and gamepad focus survive a target or queue
/// swap. Focus neighbours follow the tree's edges (prerequisites on the left, dependents on the right,
/// siblings above and below) through the existing <c>ui_*</c> focus actions; no new input actions are added.
/// </summary>
public sealed partial class TreeView : Control
{
    /// <summary>Raised when a card asks to become the research target.</summary>
    public event Action<int>? TargetRequested;

    /// <summary>Raised when a card asks to join the research queue.</summary>
    public event Action<int>? QueueRequested;

    private const float MinZoom = 0.5f;
    private const float MaxZoom = 2.0f;

    private static readonly Color _edge = new(0.45f, 0.50f, 0.54f);
    private static readonly Color _edgeDone = new(0.25f, 0.55f, 0.30f);
    private static readonly Color _divider = new(0.62f, 0.60f, 0.56f);

    private float _zoom = 1f;
    private Label? _zoomLabel;
    private Button? _zoomOut;
    private ScrollContainer? _scroll;
    private Control? _canvas;
    private bool _chrome;
    private bool _panning;
    private Vector2 _panAnchor;
    private float _panHorizontal;
    private float _panVertical;

    private IReadOnlyList<GraphNode> _nodes = [];
    private IReadOnlyList<LayoutNode> _inputs = [];
    private TreeLayoutResult? _layout;
    private Vector2[][] _edgeLines = [];
    private bool[] _fromDone = [];
    private readonly Dictionary<int, TreeNodeView> _views = new();
    private readonly Dictionary<int, Label> _eraHeaders = new();

    /// <inheritdoc />
    public override void _Ready() => EnsureChrome();

    /// <inheritdoc />
    public override void _Draw()
    {
        // The edges live on the scrolling canvas but draw here, behind the cards: the transform shifts
        // canvas-space points into this control's space, so scrolling only needs a redraw, not a rebuild.
        if (_scroll is null || _canvas is null || _layout is null)
        {
            return;
        }

        Vector2 origin = _canvas.GetGlobalRect().Position - GetGlobalRect().Position
            - new Vector2(_scroll.ScrollHorizontal, _scroll.ScrollVertical);
        DrawSetTransform(origin, 0f, Vector2.One);

        for (int i = 1; i < _layout.Eras.Count; i++)
        {
            float divider = (_layout.Eras[i - 1].MaxX + _layout.Eras[i].MinX) * 0.5f * _zoom;
            DrawLine(new Vector2(divider, 0f), new Vector2(divider, _layout.Size.Y * _zoom), _divider, 2f);
        }

        for (int i = 0; i < _edgeLines.Length; i++)
        {
            DrawPolyline(_edgeLines[i], _fromDone[i] ? _edgeDone : _edge, 3f, true);
        }

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>
    /// Shows the graph's nodes, reusing cards by node index so focus survives refreshes. Positions come from
    /// <see cref="TreeLayout"/>; both lists must hold the same nodes in the same order.
    /// </summary>
    /// <param name="nodes">The nodes to show, with their display data.</param>
    /// <param name="inputs">The layout inputs, parallel to <paramref name="nodes"/>.</param>
    public void SetNodes(IReadOnlyList<GraphNode> nodes, IReadOnlyList<LayoutNode> inputs)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(inputs);
        if (nodes.Count != inputs.Count)
        {
            throw new ArgumentException("The nodes and their layout inputs must be parallel.", nameof(inputs));
        }

        EnsureChrome();
        if (_scroll is null || _canvas is null)
        {
            return;
        }

        _nodes = nodes;
        _inputs = inputs;
        _layout = TreeLayout.Compute(inputs);

        var wanted = new HashSet<int>();
        foreach (GraphNode node in nodes)
        {
            wanted.Add(node.Index);
        }

        var gone = new List<int>();
        foreach (int index in _views.Keys)
        {
            if (!wanted.Contains(index))
            {
                gone.Add(index);
            }
        }

        foreach (int index in gone)
        {
            _views[index].QueueFree();
            _views.Remove(index);
        }

        foreach (GraphNode node in nodes)
        {
            if (!_views.TryGetValue(node.Index, out TreeNodeView? view))
            {
                view = new TreeNodeView();
                view.TargetRequested += OnTarget;
                view.QueueRequested += OnQueue;
                _views[node.Index] = view;
                _canvas.AddChild(view);
            }

            view.SetNode(node);
        }

        ApplyLayout();
        WireNeighbours();
        CacheEdges();
        QueueRedraw();
    }

    /// <summary>Multiplies the zoom, clamped to the allowed range.</summary>
    /// <param name="factor">The multiplier, above 1 to zoom in.</param>
    public void ZoomBy(float factor)
    {
        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        if (_zoomLabel is not null)
        {
            _zoomLabel.Text = $"{(int)Math.Round(_zoom * 100f)}%";
        }

        ApplyLayout();
        CacheEdges();
        QueueRedraw();
    }

    private void OnTarget(int index) => TargetRequested?.Invoke(index);

    private void OnQueue(int index) => QueueRequested?.Invoke(index);

    private void EnsureChrome()
    {
        if (_chrome)
        {
            return;
        }

        _chrome = true;
        ClipContents = true;

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 8);
        rows.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(rows);

        var tools = new HBoxContainer();
        tools.AddThemeConstantOverride("separation", 8);
        rows.AddChild(tools);

        var zoomOut = new Button { Text = "−", FocusMode = Control.FocusModeEnum.All };
        zoomOut.Pressed += () => ZoomBy(0.8f);
        _zoomOut = zoomOut;
        tools.AddChild(zoomOut);

        var zoomLabel = new Label
        {
            Text = "100%",
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(64, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        tools.AddChild(zoomLabel);
        _zoomLabel = zoomLabel;

        var zoomIn = new Button { Text = "+", FocusMode = Control.FocusModeEnum.All };
        zoomIn.Pressed += () => ZoomBy(1.25f);
        tools.AddChild(zoomIn);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FollowFocus = true,
        };
        rows.AddChild(scroll);
        _scroll = scroll;
        scroll.GetHScrollBar().ValueChanged += OnScrolled;
        scroll.GetVScrollBar().ValueChanged += OnScrolled;

        var canvas = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        canvas.GuiInput += OnCanvasInput;
        scroll.AddChild(canvas);
        _canvas = canvas;
    }

    private void OnScrolled(double value) => QueueRedraw();

    private void ApplyLayout()
    {
        if (_layout is null || _canvas is null)
        {
            return;
        }

        _canvas.CustomMinimumSize = new Vector2(
            _layout.Size.X * _zoom,
            _layout.Size.Y * _zoom);

        for (int i = 0; i < _nodes.Count; i++)
        {
            GraphNode node = _nodes[i];
            if (_views.TryGetValue(node.Index, out TreeNodeView? view))
            {
                LayoutPoint at = _layout.Positions[i];
                view.Position = new Vector2(at.X * _zoom, at.Y * _zoom);
                view.Size = new Vector2(TreeLayout.NodeWidth * _zoom, TreeLayout.NodeHeight * _zoom);
            }
        }

        var wanted = new HashSet<int>();
        foreach (EraSpan era in _layout.Eras)
        {
            wanted.Add(era.EraOrder);
            if (!_eraHeaders.TryGetValue(era.EraOrder, out Label? header))
            {
                header = new Label
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                header.AddThemeFontSizeOverride("font_size", 20);
                _eraHeaders[era.EraOrder] = header;
                _canvas.AddChild(header);
            }

            header.Text = era.Name;
            header.Position = new Vector2(era.MinX * _zoom, (TreeLayout.Margin - 4f) * _zoom);
            header.Size = new Vector2((era.MaxX - era.MinX) * _zoom, TreeLayout.HeaderHeight * _zoom);
        }

        var gone = new List<int>();
        foreach (int era in _eraHeaders.Keys)
        {
            if (!wanted.Contains(era))
            {
                gone.Add(era);
            }
        }

        foreach (int era in gone)
        {
            _eraHeaders[era].QueueFree();
            _eraHeaders.Remove(era);
        }
    }

    private void CacheEdges()
    {
        if (_layout is null)
        {
            _edgeLines = [];
            _fromDone = [];
            return;
        }

        var lines = new Vector2[_layout.Edges.Count][];
        _fromDone = new bool[_nodes.Count];
        for (int i = 0; i < _nodes.Count; i++)
        {
            _fromDone[i] = _nodes[i].State == GraphNodeState.Done;
        }

        for (int i = 0; i < _layout.Edges.Count; i++)
        {
            LayoutEdge edge = _layout.Edges[i];
            var line = new Vector2[edge.Points.Count];
            for (int s = 0; s < line.Length; s++)
            {
                line[s] = new Vector2(edge.Points[s].X * _zoom, edge.Points[s].Y * _zoom);
            }

            lines[i] = line;
        }

        _edgeLines = lines;
    }

    private void WireNeighbours()
    {
        if (_layout is null || _nodes.Count == 0)
        {
            return;
        }

        var dependents = new Dictionary<int, List<int>>();
        for (int i = 0; i < _inputs.Count; i++)
        {
            foreach (int require in _inputs[i].Requires)
            {
                if (require < 0 || require >= _inputs.Count || require == i)
                {
                    continue;
                }

                if (!dependents.TryGetValue(require, out List<int>? list))
                {
                    list = new List<int>();
                    dependents[require] = list;
                }

                list.Add(i);
            }
        }

        for (int i = 0; i < _nodes.Count; i++)
        {
            if (!_views.TryGetValue(_nodes[i].Index, out TreeNodeView? view))
            {
                continue;
            }

            Button main = view.MainButton;
            Button queue = view.QueueButton;

            if (BestNeighbour(i, _inputs[i].Requires, static (atX, atY, candX, candY) => candX < atX, true) is { } left
                && _views.TryGetValue(_nodes[left].Index, out TreeNodeView? leftView))
            {
                main.FocusNeighborLeft = leftView.MainButton.GetPath();
            }

            if (dependents.TryGetValue(i, out List<int>? outs)
                && BestNeighbour(i, outs, static (atX, atY, candX, candY) => candX > atX, true) is { } right
                && _views.TryGetValue(_nodes[right].Index, out TreeNodeView? rightView))
            {
                main.FocusNeighborRight = rightView.MainButton.GetPath();
            }

            // The queue button sits at the bottom of its card, so the vertical chain runs through it when it is
            // shown (hidden for done and targeted nodes): up lands on the above card's queue, down on the node's
            // own queue first (M2-26 focus walk).
            if (BestNeighbour(i, SameLayer(i, above: true), static (atX, atY, candX, candY) => true, false) is { } up
                && _views.TryGetValue(_nodes[up].Index, out TreeNodeView? upView))
            {
                Button upBottom = upView.QueueButton.Visible ? upView.QueueButton : upView.MainButton;
                main.FocusNeighborTop = upBottom.GetPath();
            }

            if (queue.Visible)
            {
                main.FocusNeighborBottom = queue.GetPath();
                queue.FocusNeighborTop = main.GetPath();

                if (BestNeighbour(i, SameLayer(i, above: false), static (atX, atY, candX, candY) => true, false) is { } down
                    && _views.TryGetValue(_nodes[down].Index, out TreeNodeView? downView))
                {
                    queue.FocusNeighborBottom = downView.MainButton.GetPath();
                }
            }
            else if (BestNeighbour(i, SameLayer(i, above: false), static (atX, atY, candX, candY) => true, false) is { } own
                && _views.TryGetValue(_nodes[own].Index, out TreeNodeView? ownView))
            {
                main.FocusNeighborBottom = ownView.MainButton.GetPath();
            }
        }

        // The zoom row sits above the canvas: without an explicit link the topmost node's up-neighbours stay
        // inside the graph and the zoom buttons are unreachable by ui_* navigation (M2-26 focus walk). Link the
        // topmost node both ways; the zoom buttons reach each other automatically as HBox neighbours.
        if (_zoomOut is not null && _layout is not null)
        {
            int top = -1;
            for (int j = 0; j < _nodes.Count; j++)
            {
                if (top < 0 || _layout.Positions[j].Y < _layout.Positions[top].Y)
                {
                    top = j;
                }
            }

            if (top >= 0 && _views.TryGetValue(_nodes[top].Index, out TreeNodeView? topView))
            {
                Button topMain = topView.MainButton;
                topMain.FocusNeighborTop = _zoomOut.GetPath();
                _zoomOut.FocusNeighborBottom = topMain.GetPath();
            }
        }

        List<int> SameLayer(int self, bool above)
        {
            var column = new List<int>();
            if (_layout is null)
            {
                return column;
            }

            float selfX = _layout.Positions[self].X;
            float selfY = _layout.Positions[self].Y;
            for (int j = 0; j < _nodes.Count; j++)
            {
                if (j == self)
                {
                    continue;
                }

                LayoutPoint other = _layout.Positions[j];
                if (Math.Abs(other.X - selfX) < 0.5f
                    && (above ? other.Y < selfY : other.Y > selfY))
                {
                    column.Add(j);
                }
            }

            return column;
        }
    }

    private int? BestNeighbour(
        int self,
        IReadOnlyList<int> candidates,
        Func<float, float, float, float, bool> direction,
        bool horizontal)
    {
        if (_layout is null)
        {
            return null;
        }

        LayoutPoint at = _layout.Positions[self];
        int? best = null;
        float bestPrimary = float.MaxValue;
        float bestAcross = float.MaxValue;

        foreach (int candidate in candidates)
        {
            if (candidate < 0 || candidate >= _nodes.Count || candidate == self)
            {
                continue;
            }

            LayoutPoint other = _layout.Positions[candidate];
            if (!direction(at.X, at.Y, other.X, other.Y))
            {
                continue;
            }

            float primary = horizontal ? Math.Abs(other.X - at.X) : Math.Abs(other.Y - at.Y);
            float across = horizontal ? Math.Abs(other.Y - at.Y) : Math.Abs(other.X - at.X);
            if (primary < bestPrimary - 0.5f || (Math.Abs(primary - bestPrimary) < 0.5f && across < bestAcross))
            {
                best = candidate;
                bestPrimary = primary;
                bestAcross = across;
            }
        }

        return best;
    }

    private void OnCanvasInput(InputEvent @event)
    {
        if (_scroll is null)
        {
            return;
        }

        // Mouse only: dragging the background pans the graph, and Ctrl-wheel zooms it. Plain wheel scrolls
        // through the scroll container, and the toolbar buttons zoom for keyboard and gamepad players.
        if (@event is InputEventMouseButton pressed && pressed.ButtonIndex == MouseButton.Left)
        {
            if (pressed.Pressed)
            {
                _panning = true;
                _panAnchor = GetGlobalMousePosition();
                _panHorizontal = _scroll.ScrollHorizontal;
                _panVertical = _scroll.ScrollVertical;
            }
            else
            {
                _panning = false;
            }

            return;
        }

        if (_panning
            && @event is InputEventMouseMotion
            && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            Vector2 drift = _panAnchor - GetGlobalMousePosition();
            _scroll.ScrollHorizontal = (int)(_panHorizontal + drift.X);
            _scroll.ScrollVertical = (int)(_panVertical + drift.Y);
            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseButton wheel
            && wheel.Pressed
            && (wheel.ButtonIndex == MouseButton.WheelUp || wheel.ButtonIndex == MouseButton.WheelDown)
            && Input.IsKeyPressed(Key.Ctrl))
        {
            ZoomBy(wheel.ButtonIndex == MouseButton.WheelUp ? 1.25f : 0.8f);
            _canvas?.AcceptEvent();
        }
    }
}
