using System.Reflection;
using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Contracts;

/// <summary>
/// Builds sample commands by reflection, so the contract tests cover every command the simulation declares, including the
/// ones later tasks add. A sample is made from the constructor's parameter types; a type the generator does not know
/// fails the test with a message that says where to teach it, which is the point: a new kind of command parameter also
/// needs a scenario-file converter (see <c>ScenarioJson</c>).
/// </summary>
internal static class CommandSamples
{
    /// <summary>Finds every concrete command the simulation declares, in ordinal order of their names.</summary>
    /// <returns>The command types.</returns>
    public static IReadOnlyList<Type> AllCommandTypes() =>
        [.. typeof(Command).Assembly.GetTypes()
            .Where(static t => t is { IsAbstract: false, ContainsGenericParameters: false } && t.IsSubclassOf(typeof(Command)))
            .OrderBy(static t => t.Name, StringComparer.Ordinal)];

    /// <summary>Creates a sample of a command. Two samples of the same variant are equal in value but share no array.</summary>
    /// <param name="commandType">The command record.</param>
    /// <param name="variant">0 or 1; the two variants differ in every parameter.</param>
    /// <param name="content">
    /// When given, content IDs are made to exist in it (so the sample can be written to JSON and read back); when
    /// <see langword="null"/> they are small raw numbers, which keeps the two variants different.
    /// </param>
    /// <returns>The command.</returns>
    public static Command Create(Type commandType, int variant, ContentDb? content = null)
    {
        ConstructorInfo constructor = commandType.GetConstructors().Single();
        object?[] args = [.. constructor.GetParameters().Select(p => Value(p.ParameterType, variant, content))];
        return (Command)constructor.Invoke(args);
    }

    private static object Value(Type type, int variant, ContentDb? content)
    {
        if (type == typeof(int))
        {
            return 3 + variant;
        }

        if (type == typeof(bool))
        {
            return variant == 0;
        }

        if (type == typeof(Rotation))
        {
            return (Rotation)(1 + variant);
        }

        if (type == typeof(EntityId))
        {
            return new EntityId(5 + variant);
        }

        if (type == typeof(TileCoord))
        {
            return new TileCoord(1 + variant, 2);
        }

        if (type == typeof(Amount))
        {
            return Amount.FromUnits(2 + variant);
        }

        if (type.IsArray)
        {
            Type element = type.GetElementType()!;
            var array = Array.CreateInstance(element, 2);
            array.SetValue(Value(element, variant, content), 0);
            array.SetValue(Value(element, variant + 2, content), 1);
            return array;
        }

        if (IsContentId(type))
        {
            return Activator.CreateInstance(type, ContentIdValue(type, 1 + variant, content))!;
        }

        throw new NotSupportedException(
            $"CommandSamples cannot make a {type.Name}. Teach Value() about it, and add a converter to ScenarioJson if scenario files need to write it.");
    }

    // With content, wrap the raw number into the table so the ID names a real item.
    private static ushort ContentIdValue(Type idType, int raw, ContentDb? content)
    {
        if (content is null)
        {
            return (ushort)raw;
        }

        PropertyInfo table = typeof(ContentDb).GetProperties().Single(
            p => p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(ContentTable<,>)
                && p.PropertyType.GetGenericArguments()[0] == idType);
        int count = (int)table.PropertyType.GetProperty(nameof(ContentTable<GoodId, object>.Count))!.GetValue(table.GetValue(content))!;
        return (ushort)(raw % count);
    }

    private static bool IsContentId(Type type) =>
        type.IsValueType && type.GetInterfaces().Any(
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IContentId<>) && i.GetGenericArguments()[0] == type);
}
