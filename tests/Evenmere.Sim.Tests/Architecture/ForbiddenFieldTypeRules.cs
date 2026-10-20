using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Evenmere.Sim.Tests.Architecture;

/// <summary>
/// The determinism rules for sim state (ADR-0002, ADR-0003): nothing reachable from the world state may be written with
/// a floating-point or decimal number, a hash-ordered collection, or a type that hides what it holds.
/// </summary>
/// <remarks>
/// <para>Forbidden anywhere a field's type mentions them, including inside arrays and generic arguments:</para>
/// <list type="bullet">
/// <item><see cref="float"/>, <see cref="double"/> and <see cref="decimal"/>: results can differ between CPUs and
/// runtimes. Use <c>Amount</c> (milli-units) and <c>Ratio</c> (per-mille).</item>
/// <item><c>Dictionary&lt;,&gt;</c>, <c>HashSet&lt;&gt;</c>, <c>ConcurrentDictionary&lt;,&gt;</c>, and any other type that is or
/// implements <c>IDictionary&lt;,&gt;</c>, <c>IReadOnlyDictionary&lt;,&gt;</c>, <c>ISet&lt;&gt;</c>,
/// <c>IReadOnlySet&lt;&gt;</c> or the non-generic <see cref="IDictionary"/>, because their iteration order is not part of
/// their contract. The two ordered exceptions are <c>SortedDictionary&lt;,&gt;</c> and <c>SortedSet&lt;&gt;</c>. The read-only
/// interfaces are included because a field typed as one can hold a <c>Dictionary</c>.</item>
/// <item><see cref="object"/> and <c>dynamic</c> (which the compiler stores as <see cref="object"/>): whatever sits behind
/// them is invisible to the walker, so the other rules could not check it.</item>
/// </list>
/// </remarks>
internal static class ForbiddenFieldTypeRules
{
    private static readonly HashSet<Type> _orderedExceptions = [typeof(SortedDictionary<,>), typeof(SortedSet<>)];

    private static readonly HashSet<Type> _namedHashTypes =
        [typeof(Dictionary<,>), typeof(HashSet<>), typeof(ConcurrentDictionary<,>)];

    private static readonly HashSet<Type> _hashInterfaces =
        [typeof(IDictionary<,>), typeof(IReadOnlyDictionary<,>), typeof(ISet<>), typeof(IReadOnlySet<>)];

    /// <summary>Walks a root type and checks everything reachable from it.</summary>
    /// <param name="root">The state root, such as <c>WorldState</c>.</param>
    /// <returns>One line per offending use, each starting with the path to it; empty when the state is clean.</returns>
    public static IReadOnlyList<string> Check(Type root) => Check(StateTypeWalker.Walk(root));

    /// <summary>Checks every use of a type in a walked graph.</summary>
    /// <param name="graph">The walked graph.</param>
    /// <returns>One line per offending use, each starting with the path to it; empty when the state is clean.</returns>
    public static IReadOnlyList<string> Check(StateTypeGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var violations = new List<string>();
        foreach (StateTypeUse use in graph.Uses)
        {
            foreach (Type type in use.Types)
            {
                string? reason = ReasonToForbid(type, use.Field);
                if (reason is null)
                {
                    continue;
                }

                string inside = type == use.DeclaredType ? string.Empty : $" (written inside {TypeNames.Display(use.DeclaredType)})";
                violations.Add($"{use.Text}: {TypeNames.Display(type)}{inside} is forbidden: {reason}");
            }
        }

        return violations;
    }

    /// <summary>Explains why a type may not appear in sim state.</summary>
    /// <param name="type">The type to judge.</param>
    /// <param name="field">The field that declares it, if any; used only to recognise <c>dynamic</c>.</param>
    /// <returns>The reason, or <see langword="null"/> if the type is allowed.</returns>
    public static string? ReasonToForbid(Type type, FieldInfo? field = null)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return "floating-point and decimal numbers are banned in sim state (ADR-0003); use Amount (milli-units) or Ratio (per-mille)";
        }

        if (type == typeof(object))
        {
            string what = field?.IsDefined(typeof(DynamicAttribute), inherit: false) == true ? "dynamic" : "object";
            return $"{what} hides the real type from the architecture checks; declare the concrete type";
        }

        return IsHashOrdered(type)
            ? "hash-ordered collections have no stable iteration order (ADR-0003); use IdTable<T>, an array, SortedDictionary or SortedSet"
            : null;
    }

    private static bool IsHashOrdered(Type type)
    {
        Type? definition = type.IsGenericType ? type.GetGenericTypeDefinition() : null;
        if (definition is not null && _orderedExceptions.Contains(definition))
        {
            return false;
        }

        if (definition is not null && _namedHashTypes.Contains(definition))
        {
            return true;
        }

        return IsHashInterface(type) || type.GetInterfaces().Any(IsHashInterface);
    }

    private static bool IsHashInterface(Type type) =>
        type == typeof(IDictionary) || (type.IsGenericType && _hashInterfaces.Contains(type.GetGenericTypeDefinition()));
}
