using System.Reflection;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Tests.Architecture;
using MessagePack;

namespace Evenmere.Sim.Tests.Persistence;

/// <summary>
/// Checks that every array-typed serialised member of the state names its content dependence: indexed by a content kind,
/// holding content IDs as raw numbers, or explicitly not content-indexed with a reason.
/// </summary>
internal static class ContentIndexRules
{
    private const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Checks every <c>[MessagePackObject]</c> type in a walked graph.</summary>
    /// <param name="graph">The walked graph.</param>
    /// <returns>One line per array member without a marker; empty when every array is classified.</returns>
    public static IReadOnlyList<string> Check(StateTypeGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var violations = new List<string>();
        foreach (StateTypeUse use in graph.Uses)
        {
            if (!use.DeclaredType.IsArray || !IsStateMember(use))
            {
                continue;
            }

            if (!HasMarker(use, out string where))
            {
                violations.Add($"{where}: array member with no content marker; add [ContentIndexed], [ContentValued] or [NotContentIndexed]");
            }
        }

        return violations;
    }

    /// <summary>Tests whether a use is the array member of a state type the rule covers.</summary>
    /// <param name="use">The use.</param>
    /// <returns><see langword="true"/> for an array declared by a <c>[MessagePackObject]</c> type.</returns>
    public static bool IsStateMember(StateTypeUse use)
    {
        ArgumentNullException.ThrowIfNull(use);
        if (use.Field is null || !IsMessagePackObject(use.Field.DeclaringType))
        {
            return false;
        }

        // The walker's generic-argument uses (IdTable&lt;ProducerRecord&gt; reaching ProducerRecord) have no field of
        // their own; only a member declared by a state type can need a marker.
        return use.Path.Count > 0 && use.Path[^1].Owner == use.Field.DeclaringType;
    }

    private static bool HasMarker(StateTypeUse use, out string where)
    {
        where = use.Text;
        FieldInfo field = use.Field!;
        Type owner = field.DeclaringType!;
        string name = MemberName(field.Name);

        MemberInfo? member = (MemberInfo?)owner.GetProperty(name, Members) ?? owner.GetField(name, Members);
        if (member is null)
        {
            where = $"{TypeNames.Display(owner)}.{name}: member not found";
            return false;
        }

        where = $"{TypeNames.Display(owner)}.{member.Name}";
        return member.IsDefined(typeof(ContentIndexedAttribute), inherit: false)
            || member.IsDefined(typeof(ContentValuedAttribute), inherit: false)
            || member.IsDefined(typeof(NotContentIndexedAttribute), inherit: false);
    }

    private static bool IsMessagePackObject(Type? type) =>
        type is not null && type.IsDefined(typeof(MessagePackObjectAttribute), inherit: true);

    private static string MemberName(string fieldName)
    {
        const string Suffix = ">k__BackingField";
        if (fieldName.StartsWith('<') && fieldName.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return fieldName[1..^Suffix.Length];
        }

        return fieldName;
    }
}
