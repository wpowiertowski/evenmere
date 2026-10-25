# Brief: Dirt Road (`dirt_road`) and Cobbled Road (`cobbled_road`)

1 × 1 segments, free (dirt) / 1 stone (cobbled). No workforce.

## Silhouette

Ground treatment, not geometry: flat ribbon 0.8 tiles wide with soft 1 px
`ink.soft` edges (style guide §2). Slight crown (0.05 tile rise) for the light band.

```
 ~ ~ ~ ~ ~ ~ ~      dirt: two wheel ruts (darker dashes)
 =#.=#.=#.=#.=      cobbled: sett grid (offset rows)
```

## Materials, tokens

- Dirt: `road.dirt` with rut dashes `field.soil`; edges feather into the biome.
- Cobbled: `road.cobble` setts with `stone.dark` joints, 0.4 m grid.
- Junctions: dirt blends (no kerb); cobbled gets a `stone.mid` kerb line on Town
  Hall / plaza frontages only.

## Island-zoom read

Dirt = pale thread through grass; cobbled = grey thread. Roads never out-read roofs.

## States

- No working/idle/paused. Construction: half-tone (50% desaturated) ribbon for
  the build day, then full colour. No scaffold.
- Bridges (timber_bridge, next brief) are separate buildings with piles and rails.

## Upgrade hook (M2-30)

Dirt → cobbled is a rebuild, not a variant; the renderer cross-fades the ribbon
treatment. Rail (Era IV) is a new treatment on the same ribbon part.
