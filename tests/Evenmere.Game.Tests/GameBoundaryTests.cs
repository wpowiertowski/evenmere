using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Core.Rng;
using Evenmere.Game.Host;
using Evenmere.Sim.State;
using Evenmere.Sim.Tests.Architecture;

namespace Evenmere.Game.Tests;

/// <summary>
/// ADR-0006: view code must use the query interfaces, never concrete state types. The game assembly
/// is scanned with <see cref="MetadataReader"/>: its type definitions (base types), field types,
/// method signatures and IL member and type references must never mention the world state or any
/// type reachable from it. The game test project references the game project, so the built assembly
/// is reached through <c>typeof(SimHost).Assembly</c> instead of a fragile build-output search;
/// the reachability walker is shared by linking its source file.
/// </summary>
public sealed class GameBoundaryTests
{
    [Fact]
    public void Game_NeverReferencesSimState()
    {
        IReadOnlyList<string> violations = GameBoundaryChecker.Check(typeof(SimHost).Assembly);
        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ForbiddenSet_ContainsWorldStateAndSimStateAndCoreRandom()
    {
        // The policy is only meaningful if the forbidden set really holds what it says.
        IReadOnlySet<string> forbidden = GameBoundaryChecker.ForbiddenSimpleNames;
        forbidden.ShouldContain(nameof(WorldState));
        forbidden.ShouldContain("ProductionState");
        forbidden.ShouldContain(nameof(Pcg32));
        forbidden.ShouldContain(nameof(EntityIdAllocator));
        forbidden.ShouldContain("IdTable`1");
    }
}

/// <summary>
/// The boundary rule as data plus the metadata scan. Public so the failing message can show the
/// forbidden set in tests.
/// </summary>
internal static class GameBoundaryChecker
{
    private static readonly Lazy<HashSet<string>> _forbidden = new(BuildForbidden);

    /// <summary>Gets the simple names that must never appear in the game assembly.</summary>
    internal static IReadOnlySet<string> ForbiddenSimpleNames => _forbidden.Value;

    /// <summary>Checks a game assembly.</summary>
    /// <param name="gameAssembly">The built game assembly.</param>
    /// <returns>One line per offending use, each naming the game member and the forbidden type.</returns>
    internal static IReadOnlyList<string> Check(Assembly gameAssembly)
    {
        ArgumentNullException.ThrowIfNull(gameAssembly);
        string? location = gameAssembly.Location;
        if (string.IsNullOrEmpty(location) || !File.Exists(location))
        {
            throw new InvalidOperationException("The game assembly has no location; build the game first.");
        }

        using FileStream stream = File.OpenRead(location);
        using var reader = new PEReader(stream);
        MetadataReader metadata = reader.GetMetadataReader();
        var violations = new List<string>();
        CheckTypeDefinitions(metadata, violations);
        CheckFieldsAndMethods(metadata, violations);
        CheckIl(metadata, reader, violations);
        return violations;
    }

    private static HashSet<string> BuildForbidden()
    {
        // The walker finds every Sim type reachable from the state, including the query value types
        // (TileCoord, ChunkCoord, Rotation and friends) that the state reuses and the game must also use
        // through queries. Those live in Evenmere.Sim.Queries and stay allowed; the ban is on state
        // containers (WorldState, the systems' *State classes) plus the three Core random/container types.
        Assembly simAssembly = typeof(WorldState).Assembly;
        IReadOnlyList<Type> simState = StateTypeWalker.Walk(typeof(WorldState)).TypesDeclaredIn(simAssembly);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Type type in simState)
        {
            if (type.Namespace?.StartsWith("Evenmere.Sim.Queries", StringComparison.Ordinal) == true)
            {
                continue;
            }

            names.Add(type.Name);
        }

        names.Add(nameof(Pcg32));
        names.Add(nameof(EntityIdAllocator));
        names.Add(typeof(IdTable<>).Name);
        return names;
    }

    private static bool IsForbidden(string? name) => name is not null && _forbidden.Value.Contains(name);

    private static void CheckTypeDefinitions(MetadataReader reader, List<string> violations)
    {
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            string gameType = reader.GetString(definition.Name);
            if (gameType.StartsWith('<'))
            {
                continue;
            }

            string gameFull = FullTypeName(reader, handle);
            if (IsForbidden(gameType))
            {
                continue;
            }

            foreach (EntityHandle baseHandle in BaseAndInterfaces(reader, definition))
            {
                string? forbidden = ForbiddenNameOf(reader, baseHandle);
                if (forbidden is not null)
                {
                    violations.Add($"{gameFull} inherits forbidden {forbidden} (via type definition).");
                }
            }
        }
    }

    private static IEnumerable<EntityHandle> BaseAndInterfaces(MetadataReader reader, TypeDefinition definition)
    {
        if (!definition.BaseType.IsNil)
        {
            yield return definition.BaseType;
        }

        foreach (InterfaceImplementationHandle implementation in definition.GetInterfaceImplementations())
        {
            yield return reader.GetInterfaceImplementation(implementation).Interface;
        }
    }

    private static void CheckFieldsAndMethods(MetadataReader reader, List<string> violations)
    {
        var provider = new CollectingProvider(reader);
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            string gameType = FullTypeName(reader, typeHandle);
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);

            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
                string fieldName = reader.GetString(field.Name);
                provider.Found.Clear();
                try
                {
                    field.DecodeSignature(provider, genericContext: null);
                }
                catch (Exception exception) when (exception is BadImageFormatException or ArgumentException)
                {
                    violations.Add($"{gameType}.{fieldName} has a field signature that could not be decoded (via field type).");
                    continue;
                }

                foreach (string forbidden in provider.Found.Where(IsForbidden).Distinct(StringComparer.Ordinal).OrderBy(static n => n, StringComparer.Ordinal))
                {
                    violations.Add($"{gameType}.{fieldName} uses forbidden {forbidden} (via field type).");
                }
            }

            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                string methodName = reader.GetString(method.Name);
                provider.Found.Clear();
                try
                {
                    method.DecodeSignature(provider, genericContext: null);
                }
                catch (Exception exception) when (exception is BadImageFormatException or ArgumentException)
                {
                    violations.Add($"{gameType}.{methodName} has a method signature that could not be decoded (via method signature).");
                    continue;
                }

                foreach (string forbidden in provider.Found.Where(IsForbidden).Distinct(StringComparer.Ordinal).OrderBy(static n => n, StringComparer.Ordinal))
                {
                    violations.Add($"{gameType}.{methodName} uses forbidden {forbidden} (via method signature).");
                }
            }
        }
    }

    private static void CheckIl(MetadataReader reader, PEReader peReader, List<string> violations)
    {
        Dictionary<int, OperandKind> operands = BuildOperandTable();
        foreach (MethodDefinitionHandle methodHandle in reader.MethodDefinitions)
        {
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            MethodBodyBlock body;
            try
            {
                body = peReader.GetMethodBody(method.RelativeVirtualAddress);
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            byte[]? il = body.GetILBytes();
            if (il is null)
            {
                continue;
            }

            string gameMethod = FullMethodName(reader, methodHandle, method);
            int position = 0;
            while (position < il.Length)
            {
                byte first = il[position++];
                int key;
                if (first == 0xFE)
                {
                    if (position >= il.Length)
                    {
                        break;
                    }

                    key = 0xFE00 | il[position++];
                }
                else
                {
                    key = first;
                }

                if (!operands.TryGetValue(key, out OperandKind kind))
                {
                    break;
                }

                switch (kind)
                {
                    case OperandKind.None:
                        break;
                    case OperandKind.Byte:
                        position += 1;
                        break;
                    case OperandKind.Short:
                        position += 2;
                        break;
                    case OperandKind.Int:
                        if (position + 4 > il.Length)
                        {
                            position = il.Length;
                            break;
                        }

                        int token = il[position] | (il[position + 1] << 8) | (il[position + 2] << 16) | (il[position + 3] << 24);
                        position += 4;
                        CheckToken(reader, token, gameMethod, violations);
                        break;
                    case OperandKind.Long:
                        position += 8;
                        break;
                    case OperandKind.Single:
                        position += 4;
                        break;
                    case OperandKind.Double:
                        position += 8;
                        break;
                    case OperandKind.Branch1:
                        position += 1;
                        break;
                    case OperandKind.Branch4:
                        position += 4;
                        break;
                    case OperandKind.Switch:
                        if (position + 4 > il.Length)
                        {
                            position = il.Length;
                            break;
                        }

                        int count = il[position] | (il[position + 1] << 8) | (il[position + 2] << 16) | (il[position + 3] << 24);
                        position += 4 + (count * 4);
                        break;
                    case OperandKind.Var1:
                        position += 1;
                        break;
                    case OperandKind.Var2:
                        position += 2;
                        break;
                }

                if (position > il.Length)
                {
                    break;
                }
            }
        }
    }

    private static void CheckToken(MetadataReader reader, int token, string gameMethod, List<string> violations)
    {
        EntityHandle handle;
        try
        {
            handle = (EntityHandle)MetadataTokens.Handle(token);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidCastException)
        {
            return;
        }

        string? forbidden = ForbiddenNameOf(reader, handle);
        if (forbidden is not null)
        {
            violations.Add($"{gameMethod} uses forbidden {forbidden} (via IL).");
        }
    }

    private static string? ForbiddenNameOf(MetadataReader reader, EntityHandle handle)
    {
        string? name = handle.Kind switch
        {
            HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Name),
            HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name),
            HandleKind.TypeSpecification => GenericNameOf(reader, (TypeSpecificationHandle)handle),
            HandleKind.MemberReference => ParentNameOf(reader, reader.GetMemberReference((MemberReferenceHandle)handle).Parent),
            HandleKind.MethodSpecification => ParentNameOf(reader, reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method),
            HandleKind.MethodDefinition => reader.GetString(reader.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
            HandleKind.FieldDefinition => ParentFieldTypeOf(reader, (FieldDefinitionHandle)handle),
            _ => null,
        };

        if (name is null)
        {
            return null;
        }

        // A generic instantiation reports as "IdTable`1<ProductionRecord>"; the forbidden set holds "IdTable`1".
        string simple = name.IndexOf('<', StringComparison.Ordinal) is int open and >= 0 ? name[..open] : name;
        // A nested type reports as "Outer+Inner"; either part being forbidden is enough.
        foreach (string part in simple.Split('+'))
        {
            if (IsForbidden(part))
            {
                return part;
            }
        }

        return null;
    }

    private static string? ParentNameOf(MetadataReader reader, EntityHandle parent)
    {
        return parent.Kind switch
        {
            HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name),
            HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)parent).Name),
            HandleKind.TypeSpecification => GenericNameOf(reader, (TypeSpecificationHandle)parent),
            HandleKind.MethodDefinition => ParentNameOf(reader, reader.GetMethodDefinition((MethodDefinitionHandle)parent).GetDeclaringType()),
            HandleKind.FieldDefinition => ParentNameOf(reader, reader.GetFieldDefinition((FieldDefinitionHandle)parent).GetDeclaringType()),
            HandleKind.MemberReference => ParentNameOf(reader, reader.GetMemberReference((MemberReferenceHandle)parent).Parent),
            HandleKind.MethodSpecification => ParentNameOf(reader, reader.GetMethodSpecification((MethodSpecificationHandle)parent).Method),
            _ => null,
        };
    }

    private static string? ParentFieldTypeOf(MetadataReader reader, FieldDefinitionHandle handle)
    {
        try
        {
            TypeDefinition containing = reader.GetTypeDefinition(reader.GetFieldDefinition(handle).GetDeclaringType());
            return reader.GetString(containing.Name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? GenericNameOf(MetadataReader reader, TypeSpecificationHandle handle)
    {
        try
        {
            var provider = new CollectingProvider(reader);
            reader.GetTypeSpecification(handle).DecodeSignature(provider, genericContext: null);
            return provider.Found.FirstOrDefault();
        }
        catch (Exception exception) when (exception is BadImageFormatException or ArgumentException)
        {
            return null;
        }
    }

    private static string FullTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition definition = reader.GetTypeDefinition(handle);
        string name = reader.GetString(definition.Name);
        string ns = reader.GetString(definition.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static string FullMethodName(MetadataReader reader, MethodDefinitionHandle handle, MethodDefinition method)
    {
        string methodName = reader.GetString(method.Name);
        string typeName;
        try
        {
            typeName = FullTypeName(reader, method.GetDeclaringType());
        }
        catch (ArgumentException)
        {
            typeName = "?";
        }

        return typeName + "." + methodName;
    }

    private enum OperandKind
    {
        None,
        Byte,
        Short,
        Int,
        Long,
        Single,
        Double,
        Branch1,
        Branch4,
        Switch,
        Var1,
        Var2,
    }

    private static Dictionary<int, OperandKind> BuildOperandTable()
    {
        var table = new Dictionary<int, OperandKind>();
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode code)
            {
                continue;
            }

            int key = code.Size == 1 ? code.Value & 0xFF : code.Value;
            OperandKind kind = code.OperandType switch
            {
                OperandType.InlineNone => OperandKind.None,
                OperandType.ShortInlineI => OperandKind.Byte,
                OperandType.ShortInlineBrTarget => OperandKind.Branch1,
                OperandType.InlineSwitch => OperandKind.Switch,
                OperandType.InlineI or OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok or OperandType.InlineSig or OperandType.InlineString => OperandKind.Int,
                OperandType.InlineI8 => OperandKind.Long,
                OperandType.InlineR => OperandKind.Double,
                OperandType.ShortInlineR => OperandKind.Single,
                OperandType.InlineBrTarget => OperandKind.Branch4,
                OperandType.ShortInlineVar => OperandKind.Var1,
                OperandType.InlineVar => OperandKind.Var2,
                _ => OperandKind.None,
            };

            // ldarg.s/stloc.s use ShortInlineVar but appear as ShortInlineI in some runtimes; Var1 is right for all ShortInlineVar.
            if (code.OperandType == OperandType.ShortInlineVar)
            {
                kind = OperandKind.Var1;
            }

            table[key] = kind;
        }

        return table;
    }

    private sealed class CollectingProvider(MetadataReader reader) : ISignatureTypeProvider<string, object?>
    {
        private readonly MetadataReader _reader = reader;

        /// <summary>Gets every type name the last decoded signature mentioned.</summary>
        internal HashSet<string> Found { get; } = new(StringComparer.Ordinal);

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            string name = reader.GetString(reader.GetTypeDefinition(handle).Name);
            Found.Add(name);
            return name;
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            string name = reader.GetString(reader.GetTypeReference(handle).Name);
            Found.Add(name);
            return name;
        }

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            var inner = new CollectingProvider(reader);
            try
            {
                reader.GetTypeSpecification(handle).DecodeSignature(inner, genericContext);
                foreach (string found in inner.Found)
                {
                    Found.Add(found);
                }

                return inner.Found.FirstOrDefault() ?? "spec";
            }
            catch (Exception exception) when (exception is BadImageFormatException or ArgumentException)
            {
                return "spec";
            }
        }

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        {
            Found.Add(genericType);
            foreach (string argument in typeArguments)
            {
                Found.Add(argument);
            }

            return genericType + "<" + string.Join(",", typeArguments) + ">";
        }

        public string GetArrayType(string elementType, ArrayShape shape)
        {
            Found.Add(elementType);
            return elementType + "[]";
        }

        public string GetByReferenceType(string elementType)
        {
            Found.Add(elementType);
            return elementType + "&";
        }

        public string GetPointerType(string elementType)
        {
            Found.Add(elementType);
            return elementType + "*";
        }

        public string GetSZArrayType(string elementType)
        {
            Found.Add(elementType);
            return elementType + "[]";
        }

        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";

        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        {
            Found.Add(modifier);
            Found.Add(unmodifiedType);
            return unmodifiedType;
        }

        public string GetPinnedType(string elementType)
        {
            Found.Add(elementType);
            return elementType;
        }
    }
}
