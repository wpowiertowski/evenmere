# Brief: textures for the hand-drawn look (ART-04, feeds M2-29 and ART-03)

These are the painted textures M2-29's shaders and ART-03's kit consume. Every
texture is a power-of-two PNG, at most 1024 × 1024, drawn in code by the
deterministic generator in `tools/art/textures/` (fixed seed, no timestamps in
the output files). Tiling textures are seamless: the generator draws with
toroidal wrapping, the budget test re-checks the seams, and the review contact
sheet shows a 3 × 3 tiling of each one.

No texture bakes lighting: the banded light, cast shadows and paper grain come
from the M2-29 shader pass (style guide §3). Textures carry albedo variation
only, kept low in contrast so the banding and the outlines carry the shape.

## 1. Manifest

| File | Size | Tiles | Channels | Consumer |
| --- | --- | --- | --- | --- |
| `grain/paper_grain.png` | 512 × 512 | yes | L (greyscale mask) | M2-29 full-screen paper grain multiply |
| `grain/wash.png` | 512 × 512 | yes | L (greyscale mask) | M2-29 watercolour blotch overlay |
| `water/ripples.png` | 512 × 512 | yes | L (stroke mask) | M2-29 painterly water + river surface detail |
| `terrain/plains.png` | 256 × 256 | yes | RGB albedo | M2-29 terrain, `terrain.plains` tiles |
| `terrain/forest.png` | 256 × 256 | yes | RGB albedo | M2-29 terrain, `terrain.forest` tiles |
| `terrain/beach.png` | 256 × 256 | yes | RGB albedo | M2-29 terrain, `terrain.beach` tiles |
| `terrain/hills.png` | 256 × 256 | yes | RGB albedo | M2-29 terrain, `terrain.hills` tiles |
| `terrain/highlands.png` | 256 × 256 | yes | RGB albedo | M2-29 terrain, `terrain.highlands` tiles |
| `terrain/peaks.png` | 256 × 256 | yes | RGB albedo | M2-29 terrain, `terrain.peaks` tiles |
| `terrain/fen.png` | 256 × 256 | yes | RGB albedo | M2-29 terrain, `terrain.fen` tiles |
| `terrain/marsh.png` | 256 × 256 | yes | RGB albedo | M2-29 terrain, `terrain.marsh` tiles |
| `terrain/reef.png` | 256 × 256 | yes | RGB albedo | M2-29 water, `terrain.reef` shelf |
| `terrain/sea.png` | 256 × 256 | yes | RGB albedo | M2-29 water, `terrain.sea` base |
| `terrain/deep.png` | 256 × 256 | yes | RGB albedo | M2-29 water, `terrain.deep` base |
| `terrain/river.png` | 256 × 256 | yes | RGB albedo | M2-29 water, `terrain.river` surface |
| `materials/palette_atlas.png` | 1024 × 1024 | no (cells) | RGB albedo | ART-03 kit parts, mapped by cell UV |

Total on disk is about 2 MB, inside the 8 MB ART-04 budget the test enforces
(the whole art track stays under 25 MB).

## 2. Surface grain (M2-29 screen-space pass)

- `paper_grain.png`: mid-grey (128) base, fine tileable value noise in two
  octaves, peak deviation ±10 levels. Multiplied full-screen at ≤6% amplitude.
- `wash.png`: mid-grey (128) base, low-frequency tileable blotches (4–6 cells
  across the tile) in two octaves, peak deviation ±22 levels (≤10% lightness).
  Overlaid on large flat areas (fields, roofs, sea) so they pool like watercolour.
- Both are greyscale masks, never tinted: the shader multiplies/tints them from
  the palette. No colour may appear in these files.

## 3. Water ripples (M2-29 water shader)

- `water/ripples.png`: mid-grey (128) base, hand-placed arcs in near-white
  (stroke centres 235, `ink.water` direction, not colour — the shader recolours).
  About 40 arcs per tile, radii 12–48 px, line width 2 px, plus short dashes;
  density ≈ 1 stroke per 4 tiles at world scale, aligned west-east offshore
  (the shader rotates them near shores from the distance field).
- Tileable: every stroke is drawn at all nine wrapped offsets.
- Channel use: L = stroke mask (128 = still water, 255 = stroke crest). The
  shader derives foam and ripple shading from this single channel.

## 4. Terrain (M2-29 terrain and water shaders)

One albedo tile per biome token in `palette.md` §4. Each tile:

- starts from the exact token fill (sRGB);
- adds low-frequency luminance variation ±4% (periodic value noise, 4 cells);
- adds fine grain ±3% (periodic value noise, 32 cells);
- adds one sparse hand-drawn motif in a darker/lighter token from the same
  family (see table), at street-zoom density only — nothing the island zoom
  depends on, per the style guide §6.

| Biome | Base token | Motif (token) |
| --- | --- | --- |
| plains | `terrain.plains` | grass sprigs, `leaf.deep`, ~60 per tile |
| forest | `terrain.forest` | canopy dots, `leaf.deep`, ~90 per tile |
| beach | `terrain.beach` | pebble dots, `stone.mid`, ~40 per tile |
| hills | `terrain.hills` | dry-grass strokes, `field.fallow` lightened, ~50 per tile |
| highlands | `terrain.highlands` | rock slashes, `stone.dark`, ~40 per tile |
| peaks | `terrain.peaks` | snow glints, `paper.highlight`, ~30 per tile |
| fen | `terrain.fen` | reed clumps (5 vertical strokes), `leaf.deep`, ~40 per tile |
| marsh | `terrain.marsh` | reed clumps + still-water dashes, `water.shallow`, ~40 per tile |
| reef | `terrain.reef` | subsurface arcs, `water.foam`, ~25 per tile |
| sea | `terrain.sea` | soft patchy wash toward `water.shallow` (blotch noise, no bands) |
| deep | `terrain.deep` | soft patchy wash toward `water.sea` (blotch noise, no bands) |
| river | `terrain.river` | soft centre lighten only (bank lines would stripe when tiled) |

Motif strokes are drawn wrapped (nine offsets) so the tiles stay seamless.
Sea, deep and river bands run along one axis and wrap in both.

## 5. Building palette atlas (ART-03 kit)

`materials/palette_atlas.png` is 1024 × 1024, a 4 × 4 grid of 256 × 256 px
cells. The kit maps a part's UVs into one cell; cells are not tiled. The first
row holds the Era I swatches; the remaining ten cells are reserved (flat
`paper.shadow`) for Era II materials and are not to be used yet.

| Cell (col, row) | Swatch | Base token | Painted detail |
| --- | --- | --- | --- |
| (0, 0) | timber | `wood.timber.light` | horizontal plank seams, `wood.beam.dark`, 4 courses |
| (1, 0) | wattle | `wall.wattle` | woven over-under stripes, `clay.raw` shadow lines |
| (2, 0) | thatch | `roof.thatch.light` | vertical stroke rows shading toward `roof.thatch.shadow` |
| (3, 0) | plank | `wall.plank` | vertical plank seams + knots, `wood.beam.dark` |
| (0, 1) | stone | `stone.mid` | ashlar block courses, `stone.dark` joints |
| (1, 1) | clay tile | `brick.fired` | overlapping tile rows, `clay.raw` shadow under each lip |

Cell UV rect for cell (c, r): u ∈ [c/4, (c+1)/4), v ∈ [r/4, (r+1)/4). Each cell
carries ±5% periodic grain so flat kit faces pool like watercolour under the
banded light. The atlas keeps the kit to one texture bind.

## 6. Import settings

Every PNG is imported as a 2D texture with mipmaps on (crisp at HiDPI, cheap at
distance) and sRGB in the standard mode the `.import` files pin:
albedo textures (`terrain/*`, `materials/*`) are read as colour; masks
(`grain/*`, `water/ripples.png`) are read as data in the shader from the same
sampling path, so one preset covers the set. The committed `.import` files are
generated by the Godot editor import pass, not hand-written.
