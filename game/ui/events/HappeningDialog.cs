using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Time;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Culture;
using Evenmere.Game.Ui.Progress;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Happenings;
using Godot;

namespace Evenmere.Game.Ui.Events;

/// <summary>
/// The happening dialog (M2-27): a calm card for a pending happening, with an illustration slot the art track
/// fills later, the options with effect previews, and a "Decide later" button that keeps it pending. It opens
/// itself when a happening becomes pending and reopens from the notification feed entry while it waits.
/// </summary>
public sealed partial class HappeningDialog : PanelContainer
{
    /// <summary>How many days a pending happening waits before the default option applies (HappeningsSystem.PendingTimeoutDays, internal to the sim).</summary>
    private const int PendingTimeoutDays = 10;

    private SimHost? _host;
    private ContentDb? _builtFor;
    private HappeningDialogModel? _model;
    private UiStrings? _strings;
    private HappeningId? _shownPending;
    private Label? _title;
    private Label? _body;
    private Label? _days;
    private Label? _empty;
    private PanelContainer? _illustration;
    private Button? _laterButton;
    private Button? _closeButton;
    private VBoxContainer? _options;
    private readonly Dictionary<int, OptionControls> _optionControls = new();

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <inheritdoc />
    public override void _Ready()
    {
        Place();
        PanelTheme.Apply(this);
        _host = GetNodeOrNull<SimHost>(HostPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] happenings: no SimHost at '{HostPath}'; the dialog stays empty.");
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

        if (_model is not null)
        {
            _model.Refreshed -= OnRefreshed;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_host is not { IsRunning: true } host)
        {
            return;
        }

        if (_model is null || !ReferenceEquals(_builtFor, host.Content))
        {
            RebuildModel(host);
        }

        _model?.Advance(delta);
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
            // Closing keeps the happening pending: the feed entry reopens this dialog while it waits.
            Visible = false;
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Shows the dialog for the pending happening, or hides it when none waits.</summary>
    public void ShowPending()
    {
        if (_host is not { IsRunning: true })
        {
            return;
        }

        Visible = _model is not null;
        _model?.MarkDirty();
        _model?.Advance(10.0);
    }

    private void OnEvents(IReadOnlyList<SimEvent> events)
    {
        if (_host is not { IsRunning: true } host)
        {
            return;
        }

        if (_model is null)
        {
            RebuildModel(host);
        }

        if (_model is null)
        {
            return;
        }

        foreach (SimEvent @event in events)
        {
            if (@event is HappeningPending)
            {
                Visible = true;
            }
            else if (@event is HappeningResolved)
            {
                Visible = false;
            }
        }

        _model.MarkDirty();
    }

    private void Place()
    {
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -280f;
        OffsetRight = 280f;
        // Labels add a preview line under every option, so the card is taller than the pre-label 520 px:
        // two options with marks need the extra room, and longer lists scroll inside as before.
        OffsetTop = -300f;
        OffsetBottom = 300f;
        MouseFilter = MouseFilterEnum.Stop;
    }

    private void RebuildModel(SimHost host)
    {
        if (_model is not null)
        {
            _model.Refreshed -= OnRefreshed;
        }

        _strings = new UiStrings(host.Locale);
        _builtFor = host.Content;
        _model = new HappeningDialogModel(new SimHappeningData(host), _strings);
        _model.Refreshed += OnRefreshed;
        _model.MarkDirty();
        Retitle();
    }

    private void Retitle()
    {
        if (_strings is null || _laterButton is null || _closeButton is null)
        {
            return;
        }

        _laterButton.Text = _strings.Get("ui.events.happening.decide_later");
        _laterButton.TooltipText = _strings.Format(
            "ui.events.happening.decide_later.tip",
            ("count", PendingTimeoutDays.ToString()));
        _closeButton.Text = _strings.Get("ui.events.happening.close");
        _closeButton.TooltipText = _strings.Get("ui.events.happening.close.tip");
    }

    private void OnRefreshed() => UpdateAll();

    private void UpdateAll()
    {
        if (_model is null || _strings is null || _options is null)
        {
            return;
        }

        HappeningDialogModel model = _model;
        if (_title is not null)
        {
            _title.Text = model.Title;
        }

        if (_body is not null)
        {
            _body.Text = model.Body;
            _body.Visible = model.HasPending;
        }

        if (_days is not null)
        {
            _days.Visible = model.HasPending;
            _days.Text = model.DaysLeftText;
        }

        if (_illustration is not null)
        {
            _illustration.Visible = model.HasPending;
        }

        if (_laterButton is not null)
        {
            _laterButton.Visible = model.HasPending;
        }

        if (_empty is not null)
        {
            _empty.Visible = !model.HasPending;
            _empty.Text = model.Body;
        }

        SyncOptions(model);
    }

    private void SyncOptions(HappeningDialogModel model)
    {
        HappeningId? pending = _host is { IsRunning: true } host
            ? host.Query.Happenings.PendingView()?.Happening
            : null;
        if (pending != _shownPending)
        {
            _shownPending = pending;
            foreach (OptionControls controls in _optionControls.Values)
            {
                controls.Root.QueueFree();
            }

            _optionControls.Clear();
        }

        var seen = new HashSet<int>();
        foreach (HappeningOptionRow row in model.Options)
        {
            seen.Add(row.Index);
            if (!_optionControls.TryGetValue(row.Index, out OptionControls? controls))
            {
                controls = AddOption(row.Index);
                _optionControls[row.Index] = controls;
            }

            // A labelled option shows its label on the button and keeps the effect preview underneath,
            // so the choice reads as a named decision with its consequences attached.
            string? label = OptionLabel(pending, row.Index);
            controls.Choose.Text = label ?? row.PreviewText;
            controls.Choose.TooltipText = label is null ? string.Empty : row.PreviewText;
            if (label is null)
            {
                controls.Mark.Visible = row.DefaultMark is not null;
                controls.Mark.Text = row.DefaultMark ?? string.Empty;
            }
            else if (row.DefaultMark is null)
            {
                controls.Mark.Visible = true;
                controls.Mark.Text = row.PreviewText;
            }
            else
            {
                controls.Mark.Visible = true;
                controls.Mark.Text = row.PreviewText + "\n" + row.DefaultMark;
            }
        }

        var stale = new List<int>();
        foreach (int index in _optionControls.Keys)
        {
            if (!seen.Contains(index))
            {
                stale.Add(index);
            }
        }

        foreach (int index in stale)
        {
            _optionControls[index].Root.QueueFree();
            _optionControls.Remove(index);
        }
    }

    private string? OptionLabel(HappeningId? happening, int index)
    {
        if (_host is not { IsRunning: true } host || happening is not { } id)
        {
            return null;
        }

        ContentDb content = host.Content;
        if ((uint)id.Value >= (uint)content.Happenings.Count)
        {
            return null;
        }

        HappeningDef def = content.Get(id);
        if ((uint)index >= (uint)def.Options.Length)
        {
            return null;
        }

        // The loader leaves Label null when the option has none, and the linter (EVL007) guarantees a named
        // key exists; a key that still misses its text falls back to the preview, never to a raw key.
        if (def.Options[index].Label is not { } key)
        {
            return null;
        }

        string text = host.Locale.Get(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? null : text;
    }

    private OptionControls AddOption(int index)
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 2);
        _options!.AddChild(root);

        var choose = new Button
        {
            FocusMode = Control.FocusModeEnum.All,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        choose.Pressed += () => SendChoose(index);
        root.AddChild(choose);

        var mark = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(mark);

        return new OptionControls(root, choose, mark);
    }

    private void SendChoose(int index)
    {
        if (_host is null || _model is null)
        {
            return;
        }

        if (_model.ChooseCommand(index) is { } command)
        {
            _host.Enqueue(command);
            _model.MarkDirty();
        }
    }

    private void BuildChrome()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var top = new VBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        margin.AddChild(top);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        top.AddChild(header);

        var title = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 20);
        header.AddChild(title);
        _title = title;

        var close = new Button { FocusMode = Control.FocusModeEnum.All };
        close.Pressed += () => Visible = false;
        header.AddChild(close);
        _closeButton = close;

        // The illustration slot the art track fills later: a quiet frame that keeps the card's shape.
        var illustration = new PanelContainer { CustomMinimumSize = new Vector2(0f, 140f) };
        top.AddChild(illustration);
        _illustration = illustration;

        var body = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(body);
        _body = body;

        var days = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(days);
        _days = days;

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        top.AddChild(scroll);

        var options = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        options.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(options);
        _options = options;

        var later = new Button { FocusMode = Control.FocusModeEnum.All };
        later.Pressed += () => Visible = false;
        top.AddChild(later);
        _laterButton = later;

        var empty = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(empty);
        _empty = empty;
    }

    private sealed class OptionControls(VBoxContainer Root, Button Choose, Label Mark)
    {
        public VBoxContainer Root { get; } = Root;

        public Button Choose { get; } = Choose;

        public Label Mark { get; } = Mark;
    }

    /// <summary>Reads the pending happening from the running simulation: content for the def, queries for the wait.</summary>
    private sealed class SimHappeningData(SimHost host) : IHappeningData
    {
        public HappeningCardFacts? Card
        {
            get
            {
                if (host.Query.Happenings.PendingView() is not { } pending)
                {
                    return null;
                }

                ContentDb content = host.Content;
                if ((uint)pending.Happening.Value >= (uint)content.Happenings.Count)
                {
                    return null;
                }

                HappeningDef def = content.Get(pending.Happening);
                var strings = new UiStrings(host.Locale);
                var names = new ContentConditionNames(content, strings);
                var options = new List<HappeningOptionFacts>(def.Options.Length);
                for (int i = 0; i < def.Options.Length; i++)
                {
                    options.Add(new HappeningOptionFacts(
                        i,
                        EffectsOf(content, strings, names, def.Options[i].Effects),
                        i == def.DefaultOption));
                }

                long elapsed = host.CurrentTick - pending.RaisedTick;
                long left = (PendingTimeoutDays * GameTime.TicksPerDay) - elapsed;
                int daysLeft = left <= 0 ? 0 : (int)((left + GameTime.TicksPerDay - 1) / GameTime.TicksPerDay);
                return new HappeningCardFacts(
                    pending.Happening,
                    HappeningName(pending.Happening),
                    strings.Get($"happening.{content.NameOf(pending.Happening)}.desc"),
                    options,
                    daysLeft);
            }
        }

        private List<HappeningEffectFact> EffectsOf(
            ContentDb content, UiStrings strings, IConditionNames names, ImmutableArray<Effect> effects)
        {
            var facts = new List<HappeningEffectFact>(effects.Length);
            foreach (Effect effect in effects)
            {
                switch (effect)
                {
                    case GrantGoodsEffect granted:
                        foreach (GoodAmount goods in granted.Goods)
                        {
                            facts.Add(new GrantGoodsFact(
                                strings.GoodName(content.Goods.NameOf(goods.Good)),
                                goods.Amount));
                        }

                        break;
                    case GrantPointsEffect granted:
                        foreach (PointAmount points in granted.Points)
                        {
                            facts.Add(new GrantPointsFact(
                                ContentNames.NameOf(points.Kind),
                                points.Amount));
                        }

                        break;
                    case ModifierEffect modifier:
                        facts.Add(new ModifierFact(
                            CivicGraphBuilder.StatName(modifier.Stat),
                            modifier.Percent,
                            TargetText(content, names, strings, modifier.Target)));
                        break;
                    case RequestFestivalEffect request:
                        facts.Add(new FestivalRequestFact(
                            FestivalName(request.Festival),
                            request.DiscountPercent));
                        break;
                }
            }

            return facts;
        }

        private static string TargetText(
            ContentDb content, IConditionNames names, UiStrings strings, ModifierTarget target) => target switch
        {
            BuildingsTarget buildings => buildings.Selector switch
            {
                BuildingTagSelector tagged => names.Tag(tagged.Tag),
                BuildingTypeSelector single => (uint)single.Building.Value < (uint)content.Buildings.Count
                    ? names.Building(single.Building)
                    : strings.Get("ui.traditions.target.island"),
                BuildingCategorySelector grouped => grouped.Category.ToString(),
                _ => strings.Get("ui.traditions.target.island"),
            },
            TierTarget tier => names.Tier(tier.Tier),
            GoodTarget good => names.Good(good.Good),
            _ => strings.Get("ui.traditions.target.island"),
        };

        private string HappeningName(HappeningId id)
        {
            ContentDb content = host.Content;
            string key = $"happening.{content.NameOf(id)}.name";
            string text = host.Locale.Get(key);
            return string.Equals(text, key, StringComparison.Ordinal) ? content.NameOf(id) : text;
        }

        private string FestivalName(FestivalId id)
        {
            ContentDb content = host.Content;
            string key = $"festival.{content.NameOf(id)}.name";
            string text = host.Locale.Get(key);
            return string.Equals(text, key, StringComparison.Ordinal) ? content.NameOf(id) : text;
        }
    }
}
