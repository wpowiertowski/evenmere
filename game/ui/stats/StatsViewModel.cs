using System.Collections.Immutable;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Game.Ui.Common;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Game.Ui.Stats;

/// <summary>
/// Where a statistics view-model reads its numbers. The game feeds it from <see cref="IStatisticsQueries"/>
/// through the sim host; the tests feed it fixed arrays. Series are daily samples, oldest first, in the units
/// <see cref="StatKind"/> defines (milli-units for goods, knowledge and culture; whole counts for residents).
/// </summary>
public interface IStatsData
{
    /// <summary>Gets a value indicating whether a simulation is running.</summary>
    bool IsRunning { get; }

    /// <summary>Gets the simulation tick the samples are read at, for day labels.</summary>
    long CurrentTick { get; }

    /// <summary>
    /// Copies the most recent daily samples of a series, oldest first.
    /// </summary>
    /// <param name="key">The series.</param>
    /// <param name="destination">Receives the samples from the start.</param>
    /// <returns>How many samples were written.</returns>
    int ReadSeries(StatKey key, Span<long> destination);

    /// <summary>
    /// Copies the most recent seasonal samples of a series, oldest first: one per season, eight seasons deep.
    /// Each sample averages the season's ten days, in the same units as the daily window.
    /// </summary>
    /// <param name="key">The series.</param>
    /// <param name="destination">Receives the samples from the start.</param>
    /// <returns>How many samples were written and the seasons sampled in all (for season labels).</returns>
    (int Written, long Seasons) ReadSeasonalSeries(StatKey key, Span<long> destination);

    /// <summary>Gets the tiers to chart, lowest tier first.</summary>
    ImmutableArray<(TierId Id, string Name)> Tiers { get; }

    /// <summary>Gets the goods the player can pick from, in display order.</summary>
    ImmutableArray<(GoodId Id, string Name)> Goods { get; }
}

/// <summary>
/// One plotted line: a label, window-adjusted values (oldest first), whether the raw values are milli-units,
/// how many raw daily samples stood behind the window (for the debug label), and the raw newest sample the
/// debug label proves matches <see cref="IStatisticsQueries"/>.
/// </summary>
/// <param name="Label">The legend label.</param>
/// <param name="Values">The plotted values, oldest first.</param>
/// <param name="IsMilli">Whether the raw values are milli-units (goods, knowledge, culture) or whole counts.</param>
/// <param name="SampleCount">How many raw daily samples the window was built from.</param>
/// <param name="LastRaw">The newest raw daily sample.</param>
/// <param name="ColorIndex">The <see cref="StatsPalette"/> entry.</param>
public sealed record StatsLine(
    string Label,
    long[] Values,
    bool IsMilli,
    int SampleCount,
    long LastRaw,
    int ColorIndex);

/// <summary>
/// The numbers behind the statistics panel, as plain properties the panel binds to. It has no Godot types: it
/// reads an <see cref="IStatsData"/>, formats through <see cref="UiStrings"/> (the <c>ui.stats.*</c> locale keys),
/// and refreshes at most four times a second (<see cref="ViewModel"/>). The 40-day window plots daily samples;
/// the two-year window plots the sim's seasonal series, one point per season, eight seasons deep.
/// </summary>
public sealed class StatsViewModel : ViewModel
{
    /// <summary>The most daily samples one read holds, mirroring the sim's 40-day ring.</summary>
    public const int MaxSamples = 40;

    /// <summary>The most seasonal samples one read holds, mirroring the sim's 8-season ring.</summary>
    public const int MaxSeasonalSamples = 8;

    /// <summary>How many sampled seasons the long view needs before it draws: a single point is no trend.</summary>
    public const int MinSeasonsForLongView = 2;

    private readonly IStatsData _data;
    private readonly UiStrings _strings;
    private readonly long[] _buffer = new long[MaxSamples];
    private readonly long[] _seasonalBuffer = new long[MaxSeasonalSamples];

    /// <summary>Initializes a new instance of the <see cref="StatsViewModel"/> class.</summary>
    /// <param name="data">Where the series come from.</param>
    /// <param name="strings">The display text.</param>
    public StatsViewModel(IStatsData data, UiStrings strings)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(strings);
        _data = data;
        _strings = strings;
        SelectedGood = data.Goods.Length > 0 ? data.Goods[0].Id : default;
    }

    /// <summary>Gets a value indicating whether a simulation was running at the last refresh.</summary>
    public bool HasData { get; private set; }

    /// <summary>Gets the current day number, for axis labels.</summary>
    public long CurrentDay { get; private set; }

    /// <summary>Gets the day number of the oldest plotted point.</summary>
    public long StartDay { get; private set; }

    /// <summary>Gets how many days one plotted point covers (1 for the 40-day window, 10 for the 2-year window).</summary>
    public int DayStep { get; private set; } = 1;

    /// <summary>Gets the seasons sampled in all at the last refresh, for the long-view labels and readiness.</summary>
    public long SeasonsSampled { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the long view has enough history to draw: two full seasons, so one point
    /// never poses as a trend.
    /// </summary>
    public bool LongViewReady => SeasonsSampled >= MinSeasonsForLongView;

    /// <summary>Gets one axis label per plotted point in the two-year window ("Spring Y2"), oldest first.</summary>
    public ImmutableArray<string> SeasonLabels { get; private set; } = [];

    /// <summary>Gets the resident lines, one per tier.</summary>
    public ImmutableArray<StatsLine> PopulationLines { get; private set; } = [];

    /// <summary>Gets the selected good's stock line.</summary>
    public StatsLine StockLine { get; private set; } = new StatsLine("–", [], true, 0, 0, StatsPalette.Stock);

    /// <summary>Gets the selected good's net-per-day line (produced minus consumed).</summary>
    public StatsLine NetLine { get; private set; } = new StatsLine("–", [], true, 0, 0, StatsPalette.Net);

    /// <summary>Gets the knowledge-per-day line.</summary>
    public StatsLine KnowledgeLine { get; private set; } = new StatsLine("–", [], true, 0, 0, StatsPalette.Knowledge);

    /// <summary>Gets the culture-per-day line.</summary>
    public StatsLine CultureLine { get; private set; } = new StatsLine("–", [], true, 0, 0, StatsPalette.Culture);

    /// <summary>Gets the selected good.</summary>
    public GoodId SelectedGood { get; private set; }

    /// <summary>Gets the selected good's display name.</summary>
    public string SelectedGoodName { get; private set; } = string.Empty;

    /// <summary>Gets the shown span.</summary>
    public StatsWindow Window { get; private set; } = StatsWindow.Last40Days;

    /// <summary>
    /// Gets the debug summary: the raw sample counts and newest raw samples behind every line, so a screenshot
    /// proves the charts match <see cref="IStatisticsQueries"/> point for point.
    /// </summary>
    public string DebugSummary { get; private set; } = string.Empty;

    /// <summary>Picks the good the goods charts show.</summary>
    /// <param name="good">The good; ignored when it is not in the goods list.</param>
    public void SelectGood(GoodId good)
    {
        foreach ((GoodId id, string _) in _data.Goods)
        {
            if (id.Equals(good))
            {
                SelectedGood = good;
                MarkDirty();
                return;
            }
        }
    }

    /// <summary>Picks the shown span.</summary>
    /// <param name="window">The span.</param>
    public void SetWindow(StatsWindow window)
    {
        Window = window;
        MarkDirty();
    }

    /// <summary>Formats a plotted value exactly as the charts show it.</summary>
    /// <param name="raw">The raw sample.</param>
    /// <param name="isMilli">Whether it is milli-units.</param>
    /// <returns>The display text.</returns>
    public static string FormatValue(long raw, bool isMilli) =>
        isMilli ? NumberFormat.Rate(Amount.FromMilli(raw)) : NumberFormat.Count(raw);

    /// <inheritdoc />
    protected override void OnRefresh()
    {
        if (!_data.IsRunning)
        {
            HasData = false;
            PopulationLines = [];
            StockLine = StockLine with { Values = [], SampleCount = 0, LastRaw = 0 };
            NetLine = NetLine with { Values = [], SampleCount = 0, LastRaw = 0 };
            KnowledgeLine = KnowledgeLine with { Values = [], SampleCount = 0, LastRaw = 0 };
            CultureLine = CultureLine with { Values = [], SampleCount = 0, LastRaw = 0 };
            SeasonLabels = [];
            SeasonsSampled = 0;
            DebugSummary = _strings.Get("ui.stats.empty");
            return;
        }

        HasData = true;
        CurrentDay = _data.CurrentTick / GameTime.TicksPerDay;
        if (Window == StatsWindow.TwoYears)
        {
            RefreshSeasonal();
        }
        else
        {
            RefreshDaily();
        }

        StartDay = CurrentDay - Math.Max(0, PlottedCount() - 1) * DayStep;

        DebugSummary =
            $"pop n={MaxSampleCount(PopulationLines)} · " +
            $"{SelectedGoodName} stock n={StockLine.SampleCount} last={FormatValue(StockLine.LastRaw, true)} · " +
            $"net n={NetLine.SampleCount} last={FormatValue(NetLine.LastRaw, true)}/d · " +
            $"knowledge n={KnowledgeLine.SampleCount} last={FormatValue(KnowledgeLine.LastRaw, true)}/d · " +
            $"culture n={CultureLine.SampleCount} last={FormatValue(CultureLine.LastRaw, true)}/d";
    }

    /// <summary>Gets the label of one plotted point: a day number in the daily window, a season in the long view.</summary>
    /// <param name="index">The point's index, oldest first.</param>
    /// <returns>The locale's day or season label.</returns>
    public string PointLabel(int index)
    {
        if (Window == StatsWindow.TwoYears && index >= 0 && index < SeasonLabels.Length)
        {
            return SeasonLabels[index];
        }

        return _strings.Format("ui.stats.day", ("day", (StartDay + ((long)index * DayStep)).ToString()));
    }

    private void RefreshDaily()
    {
        DayStep = 1;
        SeasonLabels = [];
        SeasonsSampled = 0;

        ImmutableArray<StatsLine>.Builder population = ImmutableArray.CreateBuilder<StatsLine>(_data.Tiers.Length);
        int order = 0;
        foreach ((TierId id, string name) in _data.Tiers)
        {
            population.Add(Plot(name, ReadCopy(StatKey.Residents(id)), isMilli: false, StatsPalette.PopulationTier(order)));
            order++;
        }

        PopulationLines = population.MoveToImmutable();

        SelectedGoodName = GoodNameOf(SelectedGood);
        long[] produced = ReadCopy(StatKey.Produced(SelectedGood));
        long[] consumed = ReadCopy(StatKey.Consumed(SelectedGood));
        StockLine = Plot(_strings.Get("ui.stats.stock"), ReadCopy(StatKey.Stock(SelectedGood)), isMilli: true, StatsPalette.Stock);
        NetLine = Plot(_strings.Get("ui.stats.net"), NetOf(produced, consumed), isMilli: true, StatsPalette.Net);
        KnowledgeLine = Plot(
            _strings.Get("ui.stats.knowledge"),
            ReadCopy(StatKey.KnowledgePerDay), isMilli: true, StatsPalette.Knowledge);
        CultureLine = Plot(
            _strings.Get("ui.stats.culture"),
            ReadCopy(StatKey.CulturePerDay), isMilli: true, StatsPalette.Culture);
    }

    private void RefreshSeasonal()
    {
        DayStep = GameTime.DaysPerSeason;

        ImmutableArray<StatsLine>.Builder population = ImmutableArray.CreateBuilder<StatsLine>(_data.Tiers.Length);
        int order = 0;
        long seasons = 0;
        foreach ((TierId id, string name) in _data.Tiers)
        {
            (long[] values, long lineSeasons) = ReadSeasonalCopy(StatKey.Residents(id));
            seasons = Math.Max(seasons, lineSeasons);
            population.Add(SeasonalLine(name, values, isMilli: false, StatsPalette.PopulationTier(order)));
            order++;
        }

        PopulationLines = population.MoveToImmutable();

        SelectedGoodName = GoodNameOf(SelectedGood);
        (long[] produced, long producedSeasons) = ReadSeasonalCopy(StatKey.Produced(SelectedGood));
        (long[] consumed, long consumedSeasons) = ReadSeasonalCopy(StatKey.Consumed(SelectedGood));
        (long[] stock, long stockSeasons) = ReadSeasonalCopy(StatKey.Stock(SelectedGood));
        (long[] knowledge, long knowledgeSeasons) = ReadSeasonalCopy(StatKey.KnowledgePerDay);
        (long[] culture, long cultureSeasons) = ReadSeasonalCopy(StatKey.CulturePerDay);
        seasons = MaxOf(seasons, producedSeasons, consumedSeasons, stockSeasons, knowledgeSeasons, cultureSeasons);
        SeasonsSampled = seasons;

        StockLine = SeasonalLine(_strings.Get("ui.stats.stock"), stock, isMilli: true, StatsPalette.Stock);
        NetLine = SeasonalLine(_strings.Get("ui.stats.net"), NetOf(produced, consumed), isMilli: true, StatsPalette.Net);
        KnowledgeLine = SeasonalLine(
            _strings.Get("ui.stats.knowledge"), knowledge, isMilli: true, StatsPalette.Knowledge);
        CultureLine = SeasonalLine(
            _strings.Get("ui.stats.culture"), culture, isMilli: true, StatsPalette.Culture);

        int points = PlottedCount();
        ImmutableArray<string>.Builder labels = ImmutableArray.CreateBuilder<string>(points);
        for (int i = 0; i < points; i++)
        {
            labels.Add(SeasonLabel(seasons - points + i));
        }

        SeasonLabels = labels.MoveToImmutable();

        // Points are whole seasons: the oldest plotted point starts its season's first day.
        StartDay = (seasons - points) * GameTime.DaysPerSeason;
    }

    private static long[] NetOf(long[] produced, long[] consumed)
    {
        int netCount = Math.Min(produced.Length, consumed.Length);
        long[] net = new long[netCount];
        for (int i = 0; i < netCount; i++)
        {
            net[i] = checked(produced[i] - consumed[i]);
        }

        return net;
    }

    private static long MaxOf(long first, params long[] rest)
    {
        long max = first;
        foreach (long value in rest)
        {
            max = Math.Max(max, value);
        }

        return max;
    }

    /// <summary>Names one season from the world start, through the locale's season label.</summary>
    /// <param name="seasonIndex">The zero-based season from the world start; negative means before the start.</param>
    /// <returns>The display label, or an empty string when the index predates the world.</returns>
    public string SeasonLabel(long seasonIndex)
    {
        if (seasonIndex < 0)
        {
            return string.Empty;
        }

        string season = ((Season)(seasonIndex % GameTime.SeasonsPerYear)) switch
        {
            Season.Spring => _strings.Get("ui.hud.season.spring"),
            Season.Summer => _strings.Get("ui.hud.season.summer"),
            Season.Autumn => _strings.Get("ui.hud.season.autumn"),
            _ => _strings.Get("ui.hud.season.winter"),
        };
        return _strings.Format(
            "ui.stats.season",
            ("season", season),
            ("year", ((seasonIndex / GameTime.SeasonsPerYear) + 1).ToString()));
    }

    private int PlottedCount()
    {
        int n = 0;
        foreach (StatsLine line in PopulationLines)
        {
            n = Math.Max(n, line.Values.Length);
        }

        n = Math.Max(n, StockLine.Values.Length);
        n = Math.Max(n, NetLine.Values.Length);
        n = Math.Max(n, KnowledgeLine.Values.Length);
        return Math.Max(n, CultureLine.Values.Length);
    }

    private static int MaxSampleCount(ImmutableArray<StatsLine> lines)
    {
        int n = 0;
        foreach (StatsLine line in lines)
        {
            n = Math.Max(n, line.SampleCount);
        }

        return n;
    }

    private string GoodNameOf(GoodId good)
    {
        foreach ((GoodId id, string name) in _data.Goods)
        {
            if (id.Equals(good))
            {
                return name;
            }
        }

        return good.Value.ToString();
    }

    private long[] ReadCopy(StatKey key)
    {
        int count = _data.ReadSeries(key, _buffer);
        long[] copy = new long[count];
        Array.Copy(_buffer, copy, count);
        return copy;
    }

    private (long[] Values, long Seasons) ReadSeasonalCopy(StatKey key)
    {
        (int written, long seasons) = _data.ReadSeasonalSeries(key, _seasonalBuffer);
        long[] copy = new long[written];
        Array.Copy(_seasonalBuffer, copy, written);
        return (copy, seasons);
    }

    private static StatsLine SeasonalLine(string label, long[] values, bool isMilli, int colorIndex)
    {
        // Seasonal samples arrive as per-day averages: no further bucketing, same units as the daily window.
        long lastRaw = values.Length == 0 ? 0 : values[^1];
        return new StatsLine(label, values, isMilli, values.Length, lastRaw, colorIndex);
    }

    private StatsLine Plot(string label, long[] raw, bool isMilli, int colorIndex)
    {
        long lastRaw = raw.Length == 0 ? 0 : raw[^1];
        ImmutableArray<StatsBucket> buckets = StatsWindowShape.Buckets(raw.Length, Window);
        long[] plotted = new long[buckets.Length];
        for (int i = 0; i < buckets.Length; i++)
        {
            plotted[i] = StatsWindowShape.Mean(raw, buckets[i]);
        }

        return new StatsLine(label, plotted, isMilli, raw.Length, lastRaw, colorIndex);
    }
}
