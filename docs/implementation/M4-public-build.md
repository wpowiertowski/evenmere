# M4 — Public build (macOS + Linux/Steam Deck)

**Goal:** turn Eras I–II into a public build on Steam for macOS and Linux, verified on Steam Deck. That means:

- Performance at the scale target.
- A tutorial, settings and accessibility, full gamepad play, audio and localisation readiness.
- Signed and notarised exports, crash capture and Steam integration.

**Gate:**

- [ ] **Sim performance:** the `perf-20k` reference save (20,000 residents, 5,000 buildings) runs within budget (≤ 2.0 ms average, ≤ 6 ms p99 per tick) on Steam Deck hardware or an equivalent Zen 2 baseline.
- [ ] **Frame rate:** 60 fps on Steam Deck at 1280 × 800 on medium settings, and 120 fps on the development Mac, on the `perf-5k` save.
- [ ] **Steam Deck checklist:** gamepad-only play, legible text, on-screen glyphs, suspend and resume safe.
- [ ] **macOS export:** a universal binary, notarised, launching on a clean Mac without Gatekeeper warnings.
- [ ] **Linux export:** launches on Steam Deck through Steam (Proton is not needed).
- [ ] **Tutorial:** a new player goes from landing to Villagers through the tutorial without outside help (2 or more testers).
- [ ] **Playtest:** a sign-off playtest round (3 or more players) is complete, and its findings are triaged.

## Task index

| ID | Title | Lane | Depends on | Status |
| --- | --- | --- | --- | --- |
| M4-00 | Kickoff: refine M4 cards against the code | sim | M3 gate | todo |
| M4-01 | Worker-thread SimHost and FrameSnapshot | game + sim | M4-00 | todo |
| M4-02 | Sim performance pass at the scale target | sim | M4-00 | todo |
| M4-03 | Rendering performance and LOD | game | M4-00 | todo |
| M4-04 | Tutorial (data-driven steps on the starter island) | content + game | M4-00 | todo |
| M4-05 | Settings and accessibility | game | M4-00 | todo |
| M4-06 | Gamepad and Steam Deck pass | game | M4-05 | todo |
| M4-07 | Audio: ambient layers and adaptive music | game | M4-00 | todo |
| M4-08 | Localisation pipeline and pseudo-locale | content-infra + game | M4-00 | todo |
| M4-09 | macOS export, signing and notarisation | infra | M4-00 | todo |
| M4-10 | Linux export and Steam Deck verification | infra | M4-00 | todo |
| M4-11 | Steam integration | game | M4-09, M4-10 | todo |
| M4-12 | Crash capture and bug-report bundle | game | M4-00 | todo |
| M4-13 | Content-pack loader (mod-ready) | content-infra | M4-00 | todo |
| M4-14 | Art direction decision and style guide | human + game | M4-00 | todo |
| M4-15 | Explain-everything audit and UI polish | game | M4-04 | todo |
| M4-16 | Playtest round and tuning | human + content | M4-01 to M4-15 | todo |
| M4-17 | Release candidate and gate review | human | M4-16 | todo |

---

### M4-00 · Kickoff: refine M4 cards against the code

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim (planning) | M3 gate | `docs/implementation/M4-public-build.md` |

**Goal:** sharpen the cards, and confirm the developer has provided the M4 prerequisites: an Apple Developer ID and notarisation credentials, a Steamworks app ID, and a decision on the art source.

---

### M4-01 · Worker-thread SimHost and FrameSnapshot

| Lane | Depends on | Owns |
| --- | --- | --- |
| game + sim | M4-00 | `game/host/**`, `src/Evenmere.Sim/Snapshot/**`, `tests/Evenmere.Sim.Tests/Snapshot/**` |

**Reads:** ADR-0006 (phase 2).

**Goal:** the sim ticks off the main thread, and the view reads immutable snapshots.

**Steps:**

1. **`FrameSnapshot`:** a builder copies the view-relevant tables (buildings, jobs, house tiers, HUD numbers, dirty chunks and events) into pooled arrays after each tick batch, and implements the query interfaces the view uses.
2. **Threading:** a worker thread runs ticks according to speed, and a lock-free single-producer/single-consumer queue carries commands. The main thread swaps to the latest snapshot each frame.
3. **Equivalence:** verify that the hash and event order are identical to phase 1 (same seed, same command log).

**Acceptance:** the replay hash is equal in both modes; frame time improves on `perf-5k`; no data races under a stress test (rapid commands at 16× speed).

---

### M4-02 · Sim performance pass at the scale target

| Lane | Depends on | Owns |
| --- | --- | --- |
| sim | M4-00 | `src/Evenmere.Sim/Systems/**` (optimisation only, with no behaviour change: hashes must match), `tests/fixtures/saves/perf-20k.evsave` |

**Goal:** meet the per-system budgets at 20,000 residents and 5,000 buildings.

**Steps:**

1. Profile `perf-20k` and fix the hot paths: allocation-free loops, struct-of-arrays where it pays, incremental field rebuilds, request batching and bucketed work.
2. Every optimisation must keep the golden hashes. If a change has to alter behaviour, it needs an ADR and new goldens.

**Acceptance:** the perf report meets the budget table; all goldens are unchanged.

---

### M4-03 · Rendering performance and LOD

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M4-00 | `game/rendering/**` |

**Goal:** frame rate targets on Deck and Mac.

**Steps:**

1. Profile `perf-5k` and `perf-20k`.
2. Add mesh LODs for placeholders, MultiMesh visibility ranges, a chunk-rebuild time budget, walker and cart caps by zoom, shadow cascades tuned per platform, and graphics presets (low, medium, high).

**Acceptance:** fps captures on the Deck and the Mac are attached.

---

### M4-04 · Tutorial (data-driven steps on the starter island)

| Lane | Depends on | Owns |
| --- | --- | --- |
| content + game | M4-00 | `content/tutorial/**`, `content/locale/en/tutorial.json`, `game/ui/tutorial/**`, `src/Evenmere.Sim/Systems/Advisors/Tutorial/**` |

**Goal:** a calm, skippable tutorial that teaches the loop through advisors.

**Steps:**

1. **Steps as data:** each step has a goal condition (the shared vocabulary), a highlight target (a UI element ID or a world area), advisor text keys and an optional camera focus.
2. **Flow:** about 12 steps, covering the first road, the houses, the market, fish and wood, research, the Shrine, the bread chain, the first Villagers, festivals and expeditions.
3. **Control:** the tutorial can be skipped, resumed and replayed from the menu.

**Acceptance:** a scenario test completes every tutorial step with the bot; two first-time testers finish it.

---

### M4-05 · Settings and accessibility

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M4-00 | `game/ui/settings/**`, `game/host/Settings/**` |

**Goal:** comfortable for everyone.

**Steps:**

1. **Graphics:** preset, resolution, fullscreen, vsync, fps cap and UI scale.
2. **Audio:** volumes per bus.
3. **Controls:** key and pad rebinding.
4. **Accessibility:** text size, colour-blind-safe overlay palettes, reduced motion (no camera shake, softer particles), hold-to-confirm options and a toggle for edge panning.
5. **Storage:** settings live in `user://settings.cfg`, separate from saves.

**Acceptance:** every setting persists and applies live; screenshots.

---

### M4-06 · Gamepad and Steam Deck pass

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M4-05 | `game/input/**`, `game/ui/**` (focus and glyph fixes only) |

**Goal:** the whole game is playable comfortably on a Deck.

**Steps:**

1. **Navigation:** audit every screen for focus order and reachability.
2. **Glyphs:** on-screen button glyphs that switch between Xbox, PlayStation and Deck sets.
3. **Radial menus:** a radial build menu and a radial tool menu on the shoulder buttons.
4. **Text:** check legibility at 1280 × 800 at arm's length (minimum font size).
5. **Suspend and resume:** autosave on focus loss.

**Acceptance:** the Steam Deck checklist in this file's gate is completed with screenshots.

---

### M4-07 · Audio: ambient layers and adaptive music

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M4-00 | `game/audio/**`, `game/assets/audio/**` (CC0 placeholders with licence notes) |

**Goal:** a calm soundscape that follows the camera and the era.

**Steps:**

1. **Ambient layers:** sea, wind, birds, market, fields and workshops, selected by zoom level and the district under the camera, which is derived from nearby building categories.
2. **Adaptive music:** a layered music system with stems by era and season, crossfading between them.
3. **Event stings:** soft cues for construction completed, a tier advancing, a tech or civic completing and a great work being created, never alarms.

**Acceptance:** a recording of a few minutes of play is attached; all asset licences are listed.

---

### M4-08 · Localisation pipeline and pseudo-locale

| Lane | Depends on | Owns |
| --- | --- | --- |
| content-infra + game | M4-00 | `src/Evenmere.Content/Locale/**`, `src/Evenmere.Tools/Commands/LocaleCommand.cs`, `content/locale/pseudo/**`, `game/ui/common/Localization*` |

**Goal:** the game is ready for translation, even though English is the only shipped language for now.

**Steps:**

1. **`evenmere locale export|import`:** converts to and from CSV, with context columns (where each string is used).
2. **Pseudo-locale:** accented characters, 30% expansion and brackets, used to catch hard-coded or truncated text.
3. **Formatting:** plural and number formatting helpers.

**Acceptance:** the whole UI runs in the pseudo-locale with no untranslated strings (an automated scan in the smoke test) and no clipped text on core screens (screenshots).

---

### M4-09 · macOS export, signing and notarisation

| Lane | Depends on | Owns |
| --- | --- | --- |
| infra | M4-00 | `scripts/export-macos.sh`, `game/export_presets.cfg` (the macOS preset), `.github/workflows/release.yml` (macOS job) |

**Reads:** ADR-0008.

**Goal:** a reproducible, notarised universal macOS build.

**Steps:**

1. **Export script:** stage the content into `game/content/`, export a universal `.app` with Godot, codesign it with hardened runtime and entitlements, notarise it with `notarytool`, staple it, and zip it.
2. **CI job:** runs on tags, using secrets (the certificate as base64, the notarisation keys). Secrets never appear in the repo.

**Acceptance:** the CI artifact launches on a clean Mac, verified by the developer.

---

### M4-10 · Linux export and Steam Deck verification

| Lane | Depends on | Owns |
| --- | --- | --- |
| infra | M4-00 | `scripts/export-linux.sh`, `game/export_presets.cfg` (the Linux preset), `.github/workflows/release.yml` (Linux job) |

**Goal:** a Linux x86_64 build verified on Steam Deck.

**Steps:**

1. **Export script:** stage the content, export, and package as a tar archive.
2. **Deck verification:** sideload the build on a Steam Deck through Steam and run the Deck checklist.

**Acceptance:** the checklist is completed on device.

---

### M4-11 · Steam integration

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M4-09, M4-10 | `game/host/Steam/**`, `Directory.Packages.props` (add `Steamworks.NET` or an equivalent named at kickoff) |

**Goal:** a good Steam citizen.

**Steps:**

1. **Initialisation:** initialise only when launched through Steam; otherwise the game runs offline unchanged.
2. **Features:** cloud saves (`user://saves`), achievements (defined as data and driven by conditions, around 20 for Eras I–II), and rich presence ("Era II · 1,240 residents").
3. **Depots:** a Steam depot configuration for macOS and Linux.

**Acceptance:** an achievement unlocks in a test build; a cloud save round-trips between the Mac and the Deck.

---

### M4-12 · Crash capture and bug-report bundle

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M4-00 | `game/host/Diagnostics/**` |

**Goal:** every bug report is replayable.

**Steps:**

1. **Crash capture:** an unhandled-exception handler writes a crash file with the stack, the game version and platform, and the last 200 log lines.
2. **"Report a problem":** a menu item that bundles the latest save, its command log, the settings, the log and the content hash into a zip in `user://reports/`.
3. **Privacy:** no network upload; the player shares the zip themselves.

**Acceptance:** a bundle replays to the same hash through `evenmere replay`.

---

### M4-13 · Content-pack loader (mod-ready)

| Lane | Depends on | Owns |
| --- | --- | --- |
| content-infra | M4-00 | `src/Evenmere.Content/Packs/**`, `game/host/ContentPaths.cs`, `docs/design/modding.md` |

**Reads:** ADR-0004 (§Content packs).

**Goal:** the base game loads through the same pack path that mods will use later.

**Steps:**

1. **Pack manifest:** `pack.json` with ID, version, dependencies and load order.
2. **Load order:** the base pack first, then `user://packs/*`, which is disabled by default behind a setting.
3. **Saves:** saves record the active packs.
4. **Docs:** write `docs/design/modding.md`.

**Acceptance:** a sample pack that adds one good and one building loads, lints and appears in game (a test pack in the fixtures).

---

### M4-14 · Art direction decision and style guide

| Lane | Depends on | Owns |
| --- | --- | --- |
| human + game | M4-00 | `docs/design/art-direction.md`, `game/data/visuals/palette.tres` |

**Goal:** decide the art source and lock the palette and the modelling rules, so real art can replace the placeholders.

**Steps:**

1. **Decision:** the developer chooses between their own Blender work, asset packs and commissioned art.
2. **Style guide:** the palette texture, tile scale, footprint rules, polygon budgets per LOD, roof and wall vocabularies per style, and the naming and import rules for glTF files mapped through `game/data/visuals`.

**Acceptance:** the guide is merged, and one building has been replaced end to end as a proof.

---

### M4-15 · Explain-everything audit and UI polish

| Lane | Depends on | Owns |
| --- | --- | --- |
| game | M4-04 | `game/ui/**` (polish only) |

**Goal:** pillar 4 holds everywhere.

**Steps:**

1. **Audit:** every number on screen has a tooltip with sources. Produce an audit list in the PR, fix the gaps, and keep the formatting consistent.
2. **Build menu:** add search and favourites.
3. **Notifications:** tune the batching.

**Acceptance:** the audit list is complete, with every entry ticked.

---

### M4-16 · Playtest round and tuning

| Lane | Depends on | Owns |
| --- | --- | --- |
| human + content | M4-01 to M4-15 | `content/**` (tuning only), `docs/implementation/M4-public-build.md` (§Playtest notes) |

**Goal:** three or more outside players complete Eras I–II, and their findings shape the final tuning.

**Acceptance:** findings are triaged; the tuning PRs keep the bot within its pacing targets.

---

### M4-17 · Release candidate and gate review

| Lane | Depends on | Owns |
| --- | --- | --- |
| human | M4-16 | `docs/implementation/M4-public-build.md` (§Gate record), `CHANGELOG.md` |

**Goal:** ship the public build.

**Acceptance:** all gate boxes are ticked; a tagged release has artifacts for macOS and Linux; the changelog is written.

---

## Gate record

_Filled in when M4 closes._
