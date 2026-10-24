using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;

namespace Evenmere.Game.Ui.Hud;

/// <summary>
/// The numbers and text of the HUD, as plain properties the HUD nodes bind to. It has no Godot types: it reads an
/// <see cref="IHudSource"/>, takes names from <see cref="UiStrings"/>, and refreshes at most four times a second
/// (<see cref="ViewModel"/>). Anything a query cannot answer yet shows the dash from <c>ui.hud.unavailable</c>.
/// </summary>
public sealed class HudViewModel : ViewModel
{
    /// <summary>The most goods the strip shows.</summary>
    public const int MaxStripGoods = 8;

    private readonly IHudSource _source;
    private readonly UiStrings _strings;
    private readonly string _dash;
    private readonly TierId[] _tiers;
    private readonly string[] _tierNames;
    private readonly GoodId[] _goods;
    private readonly string[] _goodNames;
    private readonly TrendTracker[] _trackers;

    /// <summary>Initializes a new instance of the <see cref="HudViewModel"/> class.</summary>
    /// <param name="source">Where the numbers come from.</param>
    /// <param name="content">The content database, for the tiers and goods to show and their string IDs.</param>
    /// <param name="strings">The display text.</param>
    /// <param name="stripGoods">
    /// The goods for the strip, in display order, at most <see cref="MaxStripGoods"/> (extra ones are dropped);
    /// <see langword="null"/> for <see cref="DefaultStripGoods"/>. The player will be able to pick these later.
    /// </param>
    public HudViewModel(IHudSource source, ContentDb content, UiStrings strings, IEnumerable<GoodId>? stripGoods = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(strings);

        _source = source;
        _strings = strings;
        _dash = strings.Get("ui.hud.unavailable");

        // Tier IDs are in string order; the HUD lists the unlocked ones from the lowest tier up.
        _tiers = [.. content.TiersByOrder];
        _tierNames = [.. _tiers.Select(tier => strings.TierName(content.NameOf(tier)))];

        _goods = [.. (stripGoods ?? DefaultStripGoods(content)).Take(MaxStripGoods)];
        _goodNames = [.. _goods.Select(good => strings.GoodName(content.NameOf(good)))];
        _trackers = [.. _goods.Select(static _ => new TrendTracker())];

        ShowUnavailable();
    }

    /// <summary>Gets a value indicating whether a simulation was running at the last refresh.</summary>
    public bool HasSimulation { get; private set; }

    /// <summary>Gets the date, such as <c>Day 3, Spring, Year 1</c>. The day is the day of the season, counted from 1.</summary>
    public string DateText { get; private set; } = string.Empty;

    /// <summary>Gets the speed the game was running at.</summary>
    public SimSpeed Speed { get; private set; } = SimSpeed.Paused;

    /// <summary>Gets the residents of each unlocked tier, lowest tier first.</summary>
    public ImmutableArray<HudStat> Residents { get; private set; } = [];

    /// <summary>Gets the knowledge gained per day.</summary>
    public HudStat Knowledge { get; private set; }

    /// <summary>Gets the culture gained per day.</summary>
    public HudStat Culture { get; private set; }

    /// <summary>Gets the goods in the strip, in the order they were chosen.</summary>
    public ImmutableArray<HudGood> Goods { get; private set; } = [];

    /// <summary>
    /// Picks the default strip: the first <see cref="MaxStripGoods"/> goods in ID order, which is the ordinal order of their
    /// string IDs. A later setting can replace this with the player's own list.
    /// </summary>
    /// <param name="content">The content database.</param>
    /// <returns>The goods to show.</returns>
    public static ImmutableArray<GoodId> DefaultStripGoods(ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(content);
        int count = Math.Min(content.Goods.Count, MaxStripGoods);
        ImmutableArray<GoodId>.Builder builder = ImmutableArray.CreateBuilder<GoodId>(count);
        for (int i = 0; i < count; i++)
        {
            builder.Add(GoodId.From(checked((ushort)i)));
        }

        return builder.MoveToImmutable();
    }

    /// <summary>Gets the locale key suffix of a speed's button, such as <c>x2</c>; the keys are <c>ui.hud.speed.&lt;suffix&gt;</c> and <c>ui.hud.speed.tip.&lt;suffix&gt;</c>.</summary>
    /// <param name="speed">The speed.</param>
    /// <returns>The suffix.</returns>
    public static string SpeedKey(SimSpeed speed) => speed switch
    {
        SimSpeed.Paused => "paused",
        SimSpeed.X1 => "x1",
        SimSpeed.X2 => "x2",
        SimSpeed.X4 => "x4",
        SimSpeed.X16 => "x16",
        _ => throw new ArgumentOutOfRangeException(nameof(speed), speed, "Unknown simulation speed."),
    };

    /// <inheritdoc />
    protected override void OnRefresh()
    {
        if (!_source.IsRunning)
        {
            ShowUnavailable();
            return;
        }

        HasSimulation = true;
        GameClock clock = _source.Clock;
        DateText = DateOf(clock);
        Speed = _source.Speed;

        ImmutableArray<HudStat>.Builder residents = ImmutableArray.CreateBuilder<HudStat>();
        for (int i = 0; i < _tiers.Length; i++)
        {
            if (!_source.IsTierUnlocked(_tiers[i]))
            {
                continue;
            }

            residents.Add(ResidentStat(_tiers[i], _tierNames[i], _source.Residents(_tiers[i])));
        }

        Residents = residents.ToImmutable();
        Knowledge = RateStat("ui.hud.knowledge", "ui.hud.knowledge.tip", _source.KnowledgePerDay());
        Culture = RateStat("ui.hud.culture", "ui.hud.culture.tip", _source.CulturePerDay());

        ImmutableArray<HudGood>.Builder goods = ImmutableArray.CreateBuilder<HudGood>(_goods.Length);
        for (int i = 0; i < _goods.Length; i++)
        {
            Amount stock = _source.Stock(_goods[i]);
            _trackers[i].Record(clock.Tick, stock.WholeUnits);

            Trend trend = _trackers[i].Current;
            string stockText = NumberFormat.Units(stock);
            string tooltip = _strings.Format(
                "ui.hud.goods.tip",
                ("good", _goodNames[i]),
                ("stock", stockText),
                ("trend", _strings.Get(TrendKey(trend))));
            goods.Add(new HudGood(_goodNames[i], stockText, trend, tooltip));
        }

        Goods = goods.MoveToImmutable();
    }

    private HudStat ResidentStat(TierId tier, string tierName, long? count)
    {
        string countText = count is { } known ? NumberFormat.Count(known) : _dash;
        int supply = _source.WorkforceSupply(tier);
        int demand = _source.WorkforceDemand(tier);
        var summary = WorkforceSummary.From(supply, demand, _source.WorkingBelowTier(tier), _source.FilledFromAbove(tier));

        // A tier with no jobs and no residents shows only its count: there is nothing to staff and nothing to explain.
        if (demand <= 0 && (count ?? 0) <= 0)
        {
            return new HudStat(tierName, countText, _strings.Format("ui.hud.residents.tip", ("tier", tierName), ("count", countText)), false);
        }

        // Residents but no jobs: the tier's workers sit idle, so the jobs part is dropped ("12 · 12 free").
        // A real shortage shows "short"; jobs filled from above name the cover instead. Workers in lower-tier
        // jobs append the same "in lower-tier jobs" suffix to whichever form applies.
        string workforce = demand <= 0
            ? _strings.Format("ui.hud.workforce.idle", ("free", NumberFormat.Count(summary.Free)))
            : summary.Shortfall > 0
            ? _strings.Format("ui.hud.workforce.short", ("short", NumberFormat.Count(summary.Shortfall)), ("jobs", NumberFormat.Count(demand)))
            : summary.Covered > 0
            ? _strings.Format("ui.hud.workforce.covered", ("jobs", NumberFormat.Count(demand)), ("covered", NumberFormat.Count(summary.Covered)))
            : _strings.Format("ui.hud.workforce.free", ("free", NumberFormat.Count(summary.Free)), ("jobs", NumberFormat.Count(demand)));
        if (summary.Below > 0)
        {
            workforce += " " + _strings.Format("ui.hud.workforce.below", ("below", NumberFormat.Count(summary.Below)));
        }

        string value = _strings.Format("ui.hud.residents.value", ("count", countText), ("workforce", workforce));
        string tooltip = WorkforceTooltip(tierName, supply, demand, summary);
        return new HudStat(tierName, value, tooltip, summary.Shortfall > 0);
    }

    private string WorkforceTooltip(string tierName, int supply, int demand, WorkforceSummary summary)
    {
        string tooltip = supply == 1
            ? _strings.Format("ui.hud.workforce.tip.one", ("tier", tierName), ("demand", NumberFormat.Count(demand)))
            : _strings.Format(
                "ui.hud.workforce.tip.other",
                ("supply", NumberFormat.Count(supply)),
                ("tier", tierName),
                ("demand", NumberFormat.Count(demand)));
        if (summary.Covered > 0)
        {
            string key = summary.Covered == 1 ? "ui.hud.workforce.tip.covered.one" : "ui.hud.workforce.tip.covered.other";
            tooltip += " " + _strings.Format(key, ("covered", NumberFormat.Count(summary.Covered)));
        }

        if (summary.Below > 0)
        {
            string key = summary.Below == 1 ? "ui.hud.workforce.tip.below.one" : "ui.hud.workforce.tip.below.other";
            tooltip += " " + _strings.Format(
                key,
                ("below", NumberFormat.Count(summary.Below)),
                ("tier", tierName));
        }

        if (demand > 0)
        {
            tooltip += " " + _strings.Format("ui.hud.workforce.tip.hold", ("tier", tierName));
        }

        // What to do about it: a real shortage needs more houses, and so does a covered gap, so the higher-tier
        // workers can go back to their own work.
        if (summary.Shortfall > 0)
        {
            tooltip += " " + _strings.Format("ui.hud.workforce.tip.build", ("tier", tierName));
        }
        else if (summary.Covered > 0)
        {
            tooltip += " " + _strings.Format("ui.hud.workforce.tip.relieve", ("tier", tierName));
        }

        return tooltip;
    }

    private static string TrendKey(Trend trend) => trend switch
    {
        Trend.Up => "ui.hud.trend.up",
        Trend.Down => "ui.hud.trend.down",
        _ => "ui.hud.trend.flat",
    };

    private static string SeasonKey(Season season) => season switch
    {
        Season.Spring => "ui.hud.season.spring",
        Season.Summer => "ui.hud.season.summer",
        Season.Autumn => "ui.hud.season.autumn",
        Season.Winter => "ui.hud.season.winter",
        _ => throw new ArgumentOutOfRangeException(nameof(season), season, "Unknown season."),
    };

    private string DateOf(GameClock clock) => _strings.Format(
        "ui.hud.date",
        ("day", NumberFormat.Count(clock.DayOfSeason + 1L)),
        ("season", _strings.Get(SeasonKey(clock.Season))),
        ("year", NumberFormat.Count(clock.Year + 1)));

    private HudStat RateStat(string labelKey, string tooltipKey, Amount? perDay)
    {
        string value = perDay is { } rate ? NumberFormat.Rate(rate) : _dash;
        string shown = perDay is null ? _dash : _strings.Format("ui.hud.per_day", ("value", value));
        return new HudStat(_strings.Get(labelKey), shown, _strings.Format(tooltipKey, ("value", value)), false);
    }

    /// <summary>Shows the dash everywhere, with the names in place, so the nodes can be built before a simulation exists.</summary>
    private void ShowUnavailable()
    {
        HasSimulation = false;
        DateText = _dash;
        Speed = SimSpeed.Paused;

        Residents = [.. _tierNames.Select(name => new HudStat(name, _dash, _strings.Format("ui.hud.residents.tip", ("tier", name), ("count", _dash)), false))];
        Knowledge = RateStat("ui.hud.knowledge", "ui.hud.knowledge.tip", null);
        Culture = RateStat("ui.hud.culture", "ui.hud.culture.tip", null);
        Goods = [.. _goodNames.Select(name => new HudGood(name, _dash, Trend.Flat, name))];

        foreach (TrendTracker tracker in _trackers)
        {
            tracker.Reset();
        }
    }
}
