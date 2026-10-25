namespace Evenmere.Sim.Tests;

// Wall-clock timing gates (pass/fail on Stopwatch) join the "Timing" collection so they never
// run alongside the parallel tests: a descheduled pass under full parallel load would measure
// the noise, not the code, and trip a budget for no reason. xUnit v3 runs a collection with
// DisableParallelization against no other tests, serially after the parallel pool drains.
[CollectionDefinition("Timing", DisableParallelization = true)]
public sealed class TimingCollectionDefinition;
