# Brief: Storehouse (`storehouse`) and Stone Storehouse (`stone_storehouse`)

2 × 2. Reach 20, 2 carts (stone: reach 26, 3 carts). 2 Settlers. Cost 4 timber
(stone variant: 4 stone, Masonry — M2-30 owns the content, this brief covers both).

## Silhouette

Broad shingle gable, big double doors on the long face, loading dock.

```
       /------\
       | xxxx |       double doors with cross braces
       |_[dd]_|       dock platform + crates
       crates ->
```

Plan: store 2 × 1.5 N, dock + crate rows S.

## Roof, materials, tokens

- Walls `wood.plank.mid`, roof `roof.shingle` 40° (the only shingle roof besides
  the hall — the logistics read). Doors `wood.beam.dark` with pale cross braces.
- Stone variant: same massing, walls `stone.mid`, quoins `stone.light`,
  roof unchanged (kit material swap, same silhouette).
- Signature props: **crate rows** (`wood.timber.light` cubes) + hand cart.

## Island-zoom read

Wide grey-brown roof + pale crate dots at the dock. Stone variant reads greyer.

## States

- Working: doors open, cart at dock, crates stacked 2-high.
- Idle: doors shut, one crate.
- Paused: standard tarp over the double doors.
- Construction: standard scaffold; stone variant shows stone courses rising.

## Upgrade hook (M2-30)

`stone_storehouse` is the first upgrade row: same footprint, wall-material swap
only. The kit must implement this as a part swap to prove the M2-30 path.
