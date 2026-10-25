# Brief: Orchard (`orchard`)

4 × 4, plains with apple fertility. 3 Settlers. Seasonal. → Apples.

## Silhouette

Grid of small round-canopy trees + press shed corner.

```
  o   o   o   o      canopies: leaf.orchard spheres 2 m
  |   |   |   |
  o   o   o   o
 [shed]  crates
```

Plan: 3 × 3 tree grid, shed on N edge, crate stack at gate.

## Roof, materials, tokens

- Trees: trunk `wood.bark.dark`, canopy `leaf.orchard` with `good.apples` dots
  in season (dots only at street zoom; island read is the grid texture).
- Shed: plank + shed thatch. Crates `wood.timber.light` with apple dots.

## Island-zoom read

Dark-green dotted grid. Distinct from grain gold and pasture plain green.

## States

- Working (harvest): ladders on two trees, crates full (apple dots), cart at gate.
- Idle: no ladders, crates empty-stacked.
- Paused: standard — tarp over shed (trees keep their season look).
- Construction: shed scaffold + sapling rows (thin sticks) at 40%.

## Seasons (M2-24 hook)

Spring blossom tint (paper.highlight dots), summer full canopy, autumn apple dots,
winter bare branches (`wood.bark.dark` tracery). The orchard is the season poster.

## Upgrade hook (M2-30)

None planned; yield scales via fertility and crew modifiers.
