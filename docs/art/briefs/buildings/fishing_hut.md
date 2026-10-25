# Brief: Fishing Hut (`fishing_hut`)

2 × 2, coastline. 3 Settlers. Start building. → Fish.

## Silhouette

Plank hut on short stilts (0.6 m, floods), steep 45° thatch, jetty finger 1 tile
seaward (part of the building, not a road).

```
        /\
       /  \__
      | []   |___     jetty on posts to the water
      |_##_|_| |_|
        stilts
```

Plan: hut on land half, jetty + drying rack on water half.

```
+---+---+
| HUT   |
+-+ +-+ -+
|rack jetty (over water)
```

## Roof, materials, tokens

- Walls `wood.plank.mid` weathered (add 10% `ink.soft` wash), roof thatch.
- Jetty posts `wood.bark.dark` into the water; rack poles `wood.beam.dark`.
- Signature props: **hanging net triangle** (`prop.net`) + upturned coracle
  (`wood.timber.light` hull).

## Island-zoom read

Only building reaching over water: hut + pale jetty finger + green net triangle.

## States

- Working: net hung full (sagging curve), coracle upright at jetty, smoke thin.
- Idle: net furled to the rack top, coracle overturned.
- Paused: standard tarp over door.
- Construction: standard scaffold + jetty piles driven first (piles appear at 50%).

## Upgrade hook (M2-30)

None in Era I. Deep-water Fishery (Era III) is a separate larger building, not a
variant of this footprint.
