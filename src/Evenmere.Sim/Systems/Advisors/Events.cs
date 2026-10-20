using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Advisors;

/// <summary>An advisor raised a new note.</summary>
/// <param name="Tick">The tick during which the note was raised.</param>
/// <param name="Advice">A copy of the note.</param>
public sealed record AdviceRaised(long Tick, AdviceView Advice) : SimEvent(Tick);

/// <summary>An advisor note was cleared because its problem went away (or it was dismissed).</summary>
/// <param name="Tick">The tick during which the note was cleared.</param>
/// <param name="AdviceId">The note's ID.</param>
/// <param name="Kind">What the problem was.</param>
public sealed record AdviceCleared(long Tick, int AdviceId, AdviceKind Kind) : SimEvent(Tick);
