# M3 — Era II, coin and culture depth

**Goal:** the whole Township era:

- **Coinage** introduces gentle coin.
- Visitors and attractiveness, great works and the first wonders.
- Drainage, terracing and stone bridges open new ground.
- Townsfolk (from the tech tree) and Artisans (from the culture tree).
- Architectural styles visibly restyle the town.

**Gate:**

- [ ] **Bot pacing:** balance bot v3 reaches Era III (both gateways) in 5–6 game hours on 5 seeds, with no stall longer than one season. The treasury never sits at zero for more than one season.
- [ ] **Migrations:** every M2 save fixture migrates and continues deterministically.
- [ ] **Lint:** EVL009 (no Era I upkeep) passes.
- [ ] **Performance:** the `perf-5k` reference save stays within budget × 1.0 on the development Mac.
- [ ] **Playtest:** the developer plays from Era I into Era III. Coin feels gentle, and styles and great works feel rewarding.

**Waves:**

| Wave | Tasks |
| --- | --- |
| 0 | M3-00 |
| A | M3-01, M3-02, M3-03, M3-04, M3-08 |
| B | M3-05, M3-06, M3-07, M3-15 |
| C | M3-09, M3-10, M3-11, M3-12, M3-13, M3-14 |
| D | M3-16 |
| E | M3-17 |

## Task index

| ID | Title | Lane | Depends on | Status |
| --- | --- | --- | --- | --- |
| M3-00 | Kickoff: refine M3 cards against the code | sim | M2 gate | todo |
| M3-01 | Coin economy and lint rule EVL009 | sim | M3-00 | todo |
| M3-02 | Attractiveness and visitors | sim | M3-00 | todo |
| M3-03 | Great works | sim | M3-00 | todo |
| M3-04 | Drainage and hill farming | sim | M3-00 | todo |
| M3-05 | Era II content A: goods, chains and buildings | content | M3-01, M3-04 | todo |
| M3-06 | Era II content B: tiers, techs, civics, traditions, festivals and monuments | content | M3-05, M3-02, M3-03 | todo |
| M3-07 | Era II content C: happenings, advisor texts and locale polish | content | M3-06 | todo |
| M3-08 | Architectural styles (sim and visuals mapping) | sim | M3-00 | todo |
| M3-09 | Coin HUD, ledger panel and upkeep explanations | game | M3-01 | todo |
| M3-10 | Attractiveness overlay and visitor arrivals | game | M3-02 | todo |
| M3-11 | Great-works gallery and slot UI | game | M3-03 | todo |
| M3-12 | Terrain transform and mine visuals | game | M3-04 | todo |
| M3-13 | Wonder staging visuals | game | M3-06 | todo |
| M3-14 | Style variant meshes and gradual restyling | game | M3-08 | todo |
| M3-15 | Save migration fixtures M2 → M3 | sim | M3-01, M3-02, M3-03, M3-04, M3-08 | todo |
| M3-16 | Balance bot v3: Era II and coin | tools | M3-07 | todo |
| M3-17 | Era II playtest and gate review | human | all M3 tasks | todo |

---

### M3-00 · Kickoff: refine M3 cards against the code

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim (planning) | M2 gate | `docs/implementation/M3-era-two.md` |

**Goal:** sharpen every M3 card (files, interfaces, events, fixtures) against the code after M2, and fold in the M2 gate findings.

**Acceptance:** the orchestrator approves the revised cards, and any ADRs they need are merged first.

---

### M3-01 · Coin economy and lint rule EVL009

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M3-00 | `src/Evenmere.Sim/Systems/Economy/**`, `tests/Evenmere.Sim.Tests/Economy/**`, `src/Evenmere.Content/Lint/Rules/Evl009*.cs`, `tests/fixtures/content/lint/EVL009-*/**` |

**Reads:** ADR-0009 (in full); architecture.md (§Coin); progression.md (tier coin outputs).

**Goal:** coin arrives with Coinage, and it stays gentle.

**Steps:**

1. **Treasury:** an `Amount`, active only once the `coin` feature is unlocked. Before that, the economy system does nothing.
2. **Income** (daily, bucketed): taxes per house = tier coin per 1,000 residents × (share of basic needs met), with comfort and `output.coin` bonuses. Collection needs at least one Counting House, and each Counting House adds +10%.
3. **Visitor spending:** 1 coin per visitor per day, read from M3-02 through `IPopulationQueries.VisitorsToday`.
4. **Upkeep:** each building with `upkeepCoin` pays it daily. If the treasury can't cover a building's upkeep, that building becomes `Underfunded` (50% productivity, a new stall reason) until it's paid. Buildings are paid in ID order each day, so the outcome is deterministic.
5. **Coin costs:** tier upgrade costs and stage costs that include `coin` draw from the treasury.
6. **Lint EVL009:** Era I buildings must not declare `upkeepCoin`.
7. **Queries and explanations:** `IEconomyQueries` exposes `Treasury`, `IncomeYesterday` (broken down by source) and `UpkeepYesterday` (broken down by category). Add explanation entries for `Underfunded`.

**Acceptance:**

- No coin effects before Coinage.
- Underfunding never drives the treasury negative and recovers automatically.
- The tax maths matches the per-1,000 rates.
- The EVL009 fixture fails as intended.

---

### M3-02 · Attractiveness and visitors

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M3-00 | `src/Evenmere.Sim/Systems/Culture/Attractiveness/**`, `src/Evenmere.Sim/Systems/Population/Visitors/**`, `tests/Evenmere.Sim.Tests/Attractiveness/**` |

**Reads:** culture-tree.md (§Attractiveness and visitors); progression.md (decoration values).

**Goal:** a beautiful town draws people. Before Coinage they settle; after it they spend.

**Steps:**

1. **Attractiveness:** island attractiveness = Σ building `attractiveness` values (positive and negative), plus displayed great works, plus natural wonders with an Overlook, all with modifiers applied. Recompute it on change, not every tick.
2. **Move-in rate:** before Hospitality, +1% per point, capped at +50%.
3. **Visitors:** with the `visitors` feature, visitors per day = min(lodging capacity, 2 × attractiveness) × modifiers. Before Coinage, every 20 visitors add 1 settler arrival per day.
4. **Overlooks:** an Overlook placed beside a natural wonder activates its bonus.

**Acceptance:** tests for each formula; an industry penalty lowers attractiveness and Green Lungs halves the penalty (with a fixture modifier).

---

### M3-03 · Great works

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M3-00 | `src/Evenmere.Sim/Systems/Culture/GreatWorks/**`, `tests/Evenmere.Sim.Tests/Culture/GreatWorks/**` |

**Reads:** culture-tree.md (§Great works).

**Goal:** named works created by residents, displayed in slots, yielding culture.

**Steps:**

1. **Creativity:** daily, from creators (Storyteller's Circles, and Artisans, Scholars and Patrons per 1,000 residents), accrued only for unlocked work types (the `great_works:<type>` feature).
2. **Creation:** at the threshold (100, then 25% more for each later work), and only if a free slot of the matching type exists, create a `GreatWork` with an ID, type, era, creator kind and a deterministic name from the name tables. Place it in the free slot with the lowest ID, then emit `GreatWorkCreated`.
3. **`MoveGreatWork(workId, buildingId, slotIndex)`:** swaps the works when the target slot is occupied.
4. **Yield:** each displayed work gives culture by its era (2, 4, 6 or 8 per day) and +1 attractiveness. If its building is demolished, the work moves to free storage until a slot frees up.
5. **Relics:** ruins discovered by expeditions create Relic works.

**Acceptance:** creation happens only when a slot exists; names are deterministic; demolishing a building keeps its works; save round trip.

---

### M3-04 · Drainage and hill farming

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M3-00 | `src/Evenmere.Sim/Systems/Map/Transforms/**`, `src/Evenmere.Sim/Systems/Construction/Placement/TerrainAccessRule.cs`, `tests/Evenmere.Sim.Tests/Transforms/**` |

**Reads:** progression.md (§Terrain and access ladder); tech-tree.md (Terracing, Drainage).

**Goal:** technology reshapes the island. Marsh becomes fen, and hills take farms.

**Steps:**

1. **`ApplyTerrainTool(tool, rect)`:** the `drainage_ditch` tool converts marsh tiles in the rectangle to `fen` (a buildable terrain with its fertility layers kept), costs 1 stone per tile, and needs the `marsh_drainage` access. It runs as a construction site over days, then marks chunks dirty and emits `TerrainTransformed`.
2. **Hill farming:** buildings tagged `farm` may be placed on hills when `hill_farming` is unlocked.
3. **Stone bridges:** these need content only, because M2-04 already supports span limits by type.

**Acceptance:** drained tiles accept fen farms; hill farms are rejected before Terracing and accepted after; the transform survives a save round trip.

---

### M3-05 · Era II content A: goods, chains and buildings

| Lane | Depends on | Owns |
| --- | --- | --- |
| content | M3-01, M3-04 | `content/goods/*`, `content/buildings/*`, `content/recipes/*` (Era II items), `content/locale/en/era2-production.json`, `game/data/visuals/era2-production.json` |

**Reads:** progression.md (Era II tables and chains).

**Goal:** every Era II good, chain and building as data, with upkeep values.

**Steps:**

1. Add the 16 goods and the production buildings and recipes from the Era II tables.
2. Add the infrastructure, service and culture buildings: Town Hall, Counting House, Tavern, School, Library, Stone Bridge, Drainage Ditch, Guildhall, Inn, Overlook, Market Square, Park, Fountain, the Belfry and the Great Lighthouse.
3. Set `upkeepCoin` from 1 to 6 by building class.
4. Add visual mappings for every new building.

**Acceptance:** lint is clean, including EVL004, EVL009 and EVL010.

---

### M3-06 · Era II content B: tiers, techs, civics, traditions, festivals and monuments

| Lane | Depends on | Owns |
| --- | --- | --- |
| content | M3-05, M3-02, M3-03 | `content/tiers/*`, `content/techs/*`, `content/civics/*`, `content/traditions/*`, `content/festivals/*`, `content/greatworks/*` (Era II items), `content/eras/enlightenment.json`, `content/techs/town_charter.json` (add effects), `content/locale/en/era2-progression.json` |

**Reads:** tech-tree.md (Era II); culture-tree.md (Era II); progression.md (Townsfolk and Artisans).

**Goal:** both Era II trees with their tiers, traditions, festivals and great-work types.

**Steps:**

1. **Tiers:** Townsfolk and Artisans.
2. **Town Charter:** add `unlockTier townsfolk` and `town_hall` to its effects.
3. **Trees:** 15 techs, with `natural_philosophy` as the gateway, and 9 civics, with `humanism` as the gateway.
4. **Traditions:** the 8 Era II cards, including both style cards.
5. **Festivals:** Midsummer Fair and Winter Lights.
6. **Great works:** the Writing (Folk Epic) and Art types.
7. **Era III definition:** `content/eras/enlightenment.json`, with its gateway requirements.

**Acceptance:** lint is clean; the tree docs and the content agree, or the docs are updated in the same PR.

---

### M3-07 · Era II content C: happenings, advisor texts and locale polish

| Lane | Depends on | Owns |
| --- | --- | --- |
| content | M3-06 | `content/happenings/*` (Era II), `content/locale/en/era2-happenings.json`, `content/locale/en/advisors.json`, `content/locale/en/names.json` |

**Goal:** Era II feels alive and explains itself.

**Steps:**

1. Add 10–12 Era II happenings, for example a merchant's request, a guild dispute settled kindly, a lighthouse keeper's tale and a market-day crowd.
2. Write the advisor texts for coin (introducing taxes and upkeep, `Underfunded`), visitors and the gateways.
3. Extend the name tables for tapestries, paintings and natural wonders.

**Acceptance:** lint is clean, and the gentleness lint passes.

---

### M3-08 · Architectural styles (sim and visuals mapping)

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M3-00 | `src/Evenmere.Sim/Systems/Culture/Styles/**`, `tests/Evenmere.Sim.Tests/Culture/Styles/**`, `game/data/visuals/styles.json` |

**Reads:** culture-tree.md (§Architectural styles).

**Goal:** the active style card restyles houses and civic buildings gradually and deterministically.

**Steps:**

1. **Style state:** the target style (from the slotted style card, or the era default) and a per-building applied style.
2. **Gradual restyling:** each day, up to 2% of eligible buildings (at least 5), in ID order, adopt the target style and are marked dirty for their chunks.
3. **Visuals mapping:** `styles.json` maps each style to the mesh variant set name per shape.

**Acceptance:** swapping styles converges in at most 50 days; the order is deterministic; save round trip.

---

### M3-09 · Coin HUD, ledger panel and upkeep explanations

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M3-01 | `game/ui/economy/**`, `game/ui/hud/CoinWidget*` |

**Goal:** coin is legible and never alarming.

**Steps:**

1. **HUD widget:** the treasury, with net per day. A tooltip shows income and upkeep by source.
2. **Ledger panel:** 40-day graphs of income and upkeep by category, and a list of underfunded buildings with their explanations.
3. **Introduction:** a one-time explanatory card when Coinage completes.

**Acceptance:** screenshots, including an `Underfunded` explanation.

---

### M3-10 · Attractiveness overlay and visitor arrivals

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M3-02 | `game/rendering/overlays/Attractiveness*`, `game/rendering/life/Visitors*`, `game/ui/hud/AttractivenessWidget*` |

**Goal:** see beauty and its effects.

**Steps:**

1. **Overlay:** per-building contributions shown as heat and icons.
2. **Visitors:** visitor walkers arrive from Inns, and from Harbours in M5.
3. **HUD:** a widget for attractiveness and visitors per day.

**Acceptance:** screenshots.

---

### M3-11 · Great-works gallery and slot UI

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M3-03 | `game/ui/greatworks/**` |

**Goal:** works are celebrated and easy to arrange.

**Steps:**

1. **Gallery:** all works with their name, type, era, creator and where they're displayed.
2. **Slot view:** a slot view per building in its inspector. Drag a work to move it (`MoveGreatWork`).
3. **Notification:** a gentle "A new work has been created" notice, with its name.

**Acceptance:** screenshots, and a move reflected in the culture per day.

---

### M3-12 · Terrain transform and mine visuals

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M3-04 | `game/rendering/terrain/Transforms/**`, `game/ui/tools/TerrainTool*` |

**Goal:** draining marshes and terracing hills look satisfying.

**Steps:**

1. **Drainage tool:** a rectangle drag with a preview of the tiles and cost.
2. **Visuals:** fen tiles render with ditch lines; terraced hill farms render with steps; mines show spoil heaps.

**Acceptance:** screenshots before and after.

---

### M3-13 · Wonder staging visuals

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M3-06 | `game/rendering/monuments/**` |

**Goal:** wonders grow visibly, stage by stage.

**Steps:**

1. **Stage visuals:** per-stage placeholder meshes for the Belfry and the Great Lighthouse.
2. **Lighthouse:** a rotating beam at night or in dark weather (presentation only).
3. **Completion:** a calm completion moment with a camera suggestion and a notification.

**Acceptance:** screenshots of each stage.

---

### M3-14 · Style variant meshes and gradual restyling

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M3-08 | `game/rendering/buildings/Styles/**` |

**Goal:** the town reads as Timber-frame or Island Stone at a glance.

**Steps:**

1. **Variants:** placeholder variant sets per style (roof pitch, wall colour, trim, chimney), produced by `PlaceholderMeshFactory` parameters.
2. **Restyling:** buildings restyle when their applied style changes, chunk by chunk.

**Acceptance:** screenshots of the same town in each style.

---

### M3-15 · Save migration fixtures M2 → M3

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M3-01, M3-02, M3-03, M3-04, M3-08 | `src/Evenmere.Sim/Persistence/Migrations/**`, `tests/fixtures/saves/**` |

**Goal:** M2 saves continue in M3.

**Steps:**

1. Bump the format version, and add the migration that initialises the treasury, attractiveness, great works and styles.
2. Commit the v2 fixtures (the starter island and a generated seed in late Era I) and test that they continue deterministically.

**Acceptance:** the fixtures load and continue, and the golden hashes are recorded.

---

### M3-16 · Balance bot v3: Era II and coin

| Lane | Depends on | Owns |
| --- | --- | --- |
| tools | M3-07 | `src/Evenmere.Tools/Balance/**`, `tools/bots/**` |

**Goal:** the bot reaches Era III on 5 seeds within the pacing targets.

**Steps:**

1. **Era II script:** a build order covering bricks and tools, Townsfolk, Artisans, coin (Counting House timing), festivals, great-work placement, styles, drainage and terracing.
2. **Report:** treasury health (days at zero, underfunded building-days) and the attractiveness curve.

**Acceptance:** the nightly summary meets the gate, or the PR includes data tuning with its rationale.

---

### M3-17 · Era II playtest and gate review

| Lane | Depends on | Owns |
| --- | --- | --- |
| human | all M3 tasks | `docs/implementation/M3-era-two.md` (§Gate record) |

**Goal:** confirm that the Township era is complete, that coin feels gentle, and that culture feels rewarding.

**Acceptance:** all gate boxes are ticked or explicitly waived, and the findings are triaged into M4 cards.

---

## Gate record

_Filled in when M3 closes._
