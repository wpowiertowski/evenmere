# Brief: Grain Farm (`grain_farm`)

5 × 5, plains with grain fertility. 4 Settlers. Seasonal. → Grain.

## Silhouette

Field + farmhouse corner: house 2 × 2 in one corner, the rest crop rows.

```
+-----+-----+-----+-----+-----+
|HOUSE      | row row row     |
+           +-----+-----+-----+
| row row row row row         |
| ROW ROW ROW ROW ROW (gold)  |
```

Plan: house at N corner facing the road; rows run E–W; one fallow strip rotates
(seasonal storytelling: winter rows show as soil).

## Roof, materials, tokens

- House: wattle + thatch 45°. Rows alternate `field.grain` / `field.soil` stripes
  0.5 tile wide. Fences on two sides only (open-field look).
- Signature props: **stook stacks** (3 upright sheaf cones) + farm cart.

## Island-zoom read

Biggest gold square on the island. Rows must band, not blur, at island zoom.

## States

- Working: stooks standing, cart at the gate, scarecrow up (spring/summer).
- Idle (winter): rows soil-only, stooks gone, gate shut.
- Paused: standard — tarp over the farmhouse door (field keeps growing visually).
- Construction: house scaffold + first row stripes appear at 50%.

## Seasons (M2-24 hook)

`field.grain` → green shoots (spring), gold (summer/autumn), `field.soil` (winter).
House is season-invariant.

## Upgrade hook (M2-30)

Rotation farm (Crop Rotation, Era II): same footprint, adds a second crop stripe
colour (clover green) and a stone trough; staffing 3 Villagers.
