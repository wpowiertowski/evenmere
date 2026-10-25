# Evenmere art bible — palette

All colours are sRGB hex. Names are tokens: `family.name` (for example
`wood.timber.light`). M2-29's `game/data/visuals/palette.tres` takes these tokens
verbatim; ART-02 icons and the ART-03 kit colour every part with them. No asset may
introduce an unlisted colour except through the seasonal shift rules in §7.

Token count: **81** (ink 3 · light 3 · paper 2 · UI 8 · wood 4 · wall 3 · roof 5 ·
stone 3 · clay/brick 3 · props 4 · foliage/field 10 · terrain 12 · water 5 · road 3 ·
goods 13).

## 1. Ink, light, paper

| Token | Hex | Use |
| --- | --- | --- |
| `ink.strong` | `#2E2620` | Building outlines, icon strokes, body text |
| `ink.soft` | `#57493A` | Road/terrain/UI lines, secondary strokes |
| `ink.water` | `#2F4A55` | Water detail strokes, river banks |
| `light.key` | `#FFE3B0` | Warm key tint (shader, not a fill) |
| `light.fill` | `#BCCFDF` | Cool fill tint (shader, not a fill) |
| `light.shadow` | `#8A7360` | Cast-shadow multiply base, 35% alpha |
| `paper.highlight` | `#FFF8E8` | Foam caps, flour sacks, sunlit paper |
| `paper.shadow` | `#E2D3B0` | Recessed panels, underside of eaves |

## 2. UI

Panels are cream `ui.panel`. Contrast measured against it (AA body ≥ 4.5:1):

| Token | Hex | Ratio | Allowed for |
| --- | --- | --- | --- |
| `ui.panel` | `#F4EAD2` | — | Panel fill |
| `ui.panel.dark` | `#2E2620` | 12.4:1 (cream text on it) | Headers, night badges |
| `ui.text` | `#2E2620` | 12.4:1 | All body copy |
| `ui.text.soft` | `#4A3B2C` | 9.0:1 | Secondary copy |
| `ui.accent` | `#8A2F1B` | 7.0:1 | Warnings, festival highlights |
| `ui.link` | `#1F4E5F` | 7.6:1 | Links, research highlights |
| `ui.good` | `#3E6B34` | 5.2:1 | Positive deltas, growth |
| `ui.muted` | `#7A6A4F` | 4.4:1 | Large text (≥18 pt), icons/graphics only — never body copy |

## 3. Building materials (Era I)

| Token | Hex | Use |
| --- | --- | --- |
| `wood.bark.dark` | `#59371D` | Log piles, bark, bridge piles |
| `wood.beam.dark` | `#4A3A26` | Timber frame, scaffold poles, jetty posts |
| `wood.plank.mid` | `#8A6A45` | Work-building walls, doors, fences |
| `wood.timber.light` | `#D0A05C` | Planed timber, beams, barrels |
| `wall.wattle` | `#CDBF9B` | Wattle panels, cottage walls |
| `wall.plaster` | `#EFE6D2` | Villager-tier plaster, shrine walls |
| `wall.plank` | `#A8895A` | Plank siding (darker than timber, lighter than plank.mid walls) |
| `roof.thatch.light` | `#D3A944` | Thatch lit slope |
| `roof.thatch.shadow` | `#A8812F` | Thatch shaded slope / ridge cap |
| `roof.shingle` | `#8D7B5F` | Storehouse and hall shingle roofs |
| `roof.canvas` | `#E4DCC8` | Awnings, tarps, sails (with `prop.tarp` below) |
| `roof.stone` | `#7A7A74` | Shrine capstones, oven hoods |
| `stone.light` | `#9B9B95` | Dressed stone, quarry blocks, plaza setts |
| `stone.mid` | `#7A7A74` | Dry-stone walls, footings |
| `stone.dark` | `#55554F` | Footing shadow course, smith-adjacent trim |
| `clay.raw` | `#8F5636` | Clay banks, unfired pots, daub |
| `brick.fired` | `#B25A38` | Kiln-fired pots, oven mouths (Era II walls) |
| `pottery.glaze` | `#C46A4A` | Finished pottery, market awning stripes |
| `prop.tarp` | `#4A6B74` | Paused covers, site tarps — the blue that never means water off-coast |
| `prop.rope` | `#8A7355` | Ropes, nets (dry), lashings |
| `prop.net` | `#5E6E5A` | Fishing nets (wet green-grey) |
| `prop.sail` | `#EFE6D2` | Windmill sails, identical to plaster (one token, two names by convention) |

## 4. Land: foliage, fields, terrain

| Token | Hex | Use |
| --- | --- | --- |
| `leaf.deep` | `#2F5B2A` | Forest canopy shade side, orchard shade |
| `leaf.mid` | `#3D7342` | Forest canopy, matches old `forest` fill |
| `leaf.light` | `#73A154` | Plains grass, sunlit canopy, matches old `plains` fill |
| `leaf.orchard` | `#5E8C3E` | Orchard rows |
| `leaf.fen` | `#8C9E66` | Fen reed beds, matches old `fen` fill |
| `leaf.marsh` | `#596B4D` | Marsh reeds, matches old `marsh` fill |
| `field.soil` | `#6B4E2E` | Ploughed soil stripes |
| `field.grain` | `#D3A944` | Ripe grain rows (same token as thatch: one harvest gold) |
| `field.pasture` | `#7FA35A` | Sheep pasture |
| `field.fallow` | `#9A7B52` | Fallow / dirt, matches old `dirt_road` fill |

Terrain tokens (biome fills; M2-24 seasonal shifts marked §7):

| Token | Hex | Replaces | Season shift? |
| --- | --- | --- | --- |
| `terrain.plains` | `#73A154` | old `plains` (unchanged) | yes — autumn gold, winter pale |
| `terrain.forest` | `#3D7342` | old `forest` (unchanged) | yes — autumn rust patches, winter dark |
| `terrain.beach` | `#D9C793` | old `beach` (unchanged) | no |
| `terrain.hills` | `#8C8559` | old `hills` (unchanged) | yes — like plains, weaker |
| `terrain.highlands` | `#807766` | old `highlands` (unchanged) | yes — winter snow dust |
| `terrain.peaks` | `#BFBDB8` | old `peaks` (unchanged) | no (permanent snow) |
| `terrain.fen` | `#8C9E66` | old `fen` (unchanged) | yes — autumn russet |
| `terrain.marsh` | `#596B4D` | old `marsh` (unchanged) | no |
| `terrain.reef` | `#409999` | old `reef` (unchanged) | no |
| `terrain.sea` | `#336B80` | old `sea` (unchanged) | no (light shift only) |
| `terrain.deep` | `#1A3B61` | old `deep_water` (renamed) | no |
| `terrain.river` | `#5E93A3` | old `river` (**changed**, was mud `#666147`) | no |

The `river` change: the old fill was indistinguishable from dirt, so rivers read as
ditches. Rivers are water and now share the water family.

## 5. Water

| Token | Hex | Use |
| --- | --- | --- |
| `water.shallow` | `#6FA8B8` | Shalllows ring, foam water |
| `water.sea` | `#336B80` | Open sea (same as `terrain.sea`) |
| `water.deep` | `#1A3B61` | Deep water (same as `terrain.deep`) |
| `water.foam` | `#F2EFE2` | Foam strokes, caps, wakes |
| `water.river` | `#5E93A3` | River surface (same as `terrain.river`) |

## 6. Roads

| Token | Hex | Use |
| --- | --- | --- |
| `road.dirt` | `#9A7B52` | Dirt road (same as `field.fallow`) |
| `road.cobble` | `#9B9B95` | Cobbled road (same as `stone.light`) |
| `road.bridge` | `#7A5C3D` | Timber bridge deck and rails |

## 7. Goods (chip fills and cart loads)

Sorted by relative luminance so neighbours are comparable. Rule: same-hue
neighbours keep a luminance gap ≥ 0.05 where possible; where the catalogue forces
a closer pair, the icon motif (always a unique silhouette) plus the text label
carries the distinction — colour is never the only signal (§8 icon brief).

| Token | Hex | Luminance | Note |
| --- | --- | --- | --- |
| `good.wood` | `#59371D` | 0.049 | Darkest; log-pile motif |
| `good.clay` | `#8F5636` | 0.128 | Δ .079 from wood ✓ (lump motif) |
| `good.apples` | `#BE3B28` | 0.142 | Only true red; leaf motif |
| `good.bread` | `#A9602F` | 0.170 | Loaf motif; warm-orange vs apples red |
| `good.pottery` | `#B25A38` | 0.171 | Amphora motif; close to bread in luminance but red-orange vs golden-orange hue, and the two never share a menu section (need vs comfort) |
| `good.fish` | `#3E7E96` | 0.181 | Only blue; safe under protan/deutan/tritan alike |
| `good.homespun` | `#A8895A` | 0.270 | Folded-cloth motif |
| `good.cider` | `#C9851F` | 0.293 | Mug motif; Δ .023 from homespun is the tightest same-family pair — mug vs cloth silhouette plus label carries it |
| `good.stone` | `#9B9B95` | 0.326 | Only neutral grey |
| `good.timber` | `#D0A05C` | 0.393 | Plank-stack motif; Δ .123 from homespun ✓ |
| `good.grain` | `#D3A944` | 0.426 | Wheat-ear motif |
| `good.wool` | `#D5CCB2` | 0.605 | Fleece-cloud motif; Δ .18 from flour ✓ |
| `good.flour` | `#F2EAD8` | 0.827 | Sack motif; lightest |

Close-pair rationale, protanopia/deuteranopia (red-green) and tritanopia (blue-yellow):

- Warm browns (wood/clay/bread/pottery/cider/timber/grain): ordered into a
  monotonic luminance ladder with ≥ 0.04 steps except bread/pottery (different hue
  families, different motifs, different menu sections) and homespun/cider (mug vs
  folded cloth). Red-green confusion darkens reds together, which preserves the
  ladder order rather than collapsing it.
- `good.fish` blue is untouched by protan/deutan confusion; under tritanopia it
  shifts greenish but remains the only cool token, and the fish motif is unique.
- `good.stone` grey has no hue to confuse; `good.wool`/`good.flour` differ by 0.22.
- Backstop for every pair: unique motif + text label on every chip (icon brief §3).

## 8. Placeholder mapping

Every current placeholder colour maps to a token. "Changed" rows need the owning
track's sign-off when the new asset lands (art track: ART-02/03; sim/content: as noted).

**`game/data/visuals/era1-slice.json` (buildings):**

| ID | Old fill | Token | Change |
| --- | --- | --- | --- |
| `founders_hall` | `#C9A86A` | `wood.timber.light` walls + `roof.shingle` roof | Split: one flat fill becomes wall + roof parts (kit) |
| `settler_house` | `#D9C49A` | `wall.wattle` + `roof.thatch.light` | Warmer thatch; walls stay cream-brown |
| `dirt_road` | `#9A7B52` | `road.dirt` (identical) | none |
| `storehouse` | `#8A6A45` | `wood.plank.mid` + `roof.shingle` | Split wall/roof |
| `market` | `#C46A4A` | `pottery.glaze` awning + `wood.beam.dark` stalls | Same hue, now on awning stripes |
| `woodcutters_hut` | `#7A5C3D` | `road.bridge`-family brown → `wood.plank.mid` walls | Slightly warmer; keep dark work-hut read |
| `sawpit` | `#8D7B5F` | `roof.shingle` walls + open saw frame `wood.beam.dark` | Sawpit becomes an open shed (brief) |
| `fishing_hut` | `#5E7E8C` | `wood.plank.mid` walls, `prop.net` nets, `water.shallow` base | Blue-grey body was water-confusion; blue moves to nets/water only |
| `sheep_farm` | `#7FA35A` | `field.pasture` (identical) | none |
| `weavers_cottage` | `#CDBF9B` | `wall.wattle` (identical) | none |
| `grain_farm` | `#D3A944` | `field.grain` (identical) | none |
| `windmill` | `#B9A37E` | `wall.wattle` walls + `prop.sail` sails | Sails go near-white for the island-zoom cross read |
| `bakery` | `#A9603F` | `brick.fired` oven + `wall.wattle` walls | Same hue, now on the oven mouth |
| `shrine` | `#D8D2C0` | `wall.plaster` (near-identical) | none |

**`game/data/visuals/goods.json` (cart loads):**

| Good | Old fill | Token | Change |
| --- | --- | --- | --- |
| `wood` | `#8A5A2B` | `good.wood #59371D` | Darkened: was colliding with `good.bread` range |
| `timber` | `#C89A5A` | `good.timber #D0A05C` | Lightened one step for the homespun gap |
| `fish` | `#6FA8B8` | `good.fish #3E7E96` | Deepened: old fill was foam-adjacent; new reads on water |
| `wool` | `#D8D0C0` | `good.wool #D5CCB2` | Darkened for the flour gap (0.22) |
| `homespun` | `#C4B49A` | `good.homespun #A8895A` | Darkened for the timber gap (0.12) |
| `grain` | `#D3A944` | `good.grain` (identical) | none |
| `flour` | `#EDE4D0` | `good.flour #F2EAD8` | Lightened: now the lightest token |
| `bread` | `#A9603F` | `good.bread #A9602F` | Slightly golden; separates from pottery by hue |
| (new) `stone` | fallback brown | `good.stone` | New token; was uncovered |
| (new) `apples` | fallback brown | `good.apples` | New token; only true red |
| (new) `cider` | fallback brown | `good.cider` | New token; mug motif carries the homespun proximity |
| (new) `clay` | fallback brown | `good.clay` | New token; lump motif |
| (new) `pottery` | fallback brown | `good.pottery` | New token; amphora motif |

**Terrain palette (`terrain_palette.tres`):** 11 of 12 entries keep their fills and
gain names; `river` changes from mud `#666147` to `terrain.river #5E93A3` (M2-29
river-banks work depends on this; flag to the M2 wave as a visuals-only change).

## 9. Seasonal shifts (M2-24 hooks)

Only these tokens move with the seasons, blended over two game days.
Everything structural (walls, roofs, stone, roads, water) is season-invariant:

- Spring: `leaf.*` lighten 8%, `field.soil` darkens 10% (wet), blossom tint overlay.
- Summer: `terrain.plains`/`leaf.light` saturate +8%; `light.key` warms 5%.
- Autumn: `terrain.plains` → `#B99A4A`, `terrain.forest` gains rust patches
  (`leaf.deep` → `#7A5A2E` on 30% of canopy), `field.grain` holds (harvest gold).
- Winter: `terrain.plains`/`hills` pale to `#A8A882`, `terrain.highlands` snow-dust
  to `#B0ACA4`, `leaf.deep` darkens 12%, `light.key` cools toward `#FFEFD0`.
