# M2 — Era I complete

**Goal:** the whole Founding era on generated islands:

- Rivers and bridges, fog and expeditions, seasons.
- All Era I content: 13 techs, 8 civics, 8 traditions, festivals, the Standing Stones and happenings.
- Era advancement through both gateways.
- Graph views of both trees, overlays, the production-chain viewer and statistics.
- Save migrations, replay, the performance harness and a multi-seed balance bot.

From M2 onward, every task lands through a **PR with green CI**.

**Gate:**

- [ ] **Bot pacing:** balance bot v2 reaches Era II (both gateways) in 90–120 game minutes on 5 generated seeds and on the starter island, with no stall longer than one season.
- [ ] **Performance:** the `perf-2k` reference save (about 2,000 residents) runs at ≤ 0.6 ms average per tick on the development Mac, and the nightly CI job records it.
- [ ] **Migrations:** the M1 save fixture loads through the migration framework.
- [ ] **Playtest:** the developer plays a generated island to Era II and signs off on calmness, pacing and clarity.
- [ ] **Lint:** EVL010 (the staffing rule) passes on all content.

**Waves:**

| Wave | Tasks |
| --- | --- |
| 0 | M2-00 |
| A | M2-01, M2-04, M2-05, M2-07, M2-08, M2-09, M2-10, M2-14, M2-15, M2-26 |
| B | M2-02, M2-11, M2-12, M2-16, M2-18, M2-19, M2-20, M2-21, M2-22 |
| C | M2-03, M2-06, M2-13, M2-24, M2-25, M2-27 |
| D | M2-17, M2-23 |
| E | M2-28 |

## Task index

| ID | Title | Lane | Depends on | Status |
| --- | --- | --- | --- | --- |
| M2-00 | Kickoff: refine M2 cards against the code | sim | M1 gate | todo |
| M2-01 | Island generator: heightmap, coast and biomes | sim | M2-00 | todo |
| M2-02 | Rivers, fertility, deposits, regions and discoveries | sim | M2-01 | todo |
| M2-03 | Map generation preview CLI and seed gallery | tools | M2-02 | todo |
| M2-04 | Bridges and river crossing | sim | M2-00 | todo |
| M2-05 | Seasons | sim | M2-00 | todo |
| M2-06 | Exploration: fog, expeditions and discoveries | sim | M2-02, M2-10 | todo |
| M2-07 | Traditions and slots | sim | M2-00 | todo |
| M2-08 | Festivals | sim | M2-00 | todo |
| M2-09 | Inspirations and the full condition vocabulary | sim | M2-00 | todo |
| M2-10 | Monuments: multi-stage construction | sim | M2-00 | todo |
| M2-11 | Era advancement and gateways | sim | M2-09 | todo |
| M2-12 | Happenings | sim | M2-09 | todo |
| M2-13 | Era I full content | content | M2-06, M2-07, M2-08, M2-10, M2-11, M2-12, M2-14 | todo |
| M2-14 | Lint rule EVL010: lower-tier staffing | content-infra | M2-00 | todo |
| M2-15 | Save versioning, migrations and autosave rotation | sim | M2-00 | todo |
| M2-16 | Replay and performance harness, reference saves | tools | M2-15 | todo |
| M2-17 | Balance bot v2: generated islands to Era II | tools | M2-13, M2-16 | todo |
| M2-18 | Tech tree graph view | game | M2-09 | todo |
| M2-19 | Civics tree and traditions board | game | M2-07 | todo |
| M2-20 | Overlays: coverage, reach, fertility and deposits | game | M2-00 | todo |
| M2-21 | Production chain viewer | game | M2-00 | todo |
| M2-22 | Statistics graphs | game | M2-00 | todo |
| M2-23 | Fog, regions and expedition UI | game | M2-06 | todo |
| M2-24 | Seasons, weather and smoke visuals | game | M2-05 | todo |
| M2-25 | New-game setup: seed, size, starter island | game | M2-02 | todo |
| M2-26 | Debug overlay and cheat console | game | M2-00 | todo |
| M2-27 | Festivals, monuments and happenings UI | game | M2-08, M2-10, M2-12 | todo |
| M2-28 | Era I playtest and gate review | human | all M2 tasks | todo |

---

### M2-00 · Kickoff: refine M2 cards against the code

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim (planning) | M1 gate | `docs/implementation/M2-era-one.md` |

**Goal:** make every M2 card as precise as the M1 cards: exact folders, interface methods, events and fixtures, based on the code as it exists after M1.

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
| sim | M2-00 | `src/Evenmere.Sim/Systems/Map/Generation/**`, `tests/Evenmere.Sim.Tests/MapGeneration/**` |

**Reads:** architecture.md (§Island and map); ADR-0003 (integer-only generation); progression.md (§Terrain and access ladder).

**Goal:** a seeded, integer-only generator that produces a believable island of 256, 320 or 384 tiles across, identically on every CPU.

**Steps:**

1. **Noise:** integer value or simplex noise over fixed-point coordinates (Q16.16), with octaves summed in integers, implemented in-house.
2. **Heightmap:** noise × a radial falloff (squared distance, integer) gives sea, beach and land, quantised to 0–15 elevation.
3. **Biomes:** from elevation bands plus a moisture noise. Marsh sits on low, wet land; highlands and peaks on high bands; forest on moisture; plains elsewhere.
4. **Output:** a `MapDef`, the same type as authored maps, so the rest of the sim is agnostic.
5. **Start site:** the generator picks the Founders' Hall start site on the coast, and the M2-02 start guarantee validates it.

**Acceptance:**

- Golden hashes of the generated `MapDef` for seeds 1–5 at each size are committed, and CI on Linux reproduces the macOS hashes.
- Generating 320 × 320 takes ≤ 500 ms.
- A test grep finds no `Math.` floating-point calls in the generation folder.

**Out of scope:** rivers and features (M2-02).

---

### M2-02 · Rivers, fertility, deposits, regions and discoveries

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-01 | `src/Evenmere.Sim/Systems/Map/Generation/**`, `src/Evenmere.Sim/NewGame/MapSources/**`, `tests/Evenmere.Sim.Tests/MapGeneration/**` |

**Reads:** architecture.md (§Island and map, start guarantee); progression.md (§Terrain and access ladder: fertility, deposits).

**Goal:** a generated island that's playable and interesting, with Era I guaranteed in the start region.

**Steps:**

1. **Rivers:** trace downhill from highland springs to the sea, with integer flow accumulation. Width classes: narrow (≤ 3) and wide (4–8). Clay deposits go on riverbanks.
2. **Fertility:** one noise patch layer per crop, with crop affinities (grain and hops on plains; apples on plains and hills; grapes and tea on hills; madder and sugar beet on marsh, which becomes fen once drained).
3. **Deposits:** stone outcrops on hills, iron on hills, coal and copper in highlands, reefs offshore.
4. **Regions:** 40–80 regions by flood-filled seeded Voronoi, bounded by rivers where possible.
5. **Discoveries:** 3–6 natural wonders, 4–8 ruins and 3–6 shipwrecks, placed with deterministic names drawn from `locale/en/names.json`.
6. **Start guarantee:** the start region is bounded by rivers and meets the M1-03 start guarantees. If it doesn't, the generator re-seeds with a deterministic sub-seed, up to 16 attempts, and records the sub-seed in the `MapDef`.
7. **New game:** `MapSource.Generated(seed, size)` plugs into `NewGameOptions`.

**Acceptance:**

- Seeds 1–100 at the default size all satisfy the start guarantee (test).
- Golden `MapDef` hashes are committed.
- A scenario plays the slice bot script on seed 7 and reaches Villagers.

**Out of scope:** the fog presentation (M2-23).

---

### M2-03 · Map generation preview CLI and seed gallery

| Lane | Depends on | Owns |
| --- | --- | --- |
| tools | M2-02 | `src/Evenmere.Tools/MapGen/**`, `src/Evenmere.Tools/Commands/MapGenCommand.cs` |

**Goal:** see islands without opening Godot.

**Steps:**

1. Add `evenmere mapgen --seed S --size N --out file.png [--layer terrain|elevation|fertility:<crop>|regions|deposits]`, writing PNGs with a minimal in-house PNG encoder or a permitted package named in the card refinement.
2. Add `--gallery 1..20`, which writes a contact sheet.
3. Add ASCII output for agents (`--ascii`).

**Acceptance:** a gallery of 20 seeds is attached to the PR, and the ASCII output for seed 1 matches its golden file.

---

### M2-04 · Bridges and river crossing

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-00 | `src/Evenmere.Sim/Systems/Roads/**`, `tests/Evenmere.Sim.Tests/Roads/**` |

**Reads:** tech-tree.md (Bridge-building, Stone Bridges); progression.md (§Terrain and access ladder).

**Goal:** roads can cross rivers only as bridges whose span suits the unlocked technology.

**Steps:**

1. **Bridge road types:** a road segment that crosses river tiles must be a contiguous straight run of a bridge road type (`timber_bridge`, and later `stone_bridge`). Each type's content data gives its maximum span, which is checked against the river width class.
2. **Validation:** `ValidateRoad` reports `BridgeTooLong`, `BridgeNotStraight` and `TerrainAccessLocked`, citing the access flag (`narrow_rivers` or `wide_rivers`).
3. **Costs and fields:** bridge tiles have per-tile cost data, and distance fields treat bridges as normal road tiles with their own cost.

**Acceptance:** tests cover each error and a successful crossing, and a field across a bridge equals a BFS check.

---

### M2-05 · Seasons

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-00 | `src/Evenmere.Sim/Systems/Production/**`, `tests/Evenmere.Sim.Tests/Production/Seasons/**`, `content/rules/core.json` (`seasons` block only) |

**Reads:** architecture.md (§Time model: seasons); progression.md (season yield curve).

**Goal:** the calendar shapes farm yields gently, with no punishment.

**Steps:**

1. **Season curve:** the `seasons` block in `rules/core.json` defines the curve per tag (default `seasonal`: spring 1000, summer 1200, autumn 1100, winter 700).
2. **Production:** buildings tagged `seasonal` multiply productivity by the season factor.
3. **Events:** emit `SeasonChanged`. Season-conditional modifiers already exist in M1-04; check that they flip at season boundaries.

**Acceptance:** a grain farm's yearly output equals the mean of the curve × base, within rounding, and a test asserts the yield in each season.

---

### M2-06 · Exploration: fog, expeditions and discoveries

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-02, M2-10 | `src/Evenmere.Sim/Systems/Exploration/**`, `tests/Evenmere.Sim.Tests/Exploration/**`, `src/Evenmere.Sim/Systems/Construction/Placement/FogRule.cs` (new file, the `Fogged` rule only) |

**Reads:** architecture.md (§Exploration); culture-tree.md (§Attractiveness and visitors: natural wonders).

**Goal:** the island reveals itself over time through expeditions, and discoveries reward curiosity.

**Steps:**

1. **Fog:** per region. The start region and the coastline in view start revealed. Placement on a fogged tile is rejected (`Fogged`).
2. **`StartExpedition(lodgeId, regionId)`:** the target must be adjacent to revealed land. Duration = base days + the region's distance from the lodge × a terrain factor, and the `expeditionSpeed` modifier applies. One expedition at a time per lodge.
3. **Return:** reveal the region, emit `RegionRevealed` and a `DiscoveryRevealed` per feature, and add to the counters for conditions.
4. **Discovery effects** (applied once): ruins grant knowledge (and later a Relic, from M3-03); shipwrecks grant materials; natural wonders register for Overlook bonuses (M3-02).
5. **Coastline reveal:** a hook for the Great Lighthouse effect.

**Acceptance:** an expedition reveals its region on the right tick; each discovery effect applies exactly once; save round-trip mid-expedition.

---

### M2-07 · Traditions and slots

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-00 | `src/Evenmere.Sim/Systems/Culture/Traditions/**`, `src/Evenmere.Sim/Systems/Culture/CultureSystem.cs` (registration lines only), `tests/Evenmere.Sim.Tests/Culture/Traditions/**` |

**Reads:** culture-tree.md (§How culture works, §Traditions, §Slots, §Architectural styles).

**Goal:** tradition cards in typed slots, swappable at any time, whose effects apply through the modifier index.

**Steps:**

1. **State:** slots per type, from `traditionSlot` effects already recorded by M1-04, holding the slotted cards and the set of unlocked cards.
2. **`SetTradition(slotType, index, traditionId | null)`:**
   - Valid when the card is unlocked, its slot type matches (or the slot is Wild), and it isn't already slotted elsewhere.
   - Only one `style`-tagged card can be slotted at a time.
3. **Effects:** slotting registers the card's modifiers with the source `tradition:<id>`, and unslotting removes them. Emit `TraditionChanged`.
4. **Query:** `ICultureQueries` gains `Slots()`, `UnlockedTraditions()` and `ActiveStyle()`.

**Acceptance:**

- Swapping cards changes `BonusPercent` the next tick.
- The Wild-slot rules are tested.
- A second style card is rejected.

---

### M2-08 · Festivals

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-00 | `src/Evenmere.Sim/Systems/Culture/Festivals/**`, `src/Evenmere.Sim/Systems/Culture/CultureSystem.cs` (registration lines only), `tests/Evenmere.Sim.Tests/Culture/Festivals/**` |

**Reads:** culture-tree.md (§Festivals).

**Goal:** festivals that cost goods and give a burst of culture and a pause from decline.

**Steps:**

1. **`HoldFestival(groundId, festivalId)`** is valid when:
   - The festival is unlocked and the season matches.
   - No festival is running island-wide, and this type hasn't been held this year.
   - The goods are available, after the `festival_calendar` modifiers.
2. **Running:** the goods are taken at start. Each day, credit culture × the season multiplier, set the "festival running" flag that Population reads to pause decline timers, and apply the move-in and visitor multipliers.
3. **Events:** emit `FestivalStarted` and `FestivalEnded`, and add to the `festivalsHeld` counter.

**Acceptance:** each validation rule is tested; decline timers are paused during the festival and resume after it; culture credit is exact.

---

### M2-09 · Inspirations and the full condition vocabulary

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-00 | `src/Evenmere.Sim/Systems/Progression/Conditions/**`, `tests/Evenmere.Sim.Tests/Progression/Conditions/**` |

**Reads:** architecture.md (§Conditions vocabulary); tech-tree.md and culture-tree.md (the inspiration columns).

**Goal:** every condition used by Era I to Era IV inspirations, happenings and tutorial steps can be evaluated, cheaply and deterministically.

**Steps:**

1. Implement the remaining kinds: `consumed`, `discovered`, `expeditions`, `festivalsHeld`, `greatWorks` (reads a counter that stays at zero until M3-03), `visitorsPerDay`, `season`, `era`, buildings-on-terrain counts ("buildings on hills") and `regionsRevealed` by biome.
2. **Change-driven evaluation:** maintain a dirty set of counters, and re-evaluate only the conditions that depend on changed counters.
3. **Tooling:** add a test-only command that dumps the evaluation status of every node's inspiration.

**Acceptance:**

- Truth-table tests for each kind.
- With 300 conditions, daily evaluation takes ≤ 0.05 ms on average (benchmark test).

---

### M2-10 · Monuments: multi-stage construction

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-00 | `src/Evenmere.Sim/Systems/Construction/**`, `tests/Evenmere.Sim.Tests/Construction/Monuments/**` |

**Reads:** culture-tree.md (§Monuments and wonders); progression.md (stage bills).

**Goal:** monuments and wonders built in visible stages, each with its own bill, applying their effects on completion.

**Steps:**

1. **Stages:** a building with `stages` is a site whose bill switches per stage. Emit `MonumentStageCompleted(stage)`, and expose the stage for visuals.
2. **Effects:** completion applies the building's effects through `IProgressionService` (source `monument:<id>`). Uniqueness per island is enforced at placement.
3. **Material cost:** the `materialCost` modifier (for example `benefactors`) scales the stage bills.

**Acceptance:** stage-by-stage consumption is tested; effects apply once; demolishing a finished monument removes its effects and refunds 50% of the final stage.

---

### M2-11 · Era advancement and gateways

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-09 | `src/Evenmere.Sim/Systems/Progression/Eras/**`, `tests/Evenmere.Sim.Tests/Progression/Eras/**` |

**Reads:** ADR-0010 (in full).

**Goal:** an era begins exactly when both gateways are complete, and later-era nodes stay locked until then.

**Steps:**

1. **Era check:** after research and culture complete each day (or immediately on a node's completion), check the requirements of the next era. When both gateways are complete, set `CurrentEra` and emit `EraBegan`.
2. **Advisor hook:** expose `GatewayStatus()` (tech done?, civic done?) so advisors can say "Era II needs Village Moot (culture)".

**Acceptance:** completing only one gateway doesn't advance the era; completing both advances it the same tick; era-II nodes become available.

---

### M2-12 · Happenings

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-09 | `src/Evenmere.Sim/Systems/Happenings/**`, `tests/Evenmere.Sim.Tests/Happenings/**` |

**Reads:** architecture.md (§Happenings and advisors).

**Goal:** gentle, data-driven events with choices and no harsh outcomes.

**Steps:**

1. **Daily roll:** each day, eligible happenings (trigger condition true, cooldown elapsed, no other happening pending) are rolled with the system's PCG32 at their weight per day. At most one is pending at a time.
2. **Choosing:** `ChooseHappeningOption(happeningId, optionIndex)` applies the option's effects (`grantGoods`, temporary modifiers with a duration, `requestFestival` discounts). If the player doesn't choose, the happening times out after 10 days with its default option.
3. **Lint rule** (the gentleness check): no option's effects may be net-negative. They may cost goods only in exchange for a benefit.

**Acceptance:** determinism across a save round trip while a happening is pending; timeout applies the default; the gentleness lint fixture fails as intended.

---

### M2-13 · Era I full content

| Lane | Depends on | Owns |
| --- | --- | --- |
| content | M2-06, M2-07, M2-08, M2-10, M2-11, M2-12, M2-14 | `content/**` (Era I files, `content/eras/township.json`, `content/locale/en/era1-*.json`, `content/locale/en/names.json`), `game/data/visuals/era1-*.json` |

**Reads:** progression.md (Era I); tech-tree.md (Era I); culture-tree.md (Era I, traditions, festivals); architecture.md (§Content shapes).

**Goal:** the complete Founding era as data.

**Steps:**

1. **Goods and chains:** add the remaining Era I goods, buildings and recipes: stone, apples, cider, clay and pottery; the Quarry, Clay Pit, Potter's Kiln, Orchard and Cider Press; the Scout's Lodge, Timber Bridge, Scribe's Hall, Storyteller's Circle, Festival Ground, Stone Storehouse, Plaza, Cobbled Road and Standing Stones.
2. **Villagers' comfort needs:** cider and pottery.
3. **Techs:** all 13 Era I techs with inspirations.
   - `town_charter` is the tech gateway. In M2, its effects are limited to the gateway itself; `unlockTier townsfolk` and `town_hall` are added in M3-06, once Era II goods exist.
4. **Civics:** all 8 Era I civics, with the slot grants, the 8 Era I traditions, the Harvest Fair, and `village_moot` as the civic gateway.
5. **Era II definition:** `content/eras/township.json`, with the gateway requirements.
6. **Happenings:** 8–10 Era I happenings, for example a travelling scholar, a bountiful catch, a request for a Harvest Fair, a shipwreck salvage, a lost lamb, a storyteller's visit, a fair-weather season and a stone find.
7. **Visuals:** a mapping for every new building.

**Acceptance:**

- Lint is clean, including EVL010.
- The tables in progression.md and the tree docs match the content, and every deviation is listed in the PR with a docs update.

---

### M2-14 · Lint rule EVL010: lower-tier staffing

| Lane | Depends on | Owns |
| --- | --- | --- |
| content-infra | M2-00 | `src/Evenmere.Content/Lint/**`, `tests/Evenmere.Content.Tests/Lint/**`, `tests/fixtures/content/lint/EVL010-*/**` |

**Reads:** progression.md (§How needs work: the design rule); ADR-0010.

**Goal:** a tier's basic goods and services are always made and staffed by lower tiers.

**Steps:**

1. For each tier T, find every building that produces T's basic goods, transitively through recipe inputs, plus every building that provides T's basic services.
2. Each of those buildings must declare a workforce tier lower than T. Settlers are exempt, as the first tier.
3. Report the offending building and the chain that led to it.

**Acceptance:** a failing fixture reports the right building and chain; the M1 content passes.

---

### M2-15 · Save versioning, migrations and autosave rotation

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M2-00 | `src/Evenmere.Sim/Persistence/**`, `tests/Evenmere.Sim.Tests/Persistence/**`, `tests/fixtures/saves/**`, `game/host/Persistence/**` |

**Reads:** ADR-0007.

**Goal:** saves from every past version load, and autosaves never lose more than a few minutes.

**Steps:**

1. **Migration framework:** `Migration_000N_to_000M` classes, a registry, and a test that loads every fixture in `tests/fixtures/saves/` and steps it to the current version.
2. **Format version 2:** bump to version 2 for the M2 state additions (seasons, exploration, traditions, festivals, monuments, era, happenings), with a migration from version 1.
3. **Autosave rotation** in the game host: 3 rotating slots every 10 game days, plus an autosave before demolishing more than 10 buildings at once.

**Acceptance:** the v1 fixture migrates and continues deterministically; the autosave slots rotate.

---

### M2-16 · Replay and performance harness, reference saves

| Lane | Depends on | Owns |
| --- | --- | --- |
| tools | M2-15 | `src/Evenmere.Tools/Perf/**`, `src/Evenmere.Tools/Commands/PerfCommand.cs`, `src/Evenmere.Tools/Commands/ReplayCommand.cs`, `tests/fixtures/saves/perf-*.evsave`, `.github/workflows/nightly.yml` (perf job only) |

**Reads:** architecture.md (§Performance budgets).

**Goal:** measure tick cost on realistic saves, every night.

**Steps:**

1. **`evenmere perf --save <file> --ticks 2000`:** reports the average, p50, p99 and maximum per system against the budget table. It exits non-zero above budget × 1.25 when `--enforce` is set.
2. **`evenmere replay --save <file> --log <file> --until <tick>`:** prints the hash.
3. **Reference saves:** build `perf-2k` (about 2,000 residents) with the bot. `perf-20k` is a synthetic stress save (dense placement through a generator script) and is marked provisional until M4.
4. **Nightly:** add perf runs of both saves to the nightly workflow, uploading the reports.

**Acceptance:** perf reports are attached to the PR; replaying the slice log matches its golden hash.

---

### M2-17 · Balance bot v2: generated islands to Era II

| Lane | Depends on | Owns |
| --- | --- | --- |
| tools | M2-13, M2-16 | `src/Evenmere.Tools/Balance/**`, `tools/bots/**`, `.github/workflows/nightly.yml` (balance job only) |

**Reads:** progression.md (§Pacing targets); ADR-0010.

**Goal:** the bot plays any seed through Era I to both gateways and reports pacing.

**Steps:**

1. **Era I bot script:** the full era, including expeditions, bridges, traditions (a heuristic slotting priority list), festivals, the Standing Stones, and research and civic queues that follow the gateway paths.
2. **Generated islands:** the placement search handles rivers and bridges and avoids fog.
3. **Multi-seed:** `--seeds 1..5` runs the seeds in sequence and produces a summary table (time to each milestone in §Pacing targets, stalls, final hashes).
4. **Nightly:** the nightly job runs 5 seeds plus the starter island.

**Acceptance:** the Era II timing on all 6 maps falls within 90–120 min, or the PR includes content tuning (as data changes) with a rationale.

---

### M2-18 · Tech tree graph view

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-09 | `game/ui/research/**` |

**Goal:** a readable, navigable tech tree.

**Steps:**

1. **Layout:** columns by era, layered by prerequisite depth, with edges drawn as curves. The gateway is highlighted.
2. **Nodes:** state (done, available, queued, locked), cost and progress, the inspiration indicator with its condition text, and an unlock list with icons.
3. **Interaction:** click to set the target, shift-click to queue, pan and zoom, and full gamepad navigation (focus follows edges).

**Acceptance:** screenshots at 1280 × 800 and 2560 × 1600; gamepad navigation works across all of Era I.

---

### M2-19 · Civics tree and traditions board

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-07 | `game/ui/culture/**` |

**Goal:** the culture tree, plus a board for slotting tradition cards.

**Steps:**

1. **Civics tree:** the same component as M2-18, with civic data.
2. **Traditions board:** slots grouped by type; owned cards in a drawer; drag-and-drop, or a gamepad picker; effect text from locale keys; style cards preview their style name.
3. **Feedback:** after a swap, show the changed bonuses in a calm summary (for example "Farms +20% (autumn)").

**Acceptance:** screenshots, and a swap reflected in the inspector's productivity breakdown.

---

### M2-20 · Overlays: coverage, reach, fertility and deposits

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-00 | `game/rendering/overlays/**`, `src/Evenmere.Sim/Queries/IOverlayQueries.cs` (new, per the M2-00 refinement) and its implementation folder `src/Evenmere.Sim/Systems/Overlays/**` |

**Goal:** make invisible rules visible.

**Steps:**

1. **Sim side:** an `IOverlayQueries` that produces per-tile byte layers for storehouse reach, market and service coverage, fertility per crop, deposits and buildable-with-access. The layers are computed on demand and cached by version.
2. **Game side:** shader overlays with a legend. They show automatically during placement for the relevant building (for example fertility when placing a farm).

**Acceptance:** the overlays are correct on fixture maps (sim tests); screenshots of each overlay.

---

### M2-21 · Production chain viewer

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-00 | `game/ui/chains/**` |

**Goal:** a panel that shows any chain as a graph, with live rates and bottlenecks.

**Steps:**

1. **Graph:** pick a good, and the chain graph is built from `ContentDb` recipes.
2. **Live data:** annotate each node with the building count, average productivity and produced/consumed per day from `IStatisticsQueries`.
3. **Bottlenecks:** highlight the bottleneck from `ExplainGood`. Clicking a node lists its buildings and focuses the worst one.

**Acceptance:** screenshot of the bread chain with a starved windmill highlighted.

---

### M2-22 · Statistics graphs

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-00 | `game/ui/stats/**` |

**Goal:** calm graphs of how the island is doing.

**Steps:**

1. **Line charts:** population per tier, goods stock and net per day, and knowledge and culture per day.
2. **Windows:** 40 days or 2 years; hover shows exact values.
3. **Accessibility:** a colour-blind-safe palette from the theme.

**Acceptance:** screenshots, and values matching `IStatisticsQueries` (a debug label).

---

### M2-23 · Fog, regions and expedition UI

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-06 | `game/rendering/fog/**`, `game/ui/exploration/**` |

**Goal:** see what is unknown, and send scouts.

**Steps:**

1. **Fog:** a soft volumetric-looking shader over unrevealed regions, with region outlines on hover.
2. **Expedition picker:** from a Scout's Lodge, pick an adjacent fogged region. It shows the ETA and confirms with `StartExpedition`.
3. **Reveals:** a reveal animation, and discovery cards with their effects.

**Acceptance:** a screenshot sequence of an expedition from start to reveal.

---

### M2-24 · Seasons, weather and smoke visuals

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-05 | `game/rendering/seasons/**`, `game/rendering/effects/**` |

**Goal:** the year is visible, and the city breathes.

**Steps:**

1. **Seasonal palette:** palette shifts per season (foliage, fields, light colour), blended over two game days.
2. **Seasonal effects:** a light snow tint in winter and blossom particles in spring, both very subtle.
3. **Smoke:** chimney smoke from active production buildings, as GPU particles budgeted by zoom.

**Acceptance:** screenshots of all four seasons, and the fps budget held.

---

### M2-25 · New-game setup: seed, size, starter island

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-02 | `game/ui/menus/NewGame/**` |

**Goal:** choose a starter or generated island.

**Steps:**

1. **New-game screen:** a choice between the starter island and a generated island, with a seed field (random by default), size (256, 320 or 384) and a preview thumbnail rendered from `MapDef`.
2. **Start:** starts the game through `NewGameOptions`.

**Acceptance:** screenshots, and the same seed always gives the same preview.

---

### M2-26 · Debug overlay and cheat console

| Lane | Depends on | Owns |
| --- | --- | --- |
| game + sim | M2-00 | `game/ui/debug/**`, `src/Evenmere.Sim/Systems/Debug/**`, `docs/adr/0012-cheat-commands.md` |

**Goal:** tools for testers that cannot leak into normal play.

**Steps:**

1. **ADR-0012:** cheat commands are processed only when `WorldState.CheatsEnabled` is set. The flag is set at new-game time in debug builds or with a launch flag, is stored in saves, and is visible in the save header.
2. **Commands:** `CheatGrantGoods`, `CheatCompleteNode`, `CheatSetEra`, `CheatRevealAll` and `CheatAddResidents`.
3. **Overlay** (F3): fps, tick time per system (from the profiler), entity counts, the selected entity's raw state, and the hash.
4. **Console** (backtick): parses and enqueues cheat commands.

**Acceptance:** cheats are rejected when the flag is off (test); screenshots of the overlay.

---

### M2-27 · Festivals, monuments and happenings UI

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M2-08, M2-10, M2-12 | `game/ui/events/**`, `game/ui/monuments/**`, `game/rendering/monuments/**` |

**Goal:** the culture moments feel special.

**Steps:**

1. **Festival panel:** opened from a Festival Ground. Shows the available festivals, their costs and effects, and why one can't be held (season, yearly cooldown, goods). Holds the festival. While it runs, bunting and lantern visuals appear and a quiet HUD badge shows.
2. **Monument inspector:** stage progress and the bill for each stage. The visuals grow by stage.
3. **Happening dialog:** a calm card with an illustration slot, the options with effect previews, and a "decide later" button that keeps it pending.

**Acceptance:** screenshots of each.

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
