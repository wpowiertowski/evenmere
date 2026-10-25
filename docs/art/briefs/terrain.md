# Brief: terrain and water (feeds M2-29, M2-20, M2-24)

Terrain is low-poly facets with flat fills from `terrain.*` tokens; the NPR pass
(M2-29) adds ink facet lines, grain and banding. This brief fixes the fills, the
coast/river treatment and the overlay hooks. No terrain textures are authored in
this track — Era II terrain and water textures are later cards.

## 1. Biome surfaces

Facets stay large (one tile = one facet, no sub-tiling). Fill per biome from
`palette.md` §4. Facet value jitter ≤5% lightness, seeded by tile hash
(deterministic, engine-free: integer hash of tile coordinates, never noise textures).

| Biome | Fill | Detail (street zoom only) |
| --- | --- | --- |
| Plains | `terrain.plains` | Grass tufts (3-blade sprigs, `leaf.deep`), no outlines |
| Forest | `terrain.forest` | Canopy blobs per tree: 2 stacked spheres, `leaf.mid` + `leaf.deep` shade |
| Beach | `terrain.beach` | Pebble dots `stone.mid`, sparse |
| Hills | `terrain.hills` | Dry-grass strokes `field.fallow` |
| Highlands | `terrain.highlands` | Rock slashes `stone.dark` |
| Peaks | `terrain.peaks` | Snow facets, `paper.highlight` glints |
| Fen | `terrain.leaf.fen` → `leaf.fen` | Reed clumps (5 vertical strokes) |
| Marsh | `leaf.marsh` | Reed clumps + still-water quads (`water.shallow` at 60%) |
| Reef | `terrain.reef` | Subsurface arcs, `water.foam` dashed ring at low tide |

## 2. Coast and shallows

- Coastline: 1 px `ink.soft` facet edge + one `water.foam` stroke on the water side,
  broken (dash 3:2) so it reads hand-drawn. Foam width 0.3 tiles.
- Shallows ring: 1 tile of `water.shallow` lerp from beach to `water.sea`.
- Shore distance field (existing `ShoreDistanceField`) drives foam fade; M2-29 keeps it.

## 3. Rivers and banks (M1 gate carry-forward, M2-29 builds)

- River surface: `water.river` flat fill, full tile width.
- Banks: soft `ink.soft` 1 px line on both sides + 0.25-tile `water.foam` inner edge
  where the river meets sea; inland, a `terrain.beach` mud strip 0.2 tiles wide
  (clay pits sit on this strip — tint it `clay.raw` within 1 tile of a clay deposit).
- Narrow (≤3) vs wide (4–8) rivers differ only in width, never in treatment.
- Bridges cross on piles (`wood.bark.dark` posts into the water, no foam break).

## 4. Painterly water

- Base: `water.sea` → `water.deep` by depth, banded in 2 steps (not smooth).
- Ripple strokes: hand-placed 1 px `ink.water` arcs, density ~1 per 4 tiles,
  aligned to shore tangent near coasts, to wind (west-east) offshore.
- Cast shadows lie on the water (renderer requirement, see style guide §3).
- Wakes: `water.foam` chevron behind moving ships only (later).

## 5. Overlay hooks (M2-20)

Overlays tint tiles; they must survive the palette:

- Fertility: crop-colour hatch (`field.grain` for grain, etc.) at 40% over the tile,
  never a full re-fill, so the biome still reads.
- Deposits: `stone.dark` dot + `ink.strong` ring on the tile (stone/iron/coal/copper
  share the glyph; the tooltip names the deposit).
- Coverage/reach: cream `ui.panel` wash at 35% with `ui.link` dashed border at the
  edge ring; buildable-with-access adds a `ui.good` check tick per tile.
- Fog (M2-23): desaturate 60% + paper-grain double density over unrevealed regions.
