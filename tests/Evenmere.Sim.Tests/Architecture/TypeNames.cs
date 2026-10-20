namespace Evenmere.Sim.Tests.Architecture;

/// <summary>
/// Writes types the way a reader would type them (<c>Dictionary&lt;int, int&gt;</c>, <c>FakeCell&lt;int&gt;[]</c>,
/// <c>IdTable&lt;X&gt;.Slot</c>), without namespaces, so a violation path stays short enough to read in a failing test.
/// </summary>
internal static class TypeNames
{
    private static readonly Dictionary<Type, string> _keywords = new()
    {
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(sbyte)] = "sbyte",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(nint)] = "nint",
        [typeof(nuint)] = "nuint",
        [typeof(char)] = "char",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal",
        [typeof(string)] = "string",
        [typeof(object)] = "object",
        [typeof(void)] = "void",
    };

    /// <summary>Gets the display name of a type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The name, with generic arguments, array brackets and enclosing types.</returns>
    public static string Display(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (_keywords.TryGetValue(type, out string? keyword))
        {
            return keyword;
        }

        if (type.IsArray)
        {
            return Display(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (type.IsPointer)
        {
            return Display(type.GetElementType()!) + "*";
        }

        if (type.IsByRef)
        {
            return Display(type.GetElementType()!) + "&";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        // A nested type of a generic type carries the outer type's arguments too; only the tail belongs to this type.
        Type[] allArguments = type.IsGenericType ? type.GetGenericArguments() : [];
        Type? outer = type.DeclaringType;
        int outerCount = outer is { IsGenericType: true } ? outer.GetGenericArguments().Length : 0;
        Type[] ownArguments = allArguments[Math.Min(outerCount, allArguments.Length)..];

        string name = StripArity(type.Name);
        if (ownArguments.Length > 0)
        {
            name += "<" + string.Join(", ", ownArguments.Select(Display)) + ">";
        }

        return outer is null ? name : Display(outer) + "." + name;
    }

    private static string StripArity(string name)
    {
        int tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }
}
