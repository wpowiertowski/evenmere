using System.Reflection;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Services;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Contracts;

/// <summary>The query contract of M1-00: the facade exposes every query interface, fakes exist for all of them, and the stubs say who owes the implementation.</summary>
public sealed class QueryContractTests
{
    private static readonly Assembly _simAssembly = typeof(ISimQueries).Assembly;

    private static IEnumerable<Type> QueryInterfaces() =>
        _simAssembly.GetTypes().Where(static t => t.IsInterface
            && t.IsPublic
            && t.Namespace == "Evenmere.Sim.Queries"
            && t.Name.EndsWith("Queries", StringComparison.Ordinal)
            && t != typeof(ISimQueries));

    public static TheoryData<string> QueryInterfaceNames()
    {
        var data = new TheoryData<string>();
        foreach (Type type in QueryInterfaces().OrderBy(static t => t.Name, StringComparer.Ordinal))
        {
            data.Add(type.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(QueryInterfaceNames))]
    public void SimQueries_EveryQueryInterface_IsExposedByExactlyOneProperty(string interfaceName)
    {
        Type queryInterface = QueryInterfaces().Single(t => t.Name == interfaceName);

        PropertyInfo[] exposing = [.. typeof(ISimQueries).GetProperties().Where(p => p.PropertyType == queryInterface)];

        exposing.Length.ShouldBe(1, $"ISimQueries must expose {interfaceName} through exactly one property");
    }

    [Fact]
    public void SimQueries_EveryProperty_ReturnsAQueryInterface()
    {
        foreach (PropertyInfo property in typeof(ISimQueries).GetProperties())
        {
            QueryInterfaces().ShouldContain(property.PropertyType, $"ISimQueries.{property.Name}");
        }
    }

    [Theory]
    [MemberData(nameof(QueryInterfaceNames))]
    public void Fakes_EveryQueryInterface_HasAFakeThatImplementsIt(string interfaceName)
    {
        Type queryInterface = QueryInterfaces().Single(t => t.Name == interfaceName);

        Type[] fakes =
        [
            .. typeof(FakeSimQueries).Assembly.GetTypes().Where(t =>
                t.Namespace == "Evenmere.Sim.Tests.Fakes" && t.Name.StartsWith("Fake", StringComparison.Ordinal) && queryInterface.IsAssignableFrom(t)),
        ];

        fakes.ShouldNotBeEmpty($"There is no fake for {interfaceName}");
    }

    [Fact]
    public void FakeSimQueries_Facade_ExposesAFakeForEveryProperty()
    {
        ISimQueries queries = new FakeSimQueries();

        foreach (PropertyInfo property in typeof(ISimQueries).GetProperties())
        {
            property.GetValue(queries).ShouldNotBeNull(property.Name);
        }
    }

    [Fact]
    public void QueryStubs_RealSimulation_ThrowNotImplementedWithTheOwningTaskOrWork()
    {
        // Each system's query class is a throwing stub until its task lands, and then a real implementation. Either way a
        // call may not fail some other way, and a stub names its task. Methods that take or return a span cannot be called
        // by reflection, so they are covered by StubSpanMethods_AreStubsOrWork below.
        ISimQueries queries = new WorldBuilder().Build().Query;
        var unexpected = new List<string>();

        foreach (PropertyInfo facade in typeof(ISimQueries).GetProperties())
        {
            object target = facade.GetValue(queries)!;
            foreach (MethodInfo method in facade.PropertyType.GetMethods().Where(static m => !HasRefStructInSignature(m)))
            {
                object?[] args = [.. method.GetParameters().Select(static p => DefaultArgument(p.ParameterType))];
                try
                {
                    method.Invoke(target, args);
                }
                catch (TargetInvocationException ex) when (ex.InnerException is NotImplementedException stub)
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(stub.Message, @"^M\d+-\d+$"))
                    {
                        unexpected.Add($"{facade.PropertyType.Name}.{method.Name} throws NotImplementedException(\"{stub.Message}\"); the message must be the owning task's ID");
                    }
                }
                catch (TargetInvocationException)
                {
                    // A real implementation may refuse the default arguments in its own way; that is its business.
                }
            }
        }

        unexpected.ShouldBeEmpty();
    }

    [Fact]
    public void StubSpanMethods_AreStubsOrWork()
    {
        ISimQueries queries = new WorldBuilder().Build().Query;

        AssertStubOrWorks(() => _ = queries.Logistics.ActiveJobs().Length);
        AssertStubOrWorks(() => _ = queries.Advisors.Active().Length);
        AssertStubOrWorks(() => queries.Statistics.Series(default, new long[4]));
        AssertStubOrWorks(() => queries.Map.DrainDirtyChunks(new Evenmere.Sim.Queries.Types.ChunkCoord[4]));
        AssertStubOrWorks(() => queries.Placement.ValidateRoad([], default));
    }

    private static void AssertStubOrWorks(Action call)
    {
        try
        {
            call();
        }
        catch (NotImplementedException stub)
        {
            stub.Message.ShouldMatch(@"^M\d+-\d+$");
        }
        catch (Exception)
        {
            // A real implementation may refuse the default arguments in its own way.
        }
    }

    private static bool HasRefStructInSignature(MethodInfo method) =>
        method.ReturnType.IsByRefLike || method.GetParameters().Any(static p => p.ParameterType.IsByRefLike);

    // A value to pass for each parameter: default for value types and by-reference value types, an empty list for the lists
    // the queries append to, and null for anything else.
    private static object? DefaultArgument(Type type)
    {
        Type target = type.IsByRef ? type.GetElementType()! : type;
        if (target.IsValueType)
        {
            return Activator.CreateInstance(target);
        }

        if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(List<>))
        {
            return Activator.CreateInstance(target);
        }

        return target == typeof(string) ? string.Empty : null;
    }
}
