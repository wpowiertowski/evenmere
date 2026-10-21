using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Common;
using Evenmere.Sim;

namespace Evenmere.Game.Ui.Progress;

/// <summary>
/// The view-model behind the research and civics panels: one snapshot of rows in display order plus an
/// optional inline note from a rejected command. It has no Godot types and never holds simulation state
/// beyond one refresh; the owning node rebuilds it from the host's queries at most four times a second
/// through <see cref="ViewModel"/>.
/// </summary>
/// <typeparam name="TId">The tech or civic ID type.</typeparam>
public abstract class TreeViewModel<TId> : ViewModel
    where TId : struct, IEquatable<TId>
{
    /// <summary>Initializes a new instance of the <see cref="TreeViewModel{TId}"/> class.</summary>
    /// <param name="content">The content database, for the nodes, eras and names.</param>
    /// <param name="strings">The display text.</param>
    protected TreeViewModel(ContentDb content, UiStrings strings)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(strings);
        Content = content;
        Strings = strings;
        Snapshot = new TreeSnapshot<TId>([], null, 0, 0, 0);
    }

    /// <summary>Gets the rows in display order and the next-era teaser.</summary>
    public TreeSnapshot<TId> Snapshot { get; private set; }

    /// <summary>Gets the calm inline note from the last rejected command, or <see langword="null"/>.</summary>
    public string? Note { get; private set; }

    /// <summary>Gets the content database.</summary>
    protected ContentDb Content { get; }

    /// <summary>Gets the display text.</summary>
    protected UiStrings Strings { get; }

    /// <summary>Reads every node in content order.</summary>
    /// <returns>The node inputs.</returns>
    protected abstract IReadOnlyList<NodeInput<TId>> ReadNodes();

    /// <summary>Gets the node receiving today's points, if any.</summary>
    /// <returns>The target.</returns>
    protected abstract TId? ReadTarget();

    /// <summary>Gets the queued nodes in order, without the target.</summary>
    /// <returns>The queue.</returns>
    protected abstract IReadOnlyList<TId> ReadQueue();

    /// <summary>Tests whether a node is researched or adopted.</summary>
    /// <param name="id">The node.</param>
    /// <returns><see langword="true"/> if it is done.</returns>
    protected abstract bool IsDone(TId id);

    /// <summary>Tests whether a node can be chosen as the target.</summary>
    /// <param name="id">The node.</param>
    /// <returns><see langword="true"/> if it is available.</returns>
    protected abstract bool IsAvailable(TId id);

    /// <summary>Tests whether a node's inspiration has triggered.</summary>
    /// <param name="id">The node.</param>
    /// <returns><see langword="true"/> if it is inspired.</returns>
    protected abstract bool IsInspired(TId id);

    /// <summary>Evaluates an inspiration condition as the simulation does.</summary>
    /// <param name="condition">The condition.</param>
    /// <returns><see langword="true"/> if it holds.</returns>
    protected abstract bool Evaluate(Condition condition);

    /// <summary>Gets the points gained per day.</summary>
    protected abstract Amount PerDay { get; }

    /// <summary>Gets the current era's rank.</summary>
    protected abstract int CurrentEraOrder { get; }

    /// <summary>Tests whether a rejected command is one this panel sent.</summary>
    /// <param name="command">The refused command.</param>
    /// <returns><see langword="true"/> if the panel sent it.</returns>
    protected abstract bool OwnsCommand(Command command);

    /// <summary>Creates the command that makes a node the target.</summary>
    /// <param name="id">The node.</param>
    /// <returns>The command to enqueue.</returns>
    public abstract Command TargetCommand(TId id);

    /// <summary>Creates the command that replaces the whole queue.</summary>
    /// <param name="queue">The queued nodes in order.</param>
    /// <returns>The command to enqueue.</returns>
    public abstract Command QueueCommand(IReadOnlyList<TId> queue);

    /// <summary>Creates the command that appends a node to the end of the current queue.</summary>
    /// <param name="id">The node to append.</param>
    /// <returns>The command to enqueue, or <see langword="null"/> when the node is already queued or is the target.</returns>
    public Command? AppendCommand(TId id)
    {
        List<TId> queue = [.. ReadQueue()];
        if (ReadTarget() is { } target && target.Equals(id))
        {
            return null;
        }

        foreach (TId entry in queue)
        {
            if (entry.Equals(id))
            {
                return null;
            }
        }

        queue.Add(id);
        return QueueCommand(queue);
    }

    /// <summary>Creates the command that moves a queued node up or down.</summary>
    /// <param name="id">The queued node.</param>
    /// <param name="delta">-1 to move it earlier, +1 to move it later.</param>
    /// <returns>The command to enqueue, or <see langword="null"/> when the node cannot move that way.</returns>
    public Command? MoveCommand(TId id, int delta)
    {
        List<TId> queue = [.. ReadQueue()];
        int index = -1;
        for (int i = 0; i < queue.Count; i++)
        {
            if (queue[i].Equals(id))
            {
                index = i;
                break;
            }
        }

        int other = index + Math.Sign(delta);
        if (index < 0 || other < 0 || other >= queue.Count)
        {
            return null;
        }

        (queue[index], queue[other]) = (queue[other], queue[index]);
        return QueueCommand(queue);
    }

    /// <summary>Creates the command that moves a queued node to a new position, for drag reorder.</summary>
    /// <param name="id">The queued node.</param>
    /// <param name="position">The position to move it to, clamped into the queue.</param>
    /// <returns>The command to enqueue, or <see langword="null"/> when the node is not queued.</returns>
    public Command? MoveToCommand(TId id, int position)
    {
        List<TId> queue = [.. ReadQueue()];
        int index = -1;
        for (int i = 0; i < queue.Count; i++)
        {
            if (queue[i].Equals(id))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return null;
        }

        queue.RemoveAt(index);
        queue.Insert(Math.Clamp(position, 0, queue.Count), id);
        return QueueCommand(queue);
    }

    /// <summary>Shows a rejected command as the calm inline note, when the command is one this panel sent.</summary>
    /// <param name="rejected">The drained event.</param>
    public void NoteRejected(CommandRejected rejected)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        if (!OwnsCommand(rejected.Command))
        {
            return;
        }

        Note = rejected.Code == RejectReason.NodeNotAvailable
            ? Strings.Get("ui.tree.note.node")
            : Strings.Get("ui.tree.note.other");
    }

    /// <summary>Clears the inline note, called when the player acts or the panel reopens.</summary>
    public void ClearNote() => Note = null;

    /// <inheritdoc />
    protected override void OnRefresh()
    {
        var names = new ContentConditionNames(Content, Strings);
        Snapshot = TreeModel.Build(
            ReadNodes(),
            ReadTarget(),
            ReadQueue(),
            IsDone,
            IsAvailable,
            IsInspired,
            Evaluate,
            PerDay,
            Strings,
            condition => ConditionText.Format(condition, Strings, names),
            CurrentEraOrder);
    }
}
