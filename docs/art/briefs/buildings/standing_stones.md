# Brief: Standing Stones (`standing_stones`)

3 × 3, monument in 3 stages. +4 culture, +5 attractiveness. Bills: 20 stone /
30 stone + 10 timber / 40 stone (content holds exact figures). Remembrance.

## Silhouette (finished)

Stone ring + capstone trilithon, grassy mound base. Ancient, not built.

```
       _______         capstone lintel
       |  |  |        three uprights, rough tops
      /|  |  |\
     / mound   \      turf mound, leaf.fen fringe
```

Plan: ring of 7 uprights centred on the trilithon, processional gap E.

## Materials, tokens

- Stones `stone.mid` with `stone.dark` weather streaks (vertical); capstone
  `stone.light` (sky-lit top). Mound `leaf.fen`. No timber, no thatch, no paint.
- Signature props: **the trilithon** + offering stones (small `pottery.glaze`
  bowls at the base, appear after completion).

## Island-zoom read

Grey teeth on a green mound. Only stone verticals without roofs.

## States

- Stage 1: outer ring uprights only (leaning stones + ropes).
- Stage 2: trilithon raised (scaffold + earthen ramp, removed after).
- Stage 3 (done): capstone set, bowls appear, garlands at festivals.
- Never paused (monument sites follow the monument path, not the pause flag).

## Upgrade hook (M2-30)

N/A — monuments stage, they don't upgrade. Later wonders reuse the staging
grammar (M2-10 stage visuals): scaffold-then-stone-then-dressing.
