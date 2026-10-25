# Brief: Scout's Lodge (`scouts_lodge`)

2 × 2, land. 2 Settlers. One expedition at a time.

## Silhouette

Tall look-out: cabin + open viewing platform with rail, small mast + pennant.

```
           o-- pennant (ui.link)
        ___|___
       | rail  |      viewing deck at 4 m
       | cabin |
       |_##_|
```

Plan: cabin N, woodpile + canoe rack S.

## Roof, materials, tokens

- Cabin plank + shed thatch; deck `wood.plank.mid` on `wood.beam.dark` posts;
  rail two bars. Mast pennant `ui.link`.
- Signature props: **canoe** (`wood.timber.light` hull on rack) + coiled rope
  (`prop.rope`) + far-seeing scope on the rail (street zoom).

## Island-zoom read

The only deck-on-posts + the blue pennant. Pennant flies while an expedition
is out — the expedition state reads at island zoom.

## States

- Working (expedition out): pennant flying, canoe gone, brazier lit.
- Idle: pennant lowered, canoe racked.
- Paused: standard tarp over cabin door (deck stays).
- Construction: standard scaffold (posts double as scaffold).

## Upgrade hook (M2-30)

None planned.
