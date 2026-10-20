using System.Reflection;
using System.Runtime.CompilerServices;

namespace Evenmere.Sim.Tests.Architecture;

/// <summary>
/// Every command the simulation declares has exactly one handler. A command without one is rejected as unknown at run
/// time, so a forgotten registration would only show up when a player first tried the feature; a second handler for
/// the same type is refused by <c>CommandRouter.Register</c>, so the "exactly one" half is guarded twice.
/// </summary>
internal static class CommandHandlerRules
{
    /// <summary>
    /// Finds the commands an assembly declares: every concrete, closed subtype of <see cref="Command"/>, nested or not,
    /// public or internal. Abstract bases and compiler-generated types are not commands anyone can send.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The command types, in ordinal order of their full names.</returns>
    public static IReadOnlyList<Type> FindCommandTypes(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return [.. assembly.GetTypes()
            .Where(static t => t.IsSubclassOf(typeof(Command))
                && !t.IsAbstract
                && !t.ContainsGenericParameters
                && !t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .OrderBy(static t => t.FullName, StringComparer.Ordinal)];
    }

    /// <summary>Checks that each declared command appears exactly once among the registered command types.</summary>
    /// <param name="declared">The commands that must have a handler.</param>
    /// <param name="registered">
    /// The command types the router has handlers for, one entry per handler (<c>CommandRouter.RegisteredCommandTypes</c>).
    /// </param>
    /// <returns>One line per command with no handler or with several; empty when every command has exactly one.</returns>
    public static IReadOnlyList<string> Check(IEnumerable<Type> declared, IEnumerable<Type> registered)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(registered);

        var handlers = registered.GroupBy(static t => t).ToDictionary(static g => g.Key, static g => g.Count());
        var violations = new List<string>();
        foreach (Type command in declared)
        {
            int count = handlers.GetValueOrDefault(command);
            if (count != 1)
            {
                violations.Add(count == 0
                    ? $"{TypeNames.Display(command)} has no handler registered by the pipeline"
                    : $"{TypeNames.Display(command)} has {count} handlers registered; exactly one is allowed");
            }
        }

        return violations;
    }
}
