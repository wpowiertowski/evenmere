using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Ui.Stats;

/// <summary>
/// The statistics graphs panel (M2-22, long view M2-33): calm line charts of population per tier, the picked good's stock and
/// net per day, and knowledge and culture per day. The 40-day window plots daily samples newest last; the two-year
/// window plots the sim's seasonal series, one point per season with season labels, and says so calmly when fewer
/// than two seasons exist. It is a docked panel on the right like the inspector and names exact values on hover. It owns its <see cref="StatsViewModel"/>; the charts are <see cref="LineChart"/>
/// controls. A longer view needs a seasonal series in the sim (M2 follow-up); the 40-day ring is all the panel shows.
/// </summary>
/// <remarks>
/// Input: no new input actions. The close button and every control take keyboard and gamepad focus through the
/// built-in <c>ui_*</c> actions. Debug only: <c>--demo-stats</c> opens the panel through the toggles bar for
/// screenshots, the same way <c>--demo-buildings</c> stages the world (<c>--demo-stats=long</c> opens it on the
/// two-year window); <c>--stats-debug</c> shows the raw series summary that proves the charts match the queries.
/// </remarks>
public sealed partial class StatsPanel : PanelContainer
{
    private const float PanelWidth = 416f;
    private const float PanelTop = 150f;
    private const float PanelMargin = 16f;

    // The pause menu's Menu button sits bottom-right (124 by 48 at a 16 margin); the panel ends above it with the
    // same margin, so the charts and the close button stay fully visible and clickable at every window size.
    private const float MenuReserve = 64f + PanelMargin;
    private const float ChartHeight = 140f;
    private const string DemoStatsFlag = "--demo-stats";
    private const string StatsDebugFlag = "--stats-debug";

    private SimHost? _host;
    private ContentDb? _builtFor;
    private UiStrings? _strings;
    private StatsViewModel? _model;
    private SimHostStatsData? _data;
    private double _secondsSinceRefresh;
    private bool _everRefreshed;
    private bool _demoOpened;

    private Label? _title;
    private Button? _closeButton;
    private Label? _goodsLabel;
    private OptionButton? _goodsOption;
    private Label? _windowLabel;
    private OptionButton? _windowOption;
    private VBoxContainer? _chartsBox;
    private Label? _debugLabel;
    private bool _showDebug;

    private readonly List<ChartBlock> _blocks = [];

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <summary>Gets or sets the path to the toggles bar that opens this panel, relative to this node.</summary>
    [Export]
    public NodePath TogglesPath { get; set; } = new("../PanelToggles");

    /// <summary>Gets or sets the path to the research panel, hidden while the stats panel is open.</summary>
    [Export]
    public NodePath ResearchPath { get; set; } = new("../ResearchPanel");

    /// <summary>Gets or sets the path to the civics panel, hidden while the stats panel is open.</summary>
    [Export]
    public NodePath CulturePath { get; set; } = new("../CulturePanel");

    /// <summary>Gets or sets the path to the build menu, which closes this panel while it is open.</summary>
    [Export]
    public NodePath BuildMenuPath { get; set; } = new("../BuildMenu");

    /// <summary>Gets or sets the path to the inspector panel, hidden while the stats panel is open.</summary>
    [Export]
    public NodePath InspectorPath { get; set; } = new("../InspectorPanel");

    /// <summary>Opens the panel if it is closed and closes it if it is open.</summary>
    public void Toggle()
    {
        if (Visible)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>Opens the panel: hides the other docked panels and moves focus onto the close button.</summary>
    public void Open()
    {
        HideOthers();
        Visible = true;
        _secondsSinceRefresh = double.MaxValue;
        _closeButton?.GrabFocus();
    }

    /// <summary>Closes the panel.</summary>
    public void Close() => Visible = false;

    /// <inheritdoc />
    public override void _Ready()
    {
        Place();
        PanelTheme.Apply(this);
        _showDebug = HasStatsDebugFlag();
        _host = GetNodeOrNull<SimHost>(HostPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] stats: no SimHost at '{HostPath}'; the graphs stay empty.");
            return;
        }

        BuildChrome();
        _host.EventsDrained += OnEvents;
        Visible = false;
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_host is not null)
        {
            _host.EventsDrained -= OnEvents;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_host is not { IsRunning: true } host)
        {
            return;
        }

        if (!ReferenceEquals(_builtFor, host.Content))
        {
            RebuildFor(host);
        }

        if (!_demoOpened && OS.IsDebugBuild())
        {
            _demoOpened = true;
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == DemoStatsFlag || arg.StartsWith(DemoStatsFlag + "=", StringComparison.Ordinal))
                {
                    // Screenshots travel the real toggle path when the toggles bar exists.
                    if (GetNodeOrNull<PanelToggles>(TogglesPath) is { } toggles)
                    {
                        toggles.ToggleStats();
                    }
                    else
                    {
                        Open();
                    }

                    if (arg == DemoStatsFlag + "=long" && _model is not null)
                    {
                        _model.SetWindow(StatsWindow.TwoYears);
                        SyncWindowOption();
                    }

                    break;
                }
            }
        }

        // Another docked panel opened elsewhere hides this one, so at most one is open.
        if (Visible && OtherPanelOpen())
        {
            Visible = false;
            return;
        }

        if (!Visible || _model is null)
        {
            return;
        }

        _secondsSinceRefresh += delta;
        if (_everRefreshed && _secondsSinceRefresh < ViewModel.MinRefreshIntervalSeconds)
        {
            return;
        }

        _secondsSinceRefresh = 0;
        if (_model.Advance(_secondsSinceRefresh))
        {
            _everRefreshed = true;
            RefreshCharts();
        }
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
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnEvents(IReadOnlyList<SimEvent> events) => _model?.MarkDirty();

    private void Place()
    {
        AnchorLeft = 1f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 1f;
        OffsetLeft = -PanelWidth;
        OffsetRight = -PanelMargin;
        OffsetTop = PanelTop;
        OffsetBottom = -MenuReserve;
        MouseFilter = MouseFilterEnum.Stop;
    }

    private void RebuildFor(SimHost host)
    {
        _builtFor = host.Content;
        _strings = new UiStrings(host.Locale);
        _data = new SimHostStatsData(host, _strings);
        _model = new StatsViewModel(_data, _strings);
        RebuildGoodsOption();
        Retitle();
        _everRefreshed = false;
        _secondsSinceRefresh = double.MaxValue;
    }

    private void BuildChrome()
    {
        var margin = new MarginContainer();
        margin.Name = "Margin";
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var top = new VBoxContainer();
        top.Name = "VBox";
        top.AddThemeConstantOverride("separation", 8);
        margin.AddChild(top);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        top.AddChild(header);

        _title = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddChild(_title);

        _closeButton = new Button { FocusMode = FocusModeEnum.All };
        _closeButton.Pressed += Close;
        header.AddChild(_closeButton);

        var goodsRow = new HBoxContainer();
        goodsRow.AddThemeConstantOverride("separation", 8);
        top.AddChild(goodsRow);

        var goodsLabel = new Label();
        _goodsLabel = goodsLabel;
        goodsRow.AddChild(goodsLabel);

        _goodsOption = new OptionButton { FocusMode = FocusModeEnum.All, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _goodsOption.ItemSelected += OnGoodSelected;
        goodsRow.AddChild(_goodsOption);

        var windowRow = new HBoxContainer();
        windowRow.AddThemeConstantOverride("separation", 8);
        top.AddChild(windowRow);

        var windowLabel = new Label();
        _windowLabel = windowLabel;
        windowRow.AddChild(windowLabel);

        _windowOption = new OptionButton { FocusMode = FocusModeEnum.All, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _windowOption.ItemSelected += OnWindowSelected;
        windowRow.AddChild(_windowOption);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        scroll.Name = "Scroll";
        scroll.FollowFocus = true;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        top.AddChild(scroll);

        _chartsBox = new VBoxContainer();
        _chartsBox.Name = "Charts";
        _chartsBox.AddThemeConstantOverride("separation", 10);
        _chartsBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_chartsBox);

        _debugLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = _showDebug,
        };
        top.AddChild(_debugLabel);
    }

    // The series summary is a debug aid, never player text: it shows only in debug builds with --stats-debug.
    private static bool HasStatsDebugFlag()
    {
        if (!OS.IsDebugBuild())
        {
            return false;
        }

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == StatsDebugFlag)
            {
                return true;
            }
        }

        return false;
    }

    private void Retitle()
    {
        if (_strings is null || _title is null || _closeButton is null
            || _goodsLabel is null || _goodsOption is null || _debugLabel is null
            || _windowLabel is null || _windowOption is null)
        {
            return;
        }

        UiStrings strings = _strings;
        _title.Text = strings.Get("ui.stats.title");
        _closeButton.Text = strings.Get("ui.stats.close");
        _closeButton.TooltipText = strings.Get("ui.stats.close.tip");
        _goodsLabel.Text = strings.Get("ui.stats.good");
        _windowLabel.Text = strings.Get("ui.stats.window");
        _windowOption.Clear();
        _windowOption.AddItem(strings.Get("ui.stats.window.days"), (int)StatsWindow.Last40Days);
        _windowOption.AddItem(strings.Get("ui.stats.window.years"), (int)StatsWindow.TwoYears);
        SyncWindowOption();
        _debugLabel.Text = _model?.DebugSummary ?? string.Empty;
    }

    private void SyncWindowOption()
    {
        if (_windowOption is null || _model is null)
        {
            return;
        }

        _windowOption.Select((int)_model.Window);
    }

    private void RebuildGoodsOption()
    {
        if (_goodsOption is null || _data is null || _model is null)
        {
            return;
        }

        _goodsOption.Clear();
        int selected = 0;
        for (int i = 0; i < _data.Goods.Length; i++)
        {
            _goodsOption.AddItem(_data.Goods[i].Name, i);
            if (_data.Goods[i].Id.Equals(_model.SelectedGood))
            {
                selected = i;
            }
        }

        if (_data.Goods.Length > 0)
        {
            _goodsOption.Select(selected);
        }
    }

    private void OnGoodSelected(long index)
    {
        if (_data is null || _model is null || index < 0 || index >= _data.Goods.Length)
        {
            return;
        }

        _model.SelectGood(_data.Goods[(int)index].Id);
    }

    private void OnWindowSelected(long index)
    {
        if (_model is null || index < 0 || index > (long)StatsWindow.TwoYears)
        {
            return;
        }

        _model.SetWindow((StatsWindow)index);
    }

    private void RefreshCharts()
    {
        if (_model is null || _strings is null || _chartsBox is null || _debugLabel is null)
        {
            return;
        }

        UiStrings strings = _strings;
        EnsureBlocks(strings);

        bool longView = _model.Window == StatsWindow.TwoYears;
        bool ready = !longView || _model.LongViewReady;
        string emptyText = longView && !ready ? strings.Get("ui.stats.long.empty") : strings.Get("ui.stats.empty");
        (string first, string last) = AxisEnds(_model, longView && ready);

        SetBlock(
            0,
            strings.Get("ui.stats.population.title"),
            ready ? _model.PopulationLines : [],
            floorZero: true,
            first,
            last,
            strings,
            emptyText);
        SetBlock(
            1,
            strings.Format("ui.stats.stock.title", ("good", _model.SelectedGoodName)),
            ready ? [_model.StockLine] : [],
            floorZero: true,
            first,
            last,
            strings,
            emptyText);
        SetBlock(
            2,
            strings.Format("ui.stats.net.title", ("good", _model.SelectedGoodName)),
            ready ? [_model.NetLine] : [],
            floorZero: false,
            first,
            last,
            strings,
            emptyText);
        SetBlock(
            3,
            strings.Get("ui.stats.knowledge.title"),
            ready ? [_model.KnowledgeLine] : [],
            floorZero: true,
            first,
            last,
            strings,
            emptyText);
        SetBlock(
            4,
            strings.Get("ui.stats.culture.title"),
            ready ? [_model.CultureLine] : [],
            floorZero: true,
            first,
            last,
            strings,
            emptyText);

        if (_debugLabel.Visible)
        {
            _debugLabel.Text = _model.DebugSummary;
        }
    }

    private static (string First, string Last) AxisEnds(StatsViewModel model, bool seasons)
    {
        if (seasons && model.SeasonLabels.Length > 0)
        {
            return (model.SeasonLabels[0], model.SeasonLabels[^1]);
        }

        long lastDay = model.StartDay + ((long)Math.Max(0, PlottedWidth(model) - 1) * model.DayStep);
        return ($"Day {model.StartDay}", $"Day {lastDay}");
    }

    private static int PlottedWidth(StatsViewModel model)
    {
        int n = 0;
        foreach (StatsLine line in model.PopulationLines)
        {
            n = Math.Max(n, line.Values.Length);
        }

        n = Math.Max(n, model.StockLine.Values.Length);
        n = Math.Max(n, model.NetLine.Values.Length);
        n = Math.Max(n, model.KnowledgeLine.Values.Length);
        return Math.Max(n, model.CultureLine.Values.Length);
    }

    private void EnsureBlocks(UiStrings strings)
    {
        if (_chartsBox is null)
        {
            return;
        }

        while (_blocks.Count < 5)
        {
            int kind = _blocks.Count;
            string kindName = ChartKindName(kind);
            var title = new Label();
            var chart = new LineChart { CustomMinimumSize = new Vector2(360f, ChartHeight) };
            var info = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
            var block = new ChartBlock(title, chart, info);
            chart.HoverChanged += () => OnHover(block);
            var box = new VBoxContainer();
            box.Name = kindName + "Block";
            box.AddThemeConstantOverride("separation", 2);
            box.AddChild(title);
            box.AddChild(info);
            chart.Name = kindName + "Chart";
            box.AddChild(chart);
            _chartsBox.AddChild(box);
            _blocks.Add(block);
        }

        string latestWord = strings.Get("ui.stats.latest");
        foreach (ChartBlock block in _blocks)
        {
            block.LatestWord = latestWord;
        }
    }

    private void SetBlock(
        int index,
        string title,
        ImmutableArray<StatsLine> lines,
        bool floorZero,
        string firstLabel,
        string lastLabel,
        UiStrings strings,
        string emptyText)
    {
        ChartBlock block = _blocks[index];
        block.Title.Text = title;
        block.Lines = lines;

        var chartLines = new List<ChartLine>(lines.Length);
        var latest = new System.Text.StringBuilder();
        foreach (StatsLine line in lines)
        {
            chartLines.Add(new ChartLine(line.Label, line.Values, Color.FromHtml(StatsPalette.Hex(line.ColorIndex)), line.IsMilli, floorZero));
            if (latest.Length > 0)
            {
                latest.Append(" · ");
            }

            latest.Append(line.Label);
            latest.Append(' ');
            latest.Append(StatsViewModel.FormatValue(line.Values.Length == 0 ? 0 : line.Values[^1], line.IsMilli));
        }

        block.LatestText = $"{block.LatestWord}: {(latest.Length == 0 ? emptyText : latest)}";
        block.Chart.SetData(chartLines, firstLabel, lastLabel, emptyText);
        UpdateInfo(block);
    }

    private void OnHover(ChartBlock block)
    {
        UpdateInfo(block);
    }

    private static string ChartKindName(int kind) => kind switch
    {
        0 => "Population",
        1 => "Stock",
        2 => "Net",
        3 => "Knowledge",
        _ => "Culture",
    };

    private void UpdateInfo(ChartBlock block)
    {
        if (_model is null)
        {
            return;
        }

        int hover = block.Chart.HoverIndex;
        if (hover < 0 || block.Lines.Length == 0)
        {
            block.Info.Text = block.LatestText;
            return;
        }

        string where = _model.PointLabel(hover);
        var text = new System.Text.StringBuilder();
        text.Append(where);
        text.Append(": ");
        bool first = true;
        foreach (StatsLine line in block.Lines)
        {
            if (hover >= line.Values.Length)
            {
                continue;
            }

            if (!first)
            {
                text.Append(" · ");
            }

            first = false;
            text.Append(line.Label);
            text.Append(' ');
            text.Append(StatsViewModel.FormatValue(line.Values[hover], line.IsMilli));
        }

        block.Info.Text = first ? block.LatestText : text.ToString();
    }

    private bool OtherPanelOpen()
    {
        return (GetNodeOrNull<Control>(ResearchPath) is { Visible: true })
            || (GetNodeOrNull<Control>(CulturePath) is { Visible: true })
            || (GetNodeOrNull<Control>(BuildMenuPath) is { Visible: true })
            || (GetNodeOrNull<Control>(InspectorPath) is { Visible: true });
    }

    private void HideOthers()
    {
        if (GetNodeOrNull<Control>(ResearchPath) is { } research)
        {
            research.Visible = false;
        }

        if (GetNodeOrNull<Control>(CulturePath) is { } culture)
        {
            culture.Visible = false;
        }

        if (GetNodeOrNull<Control>(InspectorPath) is { } inspector)
        {
            inspector.Visible = false;
        }
    }

    private sealed class ChartBlock(Label title, LineChart chart, Label info)
    {
        public Label Title { get; } = title;
        public LineChart Chart { get; } = chart;
        public Label Info { get; } = info;
        public ImmutableArray<StatsLine> Lines { get; set; } = [];
        public string LatestText { get; set; } = string.Empty;
        public string LatestWord { get; set; } = "Latest";
    }

    private sealed class SimHostStatsData : IStatsData
    {
        private readonly SimHost _host;

        public SimHostStatsData(SimHost host, UiStrings strings)
        {
            _host = host;
            ImmutableArray<(TierId Id, string Name)>.Builder tiers = ImmutableArray.CreateBuilder<(TierId Id, string Name)>();
            foreach (TierId tier in host.Content.TiersByOrder)
            {
                tiers.Add((tier, strings.TierName(host.Content.NameOf(tier))));
            }

            Tiers = tiers.ToImmutable();

            ImmutableArray<(GoodId Id, string Name)>.Builder goods = ImmutableArray.CreateBuilder<(GoodId Id, string Name)>();
            for (int i = 0; i < host.Content.Goods.Count; i++)
            {
                var id = GoodId.From(checked((ushort)i));
                goods.Add((id, strings.GoodName(host.Content.NameOf(id))));
            }

            Goods = goods.ToImmutable();
        }

        public bool IsRunning => _host.IsRunning;

        public long CurrentTick => _host.CurrentTick;

        public ImmutableArray<(TierId Id, string Name)> Tiers { get; }

        public ImmutableArray<(GoodId Id, string Name)> Goods { get; }

        public int ReadSeries(StatKey key, Span<long> destination) =>
            _host.Query.Statistics.Series(key, destination);

        public (int Written, long Seasons) ReadSeasonalSeries(StatKey key, Span<long> destination) =>
            _host.Query.Statistics.SeasonalSeries(key, destination);
    }
}
