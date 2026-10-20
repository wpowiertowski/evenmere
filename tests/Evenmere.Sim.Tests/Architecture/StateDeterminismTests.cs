using Evenmere.Core.Rng;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.State;

namespace Evenmere.Sim.Tests.Architecture;

/// <summary>ADR-0002 / ADR-0003: nothing reachable from <c>WorldState</c> is a float, a hash-ordered collection or an opaque object.</summary>
public sealed class StateDeterminismTests
{
    [Fact]
    public void WorldState_ReachableTypes_HaveNoForbiddenFieldTypes()
    {
        ForbiddenFieldTypeRules.Check(typeof(WorldState)).ShouldBeEmpty();
    }

    [Fact]
    public void WorldState_Walk_ReachesEverySystemStateAndTheCoreValueTypes()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(WorldState));

        var missing = new List<string>();
        foreach (SystemId id in Enum.GetValues<SystemId>())
        {
            Type? state = typeof(WorldState).Assembly.GetType($"Evenmere.Sim.Systems.{id}.{id}State");
            if (state is null || !graph.Contains(state))
            {
                missing.Add($"{id}State");
            }
        }

        foreach (Type core in new[] { typeof(Pcg32), typeof(Pcg32[]), typeof(GameClock), typeof(Amount), typeof(Amount[]), typeof(ulong) })
        {
            if (!graph.Contains(core))
            {
                missing.Add(TypeNames.Display(core));
            }
        }

        missing.ShouldBeEmpty();
    }

    [Fact]
    public void WorldState_Walk_OpensCoreTypesSoTheirPrivateFieldsAreChecked()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(WorldState));

        // The generator's two words are private fields of a Core struct; if the walker skipped Core they would go unchecked.
        // The walker records each type's fields once, along the first path that reaches it, so which system's state gets
        // there first changes as states grow. The paths are therefore matched by their ending, not spelled out in full.
        string[] uses = [.. graph.Uses.Select(static u => u.Text)];
        uses.ShouldContain(static text => text.EndsWith(" → Pcg32._state", StringComparison.Ordinal));
        uses.ShouldContain(static text => text.EndsWith(" → Amount.Milli", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_PrivateDoubleBehindAProperty_IsReportedWithItsPath()
    {
        IReadOnlyList<string> violations = ForbiddenFieldTypeRules.Check(typeof(FakeLeakyRoot));

        violations.ShouldHaveSingleItem().ShouldStartWith("FakeLeakyRoot.Child → FakeLeakyChild._speed: double is forbidden");
    }

    [Fact]
    public void Check_DictionaryInAnArrayOfAGenericStruct_IsReportedWithItsPath()
    {
        IReadOnlyList<string> violations = ForbiddenFieldTypeRules.Check(typeof(FakeArrayRoot));

        violations.ShouldHaveSingleItem().ShouldStartWith("FakeArrayRoot.Cells → FakeCell<int>._lookup: Dictionary<int, int> is forbidden");
    }

    [Fact]
    public void Check_DoubleInsideAnIdTableValue_IsReported()
    {
        IReadOnlyList<string> violations = ForbiddenFieldTypeRules.Check(typeof(FakeIdTableRoot));

        violations.ShouldHaveSingleItem().ShouldStartWith("FakeIdTableRoot.Table → FakeBadValue._weight: double is forbidden");
    }

    [Fact]
    public void Check_FloatsInABaseClassLibraryStruct_AreReported()
    {
        IReadOnlyList<string> violations = ForbiddenFieldTypeRules.Check(typeof(FakeBclStructRoot));

        violations.ShouldContain(static v => v.StartsWith("FakeBclStructRoot.Position → Vector2.X: float is forbidden", StringComparison.Ordinal));
        violations.ShouldContain(static v => v.StartsWith("FakeBclStructRoot.Position → Vector2.Y: float is forbidden", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(nameof(FakeForbiddenRoot.FloatField), "float is forbidden")]
    [InlineData(nameof(FakeForbiddenRoot.DoubleField), "double is forbidden")]
    [InlineData(nameof(FakeForbiddenRoot.DecimalField), "decimal is forbidden")]
    [InlineData(nameof(FakeForbiddenRoot.DictionaryField), "Dictionary<int, int> is forbidden")]
    [InlineData(nameof(FakeForbiddenRoot.HashSetField), "HashSet<int> is forbidden")]
    [InlineData(nameof(FakeForbiddenRoot.ConcurrentDictionaryField), "ConcurrentDictionary<int, int> is forbidden")]
    [InlineData(nameof(FakeForbiddenRoot.SortedListField), "SortedList<int, int> is forbidden")]
    [InlineData(nameof(FakeForbiddenRoot.ReadOnlyDictionaryField), "ReadOnlyDictionary<int, int> is forbidden")]
    [InlineData(nameof(FakeForbiddenRoot.ImmutableHashSetField), "ImmutableHashSet<int> is forbidden")]
    [InlineData(nameof(FakeForbiddenRoot.DictionaryInterfaceField), "IDictionary<int, int> is forbidden")]
    [InlineData(nameof(FakeForbiddenRoot.ReadOnlyDictionaryInterfaceField), "IReadOnlyDictionary<int, int> is forbidden")]
    [InlineData(nameof(FakeForbiddenRoot.SetInterfaceField), "ISet<int> is forbidden")]
    [InlineData(nameof(FakeForbiddenRoot.HashtableField), "Hashtable is forbidden")]
    [InlineData(nameof(FakeForbiddenRoot.ObjectField), "object is forbidden: object hides the real type")]
    [InlineData(nameof(FakeForbiddenRoot.DynamicField), "object is forbidden: dynamic hides the real type")]
    [InlineData(nameof(FakeForbiddenRoot.NullableDoubleField), "double (written inside Nullable<double>) is forbidden")]
    [InlineData(nameof(FakeForbiddenRoot.ListOfFloatField), "float (written inside List<float>) is forbidden")]
    public void Check_ForbiddenFieldType_IsReportedAtThatField(string field, string expected)
    {
        IReadOnlyList<string> violations = ForbiddenFieldTypeRules.Check(typeof(FakeForbiddenRoot));

        violations.ShouldContain(v => v.StartsWith($"FakeForbiddenRoot.{field}: {expected}", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_OnlyTheForbiddenFields_AreReported()
    {
        IReadOnlyList<string> violations = ForbiddenFieldTypeRules.Check(typeof(FakeForbiddenRoot));

        // Every field of the fake root is forbidden, so each one is reported at its own path and nothing else is.
        string[] reportedFields =
        [
            .. violations
                .Select(static v => v.Split(':')[0].Split(" → ")[0])
                .Distinct()
                .Order(StringComparer.Ordinal),
        ];
        string[] declaredFields =
        [
            .. typeof(FakeForbiddenRoot).GetFields().Select(static f => $"FakeForbiddenRoot.{f.Name}").Order(StringComparer.Ordinal),
        ];

        reportedFields.ShouldBe(declaredFields);
    }

    [Fact]
    public void Check_AllowedTypes_ReportNothing()
    {
        // SortedDictionary and SortedSet are the ordered exceptions; IdTable, arrays, List, tuples, Core types are fine.
        ForbiddenFieldTypeRules.Check(typeof(FakeAllowedRoot)).ShouldBeEmpty();
    }

    [Fact]
    public void Check_TypeThatIsItselfForbidden_IsReportedAtTheRoot()
    {
        ForbiddenFieldTypeRules.Check(typeof(Dictionary<int, int>))
            .ShouldContain(static v => v.StartsWith("Dictionary<int, int>: Dictionary<int, int> is forbidden", StringComparison.Ordinal));
    }
}
