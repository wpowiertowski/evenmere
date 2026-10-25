# Brief: Cider Press (`cider_press`)

2 × 2, land. 4 Villagers. Apples → Cider.

Note: the only Era I production building staffed above Settlers (comfort good for
Villagers). Its look must bridge cottage and workshop: tidier than a hut, humbler
than a tavern.

## Silhouette

Longhouse with a press-house gable + barrel store.

```
       /----\
       | ~~ |        press beam visible in open gable
       |[door] barrels |||
       |_##_|
```

Plan: press room W, barrel store E, apple heap S.

```
+---+---+
|PRESS  |
+---+---+
|heap|bar|
+---+---+
```

## Roof, materials, tokens

- Walls `wall.wattle` with `wood.beam.dark` frame showing (half-timber read);
  roof thatch 45° with a slightly flared eave.
- Signature props: **screw-press** (beam + screw + basket) visible in the open
  gable + barrel rows (`wood.timber.light` with `ink.soft` hoops) + apple heap.

## Island-zoom read

Half-timbered gable + barrel dots. Warmer and busier than a cottage.

## States

- Working: press beam down, juice trough shine, full barrels.
- Idle: beam up, empty barrels racked.
- Paused: standard tarp over the gable opening.
- Construction: standard scaffold.

## Upgrade hook (M2-30)

None in Era I. Era II brewery is a separate building.
