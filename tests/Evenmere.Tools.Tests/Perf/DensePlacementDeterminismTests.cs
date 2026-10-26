using Evenmere.Sim;
using Evenmere.Tools.Perf;

namespace Evenmere.Tools.Tests.Perf;

/// <summary>
/// The dense-placement generator is deterministic: the same plan always settles the same town.
/// A small plan (256 island, dozens of houses) builds in seconds, so the test runs it twice and
/// compares both the state hashes and the written save bytes.
/// </summary>
public sealed class DensePlacementDeterminismTests
{
    private static readonly DensePlacementPlan _smallPlan = new(
        Seed: 7,
        MapSize: 256,
        Houses: 40,
        Storehouses: 3,
        Markets: 4,
        Woodcutters: 2,
        Sawpits: 2,
        TimberGrant: 800,
        FishGrant: 800,
        WoodGrant: 800,
        SettleTicks: 240);

    [Fact]
    public void BuildCustom_TwiceSamePlan_SameHashAndSameSaveBytes()
    {
        (string hash, byte[] save, int residents) = BuildInScratch("first");
        (string otherHash, byte[] otherSave, int otherResidents) = BuildInScratch("second");

        otherHash.ShouldBe(hash);
        otherSave.ShouldBe(save);
        residents.ShouldBeGreaterThan(0);
        otherResidents.ShouldBe(residents);
    }

    private static (string Hash, byte[] Save, int Residents) BuildInScratch(string name)
    {
        string scratch = Path.Combine(Path.GetTempPath(), $"evenmere-test-{name}-{Guid.NewGuid():N}");
        try
        {
            (Simulation simulation, DensePlacementReport report) = DensePlacement.BuildCustom(
                BotWorld.Content, scratch, _smallPlan, "test_small", flattenHalf: 40);
            string path = Path.Combine(scratch, "test.evsave");
            DensePlacement.WriteSave(simulation, BotWorld.Content, "test_small", path);
            return (report.FinalHash, File.ReadAllBytes(path), report.FinalResidents);
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
