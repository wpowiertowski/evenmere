namespace Evenmere.Sim.Tests.MapGeneration.Guarantee;

/// <summary>Reads the EVENMERE_FULL_SWEEP flag: the exhaustive seed sweeps (minutes, not seconds) run only when
/// it is set to 1 — the nightly workflow sets it — while the normal suite runs a representative subset.</summary>
internal static class SweepGate
{
    /// <summary>Whether the full 1–100 seed sweeps should run. Set EVENMERE_FULL_SWEEP=1 to enable.</summary>
    internal static bool FullSweepEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("EVENMERE_FULL_SWEEP"), "1", StringComparison.Ordinal);
}
