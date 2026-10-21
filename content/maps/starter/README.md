# Starter island

The hand-authored 128 x 128 map for the M1 vertical slice (task M1-03). The
Founders' Hall lands on the plains of the **Landing** in the south-west, a
peninsula bounded by a narrow river in the east and a wide river in the north.
The peninsula holds the whole Era I start: a forest belt, grain plains, sheep
pasture, coastline for fishing huts, hills with stone, and clay riverbanks.
Beyond the rivers lie the marshlands, the ironhills, the highmoor interior with
coal and copper, the crown peaks, and an offshore reef for a later age.

## Preview (64 x 32, one character per 2 x 4 tiles)

```text
~wwwwwwwwwww~~~~~~~~~~~____~~~~~~~~~~~~~~~~wwwwwwwwwwwwwwwwwwww~
~wwwwwwwwww~~~_________,,,,,,,,,,,,,,__~~~~~wwwwwwwwwwwwwwwwwww~
~wwwwwww~~~~~__.............,,,,,,,,,.___~~~~~wwwwwwwwwwwwwwwww~
~wwww~~~~~~~~_,,,,.TTTT...........TTTTT______~~~wwwwwwwwwwwwwww~
~ww~~~~~~~____,,,,TTTTT...^^^^^^..TTTTT....,,_~~~~~~wwwwwwwwwww~
~~~~~~_,,,_...TTTTTTTTT^^^TTT^^^^^^^T^....,,,___~~~~~wwwwwwwwww~
~~~~~__,,.....TTTTTTT^^^^^TTMMMMMMMMMMMMMM....___~~~~~~wwwwwwww~
~~___.TTT....^^^^^^^^^^^MMMMMMMMMMMMMMMMMMMMM...__~~~~~~wwwwwww~
~~_..TTTTT.^^^^^^^^^^^MMMMMMMMAAAAAAAMMMMMMMMM...___~~~~~wwwwww~
~__..T....^^^^^^^^TTTMMMMMMMMAAAAAAAAMMMMMMMMMTTTTT.__~~~wwwwww~
~_..TT...^^^^^^^^^TTTMMMMMMMMMAAAAAAMMMMMMMMMMTTTTT,,,~~~~wwwww~
~_.TTT..^^^^^^^TTT^^^^MMMMMMMMMMMMMMMMMMMMMMMM....,,,_~~~~wwwww~
~_TTTTT.^^^^^^^TTT^^^^^MMMMMMMMMMMMMMMMMMMM^^^^^..,,,_~~~~~wwww~
~.TTTTTTT^^^^^^TTT^^^^^^^^MMMMMMMMMMMMMM^^^TTT^^^^.,,,_~~~owwww~
~.TTTTTTTT^^^^TTTTT^^^^^^^MMMMMTTTTTT^^^^^^TT^^^^^^^.._~~~~owww~
~.TTTTTTTTTTTTTTTTTTTTT^MMMMMMMTTT^^^^^^^^^^^^^^^^^^^__~~~~wwww~
~_TTTTTTTTTTTTTTTTTTTTT^MMMMMMMTTT^^^^^^^^^^^^TTTTT..__~~~~wwww~
~.....TTTT^=======TTTT^^MMMMMMM^^^^^^^^^^^^^^^^.....__~~~~wwwww~
~=======================M^MMM^^^^^^^^^^^^^^^^^^.....__~~~~wwwww~
~==========^^^^^^^======TT^^=^^^^^^^^^^^^^^^TTTTTT.,_~~~~~wwwww~
~~~_,^TTTT.........TTTTTTT^^==^^^^^^^^^^^^TTTTTTT..,_~~~~wwwwww~
~~TTTT^............TTT....TT==.......^TTTTTTTTTTT.__~~~~wwwwwww~
~__TTT.............TTTTTTTTT==........TTTTTTTTTT.__~~~wwwwwwwww~
~_.TT...T.........TTTTTTTTTT==.....,,,,,,,,TT...__~~~wwwwwwwwww~
~__...TTTT....T..TTTTTTTTT..==....,,,,,,,,,,,,___~~~wwwwwwwwwww~
~w___TTTT..TTTTTTTT..T^TTT..==....TT,,,,,,,,,~~~~~~wwwwwwwwwwww~
~ww~~_TTTTTTTTTTTT.....TT^^,==..__.T,,,,,,,~~~~~~wwwwwwwwwwwwww~
~wwwwo_.TTTTTTT....TTT..__,,==,,________~~~wwwwwwwwwwwwwwwwwwww~
~wwwwwwwooo___T...TTTT.~~~_,=,_~~~~~~~~~~wwwwwwwwwwwwwwwwwwwwww~
~wwwwwwwwwowww___w_ww~~~~~~~~wwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwww~
~wwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwww~
~wwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwww~
```

## Legend

| Character | Terrain | Character | Terrain |
| --- | --- | --- | --- |
| `.` | plains | `=` | river (see `rivers.txt` for narrow/wide) |
| `T` | forest | `~` | sea |
| `^` | hills | `w` | deep water |
| `_` | beach | `o` | reef |
| `,` | marsh | `M` | highlands |
| `f` | fen | `A` | peaks |

Deposits (`deposits.txt`): `s` stone, `c` clay, `i` iron, `k` coal, `u` copper.
Fertility (`fertility-<crop>.txt`): `#` fertile, `.` not. Layers exist for
grain, apples, flax, hops and grapes.

## Regions (`regions.txt`, names in `content/locale/en/maps-starter.json`)

| ID | Key | Ground |
| --- | --- | --- |
| 0 | `region.starter.landing` | The start peninsula |
| 1 | `region.starter.westford` | Shore north of the wide river |
| 2 | `region.starter.northreach` | Northern headlands |
| 3 | `region.starter.highmoor` | Highland interior |
| 4 | `region.starter.crown` | Peak crown |
| 5 | `region.starter.ironhills` | Eastern iron hills |
| 6 | `region.starter.farshore` | Far eastern shore |
| 7 | `region.starter.eastvale` | Vale beyond the narrow river |
| 8 | `region.starter.marshlands` | Marsh basin |
| 9 | `region.starter.shoals` | Southern shoals and reef |

## Files

`map.json` (128 x 128, start site, start stock of 40 timber, 20 fish and
10 wood), `terrain.txt`, `elevation.txt` (hex `0`-`F`), `regions.txt`
(base-36), `deposits.txt`, `rivers.txt` (`.` none, `1` narrow, `2` wide) and
one `fertility-<crop>.txt` per crop above. The map loads through
`MapLoader.Load` with zero diagnostics; the guarantees are pinned by the tests
in `tests/Evenmere.Sim.Tests/StarterMap/`.
