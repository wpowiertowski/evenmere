using System.Reflection;
using MessagePack;

namespace Evenmere.Sim.Tests.Architecture;

/// <summary>
/// One hop on the way from the root type to a type: the type that owns the member, and the member's name. Auto-property
/// backing fields show as the property's name; a base type and a <c>[Union]</c> subtype show as <c>&lt;base&gt;</c> and
/// <c>[Union]</c>.
/// </summary>
/// <param name="Owner">The type that declares the member.</param>
/// <param name="Member">The member's name.</param>
internal sealed record StateStep(Type Owner, string Member)
{
    /// <inheritdoc />
    public override string ToString() => TypeNames.Display(Owner) + "." + Member;
}

/// <summary>A type the walker reached, with the shortest path (breadth first) that first reached it.</summary>
/// <param name="Type">The type.</param>
/// <param name="Path">The hops from the root to the member whose declaration first mentioned the type.</param>
/// <param name="Text">The path as text, such as <c>WorldState.Production → ProductionState._foo</c>.</param>
internal sealed record StateTypeNode(Type Type, IReadOnlyList<StateStep> Path, string Text);

/// <summary>
/// One place that mentions types: a field's declared type, the root, a base type or a <c>[Union]</c> subtype. A rule that
/// forbids a type checks every use, so it reports each offending field and not just the first one to reach the type.
/// </summary>
/// <param name="Path">The hops from the root to this use, ending with the member that declares it (empty for the root).</param>
/// <param name="Field">The field, or <see langword="null"/> for the root, a base type and a union subtype.</param>
/// <param name="DeclaredType">The type as written: <c>FakeCell&lt;int&gt;[]</c> for a field of that type.</param>
/// <param name="Types">
/// <paramref name="DeclaredType"/> and every type written inside it: array element types and generic arguments, recursively.
/// </param>
/// <param name="Text">The path as text.</param>
internal sealed record StateTypeUse(IReadOnlyList<StateStep> Path, FieldInfo? Field, Type DeclaredType, IReadOnlyList<Type> Types, string Text);

/// <summary>Everything reachable from a root type, as found by <see cref="StateTypeWalker"/>.</summary>
internal sealed class StateTypeGraph
{
    private readonly Dictionary<Type, StateTypeNode> _byType;

    /// <summary>Initializes a new instance of the <see cref="StateTypeGraph"/> class.</summary>
    /// <param name="root">The type the walk started from.</param>
    /// <param name="nodes">Every reachable type, in discovery order.</param>
    /// <param name="uses">Every place that mentions a type, in discovery order.</param>
    public StateTypeGraph(Type root, IReadOnlyList<StateTypeNode> nodes, IReadOnlyList<StateTypeUse> uses)
    {
        Root = root;
        Nodes = nodes;
        Uses = uses;
        _byType = nodes.ToDictionary(static n => n.Type);
    }

    /// <summary>Gets the type the walk started from.</summary>
    public Type Root { get; }

    /// <summary>Gets every reachable type once, in breadth-first discovery order (the order is the same on every run).</summary>
    public IReadOnlyList<StateTypeNode> Nodes { get; }

    /// <summary>Gets every place that mentions a type: the root, each reachable field, base types and union subtypes.</summary>
    public IReadOnlyList<StateTypeUse> Uses { get; }

    /// <summary>Gets every reachable type, including primitives, arrays and constructed generics.</summary>
    public IEnumerable<Type> Types => Nodes.Select(static n => n.Type);

    /// <summary>Tests whether a type is reachable.</summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> if the walk reached it.</returns>
    public bool Contains(Type type) => _byType.ContainsKey(type);

    /// <summary>Gets the node of a reachable type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The node, with the path that first reached the type.</returns>
    /// <exception cref="KeyNotFoundException">The type is not reachable.</exception>
    public StateTypeNode NodeOf(Type type) => _byType[type];

    /// <summary>
    /// Lists the reachable types an assembly defines, so a caller can ask "is this assembly's state type used somewhere
    /// it should not be" without trawling through primitives and the base class library. Arrays and pointers are
    /// dropped and each constructed generic type is reported as its generic type definition, which is how another
    /// assembly's metadata names it (<c>IdTable&lt;&gt;</c>, not <c>IdTable&lt;ProductionRecord&gt;</c>).
    /// </summary>
    /// <param name="assembly">The assembly whose types to list.</param>
    /// <returns>Distinct type definitions, in ordinal order of their full names.</returns>
    public IReadOnlyList<Type> TypesDeclaredIn(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var found = new HashSet<Type>();
        foreach (Type type in Types)
        {
            if (type.IsArray || type.IsPointer || type.IsByRef || type.IsGenericParameter)
            {
                continue;
            }

            Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            if (definition.Assembly == assembly)
            {
                found.Add(definition);
            }
        }

        return [.. found.OrderBy(static t => t.FullName, StringComparer.Ordinal)];
    }
}

/// <summary>
/// Finds every type that can end up in the world state: it starts at a root type (<c>WorldState</c>) and follows every
/// instance field, recursively. The architecture rules (no floats, no hash-ordered collections, unique MessagePack
/// keys) run over what it finds, and M1-28 reuses it to check that the Godot game assembly never references a state type.
/// </summary>
/// <remarks>
/// <para>What it follows:</para>
/// <list type="bullet">
/// <item>Every instance field, public or private, declared on the type or on a base class. An auto-property's backing
/// field is the field MessagePack reads and writes, and it is followed like any other.</item>
/// <item>The types written inside a field's type: array element types and generic arguments, recursively, so
/// <c>IdTable&lt;ProductionRecord&gt;</c> reaches <c>ProductionRecord</c> and <c>Cell&lt;int&gt;[]</c> reaches
/// <c>Cell&lt;int&gt;</c>. A closed generic type is walked as itself, so a field of type <c>T</c> inside
/// <c>IdTable&lt;X&gt;</c> is seen as <c>X</c>.</item>
/// <item>Nested types, because a private nested struct is only ever reached through a field of that type
/// (<c>IdTable&lt;X&gt;.Slot</c> through <c>_slots</c>). A nested type that no field mentions is not state and is not
/// visited.</item>
/// <item>Base classes, and the subtypes named by <c>[Union]</c> attributes, which MessagePack serialises.</item>
/// </list>
/// <para>What it does not follow into: primitives, enums, <see cref="string"/> and <see cref="decimal"/> (they are
/// recorded, so a rule can see a <see cref="decimal"/>, but not opened), the fields of base class library reference
/// types such as <c>List&lt;T&gt;</c> (only their generic arguments matter), static fields (they are not instance
/// state), and the members of interfaces. It does open base class library <em>structs</em> (<c>Vector2</c>,
/// <c>DateTime</c>, <c>ValueTuple</c>), because their fields are visible state.</para>
/// <para>Core types such as <c>Pcg32</c>, <c>Amount</c> and <c>IdTable&lt;T&gt;</c> are walked like any other type,
/// so a floating-point field added to one of them is caught. Callers that only care about one assembly's types filter
/// with <see cref="StateTypeGraph.TypesDeclaredIn"/>.</para>
/// <para>Cycles are handled: each type is expanded once. A recursive generic type that makes a new closed type at every
/// level (<c>Node&lt;T&gt;</c> with a field of type <c>Node&lt;Node&lt;T&gt;&gt;</c>) never finishes, so the walk stops
/// with an <see cref="InvalidOperationException"/> after <c>maxTypes</c> types.</para>
/// </remarks>
internal static class StateTypeWalker
{
    /// <summary>The default limit on the number of distinct types a walk may reach.</summary>
    public const int DefaultMaxTypes = 4096;

    private const BindingFlags DeclaredInstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private const string BackingFieldSuffix = ">k__BackingField";

    /// <summary>Walks every type reachable from a root type.</summary>
    /// <param name="root">The type to start from, such as <c>typeof(WorldState)</c>.</param>
    /// <param name="maxTypes">The most distinct types to visit before giving up on a runaway generic type.</param>
    /// <returns>The reachable types and the places that mention them.</returns>
    /// <exception cref="InvalidOperationException">More than <paramref name="maxTypes"/> types are reachable.</exception>
    public static StateTypeGraph Walk(Type root, int maxTypes = DefaultMaxTypes)
    {
        ArgumentNullException.ThrowIfNull(root);

        var run = new Run(root, maxTypes);
        run.Use([], null, root);
        while (run.Pending.TryDequeue(out StateTypeNode? node))
        {
            Expand(run, node);
        }

        return new StateTypeGraph(root, run.Nodes, run.Uses);
    }

    private static void Expand(Run run, StateTypeNode node)
    {
        Type type = node.Type;

        // Arrays and generic arguments were found with the field that mentions them, so only types with members are opened.
        if (IsLeaf(type) || type.IsArray || type.IsPointer || type.IsByRef || type.IsFunctionPointer)
        {
            return;
        }

        // A union is usually declared on an interface or abstract class, so subtypes come before the interface cut-off.
        foreach (UnionAttribute union in type.GetCustomAttributes<UnionAttribute>(inherit: false).OrderBy(static u => u.Key))
        {
            if (union.SubType is { } subType)
            {
                run.Use([.. node.Path, new StateStep(type, "[Union]")], null, subType);
            }
        }

        // The base class library's reference types (List<T>, Dictionary<,>) keep their data in private fields that say
        // nothing about our state; their generic arguments already carry everything that matters.
        if (type.IsInterface || (!type.IsValueType && FrameworkAssemblies.Contains(type.Assembly)))
        {
            return;
        }

        Type? baseType = type.BaseType;
        if (baseType is not null && baseType != typeof(object) && baseType != typeof(ValueType) && baseType != typeof(Enum))
        {
            run.Use([.. node.Path, new StateStep(type, "<base>")], null, baseType);
        }

        foreach (FieldInfo field in type.GetFields(DeclaredInstanceFields).OrderBy(static f => f.Name, StringComparer.Ordinal))
        {
            run.Use([.. node.Path, new StateStep(type, MemberName(field))], field, field.FieldType);
        }
    }

    private static bool IsLeaf(Type type) =>
        type.IsPrimitive || type.IsEnum || type.IsGenericParameter || type == typeof(string) || type == typeof(decimal) || type == typeof(void);

    private static string MemberName(FieldInfo field)
    {
        string name = field.Name;
        int end = name.IndexOf(BackingFieldSuffix, StringComparison.Ordinal);
        return name.StartsWith('<') && end > 1 ? name[1..end] : name;
    }

    private static void CollectWrittenTypes(Type type, List<Type> into)
    {
        if (into.Contains(type))
        {
            return;
        }

        into.Add(type);
        if (type.IsArray)
        {
            CollectWrittenTypes(type.GetElementType()!, into);
        }
        else if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments())
            {
                CollectWrittenTypes(argument, into);
            }
        }
    }

    private sealed class Run(Type root, int maxTypes)
    {
        private readonly HashSet<Type> _seen = [];

        public Queue<StateTypeNode> Pending { get; } = new();

        public List<StateTypeNode> Nodes { get; } = [];

        public List<StateTypeUse> Uses { get; } = [];

        public void Use(IReadOnlyList<StateStep> path, FieldInfo? field, Type declared)
        {
            var written = new List<Type>();
            CollectWrittenTypes(declared, written);

            string text = path.Count == 0 ? TypeNames.Display(root) : string.Join(" → ", path);
            Uses.Add(new StateTypeUse(path, field, declared, written, text));

            foreach (Type type in written)
            {
                if (!_seen.Add(type))
                {
                    continue;
                }

                if (_seen.Count > maxTypes)
                {
                    throw new InvalidOperationException(
                        $"More than {maxTypes} types are reachable from {TypeNames.Display(root)}; a recursive generic type that makes "
                        + $"a new closed type at every level would explain it. The walk was at {text}.");
                }

                var node = new StateTypeNode(type, path, text);
                Nodes.Add(node);
                Pending.Enqueue(node);
            }
        }
    }
}
