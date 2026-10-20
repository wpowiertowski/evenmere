# Evenmere

A calm, single-player island city builder. You grow a settlement through Anno-style production chains, a Civilization-style technology tree and a parallel culture tree. There is no combat, no rivals and no fail state; you set the pace.

> **Status: pre-production.** The architecture, the progression design and a task-by-task implementation plan are complete. Next up is **M0 Foundations**. There is no playable build yet.

## The game

**Core loop:** settle a district → house residents → meet their needs through production chains → residents advance to higher tiers → higher tiers generate knowledge and culture → unlock technologies and civics → new goods, buildings and ground → expand across the island.

**Design pillars:**

1. **Grow, don't fight.** Challenge comes from space, logistics and balancing needs, never from enemies or deadlines.
2. **Knowledge opens the island.** Research makes rivers, hills, marshes, highlands and reefs usable.
3. **Culture is a second path, not a side stat.** Civics, traditions, festivals, great works and wonders matter. Two tiers and every era transition depend on culture.
4. **Every number explains itself.** Any shortage can be traced to its cause, and problems arrive as gentle advisor notes.
5. **A city worth watching.** Carts, walkers, smoke, ships and seasons reward zooming in.

**At a glance:**

| | |
| --- | --- |
| Eras | Founding → Township → Enlightenment → Industry |
| Population tiers | Settlers → Villagers → Townsfolk → Artisans → Scholars → Patrons |
| Economy | 47 goods across 24 production chains. A pure goods economy at first; gentle coin arrives with Coinage in Era II. |
| Technology tree | 50 technologies, with inspirations that reward play style |
| Culture tree | 31 civics and 34 tradition cards, plus festivals, great works, wonders and architectural styles |
| World | One large, seeded island (or the hand-made starter island), with fog revealed by expeditions |

## Technology

- **Godot 4.7 (.NET)** for rendering, UI, audio and export. **C# everywhere.**
- **A deterministic, engine-free simulation** in plain .NET libraries, tested in seconds with `dotnet test`. The same save, seed and commands always produce the same island.
- **All content is data:** JSON files with schemas and a content linter. Adding a building never touches code.
- **Platforms:** macOS and Linux (including Steam Deck) first; Windows comes last. Full gamepad support.

## Documentation

| Document | What's in it |
| --- | --- |
| [Architecture plan](docs/design/architecture.md) | Layers, simulation contracts, determinism, systems, content pipeline, presentation, saves, testing, budgets, roadmap |
| [Progression ladders](docs/design/progression.md) | Eras, tiers and needs, goods, production chains, buildings, terrain ladder, pacing targets |
| [Technology tree](docs/design/tech-tree.md) | All 50 techs with costs, prerequisites, unlocks and inspirations |
| [Culture tree](docs/design/culture-tree.md) | Civics, traditions and slots, styles, festivals, great works, monuments and wonders, visitors |
| [Architecture decisions](docs/adr/README.md) | ADR-0001 to ADR-0010 |
| [Implementation plan](docs/implementation/README.md) | Milestones M0–M5, task cards, dispatch protocol |
| [AGENTS.md](AGENTS.md) | Rules, commands and definition of done for coding agents |

## Roadmap

| Milestone | Delivers |
| --- | --- |
| **M0 Foundations** | Solution, core libraries, content loader and linter, simulation skeleton, CLI, Godot host |
| **M1 Vertical slice** | Starter island, Settlers → Villagers, five chains, logistics with carts, core UI, save/load; **then GitHub CI and the PR flow** |
| **M2 Era I complete** | Generated islands, bridges, fog and expeditions, seasons, the full Founding era, tree views |
| **M3 Era II, coin & culture depth** | Coinage, visitors, great works, drainage and terracing, Townsfolk and Artisans, styles |
| **M4 Public build** | Performance at scale, tutorial, accessibility, Steam Deck, audio, signed macOS and Linux builds on Steam |
| **M5 Eras III–IV + Windows** | Scholars and Patrons, highlands, harbour, steam, railways, gaslight, reefs, the Windows port |

## Getting started

Prerequisites:

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) (10.0.4xx), pinned in `global.json` from M0-01.
- [Godot 4.7.2 .NET edition](https://godotengine.org/download), needed only for the game layer. Scripts look for `$GODOT`, then `godot` on `PATH`, then `/Applications/Godot_mono.app`.

Once M0 has landed:

```sh
scripts/verify.sh                                                    # build, test, lint, Godot smoke test
dotnet run --project src/Evenmere.Tools -- lint                      # content lint
dotnet run --project src/Evenmere.Tools -- run --ticks 10000 --seed 42   # headless run; prints the state hash
```

Open `game/project.godot` in Godot 4.7 .NET to play.

## Repository layout

```text
content/      JSON game data, schemas, locale and maps
src/          Evenmere.Core · Evenmere.Content · Evenmere.Sim · Evenmere.Tools
tests/        unit, scenario and architecture tests, plus fixtures
game/         Godot 4.7 .NET project (presentation only)
docs/         design, ADRs, implementation plan
scripts/      verify.sh, Godot smoke test, export scripts
```

## How work lands

Most of Evenmere is built by coding agents working from the task cards in [docs/implementation](docs/implementation/README.md), under the rules in [AGENTS.md](AGENTS.md).

- **Until the end of M1,** finished tasks are verified locally with `scripts/verify.sh` and merged straight into `main`.
- **From task M1-29 onward,** GitHub Actions CI runs the same checks on macOS and Linux, and every change lands through a pull request with green CI.

## Licence

[MIT](LICENSE) © 2026 Wojtek Powiertowski
