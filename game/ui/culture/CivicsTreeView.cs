using Evenmere.Game.Ui.Research;
using Godot;
using GraphNode = Evenmere.Game.Ui.Research.GraphNode;
using LayoutNode = Evenmere.Game.Ui.Research.LayoutNode;

namespace Evenmere.Game.Ui.Culture;

/// <summary>
/// The civics-tree graph: the same component as M2-18's tech tree, with civic data. <see cref="TreeView"/>,
/// <see cref="TreeNodeView"/> and <see cref="TreeLayout"/> are already content-agnostic (only their namespace
/// ties them to research), so this reuses them by composition instead of copying them: the tree is a private
/// child, and target, queue and zoom requests are forwarded unchanged. If the shared graph ever needs a civic-only
/// tweak, the follow-up is to move the three research graph types into a shared namespace (for example
/// <c>Evenmere.Game.Ui.Graph</c>) rather than forking them here.
/// </summary>
public sealed partial class CivicsTreeView : Control
{
    /// <summary>Raised when a card asks to become the civic target.</summary>
    public event Action<int>? TargetRequested;

    /// <summary>Raised when a card asks to join the civic queue.</summary>
    public event Action<int>? QueueRequested;

    private TreeView? _tree;

    /// <inheritdoc />
    public override void _Ready() => EnsureChrome();

    /// <summary>
    /// Shows the graph's nodes, reusing cards by node index so keyboard and gamepad focus survive a target or
    /// queue swap. Positions come from <see cref="TreeLayout"/>; both lists must hold the same nodes in the same order.
    /// </summary>
    /// <param name="nodes">The nodes to show, with their display data.</param>
    /// <param name="inputs">The layout inputs, parallel to <paramref name="nodes"/>.</param>
    public void SetNodes(IReadOnlyList<GraphNode> nodes, IReadOnlyList<LayoutNode> inputs)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(inputs);
        EnsureChrome();
        _tree?.SetNodes(nodes, inputs);
    }

    /// <summary>Multiplies the zoom, clamped to the shared graph range.</summary>
    /// <param name="factor">The multiplier, above 1 to zoom in.</param>
    public void ZoomBy(float factor) => _tree?.ZoomBy(factor);

    private void EnsureChrome()
    {
        if (_tree is not null)
        {
            return;
        }

        var tree = new TreeView
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        tree.SetAnchorsPreset(LayoutPreset.FullRect);
        tree.TargetRequested += index => TargetRequested?.Invoke(index);
        tree.QueueRequested += index => QueueRequested?.Invoke(index);
        AddChild(tree);
        _tree = tree;
    }
}
