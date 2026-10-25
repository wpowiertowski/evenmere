using Evenmere.Core.Time;
using Evenmere.Game.Host.Persistence;

namespace Evenmere.Game.Tests.Persistence;

/// <summary>M2-15: the autosave rotation keeps three generations, and a bulk demolish takes a safety autosave.</summary>
public sealed class AutosaveRotationTests
{
    [Fact]
    public void AutosaveSlotAt_Ages_MapToExpectedIds()
    {
        SaveSlots.AutosaveSlotAt(0).ShouldBe(SaveSlots.AutosaveSlot);
        SaveSlots.AutosaveSlotAt(1).ShouldBe("autosave-1");
        SaveSlots.AutosaveSlotAt(2).ShouldBe("autosave-2");
        SaveSlots.AutosaveSlotIds.ShouldBe(["autosave", "autosave-1", "autosave-2"]);

        Should.Throw<ArgumentOutOfRangeException>(() => SaveSlots.AutosaveSlotAt(-1));
        Should.Throw<ArgumentOutOfRangeException>(() => SaveSlots.AutosaveSlotAt(SaveSlots.AutosaveSlotCount));
    }

    [Fact]
    public void NeedsPreDemolishAutosave_Threshold_MoreThanTenBuildings()
    {
        SaveService.NeedsPreDemolishAutosave(0).ShouldBeFalse();
        SaveService.NeedsPreDemolishAutosave(10).ShouldBeFalse();
        SaveService.NeedsPreDemolishAutosave(11).ShouldBeTrue();
    }

    [Fact]
    public void RotateAutosavesIn_FourPeriods_KeepsNewestThree()
    {
        string directory = NewTempDirectory();
        try
        {
            // One autosave per 10-day period, stamped with a fake clock's day, like the pause menu's timer.
            foreach (long day in new long[] { 0, 10, 20, 30 })
            {
                var clock = new GameClock(day * GameTime.TicksPerDay);
                SaveSlots.RotateAutosavesIn(directory);
                WriteGeneration(directory, SaveSlots.AutosaveSlot, $"day-{clock.Day}");
            }

            ReadGeneration(directory, "autosave").ShouldBe("day-30");
            ReadGeneration(directory, "autosave-1").ShouldBe("day-20");
            ReadGeneration(directory, "autosave-2").ShouldBe("day-10");
            Directory.GetFiles(directory, "*" + SaveSlots.SaveExtension).Length.ShouldBe(3);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RotateAutosavesIn_MovesSaveLogAndSidecarTogether()
    {
        string directory = NewTempDirectory();
        try
        {
            WriteGeneration(directory, SaveSlots.AutosaveSlot, "gen-a");
            SaveSlots.RotateAutosavesIn(directory);

            ReadGeneration(directory, "autosave-1").ShouldBe("gen-a");
            File.Exists(Path.Combine(directory, "autosave" + SaveSlots.SaveExtension)).ShouldBeFalse();
            File.Exists(Path.Combine(directory, "autosave-1" + SaveSlots.LogExtension)).ShouldBeTrue();
            File.Exists(Path.Combine(directory, "autosave-1" + SaveSlots.MetaExtension)).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RotateAutosavesIn_ClearsStaleTargetFiles()
    {
        string directory = NewTempDirectory();
        try
        {
            // The middle generation lost its log (a crash, an older build) while the oldest generation is complete:
            // after rotation the oldest slot must hold the newer save with no log, never the stale one.
            File.WriteAllText(Path.Combine(directory, "autosave-1" + SaveSlots.SaveExtension), "gen-new\n");
            File.WriteAllText(Path.Combine(directory, "autosave-1" + SaveSlots.MetaExtension), "gen-new\n");
            WriteGeneration(directory, "autosave-2", "gen-old");

            SaveSlots.RotateAutosavesIn(directory);

            ReadGeneration(directory, "autosave-2").ShouldBe("gen-new");
            File.Exists(Path.Combine(directory, "autosave-2" + SaveSlots.LogExtension)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AutosaveBeforeBulkDemolish_AtOrUnderThreshold_ReturnsNull()
    {
        // The guard returns before touching either argument, so nulls are safe here: no save is taken.
        SaveService.AutosaveBeforeBulkDemolish(null!, null!, 10, "name").ShouldBeNull();
        SaveService.AutosaveBeforeBulkDemolish(null!, null!, 0, "name").ShouldBeNull();
    }

    [Fact]
    public void RotateAutosavesIn_EmptyFolder_DoesNothing()
    {
        string directory = NewTempDirectory();
        try
        {
            SaveSlots.RotateAutosavesIn(directory);
            Directory.GetFiles(directory).ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string NewTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "evenmere-autosave-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteGeneration(string directory, string slotId, string marker)
    {
        File.WriteAllText(Path.Combine(directory, slotId + SaveSlots.SaveExtension), marker + "\n");
        File.WriteAllText(Path.Combine(directory, slotId + SaveSlots.LogExtension), marker + "\n");
        File.WriteAllText(Path.Combine(directory, slotId + SaveSlots.MetaExtension), marker + "\n");
    }

    private static string ReadGeneration(string directory, string slotId) =>
        File.ReadAllText(Path.Combine(directory, slotId + SaveSlots.SaveExtension)).Trim();
}
