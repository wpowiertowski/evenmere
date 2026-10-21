using Evenmere.Content;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Tests.Fixtures;
using Evenmere.Sim.Tests.Integration;
namespace Evenmere.Sim.Tests.Persistence;

/// <summary>
/// The committed save fixture: <c>tests/fixtures/saves/v1-slice.evsave</c>, generated from the slice_villagers
/// commands on the starter island and continued to tick 20,000. Regenerate it with
/// <c>EVENMERE_UPDATE_GOLDEN=1 dotnet test</c>, the same variable as the golden updater, and commit the rewritten file.
/// Every test that loads a slice save reads this fixture, so they all share one expensive setup.
/// </summary>
public sealed class SaveFixtureTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="SaveFixtureTests"/> class.</summary>
    /// <param name="output">The test output.</param>
    public SaveFixtureTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Gets the fixture path.</summary>
    public static string FixturePath => Path.Combine(WorldBuilder.RepoRoot, "tests", "fixtures", "saves", "v1-slice.evsave");

    [Fact]
    public void LoadV1SliceEvsave_HashMatchesStoredHash()
    {
        if (UpdateRequested && !File.Exists(FixturePath))
        {
            Regenerate();
        }

        if (!File.Exists(FixturePath))
        {
            Assert.Fail($"The save fixture is missing: run with EVENMERE_UPDATE_GOLDEN=1 to generate {FixturePath}.");
        }

        ContentDb content = SiteFinder.Content;
        using FileStream stream = File.OpenRead(FixturePath);
        (ISimulation simulation, SaveReport report) = SaveGame.Read(stream, content);

        stream.Position = 0;
        SaveGame.ReadStateHash(stream).ShouldBe(simulation.ComputeStateHash());
        simulation.CurrentTick.ShouldBe(20_000);
        report.IsClean.ShouldBeTrue();
        _output.WriteLine($"fixture tick={simulation.CurrentTick} hash={simulation.ComputeStateHash():x16}");
    }

    private static bool UpdateRequested =>
        string.Equals(Environment.GetEnvironmentVariable("EVENMERE_UPDATE_GOLDEN"), "1", StringComparison.Ordinal);

    private static void Regenerate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FixturePath)!);
        Simulation simulation = SliceHarness.RunTo(20_000);
        using FileStream stream = File.Create(FixturePath);
        SaveGame.Write(stream, simulation, new SaveMeta(SiteFinder.Content, "fixture", "starter"));
    }
}
