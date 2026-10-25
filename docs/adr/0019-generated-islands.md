# 0019. Generated islands: the generator contract and MapDef discoveries

Status: Accepted · Date: 2026-10-25

## Context

Generated islands must feed the same `MapDef` the authored loader produces, so the rest
of the sim stays agnostic (M2-01). But generation needs two things `MapDef` lacks:
which sub-seed attempt produced the island (the start guarantee re-seeds up to 16 times),
and the placed discoveries (natural wonders, ruins, shipwrecks) with stable identities
the view can name from locale data. The sim must not read display text, so names cannot
live in the generator output directly.

## Decision

- `MapDef` gains two members: `SubSeed` (byte, 0 for authored maps: the successful
  guarantee attempt) and `Discoveries` (immutable array of `MapDiscovery`, sorted by
  region then kind): `{ Region ushort, Kind DiscoveryKind, X, Y int, NameSeed int }`.
  `(X, Y)` is the feature's representative tile (a shore tile for reefs/shipwrecks, the
  peak tile for wonders); `NameSeed` picks the display name without the sim reading text.
- The generator lives in `Evenmere.Sim` (`Systems/Map/Generation/**`, M2-01/M2-02): a
  pure function of `(seed, size, ContentDb)` using a local `Pcg32`, never `SystemRngs`,
  integer-only per ADR-0003. `NewGameOptions` is unchanged: callers (CLI, game host,
  scenario runner) generate a `MapDef` and pass it as `Map`.
- Display names resolve in the view: `content/locale/en/names.json` (M2-13b) holds
  `discovery.<kind>.<index>` keys; the view reads `names[(NameSeed mod count)]`.
  Region display names reuse the existing `MapRegionName` list.
- The start guarantee (M2-02b) is a pure predicate over a `MapDef` plus content,
  shared by the generator and the tests: seeds 1–100 at 320 must satisfy it.
- `MapRegionName`'s "0 to 35" doc range is widened to the `ushort` range; generated
  islands hold 40–80 regions and never touch the ASCII `regions.txt` format.

## Consequences

- Authored maps keep working: `SubSeed` 0, empty discoveries, same loader path.
- Discovery identity is stable across save/load (indices into `MapDef.Discoveries`,
  matched with `ExplorationState` applied flags), and save remap never touches map data.
- The generator's determinism test (same seed → same `MapDef` bytes, macOS and Linux)
  covers the whole M2 map surface.

## Alternatives

Storing discoveries in sim state at new-game time instead of `MapDef`: rejected, because
the CLI gallery, the new-game preview and tests all need the features without a running
simulation.
