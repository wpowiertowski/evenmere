# 0016. Workforce stepping down, morale, a stricter hold and slower advancing

Status: Accepted · Date: 2026-10-24

## Context

The M1 playtest showed the island advancing settlers too early and too eagerly: with 45 Settlers against 51 jobs and 280 idle Villagers, production blocked while the higher tier sat free. Two causes:

- The M1-33 hold (ADR-0014) protects only the jobs that exist right now. Workforce `Demand` excludes construction sites, so houses advance down to exactly the current demand, and new buildings then cause shortages.
- Villagers have no jobs of their own in Era I, so nothing uses their labour.

The developer's decision has four parts: idle workers of a higher tier fill unfilled jobs of a lower tier; underemployed houses cannot advance (morale); the hold also counts planned demand and a reserve; and advancing takes six days instead of three. (Number 0015 is reserved for M2-30.)

## Decision

- **Stepping down, once a day, deterministic.** Each tier first staffs its own workplaces exactly as today. Then, for tiers from lowest to highest rank (`ContentDb.TiersByOrder`, because tier IDs are in string order), a tier's unfilled jobs draw idle workers from the next higher tier, then the one above, so same-tier workers are always used first and the nearest higher tier goes first. Within the tier being filled the same priority and fair-share rules apply, so high-priority buildings fill first. The allocation records per tier `WorkingBelowTier[T]` (workers of T placed in lower-tier jobs) and `FilledFromAbove[T]` (jobs of T filled from above), and per workplace the total, the substitutes and the nearest source tier. `FillOf` reports the total including substitutes, so substitute labour restores productivity.
- **Morale.** Population distributes `WorkingBelowTier[T]` over the houses of tier T in ascending house ID at each day start, filling each house's residents; the first houses carry the underemployment. `HouseInfo.MoralePerMille` is `1000 × (residents − underemployed) ÷ residents` (1000 when full or empty). Morale affects nothing but advancement: a house with morale below 1000 reports the new `AdvanceBlocker.Underemployed = 7`, after `MissingMaterials` and before `HeldForWorkforce`. The explain queries show the morale wherever the house is shown, even when an earlier blocker reports first.
- **The stricter hold.** With `planned = PlannedDemand(T)` (the jobs at new-build construction sites of the tier; in-place upgrades keep working as their old type, so their old tier's `Demand` still covers them) and `reserve = ceil(workforceReservePercent × (Demand(T) + planned) / 100)` (`workforceReservePercent` is a rule, default 20), a tier-T house advances only if `LiveTierTotal(T) − house.Residents ≥ Demand(T) + planned + reserve`. With no jobs at all, nothing is held, as before.
- **Slower advancing.** `advanceStreakDays` goes from 3 to 6 in `content/rules/core.json`.
- **Advisors.** `NoWorkforce` fires only for a real shortage: jobs unfilled after stepping down, that is `Demand(T) > Supply(T) + FilledFromAbove(T)`, two days in a row as before.

## Consequences

- Idle higher-tier workers keep lower-tier production staffed, and their houses wait instead of advancing past the shortage, so the island no longer strands its workforce. The reserve and the planned demand keep a buffer for buildings on the way.
- `IWorkforceQueries` gains `PlannedDemand`, `WorkingBelowTier`, `FilledFromAbove`, `AllocatedWorkers`, `SubstitutedWorkers` and `SubstituteSourceTier` as plain interface members, implemented explicitly in every implementer (the real queries and all fakes), so no implementation can silently report zero. `AdvanceBlocker` gains `Underemployed = 7` and `HouseInfo` gains `MoralePerMille`, recorded here as the contract addition.
- New save state: `WorkforceState.WorkingBelow` and `.FilledFromAbove` (MessagePack keys 2 and 3, content-indexed by tier, permuted by `RemapPass`), `WorkplaceRecord.Substituted` and `.SubstituteTier` (keys 2 and 3), and `HouseRecord.Underemployed` (key 13). The state-keys baseline and the save fixtures are re-baselined with the reason stated.
- `RulesDef` gains `WorkforceReservePercent` (default 20); the loader reads it optionally so older packs keep working, while the schema documents it. `advanceStreakDays` 6 shifts slice timings; the scenario hashes are re-baselined and the balance bot re-run (target: 100 Villagers within 45 minutes).
