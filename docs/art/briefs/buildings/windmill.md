# Brief: Windmill (`windmill`)

2 × 2, land (+15% on hills). 3 Settlers. Grain → Flour.

## Silhouette

Tapered round tower (stone base, wattle top) + four-sail cross facing W-SW.

```
          \ | /
           \|/          sails: lattice cross, canvas on one side
        ---------
        \  wattle /     tapered tower
        / stone  \
        |_##_|
```

Plan: tower centred, sail sweep clears the footprint (sails may overhang: allowed
exception, sweep radius 1.2 tiles).

## Roof, materials, tokens

- No pitched roof: ogee cap `roof.shingle` on the tower top. Base `stone.mid`,
  top `wall.wattle`. Sails `prop.sail` lattice with `wood.beam.dark` stocks.
- Signature props: **the sail cross itself** + grain sacks at the door.

## Island-zoom read

The island's only tall mover: white cross turning against the sky. Sails stop at
idle/paused — the state read is motion, backed by sack presence.

## States

- Working: sails turning (slow, 1 rev / 6 s), door open, sacks stacked.
- Idle: sails parked in X, door shut, one sack.
- Paused: sails parked + standard tarp over door.
- Construction: tower scaffold (round scaffold part) then cap, then sails last.

## Upgrade hook (M2-30)

Tower mill (Engineering): same tower part, taller cap + reefing stage balcony;
then steam variant (Era IV) adds a side boiler shed. Staffing shrinks per line.
