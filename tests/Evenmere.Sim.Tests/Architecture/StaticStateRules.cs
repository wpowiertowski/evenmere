using System.Reflection;
using System.Runtime.CompilerServices;

namespace Evenmere.Sim.Tests.Architecture;

/// <summary>
/// No public static mutable state in an assembly. A public static field that is not <see langword="readonly"/> or
/// <see langword="const"/>, or a public static property with a public setter, is global state that two simulations in
/// one process would share, and that a replay would not reset (ADR-0003). It is checked on every type, public or not,
/// nested or not.
/// </summary>
/// <remarks>
/// Compiler-generated types and fields are skipped: a lambda cache such as <c>&lt;&gt;c.&lt;&gt;9__0_0</c> is a public static
/// non-readonly field the compiler writes for us. A <see langword="readonly"/> static that holds a mutable collection
/// is not caught; the analyser rules and review cover that.
/// </remarks>
internal static class StaticStateRules
{
    private const BindingFlags DeclaredStatics =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Checks every type in an assembly.</summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>One line per offending member, in ordinal order of the type's full name; empty when there is none.</returns>
    public static IReadOnlyList<string> Check(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return [.. assembly.GetTypes()
            .OrderBy(static t => t.FullName, StringComparer.Ordinal)
            .SelectMany(static t => Check(t))];
    }

    /// <summary>Checks the statics one type declares.</summary>
    /// <param name="type">The type.</param>
    /// <returns>One line per offending member; empty when there is none.</returns>
    public static IReadOnlyList<string> Check(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var violations = new List<string>();
        if (IsCompilerGenerated(type))
        {
            return violations;
        }

        string typeName = TypeNames.Display(type);
        foreach (FieldInfo field in type.GetFields(DeclaredStatics).OrderBy(static f => f.Name, StringComparer.Ordinal))
        {
            if (field is { IsPublic: true, IsInitOnly: false, IsLiteral: false } && !field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            {
                violations.Add($"{typeName}.{field.Name}: public static field is mutable; make it readonly or const, or move the state into WorldState");
            }
        }

        foreach (PropertyInfo property in type.GetProperties(DeclaredStatics).OrderBy(static p => p.Name, StringComparer.Ordinal))
        {
            if (property.SetMethod is { IsPublic: true })
            {
                violations.Add($"{typeName}.{property.Name}: public static property has a public setter; remove the setter or move the state into WorldState");
            }
        }

        return violations;
    }

    private static bool IsCompilerGenerated(Type type)
    {
        for (Type? level = type; level is not null; level = level.DeclaringType)
        {
            if (level.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            {
                return true;
            }
        }

        return false;
    }
}
