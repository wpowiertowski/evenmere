using System.Reflection;

namespace Evenmere.Sim.Tests.Architecture;

/// <summary>
/// What an assembly may reference (ADR-0002). A reference is judged by the referenced assembly's simple name, in this
/// order: a banned prefix always fails; a framework assembly or an exact name on the allow-list passes; any other
/// <c>Evenmere.*</c> assembly fails (layers may not reach upward or sideways) unless <see cref="AllowAnyEvenmere"/> is
/// set; and anything else passes only if <see cref="AllowOtherPackages"/> is set.
/// </summary>
internal sealed record AssemblyReferencePolicy
{
    /// <summary>Gets the simple names of the base class library assemblies.</summary>
    public required IReadOnlySet<string> Framework { get; init; }

    /// <summary>Gets the exact names allowed on top of <see cref="Framework"/>: layer assemblies and permitted packages.</summary>
    public IReadOnlySet<string> Allowed { get; init; } = new HashSet<string>();

    /// <summary>Gets the name prefixes that are never allowed, such as <c>Godot</c> or <c>Evenmere.Game</c>.</summary>
    public IReadOnlyList<string> BannedPrefixes { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether any <c>Evenmere.*</c> assembly may be referenced. It is off for the layer policies,
    /// which name the layers they may use, and on for <see cref="AssemblyReferenceRules.NoEngine"/>, which only keeps the
    /// engine out.
    /// </summary>
    public bool AllowAnyEvenmere { get; init; }

    /// <summary>
    /// Gets a value indicating whether a package that is neither in <see cref="Framework"/>, on the allow-list nor an
    /// <c>Evenmere.*</c> layer may be referenced. Core says no (ADR-0002: BCL plus <c>System.IO.Hashing</c> only);
    /// Content and Sim say yes, because the ADR lets them use packages (JsonSchema.Net, MessagePack) and only the layers
    /// and the banned names are pinned down.
    /// </summary>
    public bool AllowOtherPackages { get; init; }
}

/// <summary>The reference rules of ADR-0002 as data, and the check that applies them to an assembly.</summary>
internal static class AssemblyReferenceRules
{
    private const string GodotPrefix = "Godot";
    private const string GamePrefix = "Evenmere.Game";
    private const string CoreName = "Evenmere.Core";
    private const string ContentName = "Evenmere.Content";

    /// <summary>
    /// Gets the policy of <c>Evenmere.Core</c>: the base class library and <c>System.IO.Hashing</c>, nothing else. In
    /// particular no other Evenmere assembly, no Godot, and no MessagePack or JSON schema package.
    /// </summary>
    public static AssemblyReferencePolicy Core { get; } = new()
    {
        Framework = FrameworkAssemblies.Names,
        Allowed = new HashSet<string> { "System.IO.Hashing" },
        BannedPrefixes = [GodotPrefix, GamePrefix, "MessagePack", "JsonSchema"],
        AllowOtherPackages = false,
    };

    /// <summary>Gets the policy of <c>Evenmere.Content</c>: Core is the only Evenmere assembly, and no Godot or MessagePack.</summary>
    public static AssemblyReferencePolicy Content { get; } = new()
    {
        Framework = FrameworkAssemblies.Names,
        Allowed = new HashSet<string> { CoreName },
        BannedPrefixes = [GodotPrefix, GamePrefix, "MessagePack"],
        AllowOtherPackages = true,
    };

    /// <summary>Gets the policy of <c>Evenmere.Sim</c>: Content and Core are the only Evenmere assemblies, and no Godot.</summary>
    public static AssemblyReferencePolicy Sim { get; } = new()
    {
        Framework = FrameworkAssemblies.Names,
        Allowed = new HashSet<string> { CoreName, ContentName },
        BannedPrefixes = [GodotPrefix, GamePrefix],
        AllowOtherPackages = true,
    };

    /// <summary>Gets the one rule that holds for every non-game assembly: no <c>Godot*</c> and no <c>Evenmere.Game*</c>.</summary>
    public static AssemblyReferencePolicy NoEngine { get; } = new()
    {
        Framework = FrameworkAssemblies.Names,
        BannedPrefixes = [GodotPrefix, GamePrefix],
        AllowAnyEvenmere = true,
        AllowOtherPackages = true,
    };

    /// <summary>Checks the assemblies an assembly references (its compiled references, which the compiler limits to those it uses).</summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <param name="policy">The rules to apply.</param>
    /// <returns>One line per forbidden reference, in ordinal order of the referenced name; empty when the references are allowed.</returns>
    public static IReadOnlyList<string> Check(Assembly assembly, AssemblyReferencePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(policy);

        return CheckNames(assembly.GetName().Name ?? "(unnamed)", ReferencedNames(assembly), policy);
    }

    /// <summary>
    /// Applies a policy to a list of referenced assembly names. <see cref="Check(Assembly, AssemblyReferencePolicy)"/>
    /// feeds it a real assembly's references; tests feed it names no real assembly in this repository references, such as
    /// <c>GodotSharp</c>, to show the rules would fire.
    /// </summary>
    /// <param name="owner">The referencing assembly's name, for the message.</param>
    /// <param name="referenced">The simple names of the assemblies it references.</param>
    /// <param name="policy">The rules to apply.</param>
    /// <returns>One line per forbidden reference, in the order given; empty when the references are allowed.</returns>
    public static IReadOnlyList<string> CheckNames(string owner, IEnumerable<string> referenced, AssemblyReferencePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(referenced);
        ArgumentNullException.ThrowIfNull(policy);

        var violations = new List<string>();
        foreach (string name in referenced)
        {
            string? problem = ProblemWith(name, policy);
            if (problem is not null)
            {
                violations.Add($"{owner} references {name}: {problem}");
            }
        }

        return violations;
    }

    /// <summary>Lists the simple names of the assemblies an assembly references.</summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>The names, in ordinal order.</returns>
    public static IReadOnlyList<string> ReferencedNames(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return [.. assembly.GetReferencedAssemblies()
            .Select(static a => a.Name ?? string.Empty)
            .Order(StringComparer.Ordinal)];
    }

    private static string? ProblemWith(string name, AssemblyReferencePolicy policy)
    {
        foreach (string prefix in policy.BannedPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return $"names starting with '{prefix}' are banned in this layer";
            }
        }

        if (policy.Framework.Contains(name) || policy.Allowed.Contains(name))
        {
            return null;
        }

        if (name.StartsWith("Evenmere.", StringComparison.Ordinal))
        {
            return policy.AllowAnyEvenmere ? null : "this layer may not reference that Evenmere assembly (ADR-0002)";
        }

        return policy.AllowOtherPackages ? null : "it is neither part of the base class library nor on the allow-list (ADR-0002)";
    }
}
