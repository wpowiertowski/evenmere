using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;

namespace Evenmere.Sim.Tests.Scenarios;

// Test-only commands that prove the scenario command path before the real commands exist (M1). They live in the test
// assembly, so no scenario can reach them from the game, and the harness registers a handler only for the ones a
// scenario names. Each command's handler emits an event, so a scenario can assert on it and a unit test can read it.

/// <summary>Does nothing but report the tick it was applied on, so tests can check when a scripted command runs.</summary>
/// <param name="Label">A free-text tag copied into the event.</param>
[ScenarioCommand(typeof(RecordTickHandler))]
internal sealed record RecordTick(string Label) : Command;

/// <summary>Emitted by <see cref="RecordTickHandler"/> during the tick the command was applied on.</summary>
/// <param name="Tick">The tick the command was applied on, which is the tick written in the scenario file.</param>
/// <param name="Label">The command's label.</param>
internal sealed record TickRecorded(long Tick, string Label) : SimEvent(Tick);

/// <summary>Handles <see cref="RecordTick"/>.</summary>
internal sealed class RecordTickHandler : ICommandHandler<RecordTick>
{
    public CommandResult Validate(SimContext ctx, RecordTick command) => CommandResult.Ok;

    public void Apply(SimContext ctx, RecordTick command) => ctx.Events.Emit(new TickRecorded(ctx.Tick, command.Label));
}

/// <summary>Adds goods straight to the island storage, so a scenario can set up stock without any production chain.</summary>
/// <param name="Good">The good.</param>
/// <param name="Amount">How much to add; a negative amount is rejected.</param>
[ScenarioCommand(typeof(GrantGoodsHandler))]
internal sealed record GrantGoods(GoodId Good, Amount Amount) : Command;

/// <summary>Emitted by <see cref="GrantGoodsHandler"/> when the goods were added.</summary>
/// <param name="Tick">The tick the goods were added on.</param>
/// <param name="Good">The good.</param>
/// <param name="Amount">How much was added.</param>
internal sealed record GoodsGranted(long Tick, GoodId Good, Amount Amount) : SimEvent(Tick);

/// <summary>Handles <see cref="GrantGoods"/>.</summary>
internal sealed class GrantGoodsHandler : ICommandHandler<GrantGoods>
{
    public CommandResult Validate(SimContext ctx, GrantGoods command) =>
        command.Amount < Amount.Zero ? CommandResult.Invalid("A grant must not be negative.") : CommandResult.Ok;

    public void Apply(SimContext ctx, GrantGoods command)
    {
        ctx.Storage.Add(command.Good, command.Amount);
        ctx.Events.Emit(new GoodsGranted(ctx.Tick, command.Good, command.Amount));
    }
}

/// <summary>
/// Credits knowledge, culture or coin straight to the points ledger, so a scenario on content without a map (and so
/// without any point income) can still complete techs and civics. Mirrors <see cref=\"GrantGoods\"/>.
/// </summary>
/// <param name=\"Kind\">The kind of point to credit.</param>
/// <param name=\"Amount\">How much to add; a negative amount is rejected.</param>
[ScenarioCommand(typeof(GrantPointsHandler))]
internal sealed record GrantPoints(PointKind Kind, Amount Amount) : Command;

/// <summary>Emitted by <see cref=\"GrantPointsHandler\"/> when the points were credited.</summary>
/// <param name=\"Tick\">The tick the points were credited on.</param>
/// <param name=\"Kind\">The kind of point.</param>
/// <param name=\"Amount\">How much was added.</param>
internal sealed record PointsGranted(long Tick, PointKind Kind, Amount Amount) : SimEvent(Tick);

/// <summary>Handles <see cref=\"GrantPoints\"/>.</summary>
internal sealed class GrantPointsHandler : ICommandHandler<GrantPoints>
{
    public CommandResult Validate(SimContext ctx, GrantPoints command) =>
        command.Amount < Amount.Zero ? CommandResult.Invalid("A grant must not be negative.") : CommandResult.Ok;

    public void Apply(SimContext ctx, GrantPoints command)
    {
        ctx.PointsLedger.Credit(command.Kind, command.Amount);
        ctx.Events.Emit(new PointsGranted(ctx.Tick, command.Kind, command.Amount));
    }
}

/// <summary>A command nothing serves, so the simulation rejects it as unknown; for checking that rejections reach a scenario's events.</summary>
[ScenarioCommand]
internal sealed record UnservedCommand : Command;
