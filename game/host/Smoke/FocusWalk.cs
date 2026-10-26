using Evenmere.Game.Host;
using Evenmere.Game.Ui.BuildMenu;
using Evenmere.Game.Ui.Chains;
using Evenmere.Game.Ui.Culture;
using Evenmere.Game.Ui.Hud;
using Evenmere.Game.Ui.Inspector;
using Evenmere.Game.Ui.Menus;
using Evenmere.Game.Ui.Notifications;
using Evenmere.Game.Ui.Research;
using Evenmere.Game.Rendering.Overlays;
using Evenmere.Game.Ui.Stats;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Host.Smoke;

/// <summary>
/// The automated focus walk (M2-26): the M2 gate's gamepad evidence. It opens every panel the game can open and
/// asserts that every enabled control is reachable by the built-in <c>ui_*</c> focus navigation from the panel's
/// first focus, with no traps. Panels are discovered, not hard-coded: the known panels open through their real
/// paths (toggle methods, the building inspector on a real building, <c>ui_cancel</c> for the pause menu), and any
/// other top-level control (a future civics board, a new dialog) is shown and walked too, so rules stay covered as
/// panels land. The debug overlay and the cheat console are skipped on purpose: they are debug-only and
/// gamepad-unreachable by design (ADR-0012). Prints a per-panel report and quits 0 on success, 1 on failure.
/// </summary>
public sealed partial class FocusWalk : Node
{
    private static readonly string[] _directions = ["ui_left", "ui_right", "ui_up", "ui_down"];

    private readonly SimHost _host;
    private Node? _root;
    private readonly List<WalkPanel> _panels = new();
    private int _panelIndex = -1;
    private WalkState? _state;
    private int _waitFrames;
    private bool _done;

    /// <summary>Initializes a new instance of the <see cref="FocusWalk"/> class.</summary>
    /// <param name="host">The running simulation host; its parent is the scene root holding the panels.</param>
    public FocusWalk(SimHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _root = _host.GetParent();
        if (_root is null)
        {
            Fail("The host has no panels to walk.");
            return;
        }

        // The sim keeps ticking under the walk and panels rebuild themselves (construction completes, toasts
        // expire); freeze it so the walked controls stay alive. The walk quits the run, so nothing restores the speed.
        _host.Speed = SimSpeed.Paused;

        DiscoverPanels();
        foreach (WalkPanel panel in _panels)
        {
            GD.Print($"[Evenmere] focus walk: discovered '{panel.Name}'.");
        }

        GD.Print($"[Evenmere] focus walk: {_panels.Count} panels at 1280x800.");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_done)
        {
            return;
        }

        if (_waitFrames > 0)
        {
            _waitFrames--;
            return;
        }

        if (_panelIndex < 0)
        {
            NextPanel();
            return;
        }

        Step();
    }

    private void DiscoverPanels()
    {

        _panels.Clear();
        if (_root is null)
        {
            return;
        }

        var known = new HashSet<string>(StringComparer.Ordinal);
        void Add(string name, Control node, Action open, bool expect)
        {
            _panels.Add(new WalkPanel(name, node, open, expect));
            known.Add(name);
        }

        Control? Node(string path) => _root.GetNodeOrNull<Control>(path);

        if (_root.GetNodeOrNull<HudRoot>("Hud/HudRoot") is { } hud)
        {
            Add("Hud", hud, static () => { }, expect: true);
        }

        if (Node("PanelToggles") is { } toggles)
        {
            Add("PanelToggles", toggles, static () => { }, expect: true);
        }

        if (_root.GetNodeOrNull<BuildMenu>("BuildMenu") is { } build)
        {
            Add("BuildMenu", build, () => build.ShowMenu(), expect: true);
        }

        if (_root.GetNodeOrNull<InspectorPanel>("InspectorPanel") is { } inspector)
        {
            Add("Inspector", inspector, () => OpenInspector(inspector), expect: true);
        }

        if (_root.GetNodeOrNull<ResearchPanel>("ResearchPanel") is { } research)
        {
            Add("Research", research, () => { if (!research.Visible) research.Toggle(); }, expect: true);
        }

        if (_root.GetNodeOrNull<CulturePanel>("CulturePanel") is { } culture)
        {
            Add("Culture", culture, () => { if (!culture.Visible) culture.Toggle(); }, expect: true);
        }

        if (_root.GetNodeOrNull<ChainViewer>("ChainViewer") is { } chains)
        {
            Add("ChainViewer", chains, () => OpenChains(chains), expect: true);
        }

        if (_root.GetNodeOrNull<NotificationFeed>("NotificationFeed") is { } feed)
        {
            Add("NotificationFeed", feed, static () => { }, expect: false);
        }

        if (_root.GetNodeOrNull<StatsPanel>("StatsPanel") is { } stats)
        {
            Add("Statistics", stats, () => { if (!stats.Visible) stats.Toggle(); }, expect: true);
        }

        if (_root.GetNodeOrNull<PauseMenu>("PauseMenu/Root") is { } pause)
        {
            Add("PauseMenu", pause, () => OpenPause(pause), expect: true);
        }

        if (_root.GetNodeOrNull<OverlayPanel>("OverlayPanel") is { } overlays)
        {
            Add("OverlayPanel", overlays, () => { if (!overlays.Visible) overlays.Toggle(); }, expect: true);
        }

        // The walk names above differ from the scene node names; keep the node names out of the generic pass too.
        known.Add("Hud");
        known.Add("PauseMenu");
        known.Add("ResearchPanel");
        known.Add("CulturePanel");
        known.Add("InspectorPanel");
        known.Add("ChainViewer");
        known.Add("BuildMenu");
        known.Add("NotificationFeed");
        known.Add("PanelToggles");
        known.Add("StatsPanel");
        known.Add("OverlayPanel");

        // Any other top-level control is a panel this card does not know (a future board or dialog): show it and
        // walk it, so coverage survives new panels without a walk change. The walk names below are the scene node
        // names, so the known panels are never walked twice.
        foreach (Node child in _root.GetChildren())
        {
            if (child is Control control
                && !known.Contains(child.Name)
                && child is not Ui.Debug.DebugOverlay
                && child is not Ui.Debug.CheatConsole)
            {
                string name = child.Name;
                Add(name, control, () => control.Visible = true, expect: false);
            }
        }
    }

    private void OpenInspector(InspectorPanel inspector)
    {
        foreach (BuildingInfo building in _host.Query.Map.ActiveBuildings)
        {
            inspector.Open(building.Id);
            return;
        }

        GD.Print("[Evenmere] focus walk: Inspector has no building to open; walking it closed.");
    }

    private void OpenChains(ChainViewer chains)
    {
        if (_host.Content.Goods.Names.Length > 0)
        {
            chains.Show(_host.Content.Goods.Names[0]);
        }
    }

    private static void OpenPause(Control pause)
    {
        // The pause menu toggles on tool_cancel (Esc), the same key a player uses; ui_cancel does something
        // else, so sending the wrong action walks the closed menu's corner button instead of the menu.
        pause.Visible = true;
        Input.ParseInputEvent(new InputEventAction { Action = "tool_cancel", Pressed = true });
        Input.ParseInputEvent(new InputEventAction { Action = "tool_cancel", Pressed = false });
    }

    private void NextPanel()
    {
        AdvancePanel();
    }

    private void Step()
    {
        try
        {
            StepInner();
        }
        catch (Exception exception)
        {
            // A panel that rebuilds itself mid-walk (a freed toast button, a refreshed list) must fail its own
            // panel, never the whole run: record it and move on so the walk always terminates.
            if (_state is { } failed)
            {
                failed.Problems.Add($"the walk itself threw: {exception.GetType().Name}: {exception.Message}");
                failed.Panel.Failed = true;
            }

            GD.Print($"[Evenmere] focus walk FAIL: step threw: {exception.GetType().Name}: {exception.Message}");
            AdvancePanel();
        }
    }

    private void StepInner()
    {
        WalkState? state = _state;
        if (state is null)
        {
            NextPanel();
            return;
        }

        if (!state.Collected)
        {
            Collect(state);
            return;
        }

        if (state.Probe < state.Probes.Count)
        {
            Probe(state);
            return;
        }

        Analyze(state);
        AdvancePanel();
    }

    private void AdvancePanel()
    {
        _state = null;
        _panelIndex++;
        if (_panelIndex >= _panels.Count)
        {
            Finish();
            return;
        }

        WalkPanel panel = _panels[_panelIndex];
        HideOthers(panel.Node);
        panel.Open();
        _waitFrames = 5;
        _state = new WalkState(panel);
    }

    private void Collect(WalkState state)
    {
        state.Nodes.Clear();
        CollectFocusable(state.Panel.Node, state.Nodes);
        state.Collected = true;

        if (state.Nodes.Count == 0)
        {
            if (state.Panel.Expect)
            {
                state.Problems.Add("expected focusable controls but found none.");
                state.Panel.Failed = true;
                GD.Print($"[Evenmere] focus walk FAIL: {state.Panel.Name}: expected focusable controls but found none.");
            }
            else
            {
                GD.Print($"[Evenmere] focus walk: {state.Panel.Name}: no focusable controls (empty panel).");
            }

            state.Probes.Clear();
            return;
        }

        Control first = state.Nodes[0];
        first.GrabFocus();
        Control? owner = GetViewport().GuiGetFocusOwner();
        if (owner != first)
        {
            state.Problems.Add($"the first focus {Label(first)} did not take focus.");
        }

        BuildProbes(state);
        state.Edges.Clear();
        state.Reading = false;
    }

    private void Probe(WalkState state)
    {
        // Tooltip links and other just-built controls are adopted only when the engine itself reaches them
        // (see below); nothing else merges mid-walk, so the probe list is stable and every read attributes to
        // the probe that pressed.
        if (state.Probe >= state.Probes.Count)
        {
            // A pass with skipped probes (a control hidden at its turn) gets up to two more passes over just
            // the skipped pairs; then the analysis judges what was observed.
            if (state.Skipped.Count > 0 && state.Pass < 2)
            {
                state.Probes.AddRange(state.Skipped);
                state.Skipped.Clear();
                state.Probe = 0;
                state.Pass++;
                return;
            }

            Analyze(state);
            AdvancePanel();
            return;
        }

        (int node, string direction) = state.Probes[state.Probe];
        if (!IsUsable(state, node))
        {
            GD.Print($"[Evenmere] focus walk: {state.Panel.Name} probe skip {Label(state.Nodes[node])} usable={GodotObject.IsInstanceValid(state.Nodes[node]) && state.Nodes[node].IsVisibleInTree()}.");
            state.Skipped.Add((node, direction));
            state.Probe++;
            return;
        }

        Control current = state.Nodes[node];
        if (!state.Reading)
        {
            current.GrabFocus();
            Control? grabbed = GetViewport().GuiGetFocusOwner();
            if (grabbed != current)
            {
                state.Notes.Add($"{Label(current)} refused focus (another control holds it)." );
                state.Skipped.Add((node, direction));
                state.Probe++;
                return;
            }

            Input.ParseInputEvent(new InputEventAction { Action = direction, Pressed = true });
            state.Reading = true;
            return;
        }

        Control? owner = GetViewport().GuiGetFocusOwner();
        Input.ParseInputEvent(new InputEventAction { Action = direction, Pressed = false });
        state.Reading = false;

        if (owner is null || owner == current)
        {
            // A gamepad player scrolls a clipped list and presses again; do the same once before giving up.
            if (!state.ScrollRetried && ScrollToward(state.Panel.Node, direction))
            {
                GD.Print($"[Evenmere] focus walk: {state.Panel.Name} scrolled {direction} and retrying {Label(current)}.");
                state.ScrollRetried = true;
                return;
            }

            state.ScrollRetried = false;
            state.Probe++;
            return;
        }

        state.ScrollRetried = false;
        state.Probe++;

        int target = state.Nodes.IndexOf(owner);
        if (target < 0)
        {
            // The engine reached a control the walk has never seen (a just-opened tooltip link): adopt it for
            // the edge, which proves its reachability, and probe it next while its tooltip is still open.
            // Inserted right after the just-completed probe, never before an in-flight one.
            if (IsWalkable(owner))
            {
                target = state.Nodes.Count;
                state.Nodes.Add(owner);
                state.Adopted.Add(target);
                GD.Print($"[Evenmere] focus walk: {state.Panel.Name} new control: {Describe(owner)}." );
                foreach (string next in _directions)
                {
                    state.Probes.Insert(state.Probe, (target, next));
                }

                state.Edges.Add((node, direction, target));
                return;
            }

            string where = owner is null ? "nowhere" : $"{Label(owner)} at {owner.GetPath()}";
            state.Notes.Add($"{Label(current)} {direction} leaves the panel to {where}.");
            return;
        }

        state.Edges.Add((node, direction, target));
    }

    /// <summary>
    /// Nudges every scroll container under the panel one page toward a direction, like a gamepad player paging
    /// a clipped list before pressing again. Returns whether anything actually scrolled.
    /// </summary>
    /// <param name="node">The panel to page.</param>
    /// <param name="direction">The <c>ui_*</c> direction of the failed probe.</param>
    /// <returns><see langword="true"/> when a container moved.</returns>
    private static bool ScrollToward(Control node, string direction)
    {
        bool moved = false;
        foreach (Node child in node.GetChildren())
        {
            if (child is ScrollContainer scroll)
            {
                if ((direction == "ui_down" || direction == "ui_up") && scroll.VerticalScrollMode != ScrollContainer.ScrollMode.Disabled)
                {
                    Godot.Range bar = scroll.GetVScrollBar();
                    double before = bar.Value;
                    bar.Value = direction == "ui_down" ? before + bar.Page : before - bar.Page;
                    moved = moved || !Mathf.IsEqualApprox((float)bar.Value, (float)before);
                }

                if ((direction == "ui_left" || direction == "ui_right") && scroll.HorizontalScrollMode != ScrollContainer.ScrollMode.Disabled)
                {
                    Godot.Range bar = scroll.GetHScrollBar();
                    double before = bar.Value;
                    bar.Value = direction == "ui_right" ? before + bar.Page : before - bar.Page;
                    moved = moved || !Mathf.IsEqualApprox((float)bar.Value, (float)before);
                }
            }
            else if (child is Control control)
            {
                moved = ScrollToward(control, direction) || moved;
            }
        }

        return moved;
    }

    private static bool IsWalkable(Control? control) =>
        control is not null
        && GodotObject.IsInstanceValid(control)
        && control.FocusMode == Control.FocusModeEnum.All
        && control.IsVisibleInTree()
        && !IsDisabled(control);

    private static void Analyze(WalkState state)
    {
        // Panels rebuild themselves (toast buttons expire, lists refresh): drop what is already freed, and say so.
        // Controls hidden at the end (a closed tooltip) leave the analysis too: they were judged while visible,
        // if they ever were. Edges store node indices, so removal remaps them.
        var remap = new System.Collections.Generic.Dictionary<int, int>();
        var kept = new List<Control>(state.Nodes.Count);
        for (int i = 0; i < state.Nodes.Count; i++)
        {
            if (!GodotObject.IsInstanceValid(state.Nodes[i]))
            {
                state.Notes.Add("a control was freed mid-walk and left the analysis.");
            }
            else if (!state.Nodes[i].IsVisibleInTree())
            {
                state.Notes.Add($"{Describe(state.Nodes[i])} was hidden at the end and left the analysis.");
            }
            else
            {
                remap[i] = kept.Count;
                kept.Add(state.Nodes[i]);
            }
        }

        state.Nodes.Clear();
        state.Nodes.AddRange(kept);
        var edges = new List<(int From, string Direction, int To)>();
        foreach ((int from, string direction, int to) in state.Edges)
        {
            if (remap.TryGetValue(from, out int newFrom) && remap.TryGetValue(to, out int newTo))
            {
                edges.Add((newFrom, direction, newTo));
            }
        }

        state.Edges.Clear();
        state.Edges.AddRange(edges);

        int count = state.Nodes.Count;
        if (count == 0)
        {
            return;
        }

        bool[] reached = new bool[count];
        var queue = new Queue<int>();
        reached[0] = true;
        queue.Enqueue(0);
        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            foreach ((int from, string _, int to) in state.Edges)
            {
                if (from == node && !reached[to])
                {
                    reached[to] = true;
                    queue.Enqueue(to);
                }
            }
        }

        int[] outgoing = new int[count];
        foreach ((int from, string _, int to) in state.Edges)
        {
            outgoing[from]++;
        }

        // Reverse reachability: can every control get back to the first focus?
        bool[] returns = new bool[count];
        var back = new Queue<int>();
        returns[0] = true;
        back.Enqueue(0);
        while (back.Count > 0)
        {
            int node = back.Dequeue();
            foreach ((int from, string _, int to) in state.Edges)
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
        for (int i = 0; i < count; i++)
        {
            if (!reached[i])
            {
                unreachable++;
                state.Problems.Add($"{Label(state.Nodes[i])} is unreachable from the first focus.");
            }
            else if (state.Adopted.Contains(i))
            {
                // Adopted tooltip links were reached while alive (that is why they were adopted); their
                // outgoing moves were never probed by design, so trap checks do not apply to them.
            }
            else if (count > 1 && outgoing[i] == 0)
            {
                traps++;
                state.Problems.Add($"{Label(state.Nodes[i])} is a trap: no direction moves focus away.");
            }
            else if (!returns[i])
            {
                traps++;
                state.Problems.Add($"{Label(state.Nodes[i])} is a trap: focus cannot return to the first focus.");
            }
        }

        GD.Print($"[Evenmere] focus walk: {state.Panel.Name}: controls={count} reachable={count - unreachable} traps={traps}"
            + (state.Notes.Count > 0 ? $" notes={state.Notes.Count}" : string.Empty));
        foreach (string note in state.Notes)
        {
            GD.Print($"[Evenmere] focus walk: {state.Panel.Name} note: {note}");
        }

        if (state.Problems.Count > 0)
        {
            foreach (string problem in state.Problems)
            {
                GD.Print($"[Evenmere] focus walk FAIL: {state.Panel.Name}: {problem}");
            }

            for (int i = 0; i < state.Nodes.Count; i++)
            {
                Control node = state.Nodes[i];
                bool valid = GodotObject.IsInstanceValid(node);
                string rect = valid ? node.GetGlobalRect().ToString() : "-";
                GD.Print($"[Evenmere] focus walk: {state.Panel.Name} node {i}: {Describe(node)} valid={valid} rect={rect}.");
            }

            // The edge dump diagnoses unreachable controls: from-node, direction, to-node and both rects.
            foreach ((int from, string direction, int to) in state.Edges)
            {
                Control a = state.Nodes[from];
                Control b = state.Nodes[to];
                if (!GodotObject.IsInstanceValid(a) || !GodotObject.IsInstanceValid(b))
                {
                    continue;
                }

                GD.Print($"[Evenmere] focus walk: {state.Panel.Name} edge: {Label(a)} {a.GetGlobalRect()} {direction} -> {Label(b)} {b.GetGlobalRect()}");
            }

            state.Panel.Failed = true;
        }
    }

    private void Finish()
    {
        _done = true;
        int failed = _panels.Count(static panel => panel.Failed);
        if (failed == 0)
        {
            GD.Print($"[Evenmere] focus walk: FOCUSWALK OK panels={_panels.Count}.");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[Evenmere] focus walk: FOCUSWALK FAIL panels={failed}/{_panels.Count}.");
            GetTree().Quit(1);
        }
    }

    private void Fail(string message)
    {
        _done = true;
        GD.Print($"[Evenmere] focus walk FAIL: {message}");
        GetTree().Quit(1);
    }

    private void HideOthers(Control current)
    {
        if (_root is null)
        {
            return;
        }

        foreach (Node child in _root.GetChildren())
        {
            if (child is Control control && control != current && control.Visible)
            {
                control.Visible = false;
            }
        }

        current.Visible = true;
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

    private static bool IsUsable(WalkState state, int node)
    {
        if ((uint)node >= (uint)state.Nodes.Count)
        {
            return false;
        }

        Control control = state.Nodes[node];
        return GodotObject.IsInstanceValid(control) && control.IsVisibleInTree() && !IsDisabled(control);
    }

    /// <summary>
    /// <summary>Rebuilds the probe list for every collected node and direction.</summary>
    /// <param name="state">The panel walk.</param>
    private static void BuildProbes(WalkState state)
    {
        state.Probes.Clear();
        for (int i = 0; i < state.Nodes.Count; i++)
        {
            foreach (string direction in _directions)
            {
                state.Probes.Add((i, direction));
            }
        }
    }

    private static string Describe(Control? control) =>
        GodotObject.IsInstanceValid(control) ? $"{control.Name}({control.GetType().Name}) at {control.GetPath()}" : "<freed>";

    private static string Label(Control control)
    {
        if (!GodotObject.IsInstanceValid(control))
        {
            return "<freed>";
        }

        string name = control.Name;
        if (control is Button button && !string.IsNullOrEmpty(button.Text))
        {
            return $"{name}('{button.Text}')";
        }

        if (control is LineEdit line && !string.IsNullOrEmpty(line.PlaceholderText))
        {
            return $"{name}('{line.PlaceholderText}')";
        }

        return $"{name}({control.GetType().Name})";
    }

    private sealed class WalkPanel(string name, Control node, Action open, bool expect)
    {
        public string Name { get; } = name;

        public Control Node { get; } = node;

        public Action Open { get; } = open;

        public bool Expect { get; } = expect;

        public bool Failed { get; set; }
    }

    private sealed class WalkState(WalkPanel panel)
    {
        public WalkPanel Panel { get; } = panel;

        public List<Control> Nodes { get; } = new();

        public List<(int Node, string Direction)> Probes { get; } = new();

        public List<(int From, string Direction, int To)> Edges { get; } = new();

        public List<string> Problems { get; } = new();

        public List<string> Notes { get; } = new();

        public HashSet<int> Adopted { get; } = new();

        public bool Collected { get; set; }

        public bool ScrollRetried { get; set; }

        public int Pass { get; set; }

        public List<(int Node, string Direction)> Skipped { get; } = new();

        public int Probe { get; set; }

        public bool Reading { get; set; }
    }
}
