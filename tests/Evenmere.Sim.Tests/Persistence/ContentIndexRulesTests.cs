using System.Reflection;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Tests.Architecture;
using Evenmere.Sim.State;
using MessagePack;

namespace Evenmere.Sim.Tests.Persistence;

/// <summary>
/// The save remap inventory (M1-14): every array-typed member of every <c>[MessagePackObject]</c> type reachable from
/// <see cref="WorldState"/> carries either a content marker (<see cref="ContentIndexedAttribute"/> or
/// <see cref="ContentValuedAttribute"/>) or an explicit <see cref="NotContentIndexedAttribute"/>. Adding a new per-good
/// array without deciding how loads remap it fails here, not in a player's save.
/// </summary>
public sealed class ContentIndexRulesTests
{
    [Fact]
    public void WorldState_ArrayMembers_AllCarryAContentMarker()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(WorldState));
        IReadOnlyList<string> violations = ContentIndexRules.Check(graph);

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void WorldState_ArrayMembers_RuleSeesStateArrays()
    {
        // The rule above is only meaningful if it actually looks at array members of state types.
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(WorldState));

        int arrays = graph.Uses.Count(static use => use.DeclaredType.IsArray && ContentIndexRules.IsStateMember(use));
        arrays.ShouldBeGreaterThan(20);
    }
}
