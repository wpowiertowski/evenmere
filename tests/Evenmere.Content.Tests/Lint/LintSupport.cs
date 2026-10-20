using System.Collections.Immutable;
using Evenmere.Content.Lint;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

/// <summary>One diagnostic a test expects: its identity (code, severity, file, pointer) and a fragment of its message.</summary>
/// <param name="Code">The rule code.</param>
/// <param name="Severity">The severity.</param>
/// <param name="File">The file the diagnostic is reported in.</param>
/// <param name="Pointer">The JSON pointer inside the file.</param>
/// <param name="MessagePart">Text the message must contain, so two diagnostics at one spot can be told apart.</param>
internal sealed record Expected(string Code, DiagnosticSeverity Severity, string File, string Pointer, string MessagePart)
{
    /// <summary>Expects an error.</summary>
    public static Expected Error(string code, string file, string pointer, string messagePart) =>
        new(code, DiagnosticSeverity.Error, file, pointer, messagePart);

    /// <summary>Expects a warning.</summary>
    public static Expected Warning(string code, string file, string pointer, string messagePart) =>
        new(code, DiagnosticSeverity.Warning, file, pointer, messagePart);
}

/// <summary>Finds and lints the committed lint fixtures in <c>tests/fixtures/content/lint</c>.</summary>
internal static class LintFixtures
{
    /// <summary>Gets the folder that holds one folder per fixture.</summary>
    public static string Root => Path.Combine(TestPaths.RepoRoot, "tests", "fixtures", "content", "lint");

    /// <summary>Gets the names of every fixture folder, in ordinal order.</summary>
    public static ImmutableArray<string> Names =>
        [.. Directory.GetDirectories(Root).Select(static path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    /// <summary>Gets the path of one fixture pack.</summary>
    public static string PathOf(string name) => Path.Combine(Root, name);

    /// <summary>Loads and lints one fixture pack.</summary>
    public static ImmutableArray<ContentDiagnostic> Lint(string name) => ContentLinter.Run(ContentLoader.Load(PathOf(name)));
}

/// <summary>Helpers for linting small edits of the minimal pack without committing a fixture for each.</summary>
internal static class LintVariants
{
    /// <summary>Copies the minimal pack, applies the edit, and lints the result.</summary>
    public static ImmutableArray<ContentDiagnostic> Lint(Action<TempContent> edit)
    {
        using var content = TempContent.FromMinimal();
        edit(content);
        return ContentLinter.Run(content.Load());
    }
}

/// <summary>Assertions on lists of diagnostics.</summary>
internal static class LintAssertions
{
    /// <summary>
    /// Asserts that the diagnostics are exactly the expected ones, in the linter's sort order: same count, and at each
    /// position the same code, severity, file and pointer, with the message containing the expected text.
    /// </summary>
    public static void ShouldBeExactly(this ImmutableArray<ContentDiagnostic> actual, params Expected[] expected)
    {
        string dump = DiagnosticAssertions.Dump(actual);
        actual.Length.ShouldBe(expected.Length, "Wrong number of diagnostics.\n" + dump);
        for (int i = 0; i < expected.Length; i++)
        {
            ContentDiagnostic got = actual[i];
            Expected want = expected[i];
            string where = $"Diagnostic {i} of:\n{dump}\n";
            got.Code.ShouldBe(want.Code, where);
            got.Severity.ShouldBe(want.Severity, where);
            got.File.ShouldBe(want.File, where);
            got.JsonPointer.ShouldBe(want.Pointer, where);
            got.Message.ShouldContain(want.MessagePart, Case.Sensitive, where);
        }
    }

    /// <summary>Gets the diagnostics with one code.</summary>
    public static ImmutableArray<ContentDiagnostic> WithCode(this ImmutableArray<ContentDiagnostic> diagnostics, string code) =>
        [.. diagnostics.Where(d => d.Code == code)];

    /// <summary>Asserts that exactly one diagnostic has the code, file and pointer, and returns it.</summary>
    public static ContentDiagnostic ShouldHaveOne(this ImmutableArray<ContentDiagnostic> diagnostics, string code, string file, string pointer) =>
        diagnostics.ShouldHaveSingleMatch(code, file, pointer);
}
