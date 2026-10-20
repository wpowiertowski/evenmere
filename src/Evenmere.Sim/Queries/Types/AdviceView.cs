namespace Evenmere.Sim.Queries.Types;

/// <summary>The problems the advisors watch for (M1-15). The values are part of the save format; add members at the end.</summary>
public enum AdviceKind : byte
{
    /// <summary>A building or site has no road touching it. The subject is the building.</summary>
    NoRoadAccess = 0,

    /// <summary>A building or site is outside every storehouse's reach. The subject is the building.</summary>
    OutsideStorehouseReach = 1,

    /// <summary>A good's fulfilment has stayed below 80% for 2 days. The subject is the good.</summary>
    GoodShortage = 2,

    /// <summary>A house has all its needs met but cannot advance (materials missing, or the tier is locked). The subject is the house.</summary>
    HouseCannotAdvance = 3,

    /// <summary>A tier's workforce demand has exceeded its supply for 2 days. The subject is the tier.</summary>
    NoWorkforce = 4,

    /// <summary>Knowledge is accruing with no research target. There is no subject.</summary>
    ResearchIdle = 5,

    /// <summary>Culture is accruing with no civic target. There is no subject.</summary>
    CivicIdle = 6,
}

/// <summary>
/// A copy of one advisor note for the view. The note's text comes from the locale key for its <see cref="Kind"/>; the
/// numbers and names it mentions come from asking the explain queries about the <see cref="Subject"/> (a good goes to
/// <c>ExplainGood</c>, a building to <c>ExplainBuilding</c> or <c>ExplainHouse</c>).
/// </summary>
/// <param name="Id">The advice's stable ID, from a deterministic counter; it is never reused, and <c>DismissAdvice</c> takes it.</param>
/// <param name="Kind">What the problem is.</param>
/// <param name="Subject">What the problem is about; <see cref="SubjectLink.None"/> when the kind has no subject.</param>
/// <param name="FirstSeenTick">The tick the problem was first detected.</param>
/// <param name="LastSeenTick">The tick the problem was last confirmed.</param>
public readonly record struct AdviceView(
    int Id,
    AdviceKind Kind,
    SubjectLink Subject,
    long FirstSeenTick,
    long LastSeenTick);
