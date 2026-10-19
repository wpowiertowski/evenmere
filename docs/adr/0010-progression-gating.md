# 0010. Dual era gateways, alternating tier unlocks, decline always on

Status: Accepted · Date: 2026-10-18

## Context

Pillar 3 says culture is a second path, not a side stat. If eras and tiers depended only on technology, the culture tree would be optional flavour. The developer also chose that houses can always decline when needs go unmet.

## Decision

- **Eras:**
  - There are four: Founding, Township, Enlightenment and Industry.
  - Era N+1 begins when **both** the tech gateway and the civic gateway of era N are complete:

    | Into | Tech gateway | Civic gateway |
    | --- | --- | --- |
    | Era II | Town Charter | Village Moot |
    | Era III | Natural Philosophy | Humanism |
    | Era IV | Steam Power | The Public Sphere |

  - Nodes of era N+1 in either tree need era N+1 to have begun.
- **Tier unlocks alternate between the trees:**

  | Tier | Unlocked by |
  | --- | --- |
  | Settlers | start |
  | Villagers | start (needs the Shrine from Hearth & Shrine) |
  | Townsfolk | Town Charter (tech) |
  | Artisans | Guilds (civic) |
  | Scholars | Universities (tech) |
  | Patrons | Patronage (civic) |

- **Decline is always on.** If a basic need that the house has had met at its current tier stays below 50% for a full season (10 days), the house steps down one tier.
  - A need never yet met at this tier only holds occupancy down, so a newly advanced house cannot bounce straight back.
  - Decline is never below Settlers, never empties a house, and needs a fresh grace period before it can repeat.
  - Festivals pause grace timers island-wide while they run.
  - The tradition *Common Voice* and the Belfry monument extend the grace period.
- **Inspirations** cut a node's cost by 40% when their condition is met, in both trees.
- **Staffing rule:** a tier's basic goods and services are produced and staffed by lower tiers. Lint rule EVL010 (added in M2) checks this.

## Consequences

- A player who ignores either tree stalls at the next era, which needs a clear advisor message: "Era II needs Village Moot (culture)".
- The balance bot must invest in both trees. Its pacing targets are in the [progression ladders](../design/progression.md#pacing-targets).
- Lint rule EVL008 enforces exactly one gateway per tree per era transition.
