using System.Collections.Immutable;

namespace Evenmere.Content.Tests.Support;

/// <summary>Assertion helpers that print every diagnostic when a test fails, so a miss is easy to diagnose.</summary>
internal static class DiagnosticAssertions
{
    /// <summary>Lists diagnostics one per line.</summary>
    public static string Dump(IEnumerable<ContentDiagnostic> diagnostics)
    {
        string text = string.Join(Environment.NewLine, diagnostics.Select(static d => d.ToString()));
        return text.Length == 0 ? "(no diagnostics)" : text;
    }

    /// <summary>Asserts that the load produced a database and no diagnostics at all.</summary>
    public static ContentDb ShouldLoadCleanly(this ContentLoadResult result)
    {
        result.Diagnostics.ShouldBeEmpty(Dump(result.Diagnostics));
        return result.Db ?? throw new ShouldAssertException("The load reported no diagnostics but produced no database.");
    }

    /// <summary>Asserts that loading failed and that exactly one diagnostic has the given code, file and pointer; returns it.</summary>
    public static ContentDiagnostic ShouldFailWith(this ContentLoadResult result, string code, string file, string pointer)
    {
        result.Db.ShouldBeNull("Loading should have failed.\n" + Dump(result.Diagnostics));
        return result.Diagnostics.ShouldHaveSingleMatch(code, file, pointer);
    }

    /// <summary>Asserts that exactly one diagnostic has the given code, file and pointer; returns it.</summary>
    public static ContentDiagnostic ShouldHaveSingleMatch(this ImmutableArray<ContentDiagnostic> diagnostics, string code, string file, string pointer)
    {
        ContentDiagnostic[] matches = [.. diagnostics.Where(d => d.Code == code && d.File == file && d.JsonPointer == pointer)];
        matches.Length.ShouldBe(1, $"Expected one {code} at {file}#{pointer}.\n" + Dump(diagnostics));
        return matches[0];
    }
}
