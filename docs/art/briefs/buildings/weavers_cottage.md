# Brief: Weaver's Cottage (`weavers_cottage`)

2 × 2, land. 4 Settlers. Wool → Homespun.

## Silhouette

Tidy wattle cottage, thatch 45°, one window with shutters + flower box.

```
        /\
       /--\
       |~|~|        shutters + flower box (red dots)
       | [door]|
       |_##_|
```

Plan: cottage fills N 2 × 1.5, dye garden strip on S edge.

```
+---+---+
| COTTAGE |
+---+---+
|garden strip |
+---+---+
```

## Roof, materials, tokens

- Walls `wall.wattle`, corner weave texture (horizontal strokes), roof thatch.
- Signature props: **dye garden** (3 rows `leaf.fen` + madder dots) + loom visible
  through the open door at street zoom (two beams + warp lines).

## Island-zoom read

Cream cottage + green garden strip. The garden strip distinguishes it from houses.

## States

- Working: shutters open, dyed skeins hanging on a line (3 colour dots), smoke.
- Idle: shutters closed, line empty.
- Paused: standard tarp.
- Construction: standard scaffold.

## Upgrade hook (M2-30)

Guild weaving shop (Era II civic): same footprint, upper jetty storey added
(timber jetty part), tile-roof swap, staffing fewer Artisans.
