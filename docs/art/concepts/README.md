# Concept sheets (ART-01 round 2)

Visual targets painted from the bible's own rules — flat token fills, ink
outlines, banded warm light, paper grain. They are references for ART-02/ART-03
and M2-29, not shipped assets: no sheet is copied into the game, and none
reproduces any external artwork (all geometry here is generated from the
measurements in `style-guide.md` and the tokens in `palette.md`).

Regenerate: `python3 docs/art/concepts/generate.py` (stdlib + Pillow + numpy,
seed `20261025`; Pillow/numpy are throwaway environment packages, not repo
dependencies). Output is deterministic: same seed, same bytes.

How made (round 3): I have no native image-generation tool — no model or service
that paints from a description, only file/bash tools — so every sheet is drawn
in code by `generate.py`: flat token fills, three-band face shading, polygonal
ink outlines with +/-1.5 px deterministic wobble, thatch-course and material
texture strokes (grain, weave, hatch, page lines), soft translucent shadows,
and a low-spatial-frequency grain finish (2 px tooth + 16 px watercolour
blotches) that keeps PNGs small. True watercolour granulation, pigment pooling
at edges, and per-object line-weight variation are beyond a code renderer; the
sheets approximate them and M2-29's shader pass implements them for real.

## `palette.png` (1500 × 1560, 66 KB)

- Every one of the 81 `palette.md` tokens as a labelled swatch, grouped exactly
  as palette.md §§1–7, in luminance order within the goods group.
- NO grain pass on this sheet: swatches must sample to their exact hex.
- Illustrates: palette.md (all sections); the goods luminance ladder (§7).

## `icons.png` (1560 × 640, 463 KB)

- Six samples on the 64-unit design grid at 3 px/unit (192 px cells; the master
  ratio is identical at 256 px): fish, bread, homespun, timber, Settlers,
  knowledge. 2-unit ink (6 px here, 8 px at master), max three tones, unique
  silhouette per good (icons.md §3). Round 3: painted texture strokes (scale
  arcs, slashes + shade, warp/weft, grain, thatch hatch, page lines) and a
  redrawn homespun rolled bolt; +/-1.5 px deterministic outline wobble.
- Concept-scale grain only; masters ship clean on transparency.
- Illustrates: icons.md §§1–3. This sheet settled the format decision (below).

## `buildings.png` (1600 × 980, 753 KB)

- Four Era I buildings at the default view (~43° pitch, 0.62 depth compression,
  30° yaw), drawn large (300–460 px wide) — settler house (timber frame lines,
  thatch courses, lintel, cross-barred window, flower box, brick chimney +
  smoke), market (striped awning + goods dots + thatch courses), windmill
  (tapered 3-tier tower, ogee gable end, lattice sails on the cap front, door
  + grain sacks), woodcutter's hut (plank seams, thatch courses, log piles
  with pale end-grain, stump with axe).
- Each: long soft cast shadow, banded faces (lit top / mid / shade), ink
  outlines, watercolour blotch + fine tooth grain.
- Illustrates: style-guide.md §§1–4, 6, 8; the four building briefs.

## `island.png` (1600 × 1000, 754 KB)

- Default-view settlement fragment: plaster/frame houses on a dirt road with a
  soft shoulder, striped market, gold grain rows with stooks, fenced paddock,
  hand cart, log pile, herb rows, canopy trees, banded hills, beach +
  shallows + broken foam ring, ripple strokes on the sea, chimney smoke.
  All fills are tokens.
- Illustrates: style-guide.md §§1–3, 6–7; terrain.md §§2–4.

## Format decision: painted PNG, not SVG

The icon sheet proved a 256 × 256 PNG master (transparent, 8 px ink = 2 units)
carries the style with fully agent-authorable tooling, while hand-authoring
bezier SVGs without vector software is error-prone and Godot rasterises imports
anyway. Recorded in `briefs/icons.md` §§1, 4. Done in round 3: the ART-02 card
in `ART-art-track.md` now says painted PNG masters (the Ground-rules line 11
still says SVG — outside this task's Owns, left for the orchestrator).

## Reconciliation: where a sheet disagrees with a rule, the rule wins

1. Windmill sails face screen-left, an approximation of WSW in screen space, and
   the tower tiers are box-plan, not truly round — the brief's tapered round
   tower stands; the sheet shows the read (white cross), not the plan.
2. Market awning drawn steeper than the brief's 30° shed so the stripes read at
   sheet scale — the 30° rule stands for the kit.
3. `island.png` compresses spacing (farm rows read wider than 0.5 tiles);
   distances are illustrative, footprints and tokens are binding.
4. Icon cells are rendered at 192 px, not the 256 px master — the grid/ink ratio
   is identical, so the style proof transfers exactly.
5. Sheet grain is screen-space blotch + tooth at concept scale; M2-29 sets the
   real amplitudes (style-guide §3: ≤6% grain, ≤10% blotch) — the numbers stand.
7. (Round 3) The windmill now matches its brief: tapered 3-tier tower, ogee
   gable end, lattice sails mounted on the cap's front gable. The sails are
   drawn flat in the screen plane — the kit builds true 3D lattice blades.
8. (Round 3) Homespun was redrawn as a rolled bolt with warp/weft lines; the
   crate-stack reading is gone. The knowledge bookmark floats above the book's
   top edge — a real bookmark does the same; ART-02 may tuck it inside.
9. (Round 3) Island hills are nested screen-space bands, not raised geometry —
   they show the banded-light idea only. The east hillock keeps one tree so it
   reads as a knoll, not a crater; M2-29 shades real relief.
6. No rule text changed for the sheets except style-guide §1 (pitch range,
   round-2 addition) and icons.md (PNG format, this round).
