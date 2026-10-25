# Evenmere art bible — style guide

The target look is the hand-drawn isometric city builder recorded in
[ADR-0013](../adr/0013-art-direction.md): SimCity/Anno-like density and readability,
drawn as if inked over the models. This guide is written for two consumers: the
agent-authored interim assets (ART-02 painted icons, ART-03 code-built kit) and the later
real-art pipeline (Blender → glTF, decided in M4-14). Both follow the same numbers.
Numbers here are binding; adjectives are not. M2-29 takes its tokens from
`palette.md` and must honour every §-marked constraint.

## 1. View and camera

- **Projection:** orthographic isometric. Yaw offset 30° (M2-29 also prototypes
  the reference's 45° for comparison; the developer picks). 90° rotation steps.
- **Pitch blends with zoom** (M1-34, confirmed in play): 30° at street zoom,
  50° fully zoomed out, about 43° at the default view.
- **Assets must read across 30°–50° of pitch at 30° yaw** (and stay valid at
  45° yaw if M2-29 picks it): silhouettes, roof pitches and signature props are
  checked at both pitch extremes, not only at the default.
- **The default view (~43° pitch, 30° yaw) is the reference for concept images**
  and look reviews. M2-29 may revisit the pitch for the orthographic camera;
  that is M2-29's call, and it updates this guide if it changes the range.
- **Design resolution:** 1600 × 900 logical points. A bigger window shows more island
  at the same tile size; UI scale follows M2-31's rule.
- **Zoom levels that art must serve:**
  - *Island zoom* (default): whole settlement readable. Only silhouette, roof shape,
    roof colour and one signature prop may be relied on.
  - *Street zoom* (closest): doors, windows, props, grain sacks, washing lines.
- **Tile metric:** 1 tile = 4 m. A 2 × 2 building is an 8 × 8 m footprint.

## 2. Ink outlines

Outlines are screen-space depth/normal edges with noise wobble (M2-29), but geometry
must already carry clean, hard normals: flat-shaded facets, no smoothed shed roofs,
no bevelled boxes. Line weights are specified in px at 1600 × 900, default zoom:

| Class | Weight | Colour token | Notes |
| --- | --- | --- | --- |
| Buildings (walls, roofs, props) | 2 px | `ink.strong` | Every closed solid gets a full outline |
| Roads, bridges, plaza paving | 1 px | `ink.soft` | Softer: reads as ground, not object |
| Terrain facets, coast, river banks | 1 px | `ink.soft` | Coast gets a second foam stroke (see terrain brief) |
| Water detail strokes (ripples) | 1 px | `ink.water` | Hand-placed strokes, not edges |
| Icons (painted PNG, 64-unit grid) | 8 px at 256 master (= 2 units) | `ink.strong` | See icon brief |
| UI panels and cards | 1 px | `ink.soft` | Cream panels, dark text (§7) |

- **Wobble:** ±0.5 px low-frequency noise on building outlines only. Terrain, water
  and UI lines stay straight.
- **Corners:** outlines close around every silhouette corner; T-junctions where a prop
  meets a wall are allowed to break (drawn-through look).
- **Never outline:** individual crop rows, grass tufts, smoke, glow, text.

## 3. Light and shading

- **Key:** warm late-afternoon sun from the west-south-west, elevation ~35°.
  Token `light.key` (#FFE3B0) tints lit faces.
- **Fill:** cool sky bounce from the east, token `light.fill` (#Bcd0E0 → `#BCCFDf`;
  final hex `light.fill #BCCFDF`). Shadows are warm grey, never blue-black.
- **Banding:** exactly 3 diffuse bands (lit / mid / shade) plus a cast-shadow band.
  Band boundaries are hard at island zoom; M2-29 may dither them by ≤1 px.
  Flat colours on the model; the shader does the banding, so kit parts and glTF
  meshes carry a single albedo each.
- **Cast shadows:** long (shadow length ≈ 1.6 × object height), soft-edged
  (4 px blur at design resolution), 35% multiply. Shadows fall across water and are
  visible on its surface (painterly water requirement).
- **Paper grain:** full-screen multiply noise, amplitude ≤6%, tileable, static in
  screen space. Watercolour blotches: ≤10% lightness variation inside large flat
  areas (fields, roofs, sea), applied in the shader, never baked into textures.
- **Edge darkening:** large surfaces darken ≤8% toward silhouette edges (the
  watercolour-pooling look).

## 4. Scale and construction

| Measure | Value |
| --- | --- |
| 1 tile | 4 m |
| Wall storey height | 2.6 m (0.65 tiles) |
| Door | 1.0 m wide × 2.0 m tall, always on the long face, never centred on corners |
| Window | 0.7 × 0.9 m, shutters on houses and workshops |
| Roof pitch, thatch (Era I) | 45° ± 3°, ridge along the long axis |
| Roof pitch, shingle/plank | 40° ± 3° |
| Roof pitch, stone/monumental | 30°–35° |
| Thatch overhang | 0.4 m past the wall face on all sides |
| Chimney | 0.6 × 0.6 m plan, rises 1.2 m above the ridge; every food/forge building has one |
| Fence / dry-stone wall | 1.1 m tall |
| Human figure (for scale checks only) | 1.7 m; never rendered in-game |

- **Roof rule:** the roof is the building's fingerprint. At island zoom the player
  must tell any two same-size buildings apart by roof shape + one prop alone.
- **Footprints** come from content JSON (`footprint`), never from the brief.
  If a brief and content disagree, content wins and the brief is fixed.

## 5. Materials by era

- **Era I (Founding):** timber frame with wattle-and-daub panels, plank walls on
  work buildings, thatch roofs everywhere except the quarry/clay pit (open workings)
  and stone buildings below. Dry-stone footings 0.5 m tall on every building.
  No brick, no tile, no glass, no iron fittings larger than a hinge.
- **Era II (Township):** brick walls, clay-tile roofs at 35°, timber jetties,
  stone sills. Briefs marked "later" use these; Era I assets must not.
- **Later eras:** stone ashlar, glass, slate, steel — out of scope for ART-02/03
  except where a brief names an upgrade hook.

## 6. Detail ladder (what reads where)

| Zoom | Must read | Must NOT rely on |
| --- | --- | --- |
| Island | Roof shape + colour; footprint size; one signature prop; working/idle smoke or sail state | Doors, windows, text, small props |
| Street | Doors, shutters, goods sacks, nets, fences, washing, signage boards | Interior, individual tools, text smaller than 24 pt equivalent |

Every building brief names the island-zoom read explicitly. If a building's read
fails at island zoom in review, the fix is roof or prop, never outline weight.

## 7. UI look (for icons and HUD chips)

- Panels: `ui.panel` cream (#F4EAD2), 1 px `ink.soft` border, 6 px corner radius,
  soft drop shadow (0, 2 px, 12% black).
- Text tokens and contrast (measured on `ui.panel`; WCAG AA needs ≥ 4.5:1):
  `ui.text` 12.4:1, `ui.text.soft` 9.0:1, `ui.accent` 7.0:1, `ui.link` 7.6:1,
  `ui.good` 5.2:1 — all pass for body text. `ui.muted` (4.4:1) is large-text
  and graphics only, never body copy.
- Chips (goods, tier badges): flat token fill + 2-unit ink icon + count in
  `ui.text`. Colour is never the only signal: icon shape + label always accompany it.

## 8. Shared state language (all buildings)

The view can show: **working**, **idle**, **paused**, **construction site**, and for
houses the **tier**. Every brief maps these to the same vocabulary:

- *Working:* chimney smoke (thin, warm grey, 2-tile rise) and/or open shutters,
  plus the trade prop active (sails turning on the windmill, nets hung on the
  fishing hut, cart at the storehouse door).
- *Idle* (staffed, no work): no smoke, shutters half-open, door closed, props
  tidied (nets furled, empty racks).
- *Paused* (player-paused): desaturate the building 30% toward grey AND lay the
  `prop.tarp` canvas cover over the door; never grey alone (colour-blind safety).
- *Construction site:* standard scaffold — raw-timber pole frame at full building
  height, 2 plank levels, rope lashings, a material stack (timber pile + stone pile
  + barrel) beside it, and one canvas tarp. Upgrade sites reuse the same scaffold
  at the existing building's height (M2-30 hook).
- *Ruin* (demolished-in-progress / placeholder path): footing + one low wall course,
  grey, no roof.
- *Houses by tier:* Settler = bare timber frame + thatch; Villager adds plaster
  panels, one flower box and a chimney with smoke; later tiers add a second storey
  element and a tile roof (Era II briefs, not this track).

## 9. Upgrade hooks (M2-30)

Every brief ends with an upgrade hook: what the M2-30 variant changes (staffing,
roof or material swap, added storey) and which parts it reuses. The kit must build
the upgrade by swapping parts on the same footprint, never by authoring a new
silhouette from scratch.
