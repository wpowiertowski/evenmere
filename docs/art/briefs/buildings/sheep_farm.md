# Brief: Sheep Farm (`sheep_farm`)

4 × 4, plains. 3 Settlers. → Wool.

## Silhouette

No big building: 1 × 2 shelter shed + fenced pasture. Fence is the silhouette.

```
. . . . . . . . .      fence posts 1.1 m, two rails
.  shelter\/      .     small gable shed in one corner
.   ~~~ sheep ~~~ .     sheep: white blobs, ink dot heads
```

Plan (4 × 4): shed in N corner, fold yard, gate on road side.

```
+---+---+---+---+
|Shed  |   yard  |
+  +---+---+   -gate-
|   pasture       |
+---+---+---+---+
```

## Roof, materials, tokens

- Shed: plank walls, thatch 45°. Fences `wood.beam.dark` posts + `wood.plank.mid`
  rails. Ground `field.pasture` with worn `field.fallow` patches at gate.
- Signature props: **sheep** (5–7, `wall.plaster` bodies) + hayrick (cone thatch).

## Island-zoom read

Green pasture square with white dots. Fence corners must close visually at zoom.

## States

- Working: sheep spread grazing, gate open, shepherd's crook at gate.
- Idle: sheep bunched at shed, gate closed.
- Paused: standard — tarp over the shed door (pasture stays).
- Construction: fence first (appears at 30%), then shed scaffold.

## Upgrade hook (M2-30)

Rotation-farm pattern (Era II): same fence part set, adds a second paddock divider
and a stone trough; staffing 3 Villagers.
