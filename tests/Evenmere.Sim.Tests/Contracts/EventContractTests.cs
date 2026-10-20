using System.Reflection;
using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Research;
using Evenmere.Sim.Tests.Scenarios;

namespace Evenmere.Sim.Tests.Contracts;

/// <summary>The event contract of M1-00: the M1 events exist, each a sealed record carrying its tick.</summary>
public sealed class EventContractTests
{
    private static IReadOnlyList<Type> AllEventTypes() =>
        [.. typeof(SimEvent).Assembly.GetTypes()
            .Where(static t => t is { IsAbstract: false, ContainsGenericParameters: false } && t.IsSubclassOf(typeof(SimEvent)))
            .OrderBy(static t => t.Name, StringComparer.Ordinal)];

    public static TheoryData<string> EventNames()
    {
        var data = new TheoryData<string>();
        foreach (Type type in AllEventTypes())
        {
            data.Add(type.Name);
        }

        return data;
    }

    [Fact]
    public void AllEventTypes_Simulation_DeclaresTheM1Events()
    {
        string[] names = [.. AllEventTypes().Select(static t => t.Name)];

        string[] expected =
        [
            "BuildingPlaced", "ConstructionCompleted", "BuildingDemolished", "BuildingUpgraded", "RoadsChanged",
            "ProductionStalled", "ProductionResumed", "TransportJobStarted", "TransportJobCompleted",
            "HouseTierAdvanced", "HouseTierDeclined", "ResidentsMovedIn",
            "TechResearched", "CivicAdopted", "InspirationTriggered", "AdviceRaised", "AdviceCleared",
        ];
        foreach (string name in expected)
        {
            names.ShouldContain(name);
        }
    }

    [Theory]
    [MemberData(nameof(EventNames))]
    public void Event_Type_IsASealedPublicRecordThatTakesTheTickFirst(string name)
    {
        Type type = AllEventTypes().Single(t => t.Name == name);

        type.IsSealed.ShouldBeTrue($"{name} must be sealed");
        type.IsPublic.ShouldBeTrue($"{name} must be public so the game can read it");
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).ShouldNotBeNull($"{name} must be a record");
        type.GetConstructors().Single().GetParameters()[0].ParameterType.ShouldBe(typeof(long), $"{name}'s first parameter is the tick");
        type.GetConstructors().Single().GetParameters()[0].Name.ShouldBe("Tick");
    }

    [Fact]
    public void Event_SimpleNames_AreUniqueAcrossSimulationAndTests()
    {
        // Scenario files name events by simple type name, so a clash would make a name ambiguous.
        EventRegistry.Default.Names.Count.ShouldBe(EventRegistry.Default.Names.Distinct().Count());
        foreach (string name in AllEventTypes().Select(static t => t.Name))
        {
            EventRegistry.Default.Contains(name).ShouldBeTrue(name);
        }
    }

    [Fact]
    public void InspirationTriggered_Factories_SetExactlyOneNode()
    {
        var tech = InspirationTriggered.ForTech(5, new TechId(2));
        var civic = InspirationTriggered.ForCivic(6, new CivicId(1));

        tech.Tech.ShouldBe(new TechId(2));
        tech.Civic.ShouldBeNull();
        civic.Civic.ShouldBe(new CivicId(1));
        civic.Tech.ShouldBeNull();
        tech.Tick.ShouldBe(5);
    }
}
