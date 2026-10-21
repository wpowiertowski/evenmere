using Evenmere.Game.Host.Persistence;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Godot;

namespace Evenmere.Game.Ui.Menus;

/// <summary>
/// The slot list both menus share: loading picks a slot, saving names one. In save mode clicking a slot prefills
/// its name (saving over it asks for confirmation first); in load mode clicking a slot loads it straight away.
/// Everything is built in code and takes the shared theme, with full gamepad focus and Esc going back one level.
/// </summary>
public sealed partial class SlotListDialog : PanelContainer
{
    /// <summary>What the dialog is for: picking a slot to load, or naming a slot to save into.</summary>
    public enum DialogMode
    {
        /// <summary>Clicking a slot loads it.</summary>
        Load = 0,

        /// <summary>Typing a name (or picking a slot to overwrite) saves into that slot.</summary>
        Save = 1,
    }

    private const float DialogWidth = 520f;
    private const float DialogHeight = 560f;

    private UiStrings? _strings;
    private DialogMode _mode;
    private Label? _title;
    private VBoxContainer? _slotsBox;
    private Label? _emptyLabel;
    private LineEdit? _nameEdit;
    private Label? _errorLabel;
    private Button? _cancelButton;
    private Button? _actionButton;
    private ConfirmationDialog? _overwriteConfirm;
    private string _pendingSlotId = string.Empty;
    private string _pendingName = string.Empty;

    /// <summary>Raised when a slot is picked for loading.</summary>
    public event Action<string>? LoadRequested;

    /// <summary>Raised when a name is confirmed for saving, with the slot ID and the display name.</summary>
    public event Action<string, string>? SaveRequested;

    /// <summary>Raised when the dialog closes for any reason.</summary>
    public event Action? Closed;

    /// <inheritdoc />
    public override void _Ready()
    {
        PanelTheme.Apply(this);
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -260f;
        OffsetTop = -280f;
        OffsetRight = 260f;
        OffsetBottom = 280f;
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
        CustomMinimumSize = new Vector2(DialogWidth, DialogHeight);
        Visible = false;
        Build();
    }

    /// <summary>Shows the dialog for loading.</summary>
    /// <param name="strings">The display text.</param>
    /// <param name="slots">The slots, most recent first.</param>
    public void ShowLoad(UiStrings strings, IReadOnlyList<SaveSlotInfo> slots)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(slots);

        _strings = strings;
        _mode = DialogMode.Load;
        Rebuild(strings, slots, string.Empty);
        Visible = true;
        FocusFirst();
    }

    /// <summary>Shows the dialog for saving, with the name field prefilled.</summary>
    /// <param name="strings">The display text.</param>
    /// <param name="slots">The slots, most recent first.</param>
    /// <param name="suggestedName">The prefilled name.</param>
    public void ShowSave(UiStrings strings, IReadOnlyList<SaveSlotInfo> slots, string suggestedName)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(suggestedName);

        _strings = strings;
        _mode = DialogMode.Save;
        Rebuild(strings, slots, suggestedName);
        Visible = true;
        _nameEdit?.GrabFocus();
        _nameEdit?.SelectAll();
    }

    /// <summary>Refreshes the slot rows, for example after a save added a slot.</summary>
    /// <param name="slots">The slots, most recent first.</param>
    public void RefreshSlots(IReadOnlyList<SaveSlotInfo> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (_strings is not null)
        {
            RebuildRows(_strings, slots);
        }
    }

    /// <summary>Hides the dialog.</summary>
    public void Dismiss()
    {
        Visible = false;
        _overwriteConfirm?.Hide();
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

    private void Build()
    {
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

        var name = new LineEdit
        {
            FocusMode = FocusModeEnum.All,
            Visible = false,
            MaxLength = 80,
        };
        name.TextSubmitted += _ => ConfirmSave();
        top.AddChild(name);
        _nameEdit = name;

        var error = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false,
        };
        top.AddChild(error);
        _errorLabel = error;

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        top.AddChild(scroll);

        var slots = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        slots.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(slots);
        _slotsBox = slots;

        var empty = new Label();
        slots.AddChild(empty);
        _emptyLabel = empty;

        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 8);
        bottom.Alignment = BoxContainer.AlignmentMode.End;
        top.AddChild(bottom);

        var cancel = new Button { FocusMode = FocusModeEnum.All };
        cancel.Pressed += Dismiss;
        bottom.AddChild(cancel);
        _cancelButton = cancel;

        var action = new Button { FocusMode = FocusModeEnum.All, Visible = false };
        action.Pressed += ConfirmSave;
        bottom.AddChild(action);
        _actionButton = action;

        var confirm = new ConfirmationDialog();
        confirm.Confirmed += () => SaveRequested?.Invoke(_pendingSlotId, _pendingName);
        AddChild(confirm);
        _overwriteConfirm = confirm;
    }

    private void Rebuild(UiStrings strings, IReadOnlyList<SaveSlotInfo> slots, string suggestedName)
    {
        if (_title is not null)
        {
            _title.Text = strings.Get(_mode == DialogMode.Load ? "ui.load.title" : "ui.save.title");
        }

        if (_nameEdit is not null)
        {
            _nameEdit.Visible = _mode == DialogMode.Save;
            _nameEdit.Text = suggestedName;
            _nameEdit.PlaceholderText = strings.Get("ui.save.name");
        }

        ShowError(null);

        if (_cancelButton is not null)
        {
            _cancelButton.Text = strings.Get(_mode == DialogMode.Load ? "ui.load.close" : "ui.save.cancel");
            _cancelButton.TooltipText = strings.Get(_mode == DialogMode.Load ? "ui.load.close.tip" : "ui.save.cancel.tip");
        }

        if (_actionButton is not null)
        {
            _actionButton.Visible = _mode == DialogMode.Save;
            _actionButton.Text = strings.Get("ui.save.save");
            _actionButton.TooltipText = strings.Get("ui.save.save.tip");
        }

        if (_overwriteConfirm is not null)
        {
            _overwriteConfirm.Title = strings.Get("ui.save.overwrite_title");
            _overwriteConfirm.OkButtonText = strings.Get("ui.save.overwrite.confirm");
            _overwriteConfirm.CancelButtonText = strings.Get("ui.save.cancel");
        }

        RebuildRows(strings, slots);
    }

    private void RebuildRows(UiStrings strings, IReadOnlyList<SaveSlotInfo> slots)
    {
        if (_slotsBox is null || _emptyLabel is null)
        {
            return;
        }

        foreach (Node child in _slotsBox.GetChildren())
        {
            if (!ReferenceEquals(child, _emptyLabel))
            {
                _slotsBox.RemoveChild(child);
                child.QueueFree();
            }
        }

        _emptyLabel.Visible = slots.Count == 0;
        if (slots.Count == 0)
        {
            _emptyLabel.Text = strings.Get("ui.load.empty");
        }

        foreach (SaveSlotInfo slot in slots)
        {
            var row = new Button
            {
                Text = MenuText.SlotDetail(strings, slot),
                FocusMode = FocusModeEnum.All,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            string captured = slot.SlotId;
            string name = slot.Name;
            row.Pressed += () => OnSlotPressed(captured, name);
            _slotsBox.AddChild(row);
            _slotsBox.MoveChild(row, _slotsBox.GetChildCount() - 2);
        }
    }

    private void OnSlotPressed(string slotId, string name)
    {
        if (_mode == DialogMode.Load)
        {
            LoadRequested?.Invoke(slotId);
            return;
        }

        if (_nameEdit is not null)
        {
            _nameEdit.Text = name;
            _nameEdit.GrabFocus();
        }
    }

    private void ConfirmSave()
    {
        if (_strings is null || _nameEdit is null)
        {
            return;
        }

        string name = _nameEdit.Text.Trim();
        if (name.Length == 0)
        {
            ShowError(_strings.Get("ui.save.name_needed"));
            _nameEdit.GrabFocus();
            return;
        }

        ShowError(null);
        string slotId = SaveSlots.SanitizeSlotId(name);
        if (File.Exists(SaveSlots.SavePath(slotId)) && _overwriteConfirm is not null)
        {
            _pendingSlotId = slotId;
            _pendingName = name;
            _overwriteConfirm.DialogText = _strings.Format("ui.save.overwrite", ("name", name));
            _overwriteConfirm.PopupCentered();
            return;
        }

        SaveRequested?.Invoke(slotId, name);
    }

    private void ShowError(string? message)
    {
        if (_errorLabel is null)
        {
            return;
        }

        _errorLabel.Visible = message is not null;
        _errorLabel.Text = message ?? string.Empty;
    }

    private void FocusFirst()
    {
        if (_slotsBox is null)
        {
            return;
        }

        foreach (Node child in _slotsBox.GetChildren())
        {
            if (child is Button row && row.Visible)
            {
                row.GrabFocus();
                return;
            }
        }

        _cancelButton?.GrabFocus();
    }
}
