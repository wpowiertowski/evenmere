// These types exist only to be reflected over by the architecture rules' own tests: each is a deliberately bad (or
// deliberately clean) example. Their fields are never read or assigned, which the compiler would otherwise warn about.
#pragma warning disable CS0169, CS0414, CS0649, CS8618, IDE0051, IDE0044, IDE0052, CA1823, CA2211

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Numerics;
using Evenmere.Core.Collections;
using Evenmere.Core.Numerics;
using Evenmere.Core.Rng;
using Evenmere.Core.Time;

namespace Evenmere.Sim.Tests.Architecture;

// ---- StateTypeWalker -------------------------------------------------------------------------------------------

/// <summary>A root that reaches its targets in each way the walker must follow.</summary>
internal sealed class FakeWalkRoot
{
    // A static field is not instance state and must not be followed.
    public static readonly FakeWalkStaticTarget StaticField = new();

    private FakeWalkPrivateTarget _hidden = new();

    public FakeWalkPropertyTarget Visible { get; set; } = new();

    // FakeWalkHolder<T> never stores a T, so the target is only reachable through the generic argument.
    public FakeWalkHolder<FakeWalkArgumentTarget> Held { get; set; } = new();

    public FakeWalkElementTarget[] Items { get; set; } = [];

    private FakeWalkNested _nested;

    private struct FakeWalkNested
    {
        public FakeWalkNestedTarget Target;
    }
}

internal sealed class FakeWalkStaticTarget;

internal sealed class FakeWalkPrivateTarget;

internal sealed class FakeWalkPropertyTarget;

internal sealed class FakeWalkHolder<T>;

internal sealed class FakeWalkArgumentTarget;

internal sealed class FakeWalkElementTarget;

internal sealed class FakeWalkNestedTarget;

/// <summary>A base class with a private field, to show that inherited fields are followed.</summary>
internal class FakeWalkBase
{
    private FakeWalkBaseTarget _inherited = new();
}

internal sealed class FakeWalkDerived : FakeWalkBase;

internal sealed class FakeWalkBaseTarget;

/// <summary>Types that refer to each other and to themselves.</summary>
internal sealed class FakeCycleA
{
    public FakeCycleB B { get; set; } = new();
}

internal sealed class FakeCycleB
{
    public FakeCycleA A { get; set; } = new();

    public FakeCycleB[] Self { get; set; } = [];
}

/// <summary>A generic type that makes a bigger closed type at every level, so the walk can never finish.</summary>
internal sealed class FakeRunaway<T>
{
    public FakeRunaway<FakeRunaway<T>>? Next { get; set; }
}

/// <summary>A subtype that a union (built in <see cref="FakeMessagePackTypes"/>) names, holding a double.</summary>
internal sealed class FakeUnionMember
{
    private double _weight;
}

/// <summary>A struct from the base class library that holds floats (<see cref="Vector2"/>).</summary>
internal sealed class FakeBclStructRoot
{
    public Vector2 Position { get; set; }
}

// ---- ForbiddenFieldTypeRules -----------------------------------------------------------------------------------

/// <summary>A state root whose child holds a double in a private field.</summary>
internal sealed class FakeLeakyRoot
{
    public FakeLeakyChild Child { get; set; } = new();
}

internal sealed class FakeLeakyChild
{
    private double _speed;
}

/// <summary>A dictionary hidden in an array of a generic struct.</summary>
internal sealed class FakeArrayRoot
{
    public FakeCell<int>[] Cells { get; set; } = [];
}

internal struct FakeCell<T>
    where T : notnull
{
    private Dictionary<T, int> _lookup;
}

/// <summary>A value that holds a double, stored in an <see cref="IdTable{T}"/>.</summary>
internal sealed class FakeIdTableRoot
{
    public IdTable<FakeBadValue> Table { get; set; } = new();
}

internal struct FakeBadValue
{
    private double _weight;
}

/// <summary>One field of every forbidden kind, each declared directly on the root so its path is <c>FakeForbiddenRoot.Name</c>.</summary>
internal sealed class FakeForbiddenRoot
{
    public float FloatField;
    public double DoubleField;
    public decimal DecimalField;
    public Dictionary<int, int> DictionaryField = [];
    public HashSet<int> HashSetField = [];
    public ConcurrentDictionary<int, int> ConcurrentDictionaryField = new();
    public SortedList<int, int> SortedListField = [];
    public ReadOnlyDictionary<int, int> ReadOnlyDictionaryField = new(new SortedDictionary<int, int>());
    public ImmutableHashSet<int> ImmutableHashSetField = [];
    public IDictionary<int, int> DictionaryInterfaceField;
    public IReadOnlyDictionary<int, int> ReadOnlyDictionaryInterfaceField;
    public ISet<int> SetInterfaceField;
    public Hashtable HashtableField = [];
    public object ObjectField;
    public dynamic DynamicField;
    public double? NullableDoubleField;
    public List<float> ListOfFloatField = [];
}

internal enum FakeColour
{
    Red,
    Green,
}

/// <summary>Everything the rules allow, including the two ordered collections and the Core types.</summary>
internal sealed class FakeAllowedRoot
{
    public bool Flag;
    public int Count;
    public long Total;
    public ulong Seed;
    public string Name = string.Empty;
    public FakeColour Colour;
    public Amount Stock;
    public Ratio Share;
    public Pcg32 Rng;
    public GameClock Clock;
    public int[] Numbers = [];
    public List<int> Numbered = [];
    public (int Left, long Right) Pair;
    public int? Maybe;
    public SortedDictionary<int, FakeAllowedChild> Sorted = [];
    public SortedSet<int> Ordered = [];
    public IdTable<FakeAllowedChild> Table = new();
}

internal sealed class FakeAllowedChild
{
    public int Level;
}

// ---- StaticStateRules ------------------------------------------------------------------------------------------

/// <summary>Static members of every kind: two are mutable and public (the violations), the rest are fine.</summary>
internal static class FakeStatics
{
    public static int MutableField;

    public static string MutableProperty { get; set; } = string.Empty;

    public static readonly int ReadonlyField = 1;

    public const int ConstField = 2;

    public static int GetOnlyProperty { get; } = 3;

    public static int PrivateSetProperty { get; private set; }

    internal static int InternalMutableField;

    private static int _privateMutableField;

    /// <summary>A nested type's statics count too.</summary>
    internal static class Nested
    {
        public static long NestedMutableField;
    }
}

// ---- CommandHandlerRules ---------------------------------------------------------------------------------------

/// <summary>A command nobody registers a handler for.</summary>
internal sealed record FakeCommandWithoutHandler : Command;

/// <summary>A command the tests register a handler for.</summary>
internal sealed record FakeCommandWithHandler : Command;

/// <summary>An abstract command: nobody can send it, so it needs no handler.</summary>
internal abstract record FakeAbstractCommand : Command;

/// <summary>A concrete command that derives from an abstract one.</summary>
internal sealed record FakeDerivedCommand : FakeAbstractCommand;
