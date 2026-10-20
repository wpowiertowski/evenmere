using System.Reflection;

namespace Evenmere.Sim.Tests.Architecture;

/// <summary>
/// Tells the base class library from everything else. "BCL" here means exactly the assemblies that ship in the
/// running .NET runtime's shared framework folder (<c>Microsoft.NETCore.App</c>): <c>System.Runtime</c>,
/// <c>System.Collections</c>, <c>System.Text.Json</c>, <c>Microsoft.CSharp</c> and so on. A name prefix would not do:
/// NuGet packages such as <c>System.IO.Hashing</c> and <c>System.CommandLine</c> start with <c>System.</c>, and
/// <c>Microsoft.*</c> covers <c>Microsoft.Extensions.*</c> and <c>Microsoft.CodeAnalysis.*</c>. Asking the runtime keeps
/// the answer exact, so a package that sneaks into Core is not mistaken for the BCL.
/// </summary>
internal static class FrameworkAssemblies
{
    private static readonly Lazy<IReadOnlySet<string>> _names = new(Load);

    /// <summary>Gets the simple names of the assemblies in the shared framework folder.</summary>
    public static IReadOnlySet<string> Names => _names.Value;

    /// <summary>Tests whether an assembly belongs to the shared framework.</summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns><see langword="true"/> if the runtime ships it.</returns>
    public static bool Contains(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Names.Contains(assembly.GetName().Name ?? string.Empty);
    }

    private static HashSet<string> Load()
    {
        // System.Private.CoreLib is always loaded from the framework folder, so its directory is that folder.
        string? directory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("The location of the base class library is unknown, so the framework assemblies cannot be listed.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(directory, "*.dll"))
        {
            names.Add(Path.GetFileNameWithoutExtension(file));
        }

        return names;
    }
}
