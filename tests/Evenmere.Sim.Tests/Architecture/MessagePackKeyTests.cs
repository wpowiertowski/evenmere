using Evenmere.Sim.State;

namespace Evenmere.Sim.Tests.Architecture;

/// <summary>ADR-0007: state types carry unique, explicit integer MessagePack keys, and every serialisable member has one.</summary>
public sealed class MessagePackKeyTests
{
    [Fact]
    public void WorldState_ReachableMessagePackTypes_HaveUniqueIntegerKeysOnEveryMember()
    {
        MessagePackKeyRules.Check(StateTypeWalker.Walk(typeof(WorldState))).ShouldBeEmpty();
    }

    [Fact]
    public void WorldState_ReachableMessagePackTypes_AreWorldStateAndEverySystemState()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(WorldState));

        Type[] marked = [.. graph.Types.Where(MessagePackKeyRules.IsMessagePackObject)];

        // The rule above is only meaningful if it actually looks at the state classes.
        marked.ShouldContain(typeof(WorldState));
        marked.Count(static t => t.Namespace!.StartsWith("Evenmere.Sim.Systems.", StringComparison.Ordinal))
            .ShouldBeGreaterThanOrEqualTo(Enum.GetValues<SystemId>().Length);
    }

    [Fact]
    public void WorldState_Keys_AreContiguousFromZero()
    {
        MessagePackKeyRules.CheckContiguousFromZero(typeof(WorldState)).ShouldBeEmpty();
    }

    [Fact]
    public void Check_DuplicateKey_IsReportedWithTheMembersThatShareIt()
    {
        Type bad = FakeMessagePackTypes.Define("DuplicateKeys",
            [FakeMember.IntProperty("First", FakeKey.Of(0)), FakeMember.IntProperty("Second", FakeKey.Of(0)), FakeMember.IntProperty("Third", FakeKey.Of(1))]);

        MessagePackKeyRules.Check(bad).ShouldHaveSingleItem().ShouldBe("DuplicateKeys: key 0 is used by 2 members (First, Second); keys must be unique");
    }

    [Fact]
    public void Check_StringKey_IsReported()
    {
        Type bad = FakeMessagePackTypes.Define("StringKeyed", [FakeMember.IntProperty("Name", FakeKey.Of("name"))]);

        MessagePackKeyRules.Check(bad).ShouldHaveSingleItem().ShouldContain("StringKeyed.Name: [Key(\"name\")] is a string key");
    }

    [Fact]
    public void Check_KeyAsPropertyName_IsReported()
    {
        Type bad = FakeMessagePackTypes.Define("PropertyNameKeyed", [FakeMember.IntProperty("Name", FakeKey.None)], keyAsPropertyName: true);

        MessagePackKeyRules.Check(bad).ShouldHaveSingleItem().ShouldContain("PropertyNameKeyed: [MessagePackObject(keyAsPropertyName: true)]");
    }

    [Fact]
    public void Check_NegativeKey_IsReported()
    {
        Type bad = FakeMessagePackTypes.Define("NegativeKeyed", [FakeMember.IntProperty("Name", FakeKey.Of(-1))]);

        MessagePackKeyRules.Check(bad).ShouldHaveSingleItem().ShouldContain("NegativeKeyed.Name: [Key(-1)] is negative");
    }

    [Theory]
    [InlineData("ReadOnly", nameof(FakeMemberKind.GetOnlyProperty))]
    [InlineData("PrivateSetter", nameof(FakeMemberKind.PrivateSetterProperty))]
    [InlineData("PrivateProperty", nameof(FakeMemberKind.PrivateProperty))]
    [InlineData("PublicField", nameof(FakeMemberKind.PublicField))]
    [InlineData("PrivateField", nameof(FakeMemberKind.PrivateField))]
    public void Check_MemberWithoutKeyOrIgnore_IsReported(string name, string kind)
    {
        // MessagePack's own resolver refuses all of these, so a state type like this fails the first save.
        Type bad = FakeMessagePackTypes.Define($"Missing{name}", [FakeMember.IntProperty("Keyed", FakeKey.Of(0)), FakeMember.Int(name, Enum.Parse<FakeMemberKind>(kind), FakeKey.None)]);

        MessagePackKeyRules.Check(bad).ShouldHaveSingleItem().ShouldBe($"Missing{name}.{name}: has neither [Key] nor [IgnoreMember], so MessagePack cannot serialise the type");
    }

    [Fact]
    public void Check_IgnoredAndPrivateKeyedMembers_AreAllowed()
    {
        Type good = FakeMessagePackTypes.Define("IgnoringAndPrivate",
        [
            FakeMember.IntProperty("Keyed", FakeKey.Of(0)),
            FakeMember.Int("Computed", FakeMemberKind.GetOnlyProperty, FakeKey.Ignored),
            FakeMember.Int("Cache", FakeMemberKind.PublicField, FakeKey.Ignored),
            FakeMember.Int("_scratch", FakeMemberKind.PrivateField, FakeKey.Ignored),
            FakeMember.Int("_hidden", FakeMemberKind.PrivateField, FakeKey.Of(1)),
        ]);

        MessagePackKeyRules.Check(good).ShouldBeEmpty();
    }

    [Fact]
    public void Check_TypeWithoutTheMarkerAttribute_IsNotChecked()
    {
        Type unmarked = FakeMessagePackTypes.DefineUnmarked("Unmarked", [FakeMember.IntProperty("Free", FakeKey.None), FakeMember.IntProperty("AlsoFree", FakeKey.None)]);

        MessagePackKeyRules.IsMessagePackObject(unmarked).ShouldBeFalse();
        MessagePackKeyRules.Check(unmarked).ShouldBeEmpty();
    }

    [Fact]
    public void Check_BadTypeReachedThroughAField_IsFoundByTheGraphCheck()
    {
        Type child = FakeMessagePackTypes.Define("GraphChild", [FakeMember.IntProperty("A", FakeKey.Of(3)), FakeMember.IntProperty("B", FakeKey.Of(3))]);
        Type root = FakeMessagePackTypes.Define("GraphRoot", [new FakeMember("Child", child, FakeMemberKind.Property, FakeKey.Of(0))]);

        MessagePackKeyRules.Check(StateTypeWalker.Walk(root)).ShouldHaveSingleItem().ShouldContain("GraphChild: key 3 is used by 2 members (A, B)");
    }

    [Fact]
    public void CheckContiguousFromZero_KeysInOrder_ReportNothing()
    {
        Type good = FakeMessagePackTypes.Define("Contiguous",
        [
            FakeMember.IntProperty("A", FakeKey.Of(1)),
            FakeMember.IntProperty("B", FakeKey.Of(0)),
            FakeMember.IntProperty("C", FakeKey.Of(2)),
            FakeMember.Int("Ignored", FakeMemberKind.Property, FakeKey.Ignored),
        ]);

        MessagePackKeyRules.CheckContiguousFromZero(good).ShouldBeEmpty();
    }

    [Fact]
    public void CheckContiguousFromZero_GapInTheKeys_IsReported()
    {
        Type gap = FakeMessagePackTypes.Define("KeysWithAGap", [FakeMember.IntProperty("A", FakeKey.Of(0)), FakeMember.IntProperty("B", FakeKey.Of(2))]);

        MessagePackKeyRules.CheckContiguousFromZero(gap).ShouldHaveSingleItem().ShouldBe("KeysWithAGap: keys must be 0..1 with no gaps or repeats, but are [0, 2]");
    }

    [Fact]
    public void CheckContiguousFromZero_KeysNotStartingAtZero_IsReported()
    {
        Type late = FakeMessagePackTypes.Define("KeysFromOne", [FakeMember.IntProperty("A", FakeKey.Of(1)), FakeMember.IntProperty("B", FakeKey.Of(2))]);

        MessagePackKeyRules.CheckContiguousFromZero(late).ShouldHaveSingleItem().ShouldContain("but are [1, 2]");
    }
}
