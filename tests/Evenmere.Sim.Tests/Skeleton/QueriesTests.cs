using Evenmere.Sim.Queries;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Skeleton;

public sealed class QueriesTests
{
    [Fact]
    public void Query_EveryProperty_IsPresent()
    {
        ISimQueries query = new WorldBuilder().Build().Query;

        object[] all =
        [
            query.Map, query.Roads, query.Storage, query.Modifiers, query.Construction, query.Workforce, query.Production,
            query.Logistics, query.Exploration, query.Needs, query.Population, query.Economy, query.Research, query.Culture,
            query.Progression, query.Happenings, query.Advisors, query.Statistics, query.Placement, query.Explain,
        ];

        all.Length.ShouldBe(20);
        all.ShouldAllBe(q => q != null);
        all.Distinct().Count().ShouldBe(20);
    }

    [Fact]
    public void Query_PerSystemInterfaces_MatchTheSystemFolders()
    {
        // One property per system (named like the system) plus Placement and Explain.
        string[] properties = [.. typeof(ISimQueries).GetProperties().Select(static p => p.Name).Order()];

        string[] expected = [.. Enum.GetNames<SystemId>().Append("Placement").Append("Explain").Order()];
        properties.ShouldBe(expected);
        foreach (System.Reflection.PropertyInfo property in typeof(ISimQueries).GetProperties())
        {
            property.PropertyType.Name.ShouldBe($"I{property.Name}Queries");
        }
    }

    [Fact]
    public void Query_SameInstanceEachTime()
    {
        Simulation sim = new WorldBuilder().Build();

        sim.Query.ShouldBeSameAs(sim.Query);
        sim.Query.Storage.ShouldBeSameAs(sim.Query.Storage);
    }
}
