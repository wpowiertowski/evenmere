# 0014. Workforce hold on house advancement

Status: Accepted · Date: 2026-10-22

## Context

Automatic upgrades stay, and the upgrade lock stays a player tool. But the playtest showed the failure mode recorded in
M1-30's findings ("Settlers run out as houses advance"): every house advances to Villagers, so no Settlers are left to
staff the Settler-workforce producers, and the economy stalls unless the player remembers to lock houses. The developer's
decision: if buildings need a lower tier of settlers for staffing, the system should automatically hold up the required
number, so the island does not run out of workers as its buildings evolve.

## Decision

- **The rule, generic for every tier:** a house of tier T, with all its other conditions for advancing met, advances
  only if the tier-T workers left after it leaves still cover tier T's jobs:
  `residentsOfTier(T) − house.Residents ≥ Demand(T)`, where `Demand(T)` is `IWorkforceQueries.Demand(T)`.
  Otherwise the house is **held**: it keeps its advance streak, so it advances as soon as there are spare workers.
  More houses of that tier, or fewer jobs, release it. With no jobs for tier T (`Demand(T) == 0`), nothing is held.
- **Live totals.** Population evaluates each house on its own bucket tick, so the check needs live per-tier resident
  totals that stay correct as houses advance, decline, fill or empty during the day. They live in
  `PopulationState.TierResidents` (indexed by `TierId.Value`): rebuilt at day start, adjusted on every change, O(1) per
  check, always equal to the sum over houses. Population runs at step 8 and Workforce at step 3, so the demand it reads
  is current for the day.
- **Blocker order.** The hold is checked last, only when the house would otherwise advance: needs met, next tier
  unlocked, not upgrade-locked and materials available. The existing blockers keep priority, so an upgrade-locked house
  still reports `UpgradeLocked`.
- **Contract addition.** `AdvanceBlocker` gains `HeldForWorkforce = 6`, appended after `MissingMaterials`. Nothing else
  in the frozen query types changes. A held house explains itself with `explain.house.blocker.held_workforce` and
  typed arguments (tier, demand, spare workers); advisors do not raise `HouseCannotAdvance` for held houses, which keep
  counting only `MissingMaterials` and `NextTierLocked`.

## Consequences

- The island keeps a working settler base by default; the upgrade lock becomes a planning tool rather than a rescue tool.
- Slice behaviour changes: fewer houses advance early, so scenario golden hashes move and the balance bot's timing
  shifts. The hashes are re-baselined with the reason stated, and the slice targets are unchanged.
- `TierResidents` is save state (MessagePack key 3, content-indexed by tier). `RemapPass.RemapPopulation` permutes it
  with the tiers like the published supply; a day-start rebuild reconciles it after loading an older save.
