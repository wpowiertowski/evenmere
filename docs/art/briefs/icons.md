# Brief: icon set (ART-02)

Painted PNG masters, imported natively by Godot. One language for 13 goods, 32 Era I
buildings (28 base plus 4 upgrades), 2 tiers, build-menu categories, knowledge and culture — every chip,
card and badge in the game.

**Format decision (settled from the `icons.png` concept sheet, ART-01 round 2):
painted PNG, not SVG.** A 256 × 256 master per icon, transparent ground, drawn on
the 64-unit design grid (4 px per unit). Reasons: the dev Mac has no vector tooling
and agents cannot reliably hand-author bezier SVGs; a PNG master is fully
authorable with the deterministic PIL pipeline proven by the concept sheets;
Godot rasterises imported SVGs (thorvg) to textures at import anyway, so a
256 master with mipmaps is visually equal at every HUD size and stays crisp at
HiDPI via the M2-31 content scale; file sizes stay small and every pixel is
checkable by test (exact token fills, ink coverage). The 64-unit grid and 2-unit
ink survive unchanged — they are the design grid, not a vector format.

## 1. Grid and stroke

- **ViewBox:** the 256 × 256 master maps to the `0 0 64 64` design grid.
  Clear-space: nothing inside 4 units (16 px) of the edge.
- **Ink:** 8 px stroke at master size (= 2 units on the 64 grid), `ink.strong`
  (`#2E2620`), round caps and joins.
  Interior detail lines may drop to 5 px (= 1.25 units); never below 4 px.
- **Fills:** flat token fills, at most **three** tones per icon (base + shade +
  one accent). No gradients, no filters, no blur — the NPR grain pass adds texture.
- **Silhouette first:** the icon must read at 16 px. Test: export at 16 × 16; if two
  icons merge, change the silhouette, not the detail.
- **Style:** frontal or three-quarter pictogram, 2 px-equivalent ink, one visual
  joke at most (the fish may smile). Calm, not cute.

## 2. Colour rules

- Fills come from `good.*` tokens for goods, building wall/roof tokens for
  buildings, tier tokens below for tiers.
- Backgrounds: icons sit on transparent ground inside cream chips; never bake a
  rounded rect into the PNG.
- Knowledge: open-book motif, `ui.link` fill. Culture: lyre/mask motif,
  `ui.accent` fill. Coin (later): `field.grain` disc.

## 3. Motifs (binding)

Each row is the one silhouette ART-02 must draw. Uniqueness was checked against
the CVD close pairs in `palette.md` §7: the motif carries what colour cannot.

**Goods (13):**

| Good | Motif | Fill |
| --- | --- | --- |
| `wood` | Three-log pile, end-grain circles | `good.wood` |
| `timber` | Three stacked planks, offset | `good.timber` |
| `fish` | Fish in profile, tail left | `good.fish` |
| `wool` | Fleece cloud, three scallops | `good.wool` |
| `homespun` | Folded bolt of cloth, two fold lines | `good.homespun` |
| `grain` | Wheat ear, five grains | `good.grain` |
| `flour` | Tied sack, knot on top | `good.flour` |
| `bread` | Round loaf, three slashes | `good.bread` |
| `stone` | Two rough blocks, cracked face | `good.stone` |
| `apples` | Apple + single leaf | `good.apples` |
| `cider` | Handled mug, foam cap | `good.cider` |
| `clay` | Irregular lump, thumb-dent | `good.clay` |
| `pottery` | Amphora, two handles | `good.pottery` |

**Buildings (32):** the building's roof shape in three-quarter view plus its
signature prop (the island-zoom read from its brief, miniaturised). Examples:
windmill = tower + four-sail cross; fishing hut = hut + hanging net triangle;
market = striped awning trapezoid; shrine = stone circle + flame.
Full list follows the building briefs; ART-02 checks coverage against content IDs
with a test (`IconLibrary` falls back to the visuals glyph when missing).

**Era I upgrades (4):** each reads as its base building plus one cue.

| Building | Motif | Cue |
| --- | --- | --- |
| `stone_shrine` | Stone cell + cap, circle + flame (as `shrine`) | Dressed-stone ashlar lines on the cell |
| `fish_weir` | Plank hut (as `fishing_hut`) | Woven weir slats: three stakes, rail and net band |
| `rotation_farm` | Soil field (as `grain_farm`) | Striped fields: sown half with wheat, resting half with stubble |
| `communal_oven` | Wattle cottage + chimney (as `bakery`) | Twin domed ovens on the long face |

**Tiers (2 now, 4 later):**

| Tier | Motif | Fill |
| --- | --- | --- |
| Settlers | Single thatch chevron (one roof) | `roof.thatch.light` |
| Villagers | Thatched house front, door + flower box | `wall.wattle` + thatch |
| Townsfolk (later) | Two-storey house, tile roof | brick + tile tokens |
| Artisans (later) | House + hammer-and-shuttle crossed | brick + `ui.accent` |

**Build-menu categories:** production = hammer; infrastructure = arch/bridge;
services = bell; knowledge = book; culture = lyre; residence = house chevron;
roads = curving path; monuments = standing stone.

## 4. Technical constraints

- One file per item: `game/assets/icons/goods/<id>.png`,
  `buildings/<id>.png`, `tiers/<id>.png`, `ui/<name>.png`, each a 256 × 256 master.
- Downscales to 16/24/32 px chips must keep the silhouette readable; test: export
  at 16 × 16 — if two icons merge, change the silhouette, not the detail.
- Keep masters flat and countable: every fill samples to a `palette.md` token,
  ink coverage testable by pixel count.
- Filenames are content IDs exactly; `IconLibrary` resolves by convention.
- No text in icons, no locale-dependent glyphs.
