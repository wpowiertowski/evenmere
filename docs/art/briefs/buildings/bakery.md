# Brief: Bakery (`bakery`)

2 × 2, land. 4 Settlers. Flour → Bread.

## Silhouette

Cottage + external beehive oven bulge on one face, tall chimney.

```
        /\
       /--\  __       chimney with smoke = working
      | [] | |  |
      |(oven)| |  |   round oven bulge, dark mouth
      |_##_|_|__|
```

Plan: cottage N, oven bulge E, log store S.

```
+---+---+
|COTTAGE|
+---+---+
|logs oven>
+---+---+
```

## Roof, materials, tokens

- Walls `wall.wattle`, roof thatch 45°. Oven `brick.fired` dome with
  `stone.dark` mouth. Chimney `brick.fired` stack 1.2 m over ridge.
- Signature props: **oven mouth glow** (warm dot at street zoom) + bread racks
  (shelves with loaf rows) beside the door.

## Island-zoom read

Chimney smoke + dark oven bulge on a cream cottage. Smoke is the working read.

## States

- Working: smoke, oven mouth lit, racks full.
- Idle: no smoke, racks empty, mouth dark.
- Paused: standard tarp over door (oven stays, covered mouth).
- Construction: standard scaffold; oven dome built at 60%.

## Upgrade hook (M2-30)

No Era I upgrade; Era II retains the same building (bread stays Settler-made per
the staffing rule). A cosmetic tile-roof swap may come with the Era II kit.
