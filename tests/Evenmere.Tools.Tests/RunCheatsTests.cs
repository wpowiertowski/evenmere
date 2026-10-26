using System.Text.RegularExpressions;

namespace Evenmere.Tools.Tests;

/// <summary>M2-35: <c>evenmere run --cheats</c> enables cheat commands for the new game, so replays of logs that contain cheat commands match.</summary>
public sealed class RunCheatsTests
{
    [Fact]
    public void Run_CheatsFlag_RunsAndPrintsHash()
    {
        (int exit, string output) = Run(["run", "--ticks", "10", "--seed", "42", "--cheats"]);

        exit.ShouldBe(ExitCodes.Success);
        Regex.IsMatch(output.Trim(), @"^hash=[0-9a-f]{16}$", RegexOptions.Multiline).ShouldBeTrue();
    }

    [Fact]
    public void Run_CheatsFlag_LeavesTheHashUnchanged()
    {
        // CheatsUsed is host tooling, never simulation state: the flag must not move the hash.
        (_, string plain) = Run(["run", "--ticks", "10", "--seed", "42"]);
        (_, string cheats) = Run(["run", "--ticks", "10", "--seed", "42", "--cheats"]);

        cheats.ShouldBe(plain);
    }

    private static (int Exit, string Output) Run(string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        string repo = RepoRoot();
        string minimal = Path.Combine(repo, "tests", "fixtures", "content", "minimal");
        string[] full = [.. args[..1], "--content", minimal, .. args[1..]];
        int exit = EvenmereCli.Run(full, output, error, repo, repo);
        return (exit, output.ToString());
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Evenmere.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Evenmere.sln was not found above " + AppContext.BaseDirectory);
    }
}
