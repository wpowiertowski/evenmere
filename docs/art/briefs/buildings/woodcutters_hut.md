# Brief: Woodcutter's Hut (`woodcutters_hut`)

2 × 2, land; yield from forest within 4 tiles. 3 Settlers. Start building.

## Silhouette

Side (8 m wide, wall 2.6 m, thatch 45°):

```
        /\
       /  \        ridge along long axis
      /----\
      | [] |        one shuttered window, plank door
      |_##_|        dry-stone footing 0.5 m
```

Plan (tiles, N up): hut on N half, log piles on S half.

```
+---+---+
| HUT   |
+---+---+
|log|log|
+---+---+
```

## Roof, materials, tokens

- Open-thatch gable, 45°, overhang 0.4 m: `roof.thatch.light` / `roof.thatch.shadow`.
- Walls plank: `wood.plank.mid`; corner posts `wood.beam.dark`; footing `stone.mid`.
- Signature props: **two crossed-log piles** (`wood.bark.dark` ends, end-grain rings
  drawn as circles) + one stump with a wedged axe.

## Island-zoom read

Dark work-hut + pale log-pile ends. The log piles are the read, not the hut.

## States

- Working: axe in stump, fresh pale log ends on top of piles, faint smoke (brazier).
- Idle: axe gone, piles tarp-covered (`prop.tarp` corner draped).
- Paused: standard tarp over door (style guide §8).
- Construction: standard scaffold + one starter log pile.

## Upgrade hook (M2-30)

No Era I upgrade planned; a later charcoal-era variant would add a kiln mound
beside the hut reusing the log-pile part.
