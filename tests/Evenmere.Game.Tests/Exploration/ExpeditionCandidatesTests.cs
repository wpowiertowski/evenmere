using Evenmere.Game.Ui.Exploration;

namespace Evenmere.Game.Tests;

/// <summary>The picker rule: fogged regions that border revealed land in four-connectivity, ascending, with the ETA inputs.</summary>
public sealed class ExpeditionCandidatesTests
{
    // 5x1 strip: [0][0][1][2][2], region 0 revealed, lodge on the leftmost tile.
    // Region 1 borders revealed land; region 2 borders only fog.
    [Fact]
    public void Find_BorderingFoggedRegion_IsTheOnlyCandidate()
    {
        ushort[] regions = [0, 0, 1, 2, 2];
        var revealed = new HashSet<ushort> { 0 };

        List<ExpeditionTarget> found = ExpeditionCandidates.Find(
            5, 1, (x, y) => regions[y * 5 + x], (x, y) => 4, revealed, 0, 0);

        ExpeditionTarget single = found.ShouldHaveSingleItem();
        single.Region.ShouldBe((ushort)1);
        single.Nearest.ShouldBe(2);
        single.AverageElevation.ShouldBe(4);
    }

    [Fact]
    public void Find_CandidatesArriveInAscendingRegionOrder()
    {
        // Outer fogged regions (1, 2, 3) each touch the revealed spine 0; fogged filler never borders it.
        ushort[] regions = [1, 0, 2, 0, 3];
        var revealed = new HashSet<ushort> { 0 };

        List<ExpeditionTarget> found = ExpeditionCandidates.Find(
            5, 1, (x, y) => regions[y * 5 + x], (x, y) => 0, revealed, 1, 0);

        found.Select(static t => t.Region).ShouldBe([(ushort)1, (ushort)2, (ushort)3]);
    }

    [Fact]
    public void Find_DiagonalTouchDoesNotQualify()
    {
        // Region 1 sits in the middle touching the revealed corner (0, 0) only diagonally; the fogged
        // surround 2 borders revealed land along its top row, so it stays the only candidate.
        ushort[,] regions = { { 0, 2, 2 }, { 2, 1, 2 }, { 2, 2, 2 } };
        var revealed = new HashSet<ushort> { 0 };

        List<ExpeditionTarget> found = ExpeditionCandidates.Find(
            3, 3, (x, y) => regions[y, x], (x, y) => 0, revealed, 0, 0);

        ExpeditionTarget single = found.ShouldHaveSingleItem();
        single.Region.ShouldBe((ushort)2);
    }

    [Fact]
    public void Find_ElevationAveragesDownAndDistanceIsChebyshev()
    {
        // Region 1: tiles (1, 1) and (2, 2) of elevation 3 and 4; the nearest is a diagonal step away.
        ushort[,] regions = { { 0, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        int[,] elevations = { { 0, 0, 0 }, { 0, 3, 0 }, { 0, 0, 4 } };
        var revealed = new HashSet<ushort> { 0 };

        List<ExpeditionTarget> found = ExpeditionCandidates.Find(
            3, 3, (x, y) => regions[y, x], (x, y) => elevations[y, x], revealed, 0, 0);

        ExpeditionTarget single = found.ShouldHaveSingleItem();
        single.Nearest.ShouldBe(1);
        single.AverageElevation.ShouldBe(3);
    }

    [Fact]
    public void Find_AllRevealed_IsEmpty()
    {
        var revealed = new HashSet<ushort> { 0, 1 };

        List<ExpeditionTarget> found = ExpeditionCandidates.Find(
            2, 1, (x, y) => (ushort)x, (x, y) => 0, revealed, 0, 0);

        found.ShouldBeEmpty();
    }
}
