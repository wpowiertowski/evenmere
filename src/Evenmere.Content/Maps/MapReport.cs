using System.Collections.Immutable;
using System.Globalization;

namespace Evenmere.Content.Maps;

/// <summary>
/// Collects the diagnostics of one map load. Every position is 1-based. At most <see cref="MaxPerFile"/> problems are kept
/// per file, so a layer that is wrong everywhere reports its first problems and a count instead of thousands of lines.
/// </summary>
internal sealed class MapReport
{
    /// <summary>The most problems reported for one file.</summary>
    public const int MaxPerFile = 20;

    private readonly List<Entry> _entries = [];

    // Only looked up by file name; the "more problems" lines are produced from the sorted file list, so its order never leaks.
    private readonly Dictionary<string, int> _countByFile = new(StringComparer.Ordinal);

    /// <summary>Gets a value indicating whether an error has been reported.</summary>
    public bool HasErrors { get; private set; }

    /// <summary>Reports an error.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="file">The file relative to its content root, or empty.</param>
    /// <param name="pointer">A JSON pointer inside <c>map.json</c>, or empty.</param>
    /// <param name="line">The 1-based line, or 0 when there is no position.</param>
    /// <param name="column">The 1-based column.</param>
    /// <param name="message">What is wrong.</param>
    public void Error(string code, string file, string pointer, int line, int column, string message) =>
        Add(DiagnosticSeverity.Error, code, file, pointer, line, column, message);

    /// <summary>Reports a warning.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="file">The file relative to its content root, or empty.</param>
    /// <param name="pointer">A JSON pointer inside <c>map.json</c>, or empty.</param>
    /// <param name="line">The 1-based line, or 0 when there is no position.</param>
    /// <param name="column">The 1-based column.</param>
    /// <param name="message">What is wrong.</param>
    public void Warning(string code, string file, string pointer, int line, int column, string message) =>
        Add(DiagnosticSeverity.Warning, code, file, pointer, line, column, message);

    /// <summary>Finishes the report: adds the "more problems" lines and sorts by file, then position.</summary>
    /// <returns>The diagnostics.</returns>
    public ImmutableArray<ContentDiagnostic> Finish()
    {
        var all = new List<Entry>(_entries);
        var files = new List<string>(_countByFile.Keys);
        files.Sort(StringComparer.Ordinal);
        foreach (string file in files)
        {
            int extra = _countByFile[file] - MaxPerFile;
            if (extra > 0)
            {
                string noun = extra == 1 ? "problem" : "problems";
                all.Add(new Entry(
                    new ContentDiagnostic(DiagnosticCodes.Evl001, DiagnosticSeverity.Warning, file, string.Empty, $"{extra.ToString(CultureInfo.InvariantCulture)} more {noun} in this file are not shown"),
                    int.MaxValue,
                    int.MaxValue));
            }
        }

        all.Sort(static (a, b) =>
        {
            int result = string.CompareOrdinal(a.Diagnostic.File, b.Diagnostic.File);
            if (result == 0)
            {
                result = a.Line.CompareTo(b.Line);
            }

            if (result == 0)
            {
                result = a.Column.CompareTo(b.Column);
            }

            return result != 0 ? result : ContentDiagnostic.Compare(a.Diagnostic, b.Diagnostic);
        });

        return [.. all.Select(static e => e.Diagnostic)];
    }

    private void Add(DiagnosticSeverity severity, string code, string file, string pointer, int line, int column, string message)
    {
        if (severity == DiagnosticSeverity.Error)
        {
            HasErrors = true;
        }

        _countByFile.TryGetValue(file, out int seen);
        _countByFile[file] = seen + 1;
        if (seen >= MaxPerFile)
        {
            return;
        }

        string text = line > 0
            ? string.Create(CultureInfo.InvariantCulture, $"line {line}, column {column}: {message}")
            : message;
        _entries.Add(new Entry(new ContentDiagnostic(code, severity, file, pointer, text), line, column));
    }

    private readonly record struct Entry(ContentDiagnostic Diagnostic, int Line, int Column);
}
