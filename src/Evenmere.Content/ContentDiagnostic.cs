namespace Evenmere.Content;

/// <summary>How serious a <see cref="ContentDiagnostic"/> is. Any <see cref="Error"/> stops a <see cref="ContentDb"/> from being built.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Suspicious but harmless; the content still loads.</summary>
    Warning = 0,

    /// <summary>The content is wrong; no <see cref="ContentDb"/> is produced.</summary>
    Error = 1,
}

/// <summary>The diagnostic codes the loader emits. The linter (M0-04) adds the rest of the EVL range.</summary>
public static class DiagnosticCodes
{
    /// <summary>EVL001: a structural or shape error (bad JSON, wrong type, unknown property, bad quantity and so on).</summary>
    public const string Evl001 = "EVL001";

    /// <summary>EVL002: a reference does not resolve to a defined item.</summary>
    public const string Evl002 = "EVL002";
}

/// <summary>One problem found while loading content. The loader collects these instead of throwing.</summary>
/// <param name="Code">The rule code, such as <see cref="DiagnosticCodes.Evl001"/>.</param>
/// <param name="Severity">How serious the problem is.</param>
/// <param name="File">The file, relative to its content root, with forward slashes; empty when no single file is to blame.</param>
/// <param name="JsonPointer">An RFC 6901 JSON pointer to the offending value inside the file; empty for the whole file.</param>
/// <param name="Message">A human-readable explanation.</param>
public sealed record ContentDiagnostic(string Code, DiagnosticSeverity Severity, string File, string JsonPointer, string Message)
{
    /// <summary>Orders diagnostics by code, file, pointer and message (all ordinal), so output is stable.</summary>
    /// <param name="left">The first diagnostic.</param>
    /// <param name="right">The second diagnostic.</param>
    /// <returns>A negative, zero or positive number, as for <see cref="IComparable{T}"/>.</returns>
    public static int Compare(ContentDiagnostic left, ContentDiagnostic right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        int result = string.CompareOrdinal(left.Code, right.Code);
        if (result != 0)
        {
            return result;
        }

        result = string.CompareOrdinal(left.File, right.File);
        if (result != 0)
        {
            return result;
        }

        result = string.CompareOrdinal(left.JsonPointer, right.JsonPointer);
        return result != 0 ? result : string.CompareOrdinal(left.Message, right.Message);
    }

    /// <summary>Formats the diagnostic on one line: <c>EVL002 error buildings/bakery.json#/recipe: message</c>.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        string severity = Severity == DiagnosticSeverity.Error ? "error" : "warning";
        string location = File.Length == 0 ? "(content)" : JsonPointer.Length == 0 ? File : $"{File}#{JsonPointer}";
        return $"{Code} {severity} {location}: {Message}";
    }
}
