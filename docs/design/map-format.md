# Authored map format

How a hand-made island is stored, loaded and held in the simulation. Written for task M1-01 (map model, loader and building registry); the starter island (M1-03) is the first user. Generated islands (M2-01) skip the files and fill the same tile arrays directly.

The format is plain text so a map can be drawn in any editor, diffed in review and loaded without tooling. A map is one folder of small files, one layer per file, one character per tile.

## Where a map lives

```text
content/maps/<id>/
├── map.json                  required: size, legends, start site, start stock, region names
├── terrain.txt               required: the terrain of every tile
├── rivers.txt                required when any tile is river, otherwise optional: river width class
├── elevation.txt             optional: height of every tile, 0 to F (default: all 0)
├── regions.txt               optional: region of every tile, 0 to Z (default: all 0)
├── deposits.txt              optional: mineral deposits (default: none)
└── fertility-<crop>.txt      optional, one per crop: where the crop grows (default: nowhere)
```

- `<id>` is a content ID: `snake_case`, at most 64 characters (`starter`, `test_16`). The folder name is the map's ID; `map.json` has no `"id"`.
- Maps sit in a content root next to `goods/` and `buildings/`. A content pack may add a map. When two roots define the same map ID, the **last root that has the folder wins as a whole**; layers are never merged across roots.
- The content loader does not read `maps/`. A map is loaded **on demand by ID** with `MapLoader.Load(roots, mapId, content)`, resolved against an already loaded `ContentDb` for terrain, good and crop IDs. Every file under `maps/` still counts toward the content hash, so changing a map changes the hash saves compare.
- Any file in the folder that is not listed above is ignored with a warning, so a mistyped layer name is not silently flat.

## Text layers

All layers except `map.json` share one grid format:

- UTF-8 without a byte order mark; LF or CRLF line endings; ASCII characters only.
- **Exactly `height` rows of exactly `width` characters**, row 0 (the northern edge) first, column 0 (the western edge) first. One final line break is allowed; any other blank line is a row of the wrong length.
- No comments, no header, no spaces. Every character is a tile.
- Positions in diagnostics are 1-based: `line 5, column 32` is row 4, column 31.

| Layer | Characters | Meaning |
| --- | --- | --- |
| `terrain.txt` | the keys of `legend` | The terrain of the tile. |
| `rivers.txt` | `.` none, `1` narrow, `2` wide | River width class. A `1` or `2` is allowed only on a river tile, and every river tile must have one. |
| `elevation.txt` | `0`-`9`, `A`-`F` (uppercase) | Elevation in steps of 0.5 world units, 0 to 15. |
| `regions.txt` | `0`-`9`, `A`-`Z` (uppercase) | Region number in base 36, 0 to 35. |
| `deposits.txt` | `.` none, plus the keys of `depositLegend` | The mineral deposit under the tile. |
| `fertility-<crop>.txt` | `.` not fertile, `#` fertile | Whether the tile is fertile for that crop. |

`<crop>` is the crop's content name: `grain`, `flax`, `hops`, `apples`, `grapes`, `tea`, `madder` or `sugar_beet` (the `CropKind` members in `snake_case`).

River width is an explicit layer rather than derived from the shape of the water, because a river that meets the sea, forks or bends has no unambiguous "width", and the bridge rules (narrow up to 3 tiles, wide up to 8) are too important to guess. The author states it; the loader checks that it agrees with the terrain layer.

## `map.json`

Strict JSON like every content file: no comments, no trailing commas, no duplicate keys, and an unknown property is an error.

```json
{
  "width": 32,
  "height": 32,
  "legend": { ".": "plains", "T": "forest", "~": "sea", "=": "river" },
  "depositLegend": { "s": "stone", "c": "clay" },
  "startSite": { "x": 12, "y": 14, "rotation": 0 },
  "startStock": { "wood": 20, "timber": 10 },
  "regions": { "0": "region.test32.landing", "1": "region.test32.uplands" }
}
```

| Property | Required | Meaning |
| --- | --- | --- |
| `width`, `height` | yes | Whole numbers from 1 to 1024. |
| `legend` | no | Object: one ASCII character to a terrain ID from `content/terrains`. Omitted means the default legend below. When present it **replaces** the default entirely. |
| `depositLegend` | no | Object: one ASCII character to a `DepositKind` name (`stone`, `clay`, `iron`, `coal`, `copper`). Omitted means the default. `.` is reserved for "none". |
| `startSite` | yes | Where the Founders' Hall stands when a new game starts: `x` and `y` of the footprint's top-left tile after rotation, and `rotation` in quarter turns clockwise, 0 to 3 (default 0). |
| `startStock` | no | Object: good ID to a quantity (a decimal number above zero with at most three decimals). Goods granted to the storage pool at the start. |
| `regions` | no | Object: one base-36 character to a locale key naming that region (for example `region.starter.landing`). |

Legend characters are printable ASCII except space (`!` to `~`). Several characters may name the same terrain.

### Default legend

Used when `legend` is omitted. It covers all twelve terrains in `content/terrains`:

| Character | Terrain ID | Character | Terrain ID |
| --- | --- | --- | --- |
| `.` | `plains` | `=` | `river` |
| `T` | `forest` | `~` | `sea` |
| `^` | `hills` | `w` | `deep_water` |
| `_` | `beach` | `o` | `reef` |
| `,` | `marsh` | `M` | `highlands` |
| `f` | `fen` | `A` | `peaks` |

An entry of the default legend whose terrain the loaded content does not define is only a problem if the map uses its character. An entry of an explicit `legend` that names an undefined terrain is always an error.

### Default deposit legend

| Character | Deposit |
| --- | --- |
| `s` | `stone` |
| `c` | `clay` |
| `i` | `iron` |
| `k` | `coal` |
| `u` | `copper` |

## What the loader checks

Problems are collected, not thrown, as `ContentDiagnostic`s (`MapLoadResult.Diagnostics`), sorted by file, then position. No `MapDef` is produced if any is an error.

- **EVL001** (shape): bad JSON, an unknown property, a wrong type or range, a layer of the wrong size (rows or columns), a character outside the layer's alphabet, a river tile without a width or a width on a dry tile, a start site that leaves the map or stands on unbuildable ground, an invalid map ID.
- **EVL002** (reference): a terrain, deposit, good or crop that does not resolve, a legend that names an undefined terrain, a map ID that no root contains.

Each diagnostic names the file relative to its content root (`maps/starter/terrain.txt`). Its message starts with `line N, column M:` wherever a position exists, which is every problem inside a text layer and every problem inside `map.json` (the JSON pointer is also set). At most 20 problems are reported per file, followed by a count of the rest.

## Terrain meaning

The terrain IDs come from content, but the engine has to know a few things about some of them to derive features. The table is fixed in the format (`MapTerrainRules` in `Evenmere.Content.Maps`) and covers the terrains of the progression ladder; a terrain it does not list has no special meaning. In code the result is `MapTerrainFeatures`, one value per terrain in `MapDef.TerrainFeatures`.

| Terrain ID | Features |
| --- | --- |
| `sea` | open water |
| `deep_water` | open water, `DeepWater` |
| `reef` | open water, `Reef` |
| `river` | `River` |
| `beach` | `Beach` |
| `marsh` | `Marsh` |

Everything else (plains, forest, hills, highlands, peaks, fen) carries no flag. Whether a terrain is buildable, blocks roads or needs research comes from its content definition (`buildable`, `blocksRoads`, `access`), not from this table.

## Derived data

The simulation derives these when a map is loaded; they are never authored:

- **Slope class** of a tile: the largest elevation difference to its four neighbours that exist. 0 steps is `Flat`, 1 `Gentle`, 2 `Moderate`, 3 or more `Steep`.
- **`Coast`** feature: a tile that is not open water and not a river, and has an open-water tile among its four neighbours. Open water is `sea`, `deep_water` and `reef`.

## Start site check

The loader checks that the Founders' Hall (the building named by the engine's required IDs) fits at `startSite`: its footprint, turned by `rotation`, lies inside the map, and every tile it covers has a buildable terrain. Whether the ground suits the Hall's other placement rules (slope, road access) is Construction's business when the game starts.

## In the simulation

`MapDef` (Content) is the loaded, immutable description: flattened arrays indexed by `y * width + x`, plus the start site, start stock and region names. `MapSystem.LoadMap(MapDef)` copies it into `MapState`, which is the saved form:

| Array | Type | Content |
| --- | --- | --- |
| terrain | `byte` | `TerrainId.Value` (at most 256 terrains) |
| elevation | `byte` | 0 to 15 |
| features | `byte` | `TileFeatures` bits, including the derived `Coast` |
| river width | `byte` | `RiverWidthClass` |
| region | `ushort` | region number |
| deposit | `byte` | 0 none, otherwise `DepositKind` + 1 |
| fertility | `uint` | bit `1 << (int)CropKind` per crop |
| slope | `byte` | `SlopeClass` |
| occupancy | `int` | `EntityId.Value` of the building covering the tile, 0 for none |

There are no floats and no dictionaries; the arrays are written by MessagePack with explicit integer keys (ADR-0007), so equal maps give equal bytes and an equal state hash. The building registry (`IdTable<BuildingRecord>`) lives in the same `MapState`. A new game starts with an empty 0 × 0 map until the new-game code loads one.

The 32 × 32 chunk grid that the view rebuilds from is **not** state: the dirty marks are bookkeeping that never reaches a save or the hash. A freshly loaded map, or a map restored from a save, starts with every chunk dirty so the view builds everything once.

## Changing the format

The format is part of the content contract. A new layer or a changed alphabet needs an ADR, an update to this document and a fixture map in `tests/fixtures/maps/`. New terrain features belong at the end of `TileFeatures`, whose values are part of the format.
