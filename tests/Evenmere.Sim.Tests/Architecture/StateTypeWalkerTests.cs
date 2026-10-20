using System.Numerics;
using Evenmere.Core.Collections;

namespace Evenmere.Sim.Tests.Architecture;

/// <summary>Proves the walker finds what the architecture rules rely on it to find, using small fake type graphs.</summary>
public sealed class StateTypeWalkerTests
{
    [Fact]
    public void Walk_Root_IsTheFirstNodeWithAnEmptyPath()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(FakeWalkRoot));

        graph.Root.ShouldBe(typeof(FakeWalkRoot));
        graph.Nodes[0].Type.ShouldBe(typeof(FakeWalkRoot));
        graph.Nodes[0].Path.ShouldBeEmpty();
        graph.Nodes[0].Text.ShouldBe("FakeWalkRoot");
    }

    [Fact]
    public void Walk_PrivateField_IsFollowed()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(FakeWalkRoot));

        graph.NodeOf(typeof(FakeWalkPrivateTarget)).Text.ShouldBe("FakeWalkRoot._hidden");
    }

    [Fact]
    public void Walk_AutoPropertyBackingField_IsFollowedAndNamedAfterTheProperty()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(FakeWalkRoot));

        graph.NodeOf(typeof(FakeWalkPropertyTarget)).Text.ShouldBe("FakeWalkRoot.Visible");
    }

    [Fact]
    public void Walk_GenericArgument_IsFollowedEvenWhenTheGenericTypeStoresNoValueOfIt()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(FakeWalkRoot));

        graph.NodeOf(typeof(FakeWalkArgumentTarget)).Text.ShouldBe("FakeWalkRoot.Held");
    }

    [Fact]
    public void Walk_ArrayElementType_IsFollowed()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(FakeWalkRoot));

        graph.NodeOf(typeof(FakeWalkElementTarget)).Text.ShouldBe("FakeWalkRoot.Items");
    }

    [Fact]
    public void Walk_PrivateNestedType_IsFollowedThroughTheFieldThatUsesIt()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(FakeWalkRoot));

        graph.NodeOf(typeof(FakeWalkNestedTarget)).Text.ShouldBe("FakeWalkRoot._nested → FakeWalkRoot.FakeWalkNested.Target");
    }

    [Fact]
    public void Walk_StaticField_IsNotFollowed()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(FakeWalkRoot));

        graph.Contains(typeof(FakeWalkStaticTarget)).ShouldBeFalse();
    }

    [Fact]
    public void Walk_PrivateFieldOfABaseClass_IsFollowed()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(FakeWalkDerived));

        graph.NodeOf(typeof(FakeWalkBaseTarget)).Text.ShouldBe("FakeWalkDerived.<base> → FakeWalkBase._inherited");
    }

    [Fact]
    public void Walk_ClosedGenericType_SeesTheArgumentAsTheFieldType()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(FakeIdTableRoot));

        // IdTable<T> keeps its values in a private nested Slot struct; the Slot field of type T is seen as FakeBadValue.
        graph.Uses.ShouldContain(u => u.Field != null && u.Field.Name == "Value" && u.DeclaredType == typeof(FakeBadValue));
        graph.Uses.ShouldContain(u => u.Field != null && u.Field.Name == "_weight" && u.DeclaredType == typeof(double));
    }

    [Fact]
    public void Walk_ReferencesThatFormACycle_VisitsEachTypeOnce()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(FakeCycleA));

        graph.Types.Count(t => t == typeof(FakeCycleA)).ShouldBe(1);
        graph.Types.Count(t => t == typeof(FakeCycleB)).ShouldBe(1);
        graph.Types.Count(t => t == typeof(FakeCycleB[])).ShouldBe(1);
        graph.Types.Distinct().Count().ShouldBe(graph.Nodes.Count);
    }

    [Fact]
    public void Walk_GenericTypeThatNestsDeeperAtEveryLevel_ThrowsInsteadOfRunningForever()
    {
        Should.Throw<InvalidOperationException>(() => StateTypeWalker.Walk(typeof(FakeRunaway<int>), maxTypes: 64))
            .Message.ShouldContain("FakeRunaway");
    }

    [Fact]
    public void Walk_UnionInterface_FollowsTheSubtypesItNames()
    {
        Type union = FakeMessagePackTypes.DefineUnionInterface("FakeWalkUnion", [typeof(FakeUnionMember)]);

        StateTypeGraph graph = StateTypeWalker.Walk(union);

        graph.NodeOf(typeof(FakeUnionMember)).Text.ShouldBe("FakeWalkUnion.[Union]");
        graph.Contains(typeof(double)).ShouldBeTrue();
    }

    [Fact]
    public void Walk_BaseClassLibraryStruct_IsOpenedToSeeItsFields()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(FakeBclStructRoot));

        graph.NodeOf(typeof(float)).Text.ShouldBe("FakeBclStructRoot.Position → Vector2.X");
    }

    [Fact]
    public void Walk_BaseClassLibraryReferenceType_IsNotOpenedButItsArgumentsAre()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(FakeAllowedRoot));

        graph.Contains(typeof(List<int>)).ShouldBeTrue();
        graph.Uses.ShouldNotContain(u => u.Path.Any(s => s.Owner == typeof(List<int>)));
        graph.Contains(typeof(FakeAllowedChild)).ShouldBeTrue();
    }

    [Fact]
    public void Walk_Twice_GivesTheSameNodesInTheSameOrder()
    {
        string[] first = [.. StateTypeWalker.Walk(typeof(FakeAllowedRoot)).Nodes.Select(static n => n.Text + "|" + TypeNames.Display(n.Type))];
        string[] second = [.. StateTypeWalker.Walk(typeof(FakeAllowedRoot)).Nodes.Select(static n => n.Text + "|" + TypeNames.Display(n.Type))];

        second.ShouldBe(first);
    }

    [Fact]
    public void TypesDeclaredIn_Assembly_ListsTypeDefinitionsAndNotPrimitivesOrArrays()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(FakeIdTableRoot));

        IReadOnlyList<Type> core = graph.TypesDeclaredIn(typeof(IdTable<>).Assembly);
        IReadOnlyList<Type> tests = graph.TypesDeclaredIn(typeof(StateTypeWalkerTests).Assembly);

        core.ShouldContain(typeof(IdTable<>));
        core.ShouldAllBe(t => t.Assembly == typeof(IdTable<>).Assembly);
        tests.ShouldContain(typeof(FakeIdTableRoot));
        tests.ShouldContain(typeof(FakeBadValue));
        tests.ShouldNotContain(typeof(double));
        tests.ShouldAllBe(t => !t.IsArray && t.Assembly == typeof(StateTypeWalkerTests).Assembly);
        graph.TypesDeclaredIn(typeof(Vector2).Assembly).ShouldNotContain(typeof(FakeIdTableRoot));
    }
}
