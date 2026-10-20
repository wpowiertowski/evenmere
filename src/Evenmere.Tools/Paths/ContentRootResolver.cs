using System.Collections.Immutable;

namespace Evenmere.Tools.Paths;

/// <summary>
/// Chooses the content roots a command loads. Roots given with <c>--content</c> win, in argument order, and relative ones
/// resolve against the current directory. Without any, the default follows <c>scripts/godot-smoke.sh</c> and the game's
/// <c>ContentPaths</c>: <c>&lt;repo&gt;/content</c> once <c>content/eras</c> exists (M1-02), otherwise the minimal fixture pack.
/// </summary>
internal static class ContentRootResolver
{
    private const string MinimalFixture = "tests/fixtures/content/minimal";

    /// <summary>Resolves the roots to load.</summary>
    /// <param name="requested">The values of <c>--content</c>, in argument order; empty if the option was not given.</param>
    /// <param name="environment">The directories to resolve against.</param>
    /// <param name="roots">The absolute roots in load order, when resolution succeeded.</param>
    /// <param name="description">One line per root for stderr, or the reason resolution failed.</param>
    /// <returns><see langword="true"/> if there is at least one root to load.</returns>
    public static bool TryResolve(
        IReadOnlyList<string> requested,
        ToolEnvironment environment,
        out ImmutableArray<string> roots,
        out ImmutableArray<string> description)
    {
        ImmutableArray<string>.Builder resolved = ImmutableArray.CreateBuilder<string>();
        ImmutableArray<string>.Builder lines = ImmutableArray.CreateBuilder<string>();

        if (requested.Count > 0)
        {
            foreach (string path in requested)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return Fail("--content needs a directory, not an empty value.", out roots, out description);
                }

                string full = Path.GetFullPath(path, environment.CurrentDirectory);
                resolved.Add(full);
                lines.Add($"content: {full}");
            }
        }
        else
        {
            string? repository = RepoLocator.FindRoot(environment);
            if (repository is null)
            {
                return Fail(
                    $"cannot find {RepoLocator.SolutionFileName} above '{environment.CurrentDirectory}' or '{environment.ExecutableDirectory}', so there is no default content root; pass --content <dir>.",
                    out roots,
                    out description);
            }

            string content = Path.Combine(repository, "content");
            if (Directory.Exists(Path.Combine(content, "eras")))
            {
                resolved.Add(content);
                lines.Add($"content: {content} (default: repository content/)");
            }
            else
            {
                string fixture = Path.Combine(repository, MinimalFixture);
                resolved.Add(fixture);
                lines.Add($"content: {fixture} (default: minimal fixture pack; content/eras does not exist yet)");
            }
        }

        roots = resolved.ToImmutable();
        description = lines.ToImmutable();
        return true;
    }

    private static bool Fail(string reason, out ImmutableArray<string> roots, out ImmutableArray<string> description)
    {
        roots = [];
        description = [reason];
        return false;
    }
}
