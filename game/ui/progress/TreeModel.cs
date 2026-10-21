using Evenmere.Content.Conditions;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Common;

namespace Evenmere.Game.Ui.Progress;

/// <summary>
/// One research or civic node as the panels read it: content facts in, no query calls.
/// </summary>
/// <param name="Id">The tech or civic ID.</param>
/// <param name="Name">The display name.</param>
/// <param name="EraOrder">The era's rank; only eras at or below the current one are shown.</param>
/// <param name="EraName">The display name of the node's era, shown on each row.</param>
/// <param name="MissingRequires">The display names of the prerequisites that are not done yet.</param>
/// <param name="Inspiration">The condition that discounts the node's cost, if it has one.</param>
/// <param name="FullCost">The node's full cost, before any inspiration discount.</param>
/// <param name="Cost">What the node still costs in all, after any inspiration discount.</param>
/// <param name="Progress">What the node has received so far.</param>
/// <typeparam name="TId">The tech or civic ID type.</typeparam>
public sealed record NodeInput<TId>(
    TId Id,
    string Name,
    int EraOrder,
    string EraName,
    IReadOnlyList<string> MissingRequires,
    Condition? Inspiration,
    Amount FullCost,
    Amount Cost,
    Amount Progress)
    where TId : struct;

/// <summary>
/// One row of a research or civics panel: everything the row shows, already formatted.
/// </summary>
/// <param name="Id">The tech or civic ID, for the commands the row sends.</param>
/// <param name="Name">The display name.</param>
/// <param name="Group">Which state group shows the row.</param>
/// <param name="QueueIndex">The position in the queue, or -1 when the row is not queued.</param>
/// <param name="EraName">The display name of the node's era.</param>
/// <param name="CostText">The discounted cost, in whole units.</param>
/// <param name="FullCostText">The full cost, in whole units, shown struck through when the node is inspired.</param>
/// <param name="Discounted">Whether the inspiration has cut the cost.</param>
/// <param name="ProgressText">The progress as <c>{done} of {cost}</c>.</param>
/// <param name="Fraction">How full the progress bar is, from 0 to 1.</param>
/// <param name="EtaText">The days left, or the dash when nothing is gained per day.</param>
/// <param name="InspirationText">The inspiration sentence with its met or unmet mark.</param>
/// <param name="InspirationMet">Whether the inspiration condition holds.</param>
/// <param name="Inspired">Whether the inspiration has triggered.</param>
/// <param name="MissingText">The missing prerequisites, or empty when none are missing.</param>
/// <typeparam name="TId">The tech or civic ID type.</typeparam>
public sealed record NodeRow<TId>(
    TId Id,
    string Name,
    ProgressGroup Group,
    int QueueIndex,
    string EraName,
    string CostText,
    string FullCostText,
    bool Discounted,
    string ProgressText,
    double Fraction,
    string EtaText,
    string InspirationText,
    bool InspirationMet,
    bool Inspired,
    string MissingText)
    where TId : struct;

/// <summary>
/// A panel's rows in display order, plus the teaser for the next era that has nodes.
/// </summary>
/// <param name="Rows">The rows: the target, the queue in order, then available, locked and researched nodes.</param>
/// <param name="NextEraName">The display name of the closest coming era that has nodes, or <see langword="null"/>.</param>
/// <param name="NextEraCount">How many nodes that coming era holds.</param>
/// <param name="DoneCount">How many nodes are researched or adopted.</param>
/// <param name="TotalCount">How many nodes the trees hold in all.</param>
/// <typeparam name="TId">The tech or civic ID type.</typeparam>
public sealed record TreeSnapshot<TId>(
    IReadOnlyList<NodeRow<TId>> Rows,
    string? NextEraName,
    int NextEraCount,
    int DoneCount,
    int TotalCount)
    where TId : struct;

/// <summary>
/// Groups research or civic nodes the way the panels show them: the target, the queue in order,
/// available, locked (with the missing prerequisites named) and researched nodes, showing only the
/// eras that have begun plus one teaser line for the next era. It has no Godot types and reads its
/// inputs only, so the game test project can exercise it on its own.
/// </summary>
public static class TreeModel
{
    /// <summary>Builds a panel's rows from the nodes and the queries' answers about them.</summary>
    /// <param name="nodes">Every node in content order, which is also the display order inside each group.</param>
    /// <param name="target">The node receiving today's points, if any.</param>
    /// <param name="queue">The queued nodes in order, without the target.</param>
    /// <param name="isDone">Whether a node is researched or adopted.</param>
    /// <param name="isAvailable">Whether a node can be chosen as the target.</param>
    /// <param name="isInspired">Whether a node's inspiration has triggered.</param>
    /// <param name="evaluate">Whether an inspiration condition holds.</param>
    /// <param name="perDay">The points gained per day.</param>
    /// <param name="strings">The display text.</param>
    /// <param name="conditionText">The inspiration condition as a sentence.</param>
    /// <param name="currentEraOrder">The current era's rank; nodes of later eras are hidden behind the teaser.</param>
    /// <typeparam name="TId">The tech or civic ID type.</typeparam>
    /// <returns>The rows in display order and the next-era teaser.</returns>
    public static TreeSnapshot<TId> Build<TId>(
        IReadOnlyList<NodeInput<TId>> nodes,
        TId? target,
        IReadOnlyList<TId> queue,
        Func<TId, bool> isDone,
        Func<TId, bool> isAvailable,
        Func<TId, bool> isInspired,
        Func<Condition, bool> evaluate,
        Amount perDay,
        UiStrings strings,
        Func<Condition, string> conditionText,
        int currentEraOrder)
        where TId : struct, IEquatable<TId>
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(isDone);
        ArgumentNullException.ThrowIfNull(isAvailable);
        ArgumentNullException.ThrowIfNull(isInspired);
        ArgumentNullException.ThrowIfNull(evaluate);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(conditionText);

        var queueIndex = new Dictionary<TId, int>();
        for (int i = 0; i < queue.Count; i++)
        {
            queueIndex.TryAdd(queue[i], i);
        }

        var rows = new List<(NodeRow<TId> Row, int Input)>(nodes.Count);
        string? nextEraName = null;
        int nextEraOrder = int.MaxValue;
        int nextEraCount = 0;
        int doneCount = 0;

        for (int n = 0; n < nodes.Count; n++)
        {
            NodeInput<TId> node = nodes[n];
            if (isDone(node.Id))
            {
                doneCount++;
            }

            if (node.EraOrder > currentEraOrder)
            {
                if (node.EraOrder < nextEraOrder)
                {
                    nextEraOrder = node.EraOrder;
                    nextEraName = node.EraName;
                    nextEraCount = 1;
                }
                else if (node.EraOrder == nextEraOrder)
                {
                    nextEraCount++;
                }

                continue;
            }

            rows.Add((MakeRow(node, target, queueIndex, isDone, isAvailable, isInspired, evaluate, perDay, strings, conditionText), n));
        }

        // List.Sort is not stable, so the content order inside each group comes from the input index.
        rows.Sort(static (left, right) =>
        {
            int group = left.Row.Group.CompareTo(right.Row.Group);
            if (group != 0)
            {
                return group;
            }

            return left.Row.Group == ProgressGroup.Queue
                ? left.Row.QueueIndex.CompareTo(right.Row.QueueIndex)
                : left.Input.CompareTo(right.Input);
        });

        return new TreeSnapshot<TId>(
            rows.Select(static entry => entry.Row).ToList(),
            nextEraName,
            nextEraCount,
            doneCount,
            nodes.Count);
    }

    private static NodeRow<TId> MakeRow<TId>(
        NodeInput<TId> node,
        TId? target,
        Dictionary<TId, int> queueIndex,
        Func<TId, bool> isDone,
        Func<TId, bool> isAvailable,
        Func<TId, bool> isInspired,
        Func<Condition, bool> evaluate,
        Amount perDay,
        UiStrings strings,
        Func<Condition, string> conditionText)
        where TId : struct, IEquatable<TId>
    {
        bool done = isDone(node.Id);
        bool isTarget = target is { } current && current.Equals(node.Id);
        bool queued = queueIndex.TryGetValue(node.Id, out int index);

        ProgressGroup group = done
            ? ProgressGroup.Researched
            : isTarget ? ProgressGroup.Target
            : queued ? ProgressGroup.Queue
            : isAvailable(node.Id) ? ProgressGroup.Available
            : ProgressGroup.Locked;

        bool inspired = isInspired(node.Id);
        int? days = ProgressMath.EtaDays(node.Cost, node.Progress, perDay);
        string missing = node.MissingRequires.Count == 0
            ? string.Empty
            : strings.Format("ui.tree.row.requires", ("names", string.Join(", ", node.MissingRequires)));

        string inspirationText;
        bool inspirationMet = false;
        if (node.Inspiration is null)
        {
            inspirationText = strings.Get("ui.tree.inspiration.none");
        }
        else
        {
            inspirationMet = evaluate(node.Inspiration);
            inspirationText = strings.Format(
                inspirationMet ? "ui.tree.inspiration.met" : "ui.tree.inspiration.unmet",
                ("condition", conditionText(node.Inspiration)));
        }

        return new NodeRow<TId>(
            node.Id,
            node.Name,
            group,
            queued ? index : -1,
            node.EraName,
            NumberFormat.Units(node.Cost),
            NumberFormat.Units(node.FullCost),
            node.Cost.Milli < node.FullCost.Milli,
            strings.Format(
                "ui.tree.row.progress",
                ("done", NumberFormat.Units(node.Progress)),
                ("cost", NumberFormat.Units(node.Cost))),
            ProgressMath.Fraction(node.Progress, node.Cost),
            days is { } eta ? strings.Format("ui.tree.row.eta", ("days", NumberFormat.Count(eta))) : strings.Get("ui.tree.row.eta.none"),
            inspirationText,
            inspirationMet,
            inspired,
            missing);
    }
}
