using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Happenings;

/// <summary>A happening became eligible and is waiting for a choice: at most one is pending at a time. (M2-12.)</summary>
/// <param name="Tick">The tick during which the happening was raised.</param>
/// <param name="Happening">The happening waiting for a choice.</param>
public sealed record HappeningPending(long Tick, HappeningId Happening) : SimEvent(Tick);

/// <summary>A pending happening was answered (or timed out into its default option) and its effects applied. (M2-12.)</summary>
/// <param name="Tick">The tick during which the happening was resolved.</param>
/// <param name="Happening">The happening that was resolved.</param>
/// <param name="OptionIndex">The option that was applied: the chosen one, or the default on a timeout.</param>
public sealed record HappeningResolved(long Tick, HappeningId Happening, int OptionIndex) : SimEvent(Tick);
