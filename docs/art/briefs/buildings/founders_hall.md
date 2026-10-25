# Brief: Founders' Hall (`founders_hall`)

4 × 4, pre-placed, unique. Storehouse (reach 24, 3 carts) + market coverage 16.
No workforce. Start building.

## Silhouette

The island's anchor: long hall, high 45° thatch, carved gable posts, banner poles.

```
          /--------\
         /  thatch  \      ridge 6 m over floor
         |  ||  ||  |      tall doors + banner poles
         |__##__##__|      stone footing + steps
      o--          --o      two banner poles
```

Plan: hall 4 × 2 centre, forecourt S with banners and muster bell.

```
+---+---+---+---+
|     HALL      |
+---+---+---+---+
| forecourt o bell o |
```

## Roof, materials, tokens

- Walls plank `wood.plank.mid` with carved post caps; roof deep thatch with
  `roof.thatch.shadow` ridge cap; footing stone + 2 steps.
- Signature props: **twin banner poles** (`ui.accent` pennants) + muster bell
  (bronze-ish `field.grain` dark? use `wood.timber.light` frame + `stone.dark`
  bell) + cart dock on E face.

## Island-zoom read

Largest thatch + twin pennants. The island's front door; visible from the landing.

## States

- Working: banners flying, bell frame manned, carts at dock.
- Idle: never idle (always staffed by role); quiet = banners furled in storms.
- Paused: cannot pause (unique civic); no paused state.
- Construction: pre-placed; no site state. Upgrade to Town Hall (Era II) reuses
  this silhouette with brick and tile (later brief).

## Upgrade hook

Town Hall (Era II, later brief): same 4 × 4, brick walls + tile roof + clock
gable; reuses hall massing and forecourt parts.
