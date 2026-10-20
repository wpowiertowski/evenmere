# AGENTS.md — rules for coding agents working on Evenmere

Read this file first, then your task card, then the documents the card lists under **Reads**. These rules override habits from other projects.

## What this repository is

Evenmere is a calm, single-player island city builder: Anno-style production chains and population tiers, a Civilization-style technology tree and a parallel culture tree, with no combat and no fail state.

- **Engine:** Godot 4.7 .NET. **Language:** C# everywhere.
- **Simulation:** deterministic and engine-free, in `src/`. **Presentation:** in `game/`. **Content:** JSON in `content/`.

| Document | Use it for |
| --- | --- |
| [docs/design/architecture.md](docs/design/architecture.md) | Layers, contracts, system order, determinism, content pipeline, budgets |
| [docs/design/progression.md](docs/design/progression.md) | Eras, tiers, needs, goods, chains, buildings, terrain |
| [docs/design/tech-tree.md](docs/design/tech-tree.md) · [culture-tree.md](docs/design/culture-tree.md) | Every tech, civic, tradition, festival, great work and wonder |
| [docs/adr/](docs/adr/README.md) | Accepted decisions; write a new one before changing a contract |
| [docs/implementation/](docs/implementation/README.md) | Milestones, task cards, dispatch protocol |

## Commands

| Command | Purpose |
| --- | --- |
| `scripts/verify.sh` | **The definition-of-done gate.** Restore, build (warnings as errors), all tests, content lint, and the Godot smoke test when Godot is installed. |
| `dotnet build Evenmere.sln -c Release` | Build everything, including the Godot game assembly |
| `dotnet test -c Release` | All unit, scenario and architecture tests |
| `dotnet test --filter "FullyQualifiedName~Evenmere.Sim.Tests.Production" --ignore-exit-code 8` | One system's tests. Exit code 8 means "no tests matched in some test project" under Microsoft.Testing.Platform, so it's ignored. |
| `dotnet run --project src/Evenmere.Tools -- lint` | Content lint (EVL rules) |
| `dotnet run --project src/Evenmere.Tools -- run --ticks 10000 --seed 42` | Headless run; prints the state hash and per-system timings |
| `scripts/godot-smoke.sh` | Godot headless smoke test (skips if Godot is not installed, unless `REQUIRE_GODOT=1`) |
| `EVENMERE_UPDATE_GOLDEN=1 dotnet test` | Rewrite golden scenario hashes. Only for an intended behaviour change, and explain it in the commit body. |

**Prerequisites:** .NET SDK 10.0.x (pinned in `global.json`) and Godot 4.7 .NET for game work (`$GODOT` may point to the binary).

## Hard rules

Breaking any of these fails review.

1. **Layering** ([ADR-0002](docs/adr/0002-project-layering.md)):
   - `Evenmere.Core` ← `Evenmere.Content` ← `Evenmere.Sim` ← `Evenmere.Tools` / `game/`.
   - Core, Content and Sim never reference Godot.
   - The game talks to the simulation only through `ISimulation`: `Enqueue`, `Tick`, `DrainEvents` and the read-only `Query` interfaces.
   - View code never holds references into `WorldState`.
2. **Determinism** ([ADR-0003](docs/adr/0003-deterministic-simulation.md)):
   - No `float`, `double` or `decimal` in sim state or sim logic. Use `Amount` (milli-units) and `Ratio` (per-mille).
   - No `Dictionary` or `HashSet` in state, and never iterate one in logic that affects state. Use `IdTable<T>`, arrays or `SortedDictionary`.
   - Randomness only from the system's own `Pcg32` held in state.
   - No wall-clock time, `Guid.NewGuid`, threads, `Task.Run` or `Parallel` in Sim (the analyser enforces this).
   - Iterate entities in ascending ID order.
3. **Content is data.** Goods, buildings, recipes, tiers, techs, civics and traditions are JSON in `content/`. Systems branch on categories, tags and effects, never on specific content IDs. The only exception is `ContentDb.Required`. Display text lives in `content/locale/`.
4. **Stay in your lane.** Modify only the paths listed under **Owns** on your task card. If you need a change anywhere else, stop and report a blocker with the exact change you need. Don't work around it.
5. **Frozen contracts change only by ADR:** `ISimulation`, `Command` and `SimEvent` base types, the query interfaces declared in M1-00, `ContentDb` and definition records, content schemas, and the save format. If your task needs a change, propose an ADR in your report and stop.
6. **Never** delete, skip or weaken a test to get green, and never update a golden hash without stating why the behaviour changed.
7. **Dependencies:** no new NuGet package unless your task card names it. Versions go in `Directory.Packages.props` (central package management).
8. **No secrets** in the repo: signing identities, Steam credentials and tokens stay out.

## Definition of done

A task is done when all of these hold:

- [ ] Every acceptance item on the task card is met, with evidence (the command and its result) in your report.
- [ ] `scripts/verify.sh` passes locally: zero build warnings, all tests green, lint clean.
- [ ] New behaviour has tests next to it. A sim system has unit tests on a tiny fixture world; behaviour that crosses systems has a scenario test.
- [ ] Public APIs in Core, Content and Sim have XML doc comments. Comments elsewhere explain *why*, not *what*.
- [ ] No TODO without a task ID (`// TODO(M2-05): seasonal yield`).
- [ ] Docs are updated if you changed behaviour that a design doc describes. If the doc and the code disagree and you are unsure which is right, ask; don't guess.

## C# conventions

- **Formatting:** file-scoped namespaces, four-space indent, LF line endings; `.editorconfig` is authoritative.
- **Types:** `sealed` by default. Use `record` for commands, events and content definitions, and `readonly record struct` for small values (IDs, `Amount`, `Ratio`).
- **Naming:** `PascalCase` for types and members, `_camelCase` for private fields, `camelCase` for locals. Folder = namespace: `src/Evenmere.Sim/Systems/Production/` → `Evenmere.Sim.Systems.Production`.
- **Nullable reference types** are on. No `!` suppression without a comment explaining why it is safe.
- **Hot paths** (anything run per tick per entity): no LINQ, no allocations in steady state, no boxing, no exceptions for control flow.
- **Each system folder** contains `<Name>System.cs` (the `ISimSystem`), `<Name>State.cs` (MessagePack-serialisable state with explicit integer keys), `Commands/`, `Events.cs`, and the implementation of its `I<Name>Queries`.
- **Tests:** xUnit v3 with Shouldly allowed; FluentAssertions is not allowed. Name tests `Subject_Condition_Expected`. Build fixture worlds with the builders in `tests/Evenmere.Sim.Tests/Fixtures/`.

## Godot conventions

- **C# scripts only.** Scenes (`.tscn`) and resources (`.tres`) are text; keep them small and composable.
- **Folders:** `game/host/` (SimHost, content paths, command log), `game/rendering/`, `game/ui/`, `game/controls/`, `game/audio/`, `game/scenes/`, and `game/data/visuals/` (content ID → scene, colour and icon mapping).
- **The view holds no authoritative state.** It reacts to drained events and dirty chunks and reads through queries between ticks.
- **One shared UI theme.** Every control must be reachable by gamepad. Check layouts at 1280 × 800 (Steam Deck) and 2560 × 1600.
- **Input** goes through named actions in `project.godot` (`camera_pan_left`, `ui_build_menu`, …), never raw key codes.
- **Placeholder art** is generated from footprints and palette colours. Real assets replace it through `game/data/visuals`, not through code changes.

## Content conventions

- **One item per file:** `content/<type>/<id>.json`, with a `snake_case` id that matches the file name.
- **Unlocks have one source:** the tech, civic or monument lists them in `effects`, and items available at the start set `"start": true`.
- **Quantities** are decimal numbers with at most three decimal places.
- **Locale keys** follow `<type>.<id>.name` and `<type>.<id>.desc`, in any `content/locale/en/*.json` file. Content tasks create their own locale file (for example `era1-production.json`) to avoid conflicts; duplicate keys are a lint error.
- `evenmere lint` must be clean.

## Git workflow

- **Branches:** each task works in its own git worktree, created by the orchestrator.
- **Implementing agents do not commit.** Leave your changes uncommitted in the worktree. The orchestrator reviews the full diff, sends feedback for you to address, and commits once the change is approved.
- **Commit message** (written by the orchestrator): `<ID>: <imperative summary>` with a body explaining what and why, plus the attribution trailer the session specifies.
- **Until GitHub CI exists** (task M1-29), the orchestrator verifies each task with `scripts/verify.sh`, commits, merges into `main` with `--no-ff`, and pushes.
- **After M1-29,** every change lands through a pull request: CI must be green, and `main` is protected, so direct pushes are blocked.
- Never rewrite published history on `main`.

## Reporting back

When you finish or get blocked, reply with:

1. **Summary:** what you changed and why, in 3–6 bullets.
2. **Acceptance evidence:** each acceptance item with the command you ran and its result.
3. **Files touched**, grouped by folder.
4. **Deviations from the card** and their reasons.
5. **Blockers, interface or ADR proposals, and follow-up tasks you discovered**, each with the exact change you need.

## Performance budgets

The per-system tick budgets are in the [architecture plan](docs/design/architecture.md#performance-budgets): a total of ≤ 2.0 ms on average and ≤ 6 ms at p99, on a Steam Deck–class CPU at 20,000 residents and 5,000 buildings. From M2 onward, check with `dotnet run --project src/Evenmere.Tools -- perf --save tests/fixtures/saves/perf-2k.evsave`, or use `perf-20k` once it exists.
