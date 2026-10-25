using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using MessagePack;

namespace Evenmere.Sim.Systems.Culture.Traditions;

/// <summary>
/// One slotted tradition card, as the save holds it: the slot it sits in and the card. The culture state's
/// <c>Slotted</c> array keeps these sorted by (slot type, index), so two runs that slot the same cards save the same
/// bytes whatever order the commands arrived in.
/// </summary>
/// <param name="Slot">The slot type the card sits in.</param>
/// <param name="Index">Which slot of that type, from 0 below the granted count.</param>
/// <param name="Tradition">The slotted card.</param>
[MessagePackObject]
public readonly record struct SlottedTradition(
    [property: Key(0)] TraditionSlotType Slot,
    [property: Key(1)] int Index,
    [property: Key(2)] TraditionId Tradition)
{
    /// <summary>Orders slotted cards by (slot type, index), which is the order the state keeps them in.</summary>
    /// <param name="left">The first card.</param>
    /// <param name="right">The second card.</param>
    /// <returns>Negative, zero or positive as <paramref name="left"/> sorts before, with or after <paramref name="right"/>.</returns>
    public static int Compare(SlottedTradition left, SlottedTradition right)
    {
        int bySlot = ((int)left.Slot).CompareTo((int)right.Slot);
        return bySlot != 0 ? bySlot : left.Index.CompareTo(right.Index);
    }
}

/// <summary>One granted tradition slot with the card it holds, for the view: the slot and the card, if one is slotted.</summary>
/// <param name="Slot">The slot type.</param>
/// <param name="Index">Which slot of that type, from 0 below the granted count.</param>
/// <param name="Tradition">The slotted card, or <see langword="null"/> when the slot is empty.</param>
public readonly record struct TraditionSlotView(TraditionSlotType Slot, int Index, TraditionId? Tradition);

/// <summary>The pure rules behind tradition slots: which cards fit where, and how the sorted slotted array changes.</summary>
internal static class TraditionSlots
{
    /// <summary>Gets the building tag that marks a tradition card as an architectural style: only one may be slotted at a time.</summary>
    internal const string StyleTag = "style";

    /// <summary>Tests whether a card fits a slot: its own type, or any card in a Wild slot. Wild-only cards fit nothing else.</summary>
    /// <param name="card">The slot type the card names.</param>
    /// <param name="slot">The slot it would sit in.</param>
    /// <returns><see langword="true"/> if the card fits.</returns>
    internal static bool FitsSlot(TraditionSlotType card, TraditionSlotType slot) => card == slot || slot == TraditionSlotType.Wild;

    /// <summary>Finds the card slotted in one slot, if one is slotted there.</summary>
    /// <param name="slotted">The slotted cards, in ascending (slot type, index) order.</param>
    /// <param name="slot">The slot type.</param>
    /// <param name="index">Which slot of that type.</param>
    /// <returns>The card, or <see langword="null"/> when the slot is empty.</returns>
    internal static TraditionId? Find(SlottedTradition[] slotted, TraditionSlotType slot, int index)
    {
        foreach (SlottedTradition entry in slotted)
        {
            if (entry.Slot == slot && entry.Index == index)
            {
                return entry.Tradition;
            }
        }

        return null;
    }

    /// <summary>Tests whether a card is slotted in any slot other than one slot.</summary>
    /// <param name="slotted">The slotted cards.</param>
    /// <param name="tradition">The card.</param>
    /// <param name="slot">The slot it would sit in.</param>
    /// <param name="index">Which slot of that type.</param>
    /// <returns><see langword="true"/> if the card sits in a different slot.</returns>
    internal static bool IsSlottedElsewhere(SlottedTradition[] slotted, TraditionId tradition, TraditionSlotType slot, int index)
    {
        foreach (SlottedTradition entry in slotted)
        {
            if (entry.Tradition == tradition && (entry.Slot != slot || entry.Index != index))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Sets one slot to a card, keeping the array sorted by (slot type, index). Setting the slot to the card it
    /// already holds changes nothing.</summary>
    /// <param name="slotted">The slotted cards, in ascending (slot type, index) order.</param>
    /// <param name="slot">The slot type.</param>
    /// <param name="index">Which slot of that type.</param>
    /// <param name="tradition">The card.</param>
    /// <returns>The new slotted array.</returns>
    internal static SlottedTradition[] WithSet(SlottedTradition[] slotted, TraditionSlotType slot, int index, TraditionId tradition)
    {
        SlottedTradition[] cleared = WithRemoved(slotted, slot, index);
        var grown = new SlottedTradition[cleared.Length + 1];
        var entry = new SlottedTradition(slot, index, tradition);
        int at = 0;
        while (at < cleared.Length && SlottedTradition.Compare(cleared[at], entry) < 0)
        {
            at++;
        }

        Array.Copy(cleared, grown, at);
        grown[at] = entry;
        Array.Copy(cleared, at, grown, at + 1, cleared.Length - at);
        return grown;
    }

    /// <summary>Clears one slot, keeping the array sorted. Clearing an empty slot changes nothing.</summary>
    /// <param name="slotted">The slotted cards, in ascending (slot type, index) order.</param>
    /// <param name="slot">The slot type.</param>
    /// <param name="index">Which slot of that type.</param>
    /// <returns>The new slotted array.</returns>
    internal static SlottedTradition[] WithRemoved(SlottedTradition[] slotted, TraditionSlotType slot, int index)
    {
        int at = 0;
        while (at < slotted.Length && (slotted[at].Slot != slot || slotted[at].Index != index))
        {
            at++;
        }

        if (at == slotted.Length)
        {
            return slotted;
        }

        var shrunk = new SlottedTradition[slotted.Length - 1];
        Array.Copy(slotted, shrunk, at);
        Array.Copy(slotted, at + 1, shrunk, at, shrunk.Length - at);
        return shrunk;
    }
}
