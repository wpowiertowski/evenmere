using Evenmere.Content.Maps;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Sim.Tests.MapGeneration.Guarantee;

/// <summary>The start-repair contract: repaired islands satisfy the predicate without touching elevation or rivers,
/// and the repair changes something (it only runs when the unrepaired island fails).</summary>
public sealed class StartRepairTests
{
    [Theory]
    [InlineData(1, 256)]
    [InlineData(2, 256)]
    [InlineData(1, 320)]
    public void Generate_PreservesElevationAndRivers(int seed, int size)
    {
        MapDef island = GeneratedIslands.Get(seed, size);
        int effective = IslandGenerator.EffectiveSeed(seed, island.SubSeed);
        GeneratedHeightmap fresh = IslandGenerator.GenerateTerrain(effective, size, GeneratedIslandFixture.Content);

        // Regions and discoveries never touch these layers; neither may the repair.
        island.Elevation.AsSpan().SequenceEqual(fresh.Elevation).ShouldBeTrue();
        island.RiverWidth.AsSpan().SequenceEqual(fresh.RiverWidth).ShouldBeTrue();
    }

    [Fact]
    public void Generate_RepairedSeed_ChangesALayer()
    {
        // Seed 1 at 256 fails every sub-seed without repair (brute-forced in round 1), so generating it must repair
        // something: at least one of the terrain, deposit or fertility layers differs from the fresh terrain.
        MapDef island = GeneratedIslands.Get(1, 256);
        int effective = IslandGenerator.EffectiveSeed(1, island.SubSeed);
        GeneratedHeightmap fresh = IslandGenerator.GenerateTerrain(effective, 256, GeneratedIslandFixture.Content);

        bool sameTerrain = island.Terrain.AsSpan().SequenceEqual(fresh.Terrain);
        bool sameDeposit = island.Deposit.AsSpan().SequenceEqual(fresh.Deposit);
        bool sameFertility = System.Runtime.InteropServices.MemoryMarshal.AsBytes(island.Fertility.AsSpan())
            .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(fresh.Fertility.AsSpan()));
        (sameTerrain && sameDeposit && sameFertility).ShouldBeFalse("seed 1 at 256 needs the start repair");
    }
}
