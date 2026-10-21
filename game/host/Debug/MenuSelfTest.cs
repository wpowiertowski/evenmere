using Evenmere.Core.Ids;
using Evenmere.Game.Host.Persistence;
using Evenmere.Game.Ui.Menus;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Systems.Research.Commands;
using Godot;

namespace Evenmere.Game.Host.Debug;

/// <summary>
/// The headless menu check behind <c>--menu-selftest</c> (M1-27 evidence), debug builds only: from a fresh game,
/// run ticks, save into slot A, enqueue three commands, run more ticks, save into slot B, then prove that loading A
/// gives back A's hash, that <c>Replay</c> of A's log reaches B's hash, and that Continue would pick B. Prints one
/// <c>SELFTEST ...</c> line per check plus <c>SELFTEST OK</c>, and exits 0; any failure prints
/// <c>SELFTEST FAIL</c> and exits 1. The self-test slots are deleted afterwards, so Continue is unaffected.
/// <see cref="Main"/> calls this only in debug builds; in release builds the flag is ignored like the other debug
/// flags. This file is the self-test's only home — <c>DebugDriver</c> and <c>DebugLaunchOptions</c> belong to other
/// tasks and stay untouched.
/// </summary>
internal static class MenuSelfTest
{
    /// <summary>The user argument that runs the self-test instead of the normal game.</summary>
    internal const string Flag = "--menu-selftest";

    /// <summary>Runs the self-test: it drives the game as play would, prints its MATCH lines and quits.</summary>
    /// <param name="main">The scene root, for the pause-menu lookup and for quitting with the result.</param>
    /// <param name="host">The running simulation host, with a fresh game already started.</param>
    /// <param name="recorder">The command-log recorder the saves rebind.</param>
    internal static void Run(Main main, SimHost host, CommandLogRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(recorder);

        // Like the smoke handler, every failure must become SELFTEST FAIL and exit code 1.
        try
        {
            const string slotA = "selftest-a";
            const string slotB = "selftest-b";

            host.RunTicksNow(1200);
            string hashA = SaveService.Save(host, recorder, slotA, "Selftest A");
            long tickA = host.CurrentTick;
            GD.Print($"SELFTEST saveA tick={tickA} hash={hashA}");

            EnqueueSelfTestCommands(host);
            host.RunTicksNow(600);
            string hashB = SaveService.Save(host, recorder, slotB, "Selftest B");
            long tickB = host.CurrentTick;
            GD.Print($"SELFTEST saveB tick={tickB} hash={hashB}");

            IReadOnlyList<SaveSlotInfo> found = SaveSlots.ListSlots();
            string latest = found.Count > 0 ? found[0].SlotId : string.Empty;
            GD.Print($"SELFTEST latest={latest} expect={slotB} {(latest == slotB ? "MATCH" : "MISMATCH")}");

            ulong replayed;
            using (FileStream save = File.OpenRead(SaveSlots.SavePath(slotA)))
            using (FileStream log = File.OpenRead(SaveSlots.LogPath(slotA)))
            {
                replayed = Replay.Run(save, log, host.Content, tickB);
            }

            string replayHash = $"{replayed:x16}";
            GD.Print($"SELFTEST replay={replayHash} expect={hashB} {(replayHash == hashB ? "MATCH" : "MISMATCH")}");

            host.UnloadGame();
            (string hashA2, SaveReport _) = SaveService.Load(host, recorder, slotA);
            GD.Print($"SELFTEST loadA tick={host.CurrentTick} hash={hashA2} {(hashA2 == hashA ? "MATCH" : "MISMATCH")}");

            string autosaveHash = RunAutosaveCheck(main, host);
            string liveHash = $"{host.ComputeStateHash():x16}";
            GD.Print($"SELFTEST autosave={autosaveHash} expect={liveHash} {(autosaveHash == liveHash ? "MATCH" : "MISMATCH")}");

            bool ok = latest == slotB && replayHash == hashB && hashA2 == hashA && autosaveHash == liveHash;
            GD.Print(ok ? "SELFTEST OK" : "SELFTEST FAIL");
            main.GetTree().Quit(ok ? 0 : 1);
        }
        catch (Exception ex)
        {
            GD.Print($"SELFTEST FAIL {ex.Message.ReplaceLineEndings(" ")}");
            GD.PrintErr(ex.ToString());
            main.GetTree().Quit(1);
        }
        finally
        {
            foreach (string slot in new[] { "selftest-a", "selftest-b", SaveSlots.AutosaveSlot })
            {
                foreach (string path in new[]
                         {
                             SaveSlots.SavePath(slot),
                             SaveSlots.LogPath(slot),
                             SaveSlots.MetaPath(slot),
                         })
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (IOException)
                    {
                        // Best effort cleanup only.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Best effort cleanup only.
                    }
                }
            }
        }
    }

    // Drives the real pause-menu autosave path headlessly: leave menu mode (as entering play does), run past the
    // next 10-day boundary and pump one frame, so PauseMenu._Process writes the autosave slot itself.
    private static string RunAutosaveCheck(Main main, SimHost host)
    {
        PauseMenu? pause = main.GetNodeOrNull<PauseMenu>("PauseMenu/Root");
        if (pause is null)
        {
            throw new InvalidOperationException("The self-test needs the PauseMenu node from Main.tscn.");
        }

        pause.SetMenuMode(false);
        pause._Process(0.016);
        host.RunTicksNow(12000);
        pause._Process(0.016);

        string path = SaveSlots.SavePath(SaveSlots.AutosaveSlot);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("The autosave slot was not written after crossing a 10-day boundary.");
        }

        return SaveService.ReadStateHash(SaveSlots.AutosaveSlot);
    }

    // Three valid commands through the game's own enqueue path (so the recorder sees them exactly as play would):
    // a research target, a civic target and a research queue holding that target.
    private static void EnqueueSelfTestCommands(SimHost host)
    {
        TechId? tech = null;
        for (int i = 0; i < host.Content.Techs.Count && tech is null; i++)
        {
            var candidate = TechId.From(checked((ushort)i));
            if (host.Query.Research.IsAvailable(candidate))
            {
                tech = candidate;
            }
        }

        CivicId? civic = null;
        for (int i = 0; i < host.Content.Civics.Count && civic is null; i++)
        {
            var candidate = CivicId.From(checked((ushort)i));
            if (host.Query.Culture.IsAvailable(candidate))
            {
                civic = candidate;
            }
        }

        if (tech is null || civic is null)
        {
            throw new InvalidOperationException("The self-test needs one available tech and one available civic.");
        }

        host.Enqueue(new SetResearchTarget(tech.Value));
        host.Enqueue(new SetCivicTarget(civic.Value));
        host.Enqueue(new SetResearchQueue([tech.Value]));
    }
}
