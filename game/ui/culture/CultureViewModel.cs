using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Evenmere.Game.Ui.Research;
using Evenmere.Sim;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Systems.Culture.Traditions.Commands;

namespace Evenmere.Game.Ui.Culture;

/// <summary>
/// The civics panel's view-model: the civic rows from <see cref="ICultureQueries"/> and
/// <see cref="IProgressionQueries"/>, plus an inline note from rejected civic commands.
/// </summary>
public sealed class CultureViewModel : TreeViewModel<CivicId>
{
    private readonly ICultureQueries _culture;
    private readonly IProgressionQueries _progression;
    private List<CivicId> _graphIds = new();

    /// <summary>Initializes a new instance of the <see cref="CultureViewModel"/> class.</summary>
    /// <param name="content">The content database.</param>
    /// <param name="strings">The display text.</param>
    /// <param name="culture">The civic queries.</param>
    /// <param name="progression">The unlock, era and condition queries.</param>
    public CultureViewModel(ContentDb content, UiStrings strings, ICultureQueries culture, IProgressionQueries progression)
        : base(content, strings)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(progression);
        _culture = culture;
        _progression = progression;
    }

    /// <inheritdoc />
    public override Command TargetCommand(CivicId id) => new SetCivicTarget(id);

    /// <inheritdoc />
    public override Command QueueCommand(IReadOnlyList<CivicId> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        var copy = new CivicId[queue.Count];
        for (int i = 0; i < queue.Count; i++)
        {
            copy[i] = queue[i];
        }

        return new SetCivicQueue(copy);
    }

    /// <inheritdoc />
    protected override IReadOnlyList<NodeInput<CivicId>> ReadNodes()
    {
        var nodes = new List<NodeInput<CivicId>>(Content.Civics.Count);
        for (int i = 0; i < Content.Civics.Count; i++)
        {
            var id = CivicId.From(checked((ushort)i));
            CivicDef def = Content.Get(id);
            string name = Content.Civics.Names[i];
            nodes.Add(new NodeInput<CivicId>(
                id,
                Strings.Get($"civic.{name}.name"),
                def.Era,
                EraName(def.Era),
                MissingNames(def.Requires),
                def.Inspiration,
                def.Cost,
                _culture.Cost(id),
                _culture.Progress(id)));
        }

        return nodes;
    }

    /// <inheritdoc />
    protected override CivicId? ReadTarget() => _culture.Target;

    /// <inheritdoc />
    protected override IReadOnlyList<CivicId> ReadQueue() => _culture.Queue;

    /// <inheritdoc />
    protected override bool IsDone(CivicId id) => _culture.IsDone(id);

    /// <inheritdoc />
    protected override bool IsAvailable(CivicId id) => _culture.IsAvailable(id);

    /// <inheritdoc />
    protected override bool IsInspired(CivicId id) => _culture.IsInspired(id);

    /// <inheritdoc />
    protected override bool Evaluate(Condition condition) => _progression.Evaluate(condition);

    /// <inheritdoc />
    protected override Amount PerDay => _culture.PointsPerDay;

    /// <inheritdoc />
    protected override int CurrentEraOrder => Content.Get(_progression.CurrentEra).Order;

    /// <inheritdoc />
    protected override bool OwnsCommand(Command command) =>
        command is SetCivicTarget or SetCivicQueue or SetTradition;

    /// <summary>
    /// Builds the cards the <see cref="CivicsTreeView"/> shows: one per visible civic in content order, plus the
    /// layout inputs in the same order. The bulk of the formatting lives in <see cref="CivicGraphBuilder"/>.
    /// </summary>
    /// <param name="iconFor">The placeholder glyph for a content building ID, or <see langword="null"/> when it has none.</param>
    /// <returns>The cards and their layout inputs.</returns>
    public TreeGraph BuildGraph(Func<string, string?> iconFor)
    {
        ArgumentNullException.ThrowIfNull(iconFor);
        TreeGraph graph = CivicGraphBuilder.Build(Content, Strings, Snapshot, iconFor, out List<CivicId> ids);
        _graphIds = ids;
        return graph;
    }

    /// <summary>Gets the civic behind a graph card.</summary>
    /// <param name="index">The card's index in the last <see cref="BuildGraph"/> answer.</param>
    /// <returns>The civic.</returns>
    public CivicId GraphId(int index) => _graphIds[index];

    /// <summary>Gets how many cards the last <see cref="BuildGraph"/> answer held.</summary>
    public int GraphCount => _graphIds.Count;

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

    private List<string> MissingNames(System.Collections.Immutable.ImmutableArray<CivicId> requires)
    {
        var missing = new List<string>();
        foreach (CivicId require in requires)
        {
            if (!_culture.IsDone(require))
            {
                missing.Add(Strings.Get($"civic.{Content.Civics.NameOf(require)}.name"));
            }
        }

        return missing;
    }
}
