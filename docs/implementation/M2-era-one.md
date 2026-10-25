# M2 — Era I complete

**Goal:** the whole Founding era on generated islands:

- Rivers and bridges, fog and expeditions, seasons.
- All Era I content: 13 techs, 8 civics, 8 traditions, festivals, the Standing Stones and happenings.
- Era advancement through both gateways.
- Graph views of both trees, overlays, the production-chain viewer and statistics.
- Save migrations, replay, the performance harness and a multi-seed balance bot.

From M2 onward, every task lands through a **PR with green CI**.

**Refinement summary (M2-00).** Every card now names its exact files, new query
members (per ADR-0017), commands, events, state keys, content and locale files, tests
and expected golden re-baselines. Accepted with this refinement: ADR-0017 (M2 contract
addendum: queries, `RemoveSource`, commands, events), ADR-0018 (save format version 2),
ADR-0019 (generator contract, `MapDef` discoveries). Proposed: ADR-0015 (crew stat),
which M2-30 accepts with the developer.
Split into lettered parts: M2-02a/b (terrain vs regions/discoveries/guarantee),
M2-13a/b (production content vs trees content), M2-17a/b (Era I bot vs multi-seed).
Developer answers (2026-10-25) recorded in §Answered: Deck-equivalent perf factor 3.0,
provisional pacing window with a human confirm step, automated focus walk instead of the
gamepad hand check.

**Gate:**

- [ ] **Bot pacing:** balance bot v2 reaches Era II (both gateways) in the provisional 90–120 game minutes on 5 generated seeds and on the starter island, with no stall longer than one season. The window is provisional: M2-17a reports real pacing, the developer confirms or moves it, and M2-17b tunes to the confirmed window.
- [ ] **Performance:** the `perf-2k` reference save (about 2,000 residents) runs at a Deck-equivalent average of ≤ 0.6 ms per tick: the development-Mac average × the Mac→Deck factor 3.0 (so Mac ≤ 0.2 ms), per the architecture budgets. The nightly CI job records the raw numbers for the trend; the gate uses the dev-Mac measurement.
- [ ] **Migrations:** the M1 save fixture loads through the migration framework.
- [ ] **Playtest:** the developer plays a generated island to Era II and signs off on calmness, pacing and clarity.
- [ ] **Lint:** EVL010 (the staffing rule) passes on all content.
- [ ] **Focus walk:** every control is reachable by `ui_*` focus navigation at 1280 × 800, proven by the automated focus walk (M2-26). This replaces the M1-waived gamepad hand check, which the developer dropped.

**Waves:**

| Wave | Tasks | Notes |
| --- | --- | --- |
| 0 | M2-00 | Refinement (this task) |
| A | M2-01, M2-05, M2-07, M2-09, M2-10, M2-14, M2-15, M2-29, M2-30 | Sim foundations + content-infra + look + upgrades design; no two cards share a file (M2-07 owns the traditions lines of the shared registration points) |

M2-31 is done (M1 playtest polish); it is not scheduled in any wave.
| B | M2-02a, M2-04, M2-08, M2-11, M2-12, M2-18, M2-19, M2-20, M2-21, M2-22, M2-26 | Map terrain; bridges; festivals; eras; happenings; tree/overlay/chain/stats views; cheats (M2-26 moved here so its `SaveHeader` line never meets M2-15) |
| C | M2-02b, M2-13a, M2-16, M2-24 | Regions/discoveries/guarantee; Era I production content; perf harness; season visuals |
| D | M2-03, M2-06, M2-25, M2-27 | Mapgen CLI; exploration; new-game UI; culture-moment UI |
| E | M2-13b, M2-23 | Trees content; fog/expedition UI |
| F | M2-17a | Era I bot |
| G | M2-17b | Multi-seed bot, nightly balance job, pacing sign-off |
| H | M2-28 | Playtest and gate review |

Shared registration points: `SystemRoster.cs` keeps M1-13's wiring (no new systems are
ticked services beyond the existing stubs — Exploration and Happenings stubs are filled
in place, each card editing only its own system's construction lines);
`SystemPipeline.cs` order is unchanged (ADR-0011, architecture.md steps 2–15).
`CultureSystem.cs` registration: M2-07 owns the `// traditions (M2-07)` lines,
M2-08 the `// festivals (M2-08)` lines. `game/project.godot [input]`: only M2-26 adds
an action (`debug_console`); every other game card uses existing `ui_*` focus navigation
and states so. `game/scenes/Main.tscn`: each game card instances only its own sub-scene.
`.github/workflows/nightly.yml`: M2-16 owns the perf-job section, M2-17b the
balance-job section. `content/locale/en/*.json` and `game/data/visuals/*.json`: one
file per card as listed; never edit another card's file.

## Task index

| ID | Title | Lane | Depends on | Status |
| --- | --- | --- | --- | --- |
| M2-00 | Kickoff: refine M2 cards against the code | sim | M1 gate | done |
| M2-01 | Island generator: heightmap, coast and biomes | sim | M2-00 | todo |
| M2-02a | Rivers, fertility and deposits | sim | M2-01 | todo |
| M2-02b | Regions, discoveries, start guarantee and generated new games | sim | M2-02a | todo |
| M2-03 | Map generation preview CLI and seed gallery | tools | M2-02b | todo |
| M2-04 | Bridges and river crossing | sim | M2-00 | todo |
| M2-05 | Seasons | sim | M2-00 | done |
| M2-06 | Exploration: fog, expeditions and discoveries | sim | M2-02b, M2-10 | todo |
| M2-07 | Traditions and slots | sim | M2-00 | todo |
| M2-08 | Festivals | sim | M2-00 | todo |
| M2-09 | Inspirations and the full condition vocabulary | sim | M2-00 | todo |
| M2-10 | Monuments: multi-stage construction | sim | M2-00 | todo |
| M2-11 | Era advancement and gateways | sim | M2-09 | todo |
| M2-12 | Happenings | sim | M2-09 | todo |
| M2-13a | Era I production content | content | M2-04, M2-05, M2-14, M2-30 | todo |
| M2-13b | Era I trees, monuments, happenings and era content | content | M2-06, M2-07, M2-08, M2-10, M2-11, M2-12, M2-13a, M2-14 | todo |
| M2-14 | Lint rule EVL010: lower-tier staffing | content-infra | M2-00 | todo |
| M2-15 | Save versioning, migrations and autosave rotation | sim | M2-00 | todo |
| M2-16 | Replay and performance harness, reference saves | tools | M2-15 | todo |
| M2-17a | Balance bot v2: the Era I island bot | tools | M2-04, M2-13b, M2-16 | todo |
| M2-17b | Multi-seed balance runs and pacing sign-off | tools | M2-17a + pacing-window confirmation (human) | todo |
| M2-18 | Tech tree graph view | game | M2-09 | todo |
| M2-19 | Civics tree and traditions board | game | M2-07 | todo |
| M2-20 | Overlays: coverage, reach, fertility and deposits | game | M2-00 | todo |
| M2-21 | Production chain viewer | game | M2-00 | todo |
| M2-22 | Statistics graphs | game | M2-00 | todo |
| M2-23 | Fog, regions and expedition UI | game | M2-06 | todo |
| M2-24 | Seasons, weather and smoke visuals | game | M2-05 | todo |
| M2-25 | New-game setup: seed, size, starter island | game | M2-02b | todo |
| M2-26 | Debug overlay and cheat console | game + sim | M2-00 | todo |
| M2-27 | Festivals, monuments and happenings UI | game | M2-08, M2-10, M2-12 | todo |
| M2-28 | Era I playtest and gate review | human | all M2 tasks | todo |
| M2-29 | Look development: hand-drawn rendering prototype | game | M1 gate | todo |
| M2-30 | Upgrade lines: design pass, crew stat and Era I upgrades | human + content | M2-00 | todo |
| M2-31 | Window and input polish from the M1 playtest | game | M1 gate | done |

---

### M2-00 · Kickoff: refine M2 cards against the code

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim (planning) | M1 gate | `docs/implementation/M2-era-one.md`, `docs/adr/0015-crew-stat.md`, `docs/adr/0017-m2-simulation-contract.md`, `docs/adr/0018-save-format-version-2.md`, `docs/adr/0019-generated-islands.md` (new), `docs/adr/README.md` (table rows only), `docs/design/architecture.md` (§Performance budgets, one sentence only), `docs/implementation/README.md` (human-tasks table rows only) |

**Goal:** make every M2 card as precise as the M1 cards: exact folders, interface methods and events, fixtures, based on the code as it exists after M1.

**Steps:**

1. Read the M1 gate record and the code under `src/`.
2. For each card, name the concrete files, interfaces and methods to add, and list the new query methods each interface gains.
3. Split any card estimated at more than about a day of agent work.
4. Flag contract changes that need ADRs, and draft them as "Proposed".

**Acceptance:** the orchestrator approves the revised cards, and any proposed ADRs are merged before dependent tasks start.

---

### M2-01 · Island generator: heightmap, coast and biomes

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-00 | `src/Evenmere.Sim/Systems/Map/Generation/**` (new: `IslandGenerator.cs`, `IntegerNoise.cs`, `HeightmapBuilder.cs`, `BiomePainter.cs`), `tests/Evenmere.Sim.Tests/MapGeneration/Heightmap/**` (new), `tests/fixtures/maps/generated-heightmap.md` (golden hashes doc, new) |

**Reads:** architecture.md (§Island and map); ADR-0003 (integer-only generation); ADR-0019; progression.md (§Terrain and access ladder).

**Goal:** a seeded, integer-only generator that produces a believable island of 256, 320 or 384 tiles across, identically on every CPU.

**Steps:**

1. **Noise:** integer value or simplex noise over fixed-point coordinates (Q16.16), with octaves summed in integers, in-house in `IntegerNoise.cs`. No `System.Random`, no `Math.*` floating point (the banned-API analyser plus a grep test).
2. **Heightmap:** noise × a radial falloff (squared distance, integer) gives sea, beach and land, quantised to 0–15 elevation, in `HeightmapBuilder.cs`.
3. **Biomes:** from elevation bands plus a moisture noise. Marsh sits on low, wet land; highlands and peaks on high bands; forest on moisture; plains elsewhere (`BiomePainter.cs`).
4. **Output:** fill a `MapDef`-shaped builder (terrain, elevation, zeroed river/region/deposit/fertility layers); rivers and features are M2-02's. Entry point `IslandGenerator.GenerateHeightmap(seed, size, content)` is pure: same seed → same arrays.
5. **Sizes:** accept only 256, 320, 384; anything else throws `ArgumentOutOfRangeException`.

**Acceptance:**

- Golden hashes of the heightmap arrays for seeds 1–5 at each size are committed in `generated-heightmap.md` and asserted by `HeightmapGoldenTests`; CI on Linux reproduces the macOS hashes.
- Generating 320 × 320 takes ≤ 500 ms (benchmark test, development Mac).
- A test grep finds no `Math.` floating-point calls in the generation folder.
- `scripts/verify.sh` passes.

**Out of scope:** rivers and features (M2-02a/b).

---

### M2-02a · Rivers, fertility and deposits

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-01 | `src/Evenmere.Sim/Systems/Map/Generation/**` (`RiverTracer.cs`, `FertilityPainter.cs`, `DepositPlacer.cs`, all new), `tests/Evenmere.Sim.Tests/MapGeneration/Rivers/**` (new) |

**Reads:** architecture.md (§Island and map); ADR-0019; progression.md (§Terrain and access ladder: fertility, deposits).

**Goal:** traced rivers with width classes, one fertility patch layer per crop, and placed deposits on the heightmap from M2-01.

**Steps:**

1. **Rivers** (`RiverTracer.cs`): trace downhill from highland springs to the sea with integer flow accumulation. Width classes: narrow (≤ 3 tiles across) and wide (4–8). Clay deposits go on riverbanks (tiles adjacent to river tiles).
2. **Fertility** (`FertilityPainter.cs`): one noise patch layer per crop (`CropKind`), with affinities — grain and hops on plains; apples on plains and hills; grapes and tea on hills; madder and sugar beet on marsh. Set the `Fertility` bitmask bits (`1 << (int)CropKind`).
3. **Deposits** (`DepositPlacer.cs`): stone outcrops on hills, iron on hills, coal and copper in highlands, reefs offshore. Write the `Deposit` byte layer (`DepositKind` value plus one).
4. Entry point `IslandGenerator.GenerateTerrain(seed, size, content)` = M2-01 heightmap plus this card's layers; still no regions or discoveries.

**Acceptance:**

- Property tests on seeds 1–50 at 320: every river tile reaches the sea by strictly descending elevation; river width classes are only 1–2 where rivers exist; every deposit sits on its required terrain.
- Fertility golden hashes for seeds 1–5 are committed alongside M2-01's.
- `scripts/verify.sh` passes.

**Out of scope:** regions, discoveries, the start guarantee (M2-02b).

---

### M2-02b · Regions, discoveries, start guarantee and generated new games

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-02a | `src/Evenmere.Sim/Systems/Map/Generation/**` (`RegionBuilder.cs`, `DiscoveryPlacer.cs`, `StartGuarantee.cs`, all new), `src/Evenmere.Content/Maps/MapDef.cs` (`SubSeed` and `Discoveries` members only, per ADR-0019), `tests/Evenmere.Sim.Tests/MapGeneration/Guarantee/**` (new), `tests/Evenmere.Sim.Tests/Scenarios/ScenarioJson.cs` (generated-map source only), `tests/fixtures/scenarios/generated_seed7.json` (new) |

**Reads:** architecture.md (§Island and map, start guarantee); ADR-0019; progression.md (§Terrain and access ladder).

**Goal:** a generated island that's playable and interesting, with Era I guaranteed in the start region.

**Steps:**

1. **Regions** (`RegionBuilder.cs`): 40–80 regions by flood-filled seeded Voronoi, bounded by rivers where possible. Widen `MapRegionName`'s doc range to the `ushort` range (comment only).
2. **Discoveries** (`DiscoveryPlacer.cs`): 3–6 natural wonders, 4–8 ruins and 3–6 shipwrecks, placed with deterministic `NameSeed` values per ADR-0019. Fill `MapDef.Discoveries` and `SubSeed`.
3. **Start guarantee** (`StartGuarantee.cs`): the start region is bounded by rivers and meets the M1-03 start guarantees (forest belt, 3 grain-fertile 5×5 placements, 4 plains 4×4 sheep-farm placements, 40 coastline tiles, hills with stone, clay riverbanks). If it doesn't, re-seed with a deterministic sub-seed, up to 16 attempts, and record it in `SubSeed`. The predicate is pure over `(MapDef, ContentDb)` and shared with the tests.
4. **New game:** `IslandGenerator.Generate(seed, size, content)` returns the finished `MapDef`; callers pass it as `NewGameOptions.Map`. No `NewGameOptions` change.
5. **Scenario support:** extend the scenario runner's `map:` source with `generated:<seed>:<size>` for tests.

**Acceptance:**

- Seeds 1–100 at the default size 320 all satisfy the start guarantee (test).
- Golden `MapDef` hashes (seeds 1–5, all sizes) are committed.
- Scenario `generated_seed7.json` plays the slice bot script on seed 7 and reaches Villagers.
- `scripts/verify.sh` passes.

**Out of scope:** the fog presentation (M2-23); the mapgen CLI (M2-03).

---

### M2-03 · Map generation preview CLI and seed gallery

| Lane | Depends on | Owns |
| --- | --- | --- |
| tools | M2-02b | `src/Evenmere.Tools/MapGen/**` (new: `MapGenRenderer.cs`, `PngWriter.cs`), `src/Evenmere.Tools/Commands/MapGenCommand.cs` (new), `src/Evenmere.Tools/EvenmereCli.cs` (`mapgen` registration lines only), `tests/Evenmere.Tools.Tests/MapGen/**` (new, or `tests/Evenmere.Sim.Tests/MapGeneration/Gallery/**` if no Tools test project exists — check and state the choice in the report) |

**Reads:** ADR-0019; docs/design/map-format.md (ASCII conventions).

**Goal:** see islands without opening Godot.

**Steps:**

1. Add `evenmere mapgen --seed S --size N --out file.png [--layer terrain|elevation|fertility:<crop>|regions|deposits]`, writing PNGs with a minimal in-house PNG writer (`PngWriter.cs`, stored DEFLATE blocks; no new NuGet package — AGENTS.md forbids unnamed packages).
2. Add `--gallery 1..20`, which writes a contact sheet.
3. Add ASCII output for agents (`--ascii`), reusing the map-format legend characters.

**Acceptance:**

- A gallery of 20 seeds is attached to the PR, and the ASCII output for seed 1 matches its committed golden file.
- `scripts/verify.sh` passes.

**Out of scope:** gameplay; the new-game screen (M2-25).

---

### M2-04 · Bridges and river crossing

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-00 | `src/Evenmere.Sim/Systems/Roads/**` (`RoadPlacement.cs` bridge-validation lines, `RoadNetwork.Fields.cs`/`Reach.cs` cost lines as needed — no new state), `tests/Evenmere.Sim.Tests/Roads/Bridges/**` (new) |

**Reads:** tech-tree.md (Bridge-building, Stone Bridges); progression.md (§Terrain and access ladder).

**Goal:** roads can cross rivers only as bridges whose span suits the unlocked technology.

**Steps:**

1. **Bridge road types:** a road segment that crosses river tiles must be a contiguous straight run of a bridge road type (`timber_bridge`, later `stone_bridge`). Each type's content data gives its maximum span (`bridgeSpan`), checked against the river width class (narrow ≤ 3, wide 4–8).
2. **Validation:** `ValidateRoad` reports `BridgeTooLong`, `BridgeNotStraight` and `TerrainAccessLocked`, citing the access flag (`narrow_rivers` or `wide_rivers`). `PlacementIssue` gains the two bridge codes (same file, additive).
3. **Costs and fields:** bridge tiles carry their own per-tile cost (`roadCost`); distance fields treat bridges as normal road tiles with that cost. No `RoadsState` change (M2-09 adds the per-type counts).

**Acceptance:**

- Tests cover each error and a successful crossing, including a wide river rejected for `timber_bridge`.
- A field across a bridge equals a brute-force BFS check (extend the M1-05 property test pattern).
- Determinism hash test.
- `scripts/verify.sh` passes.

**Out of scope:** rail (M5-05); the Timber Bridge content itself (M2-13a).

---

### M2-05 · Seasons

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-00 | `src/Evenmere.Sim/Systems/Production/Seasons/**` (new: `SeasonCurve.cs`, `SeasonEvents.cs` with `SeasonChanged`), `src/Evenmere.Sim/Systems/Production/ProductionSystem.cs` (season-factor lines only), `src/Evenmere.Sim/Simulation.cs` (season-boundary emission line only), `content/rules/core.json` (`seasons` block only), `tests/Evenmere.Sim.Tests/Production/Seasons/**` (new) |

**Reads:** architecture.md (§Time model: seasons); progression.md (§Buildings: season yield curve); culture-tree.md (Harvest Rites, `harvest_festival`).

**Goal:** the calendar shapes farm yields gently, with no punishment.

**Steps:**

1. **Season curve:** the `seasons` block in `content/rules/core.json` defines the curve per tag (default `seasonal`: spring 1000, summer 1200, autumn 1100, winter 700). The schema and `RulesDef` loader already support it — this card only adds the data. Values are per-mille integers; `SeasonCurve.FactorFor(tag, season)` returns 1000 when the tag has no curve.
2. **Production:** buildings whose content tags match a curve key multiply productivity by the season factor (per-mille maths, applied after the M1-08 productivity terms). No new state: the factor derives from `GameClock.Season`.
3. **Events:** `Simulation` emits `SeasonChanged(season)` when the clock crosses a season boundary. Season-conditional modifiers already flip in `ModifierIndex`; add a test proving an autumn-only modifier flips at the boundary.
4. **Carried over from M1-08:** the season factor is no longer a hard-coded 1000.

**Acceptance:**

- A grain farm's yearly output equals the mean of the curve × base, within rounding; a test asserts the yield in each season (spring 1.0×, summer 1.2×, autumn 1.1×, winter 0.7×).
- Slice scenarios keep their golden hashes (slice content carries no seasonal tags yet).
- `scripts/verify.sh` passes, lint clean (the `seasons` block validates against the existing schema).

**Out of scope:** season visuals (M2-24); comfort needs (M2-13a).

---

### M2-06 · Exploration: fog, expeditions and discoveries

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-02b, M2-10 | `src/Evenmere.Sim/Systems/Exploration/**` (fill `ExplorationState.cs`, `ExplorationSystem.cs`, `ExplorationQueries.cs`, `Events.cs`; new `Commands/StartExpedition.cs`, `FogRule.cs` under `src/Evenmere.Sim/Systems/Construction/Placement/` (new file, the `Fogged` rule only)), `src/Evenmere.Sim/Queries/Types/ModifierContext.cs` (`Discovery` field only, per ADR-0017), `tests/Evenmere.Sim.Tests/Fakes/FakeExplorationQueries.cs` (new), `tests/Evenmere.Sim.Tests/Exploration/**` (new) |

**Reads:** architecture.md (§Exploration); culture-tree.md (§Attractiveness and visitors: natural wonders).

**Goal:** the island reveals itself over time through expeditions, and discoveries reward curiosity.

**Carried over from M1-04:**
- `revealCoastline` only sets a pending flag (`ProgressionState.CoastlineRevealed`); consume it here.
- The `discovery` modifier target matches nothing until `ModifierContext` gains a discovery field (ADR-0017).

**Steps:**

1. **State** (`ExplorationState`, new keys per ADR-0018): `[Key(0)] ulong[] RevealedRegions` (`[NotContentIndexed("region numbers are map data, not content IDs")]`); `[Key(1)] IdTable<ExpeditionRecord> Expeditions` where `ExpeditionRecord` is `{[Key(0)] ushort TargetRegion; [Key(1)] int DaysLeft; [Key(2)] EntityId Lodge}`; `[Key(2)] bool[] DiscoveriesApplied` indexed by `MapDef.Discoveries` order (`[NotContentIndexed("map feature indices, not content IDs")]`).
2. **Queries** (`IExplorationQueries`, per ADR-0017): `IsRevealed(region)`, `RevealedRegions()`, expedition status per lodge, `Discoveries()`. The start region and the coastline in view start revealed at new-game.
3. **Fog:** per region. `FogRule.cs` adds the `Fogged` placement rejection for fogged tiles, read by the shared validation path.
4. **`StartExpedition(lodgeId, regionId)`** (new `Command`): the target must be adjacent to revealed land. Duration = base days + the region's distance from the lodge × a terrain factor, and the `expeditionSpeed` modifier applies. One expedition at a time per lodge.
5. **Return:** reveal the region, emit `RegionRevealed` and a `DiscoveryRevealed` per feature, add to the counters for conditions (`discovered`, `expeditions`), and apply discovery effects once: ruins grant knowledge (later a Relic, from M3-03); shipwrecks grant materials; natural wonders register for Overlook bonuses (M3-02).
6. **Coastline reveal:** consume `CoastlineRevealed` as the hook for the Great Lighthouse effect (data in M3, behaviour no-op besides revealing).

**Acceptance:**

- An expedition reveals its region on the right tick; each discovery effect applies exactly once (applied-flags test).
- Save round trip mid-expedition continues deterministically.
- Placement on a fogged tile is rejected with `Fogged`; revealed tiles pass.
- `scripts/verify.sh` passes.

**Out of scope:** the fog presentation (M2-23); great works and Overlooks (M3-02/03).

---

### M2-07 · Traditions and slots

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-00 | `src/Evenmere.Sim/Systems/Culture/Traditions/**` (new: `TraditionSlots.cs`, `TraditionSystem.cs` or folded into `CultureSystem.cs` — state the choice; `Commands/SetTradition.cs`), `src/Evenmere.Sim/Systems/Culture/CultureSystem.cs` (`// traditions (M2-07)` registration lines only), `src/Evenmere.Sim/Systems/Culture/CultureQueries.cs` (tradition/festival query members only), `src/Evenmere.Sim/Systems/Culture/CultureState.cs` (tradition keys only), `src/Evenmere.Sim/Systems/Modifiers/ModifierIndex.cs` (removal lines only), `src/Evenmere.Sim/Services/IProgressionService.cs` (`RemoveSource` member only, per ADR-0017), `src/Evenmere.Sim/Systems/Progression/ProgressionQueries.cs` (slot/unlock members only, per ADR-0017), `tests/Evenmere.Sim.Tests/Fakes/FakeCultureQueries.cs` + `FakeProgressionQueries.cs` (new members only), `tests/Evenmere.Sim.Tests/Scenarios/ScenarioJson.cs` (`TraditionSlotType` converter only), `tests/Evenmere.Sim.Tests/Culture/Traditions/**` (new) |

**Reads:** culture-tree.md (§How culture works, §Traditions, §Slots, §Architectural styles).

**Goal:** tradition cards in typed slots, swappable at any time, whose effects apply through the modifier index.

**Carried over from M1-04:**
- `ModifiersSystem` has no removal path yet. Add `RemoveSource(EffectSource)` to `ModifierIndex` (with a source→entries reverse index) and `IProgressionService`, because unslotting needs it, and so do a festival ending (M2-08) and a monument being demolished (M2-10).
- Tradition unlocks and slot counts are recorded but only exposed on the concrete `ProgressionQueries`. Add `IsTraditionUnlocked` and `TraditionSlotCount` to `IProgressionQueries` (ADR-0017).

**Steps:**

1. **State** (`CultureState`, new keys per ADR-0018): `[Key(7)] SlottedTradition[] Slotted` sorted by (slot type, index), where `SlottedTradition` is `{[Key(0)] TraditionSlotType Slot; [Key(1)] int Index; [Key(2)] TraditionId Tradition}` (`[ContentValued(ContentKind.Tradition)]`); slot counts stay in `ProgressionState.TraditionSlots`.
2. **`SetTradition(slotType, index, traditionId | null)`** (new `Command`):
   - Valid when the card is unlocked, its slot type matches (or the slot is Wild), and it isn't already slotted elsewhere. Rejects with `TraditionNotUnlocked`, `SlotMismatch`, `AlreadySlotted`.
   - Only one `style`-tagged card can be slotted at a time (`StyleAlreadySlotted`).
3. **Effects:** slotting registers the card's modifiers with the source `tradition:<id>`, and unslotting removes them via `RemoveSource`. Emit `TraditionChanged`.
4. **Queries** (`ICultureQueries`, per ADR-0017): `Slots()`, `UnlockedTraditions()` and `ActiveStyle()`.

**Acceptance:**

- Swapping cards changes `BonusPercent` the next tick.
- The Wild-slot rules are tested (any card fits Wild; Wild-only cards fit nothing else).
- A second style card is rejected.
- Unslotting removes every modifier of the source and nothing else (removal-isolation test).
- `scripts/verify.sh` passes.

**Out of scope:** the traditions board UI (M2-19); tradition content (M2-13b).

---

### M2-08 · Festivals

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-00 | `src/Evenmere.Sim/Systems/Culture/Festivals/**` (new: `FestivalScheduler.cs`, `Commands/HoldFestival.cs`), `src/Evenmere.Sim/Systems/Culture/CultureSystem.cs` (`// festivals (M2-08)` registration lines only), `src/Evenmere.Sim/Systems/Culture/CultureQueries.cs` (festival query members only), `src/Evenmere.Sim/Systems/Culture/CultureState.cs` (festival keys only), `src/Evenmere.Sim/Systems/Culture/Events.cs` (`FestivalStarted`, `FestivalEnded` only), `src/Evenmere.Sim/Systems/Population/PopulationSystem.cs` (festival-pause read line only — only if the M1 flag needs wiring), `tests/Evenmere.Sim.Tests/Culture/Festivals/**` (new) |

**Reads:** culture-tree.md (§Festivals).

**Goal:** festivals that cost goods and give a burst of culture and a pause from decline.

**Steps:**

1. **State** (`CultureState`, new keys per ADR-0018): `[Key(8)] FestivalRun? Active` where `FestivalRun` is `{[Key(0)] FestivalId Festival; [Key(1)] EntityId Ground; [Key(2)] int DaysLeft}`; `[Key(9)] int[] FestivalLastHeldYear` indexed by `FestivalId` (`[ContentIndexed(ContentKind.Festival)]`); `[Key(10)] bool FestivalRunning` (the flag Population reads; defaults false).
2. **`HoldFestival(groundId, festivalId)`** (new `Command`) is valid when:
   - The festival is unlocked and the season matches (`FestivalDef.Seasons`, empty means any).
   - No festival is running island-wide, and this type hasn't been held this year.
   - The goods are available, after the `festival_calendar` modifiers (`festivalCost` stat discount on the cost).
3. **Running:** the goods are taken at start. Each day, credit `culturePerDay` × the season multiplier (`SeasonBonus`), set the "festival running" flag that Population reads to pause decline timers, and apply the move-in (`settlerArrival`) and visitor multipliers through the modifier index (source `festival:<id>`, removed at end via `RemoveSource`).
4. **Events:** emit `FestivalStarted` and `FestivalEnded`, and add to the `festivalsHeld` counter for conditions.

**Acceptance:**

- Each validation rule is tested (locked, wrong season, one-running, held-this-year, goods short).
- Decline timers pause during the festival and resume after it (Population integration test).
- Culture credit is exact, including the autumn ×1.5 Harvest Fair bonus.
- `scripts/verify.sh` passes.

**Out of scope:** the festival panel UI (M2-27); festival content (M2-13b).

---

### M2-09 · Inspirations and the full condition vocabulary

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-00 | `src/Evenmere.Sim/Systems/Progression/Conditions/**` (`ConditionEvaluator.cs` new-kind lines, `CounterKinds.cs` if new counters are needed), `src/Evenmere.Sim/Systems/Roads/RoadsState.cs` (per-type tile-count array key only), `src/Evenmere.Sim/Systems/Roads/RoadPlacement.cs` (count-increment lines only), `src/Evenmere.Sim/Systems/Roads/RoadsQueries.cs` (`RoadTiles(road)` only, per ADR-0017), `tests/Evenmere.Sim.Tests/Progression/Conditions/**` (new truth-table tests + benchmark), `tests/Evenmere.Sim.Tests/Roads/RoadCounts/**` (new) |

**Reads:** architecture.md (§Conditions vocabulary); tech-tree.md and culture-tree.md (the inspiration columns).

**Goal:** every condition used by Era I to Era IV inspirations, happenings and tutorial steps can be evaluated, cheaply and deterministically.

**Steps:**

1. Make `roadTiles` with a `road` type work: `RoadsState` gains the next-key `long[] TilesByRoadType` (`[ContentIndexed(ContentKind.Building)]`, incremented on place/remove/upgrade), read through `IRoadsQueries.RoadTiles(road)` (ADR-0017).
2. Implement the remaining kinds in `ConditionEvaluator`: `consumed`, `discovered`, `expeditions`, `festivalsHeld`, `greatWorks` (reads a counter that stays at zero until M3-03), `visitorsPerDay`, `season`, `era`, buildings-on-terrain counts ("buildings on hills", via `BuildingSelector` + terrain), and `regionsRevealed` by biome. Remove the `TODO(M2-09)` fallbacks one by one; none remain.
3. **Change-driven evaluation:** maintain a dirty set of counters, and re-evaluate only the conditions that depend on changed counters (dependency index condition→counter keys, evaluated at day start and on demand for inspirations).
4. **Tooling:** add a test-only command (in tests, not the sim contract) that dumps the evaluation status of every node's inspiration.

**Acceptance:**

- Truth-table tests for each kind (true and false cases, boundary `atLeast`).
- With 300 conditions, daily evaluation takes ≤ 0.05 ms on average (benchmark test on the development Mac).
- `scripts/verify.sh` passes.

**Out of scope:** tutorial steps (M4-04); great-works behaviour (M3-03).

---

### M2-10 · Monuments: multi-stage construction

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-00 | `src/Evenmere.Sim/Systems/Construction/**` (`ConstructionSystem.cs` stage-switching lines, `SiteMath.cs` if stage maths is needed, `ConstructionQueries.cs` stage members, `Events.cs` `MonumentStageCompleted` only, `MonumentStages.cs` new if a helper is cleaner — state the choice), `src/Evenmere.Sim/Systems/Construction/ConstructionState.cs` (`SiteRecord.StageIndex [Key(8)]` only, per ADR-0018), `tests/Evenmere.Sim.Tests/Construction/Monuments/**` (new) |

**Reads:** culture-tree.md (§Monuments and wonders); progression.md (stage bills).

**Goal:** monuments and wonders built in visible stages, each with its own bill, applying their effects on completion.

**Steps:**

1. **Stages:** a building with a `MonumentDef` (looked up by building type) is a site whose bill switches per stage: `StageIndex` advances when the stage's material steps complete. Emit `MonumentStageCompleted(stage)`, and expose the stage and stage count through `IConstructionQueries` (`SiteInfo` gains both, per ADR-0017) for visuals.
2. **Effects:** on the last stage, apply the monument's effects through `IProgressionService` (source `monument:<id>`). Uniqueness per island is enforced at placement with the existing `UniqueAlreadyBuilt` rule (`placement.unique` in content).
3. **Material cost:** the `materialCost` modifier (for example `benefactors`) scales the stage bills.
4. **Demolition:** demolishing a finished monument removes its effects via `RemoveSource` and refunds 50% of the final stage (consistent with M1-06's active-building rule).

**Acceptance:**

- Stage-by-stage consumption is tested (bill switches, stalls resume per stage).
- Effects apply exactly once, on the last stage.
- Demolishing a finished monument removes its effects and refunds 50% of the final stage.
- `scripts/verify.sh` passes.

**Out of scope:** the monument visuals and inspector (M2-27); monument content (M2-13b).

---

### M2-11 · Era advancement and gateways

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-09 | `src/Evenmere.Sim/Systems/Progression/Eras/**` (new: `EraAdvancement.cs`), `src/Evenmere.Sim/Systems/Progression/ProgressionSystem.cs` (era-check call lines only), `src/Evenmere.Sim/Systems/Progression/Events.cs` (new: `EraBegan`), `tests/Evenmere.Sim.Tests/Fakes/FakeProgressionQueries.cs` (`GatewayStatus` member only), `tests/Evenmere.Sim.Tests/Progression/Eras/**` (new), `tests/fixtures/scenarios/era1_gateways.json` (new) |

**Reads:** ADR-0010 (in full); ADR-0017 (`GatewayStatus`, `EraBegan`).

**Goal:** an era begins exactly when both gateways are complete, and later-era nodes stay locked until then.

**Steps:**

1. **Era check** (`EraAdvancement.cs`): after research and culture complete each day (called from `ProgressionSystem`'s daily step, step 12 — no pipeline change), read the next era's `RequiresTech`/`RequiresCivic` (`EraDef`, content already carries them). When both gateways are complete, set `ProgressionState.Era` and emit `EraBegan`.
2. **Advisor hook:** `GatewayStatus(era)` on `IProgressionQueries` reports (tech done?, civic done?) so advisors can say "Era II needs Village Moot (culture)".
3. **Locking:** no code change — M1-11 already gates availability on `CurrentEra ≥ node era`. Add tests that era-II nodes stay unavailable until `EraBegan`.

**Acceptance:**

- Scenario `era1_gateways.json`: completing only one gateway doesn't advance the era; completing both advances it the same tick and emits `EraBegan`; era-II nodes become available.
- `scripts/verify.sh` passes.

**Out of scope:** the Era II definition content (M2-13b); era visuals.

---

### M2-12 · Happenings

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-09 | `src/Evenmere.Sim/Systems/Happenings/**` (fill `HappeningsState.cs`, `HappeningsSystem.cs`, `HappeningsQueries.cs`, `Events.cs`; new `Commands/ChooseHappeningOption.cs`), `src/Evenmere.Content/Lint/Rules/HappeningGentlenessRule.cs` (new, code EVL012), `tests/Evenmere.Sim.Tests/Fakes/FakeHappeningsQueries.cs` (new), `tests/Evenmere.Sim.Tests/Happenings/**` (new, incl. gentleness lint fixtures under `tests/fixtures/content/lint/EVL012-*/`) |

**Reads:** architecture.md (§Happenings and advisors); culture-tree.md (festival requests).

**Goal:** gentle, data-driven events with choices and no harsh outcomes.

**Steps:**

1. **State** (`HappeningsState`, new keys per ADR-0018): `[Key(0)] PendingHappening? Pending` where `PendingHappening` is `{[Key(0)] HappeningId Happening; [Key(1)] long RaisedTick}`; `[Key(1)] long[] CooldownUntil` indexed by `HappeningId` (`[ContentIndexed(ContentKind.Happening)]`); `[Key(2)] TempModifierRecord[] TempModifiers` where each is `{[Key(0)] HappeningId Happening; [Key(1)] long ExpiresTick}` for duration-based option modifiers.
2. **Daily roll:** each day, eligible happenings (trigger condition true via `ConditionEvaluator`, cooldown elapsed, none pending) are rolled with the system's own `Pcg32` at their `chancePerMille` (per-mille integer roll). At most one is pending at a time; emit `HappeningPending`.
3. **Choosing:** `ChooseHappeningOption(happeningId, optionIndex)` (new `Command`) applies the option's effects: `grantGoods` immediately; modifiers applied with source `happening:<id>` and recorded in `TempModifiers` when `durationDays > 0`, removed via `RemoveSource` at expiry; `requestFestival` discounts recorded for M2-08's cost check (`festivalCost` stat with a happening source). If the player doesn't choose, the happening times out after 10 days with its `defaultOption`; emit `HappeningResolved`.
4. **Lint rule EVL012** (the gentleness check): no option's effects may be net-negative — they may cost goods only in exchange for a benefit (grant, modifier, or festival discount). New file in `Lint/Rules/`, registered beside the other rules.

**Acceptance:**

- Determinism across a save round trip while a happening is pending (hash test).
- Timeout applies the default option on exactly day 10.
- The gentleness lint fixture fails as intended (a cost-without-benefit option).
- `scripts/verify.sh` passes.

**Out of scope:** the happening dialog UI (M2-27); happening content (M2-13b).

---

### M2-13a · Era I production content

| Lane | Depends on | Owns |
| --- | --- | --- |
| content | M2-04, M2-05, M2-14, M2-30 | `content/goods/*.json` (Era I files only: `stone`, `apples`, `cider`, `clay`, `pottery`), `content/buildings/*.json` (Era I production/infrastructure only: `quarry`, `clay_pit`, `potters_kiln`, `orchard`, `cider_press`, `scouts_lodge`, `timber_bridge`, `stone_storehouse` excluded — upgrade variant owned by M2-30 — `plaza`, `cobbled_road`; never `standing_stones`, owned by M2-13b), `content/recipes/*.json` (Era I only), `content/tiers/*.json` (Villagers comfort-needs lines only), `content/locale/en/era1-production.json` (new), `game/data/visuals/era1-production.json` (new) |

**Reads:** progression.md (Era I tables: goods, buildings, recipes, tiers); tech-tree.md (Era I unlock mapping); architecture.md (§Content shapes); ADR-0010 (tier gating).

**Goal:** the complete Era I production side as data: chains, comfort needs and the buildings that M2 systems drive.

**Steps:**

1. **Goods and chains:** the remaining Era I goods, buildings and recipes: stone, apples, cider, clay and pottery; the Quarry, Clay Pit, Potter's Kiln, Orchard and Cider Press; the Scout's Lodge, Timber Bridge (`bridgeSpan` 3, `roadCost` per tile), Plaza, Cobbled Road (`roadCost` 800). Values from progression.md Era I tables.
2. **Seasonal tags:** tag `grain_farm` and `orchard` (edit their `tags` lines only — M1-02 owns the files, but tags are additive data; list both files explicitly: `content/buildings/grain_farm.json`, `content/buildings/orchard.json`, `tags` lines only) `seasonal` to match the M2-05 curve. Re-baseline the slice scenario hashes with the reason stated (seasonal output changes yearly totals).
3. **Villagers' comfort needs:** cider and pottery in `content/tiers/villagers.json` (comfort lines only), per progression.md.
4. **Visuals:** a mapping for every new building in `game/data/visuals/era1-production.json`.
5. **Locale:** `content/locale/en/era1-production.json` with `<type>.<id>.name` and `.desc` for every item.

**Acceptance:**

- `evenmere lint` is clean, including EVL010 (M2-14) and EVL011.
- The tables in progression.md match the content, and every deviation is listed in the PR with a docs update.
- Slice scenario hashes re-baselined once, with the seasonal-output reason in the commit body.
- `scripts/verify.sh` passes.

**Out of scope:** trees, traditions, festivals, monuments, happenings, era II (M2-13b); upgrade variants (M2-30).

---

### M2-13b · Era I trees, monuments, happenings and era content

| Lane | Depends on | Owns |
| --- | --- | --- |
| content | M2-06, M2-07, M2-08, M2-10, M2-11, M2-12, M2-13a, M2-14 | `content/techs/*.json` (8 new Era I files: `stonecutting`, `pathfinding`, `pottery`, `orcharding`, `bridge_building`, `letters`, `masonry`, `town_charter` — `gateway: true` on the last; never edit the 5 slice files), `content/civics/*.json` (7 new: `oral_tradition`, `kinship`, `harvest_rites`, `sea_lore`, `craft_lore`, `remembrance`, `village_moot` — `gateway: true` on the last), `content/traditions/*.json` (8 Era I cards: `common_hearth`, `fireside_tales`, `many_hands`, `harvest_festival`, `tide_blessing`, `village_crafts`, `old_ways`, `common_voice`), `content/festivals/harvest_fair.json`, `content/buildings/standing_stones.json` + `content/monuments/standing_stones.json` (stages + effects), `content/buildings/{scribe's_hall→`scribe_hall`, `storytellers_circle`→`storyteller_circle`, `festival_ground`, `stone_storehouse` excluded (M2-30)}`, `content/happenings/*.json` (8–10 Era I happenings), `content/eras/township.json` (`order: 2`, `requires: {tech: town_charter, civic: village_moot}`), `content/locale/en/era1-trees.json` (new), `content/locale/en/names.json` (new: `discovery.<kind>.<index>` keys per ADR-0019), `game/data/visuals/era1-trees.json` (new) |

**Reads:** progression.md (Era I); tech-tree.md (Era I costs, inspirations, gateway); culture-tree.md (Era I civics, traditions, festivals, Standing Stones); architecture.md (§Content shapes).

**Goal:** the complete Founding era trees and culture moments as data.

**Steps:**

1. **Techs:** all 13 Era I techs with inspirations (the 5 slice techs already exist; add the 8 listed). `town_charter` sets `gateway: true`; in M2 its effects are limited to the gateway itself — `unlockTier townsfolk` and `town_hall` come in M3-06, once Era II goods exist.
2. **Civics:** all 8 Era I civics with slot grants (`traditionSlot`), the 8 Era I traditions (unlocked by the civics above), the Harvest Fair, and `village_moot` with `gateway: true`.
3. **Culture buildings:** Scribe's Hall, Storyteller's Circle, Festival Ground (M2-30 owns `stone_storehouse.json`, the upgrade variant).
4. **Monument:** the Standing Stones building + `monuments/standing_stones.json` with stage bills and the island-wide effect (+4 culture, +5 attractiveness).
5. **Era II definition:** `content/eras/township.json` with the gateway requirements.
6. **Happenings:** 8–10 Era I happenings (travelling scholar, bountiful catch, Harvest Fair request, shipwreck salvage, lost lamb, storyteller's visit, fair-weather season, stone find), each with at least one option and a default, all passing EVL012.
7. **Visuals + names:** mappings for every new building; `names.json` with deterministic discovery-name tables.

**Acceptance:**

- Lint is clean, including EVL010, EVL011 and EVL012.
- The tables in progression.md and the tree docs match the content, and every deviation is listed in the PR with a docs update.
- `scripts/verify.sh` passes.

**Out of scope:** production content (M2-13a); upgrade variants (M2-30).

---

### M2-14 · Lint rule EVL010: lower-tier staffing

| Lane | Depends on | Owns |
| --- | --- | --- |
| content-infra | M2-00 | `src/Evenmere.Content/Lint/Rules/TierStaffingRule.cs` (new, code EVL010), `src/Evenmere.Content/Lint/LintCodes.cs` (EVL010 entry only), `src/Evenmere.Content/Lint/ContentLinter.cs` (registration line only), `tests/Evenmere.Content.Tests/Lint/**` (`TierStaffingTests.cs` new), `tests/fixtures/content/lint/EVL010-*/**` (new) |

**Reads:** progression.md (§How needs work: the design rule); ADR-0010.

**Goal:** a tier's basic goods and services are always made and staffed by lower tiers.

**Steps:**

1. For each tier T, find every building that produces T's basic goods, transitively through recipe inputs, plus every building that provides T's basic services (market coverage counts as the Market/Plaza line).
2. Each of those buildings must declare a workforce tier lower than T, after crew reductions (ADR-0015): read the effective crew via the same rounding the sim uses. Settlers are exempt, as the first tier.
3. Report the offending building and the chain that led to it (good → recipe → building, one line per hop).

**Acceptance:**

- A failing fixture reports the right building and chain (golden diagnostic text).
- The M1 content passes; the M2-13a/b content passes (M2-13a/b depend on this card).
- `scripts/verify.sh` passes.

**Out of scope:** the gentleness rule (EVL012, M2-12).

---

### M2-15 · Save versioning, migrations and autosave rotation

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-00 | `src/Evenmere.Sim/Persistence/**` (`Migration_0001_to_0002.cs` new, `MigrationChain.cs` registration lines, `SaveFormat.cs` version line, `RemapPass.cs` new-member lines as needed), `tests/Evenmere.Sim.Tests/Persistence/**` (migration tests), `tests/fixtures/saves/**` (`v1-slice.evsave` stays; `state-keys.json` re-baselined), `game/host/Persistence/**` (`SaveSlots.cs`/`SaveService.cs` autosave-rotation lines only) |

**Reads:** ADR-0007 (in full); ADR-0018.

**Goal:** saves from every past version load, and autosaves never lose more than a few minutes.

**Steps:**

1. **Migration framework in use:** `Migration_0001_to_0002` per ADR-0018 (size new arrays to content, default new scalars, re-run the content-ID remap over new content-indexed members), and a test that loads every fixture in `tests/fixtures/saves/` and steps it to the current version.
2. **Format version 2:** bump for the M2 state additions (ExplorationState, HappeningsState, Culture/Construction/Roads keys). Re-baseline `state-keys.json` once with the reason stated.
3. **Autosave rotation** in the game host: 3 rotating slots every 10 game days, plus an autosave before demolishing more than 10 buildings at once.

**Acceptance:**

- The v1 fixture migrates and continues deterministically (save at 20k → load → to 40k equals uninterrupted).
- The autosave slots rotate (host test with a fake clock).
- `scripts/verify.sh` passes.

**Out of scope:** later version bumps (only if a card changes a key's meaning — expected: none); the cheats flag (M2-26 adds it as a version-2 key).

---

### M2-16 · Replay and performance harness, reference saves

| Lane | Depends on | Owns |
| --- | --- | --- |
| tools | M2-15 | `src/Evenmere.Tools/Perf/**` (new: `PerfRunner.cs`, `PerfReport.cs`), `src/Evenmere.Tools/Commands/PerfCommand.cs` (new), `src/Evenmere.Tools/EvenmereCli.cs` (`perf` registration lines only), `tests/fixtures/saves/perf-2k.evsave` (new, provisional), `tests/fixtures/saves/perf-20k.evsave` (new, provisional synthetic), `.github/workflows/nightly.yml` (perf-job section only) |

**Reads:** architecture.md (§Performance budgets).

**Goal:** measure tick cost on realistic saves, every night.

**Steps:**

1. **`evenmere perf --save <file> --ticks 2000`:** reports the raw average, p50, p99 and maximum per system against the budget table, plus the Deck-equivalent average and p99 (raw × the Mac→Deck factor, a named constant in one place). It exits non-zero above budget × 1.25 when `--enforce` is set; enforcement compares the Deck-equivalent average against the budget table.
2. **Replay** (`evenmere replay`, exists since M1-14): confirm it prints the hash; no change needed unless the version-2 envelope requires it (state that in the report).
3. **Reference saves:** build `perf-2k` (about 2,000 residents) with a deterministic dense-placement generator script (the Era I bot doesn't exist yet — M2-17b replaces this save with a bot-grown one). `perf-20k` is a synthetic stress save (dense placement through the same script) and is marked provisional until M4.
4. **Nightly:** add perf runs of both saves to the nightly workflow, uploading the reports. The nightly records the raw numbers for the trend (CI runners differ from the dev Mac); the gate binds on the dev-Mac measurement.

**Acceptance:**

- Perf reports for both saves are attached to the PR; the `perf-2k` Deck-equivalent average (raw Mac average × 3.0) is ≤ 0.6 ms.
- Replaying the slice log matches its golden hash.
- `scripts/verify.sh` passes.

**Out of scope:** the balance bot (M2-17a/b); budget tuning (report only).

---

### M2-17a · Balance bot v2: the Era I island bot

| Lane | Depends on | Owns |
| --- | --- | --- |
| tools | M2-04, M2-13b, M2-16 | `src/Evenmere.Tools/Balance/EraOne/**` (new: `EraOneBot.cs`, `ExpeditionPlanner.cs`, `TraditionHeuristics.cs`), `tools/bots/era1.json` (new), `tests/Evenmere.Sim.Tests/Balance/**` (bot determinism test, if the project pattern allows — else a tools test beside the bot) |

**Reads:** progression.md (§Pacing targets); ADR-0010; `tools/bots/slice.json` (v1 patterns).

**Goal:** the bot plays any seed through Era I to both gateways using only commands and queries, and reports pacing.

**Steps:**

1. **Era I bot script** (`tools/bots/era1.json`): the full era, including expeditions (via `StartExpedition` on revealed-adjacent regions), bridges (river crossings through `timber_bridge`), traditions (a heuristic slotting priority list), festivals (Harvest Fair when affordable), the Standing Stones, and research and civic queues that follow the gateway paths (`town_charter`, `village_moot`).
2. **Generated islands:** the placement search handles rivers (bridge or build around) and avoids fog (`IsRevealed` check before placing).
3. **Reactive rules:** keep v1's (a good's fulfilment below 90% for 2 days adds the bottleneck producer) and add: an idle Scout's Lodge starts an expedition; a pending happening is answered on day 5.
4. **Report** (JSON plus text): time to each milestone in §Pacing targets, stalls (no resident growth for a full season, with the top advice at the time), peak and average tick time, final hash.
5. **Command:** `evenmere balance --bot tools/bots/era1.json --seed S --size 320 --max-minutes 150`.

**Acceptance:**

- On the starter island and seeds 1–5, the bot reaches both gateways with no stall; the report tables the real pacing per map (time to each §Pacing-targets milestone, stalls, final hashes) so the developer can confirm or move the provisional 90–120 minute window before M2-17b tunes to it.
- The bot is deterministic: same seed, same report (hash test).
- `scripts/verify.sh` passes.

**Out of scope:** multi-seed runs and the nightly job (M2-17b); pacing tuning (M2-17b).

---

### M2-17b · Multi-seed balance runs and pacing sign-off

| Lane | Depends on | Owns |
| --- | --- | --- |
| tools | M2-17a, plus the developer's confirmation of the pacing window (human — confirm or move the provisional 90–120 minutes before any tuning) | `src/Evenmere.Tools/Balance/MultiSeed/**` (new: `MultiSeedRunner.cs`), `src/Evenmere.Tools/EvenmereCli.cs` (`--seeds` flag lines on the balance command only), `.github/workflows/nightly.yml` (balance-job section only), `tests/fixtures/saves/perf-2k.evsave` (regeneration from a bot save only) |

**Reads:** progression.md (§Pacing targets).

**Goal:** prove pacing on any island, and lock the reference save to real gameplay.

**Steps:**

1. **Multi-seed:** `--seeds 1..5` runs the seeds in sequence and produces a summary table (time to each milestone in §Pacing targets, stalls, final hashes).
2. **Nightly:** the nightly job runs 5 generated seeds plus the starter island.
3. **Tuning:** if Era II timing falls outside the developer-confirmed window, tune content data only (costs, outputs, curve values), with a rationale in the PR; never tune the bot to the content.
4. **Reference save:** replace the provisional `perf-2k` with a bot-grown save at about 2,000 residents and re-baseline its hash.

**Acceptance:**

- The Era II timing on all 6 maps falls within the developer-confirmed window with no stall longer than one season, or the PR includes content tuning (as data changes) with a rationale.
- The regenerated `perf-2k` perf report is attached.
- `scripts/verify.sh` passes.

**Out of scope:** Era II+ balance (M3); engine changes for pacing (report as follow-ups).

---

### M2-18 · Tech tree graph view

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-09 | `game/ui/research/**` (`TreeView.cs`, `TreeLayout.cs`, `TreeNodeView.cs`, all new; `ResearchPanel.cs`/`ResearchViewModel.cs` wiring lines only) |

**Reads:** tech-tree.md (Era I); AGENTS.md (§Godot conventions: input).

**Goal:** a readable, navigable tech tree.

**Steps:**

1. **Layout** (`TreeLayout.cs`, pure math, unit-tested without Godot): columns by era, layered by prerequisite depth, with edges drawn as curves. The gateway is highlighted.
2. **Nodes:** state (done, available, queued, locked) from `IResearchQueries`, cost and progress, the inspiration indicator with its condition text (via `IProgressionQueries.Evaluate` read through the existing research view-model), and an unlock list with icons.
3. **Interaction:** click to set the target (`SetResearchTarget`), shift-click to queue (`SetResearchQueue`), pan and zoom, and full gamepad navigation (focus follows edges with the existing `ui_*` focus actions — no new input actions).
4. **Gamepad gate item:** focus must reach every Era I node from any other via directional input at 1280 × 800, proven by the M2-26 focus walk (this card adds no separate automation).

**Acceptance:**

- Screenshots at 1280 × 800 and 2560 × 1600; Era I tree navigation is covered by the M2-26 focus walk (no separate video needed).
- A swap of target/queue is reflected after the next tick batch.
- `REQUIRE_GODOT=1 scripts/verify.sh` passes.

**Out of scope:** the civics tree (M2-19); new input actions (none — uses `ui_*` focus).

---

### M2-19 · Civics tree and traditions board

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-07 | `game/ui/culture/**` (`CivicsTreeView.cs` new, reusing the M2-18 layout component; `TraditionsBoard.cs`, `TraditionCardView.cs` new; `CulturePanel.cs`/`CultureViewModel.cs` wiring lines only) |

**Reads:** culture-tree.md (§Traditions, §Slots); AGENTS.md (§Godot conventions: input).

**Goal:** the culture tree, plus a board for slotting tradition cards.

**Steps:**

1. **Civics tree:** the same component as M2-18, with civic data from `ICultureQueries`.
2. **Traditions board:** slots grouped by type (`Slots()`); owned cards in a drawer (`UnlockedTraditions()`); drag-and-drop, and a gamepad picker (focusable card list + slot list, `ui_accept` to slot — no new input actions); effect text from locale keys; style cards preview their style name (`ActiveStyle()`).
3. **Feedback:** after a swap, show the changed bonuses in a calm summary (for example "Farms +20% (autumn)") from `ListModifiers` before/after, refreshed on `TraditionChanged`.

**Acceptance:**

- Screenshots at both resolutions, and a swap reflected in the inspector's productivity breakdown.
- The gamepad picker slots and unslots a card at 1280 × 800; panel-wide reachability is covered by the M2-26 focus walk.
- `REQUIRE_GODOT=1 scripts/verify.sh` passes.

**Out of scope:** festival/monument/happening UI (M2-27); new input actions (none).

---

### M2-20 · Overlays: coverage, reach, fertility and deposits

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-00 | `src/Evenmere.Sim/Queries/IOverlayQueries.cs` (new, per ADR-0017), `src/Evenmere.Sim/Systems/Overlays/**` (new: `OverlayQueries.cs`, cached per-tick layers — no serialized state), `tests/Evenmere.Sim.Tests/Fakes/FakeOverlayQueries.cs` (new), `tests/Evenmere.Sim.Tests/Overlays/**` (new), `game/rendering/overlays/**` (new) |

**Reads:** architecture.md (§UI: overlays); progression.md (§Terrain and access ladder).

**Goal:** make invisible rules visible.

**Steps:**

1. **Sim side:** `IOverlayQueries` produces per-tile byte layers for storehouse reach, market and service coverage, fertility per crop, deposits and buildable-with-access (terrain buildable, slope OK, access unlocked, revealed). Layers are computed on demand from `IMapQueries`/`IRoadsQueries`/`IProgressionQueries` and cached in the queries object by (tick, layer) — no serialized state, no pipeline change.
2. **Game side:** shader overlays with a legend. They show automatically during placement for the relevant building (for example fertility when placing a farm).
3. **Gamepad:** overlay toggle is a focusable toolbar button (no new input action).

**Acceptance:**

- The overlays are correct on fixture maps (sim tests: reach ring, coverage edge, fertility mask, deposit dots).
- Screenshots of each overlay on the starter island.
- `REQUIRE_GODOT=1 scripts/verify.sh` passes.

**Out of scope:** fog rendering (M2-23); new input actions (none).

---

### M2-21 · Production chain viewer

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-00 | `game/ui/chains/**` (new) |

**Reads:** progression.md (§Buildings); architecture.md (pillar 4 "Every number explains itself").

**Goal:** a panel that shows any chain as a graph, with live rates and bottlenecks.

**Steps:**

1. **Graph:** pick a good, and the chain graph is built from `ContentDb` recipes (no new sim surface).
2. **Live data:** annotate each node with the building count, average productivity and produced/consumed per day from `IStatisticsQueries` and `IProductionQueries`.
3. **Bottlenecks:** highlight the bottleneck from `ExplainGood`. Clicking a node lists its buildings and focuses the worst one.

**Acceptance:**

- Screenshot of the bread chain with a starved windmill highlighted.
- Values match `IStatisticsQueries` (a debug label in the report).
- `REQUIRE_GODOT=1 scripts/verify.sh` passes.

**Out of scope:** new sim queries (none — reads existing ones); new input actions (none).

---

### M2-22 · Statistics graphs

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-00 | `game/ui/stats/**` (new) |

**Reads:** architecture.md (pillar 4); `src/Evenmere.Sim/Systems/Statistics/StatisticsQueries.cs` (existing `Series` semantics).

**Goal:** calm graphs of how the island is doing.

**Steps:**

1. **Line charts:** population per tier, goods stock and net per day, and knowledge and culture per day, from `IStatisticsQueries.Series` (daily samples, newest last).
2. **Windows:** 40 days or 2 years; hover shows exact values.
3. **Accessibility:** a colour-blind-safe palette from the theme (`evenmere_theme.tres` palette lines only if new colours are needed — otherwise no theme edit).

**Acceptance:**

- Screenshots at both resolutions, and values matching `IStatisticsQueries` (a debug label).
- `REQUIRE_GODOT=1 scripts/verify.sh` passes.

**Out of scope:** new sim queries (none); new input actions (none).

---

### M2-23 · Fog, regions and expedition UI

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-06 | `game/rendering/fog/**` (new), `game/ui/exploration/**` (new) |

**Reads:** architecture.md (§Exploration).

**Goal:** see what is unknown, and send scouts.

**Steps:**

1. **Fog:** a soft volumetric-looking shader over unrevealed regions (from `IExplorationQueries.IsRevealed`), with region outlines on hover.
2. **Expedition picker:** from a Scout's Lodge, pick an adjacent fogged region. It shows the ETA (same inputs the sim uses: base days + distance × terrain factor, shown via a sim pure helper if M2-06 exposed one, else duplicated formula cited in code) and confirms with `StartExpedition`.
3. **Reveals:** a reveal animation on `RegionRevealed`, and discovery cards with their effects (locale keys from `names.json`).

**Acceptance:**

- A screenshot sequence of an expedition from start to reveal.
- `REQUIRE_GODOT=1 scripts/verify.sh` passes.

**Out of scope:** new input actions (none — buttons and focus); Overlooks (M3-02).

---

### M2-24 · Seasons, weather and smoke visuals

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-05 | `game/rendering/seasons/**` (new), `game/rendering/effects/**` (new) |

**Reads:** progression.md (season yield curve); ADR-0013 (art direction).

**Goal:** the year is visible, and the city breathes.

**Steps:**

1. **Seasonal palette:** palette shifts per season (foliage, fields, light colour) from `GameClock.Season`, blended over two game days. Must use the shared palette resource (`game/data/visuals/palette.tres`, owned by M2-29 — read-only use here, no edit).
2. **Seasonal effects:** a light snow tint in winter and blossom particles in spring, both very subtle.
3. **Smoke:** chimney smoke from active production buildings, as GPU particles budgeted by zoom.

**Acceptance:**

- Screenshots of all four seasons, and the fps budget held (smoke on, default view).
- `REQUIRE_GODOT=1 scripts/verify.sh` passes.

**Out of scope:** the hand-drawn look itself (M2-29); real building models (M4-14 onward).

---

### M2-25 · New-game setup: seed, size, starter island

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-02b | `game/ui/menus/NewGame/**` (new: `NewGameScreen.cs` + `.tscn`) |

**Reads:** ADR-0019; `src/Evenmere.Sim/NewGame/**` (existing setup semantics).

**Goal:** choose a starter or generated island.

**Steps:**

1. **New-game screen:** a choice between the starter island and a generated island, with a seed field (random by default), size (256, 320 or 384) and a preview thumbnail rendered from `MapDef` (terrain colours only, no sim needed — the host generates the `MapDef` off-thread and passes it as `NewGameOptions.Map`).
2. **Start:** starts the game through `NewGameOptions` (unchanged).

**Acceptance:**

- Screenshots, and the same seed always gives the same preview (preview-hash test with fixed seeds).
- `REQUIRE_GODOT=1 scripts/verify.sh` passes.

**Out of scope:** the mapgen CLI gallery (M2-03); new input actions (none — form controls focus).

---

### M2-26 · Debug overlay and cheat console

| Lane | Depends on | Owns |
| --- | --- | --- |
| game + sim | M2-00 | `docs/adr/0012-cheat-commands.md` (new, the ADR itself), `src/Evenmere.Sim/Systems/Debug/**` (new: `CheatCommands.cs` records, `CheatHandlers.cs`), `src/Evenmere.Sim/Persistence/SaveHeader.cs` (`CheatsUsed` field only), `game/host/Persistence/SaveService.cs` (cheats-flag lines only), `game/ui/debug/**` (new: overlay + console), `game/host/Smoke/FocusWalk.cs` (new: the automated focus walk), `game/host/Debug/DebugLaunchOptions.cs` (`--focus-walk` flag line only), `game/project.godot` (`[input]` section: `debug_console` backtick binding only) |

**Reads:** ADR-0006 (threading: cheats enqueue like any command); ADR-0018 (the flag rides version 2).

**Goal:** tools for testers that cannot leak into normal play.

**Steps:**

1. **ADR-0012:** cheat commands are processed only when the save's `CheatsUsed` header flag is set. The flag is set at new-game time in debug builds or with a launch flag, is stored in the save header (one version-2 key, default false, no migration), and is shown in the load list without reading state.
2. **Commands:** `CheatGrantGoods`, `CheatCompleteNode`, `CheatSetEra`, `CheatRevealAll` and `CheatAddResidents`. Each handler first checks the flag and rejects otherwise.
3. **Overlay** (F3): fps, tick time per system (from the profiler), entity counts, the selected entity's raw state, and the hash.
4. **Console** (backtick, `debug_console` action): parses and enqueues cheat commands. Backtick and F3 are free per the M2-00 binding survey; never bind the d-pad, left stick, A or B.
5. **Focus walk** (`--focus-walk` debug launch flag, off by default): a scripted pass that opens each panel — HUD, build menu, inspector, research and civics trees, traditions board, notification feed, pause menu — and asserts every enabled control is reachable by `ui_*` focus navigation from the panel's first focus, with no traps. It prints a per-panel report and fails the run on the first unreachable control or trap. The Godot smoke invokes it when the flag is passed.

**Acceptance:**

- Cheats are rejected when the flag is off (sim test for each command).
- Screenshots of the overlay and console.
- The focus walk passes at 1280 × 800 across every listed panel (no unreachable controls, no traps); this is the M2 gate's focus-walk evidence.
- `REQUIRE_GODOT=1 scripts/verify.sh` passes.

**Out of scope:** balance tuning via cheats (report only); any non-cheat sim change.

---

### M2-27 · Festivals, monuments and happenings UI

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-08, M2-10, M2-12 | `game/ui/events/**` (new: festival panel, happening dialog), `game/ui/monuments/**` (new: monument inspector), `game/rendering/monuments/**` (new: stage visuals) |

**Reads:** culture-tree.md (§Festivals, §Monuments and wonders).

**Goal:** the culture moments feel special.

**Steps:**

1. **Festival panel:** opened from a Festival Ground. Shows the available festivals, their costs and effects, and why one can't be held (season, yearly cooldown, goods — from the `HoldFestival` rejection reasons). Holds the festival. While it runs, bunting and lantern visuals appear and a quiet HUD badge shows.
2. **Monument inspector:** stage progress and the bill for each stage (from `SiteInfo` stage members), with `MonumentStageCompleted` toasts. The visuals grow by stage.
3. **Happening dialog:** a calm card with an illustration slot, the options with effect previews, and a "decide later" button that keeps it pending.

**Acceptance:**

- Screenshots of each (panel with a blocked reason, staged monument, dialog with options).
- `REQUIRE_GODOT=1 scripts/verify.sh` passes.

**Out of scope:** new input actions (none — buttons and focus).

---

### M2-29 · Look development: hand-drawn rendering prototype

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M1 gate | `game/rendering/look/**`, `game/rendering/camera/**` (projection and yaw offset), `game/data/visuals/palette.tres` |

**Reads:** [ADR-0013](../adr/0013-art-direction.md); the art bible from [ART-01](ART-art-track.md) (`docs/art/style-guide.md` and `docs/art/palette.md`; `palette.tres` takes its tokens) and ART-04's painted textures (paper grain, watercolour wash, water strokes), when they have landed; architecture.md (§Rendering); the M2-31 card (the window-size rule: a bigger window shows more map at the same tile size, in logical points); the reference screenshots at [permit.petercai.com](https://permit.petercai.com).

**Goal:** prove the hand-drawn isometric look on the existing placeholder meshes, early, so later rendering work builds toward it.

**Steps:**

1. **Camera:**
   - Add an orthographic projection mode with zoom mapped to the orthographic size, keeping the 90° steps from the yaw offset. The orthographic size scales with the window height in logical points (same rule as M2-31's FOV: more window shows more island, tile size in points stays constant).
   - Check TilePicker, the virtual cursor and the placement tools under the orthographic projection.
   - Render the same views at 30° and 45° yaw for the developer to choose from.
2. **Outlines:** a full-screen ink-outline pass from depth and normal edges, with a noise-driven wobble and a line-weight setting. Roads and terrain get softer lines than buildings.
3. **Shading:** banded (toon) diffuse light, a warm key light with soft long shadows, a cool fill, and paper and watercolour grain with light edge darkening. Use one shared palette resource in a warm, slightly desaturated palette.
4. **Water:** painterly ripple strokes over the existing distance-field foam, with the cast shadows visible on the surface.
5. **River banks:** extend the water pass to river tiles: soft inked banks over the foam edge, so narrow and wide rivers read as water, not ditches (carried forward from the M1 gate).
6. **Toggle:** a setting to switch between the M1 look and the new look, so both can be compared.

**Acceptance:**

- Side-by-side screenshots of the Landing, the overview and a busy street. Show M1 against the new look, and 30° against 45° yaw, next to the reference.
- The developer picks the yaw and approves the look's direction.
- The frame time stays within budget on the development Mac and fits the Steam Deck budget at 1280 × 800.
- Picking and placement work in every projection.
- `CameraFovTests` (or its orthographic equivalent) covers the size-from-height rule: doubling the window height in points doubles the visible span at the same zoom.

**Out of scope:** real building models (M4-14 onward) and seasonal palettes (M2-24, which must use the shared palette).

---

### M2-30 · Upgrade lines: design pass, crew stat and Era I upgrades

| Lane | Depends on | Owns |
| --- | --- | --- |
| human + content | M2-00 | `docs/design/progression.md` (§Upgrade lines section and the Era I rows that name upgrades, only), `docs/design/tech-tree.md` and `docs/design/culture-tree.md` (Era I upgrade unlock entries only), `content/buildings/*.json` (Era I upgrade-variant files only, named by the mapping table — including `stone_storehouse.json`), `content/locale/en/era1-upgrades.json` (new), `src/Evenmere.Sim/Systems/Workforce/**` (`WorkforceMath.cs` crew lines, `WorkforceSystem.cs` crew + planned-demand lines), `src/Evenmere.Content/Definitions/Enums.cs` (`Stat.Crew` member only, per ADR-0015), `docs/adr/0015-crew-stat.md` (accept the Proposed draft — status line only) |

**Reads:** progression.md (§Upgrade lines, §Buildings); tech-tree.md; culture-tree.md; architecture.md (§Workforce, effects vocabulary); the EVL010 rule (M2-14); ADR-0016 (the planned-demand question).

**Goal:** buildings modernise as the trees advance. Techs and civics unlock upgraded versions with their own staffing, so jobs move up the tiers over time. This is the developer's idea from the M1 playtest.

**Steps:**

1. **Design pass, with the developer:**
   - Map an upgrade line for every production and service building across Eras I–IV. For each, give the base, the unlocking tech or civic, the upgrade, its staffing (tier × count), the output change and the upgrade cost.
   - Keep it historically grounded and aligned with the trees.
   - Check every row against EVL010.
   - Record it in progression.md §Upgrade lines and add the unlocks to tech-tree.md and culture-tree.md.
2. **Crew stat (ADR-0015):** a `crew` modifier stat lowers the required workers of matching buildings by a percentage: `max(1, (count × (1000 + 10 × P) + 500) / 1000)` in integer maths (half up, floored at 1). Workforce demand reads the effective crew (`Ratio.FromBonusPercent`, never subtract from 1000), and explanations show the source. Accept the Proposed ADR-0015 (flip the status line) if the semantics hold; anything else goes back as a counter-proposal.
3. **Planned-demand decision (ADR-0016 follow-up):** decide whether in-place upgrade sites count towards `PlannedDemand`, implement it in `WorkforceSystem.cs`, and record the decision as an addendum note in the report (and in ADR-0016's file only if the orchestrator agrees it is a forward fix — otherwise a new Proposed ADR).
4. **Era I content:** the Era I upgrade variants from the table (`upgradeOf`, staffing, cost, locale), using the existing in-place upgrade (`UpgradeBuilding`, `BuildingUpgraded`). `stone_storehouse.json` (upgrade of Storehouse: reach 26, 3 carts, 2 Settlers, 4 stone, Masonry) is the first row. The upgrade button in the inspector shows the staffing change (inspector text comes from existing queries — no game change).
5. **Later eras:** M3-05/M3-06 (Era II), M5-03 (Era III) and M5-08 (Era IV) build their eras' rows from this table.

**Acceptance:**

- The developer approves the mapping table.
- Era I upgrades pass lint, including EVL010.
- A scenario upgrades a building and shows the workforce demand moving to the new tier (e.g. Storehouse → Stone Storehouse keeps 2 Settlers; a same-tier crew reduction lowers headcount).
- The `crew` stat has unit tests (rounding, floor of 1, demand flow, explanation line).
- `scripts/verify.sh` passes.

**Out of scope:** Era II+ upgrade content (M3-05/M3-06); any game change beyond locale text.

---

### M2-31 · Window and input polish from the M1 playtest

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M1 gate | `game/project.godot` ([display] only), `game/host/Main.cs` and `game/host/WindowSetup.cs`, `game/ui/common/UiScale.cs`, `game/ui/hud/HudRoot.cs` (content-scale code only), `game/rendering/camera/**`, `game/ui/menus/PauseMenu.cs` and `PauseMenu.tscn`, `content/locale/en/ui-menus.json`, `game/ui/notifications/**` and `game/ui/build_menu/BuildMenu.cs` (overlap fix only), `tests/Evenmere.Game.Tests/**`, `docs/implementation/M2-era-one.md`, `docs/design/architecture.md` (only if it describes the stretch mode or the UI scale) |

**Reads:** AGENTS.md (§Godot conventions); `game/project.godot`; `game/host/Main.cs` and `WindowSetup.cs`; `game/ui/common/UiScale.cs`; `game/ui/hud/HudRoot.cs`; `game/rendering/camera/`; `game/ui/menus/PauseMenu.cs`; `game/ui/notifications/`; `game/ui/build_menu/BuildMenu.cs`.

**Goal:** the UI keeps its 1600 × 900 design size at any window size, a bigger window shows more island, fullscreen has a mouse- and gamepad-reachable button, the feed stays clear of the build menu, and scrolling a list never zooms the map.

**Steps:**

1. **UI scale policy:** drop the `canvas_items` stretch for the default `disabled` mode and set `ContentScaleFactor` from one pure function — `screenScale × uiScaleSetting × clamp(min(windowPointsW / 1600, windowPointsH / 900), 0.8, 1.0)` — re-applied on resize and screen change. Window sizes arrive as physical pixels on HiDPI macOS, so one shared helper converts them to logical points first.
2. **Map growth:** set the Camera3D vertical FOV from the window height in points (`tan(fov / 2) = tan(22.5°) × h / 900`, clamped 30°–75°), so one tile covers the same points at any size.
3. **Fullscreen button:** add a Fullscreen/Windowed button to the pause menu calling the same toggle as `toggle_fullscreen`, with locale keys in `ui-menus.json`.
4. **Feed overlap:** while the build menu is open the feed collapses to its Watching header chip (history and toasts hide) and restores afterwards.
5. **Zoom guard:** wheel, trackpad scroll and pinch zoom only when the pointer is over no blocking control; gamepad zoom, edge scroll and drag panning keep their rules.

**Acceptance:**

- `REQUIRE_GODOT=1 scripts/verify.sh` passes and the smoke hashes agree.
- Screenshots at 1280 × 800, 1600 × 900 and 2560 × 1600 points show same-size HUD chips at the two larger sizes with more island visible, plus a build-menu-open shot with two notices showing.
- A log line or test shows a wheel event over the build menu leaving the zoom unchanged.
- Godot-free tests cover the scale policy, the FOV formula, the zoom guard and the button labels.

---

### M2-28 · Era I playtest and gate review

| Lane | Depends on | Owns |
| --- | --- | --- |
| human | all M2 tasks | `docs/implementation/M2-era-one.md` (§Gate record) |

**Goal:** confirm the Founding era is complete, calm and well paced.

**Steps:**

1. Run the gate checks, and attach the bot summary and perf reports.
2. The developer plays a generated island to Era II.
3. Triage the findings into M3 cards.

**Acceptance:** all gate boxes are ticked or explicitly waived.

---

## Gate record

_Filled in when M2 closes._

## Answered (developer, 2026-10-25)

1. **Performance gate: Deck-equivalent only.** The M2 gate binds on the dev-Mac `perf-2k` average × the Mac→Deck factor 3.0 (single-core M4 Pro vs Deck Zen 2, about 3; recalibrated on hardware in M4), which must be ≤ 0.6 ms — so Mac ≤ 0.2 ms. `evenmere perf` prints raw and Deck-equivalent numbers; nightly records raw for the trend.
2. **Pacing window: measure first, then fix.** 90–120 minutes stays provisional. M2-17a reports real pacing on the starter island and 5 seeds; the developer confirms or moves the window; M2-17b tunes content to the confirmed window.
3. **Gamepad: automated only.** The hand check is dropped. The M2 gate needs every control reachable by `ui_*` focus navigation at 1280 × 800, proven by the M2-26 `--focus-walk` smoke pass. The developer hand-tests gamepad play with a controller before the first beta (the M4 public build).
