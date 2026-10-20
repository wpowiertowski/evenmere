using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using MessagePack;

namespace Evenmere.Sim.Tests.Architecture;

/// <summary>
/// The save-format rules for state types (ADR-0007): a <c>[MessagePackObject]</c> type has explicit integer keys, unique
/// within the type, and every member that MessagePack would serialise carries one. A renamed member keeps its key, so
/// saves survive the rename; a reused or implicit key silently corrupts every old save.
/// </summary>
/// <remarks>
/// <para>"Every serialisable member" is what MessagePack's own resolver demands, which was checked against
/// <c>StandardResolverAllowPrivate</c> (the one <c>SimResolver</c> uses): every instance field and every instance property,
/// public or private, read-only or not, must have <c>[Key]</c> or <c>[IgnoreMember]</c>, or serialising the type throws.
/// The exceptions are compiler-generated backing fields, indexers and a record's <c>EqualityContract</c>.</para>
/// <para>Two things MessagePack accepts without complaint are rejected here: string keys (including
/// <c>keyAsPropertyName: true</c>), which bloat the save and depend on member names, and negative integer keys, which it
/// silently drops from the output.</para>
/// </remarks>
internal static class MessagePackKeyRules
{
    private const BindingFlags DeclaredInstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Tests whether a type is marked <c>[MessagePackObject]</c>.</summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> if the key rules apply to it.</returns>
    public static bool IsMessagePackObject(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.IsDefined(typeof(MessagePackObjectAttribute), inherit: true);
    }

    /// <summary>Checks every <c>[MessagePackObject]</c> type in a walked graph.</summary>
    /// <param name="graph">The walked graph.</param>
    /// <returns>One line per problem; empty when every type is clean.</returns>
    public static IReadOnlyList<string> Check(StateTypeGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var violations = new List<string>();
        foreach (Type type in graph.Types.Where(IsMessagePackObject))
        {
            violations.AddRange(Check(type));
        }

        return violations;
    }

    /// <summary>Checks the keys of one type. A type without <c>[MessagePackObject]</c> has nothing to check.</summary>
    /// <param name="type">The type.</param>
    /// <returns>One line per problem; empty when the type is clean or unmarked.</returns>
    public static IReadOnlyList<string> Check(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var violations = new List<string>();
        MessagePackObjectAttribute? marker = type.GetCustomAttribute<MessagePackObjectAttribute>(inherit: true);
        if (marker is null)
        {
            return violations;
        }

        string typeName = TypeNames.Display(type);
        if (marker.KeyAsPropertyName)
        {
            // Every member is keyed by its name, so there are no [Key] attributes left to check.
            violations.Add($"{typeName}: [MessagePackObject(keyAsPropertyName: true)] gives every member a string key; use explicit integer keys (ADR-0007)");
            return violations;
        }

        var membersByKey = new SortedDictionary<int, List<string>>();
        foreach (MemberInfo member in SerialisableMembers(type))
        {
            string where = $"{typeName}.{member.Name}";
            KeyAttribute? key = member.GetCustomAttribute<KeyAttribute>(inherit: true);
            if (key is null)
            {
                violations.Add($"{where}: has neither [Key] nor [IgnoreMember], so MessagePack cannot serialise the type");
            }
            else if (key.IntKey is not { } intKey)
            {
                violations.Add($"{where}: [Key(\"{key.StringKey}\")] is a string key; use an integer key (ADR-0007)");
            }
            else if (intKey < 0)
            {
                violations.Add($"{where}: [Key({intKey})] is negative, and MessagePack silently drops a member with a negative key");
            }
            else
            {
                if (!membersByKey.TryGetValue(intKey, out List<string>? sharing))
                {
                    membersByKey[intKey] = sharing = [];
                }

                sharing.Add(member.Name);
            }
        }

        foreach ((int key, List<string> names) in membersByKey)
        {
            if (names.Count > 1)
            {
                violations.Add($"{typeName}: key {key} is used by {names.Count} members ({string.Join(", ", names.Order(StringComparer.Ordinal))}); keys must be unique");
            }
        }

        return violations;
    }

    /// <summary>
    /// Checks that a type's integer keys are exactly <c>0, 1, 2, …</c> with no gap and no repeat. Used for
    /// <c>WorldState</c>, whose keys are handed out in order; retiring a key later means updating that test on purpose.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>One line if the keys are not contiguous from zero; empty otherwise.</returns>
    public static IReadOnlyList<string> CheckContiguousFromZero(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        int[] keys =
        [
            .. SerialisableMembers(type)
                .Select(static m => m.GetCustomAttribute<KeyAttribute>(inherit: true)?.IntKey)
                .OfType<int>()
                .Order(),
        ];

        return keys.SequenceEqual(Enumerable.Range(0, keys.Length))
            ? []
            : [$"{TypeNames.Display(type)}: keys must be 0..{keys.Length - 1} with no gaps or repeats, but are [{string.Join(", ", keys)}]"];
    }

    private static IEnumerable<MemberInfo> SerialisableMembers(Type type)
    {
        for (Type? level = type; level is not null && level != typeof(object) && level != typeof(ValueType); level = level.BaseType)
        {
            foreach (PropertyInfo property in level.GetProperties(DeclaredInstanceMembers))
            {
                if (property.GetIndexParameters().Length == 0 && !IsRecordEqualityContract(property) && !IsIgnored(property))
                {
                    yield return property;
                }
            }

            foreach (FieldInfo field in level.GetFields(DeclaredInstanceMembers))
            {
                if (!field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) && !IsIgnored(field))
                {
                    yield return field;
                }
            }
        }
    }

    private static bool IsIgnored(MemberInfo member) =>
        member.IsDefined(typeof(IgnoreMemberAttribute), inherit: true) || member.IsDefined(typeof(IgnoreDataMemberAttribute), inherit: true);

    private static bool IsRecordEqualityContract(PropertyInfo property) =>
        property is { Name: "EqualityContract" } && property.PropertyType == typeof(Type) && property.GetMethod is { IsPublic: false };
}
