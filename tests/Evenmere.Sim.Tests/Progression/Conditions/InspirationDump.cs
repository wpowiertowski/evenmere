using Evenmere.Core.Ids;
using Evenmere.Sim;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;

namespace Evenmere.Sim.Tests.Progression.Conditions;

/// <summary>
/// A test-only command that dumps the evaluation status of every tech and civic inspiration into lines, for agents
/// debugging pacing and inspiration coverage. It lives in the tests, not the sim contract, so it can never ship.
/// </summary>
internal sealed record DumpInspirationStatus : Command;

/// <summary>Handles <see cref="DumpInspirationStatus"/> by evaluating every node's inspiration against the live queries.</summary>
/// <param name="lines">Receives one line per node with an inspiration.</param>
internal sealed class DumpInspirationStatusHandler(List<string> lines) : ICommandHandler<DumpInspirationStatus>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, DumpInspirationStatus command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);
        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, DumpInspirationStatus command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        // Techs then civics, each in ascending ID order, so the dump is deterministic.
        for (int i = 0; i < ctx.Content.Techs.Count; i++)
        {
            var tech = TechId.From(checked((ushort)i));
            if (ctx.Content.Get(tech).Inspiration is { } when)
            {
                lines.Add(FormattableString.Invariant(
                    $"tech {ctx.Content.NameOf(tech)}: holds={YesNo(ctx.Queries.Progression.Evaluate(when))} inspired={YesNo(ctx.Queries.Research.IsInspired(tech))}"));
            }
        }

        for (int i = 0; i < ctx.Content.Civics.Count; i++)
        {
            var civic = CivicId.From(checked((ushort)i));
            if (ctx.Content.Get(civic).Inspiration is { } when)
            {
                lines.Add(FormattableString.Invariant(
                    $"civic {ctx.Content.NameOf(civic)}: holds={YesNo(ctx.Queries.Progression.Evaluate(when))} inspired={YesNo(ctx.Queries.Culture.IsInspired(civic))}"));
            }
        }
    }

    private static string YesNo(bool value) => value ? "yes" : "no";
}
