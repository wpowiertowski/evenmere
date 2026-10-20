using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using MessagePack;

namespace Evenmere.Sim.Tests.Architecture;

/// <summary>How a <see cref="FakeMember"/> is declared.</summary>
internal enum FakeMemberKind
{
    /// <summary>A public auto-property with a public getter and setter.</summary>
    Property,

    /// <summary>A public property with a getter and no setter.</summary>
    GetOnlyProperty,

    /// <summary>A public property whose setter is private.</summary>
    PrivateSetterProperty,

    /// <summary>A public instance field.</summary>
    PublicField,

    /// <summary>A private instance field.</summary>
    PrivateField,

    /// <summary>A private auto-property.</summary>
    PrivateProperty,
}

/// <summary>The MessagePack attribute on a <see cref="FakeMember"/>.</summary>
/// <param name="IntKey">The value of <c>[Key(int)]</c>, if the member has one.</param>
/// <param name="StringKey">The value of <c>[Key(string)]</c>, if the member has one.</param>
/// <param name="Ignore">Whether the member has <c>[IgnoreMember]</c>.</param>
internal readonly record struct FakeKey(int? IntKey = null, string? StringKey = null, bool Ignore = false)
{
    /// <summary>Gets "no attribute at all".</summary>
    public static FakeKey None => default;

    /// <summary>Gets <c>[IgnoreMember]</c>.</summary>
    public static FakeKey Ignored => new(Ignore: true);

    /// <summary>Gets <c>[Key(n)]</c>.</summary>
    /// <param name="key">The integer key.</param>
    /// <returns>The attribute description.</returns>
    public static FakeKey Of(int key) => new(IntKey: key);

    /// <summary>Gets <c>[Key("name")]</c>.</summary>
    /// <param name="key">The string key.</param>
    /// <returns>The attribute description.</returns>
    public static FakeKey Of(string key) => new(StringKey: key);
}

/// <summary>One member of a type built by <see cref="FakeMessagePackTypes"/>.</summary>
/// <param name="Name">The member's name.</param>
/// <param name="Type">The member's type.</param>
/// <param name="Kind">How it is declared.</param>
/// <param name="Key">The MessagePack attribute on it.</param>
internal sealed record FakeMember(string Name, Type Type, FakeMemberKind Kind, FakeKey Key)
{
    /// <summary>Describes a public auto-property of type <see cref="int"/>.</summary>
    /// <param name="name">The property name.</param>
    /// <param name="key">The MessagePack attribute.</param>
    /// <returns>The member.</returns>
    public static FakeMember IntProperty(string name, FakeKey key) => new(name, typeof(int), FakeMemberKind.Property, key);

    /// <summary>Describes a member of type <see cref="int"/> with the given declaration.</summary>
    /// <param name="name">The member name.</param>
    /// <param name="kind">How it is declared.</param>
    /// <param name="key">The MessagePack attribute.</param>
    /// <returns>The member.</returns>
    public static FakeMember Int(string name, FakeMemberKind kind, FakeKey key) => new(name, typeof(int), kind, key);
}

/// <summary>
/// Builds <c>[MessagePackObject]</c> types at run time. The deliberately bad MessagePack types cannot be written as C#
/// in the test assembly: the MessagePack source generator and analyzers reach this project through the Sim reference, so
/// any <c>[MessagePackObject]</c> class in it makes the build generate a second <c>GeneratedMessagePackResolver</c>
/// (CS0436), and a duplicate or mixed-type <c>[Key]</c> crashes the generator (CS8785). Emitting the types sidesteps
/// both, and they look to reflection like compiled ones: public types with compiler-generated backing fields named
/// <c>&lt;Name&gt;k__BackingField</c>.
/// </summary>
internal static class FakeMessagePackTypes
{
    private static readonly ModuleBuilder _module = AssemblyBuilder
        .DefineDynamicAssembly(new AssemblyName("Evenmere.Sim.Tests.Architecture.FakeMessagePackTypes"), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("FakeMessagePackTypes");

    // Test classes run in parallel and share the one dynamic module.
    private static readonly Lock _gate = new();

    /// <summary>Builds a public sealed class marked <c>[MessagePackObject]</c>.</summary>
    /// <param name="name">A name unique among the types built so far.</param>
    /// <param name="members">The members to declare.</param>
    /// <param name="keyAsPropertyName">Whether to use <c>[MessagePackObject(keyAsPropertyName: true)]</c>.</param>
    /// <returns>The built type.</returns>
    public static Type Define(string name, IReadOnlyList<FakeMember> members, bool keyAsPropertyName = false) =>
        Build(name, members, marked: true, keyAsPropertyName);

    /// <summary>Builds a public sealed class without <c>[MessagePackObject]</c>.</summary>
    /// <param name="name">A name unique among the types built so far.</param>
    /// <param name="members">The members to declare.</param>
    /// <returns>The built type.</returns>
    public static Type DefineUnmarked(string name, IReadOnlyList<FakeMember> members) =>
        Build(name, members, marked: false, keyAsPropertyName: false);

    /// <summary>Builds a public interface carrying one <c>[Union(i, subType)]</c> per subtype, the way MessagePack declares a polymorphic member.</summary>
    /// <param name="name">A name unique among the types built so far.</param>
    /// <param name="subTypes">The subtypes, which get the keys 0, 1, 2, …</param>
    /// <returns>The built interface.</returns>
    public static Type DefineUnionInterface(string name, IReadOnlyList<Type> subTypes)
    {
        lock (_gate)
        {
            return BuildUnionInterface(name, subTypes);
        }
    }

    private static Type BuildUnionInterface(string name, IReadOnlyList<Type> subTypes)
    {
        TypeBuilder type = _module.DefineType(name, TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        ConstructorInfo union = typeof(UnionAttribute).GetConstructor([typeof(int), typeof(Type)])!;
        for (int key = 0; key < subTypes.Count; key++)
        {
            type.SetCustomAttribute(new CustomAttributeBuilder(union, [key, subTypes[key]]));
        }

        return type.CreateType();
    }

    private static Type Build(string name, IReadOnlyList<FakeMember> members, bool marked, bool keyAsPropertyName)
    {
        lock (_gate)
        {
            return BuildType(name, members, marked, keyAsPropertyName);
        }
    }

    private static Type BuildType(string name, IReadOnlyList<FakeMember> members, bool marked, bool keyAsPropertyName)
    {
        TypeBuilder type = _module.DefineType(name, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed, typeof(object));
        type.DefineDefaultConstructor(MethodAttributes.Public);

        if (marked)
        {
            ConstructorInfo marker = typeof(MessagePackObjectAttribute).GetConstructor([typeof(bool)])!;
            type.SetCustomAttribute(new CustomAttributeBuilder(marker, [keyAsPropertyName]));
        }

        foreach (FakeMember member in members)
        {
            switch (member.Kind)
            {
                case FakeMemberKind.PublicField:
                    ApplyKey(type.DefineField(member.Name, member.Type, FieldAttributes.Public).SetCustomAttribute, member.Key);
                    break;
                case FakeMemberKind.PrivateField:
                    ApplyKey(type.DefineField(member.Name, member.Type, FieldAttributes.Private).SetCustomAttribute, member.Key);
                    break;
                case FakeMemberKind.Property:
                    DefineProperty(type, member, getterPublic: true, setterPublic: true, hasSetter: true);
                    break;
                case FakeMemberKind.GetOnlyProperty:
                    DefineProperty(type, member, getterPublic: true, setterPublic: false, hasSetter: false);
                    break;
                case FakeMemberKind.PrivateSetterProperty:
                    DefineProperty(type, member, getterPublic: true, setterPublic: false, hasSetter: true);
                    break;
                case FakeMemberKind.PrivateProperty:
                    DefineProperty(type, member, getterPublic: false, setterPublic: false, hasSetter: true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(members), member.Kind, "Unknown member kind.");
            }
        }

        return type.CreateType();
    }

    private static void DefineProperty(TypeBuilder type, FakeMember member, bool getterPublic, bool setterPublic, bool hasSetter)
    {
        // Like the compiler's auto-property: a private, compiler-generated backing field behind accessor methods.
        FieldBuilder backing = type.DefineField($"<{member.Name}>k__BackingField", member.Type, FieldAttributes.Private);
        backing.SetCustomAttribute(new CustomAttributeBuilder(typeof(CompilerGeneratedAttribute).GetConstructor(Type.EmptyTypes)!, []));

        PropertyBuilder property = type.DefineProperty(member.Name, PropertyAttributes.None, member.Type, null);

        MethodAttributes getAttributes = (getterPublic ? MethodAttributes.Public : MethodAttributes.Private) | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
        MethodBuilder getter = type.DefineMethod("get_" + member.Name, getAttributes, member.Type, Type.EmptyTypes);
        ILGenerator il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, backing);
        il.Emit(OpCodes.Ret);
        property.SetGetMethod(getter);

        if (hasSetter)
        {
            MethodAttributes setAttributes = (setterPublic ? MethodAttributes.Public : MethodAttributes.Private) | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
            MethodBuilder setter = type.DefineMethod("set_" + member.Name, setAttributes, typeof(void), [member.Type]);
            il = setter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, backing);
            il.Emit(OpCodes.Ret);
            property.SetSetMethod(setter);
        }

        ApplyKey(property.SetCustomAttribute, member.Key);
    }

    private static void ApplyKey(Action<CustomAttributeBuilder> apply, FakeKey key)
    {
        if (key.IntKey is { } intKey)
        {
            apply(new CustomAttributeBuilder(typeof(KeyAttribute).GetConstructor([typeof(int)])!, [intKey]));
        }
        else if (key.StringKey is { } stringKey)
        {
            apply(new CustomAttributeBuilder(typeof(KeyAttribute).GetConstructor([typeof(string)])!, [stringKey]));
        }

        if (key.Ignore)
        {
            apply(new CustomAttributeBuilder(typeof(IgnoreMemberAttribute).GetConstructor(Type.EmptyTypes)!, []));
        }
    }
}
