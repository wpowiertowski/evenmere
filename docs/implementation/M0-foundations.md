# M0 — Foundations

**Goal:** a buildable, testable, deterministic skeleton that every later task plugs into. M0 adds no gameplay.

**Gate:**

- [ ] `scripts/verify.sh` passes on a clean clone on macOS, and on Linux if one is available.
- [ ] `evenmere run --content tests/fixtures/content/minimal --ticks 10000 --seed 42` prints the same state hash on two runs. (`content/` stays empty until M1-02, so M0 runs against the minimal fixture pack.)
- [ ] A save-to-bytes / load round trip at tick 5,000 continues to the same hash at tick 10,000.
- [ ] `scripts/godot-smoke.sh` prints `SMOKE OK`, and its hash matches `evenmere run` for the same seed and tick count.
- [ ] The architecture and determinism guard tests pass.

**Waves** (tasks in the same wave can run in parallel):

| Wave | Tasks |
| --- | --- |
| 1 | M0-01 |
| 2 | M0-02 |
| 3 | M0-03 |
| 4 | M0-04, M0-05 |
| 5 | M0-06, M0-07, M0-08, M0-09 |

## Task index

| ID | Title | Lane | Depends on | Status |
| --- | --- | --- | --- | --- |
| M0-01 | Solution scaffold and build configuration | infra | — | done |
| M0-02 | Core primitives | core | M0-01 | done |
| M0-03 | Content definitions, loader and ContentDb | content-infra | M0-02 | done |
| M0-04 | JSON Schemas and content linter v1 | content-infra | M0-03 | todo |
| M0-05 | Simulation skeleton | sim | M0-03 | done |
| M0-06 | Architecture and determinism guard tests | sim | M0-05 | todo |
| M0-07 | Tools CLI v0 | tools | M0-04, M0-05 | todo |
| M0-08 | Scenario test harness | sim | M0-05 | todo |
| M0-09 | Godot project and SimHost bridge | game | M0-05 | todo |

---

### M0-01 · Solution scaffold and build configuration

| Lane | Depends on | Owns |
| --- | --- | --- |
| infra | — | `Evenmere.sln`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, `src/BannedSymbols.txt`, `src/*/*.csproj`, `tests/*/*.csproj`, one placeholder file per project, `scripts/verify.sh` |

**Reads:** architecture.md (§Engine, §Repository layout and module boundaries); ADR-0001, ADR-0002, ADR-0003.

**Goal:** an empty .NET solution with every project wired by the allowed references, strict build settings, central package management and the single verification script.

**Steps:**

1. Confirm `dotnet --list-sdks` shows a 10.0.x SDK. Write `global.json` pinning `10.0.100` with `"rollForward": "latestFeature"`.
2. Create these projects, all targeting `net10.0`:
   - Class libraries: `src/Evenmere.Core`, `src/Evenmere.Content` and `src/Evenmere.Sim`.
   - A console app: `src/Evenmere.Tools`, with `AssemblyName` `evenmere`.
   - xUnit v3 test projects: `tests/Evenmere.Core.Tests`, `tests/Evenmere.Content.Tests` and `tests/Evenmere.Sim.Tests`.
   - Add them all to `Evenmere.sln` under the solution folders `src` and `tests`.
3. Add the project references exactly as in the ADR-0002 table. Each test project references its subject; `Evenmere.Sim.Tests` also references Content and Core.
4. Write `Directory.Build.props` with:
   - `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`
   - `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild=true`
   - `Deterministic=true`, `InvariantGlobalization=true`
   - `GenerateDocumentationFile=true` for projects under `src/`
5. Write `Directory.Packages.props` with `ManagePackageVersionsCentrally=true` and versions for `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Shouldly` and `Microsoft.CodeAnalysis.BannedApiAnalyzers`. Use current stable versions.
6. Write `src/BannedSymbols.txt` with the list from ADR-0003: `System.Random`, `DateTime.Now`/`UtcNow`, `Environment.TickCount`/`TickCount64`, `Guid.NewGuid`, `Parallel`, `Task.Run`, `ThreadPool` and `Stopwatch`. Wire the analyser into `Evenmere.Core` and `Evenmere.Sim` only. `Stopwatch` is allowed later in `Sim/Diagnostics` through a single `#pragma` with a comment.
7. Write `.editorconfig`:
   - Four-space indent for C#, two-space for JSON, LF line endings, final newline.
   - File-scoped namespaces (warning), `_camelCase` private fields, and `var` only when the type is apparent.
8. Append to `.gitignore`, keeping the existing Godot entries: `bin/`, `obj/`, `*.user`, `.vs/`, `.idea/`, `TestResults/`, `artifacts/`, `game/content/` (staged by export) and `*.evsave`, with a negated exception `!tests/fixtures/**/*.evsave`.
9. Write `scripts/verify.sh` (bash, `set -euo pipefail`, executable). It `cd`s to the repo root. If `dotnet` isn't on `PATH` but `/usr/local/share/dotnet/dotnet` exists, it prepends that directory. It runs in these marked sections:
   - `# --- section: build ---`: `dotnet restore`, then `dotnet build -c Release --no-restore`
   - `# --- section: test ---`: `dotnet test -c Release --no-build`
   - `# --- section: lint ---`: placeholder `echo "lint: not yet wired"`
   - `# --- section: godot ---`: placeholder `echo "godot smoke: not yet wired"`
10. Add one trivial passing test per test project so that `dotnet test` exercises the pipeline.

**Acceptance:**

- `scripts/verify.sh` exits 0 on a clean clone. Paste the tail of its output in your report.
- `dotnet build -c Release` reports 0 warnings.
- Adding `var r = new System.Random();` to any file in `Evenmere.Sim` fails the build with a banned-API error. Check this locally and don't commit it.
- The output of `dotnet list <project> reference` for each project matches ADR-0002. Paste it in your report.

**Out of scope:** any real types (M0-02 onward) and the Godot project (M0-09).

---

### M0-02 · Core primitives

| Lane | Depends on | Owns |
| --- | --- | --- |
| core | M0-01 | `src/Evenmere.Core/**`, `tests/Evenmere.Core.Tests/**`, `Directory.Packages.props` (add `System.IO.Hashing`) |

**Reads:** ADR-0003; architecture.md (§Determinism rules, §Time model).

**Goal:** the small, exhaustively tested value types that every other layer uses.

**Steps:**

1. **`Amount`** (`readonly record struct`, `long` milli-units):
   - Factories `FromUnits(long)`, `FromMilli(long)` and `Parse(string)`, which accepts at most three decimals with an invariant culture and throws `FormatException` beyond that.
   - Operators `+`, `-`, comparison and `Min`/`Max`; `MultiplyBy(Ratio)` and `MulDiv(long num, long den)`, both rounding toward negative infinity.
   - `WholeUnits` (floor) and `ToString()` → `"12.5"`.
   - Use checked arithmetic so overflow throws.
2. **`Ratio`** (`readonly record struct`, `int` per-mille):
   - `One` (1000), `Zero` and `FromPercent(int)`.
   - `FromBonusPercent(int sumOfPercents)` → `1000 + 10 × sum`, clamped at ≥ 0.
   - `Apply(Amount)`, delegating to `Amount.MultiplyBy`.
3. **`EntityId`** (`readonly record struct`, `int`; 0 = none) and **`EntityIdAllocator`**: monotonic, never reuses IDs, with serialisable state.
4. **Content IDs:** one concrete `public readonly record struct XxxId(ushort Value)` per kind, implementing `IContentId<TSelf>` (`ushort Value`, `static abstract TSelf From(ushort)`) for generic code. Global using aliases were rejected because they don't cross assembly boundaries.
   - The types are `GoodId`, `BuildingTypeId`, `RecipeId`, `TierId`, `TechId`, `CivicId`, `TraditionId`, `FestivalId`, `GreatWorkTypeId`, `MonumentId`, `HappeningId`, `EraId` and `TerrainId`.
5. **`Pcg32`** (`struct`, with `ulong state, inc`):
   - `Pcg32(ulong seed, ulong stream)` with the reference seeding, the static `FromState(state, inc)` for saves, `NextUInt()`, `NextInt(int maxExclusive)` (unbiased, Lemire's method), `NextInt(min, maxExclusive)`, and `Chance(Ratio)`, which always consumes one draw.
   - Known-answer test: the pcg-c reference demo with seed 42 and stream 54 yields `0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e` as its first outputs.
   - `Pcg32.ForSystem(ulong worldSeed, string systemName)` derives a stream from a stable hash of the name.
6. **`StableHasher`**: an incremental XxHash64 (from `System.IO.Hashing`) with `Add(int)`, `Add(long)`, `Add(ulong)`, `Add(ReadOnlySpan<byte>)` and `Add(string)` (UTF-8, length-prefixed), plus `ToUInt64()`.
7. **`IdTable<T>`**: dense storage keyed by `EntityId`.
   - API: `Add(EntityId, T)`, `Remove`, `TryGet`, `ref T GetRef(EntityId)`, `Contains` and `Count`.
   - The enumerator yields `(EntityId, ref T)` in ascending ID order and skips removed slots. It must not allocate.
   - Removal leaves a tombstone, so removing while enumerating is safe.
   - `Add` compacts the tombstones once they outnumber `max(32, live)`, so high-churn tables such as transport jobs stay bounded. Adding while enumerating throws.
8. **`GameTime`** constants and **`GameClock`**:
   - Constants: `TicksPerDay = 120`, `DaysPerSeason = 10`, `SeasonsPerYear = 4`.
   - `GameClock` (readonly struct over `long tick`): `Day`, `TickOfDay`, `Season` (enum `Spring` / `Summer` / `Autumn` / `Winter`), `Year`, `IsDayStart`, `IsSeasonStart`, the static `BucketOf(EntityId)` (`id % 120`), and `IsInBucket(EntityId)`.
   - Namespaces follow the folders: `Evenmere.Core.Numerics`, `.Ids`, `.Rng`, `.Hashing`, `.Collections` and `.Time`.

**Acceptance:**

- `dotnet test tests/Evenmere.Core.Tests` passes, with tests for:
  - Parsing and formatting `Amount` round-trips, including `"0.001"`, `"12.5"` and `"-3.25"`. `"1.2345"` throws.
  - Negative values round toward negative infinity in `MultiplyBy`.
  - `long` overflow throws.
  - `Ratio.FromBonusPercent(20 + 20)` gives 1400 (no compounding).
  - The `Pcg32` known-answer vector, and `NextInt(7)` always in range over 100k draws.
  - `IdTable` iterates in ascending order after interleaved adds and removes.
  - Clock boundaries: tick 119→120 is a new day; day 9→10 is a new season; day 40 is year 1, spring.
- No public type in `Evenmere.Core` lacks XML docs (the build enforces this).

**Out of scope:** content-specific logic and serialisation attributes. MessagePack formatters for these types are added by M0-05 in Sim.

---

### M0-03 · Content definitions, loader and ContentDb

| Lane | Depends on | Owns |
| --- | --- | --- |
| content-infra | M0-02 | `src/Evenmere.Content/**` (except `Lint/`), `tests/Evenmere.Content.Tests/**` (except `Lint/`), `tests/fixtures/content/minimal/**` |

**Reads:**

- ADR-0004.
- architecture.md (§Data-driven content pipeline, including the effects and conditions vocabularies).
- progression.md (§Population tiers, §Buildings), for the fields definitions need.
- The authoring notes in tech-tree.md and culture-tree.md.

**Goal:** typed, immutable content definitions loaded from JSON into a `ContentDb`, with diagnostics instead of exceptions.

**Steps:**

1. **Definition records** (sealed, immutable) in `Definitions/`:
   - `GoodDef` (`category`, `era`, `tags`).
   - `BuildingDef`, with `category`, `era`, `footprint`, `placement` (terrain list, `roadAccess`, `coastline`, `riverside`, `deposit`, `fertility` crop and minimum share, `areaYield` spec), `cost`, `buildDays`, `workforce`, `recipe?`, `storage?` (reach, carts), `service?` (service type, coverage), `outputs?` (knowledge/culture per day), `slots?` (great-work slot types and counts), `upgradeOf?`, `stages?` (monuments), `fuel?`, `upkeepCoin?`, `unique`, `start` and `tags`.
   - `RecipeDef`.
   - `TierDef`, with `order`, `era`, `capacity`, `advanceFrom?`, `upgradeCost`, `basicNeeds` (goods per 1,000 per day, services), `comfortNeeds` and `outputs` per 1,000 per day.
   - `TechDef` and `CivicDef`, sharing a `NodeDef` shape: `era`, `cost`, `requires`, `effects`, `inspiration?`, `gateway`, `repeatable?`.
   - `TraditionDef` (`slot`, `effects`, `tags`, `style?`), `FestivalDef`, `GreatWorkTypeDef`, `MonumentDef` (or a `BuildingDef` with `stages`; document your choice), `HappeningDef` (`trigger` condition, `options` with effects), `EraDef` (`order`, `requires` tech and civic) and `TerrainDef` (`buildable`, `access` flag, `blocksRoads`).
   - `RulesDef`: the single file `content/rules/core.json` holding game-wide tunables (cart ticks per tile, buffer sizes, decline grace days, advance streak days, inspiration discount, road type costs). Systems read tunables from here, never from constants.
2. **Effects and conditions:** closed hierarchies (an abstract record with sealed subtypes) for the exact vocabularies in architecture.md. Write custom `System.Text.Json` converters that read the single-key object form (`{ "unlockBuilding": "bakery" }`). An unknown key produces a diagnostic, never an exception.
3. **Raw loading:**
   - `ContentLoader.Load(IReadOnlyList<string> roots)` reads `content/<type>/*.json` for each root, in ordinal path order.
   - It uses a source-generated `JsonSerializerContext` and collects `ContentDiagnostic { Code, Severity, File, JsonPointer, Message }`.
   - In a later root, an item with the same ID replaces the earlier item whole.
4. **Interning:** per type, sort the string IDs ordinally and assign `ushort` IDs. Resolve every reference to a typed ID; an unresolved reference produces `EVL002` with the file and pointer. Quantities are parsed with `Amount.Parse`, so more than three decimals produces a diagnostic.
5. **`ContentDb`:**
   - Immutable arrays per type indexed by ID, plus `TryGet(string, out TId)` / `Get<T>(id)` lookups.
   - `Required`: named IDs for `founders_hall`, `settler_house`, `dirt_road`, `settlers` and `storehouse`, with a diagnostic if any is missing.
   - `ContentHash`: XxHash64 over the files sorted by relative path, with CRLF normalised to LF.
6. **Locale:** `LocaleTable.Load(root, lang)` merges every `locale/<lang>/*.json` file. A duplicate key produces a diagnostic, and lookups use `Get(key)`.
7. **Minimal fixture pack** in `tests/fixtures/content/minimal/`, valid against the shapes above:
   - 2 eras, 4 terrains, 3 goods (`wood`, `timber`, `fish`), a building for each `Required` ID plus `woodcutters_hut`, `sawpit` and `fishing_hut`, and 3 recipes (one per producing building).
   - 2 tiers, 2 techs, 1 civic and 1 tradition.
   - `rules/core.json`.
   - English locale for all of them.

**Acceptance:**

- The minimal pack loads with zero diagnostics.
- Integer IDs follow ordinal string order (tested).
- `ContentHash` is identical for an LF copy and a CRLF copy of the fixture (the test writes the temporary copies).
- Fixtures with a broken reference produce `EVL002`, naming the right file and JSON pointer.
- `"1.2345"` in a cost produces a diagnostic.
- An override pack replaces an item by ID (tested).

**Out of scope:** JSON Schemas and lint rules other than reference resolution (M0-04).

---

### M0-04 · JSON Schemas and content linter v1

| Lane | Depends on | Owns |
| --- | --- | --- |
| content-infra | M0-03 | `content/schemas/**`, `content/README.md`, `src/Evenmere.Content/Lint/**`, `tests/Evenmere.Content.Tests/Lint/**`, `tests/fixtures/content/lint/**`, `Directory.Packages.props` (add `JsonSchema.Net`) |

**Reads:** architecture.md (§Content linter); ADR-0004; ADR-0010.

**Goal:** a schema per content type, and a linter that implements rules EVL001–EVL008 with precise diagnostics.

**Steps:**

1. Write one JSON Schema (draft 2020-12) per type in `content/schemas/<type>.schema.json`, matching the M0-03 definitions. Set `additionalProperties: false`, enforce the `snake_case` ID pattern, and limit quantities to `multipleOf: 0.001`.
2. Implement `ContentLinter.Run(ContentDb db, RawContent raw) → IReadOnlyList<ContentDiagnostic>`, sorted by code, then file, then pointer, with the following rules:
   - **EVL001:** schema validation (JsonSchema.Net).
   - **EVL002:** references resolve. Reuse the loader's results and add locale-key checks.
   - **EVL003:** tech and civic graphs are acyclic, and every node is reachable from a root.
   - **EVL004:** every good has a producer (a recipe output or a gatherer) and a consumer (a recipe input, a tier need, a building cost, a stage cost, a festival cost or fuel).
   - **EVL005:** every building, recipe, tier, tradition and festival has exactly one unlock source or `"start": true`.
   - **EVL006:** every tier's basic needs are unlockable by nodes of the tier's era or earlier, where a tier's era is the era of its unlock source.
   - **EVL007:** every item has `<type>.<id>.name` in `locale/en`.
   - **EVL008:** every transition between consecutive eras has exactly one tech gateway and one civic gateway, and no node requires a node from a later era.
3. Write `content/README.md`, a two-page authoring guide covering the folder layout, ID rules, unlock rules, quantities, locale files and how to run lint.

**Acceptance:**

- Each rule has at least one failing fixture under `tests/fixtures/content/lint/<code>-*`, and a test asserting the exact code and pointer.
- The minimal fixture passes every rule.
- Every schema validates the minimal fixture, and a fixture with an extra property fails EVL001.

**Out of scope:** EVL009 (M3-01) and EVL010 (M2-14). Real game content (M1-02).

---

### M0-05 · Simulation skeleton

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M0-03 | `src/Evenmere.Sim/**`, `tests/Evenmere.Sim.Tests/Skeleton/**`, `tests/Evenmere.Sim.Tests/Fixtures/**`, `Directory.Packages.props` (add `MessagePack`) |

**Reads:** architecture.md (§Simulation core, all of it); ADR-0003, ADR-0005, ADR-0006, ADR-0007.

**Goal:** `ISimulation` running an empty world through the full system pipeline deterministically, with every system folder stubbed so later tasks only fill their own folder.

**Steps:**

1. **Contracts:** `ISimulation` exactly as in architecture.md (§Contracts), plus `Command`, `SimEvent`, `CommandRejected(Tick, Command, RejectReason Code, string Detail)` and `ISimQueries`.
2. **`WorldState`** in `State/WorldState.cs`:
   - Holds `GameClock`, `EntityIdAllocator`, `ulong Seed`, one `Pcg32` per system (via `Pcg32.ForSystem`), and **one state object per system**: Map, Roads, Storage, Modifiers, Construction, Workforce, Production, Logistics, Exploration, Needs, Population, Economy, Research, Culture, Progression, Happenings, Advisors and Statistics.
   - Each state object is an empty `sealed class <Name>State` in `Systems/<Name>/`, marked with `[MessagePackObject]`, with a comment naming the task that fills it.
3. **Commands:**
   - `ICommandHandler<TCommand>` with `Validate(SimContext, TCommand) → CommandResult` and `Apply(SimContext, TCommand)`.
   - A `CommandRouter` that maps each command type to its handler; registering twice throws.
   - Commands apply in enqueue order at tick start. A rejected command emits `CommandRejected`.
4. **Systems:**
   - `ISimSystem { string Name; void Register(CommandRouter router); void Run(SimContext ctx); }`.
   - `SystemPipeline` builds every system in the architecture order (§System order) with its cadence: every tick, daily, or daily-bucketed.
   - `SimContext` carries `ContentDb`, `WorldState`, the clock, the event sink, the queries and `IsInBucket(EntityId)`.
   - Every system is a stub with an empty `Run`.
5. **Queries:** `ISimQueries` exposes one property per system interface (`IMapQueries Map`, …, plus `IPlacementQueries Placement` and `IExplainQueries Explain`). Every interface is empty in M0; M1-00 adds the methods.
6. **Storage pool:** `Systems/Storage/StoragePool`, an `Amount` per `GoodId` in a dense array with `Get`, `Add` and `TryTake` (all or nothing), plus `IStorageQueries.Stock(GoodId)`. This is the one real system in M0, because Construction, Logistics and Needs all depend on it.
7. **Serialisation and hash:**
   - MessagePack-CSharp formatters for the M0-02 types.
   - `StateSerializer.Write(WorldState) → byte[]` (canonical, uncompressed) and `Read(byte[], ContentDb) → WorldState`.
   - `ComputeStateHash()` = XxHash64 over `Write()` bytes.
8. **New game:** `Simulation.CreateNew(ContentDb, NewGameOptions { ulong Seed })` creates an empty world. The map comes in M1-01.
9. **Diagnostics:** `Sim/Diagnostics/SimProfiler` records the elapsed ticks per system, using `Stopwatch` with the documented pragma. It is exposed through `ISimulation` as `IProfilerReadout` (an optional extra interface).
10. **Fixtures:** a `WorldBuilder` in `tests/Evenmere.Sim.Tests/Fixtures/` that creates a simulation from the minimal content pack. Later tasks extend it with maps and buildings.

**Acceptance:**

- 10,000 ticks on an empty world give identical hashes across two runs (test).
- Serialising at tick 5,000, deserialising and continuing to tick 10,000 gives the same hash as an uninterrupted run (test).
- A pipeline test asserts the exact system order and cadences from architecture.md.
- A command with no handler is rejected with `RejectReason.UnknownCommand`; a command whose validation fails emits `CommandRejected` (tests with test-only commands).
- Every folder listed in step 2 exists with its stub state and system.

**Out of scope:** any gameplay logic, save files on disk (M1-14), the map (M1-01).

---

### M0-06 · Architecture and determinism guard tests

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M0-05 | `tests/Evenmere.Sim.Tests/Architecture/**` |

**Reads:** ADR-0002, ADR-0003, ADR-0006, ADR-0007.

**Goal:** turn the boundary and determinism rules into tests that fail loudly.

**Steps:** write reflection-based tests that assert:

1. The Core, Content and Sim assemblies reference no assembly whose name starts with `Godot` or `Evenmere.Game`, and Core references nothing beyond the BCL and `System.IO.Hashing`.
2. Every type reachable from `WorldState` fields (recursively, including generic arguments and arrays) has no field of type `float`, `double`, `decimal`, `Dictionary<,>`, `HashSet<>` or `ConcurrentDictionary<,>`.
3. Every concrete `Command` subtype in `Evenmere.Sim` has exactly one handler registered by the full pipeline.
4. MessagePack `[Key]` values are unique within each state type, and no state type uses string keys.
5. `Evenmere.Sim` has no public static mutable fields.
6. Every checker is itself proven by a negative test against deliberately bad types declared inside the test assembly.

**Acceptance:** all tests pass on the M0-05 skeleton, and each negative test proves its checker fails on a bad type.

**Out of scope:** the check that `game/` never uses sim state types (`WorldState` and every type reachable from it); that comes in M1-28.

---

### M0-07 · Tools CLI v0

| Lane | Depends on | Owns |
| --- | --- | --- |
| tools | M0-04, M0-05 | `src/Evenmere.Tools/**`, `scripts/verify.sh` (section `lint` only), `Directory.Packages.props` (add `System.CommandLine`) |

**Reads:** architecture.md (§Testing, tooling and conventions); AGENTS.md (§Commands).

**Goal:** the `evenmere` command-line tool that agents and CI use to lint content and run the simulation headless.

**Steps:**

1. Build the root command `evenmere` with System.CommandLine. The global option `--content <dir>` may repeat, and defaults to `content` relative to the repo root.
2. `lint [--format text|json]` loads the content, runs `ContentLinter` and prints the diagnostics. It exits 1 on any error, and prints the item counts and content hash on success.
3. `run --ticks N --seed S [--hash-every K]` creates a new game, ticks N times and prints `hash=<hex16>`, followed by a per-system timing table from the profiler. `--hash-every` prints intermediate hashes.
4. `content-hash` prints the content hash.
5. Wire the `lint` section of `scripts/verify.sh`:
   - Always lint `tests/fixtures/content/minimal`.
   - Lint `content/` only once `content/eras/` exists, so an empty `content/` doesn't fail before M1-02.

**Acceptance:**

- `dotnet run --project src/Evenmere.Tools -- run --content tests/fixtures/content/minimal --ticks 10000 --seed 42` prints the same hash twice, and the hash differs for seed 43.
- `lint --content tests/fixtures/content/minimal` exits 0, and `lint --content tests/fixtures/content/lint/EVL003-cycle` exits 1 and prints `EVL003`.
- `scripts/verify.sh` runs the lint section.

**Out of scope:** `perf`, `balance`, `replay` and `mapgen` commands (later milestones).

---

### M0-08 · Scenario test harness

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M0-05 | `tests/Evenmere.Sim.Tests/Scenarios/**`, `tests/fixtures/scenarios/**` |

**Reads:** architecture.md (§Testing); ADR-0003.

**Goal:** a data-driven way to script commands over time and assert outcomes and golden hashes, used by every system from M1 onward.

**Steps:**

1. **Scenario file format** (JSON in `tests/fixtures/scenarios/<name>.json`):
   - `{ "content": "<fixture dir>", "map": "<fixture map id or null>", "seed": 42, "ticks": 10000 }`
   - `"commands": [{ "tick": 0, "type": "PlaceBuilding", "args": { … } }]`
   - `"expect": { "hash": "<hex16>", "events": [{ "type": "…", "atLeast": 1 }], "stock": { "timber": ">= 10" } }`
2. **Command registry:** build a test-side registry mapping `type` to a Command record, by reflection over `Evenmere.Sim` command types, and deserialise `args` into it with System.Text.Json.
3. **`ScenarioRunner`:** runs the scenario, collects events, evaluates the expectations and returns a readable failure report: the first failing expectation, the expected and actual hash, and the last 20 events.
4. **Golden files:** when `EVENMERE_UPDATE_GOLDEN=1` is set, a hash mismatch rewrites the `expect.hash` in the file instead of failing.
5. **Discovery:** an xUnit theory runs every scenario file in the folder, each as its own test case.
6. **Sample:** `empty_world_10k.json` (empty world, no commands, golden hash).

**Acceptance:**

- The sample passes.
- Editing its seed makes it fail, and the failure report shows both hashes.
- Running with `EVENMERE_UPDATE_GOLDEN=1` rewrites the hash, after which the test passes.

**Out of scope:** gameplay scenarios (added by each M1 task and by M1-13).

---

### M0-09 · Godot project and SimHost bridge

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M0-05 | `game/**`, `scripts/godot-smoke.sh`, `scripts/verify.sh` (section `godot` only), `Evenmere.sln` (add the game project only) |

**Reads:** architecture.md (§Presentation layer, §Bridging sim and view); ADR-0001, ADR-0006, ADR-0008.

**Goal:** a Godot 4.7 .NET project that builds with `dotnet build`, hosts the simulation through `SimHost`, and passes a headless smoke test.

**Steps:**

1. **Project:** create `game/project.godot` with these settings:
   - Name `Evenmere`, renderer Forward+.
   - Default window 1600 × 900, minimum 1280 × 800.
   - Stretch mode `canvas_items`, aspect `expand`.
   - Main scene `res://scenes/Main.tscn`.
2. **Game project file:** `game/Evenmere.Game.csproj` uses the `Godot.NET.Sdk/4.7.2` SDK from NuGet, targets `net10.0` with `EnableDynamicLoading=true`, and has `ProjectReference`s to Core, Content and Sim. Add it to `Evenmere.sln` under a `game` solution folder. The `net10.0` setup was verified on 2026-10-19 (see ADR-0001).
3. **`game/host/ContentPaths.cs`:** resolves the content root as `$EVENMERE_CONTENT` if set, else `res://content` if it exists (exported builds), else `<repo>/content` found by walking up from `ProjectSettings.GlobalizePath("res://")`.
4. **`game/host/SimHost.cs`** (a `Node`):
   - Loads `ContentDb` and creates or loads an `ISimulation`.
   - Runs a fixed-step accumulator, with speed values pause, 1×, 2× and 4× (debug builds add 16×).
   - Enforces a per-frame budget of at most 8 ticks and 4 ms, and exposes `InterpolationAlpha`.
   - After ticking, drains events and raises the C# event `EventsDrained(IReadOnlyList<SimEvent>)`, plus a stub `DirtyChunks` until M1-01.
   - Exposes `Enqueue(Command)` and `ISimQueries Query`; queries are valid only between ticks.
5. **`game/scenes/Main.tscn`:** a `SimHost`, a `Camera3D`, a `DirectionalLight3D` and a `Label` showing the tick, day and season.
6. **Smoke mode:**
   - Parse user arguments after `--` for `--smoke-ticks=N` and `--seed=S`.
   - Run N ticks as fast as possible, print `SMOKE OK hash=<hex16>` and quit with exit code 0.
   - Any exception prints `SMOKE FAIL <message>` and exits with code 1.
7. **`scripts/godot-smoke.sh`:**
   - Locate Godot from `$GODOT`, then `godot` on `PATH`, then `/Applications/Godot_mono.app/Contents/MacOS/Godot`. If none is found, print `godot smoke: skipped (Godot not found)` and exit 0, unless `REQUIRE_GODOT=1`.
   - Until `content/eras/` exists, export `EVENMERE_CONTENT=<repo>/tests/fixtures/content/minimal`, using the same rule as the lint section.
   - Run `dotnet build game/Evenmere.Game.csproj -c Debug`, then `"$GODOT" --headless --path game --import`, then `"$GODOT" --headless --path game -- --smoke-ticks=1000 --seed=42`.
   - Grep for `SMOKE OK`.
   - Guard every Godot run against hangs **without** GNU `timeout`, which macOS lacks. Use `perl -e 'alarm 300; exec @ARGV' "$GODOT" …` or Godot's own `--quit-after`.
   - If `dotnet` isn't on `PATH` but `/usr/local/share/dotnet/dotnet` exists, prepend that directory. Shells started before the SDK was installed miss it.
8. **Verify script:** wire the `godot` section of `scripts/verify.sh` to call `scripts/godot-smoke.sh`.

**Acceptance:**

- `dotnet build Evenmere.sln -c Release` builds the game project with 0 warnings.
- With Godot installed, `scripts/godot-smoke.sh` prints `SMOKE OK hash=X`, where X equals `evenmere run --ticks 1000 --seed 42` run against the same content root.
- `git status` shows no `.godot/` or other generated files after a run.

**Out of scope:** rendering, UI and input (M1).

---

## Gate record

_Filled in by the orchestrator when M0 closes: date, commit, hash outputs, and the platforms verified._
