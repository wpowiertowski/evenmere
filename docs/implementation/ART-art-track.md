# ART — Art track

**Goal:** build the hand-drawn isometric look ([ADR-0013](../adr/0013-art-direction.md)) as real assets alongside the game logic. M4's style lock and its art-source decision (M4-14) can then start from a working asset set rather than from placeholders.

The art track runs in parallel with M2–M4 and has no gate of its own. Every task lands through a PR with green CI, as in M2.

**Ground rules:**

- **Data, not code.** Assets replace the placeholders only through `game/data/visuals/` (AGENTS.md, §Godot conventions). No code path may branch on a content ID.
- **Agent-made assets.**
  - The implementing agent is multimodal and generates images from precise requirements: concept art, painted icons and textures. Every image follows the art bible and is reviewed visually.
  - Icons are painted PNGs or hand-authored SVGs; Godot imports both natively.
  - Buildings come from code-built meshes, generated from a per-building spec and optionally textured from a painted palette atlas.
  - The development Mac has no Blender, Inkscape or ImageMagick. A task that needs an external tool stops and reports it.
- **Image budgets.**
  - Concept images in `docs/art/concepts/` are PNG, at most 1 MB each and 12 in all.
  - Game textures are PNG, power-of-two, at most 1024 × 1024 each. Tileable ones tile seamlessly. Their import settings (`.import` files) are committed.
  - The whole track stays under 25 MB of images in the repository.
- **One style.** Every asset follows the art bible (ART-01).
  - The palette tokens in ART-01 are the source for M2-29's `game/data/visuals/palette.tres`.
  - Assets must look right under both the M1 look and M2-29's hand-drawn look: clean normals for the edge outlines, flat colours for the banded light.
- **Owns.** Art tasks never share files with M2 tasks that run at the same time. Each card lists its own files, and the orchestrator checks them against the running M2 wave before dispatch.

## Task index

| ID | Title | Lane | Depends on | Status |
| --- | --- | --- | --- | --- |
| ART-01 | Art bible: style guide, palette and asset briefs | art (docs) | ADR-0013 | todo |
| ART-02 | Icon set: goods, buildings, tiers and UI | art | ART-01 | todo |
| ART-03 | Building kit: stylized Era I buildings | art | ART-01 | todo |
| ART-04 | Textures for the hand-drawn look | art | ART-01 | todo |

Later cards (Era II buildings, terrain and water textures, a UI theme skin, props and life) are written after ART-01, from its briefs.

---

### ART-01 · Art bible: style guide, palette and asset briefs

| Lane | Depends on | Owns |
| --- | --- | --- |
| art (docs) | ADR-0013 | `docs/art/**` (new, including generated concept images in `docs/art/concepts/`); this file (the ART-01 row only) |

**Reads:** ADR-0013; the reference at [permit.petercai.com](https://permit.petercai.com), as summarised in ADR-0013; progression.md (Era I and Era II buildings, goods, tiers and terrain); `game/data/visuals/*.json` and `game/rendering/buildings/PlaceholderMeshFactory.cs` (today's placeholders); the M2-29 card.

**Goal:** one written art bible that every art task and M2-29 follow.

**Steps:**

1. **Style guide** (`docs/art/style-guide.md`):
   - **View:** the isometric view, with the 30° and 45° yaw options M2-29 compares.
   - **Lines:** ink outline weights by object class (buildings, roads, terrain, water).
   - **Light:** banded light with a warm key, a cool fill and soft long shadows; paper and watercolour grain; edge darkening.
   - **Detail:** what reads at island zoom and what at street zoom.
   - **Scale:** metres per tile, storey height, roof pitches and door sizes.
   - **Materials by era:** Era I timber, wattle and thatch; Era II brick and tile; later stone and glass.
2. **Palette** (`docs/art/palette.md`):
   - Named sRGB tokens for terrain biomes, water depths, building materials, roofs, accents and the UI.
   - A mapping from today's placeholder colours to the tokens.
   - A contrast check for UI text, and goods colours that stay distinct for colour-blind players.
3. **Building briefs** (`docs/art/briefs/buildings/<id>.md`), one per Era I building in progression.md, not only the slice's 14:
   - the footprint, and a silhouette in words or ASCII;
   - the roof shape, materials and palette tokens;
   - signature props: log piles, drying nets, sails, sacks;
   - what reads at island zoom;
   - the working, idle and paused cues, and the construction-site look;
   - hooks for the upgrade variants M2-30 may add.
4. **Icon brief** (`docs/art/briefs/icons.md`):
   - the grid: a 64 × 64 SVG viewBox, a 2-unit ink stroke, a watercolour-style fill in at most three tones;
   - one motif per good, building, tier, build-menu category, knowledge and culture.
5. **Terrain and water brief** (`docs/art/briefs/terrain.md`):
   - biome surfaces, coast and shallows;
   - river banks, carried forward from the M1 gate and feeding M2-29;
   - styling hooks for M2-20's fertility and deposit overlays.
6. **Coverage table** (`docs/art/README.md`): every Era I building, good and tier in progression.md, with its brief and its planned asset task.
7. **Visual targets** (`docs/art/concepts/`): generated concept images that anchor the written rules, within the image budget:
   - a palette swatch sheet with the token names;
   - an icon style sheet: six sample icons on the 64-unit grid;
   - a building style sheet: four representative Era I buildings (a settler house, the market, the windmill and a production hut) at the game's camera angle, with their silhouettes;
   - one island scene at the default view, in the target look, with roads, houses and a farm.

   The written rules win where an image disagrees; note any disagreement and fix the image or the rule.

**Acceptance:**

- The coverage table lists every Era I building, good and tier, each with a brief.
- The palette maps every current placeholder colour.
- The concept images exist within the budget and match the written rules.
- The developer reviews the style guide and the concept images, and signs off on the direction.

**Out of scope:** code and the assets themselves.

---

### ART-02 · Icon set: goods, buildings, tiers and UI

| Lane | Depends on | Owns |
| --- | --- | --- |
| art | ART-01 | `game/assets/icons/**` (new painted PNGs or SVGs: `goods/<id>`, `buildings/<id>`, `tiers/<id>`, `ui/<name>`); `game/ui/common/IconLibrary.cs` (new); the icon-drawing lines of the build-menu entries and the HUD goods chips; `tests/Evenmere.Game.Tests/**` (icon resolution tests); this file (the ART-02 row only) |

**Reads:** ART-01 (the icon brief and palette); `game/data/visuals/BuildingVisuals.cs` (today's icon glyphs); the build-menu and HUD view code.

**Goal:** real icons in the established style replace the emoji and glyph placeholders.

**Steps:**

1. **Make the icons** from ART-01's icon brief and icon style sheet. Use painted PNGs (a 256 × 256 master with a transparent background) or SVGs, whichever the brief chooses, consistently across the set:
   - every Era I good, building and tier;
   - the build-menu categories;
   - knowledge and culture.

   Keep one stroke weight, one light direction and the palette across the whole set.
2. **`IconLibrary`.** It resolves a content ID and a kind to a texture from `game/assets/icons/` by convention (PNG or SVG), falling back to the visuals glyph, so a missing icon never breaks a menu.
   - The imports stay crisp at HiDPI (M2-31's content scale): mipmaps for PNGs, an import scale for SVGs.
3. **Wire the icons** into the build-menu cards and the HUD goods chips. Other surfaces adopt `IconLibrary` in their own cards.

**Acceptance:**

- Every Era I good, building and tier has an icon, and a test checks that coverage against content.
- Screenshots of the build menu and HUD at 1280 × 800, 1600 × 900 and 2560 × 1600 show crisp icons.
- The developer reviews the set.
- `scripts/verify.sh` passes.

**Out of scope:** icons for Era II and later; a full UI theme skin.

---

### ART-03 · Building kit: stylized Era I buildings

| Lane | Depends on | Owns |
| --- | --- | --- |
| art | ART-01 | `game/rendering/buildings/Kit/**` (new); `game/data/visuals/kit/<id>.json` (new, one spec per building); the mesh-selection lines in `game/rendering/buildings/PlaceholderMeshFactory.cs` and `BuildingRenderer.cs`; `tests/Evenmere.Game.Tests/**` (kit tests); this file (the ART-03 row only) |

**Reads:** ART-01 (the style guide, palette, building briefs and the building style sheet); ART-04's painted palette atlas, if it has landed; `game/rendering/buildings/` (the placeholder path and multimesh batching); the M2-29 card.

**Goal:** stylized, code-built buildings in the art bible's style replace the placeholder boxes, building type by building type.

**Steps:**

1. **Kit parts:**
   - wall types (timber frame, wattle, plank);
   - roofs (gable, hip, thatch with overhang);
   - chimneys, doors, windows and shutters;
   - props: log piles, nets, sacks, fences, crop rows, windmill sails.

   Each part is a pure mesh builder with clean normals for M2-29's outlines, coloured from palette tokens.
2. **Per-building spec** (`game/data/visuals/kit/<id>.json`): it composes parts on the footprint. The renderer uses the kit mesh when a spec exists and the placeholder otherwise, keeping one mesh per building type so multimesh batching holds.
3. **Coverage:**
   - the 14 slice buildings first, then the remaining Era I buildings;
   - a construction-site variant (scaffold and stacked materials);
   - a hook for the upgrade variants.
4. **Detail:** the silhouette reads at island zoom; small props appear only at street zoom, if that's cheap.

**Acceptance:**

- Screenshots of the Landing at the default and street zoom compare the kit with the placeholders, under the M1 look and, once it lands, M2-29's look.
- The frame rate stays at the 120 fps cap on the development Mac at 1280 × 800 on the bench save.
- Picking is unaffected.
- The developer reviews the buildings.
- `scripts/verify.sh` passes.

**Out of scope:** Era II buildings, animation (sails turning, smoke: M2-24), terrain textures.

---

### ART-04 · Textures for the hand-drawn look

| Lane | Depends on | Owns |
| --- | --- | --- |
| art | ART-01 | `game/assets/textures/**` (new PNGs and their `.import` files); `docs/art/briefs/textures.md` (new); `tests/Evenmere.Game.Tests/**` (texture budget tests); this file (the ART-04 row only) |

**Reads:** ART-01 (the style guide, palette, terrain brief and concept images); the M2-29 card (which shaders consume which texture); `game/rendering/terrain/` and `game/rendering/water/`.

**Goal:** painted textures that M2-29's shaders and ART-03's kit use for the hand-drawn look.

**Steps:**

1. **Texture brief** (`docs/art/briefs/textures.md`): for each texture, its consumer, size, channel use, tiling, and the palette tokens it may carry.
2. **Generate the textures:**
   - **Surface grain:** a tileable paper grain and a watercolour wash, greyscale, so the shader tints them from the palette.
   - **Water:** hand-drawn ripple strokes, tileable, for the painterly water and the river banks.
   - **Terrain:** one tileable hand-painted texture per biome in the terrain brief, low in contrast, so the banded light and the outlines carry the shape.
   - **Building palette atlas:** a small atlas of painted material swatches (timber, wattle, thatch, plank, stone, roof tile), which ART-03's kit can map by UV.
3. **Budgets and seams.** Every texture is power-of-two, at most 1024 × 1024 and seamless where tiling. A Godot-free test checks the sizes and the track's total budget.

**Acceptance:**

- A contact sheet of every texture, plus a tiled 3 × 3 view of each tileable one, shows no visible seams.
- The textures fit the image budget.
- The developer reviews the set.
- `scripts/verify.sh` passes.

**Out of scope:** the shaders that use them (M2-29), seasonal variants (M2-24).

