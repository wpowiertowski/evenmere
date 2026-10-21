using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
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
