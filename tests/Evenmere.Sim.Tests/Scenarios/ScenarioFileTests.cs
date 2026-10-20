namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>
/// Runs every <c>*.json</c> scenario under <c>tests/fixtures/scenarios</c>, in subfolders too, each as its own test case
/// named after the file. Adding a scenario means adding a file; nothing here changes. Scenarios that live in the
/// <c>harness/</c> subfolder use the harness's own test-only commands (see <c>HarnessCommands.cs</c>); a scenario that
/// is meant to fail must not be committed here, since every file here must pass. The harness's own tests write the files
/// they need to a temporary folder instead.
/// </summary>
public sealed class ScenarioFileTests(ITestOutputHelper output)
{
    /// <summary>Gets one row per scenario file, named by its path below <c>tests/fixtures/scenarios</c> (for example <c>harness/grant_goods.json</c>).</summary>
    /// <returns>The rows.</returns>
    public static IEnumerable<TheoryDataRow<string>> ScenarioFiles() =>
        FindScenarioFiles().Select(static name => new TheoryDataRow<string>(name) { TestDisplayName = name });

    [Theory]
    [MemberData(nameof(ScenarioFiles))]
    public void ScenarioFile_Run_MeetsItsExpectations(string scenario)
    {
        string path = Path.Combine(ScenarioPaths.ScenarioDirectory, scenario);

        ScenarioResult result = ScenarioRunner.RunFile(path, ScenarioRunOptions.FromEnvironment());

        if (result.GoldenUpdated)
        {
            // Visible in the test output and, with diagnostics on, in the run log.
            output.WriteLine(result.Report);
            TestContext.Current.SendDiagnosticMessage(result.Report);
        }

        if (!result.Passed)
        {
            Assert.Fail(result.Report);
        }
    }

    [Fact]
    public void ScenarioFiles_InTheRepository_IncludeTheCommittedSamples()
    {
        string[] names = [.. FindScenarioFiles()];

        names.ShouldContain("empty_world_10k.json");
        names.ShouldContain(static name => name.StartsWith("harness/", StringComparison.Ordinal));
        names.ShouldBe([.. names.Order(StringComparer.Ordinal)], "discovery lists files in a stable order");
    }

    private static List<string> FindScenarioFiles()
    {
        string root = ScenarioPaths.ScenarioDirectory;
        var names = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            names.Add(Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'));
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }
}
