# 0006. SimHost threading: main thread first, snapshot worker later

Status: Accepted · Date: 2026-10-18

## Context

The v0.1 plan ran ticks on a worker thread while view controllers read sim queries. Without a snapshot this is a data race: the UI could read half-updated tables. A full snapshot copy every tick costs memory bandwidth, which is premature before the sim is profiled.

## Decision

- **Phase 1 (M0–M3):**
  - The `SimHost` ticks synchronously on Godot's main thread inside a fixed-step accumulator, with a per-frame budget of at most 8 ticks and 4 ms. Leftover time carries to the next frame, and at 4× speed the sim may lag rather than drop frames.
  - Queries are called only between ticks.
- **Phase 2 (M4):**
  - The sim runs on a dedicated worker thread.
  - After each tick it publishes an immutable `FrameSnapshot`, which holds the view-relevant tables: buildings, jobs, house tiers, dirty chunks, HUD numbers.
  - The main thread reads only the latest published snapshot. The snapshot implements the same query interfaces, so view code doesn't change.
  - Commands cross threads through a lock-free queue.
- **Never** let view code hold references into live `WorldState`.

## Consequences

- Phase 1 is simple and safe, and it's enough for the vertical slice and Era I scale.
- View code must use the query interfaces, never concrete state types. An architecture test (M1-28) checks that `game/` uses no `WorldState` type, and no type reachable from it.
- Phase 2 is a contained change to `game/host/` and a snapshot builder in `Evenmere.Sim`.
