using System.Reflection;
using System.Text.Json;
using Evenmere.Sim.State;
using Evenmere.Sim.Tests.Architecture;
using Evenmere.Sim.Tests.Fixtures;
namespace Evenmere.Sim.Tests.Persistence;

/// <summary>
/// The MessagePack key baseline (ADR-0007): <c>tests/fixtures/saves/state-keys.json</c> lists every
/// <c>[MessagePackObject]</c> type reachable from <see cref="WorldState"/> with its key-to-member map. Renaming a
/// field is free, but a key that disappears, changes member type or is reused fails here. Adding keys is allowed; the
/// test then prints how to refresh the baseline, and <c>EVENMERE_UPDATE_STATE_KEYS=1</c> rewrites the file.
/// </summary>
public sealed class StateKeysTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="StateKeysTests"/> class.</summary>
    /// <param name="output">The test output.</param>
    public StateKeysTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Gets the baseline path.</summary>
    public static string BaselinePath => Path.Combine(WorldBuilder.RepoRoot, "tests", "fixtures", "saves", "state-keys.json");

    [Fact]
    public void WorldState_Keys_MatchBaseline()
    {
        SortedDictionary<string, SortedDictionary<string, KeyEntry>> actual = BuildBaseline();
        if (UpdateRequested)
        {
            WriteBaseline(actual);
        }

        if (!File.Exists(BaselinePath))
        {
            Assert.Fail($"The key baseline is missing: run with EVENMERE_UPDATE_STATE_KEYS=1 to write {BaselinePath}.");
        }

        SortedDictionary<string, SortedDictionary<string, KeyEntry>> baseline = ReadBaseline();
        var failures = new List<string>();
        foreach ((string type, SortedDictionary<string, KeyEntry> keys) in baseline)
        {
            if (!actual.TryGetValue(type, out SortedDictionary<string, KeyEntry>? current))
            {
                failures.Add($"{type}: the type is gone from the state");
                continue;
            }

            foreach ((string key, KeyEntry entry) in keys)
            {
                if (!current.TryGetValue(key, out KeyEntry? now))
                {
                    failures.Add($"{type} key {key} ({entry.Member}: {entry.Type}): the key disappeared");
                }
                else if (now.Member != entry.Member || now.Type != entry.Type)
                {
                    failures.Add($"{type} key {key}: was {entry.Member}: {entry.Type}, now {now.Member}: {now.Type}");
                }
            }
        }

        int added = actual.Sum(kv => kv.Value.Count) - baseline.Sum(kv => kv.Value.Count);
        if (added > 0)
        {
            _output.WriteLine($"{added} new key(s) since the baseline; run with EVENMERE_UPDATE_STATE_KEYS=1 to refresh {BaselinePath}.");
        }

        failures.ShouldBeEmpty();
    }

    private static SortedDictionary<string, SortedDictionary<string, KeyEntry>> BuildBaseline()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(WorldState));
        var baseline = new SortedDictionary<string, SortedDictionary<string, KeyEntry>>(StringComparer.Ordinal);
        foreach (Type type in graph.Types)
        {
            if (!MessagePackKeyRules.IsMessagePackObject(type))
            {
                continue;
            }

            SortedDictionary<string, KeyEntry>? keys = KeysOf(type);
            if (keys is not null)
            {
                baseline[Display(type)] = keys;
            }
        }

        return baseline;
    }

    private static SortedDictionary<string, KeyEntry>? KeysOf(Type type)
    {
        var keys = new SortedDictionary<string, KeyEntry>(StringComparer.Ordinal);
        // The same members MessagePack's own resolver reads: every instance property and field, public or
        // private, except indexers, the record equality contract and [IgnoreMember].
        for (Type? level = type; level is not null && level != typeof(object) && level != typeof(ValueType); level = level.BaseType)
        {
            foreach (PropertyInfo property in level.GetProperties(DeclaredMembers))
            {
                if (property.GetIndexParameters().Length == 0 && !IsRecordEqualityContract(property) && !IsIgnored(property))
                {
                    AddKey(keys, property);
                }
            }

            foreach (FieldInfo field in level.GetFields(DeclaredMembers))
            {
                if (!field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false) && !IsIgnored(field))
                {
                    AddKey(keys, field);
                }
            }
        }

        return keys.Count == 0 ? null : keys;
    }

    private const BindingFlags DeclaredMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static void AddKey(SortedDictionary<string, KeyEntry> keys, System.Reflection.MemberInfo member)
    {
        MessagePack.KeyAttribute? key = member.GetCustomAttribute<MessagePack.KeyAttribute>(inherit: true);
        if (key?.IntKey is not { } number)
        {
            return;
        }

        keys[number.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
            new KeyEntry(MemberDisplay(member), MemberTypeDisplay(member));
    }

    private static bool IsIgnored(MemberInfo member) =>
        member.IsDefined(typeof(MessagePack.IgnoreMemberAttribute), inherit: true);

    private static bool IsRecordEqualityContract(PropertyInfo property) =>
        property is { Name: "EqualityContract" } && property.PropertyType == typeof(Type) && property.GetMethod is { IsPublic: false };

    private static string Display(Type type) => type.FullName ?? type.Name;

    private static string MemberDisplay(MemberInfo member) => member switch
    {
        PropertyInfo property => $"property {property.Name}",
        FieldInfo field => $"field {field.Name}",
        _ => member.Name,
    };

    private static string MemberTypeDisplay(MemberInfo member)
    {
        Type type = member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => typeof(object),
        };
        return TypeDisplay(type);
    }

    private static string TypeDisplay(Type type)
    {
        if (type.IsArray)
        {
            return TypeDisplay(type.GetElementType()!) + "[]";
        }

        if (type.IsGenericType)
        {
            string name = type.GetGenericTypeDefinition().FullName ?? type.Name;
            return $"{name}<{string.Join(",", type.GetGenericArguments().Select(TypeDisplay))}>";
        }

        return type.FullName ?? type.Name;
    }

    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions _readOptions = new() { PropertyNameCaseInsensitive = true };

    private static void WriteBaseline(SortedDictionary<string, SortedDictionary<string, KeyEntry>> baseline)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath)!);
        File.WriteAllText(BaselinePath, JsonSerializer.Serialize(new BaselineFile(baseline), _writeOptions) + "\n");
    }

    private static SortedDictionary<string, SortedDictionary<string, KeyEntry>> ReadBaseline()
    {
        BaselineFile? file = JsonSerializer.Deserialize<BaselineFile>(File.ReadAllText(BaselinePath), _readOptions);
        return file?.Types ?? [];
    }

    private static bool UpdateRequested =>
        string.Equals(Environment.GetEnvironmentVariable("EVENMERE_UPDATE_STATE_KEYS"), "1", StringComparison.Ordinal);

    private sealed record BaselineFile(SortedDictionary<string, SortedDictionary<string, KeyEntry>> Types);

    private sealed record KeyEntry(string Member, string Type);
}
