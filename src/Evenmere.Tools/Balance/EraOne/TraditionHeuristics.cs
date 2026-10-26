using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Culture.Traditions;
using Evenmere.Sim.Systems.Culture.Traditions.Commands;

namespace Evenmere.Tools.Balance.EraOne;

/// <summary>
/// Which tradition card the Era I bot slots, per slot type, in priority order. Slotting is free and reversible, so the
/// bot re-slots whenever the unlocked set or the granted slots change: knowledge and culture first (the gateway race),
/// then construction and production, then capacity and comfort. Only one card fits each Era I slot, so the priority only
/// matters when several cards for one slot are unlocked at once.
/// </summary>
public static class TraditionHeuristics
{
    /// <summary>Gets the tag that marks a tradition card as an architectural style: only one may be slotted at a time.</summary>
    public const string StyleTag = "style";

    private static readonly string[] _hearthOrder = ["fireside_tales", "common_hearth", "common_voice"];
    private static readonly string[] _craftOrder = ["many_hands", "village_crafts", "tide_blessing", "harvest_festival"];
    private static readonly string[] _heritageOrder = ["old_ways"];

    /// <summary>
    /// Plans the slotting commands that bring the island to the heuristic choice: one <see cref="SetTradition"/> per
    /// slot whose card should change, in ascending (slot type, index) order. Slots whose best card is already slotted
    /// produce no command. The plan is deterministic for one content and one unlocked set.
    /// </summary>
    /// <param name="content">The loaded content.</param>
    /// <param name="queries">The culture queries, read between ticks.</param>
    /// <returns>The commands to send, in slot order; empty when every slot already holds its best card.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static List<SetTradition> PlanSlottings(ContentDb content, ICultureQueries queries)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(queries);

        var unlocked = new HashSet<TraditionId>(queries.UnlockedTraditions());
        var slotted = new Dictionary<(TraditionSlotType Slot, int Index), TraditionId?>();
        foreach (TraditionSlotView view in queries.Slots())
        {
            slotted[(view.Slot, view.Index)] = view.Tradition;
        }

        var taken = new HashSet<TraditionId>();
        foreach ((_, TraditionId? card) in slotted)
        {
            if (card is not null)
            {
                taken.Add(card.Value);
            }
        }

        TraditionId? activeStyle = queries.ActiveStyle();
        var commands = new List<SetTradition>();
        foreach (TraditionSlotView view in queries.Slots())
        {
            // The slot's own card is not contention: keeping it needs no move, so it leaves the taken set first.
            if (view.Tradition is not null)
            {
                taken.Remove(view.Tradition.Value);
            }

            TraditionId? best = BestFor(content, view.Slot, unlocked, taken, activeStyle);
            if (best != view.Tradition)
            {
                commands.Add(new SetTradition(view.Slot, view.Index, best));
                if (view.Tradition is not null)
                {
                    taken.Remove(view.Tradition.Value);
                }

                if (best is not null)
                {
                    taken.Add(best.Value);
                }
            }
            else if (best is not null)
            {
                taken.Add(best.Value);
            }
        }

        return commands;
    }

    private static TraditionId? BestFor(
        ContentDb content,
        TraditionSlotType slot,
        HashSet<TraditionId> unlocked,
        HashSet<TraditionId> taken,
        TraditionId? activeStyle)
    {
        string[] order = slot switch
        {
            TraditionSlotType.Hearth => _hearthOrder,
            TraditionSlotType.Craft => _craftOrder,
            TraditionSlotType.Heritage => _heritageOrder,
            _ => [],
        };

        foreach (string name in order)
        {
            if (!content.Traditions.TryGet(name, out TraditionId id))
            {
                continue;
            }

            if (!unlocked.Contains(id) || taken.Contains(id))
            {
                continue;
            }

            TraditionDef def = content.Get(id);
            // A card fits its own slot type, or any card in a Wild slot; Wild-only cards fit nothing else.
            // This mirrors the sim's slot rule (TraditionSlots.FitsSlot), so the plan never proposes a refused slotting.
            if (def.Slot != slot && slot != TraditionSlotType.Wild)
            {
                continue;
            }

            // A style card cannot join while a different style is active; the active style itself stays.
            if (def.Tags.Contains(StyleTag) && activeStyle is not null && activeStyle != id)
            {
                continue;
            }

            return id;
        }

        return null;
    }
}
