using Evenmere.Content;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.State;
using Evenmere.Sim.Tests.Fixtures;
using Evenmere.Sim.Tests.Integration;

namespace Evenmere.Sim.Tests.Persistence;

/// <summary>
/// M2-15: the version-1 to version-2 migration (ADR-0018). The frozen v1 slice fixture loads through the migration
/// chain and continues to the same hash as an uninterrupted run, and every fixture in <c>tests/fixtures/saves/</c>
/// steps to the current version. Older fixtures pin migration, not bytes: their stored v1 hashes cannot equal the
/// migrated state, since migration adds keys by design.
/// </summary>
public sealed class Migration0001To0002Tests
{
    [Fact]
    public void Chain_ShipsSingleStep_From1To2()
    {
        SaveFormat.CurrentVersion.ShouldBe((ushort)2);

        IMigration step = MigrationChain.All.ShouldHaveSingleItem();
        step.FromVersion.ShouldBe(1);
        step.ToVersion.ShouldBe(2);
    }

    [Fact]
    public void Apply_ShortArrays_GrowsToContentKeepingValues()
    {
        ContentDb content = SiteFinder.Content;

        // A version-1 state whose arrays predate content additions: one slot each, so the migration must grow them.
        var state = WorldState.CreateNew(content, 7);
        state.Research.EnsureSlots(1);
        state.Culture.EnsureSlots(1, 0);
        state.Advisors.EnsureSlots(1, 1);
        state.Research.Progress[0] = Amount.FromMilli(123);
        state.Research.Done = [1UL];
        state.Culture.Progress[0] = Amount.FromMilli(45);
        state.Advisors.GoodStreak[0] = 2;

        WorldState migrated = new Migration_0001_to_0002().Apply(state, content);

        migrated.ShouldBeSameAs(state);
        migrated.Research.Progress.Length.ShouldBe(content.Techs.Count);
        migrated.Research.Progress[0].ShouldBe(Amount.FromMilli(123));
        migrated.Research.Done[0].ShouldBe(1UL);
        migrated.Culture.Progress.Length.ShouldBe(content.Civics.Count);
        migrated.Culture.Progress[0].ShouldBe(Amount.FromMilli(45));
        migrated.Advisors.GoodStreak.Length.ShouldBe(content.Goods.Count);
        migrated.Advisors.GoodStreak[0].ShouldBe(2);
    }

    [Fact]
    public void Apply_MissingMember_ThrowsInvalidData()
    {
        var state = WorldState.CreateNew(SiteFinder.Content, 7);
        // A damaged file can deserialise a nil member; the suppression builds that shape on purpose.
        state.Research = null!;

        Should.Throw<InvalidDataException>(() => new Migration_0001_to_0002().Apply(state, SiteFinder.Content));
    }

    [Fact]
    public void FixtureSaves_AllMigrateToCurrentVersion()
    {
        string directory = Path.Combine(WorldBuilder.RepoRoot, "tests", "fixtures", "saves");
        string[] fixtures = Directory.GetFiles(directory, "*.evsave");
        fixtures.ShouldNotBeEmpty();

        foreach (string fixture in fixtures)
        {
            ushort version = SaveFixtureFormat.ReadEnvelopeVersion(fixture);
            version.ShouldBeLessThanOrEqualTo(SaveFormat.CurrentVersion);

            using FileStream stream = File.OpenRead(fixture);
            (ISimulation simulation, SaveReport report) = SaveGame.Read(stream, SiteFinder.Content);
            simulation.CurrentTick.ShouldBeGreaterThan(0);

            if (version < SaveFormat.CurrentVersion)
            {
                // An older save cannot pin the migrated bytes, so it pins the migration instead: loading must lose
                // nothing. Remaps from content growth are fine; ruins and drops are not.
                report.IsLossless.ShouldBeTrue();
            }
            else
            {
                stream.Position = 0;
                SaveGame.ReadStateHash(stream).ShouldBe(simulation.ComputeStateHash());
            }

            // The v1 slice fixture migrates losslessly: grown content remaps its tables by name, and nothing is
            // ruined or dropped.
            if (Path.GetFileName(fixture) == "v1-slice.evsave")
            {
                version.ShouldBe((ushort)1);
                simulation.CurrentTick.ShouldBe(20_000);
                report.IsLossless.ShouldBeTrue();
            }

            if (Path.GetFileName(fixture) == "slice-20000.evsave")
            {
                simulation.CurrentTick.ShouldBe(20_000);
                report.IsClean.ShouldBeTrue();
            }
        }
    }

    [Fact]
    public void V1Slice_MigratesAndContinues_ToSameHashAsUninterrupted()
    {
        ContentDb content = SiteFinder.Content;
        (ISimulation resumed, _) = ReadFixture(content);
        resumed.CurrentTick.ShouldBe(20_000);
        for (long tick = resumed.CurrentTick + 1; tick <= 40_000; tick++)
        {
            resumed.Tick();
        }

        Simulation direct = SliceHarness.RunTo(40_000);
        resumed.ComputeStateHash().ShouldBe(direct.ComputeStateHash());
    }

    private static (ISimulation Simulation, SaveReport Report) ReadFixture(ContentDb content)
    {
        using FileStream stream = File.OpenRead(SaveFixtureTests.FixturePath);
        return SaveGame.Read(stream, content);
    }
}
