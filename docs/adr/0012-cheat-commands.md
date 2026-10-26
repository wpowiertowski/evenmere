# 0012. Cheat commands and the cheats flag

Status: Accepted · Date: 2026-10-26

## Context

Testers need tools that normal play must never touch: grant goods, finish a node, jump
eras, lift fog, add residents, plus an overlay showing fps, tick times and the hash. M2-26
adds all of it. The danger is leakage: a cheat reaching a real save, or a console binding
a normal player can hit. The cheats flag (a version-2 `SaveHeader` key per ADR-0018) is the
one gate, and debug-build-only presentation is the second.

## Decision

- **Commands** (`src/Evenmere.Sim/Systems/Debug/Commands/CheatCommands.cs`, all sealed
  records extending `Command`): `CheatGrantGoods(GoodId, Amount)`,
  `CheatCompleteNode(TechId?, CivicId?)` (exactly one set, so no new enum was needed),
  `CheatSetEra(EraId)`, `CheatRevealAll(int Region)` (`-1`, `AllRegions`, reveals everything;
  a region number reveals one region once exploration lands), `CheatAddResidents(int Count)`.
- **Handlers** (`CheatHandlers.cs`): every handler first checks the flag and rejects with
  the new `RejectReason.CheatsDisabled` (17, appended at the end) when the save's cheats
  flag is off. Past the gate they use the same services the real systems use, in ascending
  ID order: the pool for grants, the research/culture completion paths (Done bit, effects,
  event, target/queue fix-up) for nodes with availability bypassed on purpose, a direct era
  set plus `EraBegan` for eras, and an all-or-nothing fill of free beds for residents.
  Completing a done node and re-setting the current era are silent no-ops.
- **The flag:** `ISimulation.CheatsUsed` (plain member, no default implementation) is
  permission, not state: it never reaches the state hash. `Simulation` carries it in the
  `SimContext` the handlers read, so tests set it directly. The host sets it at new-game
  time (every debug build, or a release build with `--cheats`), and the save header mirrors
  it as key 22 (`CheatsUsed`, default false, no version bump, no migration). Loading
  restores the runtime flag from the header; `SaveGame.ReadCheatsUsed` reads the header
  without touching the body, so the load list can show it without reading state.
- **Registration:** cheat handlers are host tooling, not a ticked system. They register in
  the `Simulation` constructor with no pipeline entry, no cadence and no random stream, so
  no `SystemId` (whose values index the save-format rng array) and no state keys were
  needed. The log carries the five commands like any other (formatter cases plus remap:
  goods, techs, civics and eras remap by name and drop when gone; region numbers are map
  data and pass through).
- **Presentation, debug-only:** the overlay (F3, handled in code; M2 adds no input action
  for it) shows fps, tick time per system from `IProfilerReadout`, entity counts, the
  selected entity and the hash. The console opens with the backtick (`debug_console`, the
  only new input action in M2, keyboard-only: no gamepad button), parses lines with the
  pure `CheatParser` and enqueues them like any command. Both nodes are created only in
  debug builds, both ignore the mouse except the console's click-to-type field, and the
  focus walk skips both on purpose: the console is gamepad-unreachable by design.
- **`CheatRevealAll` (M2-06):** exploration holds the fog, so past the flag gate the handler reveals through
  it: `-1` reveals every region, any other value reveals one region by number (map data, never remapped).

## Consequences

- A release save can never process a cheat unless it was started with `--cheats`, and the
  header says so in the load list.
- The command-log coverage guard covers the five commands automatically (their parameters
  use only shapes `CommandSamples` already knows); no contract test was touched.
- The focus walk (`--focus-walk`, debug builds only) opens every panel in turn — HUD,
  build menu, inspector (on a real building, including the chain button), research and
  civics trees, chain viewer, notification feed, pause menu — plus any future top-level
  control, and asserts every enabled control is reachable by `ui_*` navigation with no
  traps. It is the M2 gate's gamepad evidence.

## Alternatives

- A `WorldState` flag instead of a runtime one: rejected, because permission is not state.
  A state flag would have re-baselined every golden hash for a bit that never changes
  behaviour; the runtime flag leaves all hashes alone.
- A new `SystemId` for a debug system: rejected, because the values index the
  save-format rng array and a nineteenth stream would break old saves.
