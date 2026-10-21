using System.Collections.Immutable;

namespace Evenmere.Content.Maps;

/// <summary>The outcome of <see cref="MapLoader.Load"/>: the map if it is sound, and always the diagnostics.</summary>
public sealed class MapLoadResult
{
    internal MapLoadResult(MapDef? map, ImmutableArray<ContentDiagnostic> diagnostics)
    {
        Map = map;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the map, or <see langword="null"/> if any error was reported (warnings alone do not stop it).</summary>
    public MapDef? Map { get; }

    /// <summary>Gets every problem found, sorted by file, then by position in the file.</summary>
    public ImmutableArray<ContentDiagnostic> Diagnostics { get; }

    /// <summary>Gets a value indicating whether any diagnostic is an error.</summary>
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>Gets the map, or throws if the map had errors. For hosts that cannot continue without it.</summary>
    /// <returns>The map.</returns>
    /// <exception cref="MapLoadException">The map had errors.</exception>
    public MapDef GetMapOrThrow() => Map ?? throw new MapLoadException(Diagnostics);
}

/// <summary>Thrown by <see cref="MapLoadResult.GetMapOrThrow"/> when a map did not load.</summary>
public sealed class MapLoadException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="MapLoadException"/> class.</summary>
    /// <param name="diagnostics">The problems that stopped loading.</param>
    public MapLoadException(ImmutableArray<ContentDiagnostic> diagnostics)
        : base(BuildMessage(diagnostics))
    {
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the problems that stopped loading.</summary>
    public ImmutableArray<ContentDiagnostic> Diagnostics { get; }

    private static string BuildMessage(ImmutableArray<ContentDiagnostic> diagnostics)
    {
        int errors = diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Error);
        string first = diagnostics.FirstOrDefault(static d => d.Severity == DiagnosticSeverity.Error)?.ToString() ?? "no details";
        return $"The map failed to load with {errors} error(s); first: {first}";
    }
}
