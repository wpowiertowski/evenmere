using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Godot;

namespace Evenmere.Game.Ui.Menus;

/// <summary>
/// The settings placeholder both menus open: a stub panel saying the settings arrive in a later milestone (M4-05
/// owns them). It closes on its button or on Esc, like every other dialog.
/// </summary>
public sealed partial class SettingsStub : PanelContainer
{
    private Label? _title;
    private Label? _body;
    private Button? _close;

    /// <summary>Raised when the stub closes for any reason.</summary>
    public event Action? Closed;

    /// <inheritdoc />
    public override void _Ready()
    {
        PanelTheme.Apply(this);
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -210f;
        OffsetTop = -130f;
        OffsetRight = 210f;
        OffsetBottom = 130f;
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
        CustomMinimumSize = new Vector2(420f, 0);
        Visible = false;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        AddChild(margin);

        var top = new VBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        margin.AddChild(top);

        var title = new Label();
        top.AddChild(title);
        _title = title;

        var body = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(body);
        _body = body;

        var bottom = new HBoxContainer();
        bottom.Alignment = BoxContainer.AlignmentMode.End;
        top.AddChild(bottom);

        var close = new Button { FocusMode = FocusModeEnum.All };
        close.Pressed += Dismiss;
        bottom.AddChild(close);
        _close = close;
    }

    /// <summary>Shows the stub.</summary>
    /// <param name="strings">The display text.</param>
    public void ShowSettings(UiStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        if (_title is not null)
        {
            _title.Text = strings.Get("ui.settings.title");
        }

        if (_body is not null)
        {
            _body.Text = strings.Get("ui.settings.stub");
        }

        if (_close is not null)
        {
            _close.Text = strings.Get("ui.settings.close");
            _close.TooltipText = strings.Get("ui.settings.close.tip");
        }

        Visible = true;
        _close?.GrabFocus();
    }

    /// <summary>Hides the stub.</summary>
    public void Dismiss()
    {
        Visible = false;
        Closed?.Invoke();
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || @event.IsEcho())
        {
            return;
        }

        if (InputMap.HasAction("tool_cancel") && @event.IsActionPressed("tool_cancel"))
        {
            Dismiss();
            GetViewport().SetInputAsHandled();
        }
    }
}
