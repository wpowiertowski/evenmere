using System.Reflection;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.State;
using MessagePack;

namespace Evenmere.Sim.Tests.Skeleton;

public sealed class SystemFolderTests
{
    private static readonly Assembly _simAssembly = typeof(Simulation).Assembly;

    public static TheoryData<string> SystemNamesData()
    {
        var data = new TheoryData<string>();
        foreach (SystemId id in Enum.GetValues<SystemId>())
        {
            data.Add(id.ToString());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SystemNamesData))]
    public void Folder_EverySystem_HasItsStubStateAndSystem(string name)
    {
        string ns = $"Evenmere.Sim.Systems.{name}";

        Type? state = _simAssembly.GetType($"{ns}.{name}State");
        Type? system = _simAssembly.GetType($"{ns}.{name}System");
        Type? queries = _simAssembly.GetType($"{ns}.{name}Queries");

        state.ShouldNotBeNull($"{ns}.{name}State");
        system.ShouldNotBeNull($"{ns}.{name}System");
        queries.ShouldNotBeNull($"{ns}.{name}Queries");
        state.IsSealed.ShouldBeTrue();
        state.GetCustomAttribute<MessagePackObjectAttribute>().ShouldNotBeNull();
        system.IsAssignableTo(typeof(ISimSystem)).ShouldBeTrue();
        queries.GetInterfaces().ShouldContain(_simAssembly.GetType($"Evenmere.Sim.Queries.I{name}Queries")!);
    }

    [Theory]
    [MemberData(nameof(SystemNamesData))]
    public void WorldState_EverySystem_HasAKeyedStateMember(string name)
    {
        PropertyInfo? property = typeof(WorldState).GetProperty(name);

        property.ShouldNotBeNull();
        property.PropertyType.FullName.ShouldBe($"Evenmere.Sim.Systems.{name}.{name}State");
        property.GetCustomAttribute<KeyAttribute>().ShouldNotBeNull();
        property.GetCustomAttribute<KeyAttribute>()!.IntKey.ShouldNotBeNull();
    }

    [Fact]
    public void WorldState_Keys_AreUniqueIntegersFromZero()
    {
        int[] keys =
        [
            .. typeof(WorldState).GetProperties()
                .Select(static p => p.GetCustomAttribute<KeyAttribute>())
                .Select(static k => k!.IntKey!.Value)
                .Order(),
        ];

        keys.ShouldBe(Enumerable.Range(0, keys.Length));
    }
}
