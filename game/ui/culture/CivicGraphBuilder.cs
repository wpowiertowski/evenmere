using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Evenmere.Game.Ui.Research;

namespace Evenmere.Game.Ui.Culture;

/// <summary>
/// Builds the civics tree cards the <see cref="CivicsTreeView"/> shows: one per visible civic in content order,
/// plus the layout inputs in the same order. States, costs, inspiration text and unlocks come from the snapshot
/// rows, so the graph agrees with the panel; prerequisites address cards by their position in the returned lists.
/// It mirrors the research graph builder, with one extra unlock line for granted tradition slots. It is pure
/// C# with no Godot types, so the game test project exercises it on its own.
/// </summary>
public static class CivicGraphBuilder
{
    /// <summary>
    /// Builds the cards and their layout inputs from the panel snapshot.
    /// </summary>
    /// <param name="content">The content database.</param>
    /// <param name="strings">The display text.</param>
    /// <param name="snapshot">The civics snapshot, in display order.</param>
    /// <param name="iconFor">The placeholder glyph for a content building ID, or <see langword="null"/> when it has none.</param>
    /// <param name="graphIds">Receives the civic behind each card, in card order.</param>
    /// <returns>The cards and their layout inputs.</returns>
    public static TreeGraph Build(
        ContentDb content,
        UiStrings strings,
        TreeSnapshot<CivicId> snapshot,
        Func<string, string?> iconFor,
        out List<CivicId> graphIds)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(iconFor);

        var rowById = new Dictionary<CivicId, NodeRow<CivicId>>(snapshot.Rows.Count);
        foreach (NodeRow<CivicId> row in snapshot.Rows)
        {
            rowById[row.Id] = row;
        }

        var ids = new List<CivicId>(snapshot.Rows.Count);
        for (int i = 0; i < content.Civics.Count; i++)
        {
            var id = CivicId.From(checked((ushort)i));
            if (rowById.ContainsKey(id))
            {
                ids.Add(id);
            }
        }

        var indexById = new Dictionary<CivicId, int>(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            indexById[ids[i]] = i;
        }

        var nodes = new List<GraphNode>(ids.Count);
        var layout = new List<LayoutNode>(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            NodeRow<CivicId> row = rowById[ids[i]];
            CivicDef def = content.Get(row.Id);

            var requires = new List<int>(def.Requires.Length);
            foreach (CivicId require in def.Requires)
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
                StateText(strings, row),
                $"{row.ProgressText} \u00b7 {row.EtaText}",
                row.Fraction,
                row.InspirationText,
                row.Inspired,
                row.Inspired ? strings.Get("ui.tree.row.inspired") : string.Empty,
                row.MissingText,
                DescribeEffects(content, strings, def.Effects, iconFor),
                TooltipOf(strings, row)));
            layout.Add(new LayoutNode(def.Era, row.EraName, requires, def.Gateway));
        }

        graphIds = ids;
        return new TreeGraph(nodes, layout);
    }

    /// <summary>
    /// Turns a civic's (or tradition's) effects into the unlock lines a card shows, in content order.
    /// Shared by the civics tree and the tradition cards, so both read the same words.
    /// </summary>
    /// <param name="content">The content database.</param>
    /// <param name="strings">The display text.</param>
    /// <param name="effects">The effects to describe.</param>
    /// <param name="iconFor">The placeholder glyph for a content building ID, or <see langword="null"/> when it has none.</param>
    /// <returns>The unlock lines.</returns>
    public static IReadOnlyList<GraphUnlock> DescribeEffects(
        ContentDb content,
        UiStrings strings,
        System.Collections.Immutable.ImmutableArray<Effect> effects,
        Func<string, string?> iconFor) =>
        EffectText.Describe(content, strings, effects, iconFor, EffectText.StatName);

    /// <summary>
    /// Humanizes a modifier stat's JSON name for display: the content spelling (<c>output.culture</c>,
    /// <c>constructionSpeed</c>) reads as calm words (<c>culture output</c>, <c>construction speed</c>).
    /// Output stats name what they make first, so the good comes first.
    /// </summary>
    /// <param name="stat">The stat.</param>
    /// <returns>The display name, all lowercase to sit inside effect lines.</returns>
    public static string StatName(Stat stat) => EffectText.StatName(stat);

    /// <summary>
    /// Names a tradition slot type from the locale table (<c>ui.traditions.slot.*</c>).
    /// </summary>
    /// <param name="strings">The display text.</param>
    /// <param name="slot">The slot type.</param>
    /// <returns>The display name.</returns>
    public static string SlotName(UiStrings strings, TraditionSlotType slot) => EffectText.SlotName(strings, slot);

    private static string StateText(UiStrings strings, NodeRow<CivicId> row) => row.Group switch
    {
        ProgressGroup.Target => strings.Get("ui.tree.group.target"),
        ProgressGroup.Queue => strings.Format(
            "ui.tree.row.queue_position",
            ("position", NumberFormat.Count(row.QueueIndex + 1))),
        ProgressGroup.Researched => strings.Get("ui.culture.group.adopted"),
        ProgressGroup.Available => strings.Get("ui.tree.group.available"),
        _ => row.MissingText.Length > 0 ? row.MissingText : strings.Get("ui.tree.group.locked"),
    };

    private static string TooltipOf(UiStrings strings, NodeRow<CivicId> row)
    {
        string tip = strings.Get(row.Group == ProgressGroup.Queue ? "ui.tree.row.tip.queued" : "ui.tree.row.tip");
        return row.MissingText.Length > 0 ? row.MissingText + "\n" + tip : tip;
    }
}
