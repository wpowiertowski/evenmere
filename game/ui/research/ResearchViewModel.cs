using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Evenmere.Sim;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Systems.Research.Commands;

namespace Evenmere.Game.Ui.Research;

/// <summary>
/// The research panel's view-model: the tech rows from <see cref="IResearchQueries"/> and
/// <see cref="IProgressionQueries"/>, plus an inline note from rejected research commands.
/// </summary>
public sealed class ResearchViewModel : TreeViewModel<TechId>
{
    private readonly IResearchQueries _research;
    private readonly IProgressionQueries _progression;
    private List<TechId> _graphIds = new();

    /// <summary>Initializes a new instance of the <see cref="ResearchViewModel"/> class.</summary>
    /// <param name="content">The content database.</param>
    /// <param name="strings">The display text.</param>
    /// <param name="research">The research queries.</param>
    /// <param name="progression">The unlock, era and condition queries.</param>
    public ResearchViewModel(ContentDb content, UiStrings strings, IResearchQueries research, IProgressionQueries progression)
        : base(content, strings)
    {
        ArgumentNullException.ThrowIfNull(research);
        ArgumentNullException.ThrowIfNull(progression);
        _research = research;
        _progression = progression;
    }

    /// <inheritdoc />
    public override Command TargetCommand(TechId id) => new SetResearchTarget(id);

    /// <inheritdoc />
    public override Command QueueCommand(IReadOnlyList<TechId> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        var copy = new TechId[queue.Count];
        for (int i = 0; i < queue.Count; i++)
        {
            copy[i] = queue[i];
        }

        return new SetResearchQueue(copy);
    }

    /// <inheritdoc />
    protected override IReadOnlyList<NodeInput<TechId>> ReadNodes()
    {
        var nodes = new List<NodeInput<TechId>>(Content.Techs.Count);
        for (int i = 0; i < Content.Techs.Count; i++)
        {
            var id = TechId.From(checked((ushort)i));
            TechDef def = Content.Get(id);
            string name = Content.Techs.Names[i];
            nodes.Add(new NodeInput<TechId>(
                id,
                Strings.Get($"tech.{name}.name"),
                def.Era,
                EraName(def.Era),
                MissingNames(def.Requires),
                def.Inspiration,
                def.Cost,
                _research.Cost(id),
                _research.Progress(id)));
        }

        return nodes;
    }

    /// <inheritdoc />
    protected override TechId? ReadTarget() => _research.Target;

    /// <inheritdoc />
    protected override IReadOnlyList<TechId> ReadQueue() => _research.Queue;

    /// <inheritdoc />
    protected override bool IsDone(TechId id) => _research.IsDone(id);

    /// <inheritdoc />
    protected override bool IsAvailable(TechId id) => _research.IsAvailable(id);

    /// <inheritdoc />
    protected override bool IsInspired(TechId id) => _research.IsInspired(id);

    /// <inheritdoc />
    protected override bool Evaluate(Condition condition) => _progression.Evaluate(condition);

    /// <inheritdoc />
    protected override Amount PerDay => _research.PointsPerDay;

    /// <inheritdoc />
    protected override int CurrentEraOrder => Content.Get(_progression.CurrentEra).Order;

    /// <inheritdoc />
    protected override bool OwnsCommand(Command command) =>
        command is SetResearchTarget or SetResearchQueue;

    /// <summary>
    /// Builds the cards the <see cref="TreeView"/> shows: one per visible tech in content order, plus the
    /// layout inputs in the same order. States, costs, inspiration text and unlocks come from the snapshot rows,
    /// so the graph agrees with the list; prerequisites address cards by their position in the returned lists.
    /// Content order keeps cards in place when the target or queue changes; they only move when a new era begins.
    /// </summary>
    /// <param name="iconFor">The placeholder glyph for a content building ID, or <see langword="null"/> when it has none.</param>
    /// <returns>The cards and their layout inputs.</returns>
    public TreeGraph BuildGraph(Func<string, string?> iconFor)
    {
        ArgumentNullException.ThrowIfNull(iconFor);
        TreeSnapshot<TechId> snapshot = Snapshot;

        // Content order, not group order: cards keep their places when the target or queue changes,
        // and only move when a new era begins. Rows for later eras are absent from the snapshot.
        var rowById = new Dictionary<TechId, NodeRow<TechId>>(snapshot.Rows.Count);
        foreach (NodeRow<TechId> row in snapshot.Rows)
        {
            rowById[row.Id] = row;
        }

        var ids = new List<TechId>(snapshot.Rows.Count);
        for (int i = 0; i < Content.Techs.Count; i++)
        {
            var id = TechId.From(checked((ushort)i));
            if (rowById.ContainsKey(id))
            {
                ids.Add(id);
            }
        }

        var indexById = new Dictionary<TechId, int>(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            indexById[ids[i]] = i;
        }

        var nodes = new List<GraphNode>(ids.Count);
        var layout = new List<LayoutNode>(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            NodeRow<TechId> row = rowById[ids[i]];
            TechDef def = Content.Get(row.Id);

            var requires = new List<int>(def.Requires.Length);
            foreach (TechId require in def.Requires)
            {
                if (indexById.TryGetValue(require, out int requireIndex))
                {
                    requires.Add(requireIndex);
                }
            }

            GraphNodeState state = row.Group switch
            {
                ProgressGroup.Researched => GraphNodeState.Done,
                ProgressGroup.Target => GraphNodeState.Target,
                ProgressGroup.Queue => GraphNodeState.Queued,
                ProgressGroup.Available => GraphNodeState.Available,
                _ => GraphNodeState.Locked,
            };

            nodes.Add(new GraphNode(
                i,
                def.Gateway ? $"\u2605 {row.Name}" : row.Name,
                row.EraName,
                def.Gateway,
                state,
                row.QueueIndex,
                StateText(row),
                $"{row.ProgressText} \u00b7 {row.EtaText}",
                row.Fraction,
                row.InspirationText,
                row.Inspired,
                row.Inspired ? Strings.Get("ui.tree.row.inspired") : string.Empty,
                row.MissingText,
                UnlocksOf(def, iconFor),
                TooltipOf(row)));
            layout.Add(new LayoutNode(def.Era, row.EraName, requires, def.Gateway));
        }

        _graphIds = ids;
        return new TreeGraph(nodes, layout);
    }

    /// <summary>Gets the tech behind a graph card.</summary>
    /// <param name="index">The card's index in the last <see cref="BuildGraph"/> answer.</param>
    /// <returns>The tech.</returns>
    public TechId GraphId(int index) => _graphIds[index];

    /// <summary>Gets how many cards the last <see cref="BuildGraph"/> answer held.</summary>
    public int GraphCount => _graphIds.Count;

    private string StateText(NodeRow<TechId> row) => row.Group switch
    {
        ProgressGroup.Target => Strings.Get("ui.tree.group.target"),
        ProgressGroup.Queue => Strings.Format(
            "ui.tree.row.queue_position",
            ("position", NumberFormat.Count(row.QueueIndex + 1))),
        ProgressGroup.Researched => Strings.Get("ui.tree.group.researched"),
        ProgressGroup.Available => Strings.Get("ui.tree.group.available"),
        _ => row.MissingText.Length > 0 ? row.MissingText : Strings.Get("ui.tree.group.locked"),
    };

    private string TooltipOf(NodeRow<TechId> row)
    {
        string tip = Strings.Get(row.Group == ProgressGroup.Queue ? "ui.tree.row.tip.queued" : "ui.tree.row.tip");
        return row.MissingText.Length > 0 ? row.MissingText + "\n" + tip : tip;
    }

    private List<GraphUnlock> UnlocksOf(TechDef def, Func<string, string?> iconFor) =>
        new(EffectText.Describe(Content, Strings, def.Effects, iconFor, ContentNames.NameOf));

    private string EraName(int order)
    {
        foreach (EraId era in Content.ErasByOrder)
        {
            if (Content.Get(era).Order == order)
            {
                return Strings.Get($"era.{Content.Eras.NameOf(era)}.name");
            }
        }

        return Strings.Get("ui.hud.unavailable");
    }

    private List<string> MissingNames(System.Collections.Immutable.ImmutableArray<TechId> requires)
    {
        var missing = new List<string>();
        foreach (TechId require in requires)
        {
            if (!_research.IsDone(require))
            {
                missing.Add(Strings.Get($"tech.{Content.Techs.NameOf(require)}.name"));
            }
        }

        return missing;
    }
}
