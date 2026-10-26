using Evenmere.Content;
using Evenmere.Content.Locale;
using Evenmere.Game.Controls;
using Evenmere.Game.Host;
using Evenmere.Game.Host.Persistence;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Menus;
using Evenmere.Sim;
using Godot;

namespace Evenmere.Game.Ui.Debug;

/// <summary>
/// The cheat console (ADR-0012): a debug-build-only panel that parses lines through <see cref="CheatParser"/>
/// and enqueues the resulting cheat commands like any other command (ADR-0006). It opens with the backtick
/// (<c>debug_console</c> action, keyboard only) and is gamepad-unreachable by design: the input field takes mouse
/// clicks only (<see cref="Control.FocusModeEnum.Click"/>), no gamepad button opens it, and the focus walk skips it.
/// Rejections arrive a tick later through the drained events and are matched by reference, so a refused cheat
/// prints its reason instead of a stale promise.
/// </summary>
public sealed partial class CheatConsole : CanvasLayer
{
    private const string ShowFlag = "--show-cheat-console";
    private const string SaveQuitFlag = "--debug-save-and-quit";
    private const string ShowLoadListFlag = "--show-load-list";
    private const int MaxLines = 80;
    private const int OwnRingSize = 32;

    private readonly SimHost _host;
    private readonly DebugOverlay? _overlay;
    private readonly Command?[] _ownCommands = new Command[OwnRingSize];
    private int _ownNext;
    private PanelContainer? _panel;
    private RichTextLabel? _history;
    private LineEdit? _input;
    private readonly List<string> _lines = new();
    private readonly List<string> _pendingSubmits = new();
    private string? _saveQuitSlot;
    private bool _saveQuitDone;
    private bool _showLoadList;

    /// <summary>Initializes a new instance of the <see cref="CheatConsole"/> class.</summary>
    /// <param name="host">The running simulation host.</param>
    /// <param name="overlay">The debug overlay the <c>overlay</c> command toggles, or <see langword="null"/> for none.</param>
    public CheatConsole(SimHost host, DebugOverlay? overlay = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _overlay = overlay;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        // Layer 2, like the pause menu: above the HUD (layer 1).
        Layer = 2;

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        panel.OffsetLeft = 8;
        panel.OffsetRight = -8;
        panel.OffsetTop = -220;
        panel.OffsetBottom = -8;
        panel.Visible = false;
        panel.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(panel);
        _panel = panel;

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 4);
        panel.AddChild(rows);

        var history = new RichTextLabel();
        history.BbcodeEnabled = false;
        history.ScrollFollowing = true;
        history.CustomMinimumSize = new Vector2(0, 160);
        history.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        history.FocusMode = Control.FocusModeEnum.None;
        rows.AddChild(history);
        _history = history;

        var input = new LineEdit();
        input.PlaceholderText = "cheat (grant timber 10, tech net_weaving, help)";
        input.FocusMode = Control.FocusModeEnum.Click;
        input.TextSubmitted += OnSubmitted;
        rows.AddChild(input);
        _input = input;

        Print(CheatParser.Usage);

        _host.EventsDrained += OnEvents;

        // Screenshots: --show-cheat-console opens the panel, with each --show-cheat-console=<line> submitting one
        // line in order. --debug-save-and-quit=<slot> saves the running world once and quits, for seeding saves.
        // --show-load-list opens the load dialog over whatever is showing, for the load-list shot.
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == ShowFlag)
            {
                panel.Visible = true;
            }
            else if (arg.StartsWith(ShowFlag + "=", StringComparison.Ordinal))
            {
                panel.Visible = true;
                _pendingSubmits.Add(arg[(ShowFlag.Length + 1)..]);
            }
            else if (arg.StartsWith(SaveQuitFlag + "=", StringComparison.Ordinal))
            {
                _saveQuitSlot = arg[(SaveQuitFlag.Length + 1)..];
            }
            else if (arg == ShowLoadListFlag)
            {
                _showLoadList = true;
            }
        }

        if (_showLoadList)
        {
            ShowLoadList();
        }
    }

    /// <summary>Opens the load list over whatever is showing, for the load-list screenshot (debug builds only).</summary>
    private void ShowLoadList()
    {
        LocaleTable table = LocaleTable.Load(ContentPaths.Resolve(), SimHost.DefaultLanguage).Table;
        var dialog = new SlotListDialog();
        AddChild(dialog);
        dialog.ShowLoad(new UiStrings(table), SaveSlots.ListSlots());
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _host.EventsDrained -= OnEvents;
    }

    /// <inheritdoc />
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.DebugConsole))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!_host.IsRunning)
        {
            return;
        }

        while (_pendingSubmits.Count > 0)
        {
            string line = _pendingSubmits[0];
            _pendingSubmits.RemoveAt(0);
            Submit(line);
        }

        if (_saveQuitSlot is { Length: > 0 } slot && !_saveQuitDone)
        {
            _saveQuitDone = true;
            SaveService.Save(_host, new CommandLogRecorder(), slot, slot);
            GD.Print($"[Evenmere] debug: saved slot '{slot}' and quitting.");
            GetTree().Quit();
        }
    }

    private void Toggle()
    {
        if (_panel is null || _input is null)
        {
            return;
        }

        _panel.Visible = !_panel.Visible;
        if (_panel.Visible)
        {
            _input.GrabFocus();
        }
        else
        {
            _input.ReleaseFocus();
        }
    }

    private void OnSubmitted(string text)
    {
        Submit(text);
        _input?.Clear();
    }

    private void OnEvents(IReadOnlyList<SimEvent> events)
    {
        foreach (SimEvent @event in events)
        {
            if (@event is CommandRejected rejected && IsOwn(rejected.Command))
            {
                Print($"Rejected: {rejected.Detail}");
            }
        }
    }

    private bool IsOwn(Command command)
    {
        foreach (Command? own in _ownCommands)
        {
            if (ReferenceEquals(own, command))
            {
                return true;
            }
        }

        return false;
    }

    private void EnqueueOwn(Command command)
    {
        _host.Enqueue(command);
        _ownCommands[_ownNext] = command;
        _ownNext = (_ownNext + 1) % OwnRingSize;
    }

    private void Submit(string text)
    {
        Print("> " + text);
        if (!_host.IsRunning)
        {
            Print("No simulation is running.");
            return;
        }

        if (string.Equals(text.Trim(), "overlay", StringComparison.OrdinalIgnoreCase))
        {
            // View-only, like help: no sim command, just the overlay switch (the user's keyboard has no F keys).
            if (_overlay is null)
            {
                Print("No overlay.");
                return;
            }

            _overlay.Toggle();
            Print(_overlay.IsOpen ? "Overlay on." : "Overlay off.");
            return;
        }

        if (!_host.Simulation.CheatsUsed)
        {
            Print("Cheats are off for this save: start a new game in a debug build or with --cheats.");
            return;
        }

        if (!CheatParser.TryParse(text, _host.Content, out Command? command, out string error) || command is null)
        {
            Print(error);
            return;
        }

        EnqueueOwn(command);
        Print("Enqueued; it applies on the next tick.");
    }

    private void Print(string line)
    {
        _lines.Add(line);
        while (_lines.Count > MaxLines)
        {
            _lines.RemoveAt(0);
        }

        if (_history is not null)
        {
            _history.Text = string.Join("\n", _lines);
        }
    }
}
