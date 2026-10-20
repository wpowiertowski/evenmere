using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Queries.Types;

/// <summary>What currently stops a house from advancing to the next tier (progression.md, How needs work). It is the first blocker found, in the order listed.</summary>
public enum AdvanceBlocker : byte
{
    /// <summary>Nothing blocks the house: it advances once its needs have been met long enough.</summary>
    None = 0,

    /// <summary>Not every basic need is met, so the advance streak cannot grow.</summary>
    NeedsUnmet = 1,

    /// <summary>The next tier exists but has not been unlocked by research or culture.</summary>
    NextTierLocked = 2,

    /// <summary>The house is at the highest tier the content defines.</summary>
    NoNextTier = 3,

    /// <summary>The player has locked the house against upgrades.</summary>
    UpgradeLocked = 4,

    /// <summary>The island pool lacks the upgrade materials.</summary>
    MissingMaterials = 5,
}

/// <summary>A copy of one house's state for the view, the explain queries and the advisors.</summary>
/// <param name="Id">The house's entity ID, the same as its building's.</param>
/// <param name="Tier">The tier the house is at.</param>
/// <param name="Residents">The residents living in it now.</param>
/// <param name="TargetResidents">The occupancy the house is moving towards: <c>max(1, capacity × basic needs met ÷ basic needs total)</c>. Residents move one step per day.</param>
/// <param name="Capacity">The most residents it can hold now: the tier's capacity scaled by the <c>capacity</c> modifiers.</param>
/// <param name="AdvanceStreakDays">The consecutive whole days all basic needs have been met, counted up to the advance threshold.</param>
/// <param name="DeclineTimerDays">The whole days a basic need that was once met has been below 50%; 0 when the timer is not running.</param>
/// <param name="UpgradeLocked">Whether the player has locked the house against advancing.</param>
/// <param name="DeclinePaused">Whether a festival is pausing the decline timer (always false in M1).</param>
/// <param name="AdvanceBlocker">What currently stops the house from advancing.</param>
public readonly record struct HouseInfo(
    EntityId Id,
    TierId Tier,
    int Residents,
    int TargetResidents,
    int Capacity,
    int AdvanceStreakDays,
    int DeclineTimerDays,
    bool UpgradeLocked,
    bool DeclinePaused,
    AdvanceBlocker AdvanceBlocker);

/// <summary>Whether a need decides a house's growth (basic) or only raises the tier's output (comfort).</summary>
public enum NeedClass : byte
{
    /// <summary>A basic need: it drives occupancy, advancing and decline.</summary>
    Basic = 0,

    /// <summary>A comfort need: each one met adds to the tier's knowledge, culture and coin output and never causes decline.</summary>
    Comfort = 1,
}

/// <summary>Why a need is not met.</summary>
public enum NeedUnmetReason : byte
{
    /// <summary>The need is met.</summary>
    None = 0,

    /// <summary>The house is outside every market's coverage, so it cannot draw any goods.</summary>
    NoMarketCoverage = 1,

    /// <summary>The service building is not within road range of the house.</summary>
    ServiceNotCovered = 2,

    /// <summary>The good's island-wide fulfilment is below 50%.</summary>
    LowFulfilment = 3,
}

/// <summary>One need of a house and whether it is met. Exactly one of <paramref name="Good"/> and <paramref name="Service"/> is set.</summary>
/// <param name="Class">Whether the need is basic or comfort.</param>
/// <param name="Good">The good, for a goods need; otherwise <see langword="null"/>.</param>
/// <param name="Service">The service, for a service need; otherwise <see langword="null"/>.</param>
/// <param name="Met">Whether the need is met today.</param>
/// <param name="EverMet">Whether the need has been met at the house's current tier; only such needs can start the decline timer.</param>
/// <param name="Fulfilment">For a goods need, today's island-wide fulfilment ratio of the good; for a service need, 1000 per-mille if covered and 0 if not.</param>
/// <param name="Reason">Why the need is unmet; <see cref="NeedUnmetReason.None"/> when it is met.</param>
public readonly record struct NeedStatus(
    NeedClass Class,
    GoodId? Good,
    ServiceKind? Service,
    bool Met,
    bool EverMet,
    Ratio Fulfilment,
    NeedUnmetReason Reason);

/// <summary>The needs of one house, for the inspector and the explain queries.</summary>
/// <param name="House">The house's entity ID.</param>
/// <param name="HasMarketCoverage">Whether a market covers the house; without it every goods need is unmet.</param>
/// <param name="Needs">The house's needs, basic ones first and then comfort ones, each group in the order of the tier's definition.</param>
public sealed record HouseNeedsInfo(EntityId House, bool HasMarketCoverage, ImmutableArray<NeedStatus> Needs)
{
    /// <summary>Gets how many basic needs the tier has.</summary>
    public int BasicTotal => Count(NeedClass.Basic, metOnly: false);

    /// <summary>Gets how many basic needs are met today.</summary>
    public int BasicMet => Count(NeedClass.Basic, metOnly: true);

    /// <summary>Gets how many comfort needs are met today.</summary>
    public int ComfortMet => Count(NeedClass.Comfort, metOnly: true);

    /// <inheritdoc />
    public bool Equals(HouseNeedsInfo? other) =>
        other is not null && House == other.House && HasMarketCoverage == other.HasMarketCoverage && ValueArrays.Equal(Needs, other.Needs);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(House, HasMarketCoverage, ValueArrays.Hash(Needs));

    private int Count(NeedClass needClass, bool metOnly)
    {
        int count = 0;
        foreach (NeedStatus need in Needs.AsSpan())
        {
            if (need.Class == needClass && (!metOnly || need.Met))
            {
                count++;
            }
        }

        return count;
    }
}
