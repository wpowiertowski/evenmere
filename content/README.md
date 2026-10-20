# Authoring content

Everything in Evenmere that is not code is JSON in this folder. A new building is a JSON file and two lines of display text, with no C# ([ADR-0004](../docs/adr/0004-data-driven-content.md)). This is the short guide. The schemas in [schemas/](schemas/) are authoritative, and the [architecture plan](../docs/design/architecture.md#data-driven-content-pipeline) explains the design.

## Layout and files

```text
content/
├── goods/  buildings/  recipes/  tiers/  techs/  civics/  traditions/
│   festivals/  greatworks/  monuments/  happenings/  eras/  terrains/     one item per file: <id>.json
├── rules/core.json      game-wide tunables; the only file in rules/, and it has no "id"
├── locale/en/*.json     display text, in any file names you like
├── maps/<id>/           hand-made maps
└── schemas/             one JSON Schema per type, plus common.schema.json
```

- **One item per file**, directly in its type's folder, named `<id>.json`. The `"id"` inside must equal the file name.
- Files are strict JSON: UTF-8 without a byte order mark, no comments, no trailing commas, no duplicate keys. An unknown property is an error, so a typo is never silently ignored.
- A content pack (mod) is another folder with the same layout. It loads after the base game, may add items and replace whole items by ID, and cannot delete any.

## IDs and era numbers

- An ID is `snake_case`: lowercase letters and digits in groups joined by single underscores, starting with a letter, at most 64 characters (`bakery`, `town_charter`, `hops2`). The same grammar applies to tags and style keys. `coin` is reserved (see Quantities).
- An `"era"` is the era's number: its `"order"` in `content/eras/`, so Founding is `1`. Every era after the first names a tech gateway and a civic gateway of the era before it in `"requires"`. A node with `"gateway": true` is the gateway into exactly one later era and belongs to the era just before it. No tech or civic may require a node of a later era.

## Unlocks have one source

Buildings never name their own tech. The tech, civic or monument that opens something lists it in its `effects`, and things available from the first minute set `"start": true`. Each building, tier, tradition and festival needs **exactly one** source, whichever it is: zero or two is lint error EVL005.

| Item | Unlocked by the effect | Or |
| --- | --- | --- |
| Building | `{ "unlockBuilding": "bakery" }` | `"start": true` |
| Tier | `{ "unlockTier": "townsfolk" }` | `"start": true` |
| Tradition | `{ "unlockTradition": "harvest_festival" }` | `"start": true` |
| Festival | `{ "unlockFestival": "harvest_fair" }` | `"start": true` |

A **recipe** that a building names in its `"recipe"` comes with that building and needs nothing more. A recipe that no building names needs exactly one `{ "unlockRecipe": "..." }` or `"start": true`. A tier declares its own `"era"`. It may not be earlier than the era of its unlock source (`start` is era 1), but it may be later: the gateway tech Town Charter belongs to era I and unlocks Townsfolk, an era II tier. A tier's basic needs must be producible with what its declared era has unlocked (EVL006).

## Quantities

Quantities are plain decimal numbers with at most **three decimal places** and no exponent: `6`, `1.5`, `0.025`. `1.2345` and `1e2` are errors. Goods and costs are above zero; a tech or civic `"cost"` is a whole number of points. Percentages are whole numbers, and a negative one reduces the stat.

A cost is an object of good ID to quantity. The key `coin` is **reserved**: it is coin, not a good, so `{ "timber": 6, "coin": 20 }` costs six timber and twenty coin, and no good may be called `coin`.

## Effects, conditions, site bonuses and auras

An **effect** is an object with exactly one key: unlocks (`unlockBuilding`, `unlockRecipe`, `unlockTier`, `unlockTradition`, `unlockFestival`, `unlockTerrainAccess`, `unlockFeature`), `traditionSlot`, `modifier`, and one-off actions (`grantGoods`, `grantPoints`, `revealCoastline`, `requestFestival`). A **modifier** changes a stat by a percentage, for an optional `target` (`building`, `tag`, `category`, `tier`, `good` or `discovery`; none means island-wide) and an optional `when` condition. A **condition** is also a one-key object (`count`, `produced`, `consumed`, `stock`, `population`, `season`, `era`, `all`, `any`, `not` and more) and tests the game state; `inspiration` uses one to cut a node's cost. `content/techs/milling.json`:

```json
{
  "id": "milling",
  "era": 1,
  "cost": 60,
  "requires": ["cultivation"],
  "effects": [
    { "unlockBuilding": "windmill" },
    { "modifier": { "stat": "productivity", "target": { "tag": "farm" }, "percent": 10, "when": { "season": "autumn" } } }
  ],
  "inspiration": { "when": { "all": [ { "produced": { "good": "grain", "atLeast": 30 } }, { "count": { "tag": "farm", "atLeast": 3 } } ] } }
}
```

Two bonuses live in a building rather than in effects. **`siteBonus`** applies to that building only, when it stands in the right place (`terrain` or `near` a building or tag). **`aura`** is spread over the buildings within road `coverage`, for a `target` that is never island-wide. `content/buildings/windmill.json` has a site bonus:

```json
{
  "id": "windmill",
  "category": "production",
  "era": 1,
  "footprint": [2, 2],
  "placement": { "terrain": ["plains", "hills"] },
  "cost": { "timber": 6, "coin": 10 },
  "buildDays": 2,
  "workforce": { "tier": "settlers", "count": 3 },
  "recipe": "grind_flour",
  "siteBonus": [ { "when": { "terrain": ["hills"] }, "stat": "productivity", "percent": 15 } ],
  "tags": ["processing"]
}
```

`content/buildings/bathhouse.json` has an aura:

```json
{
  "id": "bathhouse",
  "category": "service",
  "era": 2,
  "footprint": [3, 3],
  "buildDays": 4,
  "cost": { "timber": 10, "bricks": 4 },
  "aura": { "stat": "capacity", "percent": 10, "target": { "category": "residence" }, "coverage": 16 }
}
```

## Display text

Content files never contain display text. Put it in `locale/en/` (any file names; each file is a flat JSON object of key to text). The key is `<type>.<id>.name`, and `<type>.<id>.desc` for the description, where `<type>` is `good`, `building`, `recipe`, `tier`, `tech`, `civic`, `tradition`, `festival`, `greatwork`, `monument`, `happening`, `era` or `terrain`. A missing name is an error (EVL007) and a missing description a warning. A key defined twice is an error, so a content task adds its own file, such as `content/locale/en/era1-production.json`:

```json
{
  "building.windmill.name": "Windmill",
  "building.windmill.desc": "Grinds grain into flour; it works best on hills."
}
```

## Checking your work

`evenmere lint` reports every problem with the file and the JSON pointer to the value, as `EVL005 error buildings/windmill.json: building 'windmill' has no unlock source`. It must be clean before a change lands.

| Code | Checks |
| --- | --- |
| EVL001 | Each file against its JSON Schema, and the shape errors the loader finds |
| EVL002 | Every reference names an item that exists |
| EVL003 | The tech and civic trees have no cycles and every node can be reached |
| EVL004 | Every good is made by a recipe and used by something |
| EVL005 | Exactly one unlock source per building, tier, tradition and festival |
| EVL006 | Each tier's basic needs can be met with items of its era or earlier |
| EVL007 | Every item has a display name (and should have a description) |
| EVL008 | Era gateways exist and no node requires a later era |

Run it with `dotnet run --project src/Evenmere.Tools -- lint` once the command-line tool lands (task M0-07). Until then, the same linter runs in the tests: `dotnet test tests/Evenmere.Content.Tests -c Release`, or call `ContentLinter.Run(ContentLoader.Load("content"))` from C#.

To get completion and errors while you type, map the schemas in VS Code settings, for example `"json.schemas": [{ "fileMatch": ["/content/buildings/*.json"], "url": "./content/schemas/buildings.schema.json" }]`, with one entry per type folder. The schema for `rules/core.json` is `rules.schema.json`.
