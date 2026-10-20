using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Evenmere.Content.Lint;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

/// <summary>Keeps the authoring guide honest: its example files must be valid, and it must mention every rule.</summary>
public sealed partial class ReadmeTests
{
    private static string ReadmePath => Path.Combine(TestPaths.RepoRoot, "content", "README.md");

    [GeneratedRegex(@"`content/(?<path>[a-z0-9_/\-]+\.json)`", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ExampleCaption();

    [Fact]
    public void Readme_EveryExampleFile_IsValidForItsSchemaAndTheLoader()
    {
        ImmutableArray<(string Path, string Json)> examples = ExampleFiles();
        examples.Length.ShouldBeGreaterThanOrEqualTo(4, "the guide shows a tech, two buildings and a locale file");

        foreach ((string path, string json) in examples)
        {
            string[] segments = path.Split('/');
            if (segments[0] == "locale")
            {
                using var locale = JsonDocument.Parse(json);
                locale.RootElement.ValueKind.ShouldBe(JsonValueKind.Object, path);
                locale.RootElement.EnumerateObject().ShouldAllBe(static p => p.Value.ValueKind == JsonValueKind.String, path);
                continue;
            }

            ContentTypes.TryFromDirectory(segments[0], out _).ShouldBeTrue($"{path} is not in a content folder.");
            using var document = JsonDocument.Parse(json);
            document.RootElement.GetProperty("id").GetString().ShouldBe(Path.GetFileNameWithoutExtension(path), $"{path}: the id must equal the file name.");

            using var content = TempContent.FromMinimal();
            content.Write(path, json);
            ContentLoadResult result = content.Load();

            // The examples name items the minimal pack does not have (EVL002), but their shape must be right.
            result.Diagnostics.Where(d => d.File == path && d.Code == DiagnosticCodes.Evl001).ShouldBeEmpty(path + "\n" + DiagnosticAssertions.Dump(result.Diagnostics));
            ContentLinter.ValidateSchemas(result.Raw).Where(d => d.File == path).ShouldBeEmpty(path);
        }
    }

    [Fact]
    public void Readme_MentionsEveryRuleAndTheReservedCoinKey()
    {
        string text = File.ReadAllText(ReadmePath);

        foreach (string code in new[] { "EVL001", "EVL002", "EVL003", "EVL004", "EVL005", "EVL006", "EVL007", "EVL008" })
        {
            text.ShouldContain(code);
        }

        text.ShouldContain("reserved");
        text.ShouldContain("three decimal places");
    }

    [Fact]
    public void Readme_LocaleKeyTypesAreTheSingularNamesTheLinterChecks()
    {
        string text = File.ReadAllText(ReadmePath);

        foreach (ContentType type in ContentTypes.All.Where(static t => t != ContentType.Rules))
        {
            text.ShouldContain($"`{type.SingularName()}`", Case.Sensitive, $"The guide should list the locale prefix '{type.SingularName()}'.");
        }
    }

    /// <summary>Finds every fenced json block whose preceding line names the file it is an example of, such as `content/techs/milling.json`.</summary>
    private static ImmutableArray<(string Path, string Json)> ExampleFiles()
    {
        string[] lines = File.ReadAllLines(ReadmePath);
        ImmutableArray<(string, string)>.Builder examples = ImmutableArray.CreateBuilder<(string, string)>();
        string caption = string.Empty;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("```json", StringComparison.Ordinal))
            {
                int end = i + 1;
                while (!lines[end].StartsWith("```", StringComparison.Ordinal))
                {
                    end++;
                }

                Match match = ExampleCaption().Match(caption);
                if (match.Success)
                {
                    examples.Add((match.Groups["path"].Value, string.Join('\n', lines[(i + 1)..end])));
                }

                i = end;
            }
            else if (lines[i].Length > 0)
            {
                caption = lines[i];
            }
        }

        return examples.ToImmutable();
    }
}
