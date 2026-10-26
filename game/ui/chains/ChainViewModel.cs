using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Explain;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Game.Ui.Chains;

/// <summary>One live producer of a graphed good, as the chain viewer needs it.</summary>
/// <param name="Id">The building's entity ID.</param>
/// <param name="Type">The building's type.</param>
/// <param name="ProductivityPerMille">The productivity applied on the last tick; 1000 is a full cycle speed.</param>
/// <param name="Stall">Why the building is not producing; <see cref="StallReason.None"/> when it is.</param>
public readonly record struct ChainProducer(
    EntityId Id,
    BuildingTypeId Type,
    int ProductivityPerMille,
    StallReason Stall);

/// <summary>
/// The live numbers behind one chain graph. The viewer reads the simulation through this, never through the queries
/// directly, so the view-model stays Godot-free and testable. The game wires it to <see cref="SimChainData"/>; tests
/// bring their own in-memory stand-in.
/// </summary>
public interface IChainData
{
    /// <summary>Lists the finished buildings producing a good, in ascending building-ID order.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The producers; empty when nothing produces the good.</returns>
    IReadOnlyList<ChainProducer> ProducersOf(GoodId good);

    /// <summary>Gets how much of a good the island produced during the previous whole day, in milli-units.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The amount in milli-units; 0 during the first day.</returns>
    long ProducedPerDayMilli(GoodId good);

    /// <summary>Gets how much of a good the island consumed during the previous whole day, in milli-units.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The amount in milli-units; 0 when nothing was consumed or nothing was sampled yet.</returns>
    long ConsumedPerDayMilli(GoodId good);

    /// <summary>Explains a good: its supply and demand, its producer groups, and the chained causes of any shortage.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The explanation; never <see langword="null"/>.</returns>
    Explanation Explain(GoodId good);
}

/// <summary>One graphed good with its live numbers, ready to render: names and rates are already formatted text.</summary>
/// <param name="Good">The good.</param>
/// <param name="Name">The display name.</param>
/// <param name="Column">The grid column (0 is the raw inputs).</param>
/// <param name="Row">The grid row within the column.</param>
/// <param name="RecipeLine">What makes the good: the recipe and its buildings, such as "Bake bread · Bakery".</param>
/// <param name="StatsLine">The producer count, buildings and average productivity, such as "2 × Bakery · 45%".</param>
/// <param name="RatesLine">The produced and consumed rates, such as "+12.0 −8.0 per day".</param>
/// <param name="IsShortage">Whether the good lies on the shortage path behind the selected root.</param>
/// <param name="IsBottleneck">Whether the good is the root cause at the end of that path.</param>
public sealed record ChainRow(
    GoodId Good,
    string Name,
    int Column,
    int Row,
    string RecipeLine,
    string StatsLine,
    string RatesLine,
    bool IsShortage,
    bool IsBottleneck);

/// <summary>One building producing the selected good, ready to render.</summary>
/// <param name="Id">The building's entity ID.</param>
/// <param name="Name">The display name with its entity number, such as "Bakery #12".</param>
/// <param name="ProductivityPerMille">The productivity applied on the last tick.</param>
/// <param name="StallText">Why it is not producing, such as "missing input"; "working" when it is.</param>
/// <param name="IsWorst">Whether this is the worst producer the viewer focuses.</param>
public sealed record ChainRowBuilding(
    EntityId Id,
    string Name,
    int ProductivityPerMille,
    string StallText,
    bool IsWorst);

/// <summary>
/// The production chain viewer model (M2-21): the <see cref="ChainGraph"/> of the selected good annotated with live
/// numbers. Building counts and average productivity come from the producers behind each good; produced and consumed
/// per day come from the statistics daily samples; the bottleneck comes from <c>ExplainGood</c>'s missing-input
/// chain. It holds no simulation state of its own beyond what it shows.
/// </summary>
public sealed class ChainViewModel : ViewModel
{
    private const string MissingInputKey = "explain.good.cause.missing_input";
    private const string SupplyKey = "explain.good.supply";

    private readonly ContentDb _content;
    private readonly UiStrings _strings;
    private readonly IChainData _data;
    private GoodId _selected;
    private bool _hasSelection;

    /// <summary>Initializes a new instance of the <see cref="ChainViewModel"/> class.</summary>
    /// <param name="content">The content database, for names and recipes.</param>
    /// <param name="strings">The display text.</param>
    /// <param name="data">The live numbers.</param>
    public ChainViewModel(ContentDb content, UiStrings strings, IChainData data)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        Rows = [];
        Buildings = [];
        WorstBuilding = EntityId.None;
        DebugLine = string.Empty;
    }

    /// <summary>Gets the selected good.</summary>
    public GoodId Selected => _selected;

    /// <summary>Gets whether a good is selected.</summary>
    public bool HasSelection => _hasSelection;

    /// <summary>Gets the annotated graph rows in (row, column) order, the root first.</summary>
    public IReadOnlyList<ChainRow> Rows { get; private set; }

    /// <summary>Gets the graph links behind <see cref="Rows"/>, for drawing connectors.</summary>
    public IReadOnlyList<ChainEdge> Edges { get; private set; } = [];

    /// <summary>Gets the shortage path behind the selection, from the selected good down to the root cause.</summary>
    public IReadOnlyList<GoodId> ShortagePath { get; private set; } = [];

    /// <summary>Gets the panel title, such as "Bread chain".</summary>
    public string Title => _hasSelection
        ? _strings.Format("ui.chains.title", ("name", GoodDisplayName(_selected)))
        : _strings.Get("ui.chains.title.empty");

    /// <summary>Gets the bottleneck summary line, when <see cref="HasBottleneck"/>; otherwise an empty string.</summary>
    public string BottleneckLine => Bottleneck.HasValue
        ? _strings.Format("ui.chains.bottleneck", ("name", GoodDisplayName(Bottleneck.Value)))
        : string.Empty;

    /// <summary>Gets the buildings producing the selected good, in ascending building-ID order.</summary>
    public IReadOnlyList<ChainRowBuilding> Buildings { get; private set; }

    /// <summary>Gets the worst building producing the selected good, or <see cref="EntityId.None"/> when there is none.</summary>
    public EntityId WorstBuilding { get; private set; }

    /// <summary>Gets whether the shortage path behind the selection ends at a root cause.</summary>
    public bool HasBottleneck => Bottleneck.HasValue;

    /// <summary>Gets the root cause at the end of the shortage path, when <see cref="HasBottleneck"/>.</summary>
    public GoodId? Bottleneck { get; private set; }

    /// <summary>Gets the selected good's rates straight from the statistics samples, for the acceptance debug label.</summary>
    public string DebugLine { get; private set; }

    /// <summary>Selects the good whose chain to show.</summary>
    /// <param name="good">The good.</param>
    public void Select(GoodId good)
    {
        if (_hasSelection && _selected == good)
        {
            return;
        }

        _selected = good;
        _hasSelection = true;
        MarkDirty();
    }

    /// <inheritdoc />
    protected override void OnRefresh()
    {
        if (!_hasSelection)
        {
            Rows = [];
            Edges = [];
            ShortagePath = [];
            Buildings = [];
            WorstBuilding = EntityId.None;
            Bottleneck = null;
            DebugLine = string.Empty;
            return;
        }

        var graph = ChainGraph.Build(_content, _selected);
        Edges = graph.Edges;
        var inGraph = new HashSet<GoodId>();
        foreach (ChainGoodNode node in graph.Goods)
        {
            inGraph.Add(node.Good);
        }

        List<GoodId> shortage = WalkShortage(_data.Explain(_selected), inGraph);
        ShortagePath = shortage;
        var shortageSet = new HashSet<GoodId>(shortage);
        GoodId? bottleneck = shortage.Count > 0 ? shortage[^1] : null;
        Bottleneck = bottleneck;

        var rows = new List<ChainRow>(graph.Goods.Length);
        foreach (ChainGoodNode node in graph.Goods)
        {
            IReadOnlyList<ChainProducer> producers = _data.ProducersOf(node.Good);
            long produced = _data.ProducedPerDayMilli(node.Good);
            long consumed = _data.ConsumedPerDayMilli(node.Good);
            bool isShortage = shortageSet.Contains(node.Good);
            bool isBottleneck = bottleneck.HasValue && bottleneck.Value == node.Good;
            rows.Add(new ChainRow(
                node.Good,
                GoodDisplayName(node.Good),
                node.Column,
                node.Row,
                RecipeLine(node),
                StatsLine(producers),
                RatesLine(produced, consumed),
                isShortage,
                isBottleneck));
        }

        Rows = rows;
        Buildings = BuildingsOf(_selected, out EntityId worst);
        WorstBuilding = worst;
        DebugLine = DebugLineFor(_selected);
    }

    // Follows the missing-input causes from the selected root down to the root cause: each cause line names the
    // missing good and nests that good's own supply line, whose children name the next cause. Stops at goods outside
    // the graph, at repeats, and at a bounded depth, so a surprising tree cannot loop the viewer.
    private static List<GoodId> WalkShortage(Explanation explanation, HashSet<GoodId> inGraph)
    {
        var path = new List<GoodId>(4);
        ExplanationLine line = explanation.Root;
        for (int depth = 0; depth <= inGraph.Count; depth++)
        {
            ExplanationLine? cause = null;
            foreach (ExplanationLine child in line.Children)
            {
                if (string.Equals(child.Key, MissingInputKey, StringComparison.Ordinal)
                    && child.Subjects.Length > 0)
                {
                    cause = child;
                    break;
                }
            }

            if (cause is null)
            {
                break;
            }

            GoodId missing = cause.Subjects[0].AsGood();
            if (!inGraph.Contains(missing) || path.Contains(missing))
            {
                break;
            }

            path.Add(missing);
            ExplanationLine? nested = null;
            foreach (ExplanationLine child in cause.Children)
            {
                if (string.Equals(child.Key, SupplyKey, StringComparison.Ordinal))
                {
                    nested = child;
                    break;
                }
            }

            if (nested is null)
            {
                break;
            }

            line = nested;
        }

        return path;
    }

    private List<ChainRowBuilding> BuildingsOf(GoodId good, out EntityId worst)
    {
        IReadOnlyList<ChainProducer> producers = _data.ProducersOf(good);
        var rows = new List<ChainRowBuilding>(producers.Count);
        worst = EntityId.None;
        int worstProductivity = int.MaxValue;
        foreach (ChainProducer producer in producers)
        {
            if (producer.ProductivityPerMille < worstProductivity
                || (producer.ProductivityPerMille == worstProductivity
                    && (worst.IsNone || producer.Id < worst)))
            {
                worstProductivity = producer.ProductivityPerMille;
                worst = producer.Id;
            }
        }

        foreach (ChainProducer producer in producers)
        {
            rows.Add(new ChainRowBuilding(
                producer.Id,
                $"{BuildingDisplayName(producer.Type)} #{producer.Id.Value}",
                producer.ProductivityPerMille,
                ExplainFormat.FormatArg(ExplainArg.Of(producer.Stall), _content, _strings),
                producer.Id == worst));
        }

        return rows;
    }

    private string RecipeLine(ChainGoodNode node)
    {
        if (node.ProducedBy.IsEmpty)
        {
            return _strings.Get("ui.chains.raw");
        }

        var parts = new List<string>(node.ProducedBy.Length);
        foreach (RecipeId recipe in node.ProducedBy)
        {
            string recipeName = RecipeDisplayName(recipe);
            var runners = new List<string>();
            for (int b = 0; b < _content.Buildings.Count; b++)
            {
                var type = BuildingTypeId.From(checked((ushort)b));
                if (_content.Get(type).Recipe == recipe)
                {
                    runners.Add(BuildingDisplayName(type));
                }
            }

            parts.Add(runners.Count == 0 ? recipeName : $"{recipeName} · {string.Join(", ", runners)}");
        }

        return string.Join(" / ", parts);
    }

    private string StatsLine(IReadOnlyList<ChainProducer> producers)
    {
        if (producers.Count == 0)
        {
            return _strings.Get("ui.chains.noproducers");
        }

        var types = new List<string>();
        long sum = 0;
        foreach (ChainProducer producer in producers)
        {
            sum += producer.ProductivityPerMille;
            string name = BuildingDisplayName(producer.Type);
            if (!types.Contains(name, StringComparer.Ordinal))
            {
                types.Add(name);
            }
        }

        int average = (int)(sum / Math.Max(1, producers.Count));
        string percent = ExplainFormat.FormatPercent(Ratio.FromPerMille(average));
        return $"{NumberFormat.Count(producers.Count)} × {string.Join(", ", types)} · {percent}";
    }

    private string RatesLine(long producedMilli, long consumedMilli)
    {
        string produced = NumberFormat.Rate(Amount.FromMilli(producedMilli));
        string consumed = NumberFormat.Rate(Amount.FromMilli(consumedMilli));
        string perDay = _strings.Get("ui.chains.perday");
        return $"+{produced} −{consumed} {perDay}";
    }

    private string DebugLineFor(GoodId good)
    {
        long produced = _data.ProducedPerDayMilli(good);
        long consumed = _data.ConsumedPerDayMilli(good);
        string name = GoodDisplayName(good);
        string producedText = NumberFormat.Rate(Amount.FromMilli(produced));
        string consumedText = NumberFormat.Rate(Amount.FromMilli(consumed));
        return $"{name}: +{producedText} −{consumedText} per day (stats)";
    }

    private string GoodDisplayName(GoodId good)
    {
        string key = $"good.{_content.NameOf(good)}.name";
        string text = _strings.Get(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? _content.NameOf(good) : text;
    }

    private string BuildingDisplayName(BuildingTypeId type)
    {
        string key = $"building.{_content.NameOf(type)}.name";
        string text = _strings.Get(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? _content.NameOf(type) : text;
    }

    private string RecipeDisplayName(RecipeId recipe)
    {
        string key = $"recipe.{_content.NameOf(recipe)}.name";
        string text = _strings.Get(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? _content.NameOf(recipe) : text;
    }
}
