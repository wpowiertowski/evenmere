# 0018. Save format version 2: M2 state and the migration framework in use

Status: Accepted · Date: 2026-10-25

## Context

ADR-0007 defined the save envelope and the rule that MessagePack keys are never reused,
and M1-14 built the migration framework without a first use. M2 fills `ExplorationState`
and `HappeningsState` and adds keys to `CultureState` (slotted traditions, festival run,
last-held years), `ConstructionState` (`SiteRecord.StageIndex`), `RoadsState`
(per-road-type tile counts) and `WorkforceState` if the crew stat (ADR-0015) needs any.
Version 1 readers must keep loading (the M1 gate's `v1-slice.evsave` fixture), and the
M2 gate requires that fixture to migrate and continue deterministically.

## Decision

- Bump `SaveFormat.CurrentVersion` 1 → 2 for the first M2 state addition, and add
  `Migration_0001_to_0002`: it sizes the new arrays to the loaded content (same rules as
  each state's `FitTo`), defaults new scalars (no festival running, stage 0, zero counts),
  and re-runs the existing content-ID `RemapPass` over the new content-indexed members
  (`[ContentIndexed]` / `[ContentValued]` / `[NotContentIndexed]` markers on every new
  array, per the state rules).
- Later M2 additions that only append keys with load-safe defaults ride version 2 with no
  new migration class; a change that alters the meaning of an existing key needs version 3
  and a new class. M2-15 owns the version bump and the 0001→0002 class; each later state
  card states in its PR whether it needed a new class (expected: none).
- **Two save fixtures (amended in M2-07).** `tests/fixtures/saves/v1-slice.evsave` is the frozen M1 save, the last version-1 file, and is never regenerated: its tests check that it migrates cleanly and continues deterministically. `tests/fixtures/saves/slice-20000.evsave` is the current-version golden, which `EVENMERE_UPDATE_GOLDEN=1` regenerates when state changes.
- `tests/fixtures/saves/state-keys.json` is re-baselined once by M2-15 and then kept
  current by whichever card adds a key (the baseline test prints the refresh command).
- `SaveHeader` gains exactly one M2 key: `CheatsUsed` (bool, default false), added by M2-26 with no version bump, so the load list can show it without reading state. No other M2 card touches the header.

## Consequences

- `v1-slice.evsave` loads through the migration and continues to the same hash as an
  uninterrupted run (M2-15 scenario), proving old saves survive.
- Adding a key stays cheap, but changing a key's meaning stays a deliberate version bump.

## Alternatives

One version per state card: rejected; MessagePack's additive keys make a single M2
version sufficient, and fewer versions mean fewer migration paths to test.
