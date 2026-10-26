# Architecture decision records

Each file records one decision: its context, the decision itself and its consequences. An accepted ADR is changed only by a new ADR that supersedes it.

**When to write one:** before adding a public interface, before changing a frozen contract (`ISimulation`, `Command`, `SimEvent`, `ContentDb`, the save format), before adding a dependency to `Evenmere.Core`, `Evenmere.Content` or `Evenmere.Sim`, or before departing from the [architecture plan](../design/architecture.md).

| # | Decision | Status |
| --- | --- | --- |
| [0001](0001-engine-and-language.md) | Godot 4.7 .NET with C#, and an engine-free simulation | Accepted |
| [0002](0002-project-layering.md) | Project layering and boundary enforcement | Accepted |
| [0003](0003-deterministic-simulation.md) | Deterministic fixed-tick simulation | Accepted |
| [0004](0004-data-driven-content.md) | Data-driven content and ContentDb | Accepted |
| [0005](0005-logistics-model.md) | Island storage pool fed by real transport jobs | Accepted |
| [0006](0006-simhost-threading.md) | SimHost threading: main thread first, snapshot worker later | Accepted |
| [0007](0007-save-format.md) | Save format, ID remapping and migrations | Accepted |
| [0008](0008-platform-targets.md) | macOS and Linux/Steam Deck first, Windows last | Accepted |
| [0009](0009-economy-staging.md) | Goods-only Era I; coin introduced by Coinage | Accepted |
| [0010](0010-progression-gating.md) | Dual era gateways, alternating tier unlocks, decline always on | Accepted |
| [0011](0011-m1-simulation-contract.md) | The M1 simulation contract | Accepted |
| [0012](0012-cheat-commands.md) | Cheat commands and the cheats flag (M2-26) | Accepted |
| [0014](0014-workforce-hold.md) | Workforce hold on house advancement | Accepted |
| [0013](0013-art-direction.md) | Art direction: hand-drawn isometric, through a non-photorealistic pass over low-poly 3D | Accepted |
| [0015](0015-crew-stat.md) | Crew stat: modifiers that reduce a building's required workers | Proposed (M2-30) |
| [0016](0016-workforce-stepping-down-and-morale.md) | Workforce stepping down, morale, a stricter hold and slower advancing | Accepted |
| [0017](0017-m2-simulation-contract.md) | M2 simulation contract addendum: queries, services, commands and events | Accepted |
| [0018](0018-save-format-version-2.md) | Save format version 2: M2 state and the migration framework in use | Accepted |
| [0019](0019-generated-islands.md) | Generated islands: the generator contract and MapDef discoveries | Accepted |

## Template

Copy this into `NNNN-short-title.md`, using the next free number.

```markdown
# NNNN. Title

Status: Proposed | Accepted | Superseded by NNNN · Date: YYYY-MM-DD

## Context
What forces are at play? What problem does this solve?

## Decision
What we will do, stated plainly.

## Consequences
What becomes easier, what becomes harder, and what must now be true (tests, lint rules, docs).
```
