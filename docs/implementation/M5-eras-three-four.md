# M5 — Eras III–IV and the Windows port

**Goal:** complete the game:

- **Enlightenment:** Scholars, highlands, the harbour, glass, books and three wonders.
- **Industry:** Patrons, steam, railways, gaslight, reefs and three more wonders.
- **Endgame:** repeatable nodes.
- **Windows:** the last platform, ported.

Both eras may ship as free updates after the public build.

**Gate:**

- [ ] **Bot pacing:** balance bot v4 reaches Era III in 5–6 h, Era IV in 10–12 h and completes both trees in 18–20 h on 5 seeds.
- [ ] **Migrations:** every M4 save fixture migrates and continues.
- [ ] **Performance:** the `perf-20k` save, rebuilt with Era IV content (rail, steam), stays within budget.
- [ ] **Windows:** the build passes the smoke test and the QA checklist, and CI exports all three platforms.
- [ ] **Playtest:** a full-game playtest is signed off.

## Task index

| ID | Title | Lane | Depends on | Status |
| --- | --- | --- | --- | --- |
| M5-00 | Kickoff: refine M5 cards against the code | sim | M4 gate | todo |
| M5-01 | Highlands and deep-water access; generator tuning | sim | M5-00 | todo |
| M5-02 | Harbour, visitor ships and the deep-water fishery | sim + game | M5-01 | todo |
| M5-03 | Era III content | content | M5-01, M5-02 | todo |
| M5-04 | Steam upgrades (fuel and building variants) | sim + game | M5-00 | todo |
| M5-05 | Railways | sim + game | M5-04 | todo |
| M5-06 | Gaslight and the presentation day/night cycle | sim + game | M5-00 | todo |
| M5-07 | Reefs and the Diving Bell | sim + game | M5-01 | todo |
| M5-08 | Era IV content | content | M5-03 to M5-07 | todo |
| M5-09 | Repeatable nodes and the endgame | sim + content | M5-08 | todo |
| M5-10 | Balance bot v4: full trees | tools | M5-09 | todo |
| M5-11 | Windows export, input and CI job | infra | M5-00 | todo |
| M5-12 | Windows QA pass | human + game | M5-11 | todo |
| M5-13 | Full-game playtest and gate review | human | all M5 tasks | todo |

---

### M5-00 · Kickoff: refine M5 cards against the code

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim (planning) | M4 gate | `docs/implementation/M5-eras-three-four.md` |

**Goal:** sharpen the cards, split the content tasks if they are too large, and propose ADRs for rail as a transport layer and for the presentation day/night cycle.

---

### M5-01 · Highlands and deep-water access; generator tuning

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M5-00 | `src/Evenmere.Sim/Systems/Map/Generation/**`, `src/Evenmere.Sim/Systems/Construction/Placement/TerrainAccessRule.cs`, `tests/Evenmere.Sim.Tests/MapGeneration/**` |

**Goal:** the highlands and deep water become usable through technology.

**Steps:**

1. **Highlands:** placement on highlands needs `highlands` access, and roads on highlands cost 1,300 per tile.
2. **Generator:** guarantee tea-fertile terraces and highlands, coal seams, copper and reefs on every seed.
3. **Validation:** extend the generator validation and the golden hashes.

**Acceptance:** 100 seeds meet the new guarantees.

---

### M5-02 · Harbour, visitor ships and the deep-water fishery

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim + game | M5-01 | `src/Evenmere.Sim/Systems/Population/Visitors/**`, `game/rendering/life/Ships*` |

**Goal:** the harbour lodges visitors and works as a storehouse, and ships animate the coast.

**Steps:**

1. **Harbour building:** behaves as a storehouse and provides visitor lodging.
2. **Deep-water fishery:** placement on deep water.
3. **Visitor ships:** presentation only, timed to the arrival of visitors.

**Acceptance:** tests and screenshots.

---

### M5-03 · Era III content

| Lane | Depends on | Owns |
| --- | --- | --- |
| content | M5-01, M5-02 | `content/**` (Era III files), `content/locale/en/era3-*.json`, `game/data/visuals/era3-*.json` |

**Reads:** progression.md, tech-tree.md and culture-tree.md (Era III).

**Goal:** the complete Enlightenment era as data.

**Carried over from M1-04:** building `aura` specs (the Bathhouse) are parsed but not evaluated by any system. Implement aura evaluation (coverage along roads, like services) before or with this content.

**Steps:**

1. Add 8 goods and 5 chains.
2. Add the Era III buildings and three wonders (Great Library, Star Tower and Botanical Garden).
3. Add the Scholars tier, 12 techs, 7 civics, the Era III traditions and the Regatta.
4. Add the Science and Writing great-work types and 10 happenings.
5. Add `content/eras/industry.json`.

**Acceptance:** lint is clean (EVL001–EVL010).

---

### M5-04 · Steam upgrades (fuel and building variants)

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim + game | M5-00 | `src/Evenmere.Sim/Systems/Production/Fuel/**`, `game/rendering/effects/Steam*` |

**Goal:** steam makes production faster, at the cost of coal and beauty.

**Steps:**

1. **Fuel:** a building's `fuel` is consumed per day from the pool. Without fuel, the steam bonus is lost but the building keeps working at base rate. This is gentle by design.
2. **Steam variants:** in-place upgrade variants, reusing `UpgradeBuilding`.
3. **Visuals:** smoke and steam effects.

**Acceptance:** tests for fuel shortfall behaviour, and screenshots.

---

### M5-05 · Railways

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim + game | M5-04 | `src/Evenmere.Sim/Systems/Roads/**`, `src/Evenmere.Sim/Systems/Logistics/**`, `game/rendering/roads/Rail*`, `game/rendering/life/Trains*` |

**Reads:** ADR-0005; the rail ADR from M5-00.

**Goal:** rail as a fast transport layer that fits the existing distance-field model.

**Steps:**

1. **Rail tiles:** a road type with cost 300 that can't be built on for houses. Stations connect rail to roads.
2. **Fields and jobs:** distance fields include rail through stations. A Railway Station acts as a storehouse with reach along rail. Jobs use the combined cost.
3. **Trains:** visual replay of long jobs.

**Acceptance:** field correctness tests and a perf check on `perf-20k`.

---

### M5-06 · Gaslight and the presentation day/night cycle

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim + game | M5-00 | `src/Evenmere.Sim/Systems/Services/Gas/**`, `game/rendering/lighting/**` |

**Goal:** the industrial town glows at night.

**Steps:**

1. **Gas coverage:** Gasworks coverage lights Gas Lamps, which grant attractiveness only inside coverage.
2. **Day/night:** a presentation-only cycle, following the M5-00 ADR (proposal: one visual day per season), with lit windows and lamps.

**Acceptance:** tests and screenshots of day and night.

---

### M5-07 · Reefs and the Diving Bell

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim + game | M5-01 | `src/Evenmere.Sim/Systems/Construction/Placement/ReefRule.cs`, `game/rendering/water/Reef*` |

**Goal:** the reefs, the last of the island's ground, open up.

**Steps:**

1. **Pearl Divers' Huts:** placed on coast tiles adjacent to a reef, with `reefs` access. Yield comes from reef tiles within radius 4.
2. **Visuals:** reef visuals and diving boats.

**Acceptance:** tests and screenshots.

---

### M5-08 · Era IV content

| Lane | Depends on | Owns |
| --- | --- | --- |
| content | M5-03 to M5-07 | `content/**` (Era IV files), `content/locale/en/era4-*.json`, `game/data/visuals/era4-*.json` |

**Goal:** the complete Industry era as data.

**Steps:**

1. Add 10 goods and 4 chains, plus coal.
2. Add the Era IV buildings, the steam variants, rail, gas, and three wonders and monuments (Railway Terminus, Grand Opera and Crystal Pavilion).
3. Add the Patrons tier, 10 techs, 7 civics, the Era IV traditions, the Promenade Concert and the Exhibition Fair.
4. Add the Music great-work type and 10 happenings.

**Acceptance:** lint is clean.

---

### M5-09 · Repeatable nodes and the endgame

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim + content | M5-08 | `src/Evenmere.Sim/Systems/Research/Repeatable*`, `src/Evenmere.Sim/Systems/Culture/Repeatable*`, `content/techs/scientific_societies.json`, `content/civics/living_heritage.json` |

**Goal:** a calm, open endgame.

**Carried over from M1-04:** effects are idempotent per `EffectSource`, so a repeatable node currently applies only once. Add a level to `EffectSource` (or key repeatable applications by level) with an ADR-0011 addendum.

**Steps:**

1. **Repeatable nodes:** costs grow by `costGrowthPercent` per level, and each level stacks its effects as a separate modifier source.
2. **Endgame happenings:** "the island remembers", a summary of the island's history (this needs a short design note in the PR).
3. **Sandbox:** the game continues indefinitely.

**Acceptance:** tests for cost growth and stacking.

---

### M5-10 · Balance bot v4: full trees

| Lane | Depends on | Owns |
| --- | --- | --- |
| tools | M5-09 | `src/Evenmere.Tools/Balance/**`, `tools/bots/**` |

**Goal:** the full game is paced to the targets.

**Steps:**

1. **Bot scripts:** cover Eras III–IV.
2. **Nightly runs:** full games on 5 seeds, nightly or weekly as the run time requires.

**Acceptance:** the summary meets the gate, or data tuning is included with its rationale.

---

### M5-11 · Windows export, input and CI job

| Lane | Depends on | Owns |
| --- | --- | --- |
| infra | M5-00 | `scripts/export-windows.ps1`, `game/export_presets.cfg` (the Windows preset), `.github/workflows/ci.yml` (add `windows-latest` to the matrix), `.github/workflows/release.yml` (Windows job) |

**Reads:** ADR-0008.

**Goal:** Windows joins as the last platform.

**Steps:**

1. **CI matrix:** add `windows-latest` to `verify`, so that `verify.sh` runs under Git Bash or gets a PowerShell twin.
2. **Export:** a Windows export, with optional code signing (the developer decides).
3. **Platform quirks:** fix path casing, DPI and focus issues found by CI.

**Acceptance:** CI is green on three operating systems, and the release artifact launches on Windows 11.

---

### M5-12 · Windows QA pass

| Lane | Depends on | Owns |
| --- | --- | --- |
| human + game | M5-11 | `game/**` (Windows fixes only) |

**Goal:** Windows is as solid as the other platforms.

**Steps:** run the QA checklist (install, play Eras I–II, save and load, cloud saves, controller, alt-tab, multiple monitors, high DPI) and fix the findings.

**Acceptance:** the checklist is complete.

---

### M5-13 · Full-game playtest and gate review

| Lane | Depends on | Owns |
| --- | --- | --- |
| human | all M5 tasks | `docs/implementation/M5-eras-three-four.md` (§Gate record), `CHANGELOG.md` |

**Goal:** Evenmere 1.0: every era, every platform.

**Acceptance:** all gate boxes are ticked, and the release is tagged with artifacts for all three platforms.

---

## Gate record

_Filled in when M5 closes._

## Backlog beyond 1.0

- NPC trader ships (a relaxed trade loop), from the architecture plan's out-of-scope list.
- Mod support switched on by default, with a Steam Workshop integration.
- More languages, following the M4 localisation pipeline.
- Real art replacing every placeholder, following the M4 style guide.
