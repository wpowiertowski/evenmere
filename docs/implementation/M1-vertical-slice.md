# M1 — Vertical slice

**Goal:** prove the calm core loop on the hand-authored starter island:

- Settlers advance to Villagers through five production chains: wood, timber, fish, homespun and bread.
- Supporting systems: construction, roads, logistics with visible carts, workforce, needs with decline, the first techs and the first civic, a usable UI, and save/load.
- At the end of M1, **GitHub CI and the PR flow go live**. From then on, nothing lands on `main` without a green PR.

**Slice content** (defined fully in M1-02):

| Kind | Items |
| --- | --- |
| Goods | `wood`, `timber`, `fish`, `wool`, `homespun`, `grain`, `flour`, `bread` |
| Buildings | `founders_hall`, `settler_house`, `dirt_road`, `storehouse`, `market`, `woodcutters_hut`, `sawpit`, `fishing_hut`, `sheep_farm`, `weavers_cottage`, `grain_farm`, `windmill`, `bakery`, `shrine` |
| Tiers | `settlers`, `villagers` (comfort needs are omitted until M2) |
| Techs | `animal_husbandry`, `weaving`, `cultivation`, `milling`, `net_weaving` |
| Civics | `hearth_and_shrine` (unlocks the Shrine only; slots and traditions come in M2) |

**Gate:**

- [ ] **Bot:** balance bot v1 reaches 100 Villagers on the starter island in ≤ 45 min of game time, with no stall longer than one season. Its JSON report is attached to the gate record.
- [ ] **Golden hash:** scenario `slice_villagers` passes against its golden hash.
- [ ] **Save round trip:** saving and reloading at tick 20,000 continues to the same hash as an uninterrupted run (scenario).
- [ ] **Playtest:** the developer reaches 100 Villagers in ≤ 45 min using only the UI and no cheats, and records calmness and clarity notes.
- [ ] **Godot:** smoke v2 passes. The starter island holds ≥ 60 fps on the development Mac at default settings, and the build is playable with a gamepad at 1280 × 800.
- [ ] **CI:** GitHub Actions is green on `main`, branch protection is on, and the PR flow is documented.

**Waves:**

| Wave | Tasks | Notes |
| --- | --- | --- |
| A | M1-00, M1-02, M1-17, M1-18 | Interfaces, content, camera and HUD start together |
| B | M1-01, M1-04, M1-05, M1-06, M1-07, M1-08, M1-09, M1-10, M1-11, M1-12 | All sim systems in parallel, against M1-00's interfaces and fakes |
| C | M1-03, M1-13, M1-15, M1-19 | Starter map; integration; advisors; terrain rendering |
| D | M1-14, M1-16, M1-20 | Save/load; balance bot; buildings rendering |
| E | M1-21, M1-22, M1-23, M1-24, M1-25, M1-26, M1-27 | Interaction and panels |
| F | M1-28 → M1-29 → M1-30 | Smoke v2, CI, playtest |

## Task index

| ID | Title | Lane | Depends on | Status |
| --- | --- | --- | --- | --- |
| M1-00 | Cross-system interfaces, commands, events and test fakes | sim | M0 gate | todo |
| M1-01 | Map model, map loading and building registry | sim | M1-00 | todo |
| M1-02 | Vertical-slice content | content | M0-04 | todo |
| M1-03 | Starter island map | content | M1-01, M1-02 | todo |
| M1-04 | Progression core: unlocks, effects, modifiers, ledger, counters | sim | M1-00 | todo |
| M1-05 | Roads, connectivity, reach and coverage fields | sim | M1-00 | todo |
| M1-06 | Construction and placement validation | sim | M1-00 | todo |
| M1-07 | Workforce | sim | M1-00 | todo |
| M1-08 | Production | sim | M1-00 | todo |
| M1-09 | Logistics: carts and transport jobs | sim | M1-00 | todo |
| M1-10 | Needs and population | sim | M1-00 | todo |
| M1-11 | Research and civics | sim | M1-00 | todo |
| M1-12 | Statistics and explain queries | sim | M1-00 | todo |
| M1-13 | Integration: new game and end-to-end scenarios | sim | M1-01, M1-03 to M1-12 | todo |
| M1-14 | Save/load v1 and command log | sim | M1-13 | todo |
| M1-15 | Advisors v1 | sim | M1-12 | todo |
| M1-16 | Balance bot v1 | tools | M1-13 | todo |
| M1-17 | Camera rig and input actions | game | M0-09 | todo |
| M1-18 | UI theme, HUD and speed controls | game | M0-09 | todo |
| M1-19 | Terrain and water rendering | game | M1-01, M1-17 | todo |
| M1-20 | Buildings, roads and sites (placeholder meshes) | game | M1-19, M1-06 | todo |
| M1-21 | Placement, road and demolish tools | game | M1-20 | todo |
| M1-22 | Carts and walkers | game | M1-20, M1-09 | todo |
| M1-23 | Build menu | game | M1-18, M1-21 | todo |
| M1-24 | Inspector and explain tooltips | game | M1-18, M1-12 | todo |
| M1-25 | Research and civics panels (list view) | game | M1-18, M1-11 | todo |
| M1-26 | Notification feed | game | M1-18, M1-15 | todo |
| M1-27 | Main menu, pause menu and save/load UI | game | M1-14, M1-18 | todo |
| M1-28 | Godot smoke v2 and view boundary check | game | M1-13, M1-20, M1-27 | todo |
| M1-29 | GitHub CI and PR flow | infra | M1-28, M1-16 | todo |
| M1-30 | Vertical-slice playtest and gate review | human | M1-29 | todo |

---

### M1-00 · Cross-system interfaces, commands, events and test fakes

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M0 gate | `src/Evenmere.Sim/Queries/**`, `src/Evenmere.Sim/Services/**`, `src/Evenmere.Sim/Systems/*/Commands/*.cs` (records only), `src/Evenmere.Sim/Systems/*/Events.cs`, `tests/Evenmere.Sim.Tests/Fakes/**`, `tests/Evenmere.Sim.Tests/Scenarios/ScenarioJson.cs` (converters for new command parameter types only) |

**Reads:** architecture.md (§Simulation core, §Gameplay systems); ADR-0002, ADR-0005; progression.md (§How needs work).

**Goal:** freeze the M1 cross-system surface (queries, write services, commands and events), so that ten sim tasks and the game tasks can run in parallel against it.

**Steps:**

1. **Shared value types** in `Queries/Types/`:
   - `TileCoord`, `Rotation` (0–3), `TileInfo` (terrain, elevation, slope class, flags, region, deposit, fertility mask) and `BuildingInfo` (type, origin, rotation, rotated size, `BuildingState` {Site, Active, Paused, Ruin}, stage).
   - `PlacementResult` (errors and warnings as `PlacementIssue` codes plus affected tiles) and `StallReason` (enum per architecture.md).
   - `TransportJobView` (id, storehouse, building, good, amount, direction, phase, phase start tick, phase ticks).
   - `HouseInfo`, `HouseNeedsInfo`, `ProductionInfo`, `ModifierContext`, `ModifierContribution`, `Explanation` (a tree of labelled lines with values and links to subjects), `StatKey` and `AdviceView`.
2. **Query methods** on the empty interfaces from M0-05. Interface names follow the system folders (`IRoadsQueries`, `IAdvisorsQueries`, …). Method names may be refined, but the semantics must hold. The empty `IModifiersQueries`, `IConstructionQueries`, `IExplorationQueries`, `IEconomyQueries` and `IHappeningsQueries` also exist; leave them empty unless a method below needs them.

   | Interface | Methods |
   | --- | --- |
   | `IMapQueries` | `Width`, `Height`, `GetTile(x,y)`, `BuildingAt(x,y)`, `GetBuilding(id)`, `ActiveBuildings` (struct enumerator, ID order), `DrainDirtyChunks(Span<ChunkCoord>)` |
   | `IRoadsQueries` | `RoadAt(x,y)`, `HasRoadAccess(id)`, `IsInStorehouseReach(id)`, `TryNearestStorehouse(id, out storehouse, out cost)`, `IsCovered(house, ServiceKind)`, `TryFindViewPath(from, to, List<TileCoord>)` |
   | `IStorageQueries` | `Stock(GoodId)` (exists) |
   | `IProgressionQueries` | `IsUnlocked(BuildingTypeId / RecipeId / TierId)`, `HasAccess(TerrainAccess)`, `HasFeature(Feature)`, `CurrentEra`, `BonusPercent(Stat, in ModifierContext)`, `ListModifiers(Stat, in ModifierContext, List<ModifierContribution>)`, `Counter(CounterKey)` |
   | `IWorkforceQueries` | `Supply(TierId)`, `Demand(TierId)`, `FillOf(id)` |
   | `IProductionQueries` | `Get(id) → ProductionInfo`, `ProducedYesterday(GoodId)` |
   | `ILogisticsQueries` | `ActiveJobs() → ReadOnlySpan<TransportJobView>`, `FreeCarts(storehouse)` |
   | `INeedsQueries` | `Fulfilment(GoodId)`, `HouseNeeds(house)`, `DemandYesterday(GoodId)` |
   | `IPopulationQueries` | `Residents(TierId)`, `TotalResidents`, `House(id)` |
   | `IResearchQueries` / `ICultureQueries` | `Target`, `Queue`, `Progress(node)`, `Cost(node)`, `IsDone(node)`, `IsAvailable(node)`, `PointsPerDay`, `IsInspired(node)` |
   | `IStatisticsQueries` | `Series(StatKey, Span<long>) → count` (daily samples, newest last) |
   | `IExplainQueries` | `ExplainGood(GoodId)`, `ExplainBuilding(id)`, `ExplainHouse(id)` |
   | `IPlacementQueries` | `Validate(type, x, y, rot)`, `ValidateRoad(ReadOnlySpan<TileCoord>, roadType)` |
   | `IAdvisorsQueries` | `Active() → ReadOnlySpan<AdviceView>` |

3. **Write services** in `Services/`. These are internal to the sim and not exposed to the game:
   - `IBuildingRegistry`: add, remove, set state, change type.
   - `IBuildingLifecycle`: subscribe a listener with `OnActivated`, `OnRemoved` and `OnTypeChanged`. Listeners are called in pipeline order.
   - `IStorageService`: `Add` and `TryTake`, already implemented in M0-05 and exposed here.
   - `IProgressionService.ApplyEffects(source, effects)`.
   - `IPointsLedger`: `Credit(kind, amount)` and `TakeToday(kind)`.
   - `ICounters.Add(CounterKey, long)`.
   - `IProductionBuffers`: `ListRequests(List<TransportRequest>)`, `TryTakeOutput(id, good, amount)` and `AddInput(id, good, amount)`, used by Logistics.
   - `IWorkforceSupply`: Population sets the supply per tier each day.
4. **Command records** (records only; the system tasks add handlers):
   - Construction: `PlaceBuilding(type, x, y, rot)`, `Demolish(id)`, `UpgradeBuilding(id, toType)`.
   - Roads: `PlaceRoad(TileCoord[] path, roadType)`, `RemoveRoad(TileCoord[] tiles)`.
   - Production: `SetBuildingPaused(id, bool)`.
   - Workforce: `SetWorkforcePriority(id, bool high)`.
   - Population: `SetHouseUpgradeLock(id, bool)`.
   - Research: `SetResearchTarget(TechId)`, `SetResearchQueue(TechId[])`.
   - Culture: `SetCivicTarget(CivicId)`, `SetCivicQueue(CivicId[])`.
   - Advisors: `DismissAdvice(int adviceId)`.
5. **Event records:**
   - `BuildingPlaced`, `ConstructionCompleted`, `BuildingDemolished`, `BuildingUpgraded`, `RoadsChanged`
   - `ProductionStalled` / `ProductionResumed`, `TransportJobStarted` / `TransportJobCompleted`
   - `HouseTierAdvanced`, `HouseTierDeclined`, `ResidentsMovedIn`
   - `TechResearched`, `CivicAdopted`, `InspirationTriggered`
   - `AdviceRaised`, `AdviceCleared`
6. **Fakes** in `tests/Evenmere.Sim.Tests/Fakes/`: settable fakes for every query interface and write service, so each system can be unit-tested in isolation.
7. **Scenario converters:** for every command parameter type that isn't a content ID, `Amount` or `EntityId` (for example `TileCoord`, `TileCoord[]`, `Rotation`, `TechId[]`), add a System.Text.Json converter in `tests/Evenmere.Sim.Tests/Scenarios/ScenarioJson.cs`. Without one, scenario args silently read as zero. Add a registry test proving each M1 command deserialises from scenario JSON.
8. **Stub implementations:** each M0-05 system stub implements its interface by throwing `NotImplementedException("M1-NN")`, naming the owning task. This is the only edit this task makes inside system folders, apart from `Commands/` and `Events.cs`.

**Acceptance:**

- The solution builds and every existing test passes.
- Every interface method has XML docs that state its units (ticks, cost units of 1000 per dirt tile, `Amount` milli-units).
- A test enumerates every `Command` subtype and checks it's a sealed record with value semantics.
- The fakes compile and are used by at least one sample test.

**Out of scope:** any behaviour.

---

### M1-01 · Map model, map loading and building registry

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M1-00 | `src/Evenmere.Sim/Systems/Map/**`, `src/Evenmere.Content/Maps/**`, `tests/Evenmere.Sim.Tests/Map/**`, `tests/fixtures/maps/**`, `docs/design/map-format.md` |

**Reads:** architecture.md (§Island and map); progression.md (§Terrain and access ladder); ADR-0003.

**Goal:** the tile grid, the authored map format and loader, and the building registry with an occupancy grid, lifecycle notifications and dirty-chunk tracking.

**Steps:**

1. **`MapState`:**
   - `Width`, `Height`, and flattened per-tile arrays (index = y × width + x): terrain (`TerrainId` as byte), elevation (0–15), flags (river, coast, beach, deep water, reef, marsh), river width class, region (`ushort`), deposit (byte enum), and fertility (`uint` bitmask per crop).
   - Derived on load: the slope class (max elevation delta to the 4-neighbours) and the coastline flag (land tile adjacent to sea).
2. **Authored map format:** write `docs/design/map-format.md` first.
   - A folder `content/maps/<id>/` holds `map.json` with size, legend, `startSite` (Founders' Hall origin and rotation), `startStock` and region names.
   - ASCII layers, one character per tile with rows top to bottom:
     - `terrain.txt`, with legend characters (`.` plains, `^` hills, `T` forest, `~` sea, `=` river, `,` marsh, `M` highlands, `A` peaks, `_` beach, `o` reef)
     - `elevation.txt`, hex digits 0–F
     - `fertility-<crop>.txt`, where `#` marks fertile
     - `deposits.txt` (`s` stone, `c` clay, `i` iron, `k` coal, `u` copper)
     - `regions.txt` (base-36 region IDs)
3. **`MapDef` loader** in `src/Evenmere.Content/Maps/`: parses the format into an immutable `MapDef` with precise diagnostics (file, line and column), and validates layer sizes and legend characters.
4. **Building registry** (`IBuildingRegistry`, `IMapQueries`):
   - `IdTable<BuildingRecord>` plus an occupancy grid of `EntityId` per tile.
   - Add and remove keep both consistent.
   - Rotation swaps width and height.
   - `IBuildingLifecycle` notifies listeners in pipeline order.
5. **`ChunkDirtyTracker`:** 32 × 32 chunks, marked by terrain, road (through a Roads call into Map) and building changes, and drained by the host.
6. **`TerrainAccess` check helper:** `IsUsable(tile, requiredAccess, accessSet)`. The access set itself lives in Progression.
7. **Fixtures:** 16 × 16 and 32 × 32 test maps in `tests/fixtures/maps/`.

**Acceptance:**

- The fixture maps load, and a malformed fixture reports the line and column.
- A property test runs 1,000 random add/remove/rotate operations and keeps the occupancy grid and registry consistent.
- Dirty chunks are marked correctly across chunk boundaries.
- The state hash is stable across save round trips.

**Out of scope:** island generation (M2-01), roads (M1-05).

---

### M1-02 · Vertical-slice content

| Lane | Depends on | Owns |
| --- | --- | --- |
| content | M0-04 | `content/**` except `content/maps/**` and `content/schemas/**` |

**Reads:** progression.md (Era I tables); tech-tree.md (Era I); culture-tree.md (Era I); content/README.md; architecture.md (§Content shapes).

**Goal:** the slice's content as JSON, lint-clean.

**Steps:**

1. **`content/eras/founding.json`:** era order 1, no requirements. This is the only era defined in M1.
2. **`content/terrains/*.json`:** every terrain in the progression terrain ladder, with `buildable`, `blocksRoads` and `access` flags.
3. **`content/rules/core.json`** (game rules):
   - Ticks per tile for carts: 2.
   - Buffer sizes: output 4, cart load 2.
   - Grace: decline 10 days, advance streak 3 days.
   - Inspiration discount: 400 per-mille.
   - Road types are buildings with `roadCost` (and `bridgeSpan` for bridges); starting unlocks are `"start": true` on items, never in rules.
   - Market gating on.
4. **Goods, buildings, recipes, tiers, techs and civics** for the slice list at the top of this file, with values from progression.md and the tree docs.
   - Villagers list only their basic needs; comfort needs are added in M2-13.
   - `hearth_and_shrine` unlocks only `shrine`.
   - `net_weaving` carries the +25% fishing modifier.
5. **Locale file:** `content/locale/en/era1-slice.json`, with a name and a one-line description for every item.

**Acceptance:**

- `evenmere lint` reports 0 diagnostics.
- `evenmere content-hash` is printed in the report.
- Every number matches progression.md, or the deviation is listed in the report.

**Out of scope:** the map (M1-03); the rest of Era I (M2-13).

---

### M1-03 · Starter island map

| Lane | Depends on | Owns |
| --- | --- | --- |
| content | M1-01, M1-02 | `content/maps/starter/**`, `tests/Evenmere.Sim.Tests/StarterMap/**` |

**Reads:** docs/design/map-format.md; architecture.md (§Island and map, start guarantee); progression.md (§Terrain and access ladder).

**Goal:** a hand-authored 128 × 128 starter island that guarantees a comfortable slice and previews later ground.

**Steps:**

1. **Shape:** an island inside open sea, with the Founders' Hall `startSite` near the south-west coast. The **start region** is bounded by a narrow river and a wide river, and contains:
   - a forest belt;
   - at least 3 grain-fertile 5 × 5 placements;
   - at least 4 plains 4 × 4 sheep-farm placements;
   - at least 40 coastline tiles;
   - hills with a stone outcrop;
   - clay riverbanks.
2. **Beyond the rivers:** a marsh, hills with an iron deposit, a highland interior with coal and copper, a reef offshore, and 8–12 regions in all.
3. **Start stock:** 40 timber, 20 fish and 10 wood.
4. **Starter map tests:** load the map with the real loader, and assert the start-region guarantees by counting tiles and placements within 24 tiles of the hall.
5. **Preview:** add an ASCII preview to `content/maps/starter/README.md`.

**Acceptance:**

- The starter map tests pass.
- `evenmere run --map starter --ticks 1000` runs (after M1-13 lands this becomes a scenario; until then, test only the load).

**Out of scope:** procedural generation.

---

### M1-04 · Progression core: unlocks, effects, modifiers, ledger, counters

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M1-00 | `src/Evenmere.Sim/Systems/Progression/**`, `src/Evenmere.Sim/Systems/Modifiers/**`, `tests/Evenmere.Sim.Tests/Progression/**`, `tests/Evenmere.Sim.Tests/Modifiers/**` |

**Reads:** architecture.md (§Data-driven content: the effects and conditions vocabularies); ADR-0004, ADR-0010; tech-tree.md (§How research works).

**Goal:** one place that applies effects, answers "is X unlocked?", sums modifiers, and holds the points ledger and the milestone counters that conditions read.

**Steps:**

1. **`ProgressionState`:**
   - Bitsets of unlocked buildings, recipes, tiers, traditions and festivals; terrain access flags; features; the current era.
   - The start unlocks (items marked `"start": true`) are applied when a new game is created.
2. **`EffectApplier`:** implements every effect kind in the vocabulary.
   - Effects outside M1's scope (`traditionSlot`, `unlockFestival`) are recorded in state for later systems, without behaviour.
   - Every effect application is idempotent per source.
3. **`ModifierIndex`:**
   - Active modifiers record their source, stat, target selector, percent and optional condition.
   - `BonusPercent(stat, ctx)` sums the matching percents, evaluating season conditions from the clock.
   - It's cached per (stat, building type, tier, good) and invalidated when modifiers change or the season changes.
   - `ListModifiers` returns the contributions for the explain queries.
4. **`PointsLedger`:** today's credited knowledge, culture and creativity per type, plus coin, which stays at zero until M3. Research and Culture take from it daily.
5. **`Counters` and `ConditionEvaluator`:**
   - Monotonic `long` counters keyed by `CounterKey`, such as `(Produced, GoodId)`, `(Built, BuildingTypeId)` and `(RoadTilesLaid)`.
   - Population snapshot values come from `IPopulationQueries` at evaluation time.
   - M1 implements `count`, `produced`, `population`, `researched`, `adopted`, `roadTiles`, `all`, `any` and `not`. The other condition kinds return false with a `TODO(M2-09)`.

**Acceptance:**

- Applying a tech's effects unlocks its buildings.
- Two +20% productivity modifiers give `BonusPercent` 40, applied once.
- An autumn-only modifier is active only in autumn.
- The condition evaluator passes truth-table tests for each M1 kind.
- Unlock sets are included in the state hash.

**Out of scope:** research and civic point accrual (M1-11); era transitions (M2-11).

---

### M1-05 · Roads, connectivity, reach and coverage fields

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M1-00 | `src/Evenmere.Sim/Systems/Roads/**`, `tests/Evenmere.Sim.Tests/Roads/**` |

**Reads:** ADR-0005; architecture.md (§Logistics, §Construction).

**Goal:** roads, road-access rules, and the integer distance fields that give storehouse reach and service coverage, rebuilt only where roads change.

**Steps:**

1. **Road layer:** a road type per tile. `PlaceRoad` validates each tile (in bounds, buildable terrain, not occupied by a building, not a river unless it is a bridge, which comes in M2-04) and places only the valid prefix. A road over an existing road of a different type upgrades it. `RemoveRoad` clears the given tiles.
2. **Access tiles:** a building's access tiles are the road tiles orthogonally adjacent to its footprint, sorted in a deterministic order.
3. **Distance fields:**
   - For each active building with a `storage` or `service` spec, run a multi-source Dijkstra from its access tiles over road tiles, using integer per-tile cost (the road type's `cost`; dirt is 1000) and bounded by `reach × 1000` or `coverage × 1000`.
   - Store the fields compactly: a sparse map from tile index to cost, using sorted arrays, never a `Dictionary`.
4. **Invalidation:** when roads change, rebuild only the fields whose bounding box intersects the changed tiles. Also rebuild when a source building is added or removed (lifecycle listener).
5. **Derived lookups:**
   - For each building: in storehouse reach, the nearest storehouse and its cost.
   - For each house: the covered service kinds.
   - These are recomputed for the affected buildings after field rebuilds.
6. **View paths:** `TryFindViewPath` runs A* over road tiles. It is for presentation only and is never called by systems.
7. **Command handlers** for `PlaceRoad` and `RemoveRoad`. Emit `RoadsChanged` with the bounding box, and mark dirty chunks.

**Acceptance:**

- A property test checks fields against brute-force BFS on random 32 × 32 road layouts.
- An instrumented test shows that a road change far from a field doesn't rebuild that field.
- Coverage is correct when two markets overlap.
- Determinism hash test.

**Out of scope:** bridges (M2-04); rail (M5-05).

---

### M1-06 · Construction and placement validation

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M1-00 | `src/Evenmere.Sim/Systems/Construction/**`, `tests/Evenmere.Sim.Tests/Construction/**` |

**Reads:** architecture.md (§Construction); progression.md (§Buildings, placement column).

**Goal:** placing, building, upgrading and demolishing buildings, with one validation path shared by the command and the ghost preview.

**Steps:**

1. **`IPlacementQueries.Validate`** returns errors that block placement:
   - `OutOfBounds`, `Occupied`, `TerrainNotAllowed`, `TerrainAccessLocked`, `SlopeTooSteep`, `NotUnlocked`, `UniqueAlreadyBuilt`, `NeedsCoastline`, `NeedsDeposit`, `FertilityTooLow`
   - It also returns warnings that allow placement: `NoRoadAccess`, `OutsideStorehouseReach`, `LowYield`.
   - The rules come from the building's `placement` data, not from code.
2. **`PlaceBuilding` handler:** creates a building record in the `Site` state, registers it and emits `BuildingPlaced`. Roads are free, and never become sites.
3. **Site progress** (every tick):
   - A site advances only while it's in storehouse reach.
   - It draws its materials in 10 equal steps from the storage pool. If a step's materials are missing, it waits with `MissingMaterial`.
   - Total time is `buildDays × 120` ticks, scaled by the `constructionSpeed` modifier.
   - On completion, it sets `Active`, notifies lifecycle listeners and emits `ConstructionCompleted`.
4. **`Demolish`:** refund 100% of the materials a site consumed, or 50% of an active building's cost, rounded down. Then remove the building, notify lifecycle listeners and emit `BuildingDemolished`. The Founders' Hall can't be demolished.
5. **`UpgradeBuilding`:** for definitions with `upgradeOf`, start an in-place upgrade site that keeps the old building running until completion, then changes its type through `OnTypeChanged`. House tier changes are Population's job, not this command's.
6. **Pre-placement:** `PlacePreBuilt(type, x, y, rot)` is an internal service used by new-game setup to create an active Founders' Hall.

**Acceptance:**

- One test per error and warning code.
- Partial materials stall a site, and it resumes when they arrive.
- Demolish refunds exactly as specified.
- An upgrade keeps the building operating until it completes.
- Command validation and the ghost query return identical results (a shared-code test).

**Out of scope:** monuments with stages (M2-10); bridges (M2-04).

---

### M1-07 · Workforce

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M1-00 | `src/Evenmere.Sim/Systems/Workforce/**`, `tests/Evenmere.Sim.Tests/Workforce/**` |

**Reads:** architecture.md (§Workforce); progression.md (§Population tiers).

**Goal:** a daily allocation of each tier's workers to the buildings that need them.

**Steps:**

1. **Supply:** each tier's supply is set daily by Population through `IWorkforceSupply`. The `workforce` modifier scales it per tier.
2. **Demand:** the sum of `workforce.count` over active, unpaused buildings of each tier.
3. **Allocation** (at day start):
   - High-priority buildings fill first, in ID order.
   - The rest share the remainder at an equal fill ratio, computed in per-mille.
   - Leftover individual workers go one each to the lowest IDs, so allocation is deterministic.
4. **`FillOf(id)`** returns a `Ratio`, used by Production.
5. **`SetWorkforcePriority`** handler.

**Acceptance:**

- Unit tests with fakes cover:
  - Supply 10 against demand 15 across three buildings gives fills summing to 10 workers.
  - A priority building is filled first.
  - Zero supply gives zero fill.
- Determinism.

**Out of scope:** commuting and distance (not modelled).

---

### M1-08 · Production

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M1-00 | `src/Evenmere.Sim/Systems/Production/**`, `tests/Evenmere.Sim.Tests/Production/**` |

**Reads:** architecture.md (§Production chains); ADR-0005; progression.md (§Buildings).

**Goal:** running recipes with explicit productivity and stall reasons, filling the buffers that Logistics serves.

**Steps:**

1. **Production state:** on `OnActivated` for a building with a recipe, create its state: cycle progress, input and output buffers, last stall reason and an area-yield cache.
2. **Area yield**, computed on activation and when a nearby map, road or building change is notified:
   - Woodcutter: forest tiles within radius 4, divided by 24.
   - Fishing hut: water tiles within radius 5 not claimed by a lower-ID hut, divided by 30.
   - Farms: the share of footprint tiles fertile for the crop.
   - Deposit gatherers: the share of footprint tiles over the deposit.
   - All are capped at 1000 per-mille.
3. **Each tick:**
   - productivity = workforce fill × (inputs present for the next cycle ? 1 : 0) × area yield × (1000 + bonus) / 1000
   - The `productivity` bonus comes from `IProgressionQueries.BonusPercent` with the building's context. The season factor is 1000 until M2-05.
   - `progress += productivity`. When it reaches `cycleSeconds × 4 × 1000`, consume the inputs, add the outputs to the output buffer, `ICounters.Add(Produced, good)`, and carry the remainder over.
4. **Stall reasons**, set in priority order: `Paused`, `NoStorehouseReach`, `NoWorkforce`, `MissingInput`, `OutputFull`. Emit `ProductionStalled` or `ProductionResumed` only when the reason changes.
5. **`IProductionBuffers`:**
   - A pickup request when the output buffer holds at least one cart load.
   - A delivery request when an input buffer is below one cart load and no delivery is pending.
   - Requests are listed in (request tick, building ID) order.
6. **`SetBuildingPaused`** handler, and `ProducedYesterday` bookkeeping per good.

**Acceptance:**

- Cycle timing is exact: a 30 s recipe at full productivity yields 1 unit per 120 ticks.
- Half workforce gives half the rate.
- Missing input stalls with the correct reason.
- Fishing-hut claims are deterministic, with overlapping huts sharing water correctly.
- The +25% Net-weaving modifier is reflected in the rate.

**Out of scope:** seasons (M2-05); fuel and steam (M5-04).

---

### M1-09 · Logistics: carts and transport jobs

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M1-00 | `src/Evenmere.Sim/Systems/Logistics/**`, `tests/Evenmere.Sim.Tests/Logistics/**` |

**Reads:** ADR-0005 (in full); architecture.md (§Logistics).

**Goal:** the job records that move goods between producers and the island pool. Their cost scales with jobs, not agents.

**Steps:**

1. **Carts:** each storehouse (any building with a `storage` spec, including the Founders' Hall) owns N carts from its spec.
2. **Assignment** (each tick): read the requests from `IProductionBuffers`. Serve each in order with the nearest storehouse that has the building in reach and a free cart. Ties go to the lower storehouse ID.
3. **Pickup job:**
   - Travel out (cost / 1000 × ticks per tile × (1000 − cartSpeed bonus) / 1000), then load (`TryTakeOutput`), then travel back, then unload into the pool (`IStorageService.Add`).
4. **Delivery job:**
   - At assignment, reserve the goods with `TryTake` from the pool. If they're unavailable, skip the request until the next day.
   - Travel, `AddInput`, then return.
5. **Interruptions:** if a building or storehouse is removed mid-job, the job completes its current phase and returns. Any goods it carries go to the pool.
6. **Views:** `ILogisticsQueries.ActiveJobs` exposes `TransportJobView` structs for the view, and the system emits `TransportJobStarted` and `TransportJobCompleted`.

**Acceptance:**

- One cart serving a producer 10 tiles away completes a round trip in exactly 2 × 10 × 2 ticks plus load and unload ticks (as configured).
- Two storehouses split requests by distance.
- Delivery reservations never drive the pool negative (property test).
- Removing a storehouse mid-job loses no goods.

**Out of scope:** rail (M5-05); harbour shipping (M5-02).

---

### M1-10 · Needs and population

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M1-00 | `src/Evenmere.Sim/Systems/Needs/**`, `src/Evenmere.Sim/Systems/Population/**`, `tests/Evenmere.Sim.Tests/Needs/**`, `tests/Evenmere.Sim.Tests/Population/**` |

**Reads:** progression.md (§Population tiers, §Needs, §How needs work) in full; ADR-0010; architecture.md (§Population and needs).

**Goal:** houses that consume, grow, advance and decline exactly as specified, and supply workforce and knowledge.

**Steps:**

1. **House state:**
   - Created on `OnActivated` for residence buildings, at the start tier with 1 resident.
   - Fields: tier, residents, a needs-met bitmask, an ever-met-at-this-tier bitmask, the advance streak, the decline timer and the upgrade lock.
2. **Fair shares** (once at each day start, in Needs):
   - demand(good) = Σ over houses of residents × rate.
   - fulfilment(good) = min(1, (stock + producedYesterday) / demand).
3. **Per house, on its bucket tick:**
   - The house needs Market coverage. Without it, every goods need is unmet.
   - Each goods need: consume residents × rate × fulfilment × (1000 + consumption bonus) / 1000 from the pool with `TryTake`. The need is met when fulfilment ≥ 50%.
   - Service needs are met by coverage.
   - Update the met and ever-met masks, and add to the consumption counters.
4. **Occupancy:**
   - target = max(1, capacity × met ÷ total), with capacity scaled by the `capacity` modifier.
   - Residents move one step per day toward the target.
   - Emit `ResidentsMovedIn` in daily batches.
5. **Advance:** all basic needs met for 3 days, plus the next tier unlocked, plus not locked, plus `TryTake(upgradeCost)` succeeds → change the tier, reset the masks and timers, notify `OnTypeChanged` if the content uses distinct house types per tier (otherwise update the tier field), and emit `HouseTierAdvanced`.
6. **Decline:**
   - The timer counts days while any ever-met basic need is below 50%. It's scaled by `declineGrace`, and paused while a festival runs (M2-08 sets that flag; here it's a state flag that defaults to false).
   - At 10 days, the house steps down one tier, keeps no more than the lower tier's capacity, and emits `HouseTierDeclined`.
   - Never below Settlers, never fewer than 1 resident, and a fresh full timer is needed before the next decline.
7. **Outputs and supply** (daily):
   - Credit the ledger with each tier's outputs per 1,000 residents, × (1 + 0.25 × comfort needs met).
   - Set the workforce supply per tier through `IWorkforceSupply`.
8. **`SetHouseUpgradeLock`** handler.

**Acceptance:**

- A Settlers house with fish, wood and a market grows to 6 residents and advances once Villagers' needs exist.
- A newly advanced house never declines for needs it has never had.
- Losing bread after it was met declines the house exactly 10 days later, one tier only.
- Fair shares split a short good equally between houses regardless of ID (test with two houses, half supply).
- Ledger credit matches the per-1,000 rates.

**Out of scope:** visitors (M3-02); coin taxes (M3-01).

---

### M1-11 · Research and civics

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M1-00 | `src/Evenmere.Sim/Systems/Research/**`, `src/Evenmere.Sim/Systems/Culture/**` (civic progression only), `tests/Evenmere.Sim.Tests/Research/**`, `tests/Evenmere.Sim.Tests/Culture/**` |

**Reads:** tech-tree.md (§How research works); culture-tree.md (§How culture works); ADR-0010.

**Goal:** knowledge and culture turned into techs and civics, with targets, queues and inspirations.

**Steps:**

1. **Daily accrual:** `TakeToday(Knowledge)` adds to the target's progress, with any overflow going to the next queued node. Culture works the same way for civics.
2. **Availability:** all prerequisites are done, the node's era has begun (`IProgressionQueries.CurrentEra` ≥ the node's era), and the node isn't done. Repeatable nodes come in M5.
3. **Completion:** apply the effects through `IProgressionService`, emit `TechResearched` or `CivicAdopted`, and pop the queue.
4. **Inspirations** (evaluated daily): when a node's condition first becomes true, mark it inspired, which subtracts 40% of the full cost (from `rules.core`) from the remaining cost, floored at the progress already made, and emit `InspirationTriggered`.
5. **Commands:** handlers for `SetResearchTarget`, `SetResearchQueue`, `SetCivicTarget` and `SetCivicQueue`. Validation rejects unavailable nodes with `NodeNotAvailable`.

**Acceptance:**

- A 60-knowledge tech at 6 knowledge per day completes on day 10.
- An inspiration on day 3 completes it on day 6 (60 × 0.6 = 36 needed).
- Queue overflow carries over.
- Targeting a locked node is rejected.

**Out of scope:** traditions and slots (M2-07); era advance (M2-11).

---

### M1-12 · Statistics and explain queries

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M1-00 | `src/Evenmere.Sim/Systems/Statistics/**`, `src/Evenmere.Sim/Explain/**`, `tests/Evenmere.Sim.Tests/Statistics/**`, `tests/Evenmere.Sim.Tests/Explain/**` |

**Reads:** architecture.md (pillar 4 "Every number explains itself", §UI); ADR-0005.

**Goal:** daily history for graphs, and structured explanations that trace any number to its causes.

**Steps:**

1. **Statistics:** sample daily the stock, produced, consumed and demand per good; residents per tier; knowledge and culture per day; and active jobs. Keep 40-day ring buffers in state (fixed-size arrays), plus seasonal rollups for 2 years.
2. **`ExplainGood`:** the supply/demand line ("Bread: +8/day produced, −12/day demanded, stock 30") with fulfilment, then each producer type with its count, productivity distribution and dominant stall reasons, then chained causes (for example bakeries `MissingInput` → flour shortage → windmills `NoWorkforce` → Settlers supply 40 vs demand 55).
3. **`ExplainBuilding`:** a productivity breakdown (workforce fill, input availability, area yield, each modifier contribution with its source name key, season), the current stall reason with its fix hint key, buffers, and storehouse reach and cost.
4. **`ExplainHouse`:** each need with met or unmet status and its reason (no market coverage, fulfilment %, the service not in range), occupancy target maths, advance progress, the decline timer and what stops it.
5. **Text keys:** explanations carry locale *keys* and numbers, never English text. Add the keys to `content/locale/en/explain.json`; this task may create that one file.

**Acceptance:**

- Golden-structure tests for each explanation type on fixture worlds.
- A three-step chained shortage (bread ← flour ← workforce) is explained to the root cause.
- Ring buffers wrap correctly.

**Out of scope:** UI rendering of explanations (M1-24).

---

### M1-13 · Integration: new game and end-to-end scenarios

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M1-01, M1-03 to M1-12 | `src/Evenmere.Sim/Simulation/**`, `src/Evenmere.Sim/NewGame/**`, `tests/fixtures/scenarios/slice_*.json`, `tests/Evenmere.Sim.Tests/Integration/**`; plus **minimal fixes** in any `src/Evenmere.Sim/Systems/*` folder, each listed in the report with its reason |

**Reads:** every M1 sim task report; architecture.md (§System order).

**Goal:** a real new game on the starter island running every system together, proven by scenarios with golden hashes.

**Steps:**

1. **New game:** `NewGameOptions { Seed, MapId }` does the following:
   - Loads the `MapDef` into `MapState` and applies the start unlocks.
   - Pre-places the Founders' Hall at `startSite` and adds `startStock` to the pool.
   - Sets the research and civic targets to none.
2. **Wiring:** replace every `NotImplementedException` stub with the real implementations in `ISimQueries`, and register the lifecycle listeners in pipeline order.
3. **Scenarios:**
   - `slice_first_houses`: roads, 8 houses, a market and a fishing hut. Settlers grow.
   - `slice_bread_chain`: the full bread chain with storehouse logistics. Bread stock rises.
   - `slice_villagers`: a scripted build order through Shrine, homespun and bread. At least 100 Villagers by tick 10,800 (45 min at 4 ticks per second).
   - `slice_decline`: break the bread chain after Villagers exist. Exactly one decline step occurs after 10 days.
4. **Bug fixing:** fix integration bugs at their root, in the owning folder, with a regression test.

**Acceptance:**

- All four scenarios pass with committed golden hashes.
- `evenmere run --map starter --ticks 20000 --seed 1` runs.
- The per-system timings are printed and the total average stays under 0.5 ms per tick on the slice.

**Out of scope:** new features; any design change goes back as a proposal.

---

### M1-14 · Save/load v1 and command log

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M1-13 | `src/Evenmere.Sim/Persistence/**`, `tests/Evenmere.Sim.Tests/Persistence/**`, `tests/fixtures/saves/**`, `src/Evenmere.Tools/Commands/HashCommand.cs` |

**Reads:** ADR-0007 (in full); ADR-0003.

**Goal:** save files that survive content changes, plus a command log that makes any session replayable.

**Steps:**

1. **`SaveGame.Write(Stream, ISimulation, SaveMeta)`** writes the magic `EVMR`, the format version 1, a MessagePack header (with string-ID tables for every content type) and an LZ4 body. `SaveGame.WriteJson` is the debug variant.
2. **`SaveGame.Read(Stream, ContentDb) → (ISimulation, SaveReport)`**:
   - Runs migrations (none yet, but the framework is in place), remaps integer IDs from the string tables, and turns unknown building types into `Ruin`s with a full refund.
   - The `SaveReport` lists anything remapped or missing.
3. **Command log:**
   - `CommandLogWriter` and `CommandLogReader` (MessagePack, (tick, command) pairs).
   - `Replay.Run(save, log, untilTick) → hash`.
4. **Tools:** `evenmere hash --save <file>` prints the save's state hash.
5. **Fixture:** commit `tests/fixtures/saves/v1-slice.evsave`, generated from `slice_villagers` at tick 20,000.
6. **Key baseline:** ADR-0007 says MessagePack keys are never removed or reused. Commit `tests/fixtures/saves/state-keys.json`, listing every `[MessagePackObject]` type reachable from `WorldState` (via M0-06's `StateTypeWalker`) with its key → member name and type. Add a test that fails if a key disappears, changes member type, or is reused. Adding keys is allowed, and the test prints how to refresh the baseline.

**Acceptance:**

- Saving at tick 20,000, loading and continuing to tick 40,000 gives the same hash as an uninterrupted run.
- Loading against content with an extra good succeeds, and the IDs are remapped (tested).
- Loading against content missing `windmill` produces ruins and a report.
- Replay reproduces the hash.

**Out of scope:** autosave rotation and migrations beyond the framework (M2-15).

---

### M1-15 · Advisors v1

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M1-12 | `src/Evenmere.Sim/Systems/Advisors/**`, `tests/Evenmere.Sim.Tests/Advisors/**`, `content/locale/en/advisors.json` |

**Reads:** architecture.md (§Happenings and advisors, pillar 4).

**Goal:** gentle, explained notes about problems, each linked to its subject and cause.

**Steps:**

1. **Daily rules:**
   - `NoRoadAccess` (an active or site building without road access)
   - `OutsideStorehouseReach`
   - `GoodShortage` (fulfilment below 80% for 2 days)
   - `HouseCannotAdvance` (all needs met but materials missing, or the tier is locked by research)
   - `NoWorkforce` (demand exceeds supply for a tier for 2 days)
   - `ResearchIdle` and `CivicIdle` (no target while points accrue)
2. **Advice records:**
   - Each record has a stable ID (a deterministic counter), its kind, its subject (building, good or tier), first-seen and last-seen ticks, and an explanation reference into `IExplainQueries`.
   - Advice is cleared when its condition stops, which emits `AdviceRaised` or `AdviceCleared`.
3. **`DismissAdvice`:** suppresses that kind and subject for one season.
4. **Calm by design:** at most 5 active advice items, chosen by priority and then age; the rest are queued.

**Acceptance:**

- One test per rule raising and clearing.
- Dismissal suppresses for exactly one season.
- The cap holds.

**Out of scope:** happenings (M2-12); the tutorial (M4-04).

---

### M1-16 · Balance bot v1

| Lane | Depends on | Owns |
| --- | --- | --- |
| tools | M1-13 | `src/Evenmere.Tools/Balance/**`, `src/Evenmere.Tools/Commands/BalanceCommand.cs`, `tools/bots/**` |

**Reads:** architecture.md (§Testing: balance bot); progression.md (§Pacing targets).

**Goal:** an auto-player that follows a build-order script using only commands and queries, and reports pacing and stalls.

**Steps:**

1. **Bot script format** (`tools/bots/<name>.json`): ordered goals such as `{ "build": "fishing_hut", "count": 2, "near": "founders_hall" }`, `{ "research": "cultivation" }`, `{ "ensureSupply": "bread", "targetFulfilment": 1.0 }` and `{ "housesUntil": { "tier": "villagers", "residents": 100 } }`.
2. **Placement search:**
   - Spiral outward from an anchor and validate through `IPlacementQueries`.
   - Prefer positions with no errors and no warnings.
   - Connect each building to the network with a BFS road planner over free tiles.
3. **Reactive rules:** if a good's fulfilment drops below 90% for 2 days, add the producer whose explanation shows it as the bottleneck.
4. **Report** (JSON plus text): the time to first Villager, to 50 Villagers and to 100 Villagers; stalls (no resident growth for a full season, with the top advice at the time); peak and average tick time; the final hash.
5. **Command:** `evenmere balance --map starter --bot tools/bots/slice.json --seed 1 --max-minutes 90`.

**Acceptance:**

- The `slice.json` bot reaches 100 Villagers within 45 game minutes on seed 1, with no stall. Paste the report.
- The bot is deterministic: same seed, same report.

**Out of scope:** generated islands and multiple seeds (M2-17).

---

### M1-17 · Camera rig and input actions

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M0-09 | `game/rendering/camera/**`, `game/input/**`, `game/project.godot` (the `[input]` section only) |

**Reads:** architecture.md (§Rendering: camera, §UI: input); ADR-0008.

**Goal:** a calm, responsive camera driven equally well by mouse and keyboard and by gamepad.

**Steps:**

1. **Input actions** in `project.godot`:
   - `camera_pan_*`, `camera_zoom_in` / `camera_zoom_out`, `camera_rotate_left` / `camera_rotate_right`
   - `ui_build_menu`, `tool_rotate`, `tool_cancel`, `speed_pause`, `speed_1` / `speed_2` / `speed_3`
   - `cursor_*` (gamepad virtual cursor)
   - Bind each to keys, mouse and gamepad.
2. **`CameraRig`:**
   - A tilted perspective (pitch interpolated from 55° at street level to 70° at island view), with smooth exponential zoom between 8 and 220 world units.
   - Pans with WASD, the arrows, the screen edge (toggleable), middle-drag and the left stick.
   - Rotates in 90° steps with easing, and clamps to the map bounds.
3. **Virtual cursor:** in gamepad mode, the right stick moves a tile cursor that snaps to tiles, and the mouse and gamepad modes switch automatically on input.
4. **Picking helper:** a ray from the screen point (or the virtual cursor) to a tile coordinate on the terrain height. It's used by the tools; the exposed API is `TryPickTile(out TileCoord)`.

**Acceptance:**

- A manual check on the development Mac (described in the report) covers pan, zoom and rotate with the keyboard and mouse and with a gamepad.
- Picking returns the correct tile on a flat test scene (a headless unit check of the maths).

**Out of scope:** UI panels.

---

### M1-18 · UI theme, HUD and speed controls

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M0-09 | `game/ui/theme/**`, `game/ui/hud/**`, `game/ui/common/**`, `game/assets/fonts/**` |

**Reads:** architecture.md (§UI); ADR-0008; culture-tree.md and progression.md (for the names shown).

**Goal:** the shared theme, the view-model base and a calm HUD with date, season, speed and key numbers.

**Steps:**

1. **Theme:** `game/ui/theme/evenmere_theme.tres` with a calm palette, rounded panels and focus styles that are visible for gamepad navigation. Use an OFL-licensed font, such as Atkinson Hyperlegible; add its licence file next to it.
2. **View-model base:** `ViewModel` (refreshes on `SimHost` tick batches at most 4 times a second) and `UiStrings`, which reads `LocaleTable` keys. Never hard-code display text.
3. **HUD top bar:**
   - Date (day, season, year), speed buttons (pause, 1×, 2×, 4×) bound to the `speed_*` actions.
   - Residents by tier, knowledge and culture per day.
   - A configurable strip of up to 8 goods, showing stock and trend arrows.
4. **UI scale:** a setting (0.75–1.5) applied through `content_scale_factor`. Check that the layout holds at 1280 × 800 and 2560 × 1600.

**Acceptance:**

- Screenshots at both resolutions in the report.
- The HUD updates while the sim runs.
- Every control is reachable by gamepad focus.

**Out of scope:** panels beyond the HUD.

---

### M1-19 · Terrain and water rendering

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M1-01, M1-17 | `game/rendering/terrain/**`, `game/rendering/water/**` |

**Reads:** architecture.md (§Rendering); docs/design/map-format.md.

**Goal:** the island drawn from `IMapQueries`, in 32 × 32 chunks, rebuilt only when dirty.

**Steps:**

1. **Chunk meshes:** generate one `ArrayMesh` per chunk from tile elevations (0.5 units per step), coloured by terrain from a palette resource, with flat-shaded low-poly facets.
2. **Water:** one animated water plane (a shader with gentle waves and shoreline foam from a distance-to-land texture baked at load), flat river strips, and reef and deep-water tints.
3. **Dirty rebuilds:** rebuild at most 4 chunks per frame from `DrainDirtyChunks`.
4. **Grid overlay:** a toggleable shader overlay showing the tile grid.
5. **Picking support:** expose terrain height lookup for the camera picking.

**Acceptance:**

- The starter island renders at ≥ 60 fps on the development Mac, and the frame time is reported.
- A dirty chunk rebuild after a test terrain change is visible.
- Headless smoke still passes (no GPU-only calls on load paths).

**Out of scope:** seasons (M2-24); fog (M2-23).

---

### M1-20 · Buildings, roads and sites (placeholder meshes)

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M1-19, M1-06 | `game/rendering/buildings/**`, `game/rendering/roads/**`, `game/data/visuals/**` |

**Reads:** architecture.md (§Rendering: buildings, placeholder art); AGENTS.md (§Godot conventions).

**Goal:** every slice building and road visible as palette-tinted placeholder meshes, instanced per type per chunk.

**Steps:**

1. **Visual mapping:** `game/data/visuals/era1-slice.json` maps each building ID to a palette colour, a placeholder shape (`hut`, `house`, `hall`, `farm`, `mill`, `market`, `shrine`), an optional `scene` path and an icon.
2. **`PlaceholderMeshFactory`:** builds a body, a roof and an optional chimney or sails from the footprint and shape. Farms get field strips across their footprint.
3. **Instancing:** one `MultiMeshInstance3D` per building type per chunk, rebuilt on dirty chunks and lifecycle events.
4. **States:** sites show scaffolding scaled by build progress, paused buildings are desaturated, and ruins are grey.
5. **Roads:** flat strips with auto-connection (straight, corner, T, cross) per road type.

**Acceptance:**

- A scripted test scene places one of each slice building and screenshots it for the report.
- Placing or demolishing updates only the affected chunk (debug counter).

**Out of scope:** real art; style variants (M3-13).

---

### M1-21 · Placement, road and demolish tools

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M1-20 | `game/ui/tools/**` |

**Reads:** architecture.md (§Construction, §UI); M1-06 (placement issues).

**Goal:** a fluent, explained way to place buildings and roads and to demolish, by mouse and by gamepad.

**Steps:**

1. **Tool states:** `Idle`, `Build(type)`, `Road(type)` and `Demolish`, switched by the build menu and hotkeys and cancelled with `tool_cancel`.
2. **Build ghost:**
   - A translucent placeholder that follows the picked tile and rotates with `tool_rotate`.
   - It calls `IPlacementQueries.Validate` only when the tile or rotation changes, and tints green or red.
   - Warnings appear as icons with tooltips from locale keys.
   - Clicking enqueues `PlaceBuilding`. Shift keeps the tool active.
3. **Road tool:** drag shows an L-shaped preview validated with `ValidateRoad`. Releasing enqueues `PlaceRoad`.
4. **Demolish:** hovering highlights the target; clicking demolishes. A drag rectangle selects several, with a confirmation above 5.
5. **Feedback:** a `CommandRejected` event for the player's own command shows a calm toast with the reason.

**Acceptance:**

- A recorded walkthrough (screenshots or GIF frames) shows a valid placement, an invalid placement with its reason, and road dragging.
- The tools work with a gamepad virtual cursor.

**Out of scope:** the build menu UI (M1-23).

---

### M1-22 · Carts and walkers

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M1-20, M1-09 | `game/rendering/life/**` |

**Reads:** ADR-0005 (§Visual replay); architecture.md (§Rendering: life, level of detail).

**Goal:** a living town. Carts replay transport jobs exactly; walkers are pure ambience.

**Steps:**

1. **Carts:**
   - For each `TransportJobView`, cache a view path from `IRoadsQueries.TryFindViewPath` when the job starts.
   - Position the cart along it by phase progress = (now − phaseStart + interpolation alpha) / phaseTicks.
   - Render with a MultiMesh, with a simple bob animation and the good's colour on the load.
2. **Walkers:**
   - Deterministic per house (seeded from the entity ID): residents ÷ 4 walkers wander between the house, the market and their workplace along roads.
   - Purely visual, capped at 1,500 visible, and thinned with zoom.
3. **LOD:** beyond zoom distance 120, hide walkers and show carts as dots.

**Acceptance:**

- A cart's arrival matches the `TransportJobCompleted` tick (debug overlay check in the report).
- 500 walkers hold ≥ 60 fps on the development Mac.

**Out of scope:** ships (M5-02); smoke particles (M2-24).

---

### M1-23 · Build menu

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M1-18, M1-21 | `game/ui/build_menu/**` |

**Reads:** architecture.md (§UI); progression.md (§Buildings).

**Goal:** a categorised build menu showing what each building costs, needs and does, including what is still locked and why.

**Steps:**

1. **Categories:** Housing, Production, Storage & Roads, Services, Culture & Knowledge. A building's category comes from its content `category` and `tags`.
2. **Entries:** icon, name, cost, workforce and recipe summary. A locked entry is dimmed and shows "Unlocked by <tech/civic>", resolved through `ContentDb` effect sources.
3. **Interaction:** selecting an entry activates the Build tool. Hotkeys (B, then a number) and full gamepad navigation.

**Acceptance:** screenshots of the menu with locked and unlocked items, and gamepad navigation demonstrated.

**Out of scope:** search and favourites (M4-15).

---

### M1-24 · Inspector and explain tooltips

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M1-18, M1-12 | `game/ui/inspector/**`, `game/ui/explain/**` |

**Reads:** architecture.md (pillar 4, §UI); M1-12 explanation model.

**Goal:** click anything and see why it is the way it is.

**Steps:**

1. **`ExplanationView`:** a reusable control that renders an `Explanation` tree with locale keys, numbers formatted by unit, and clickable subject links that focus the camera on, or open, the subject.
2. **Building inspector:**
   - Name, state and productivity with its breakdown, buffers, workforce fill, and storehouse reach.
   - Pause and priority toggles (commands), plus demolish and upgrade buttons.
3. **House inspector:** tier, residents and target, a needs list with status icons and reasons, the advance streak, the decline timer, and the upgrade lock toggle.
4. **HUD goods tooltip:** shows `ExplainGood`.

**Acceptance:** screenshots of a stalled bakery explained to its root cause, and of a house blocked from advancing with its reason.

**Out of scope:** the production-chain viewer (M2-21).

---

### M1-25 · Research and civics panels (list view)

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M1-18, M1-11 | `game/ui/research/**`, `game/ui/culture/**` |

**Reads:** tech-tree.md and culture-tree.md (§How research works, §How culture works).

**Goal:** choose research and civic targets with clear progress, costs and inspirations. The graph views come in M2.

**Steps:**

1. **Research panel:** techs grouped by state (available, in queue, researched, locked with missing prerequisites), with cost, progress, knowledge per day, ETA in days, and the inspiration condition text with its met status.
2. **Interaction:** click sets the target; shift-click appends to the queue; drag reorders the queue.
3. **Civics panel:** the same for civics.

**Acceptance:** setting a target from the panel shows progress the next day, and an inspiration toast appears when its condition is met.

**Out of scope:** the tree graph (M2-18); traditions (M2-19).

---

### M1-26 · Notification feed

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M1-18, M1-15 | `game/ui/notifications/**` |

**Reads:** architecture.md (pillar 4: "gentle advisor notes, not alarms").

**Goal:** a calm feed of what happened and what needs attention.

**Steps:**

1. **Mapping:** `ConstructionCompleted` (batched per type per day), `HouseTierAdvanced` and `HouseTierDeclined`, `TechResearched` and `CivicAdopted`, `InspirationTriggered`, and `AdviceRaised` and `AdviceCleared`.
2. **Toasts:** stacked, at most 3 at a time, fading after 6 s, with no alarm sounds.
3. **Interaction:** a history panel with filters. Clicking a notification focuses its subject and opens its inspector or explanation.
4. **Advisor notes** stay pinned in a side list until cleared, and can be dismissed (`DismissAdvice`).

**Acceptance:** screenshots of the feed during the slice scenario.

**Out of scope:** happenings dialogs (M2-27).

---

### M1-27 · Main menu, pause menu and save/load UI

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M1-14, M1-18 | `game/ui/menus/**`, `game/host/Persistence/**`, `game/scenes/MainMenu.tscn` |

**Reads:** ADR-0007; M1-14 API.

**Goal:** start, continue, save and load games, with the command log recorded alongside each save.

**Steps:**

1. **Main menu:** New Game (starter island, seed shown), Continue (latest save), Load, Settings (stub) and Quit.
2. **Pause menu:** Resume, Save, Load, Settings (stub) and Quit to Menu. The game pauses while it's open.
3. **Save slots** in `user://saves/`: name, play time, day and era, and a thumbnail later. Saving writes the `.evsave` and the matching `.evlog` (`game/host/Persistence/CommandLogRecorder` records every enqueued command with its tick).
4. **Autosave:** every 10 in-game days into a single slot. Rotation comes in M2-15.

**Acceptance:** save, quit to menu, load, and the island is identical (hash shown in a debug label). Continue loads the latest save.

**Out of scope:** settings content (M4-05).

---

### M1-28 · Godot smoke v2 and view boundary check

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M1-13, M1-20, M1-27 | `game/host/Smoke/**`, `scripts/godot-smoke.sh`, `scripts/verify.sh` (section `godot` only), `tests/Evenmere.Game.Tests/**` (new), `Evenmere.sln` (add that test project only) |

**Reads:** M0-09; ADR-0006.

**Goal:** the smoke test exercises the real game scene, and a check stops view code from touching sim state types.

**Steps:**

1. **Smoke mode:** loads `Main.tscn` with the starter island, enqueues the commands of `tests/fixtures/scenarios/slice_first_houses.json` at their ticks, and runs 1,000 ticks with the renderers active. It fails if any error is logged.
2. **Hash check:** the smoke prints the final hash. The script compares it with `evenmere run` replaying the same scenario.
3. **Boundary check:** state classes live in `Evenmere.Sim.Systems.<Name>` namespaces, so a namespace grep can't catch misuse. Policy: the game may use Core *value* types that appear in queries (`Amount`, `Ratio`, `EntityId`, the content IDs, `GameClock`, `Season`), but never `WorldState`, a system state class, `Pcg32`, `EntityIdAllocator` or `IdTable<T>`. Use `StateTypeWalker.Walk(typeof(WorldState)).TypesDeclaredIn(...)` for the Sim assembly, plus those three Core types. Instead, add a reflection test (in `tests/Evenmere.Sim.Tests/Architecture/GameBoundaryTests.cs`, owned by this task). It loads the built `Evenmere.Game` assembly from its build output and fails if any type, field, method signature or IL member reference in it uses `WorldState`, or any type reachable from `WorldState`'s fields. Reuse the M0-06 reachability walker.

4. **Game unit tests:** create `tests/Evenmere.Game.Tests` (xUnit v3, referencing `game/Evenmere.Game.csproj`) for the Godot-free pieces: `FixedStepper` (exact tick counts at each speed, the backlog cap, pause, the 8-tick cap), `ContentPaths.Resolve(env, exported, projectDir)` and `LaunchOptions`. These were verified only manually in M0-09. If referencing the Godot project from a test project proves impractical, move these pure classes into a small `game/host/Pure/` folder and explain the approach in the report.

**Acceptance:** `REQUIRE_GODOT=1 scripts/verify.sh` passes locally, an intentional reference to `WorldState` or to a system's state class in `game/` fails the boundary test, and the game unit tests run under `dotnet test`.

**Out of scope:** performance assertions (M2-16).

---

### M1-29 · GitHub CI and PR flow

| Lane | Depends on | Owns |
| --- | --- | --- |
| infra | M1-28, M1-16 | `.github/**`, `docs/implementation/README.md` (§Dispatch protocol step 7 only), `AGENTS.md` (§Git workflow only) |

**Reads:** AGENTS.md (§Git workflow); ADR-0008 (§CI matrix); the developer's note: *"once we have a functional set of the game we will need to establish a github CI flow which will then impose use of PR+CI for landing future commits"*.

**Goal:** every future change lands through a PR whose CI runs the same checks agents run locally.

**Steps:**

1. **`.github/workflows/ci.yml`:**
   - Triggers: `pull_request` and `push` to `main`, with a concurrency group per ref.
   - Matrix: `ubuntu-latest` and `macos-latest`.
   - Steps: checkout, `actions/setup-dotnet` (10.0.x), NuGet cache, install Godot 4.7.2 .NET, pinned to the same version as the development Mac (for example `chickensoft-games/setup-godot` with `use-dotnet: true`), then `REQUIRE_GODOT=1 scripts/verify.sh`.
   - Upload test results (TRX) as artifacts.
2. **`.github/workflows/nightly.yml`:** a scheduled run of the balance bot `slice.json` on seeds 1–3, uploading the reports. M2 adds perf.
3. **`.github/pull_request_template.md`:** Task ID, Summary, Acceptance evidence, and a checklist (verify passes, Owns respected, docs updated).
4. **Docs:** update the AGENTS.md git workflow and the dispatch protocol step 7 to say that PRs are now mandatory.
5. **Branch protection on `main`:** require PRs, require the two `verify` checks, block force pushes, and allow squash merging only.
   - This needs admin rights. If the agent's token can't do it, write the exact settings in the report for the developer to apply. This is a **human step**.

**Acceptance:**

- A test PR with a trivial docs change shows both checks green.
- A PR with a deliberately failing test is blocked.
- Branch protection is confirmed, by a screenshot or an API response.

**Out of scope:** release and export pipelines (M4-09, M4-10).

---

### M1-30 · Vertical-slice playtest and gate review

| Lane | Depends on | Owns |
| --- | --- | --- |
| human | M1-29 | `docs/implementation/M1-vertical-slice.md` (§Gate record) |

**Goal:** confirm that the slice is calm, legible and fun enough to build on.

**Steps:**

1. The developer plays from a new game to 100 Villagers using only the UI, noting the time taken, every moment of confusion, and any number that didn't explain itself.
2. The orchestrator attaches the balance bot reports (seeds 1–3), the scenario hashes and the fps measurements.
3. Triage the findings into new M2 cards or fixes, and record the decisions in the gate record.

**Acceptance:** every gate checkbox is ticked or explicitly waived with a reason.

---

## Gate record

_Filled in when M1 closes._
