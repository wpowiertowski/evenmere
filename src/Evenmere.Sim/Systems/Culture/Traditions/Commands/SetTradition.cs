using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Culture.Traditions.Commands;

/// <summary>
/// Slots a tradition card, or clears the slot when <see cref="Tradition"/> is <see langword="null"/>. Slotting is free
/// and allowed at any time. Handled by M2-07.
/// </summary>
/// <remarks>
/// Refused with <see cref="RejectReason.TraditionNotUnlocked"/> when the card is locked or unknown,
/// <see cref="RejectReason.SlotMismatch"/> when the card does not fit the slot or the slot does not exist,
/// <see cref="RejectReason.AlreadySlotted"/> when the card sits in another slot, and
/// <see cref="RejectReason.StyleAlreadySlotted"/> when a different style card is slotted.
/// </remarks>
/// <param name="Slot">The slot type.</param>
/// <param name="Index">Which slot of that type, from 0 below the granted count.</param>
/// <param name="Tradition">The card to slot, or <see langword="null"/> to clear the slot.</param>
public sealed record SetTradition(TraditionSlotType Slot, int Index, TraditionId? Tradition) : Command;
