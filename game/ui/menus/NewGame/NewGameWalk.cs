using Godot;

namespace Evenmere.Game.Ui.Menus.NewGame;

/// <summary>
/// The scripted <c>ui_*</c> reachability probe for the new-game screen (M2-25, debug only). The automated focus
/// walk (<c>--focus-walk</c>) starts a game directly and never shows the main menu, so it cannot reach this
/// screen; instead, <c>--newgame-walk</c> adds this probe, which waits for the preview in each island mode and
/// then walks every enabled control with the real directional actions, exactly the walk's criterion: every
/// control must be reachable from the first focus, and focus must be able to return. It prints one line per
/// phase plus any failures, then quits 0 on success and 1 on failure. It never presses <c>ui_accept</c>, so
/// Start is never chosen and no game begins.
/// </summary>
internal sealed partial class NewGameWalk : Node
{
    private static readonly string[] _directions = ["ui_left", "ui_right", "ui_up", "ui_down"];

    private readonly NewGameScreen _screen;
    private bool _running;

    /// <summary>Initializes a new instance of the <see cref="NewGameWalk"/> class.</summary>
    /// <param name="screen">The open new-game screen to walk.</param>
    public NewGameWalk(NewGameScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        _screen = screen;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        if (!_running)
        {
            _running = true;
            RunAsync();
        }
    }

    private static string Label(Control control)
    {
        if (control is Button button && !string.IsNullOrEmpty(button.Text))
        {
            return $"{control.Name}('{button.Text}')";
        }

        if (control is LineEdit line && !string.IsNullOrEmpty(line.Text))
        {
            return $"{control.Name}('{line.Text}')";
        }

        return $"{control.Name}({control.GetType().Name})";
    }

    private async void RunAsync()
    {
        try
        {
            bool ok = true;
            ok &= await WalkPhaseAsync("starter", static screen => screen.SelectStarter());
            ok &= await WalkPhaseAsync("generated", static screen => screen.SelectGenerated(7, 320));
            GD.Print(ok
                ? "[Evenmere] newgame-walk: NEWGAMEWALK OK phases=2."
                : "[Evenmere] newgame-walk: NEWGAMEWALK FAIL.");
            GetTree().Quit(ok ? 0 : 1);
        }
        catch (Exception ex)
        {
            GD.Print($"[Evenmere] newgame-walk FAIL: the walk itself threw: {ex.GetType().Name}: {ex.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task<bool> WalkPhaseAsync(string phase, Action<NewGameScreen> open)
    {
        open(_screen);
        if (!await WaitReadyAsync())
        {
            GD.Print($"[Evenmere] newgame-walk FAIL: phase '{phase}': the preview never landed.");
            return false;
        }

        // One frame, so the ready state (the newly enabled Start button) is in the tree before collecting.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        List<Control> nodes = new();
        CollectFocusable(_screen, nodes);
        if (nodes.Count == 0)
        {
            GD.Print($"[Evenmere] newgame-walk FAIL: phase '{phase}': no focusable controls.");
            return false;
        }

        Control first = nodes[0];
        first.GrabFocus();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (GetViewport().GuiGetFocusOwner() != first)
        {
            GD.Print($"[Evenmere] newgame-walk FAIL: phase '{phase}': the first focus {Label(first)} did not take focus.");
            return false;
        }

        List<(int From, string Direction, int To)> edges = new();
        List<(int Node, string Direction)> probes = new();
        foreach (int i in Enumerable.Range(0, nodes.Count))
        {
            foreach (string direction in _directions)
            {
                probes.Add((i, direction));
            }
        }

        // Passes mirror the walk: a pass over skipped pairs (a control hidden at its turn) runs up to twice more.
        List<(int Node, string Direction)> skipped = new();
        int pass = 0;
        while (true)
        {
            foreach ((int node, string direction) in probes)
            {
                if (!IsUsable(nodes[node]))
                {
                    skipped.Add((node, direction));
                    continue;
                }

                Control current = nodes[node];
                current.GrabFocus();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (GetViewport().GuiGetFocusOwner() != current)
                {
                    skipped.Add((node, direction));
                    continue;
                }

                Input.ParseInputEvent(new InputEventAction { Action = direction, Pressed = true });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Control? owner = GetViewport().GuiGetFocusOwner();
                Input.ParseInputEvent(new InputEventAction { Action = direction, Pressed = false });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                if (owner is null || owner == current)
                {
                    continue;
                }

                int target = nodes.IndexOf(owner);
                if (target >= 0)
                {
                    edges.Add((node, direction, target));
                }
            }

            if (skipped.Count == 0 || pass >= 2)
            {
                break;
            }

            probes = skipped;
            skipped = new List<(int Node, string Direction)>();
            pass++;
        }

        int count = nodes.Count;
        bool[] reached = new bool[count];
        Queue<int> queue = new();
        reached[0] = true;
        queue.Enqueue(0);
        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            foreach ((int from, string _, int to) in edges)
            {
                if (from == node && !reached[to])
                {
                    reached[to] = true;
                    queue.Enqueue(to);
                }
            }
        }

        bool[] returns = new bool[count];
        Queue<int> back = new();
        returns[0] = true;
        back.Enqueue(0);
        while (back.Count > 0)
        {
            int node = back.Dequeue();
            foreach ((int from, string _, int to) in edges)
            {
                if (to == node && !returns[from])
                {
                    returns[from] = true;
                    back.Enqueue(from);
                }
            }
        }

        int unreachable = 0;
        int traps = 0;
        int[] outgoing = new int[count];
        foreach ((int from, string _, int _) in edges)
        {
            outgoing[from]++;
        }

        for (int i = 0; i < count; i++)
        {
            if (!reached[i])
            {
                unreachable++;
                GD.Print($"[Evenmere] newgame-walk FAIL: phase '{phase}': {Label(nodes[i])} is unreachable from the first focus.");
            }
            else if (count > 1 && outgoing[i] == 0)
            {
                traps++;
                GD.Print($"[Evenmere] newgame-walk FAIL: phase '{phase}': {Label(nodes[i])} is a trap: no direction moves focus away.");
            }
            else if (!returns[i])
            {
                traps++;
                GD.Print($"[Evenmere] newgame-walk FAIL: phase '{phase}': {Label(nodes[i])} is a trap: focus cannot return to the first focus.");
            }
        }

        GD.Print($"[Evenmere] newgame-walk: phase '{phase}': controls={count} reached={count - unreachable} traps={traps} seed=7 size=320.");
        return unreachable == 0 && traps == 0;
    }

    private async Task<bool> WaitReadyAsync()
    {
        // Generation of a 320 island takes about a second in debug; ten seconds is already a failure, but the
        // timeout stays generous so a slow shared machine fails loudly on the walk instead of timing out here.
        for (int i = 0; i < 3600; i++)
        {
            if (!IsInstanceValid(_screen))
            {
                return false;
            }

            if (_screen.IsReady)
            {
                return true;
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        return false;
    }

    private static void CollectFocusable(Control node, List<Control> into)
    {
        if (node.FocusMode == Control.FocusModeEnum.All && node.IsVisibleInTree() && !IsDisabled(node))
        {
            into.Add(node);
        }

        foreach (Node child in node.GetChildren())
        {
            if (child is Control control)
            {
                CollectFocusable(control, into);
            }
        }
    }

    private static bool IsDisabled(Control control) =>
        (control is Button button && button.Disabled) || (control is LineEdit line && !line.Editable);

    private static bool IsUsable(Control control) =>
        GodotObject.IsInstanceValid(control) && control.IsVisibleInTree() && !IsDisabled(control);
}
