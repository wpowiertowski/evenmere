using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Culture.Traditions;

/// <summary>A tradition card was slotted or a slot was cleared, and its modifiers now apply (or no longer do).</summary>
/// <param name="Tick">The tick during which the slot changed.</param>
/// <param name="Slot">The slot type.</param>
/// <param name="Index">Which slot of that type.</param>
/// <param name="Old">The card the slot held before, or <see langword="null"/> when it was empty.</param>
/// <param name="New">The card the slot holds now, or <see langword="null"/> when it was cleared.</param>
public sealed record TraditionChanged(long Tick, TraditionSlotType Slot, int Index, TraditionId? Old, TraditionId? New) : SimEvent(Tick);
