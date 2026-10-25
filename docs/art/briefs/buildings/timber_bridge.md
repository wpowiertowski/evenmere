# Brief: Timber Bridge (`timber_bridge`)

1 × (2–5), road across rivers ≤ 3 wide. 2 timber per tile. Bridge-building.

## Silhouette

Low arc deck on pile pairs + simple rails. Must read as bridge, not road, from
the default tilt: rails + piles below deck level.

```
  |¯¯¯¯¯deck¯¯¯¯¯|      rails 0.9 m, posts every tile
  | | piles | | |      pile pairs into water
  ~~~water~~~
```

Plan: straight run only (BridgeNotStraight rejected); deck 0.8 tiles wide.

## Materials, tokens

- Deck `road.bridge`, rails + piles `wood.bark.dark`, rope lashings `prop.rope`.
- Abutments: `stone.mid` one course at each bank (even the timber bridge gets
  stone feet — permanence read).

## Island-zoom read

Brown thread with rails across blue. Rails are the read; a road ribbon without
rails over water is a bug.

## States

- No working/idle/paused. Construction: piles first (rise from water at 30%),
  then deck planks tile by tile from the near bank.
- Stone Bridge (Era II, later brief): same grammar, stone arches instead of piles.

## Upgrade hook (M2-30)

Not an upgrade line (separate content); the kit reuses deck/rail parts with stone
arch parts for the Era II bridge.
