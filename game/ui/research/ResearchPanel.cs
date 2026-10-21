using Evenmere.Core.Ids;
using Evenmere.Game.Controls;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Godot;

namespace Evenmere.Game.Ui.Research;

/// <summary>
/// The research list panel: techs grouped by state with cost, progress, ETA and inspiration, opened and closed
/// by <c>ui_research</c> and <c>tool_cancel</c> and by the HUD-adjacent toggles. It owns a
/// <see cref="TreePanelController{TechId}"/>; everything shared lives there.
/// </summary>
public sealed partial class ResearchPanel : PanelContainer
{
    private TreePanelController<TechId>? _controller;

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <summary>Gets or sets the path to the civics panel, hidden when this panel opens.</summary>
    [Export]
    public NodePath OtherPath { get; set; } = new("../CulturePanel");

    /// <inheritdoc />
    public override void _Ready()
    {
        _controller = new TreePanelController<TechId>(
            this,
            "ui.research.title",
            InputActions.UiResearch,
            static host => new ResearchViewModel(
                host.Content,
                new UiStrings(host.Locale),
                host.Query.Research,
                host.Query.Progression));
        _controller.Opened += HideOther;
        _controller.Ready(HostPath);
    }

    /// <inheritdoc />
    public override void _ExitTree() => _controller?.Exit();

    /// <inheritdoc />
    public override void _Process(double delta) => _controller?.Process(delta);

    /// <inheritdoc />
    public override void _Input(InputEvent @event) => _controller?.HandleInput(@event);

    /// <summary>Opens the panel if it is closed and closes it if it is open.</summary>
    public void Toggle() => _controller?.Toggle();

    private void HideOther()
    {
        if (GetNodeOrNull<Control>(OtherPath) is { } other)
        {
            other.Visible = false;
        }
    }
}
