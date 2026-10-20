# 0011. The M1 simulation contract

Status: Accepted · Date: 2026-10-19

## Context

M1 builds ten simulation systems in parallel. Each needs to call the others without waiting for them, so task M1-00 declared the whole cross-system surface up front: queries, write services, commands and events, with throwing stand-ins and test fakes. The M1-00 card's interface table was a sketch. The code is now the source of truth, and a few questions the card left open needed answers before the system tasks start.

## Decision

- **The code is the contract.**
  - Queries live in `src/Evenmere.Sim/Queries/` (`I<Name>Queries`, value types in `Queries/Types/`).
  - Write services live in `src/Evenmere.Sim/Services/`, internal, and are reached through `SimContext`.
  - Commands live in `Systems/<Owner>/Commands/`, and events in `Systems/<Owner>/Events.cs`.
  - Changing any of these signatures needs a new ADR. Adding a method that a task card names does not.
- **Names:**
  - Query interfaces follow the folder names: `IRoadsQueries`, `IAdvisorsQueries`.
  - Lookups that can miss use the `TryGet…` form: `TryGetBuilding`, `TryGetHouse`, `TryGetHouseNeeds`, `TryGetSite`.
  - `Rotation` is an enum with values 0 to 3.
- **`RejectReason` is append-only.** Its numeric values are stable.
- **Lifecycle:**
  - `IBuildingLifecycleListener` callbacks receive the `SimContext` and are called synchronously, in subscription order.
  - M1-13 subscribes listeners in pipeline order.
  - Listeners keep any per-building state in their own system state, so a restored save never depends on replaying lifecycle calls.
- **Placement** is answered by two internal halves: `IBuildingPlacement` (Construction) and `IRoadPlacement` (Roads). `PlacementQueries` forwards to them, and commands and ghost previews share the same code.
- **Pausing:**
  - `BuildingState.Paused` in the building registry is the single source of truth.
  - The `SetBuildingPaused` handler (Production) sets it through `IBuildingRegistry.SetState`.
  - Workforce demand and Production both read it from `IMapQueries`. No system keeps its own paused flag.
- **Building outputs:** a building's own `outputs` (for example, the Founders' Hall's knowledge and culture, or the Shrine's culture) are credited daily by the system that spends those points.
  - Research credits knowledge, and Culture credits culture, just before taking the day's points.
  - The amount is `outputs × workforce fill × (1000 + output bonus) / 1000` for active buildings. A building with no workforce counts as fully staffed.
  - Population credits tier outputs.
- **Area yield** is recomputed on building lifecycle changes only. Terrain is static in M1 and roads don't affect yield, so no road-change channel is added.
- **Transport requests stay listed until the goods move.** Logistics must recognise a request it is already serving and never start a second job for it.
- **`SystemRoster.cs`** is the composition root. Each line is tagged with the task that replaces it, and a task may edit only its own tagged lines. Leftover stand-ins (`Unimplemented*` classes, the stub `ExplainQueries`) are deleted by the task that replaces them, or by M1-13.

## Addenda

- **2026-10-20:** `IRoadsQueries.IsFootprintInStorehouseReach(x, y, width, height)` is added, so Construction's ghost preview can ask Roads about storehouse reach for a footprint that isn't built yet, instead of duplicating the distance search (found while reviewing M1-06).
- **2026-10-20:** a state member of type `IdTable<T>` carries `[MessagePackFormatter(typeof(IdTableFormatter<T>))]`, because MessagePack's source generator otherwise emits a broken resolver (CS0426). The bytes are identical.
- **2026-10-20:** demolishing an active or paused building refunds half its cost, rounded down per good to the milli-unit. A site refunds 100% of the materials it has drawn.

## Consequences

- Ten system tasks run in parallel against fakes, and integration problems surface in M1-13's end-to-end scenarios.
- The pause and output rules remove two ways systems could disagree.
- Any later change to a signature is visible and deliberate, because it needs an ADR.
