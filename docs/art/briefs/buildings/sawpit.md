# Brief: Sawpit (`sawpit`)

2 × 2, land. 3 Settlers. Start building. Wood → Timber.

## Silhouette

An **open shed**, not a closed hut: four posts, low thatch roof, the pit visible.

```
    ___________
   /  thatch   \      posts at corners, no walls
   | |  pit  | |
   |_|_____|_|      footing frame only
   plank stack ->
```

Plan: saw frame (two trestles + log) on W half, plank stacks on E half.

```
+---+---+
|Saw|plk|
|frm|stk|
+---+---+
```

## Roof, materials, tokens

- Shed thatch roof 40°, posts `wood.beam.dark`, stacks `wood.timber.light`.
- Signature props: **trestle saw frame** (X-legs + long log) + leaning plank stacks.

## Island-zoom read

The only roofless work building: posts + pale plank stacks. Reads pale buff.

## States

- Working: log on trestles, sawdust ring (`field.fallow` dust decal), no smoke.
- Idle: trestles empty, planks stacked square and tarp-cornered.
- Paused: standard (tarp over trestle).
- Construction: standard scaffold (posts double as scaffold).

## Upgrade hook (M2-30)

Water-powered sawmill (Era II): same footprint, adds a side waterwheel
(`wood.beam.dark` wheel, 3 m diameter) on the E face reusing post parts; staffing
moves to 2 Townsfolk.
