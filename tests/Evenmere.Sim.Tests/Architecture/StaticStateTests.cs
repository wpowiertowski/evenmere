using System.Reflection;

namespace Evenmere.Sim.Tests.Architecture;

/// <summary>ADR-0003: Evenmere.Sim has no public static mutable fields, which two simulations in one process would share.</summary>
public sealed class StaticStateTests
{
    [Fact]
    public void Sim_Assembly_HasNoPublicStaticMutableFieldsOrSettableProperties()
    {
        StaticStateRules.Check(typeof(Simulation).Assembly).ShouldBeEmpty();
    }

    [Fact]
    public void Check_PublicStaticFieldAndSettableProperty_AreReported()
    {
        IReadOnlyList<string> violations = StaticStateRules.Check(typeof(FakeStatics));

        violations.Count.ShouldBe(2);
        violations.ShouldContain(static v => v.StartsWith("FakeStatics.MutableField: public static field is mutable", StringComparison.Ordinal));
        violations.ShouldContain(static v => v.StartsWith("FakeStatics.MutableProperty: public static property has a public setter", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_ReadonlyConstGetOnlyPrivateSetterAndNonPublicStatics_AreAllowed()
    {
        string[] allowed = ["ReadonlyField", "ConstField", "GetOnlyProperty", "PrivateSetProperty", "InternalMutableField", "_privateMutableField"];

        IReadOnlyList<string> violations = StaticStateRules.Check(typeof(FakeStatics));

        foreach (string name in allowed)
        {
            violations.ShouldNotContain(v => v.StartsWith($"FakeStatics.{name}:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Check_StaticFieldOfANestedType_IsReported()
    {
        StaticStateRules.Check(typeof(FakeStatics.Nested))
            .ShouldHaveSingleItem()
            .ShouldStartWith("FakeStatics.Nested.NestedMutableField: public static field is mutable");
    }

    [Fact]
    public void Check_Assembly_FindsStaticsInNestedTypes()
    {
        StaticStateRules.Check(typeof(StaticStateTests).Assembly)
            .ShouldContain(static v => v.StartsWith("FakeStatics.Nested.NestedMutableField:", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_CompilerGeneratedLambdaCache_IsNotReported()
    {
        Assembly tests = typeof(StaticStateTests).Assembly;

        // The compiler caches each static lambda in a public static non-readonly field of a generated <>c class.
        tests.GetTypes().Any(static t => t.Name == "<>c").ShouldBeTrue();
        StaticStateRules.Check(tests).ShouldNotContain(static v => v.Contains("<>", StringComparison.Ordinal));
    }
}
