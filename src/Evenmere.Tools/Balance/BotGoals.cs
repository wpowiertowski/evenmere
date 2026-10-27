using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Tools.Balance;

/// <summary>
/// The goal mechanics shared by the v1 slice bot (<see cref="BalanceBot"/>) and the Era I island bot
/// (<see cref="EraOne.EraOneBot"/>): per-goal tracking state, pending placements and goal completion.
/// The two bots run different reactive rules on top, but a goal means the same thing in both, so the
/// state and the result recording live here exactly once. Everything here is a plain data holder or a
/// pure record step; the runners own the simulation.
/// </summary>
internal static class BotGoals
{
    /// <summary>Records a finished goal on the shared result list.</summary>
    /// <param name="results">The results in script order.</param>
    /// <param name="nowTick">The current tick.</param>
    /// <param name="goal">The goal that finished.</param>
    /// <param name="index">The goal's index in the script.</param>
    /// <param name="state">The goal's tracking state.</param>
    /// <param name="status">How it ended.</param>
    /// <param name="detail">What happened, in words.</param>
    public static void Finish(
        List<BalanceGoalResult> results,
        long nowTick,
        BotGoal goal,
        int index,
        BotGoalState state,
        BalanceGoalStatus status,
        string detail)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(state);
        state.Status = status;
        state.Detail = detail;
        long start = state.StartTick < 0 ? nowTick : state.StartTick;
        results.Add(new BalanceGoalResult(index, KindOf(goal), detail, status, nowTick - start));
    }

    /// <summary>Names a goal's kind for the report.</summary>
    /// <param name="goal">The goal.</param>
    /// <returns>The kind word used in reports.</returns>
    public static string KindOf(BotGoal goal) => goal switch
    {
        BuildGoal => "build",
        ResearchGoal => "research",
        CivicGoal => "civic",
        EnsureSupplyGoal => "ensureSupply",
        HousesUntilGoal => "housesUntil",
        LockHousesGoal => "lockHouses",
        UnlockHousesGoal => "unlockHouses",
        WaitUntilTickGoal => "wait",
        _ => "unknown",
    };
}

/// <summary>Tracks one script goal: when it started, when to retry, and what it has sent.</summary>
internal sealed class BotGoalState
{
    /// <summary>Gets or sets the tick the goal started, or -1 before it starts.</summary>
    public long StartTick = -1;

    /// <summary>Gets or sets the tick before which the goal does not retry.</summary>
    public long RetryTick;

    /// <summary>Gets or sets the progress counter for multi-step goals.</summary>
    public int Progress;

    /// <summary>Gets or sets the placement waiting to resolve, if any.</summary>
    public BotPendingPlacement? Pending;

    /// <summary>Gets or sets the research or civic target sent but not yet acknowledged.</summary>
    public Command? SentTarget;

    /// <summary>Gets or sets the research or civic queue sent but not yet acknowledged.</summary>
    public Command? SentQueue;

    /// <summary>Gets or sets how the goal ended, or null while it runs.</summary>
    public BalanceGoalStatus? Status;

    /// <summary>Gets or sets what the goal is waiting on, in words.</summary>
    public string? Detail;
}

/// <summary>A building the bot placed whose completion it still tracks.</summary>
internal sealed class BotPendingPlacement
{
    /// <summary>Creates the placement.</summary>
    /// <param name="type">The building type.</param>
    /// <param name="typeName">The building type name, for notes.</param>
    /// <param name="x">The tile X.</param>
    /// <param name="y">The tile Y.</param>
    /// <param name="rotation">The rotation.</param>
    /// <param name="sentBuilding">The sent place command.</param>
    /// <param name="seekTick">The tick the commands were sent.</param>
    public BotPendingPlacement(
        BuildingTypeId type, string typeName, int x, int y,
        Rotation rotation, Command sentBuilding, long seekTick)
    {
        Type = type;
        TypeName = typeName;
        X = x;
        Y = y;
        Rotation = rotation;
        SentBuilding = sentBuilding;
        SeekTick = seekTick;
    }

    /// <summary>Gets the building type.</summary>
    public BuildingTypeId Type { get; }

    /// <summary>Gets the building type name, for notes.</summary>
    public string TypeName { get; }

    /// <summary>Gets the tile X.</summary>
    public int X { get; }

    /// <summary>Gets the tile Y.</summary>
    public int Y { get; }

    /// <summary>Gets the rotation.</summary>
    public Rotation Rotation { get; }

    /// <summary>Gets the sent place command.</summary>
    public Command SentBuilding { get; }

    /// <summary>Gets the tick the commands were sent.</summary>
    public long SeekTick { get; }

    /// <summary>Gets the house a coverage repair is for, or <see cref="EntityId.None"/>.</summary>
    public EntityId CoverHouseId { get; init; } = EntityId.None;

    /// <summary>Gets the service a coverage repair restores, if any.</summary>
    public ServiceKind? CoverService { get; init; }

    /// <summary>Gets or sets the placed building once it appears.</summary>
    public EntityId PlacedId { get; set; } = EntityId.None;

    /// <summary>Gets or sets whether the coverage check ran.</summary>
    public bool CoverageChecked { get; set; }

    /// <summary>Gets or sets whether the placement resolved and needs no more work.</summary>
    public bool Dead { get; set; }
}
