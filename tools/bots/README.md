# Bot scripts

A bot script is a JSON build order the balance bot (`evenmere balance`) plays using
only commands and queries, the way a player uses the UI. The bot never touches
`WorldState` or internal types.

Run it from the repository root:

```sh
evenmere balance --map starter --bot tools/bots/slice.json --seed 1 --max-minutes 90
```

Game minutes: 1 minute is 240 ticks at 4 ticks a second. Reaching or missing the
script's target is reported, not an error; only an invalid script exits non-zero.

## Format

```json
{
  "name": "slice",
  "goals": [
    { "research": "cultivation", "queue": ["milling", "weaving"] },
    { "civic": "hearth_and_shrine" },
    { "build": "market", "count": 1, "near": "founders_hall" },
    { "build": "settler_house", "count": 8, "near": "market" },
    { "lockHouses": 7 },
    { "waitUntilTick": 3000 },
    { "build": "settler_house", "count": 19, "near": "market" },
    { "housesUntil": { "tier": "villagers", "residents": 100 }, "timeoutMinutes": 60 }
  ]
}
```

Goals run in order. Blocking goals (`build`, `lockHouses`, `unlockHouses`,
`waitUntilTick`, `ensureSupply`, `housesUntil`) each hold until satisfied or their
timeout passes (`timeoutMinutes`, default 30), then the bot moves on. `research`
and `civic` only set their target and queue and then track completion in the
background while later goals proceed: knowledge and culture accrue over days, and
blocking every building on a 15-day tech would idle the island (the reference
build order researches and builds in parallel). A later `build` that needs the
tech simply waits for its unlock, so nothing is built early.

| Goal | Meaning |
| --- | --- |
| `{ "build": "<type>", "count": <n>, "near": "<anchor>" }` | Ensure at least `n` buildings of `<type>` exist (sites count). `count` defaults to 1, `near` to `founders_hall`. The bot spirals outward from the lowest-ID building of the anchor type, validates each candidate (with rotations) through `IPlacementQueries.Validate`, and connects the winner to the road network with a BFS road planner over free, buildable tiles (validated through `ValidateRoad`). It prefers no errors and no warnings, then no errors without a `LowYield` warning (road warnings clear once the bot connects the building; a poor yield lasts forever), then any placeable spot. It waits while the type is still locked by research or culture. Service buildings (market, shrine) are scored by the houses they would cover over today's roads. |
| `{ "research": "<tech>", "queue": [...] }` | Set the research target (and queue); done when the tech is researched. |
| `{ "civic": "<civic>", "queue": [...] }` | Set the civic target (and queue); done when it is adopted. |
| `{ "ensureSupply": "<good>", "targetFulfilment": <0-1] }` | Wait until the good's fulfilment reaches the target (1.0 by default). The target may also be written as an object: `{ "ensureSupply": { "good": "bread", "targetFulfilment": 1.0 } }`. |
| `{ "housesUntil": { "tier": "<tier>", "residents": <n> } }` | Build houses (the cheapest unlocked residence, near the market when one is finished) until `n` residents live in `<tier>` houses, then wait for growth. No more than `ceil(n / capacity) + locked + 4` houses are kept: enough roofs for the target, the locked workforce reserve, and a growth reserve so filling overlaps the next supply chain coming online. Every house is coverage-checked once it lands: it must lie in market coverage, and in shrine coverage once a shrine is finished, or it is demolished and the spot is skipped. |
| `{ "lockHouses": <n> }` | Lock the `n` oldest houses against advancing, so they stay in their tier and keep working. Waits until `n` houses exist. Locking as they complete beats the three-day advance streak, so a house locked early never dips when its next tier's needs are missing. |
| `{ "unlockHouses": <n> }` | Unlock the `n` newest locked houses that are already covered (market, and shrine once a shrine is finished), so advancing never drops occupancy for missing coverage. The oldest locked houses (the workforce reserve) stay locked. Waits until `n` covered houses can be unlocked. |
| `{ "waitUntilTick": <tick> }` | Wait until the tick is reached, for timing a later goal (like a wave of houses) to land while its supply chain comes online. |

Any unknown key is an error naming the goal index, as is a goal that names none
(or two) of the kinds above. Every name must exist in the loaded content
(checked before the run starts).

## Reactive rules

While the goals run, the bot also plays the janitor, once per game day:

- When a good with demand stays below 90% fulfilment for 2 consecutive days (or
  sits at zero stock with demand), the bot asks `IExplainQueries.ExplainGood`
  for the bottleneck (the first other good the explanation names, or the short
  good itself) and builds one producer of it. When every active producer of the
  bottleneck stalls on missing input, it builds the input's producer instead
  (bread → flour → windmill). A half-built producer of the same type blocks a
  duplicate, and each good cools down for 5 days after the bot answers it.
- When an advisor reports `NoRoadAccess` or `OutsideStorehouseReach` for a
  building, the bot lays a road to the network; when reach persists for a
  connected building, it adds a storehouse nearby. Each note cools down for
  5 days.
- When a finished house lacks market or shrine coverage its tier needs, the bot
  builds the cheapest covering service nearby and verifies the coverage once it
  completes (removing and banning the spot on a miss).

## Houses

Houses go up in compact blocks along roads, near the market first. Every placed
house is coverage-checked through `IRoadsQueries.IsCovered` (market always, and
shrine once a shrine is finished); an uncovered house is demolished and its
spot is banned, so the bot never guesses coverage.

## Report

Stdout is the JSON report: a `deterministic` section (byte-identical for one seed
and script) and a `timings` section with the wall-clock milliseconds per tick.
Stderr carries the same facts as text. The report holds the time to the first,
the 50th and the 100th Villager in ticks and game minutes, stalls (no resident
growth for a full season, each with the top advice then), commands issued and
rejected by type, final residents per tier, final stock, peak and average tick
time, and the final state hash.
