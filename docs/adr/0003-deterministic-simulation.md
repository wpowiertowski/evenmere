# 0003. Deterministic fixed-tick simulation

Status: Accepted · Date: 2026-10-18

## Context

Replayable bug reports, reproducible balance runs, golden-hash scenario tests and trustworthy saves all need the same input to give the same output, on every platform: macOS ARM64, Linux x64 (including Steam Deck) and, later, Windows x64.

## Decision

- **Fixed tick:** one tick is 0.25 s of game time. A day is 120 ticks, a season 10 days and a year 4 seasons. `ISimulation.Tick()` advances exactly one tick, and the host decides how many ticks run per frame.
- **Integer state:**
  - `Amount` is an `int64` count of milli-units (1 unit = 1000).
  - `Ratio` is an `int32` per-mille (1000 = 100%).
  - Multiplying an `Amount` by a `Ratio` rounds toward negative infinity.
  - Modifier percentages on the same stat add up before being applied once.
- **Randomness:** a PCG32 generator per system, seeded from the world seed plus a constant salt per system. Generator states are part of `WorldState`, and therefore part of saves and the state hash.
- **Ordering:**
  - Entity IDs are monotonic and never reused, and iteration is always in ID order.
  - Commands apply in enqueue order.
  - Events are emitted in system order, then in entity order.
- **No hidden inputs:**
  - Banned in `Evenmere.Sim` by analyser: `System.Random`, `DateTime.Now`, `DateTime.UtcNow`, `Environment.TickCount`, `Stopwatch` (except in the profiling hooks under `Sim/Diagnostics`), `Guid.NewGuid`, `Parallel`, `Task.Run`, and `ThreadPool`.
- **Map generation is integer-only.** It uses integer value or simplex noise and no transcendental `Math` functions, whose results can differ between CPUs and runtimes.
- **Hash:** `ComputeStateHash()` is XxHash64 over the canonical binary serialisation of `WorldState`, the same writer the save format uses.
- **Bucketed daily work:** daily systems process the entities with `id % 120 == tick % 120` on each tick, which spreads the cost across the day without changing results between runs.

## Consequences

- Scenario tests assert golden hashes. Any unintended behaviour change shows up as a hash diff, and an intended one updates the golden file in the same commit with an explanation.
- A desync between two runs can be bisected tick by tick by comparing hashes.
- Visual smoothness comes from interpolation in the view, never from fractional sim state.
- Balancing uses integer per-mille maths, and the content loader rejects decimals finer than 0.001.
