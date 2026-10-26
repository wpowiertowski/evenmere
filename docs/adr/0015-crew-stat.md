# 0015. Crew stat: modifiers that reduce a building's required workers

Status: Accepted · Date: 2026-10-25

## Context

Upgrade lines (progression.md §Upgrade lines) modernise buildings in two ways: full upgrade
variants with their own staffing, and small same-tier crew reductions from techs and civics
(for example "Saw Frames: sawpits need one worker fewer") that apply without rebuilding.
The modifier vocabulary has no stat for "needs fewer workers": `workforce` scales supply,
not demand. M2-30 needs one for its Era I rows, and EVL010 must still be checkable against
the reduced crews.

## Decision

- Add `Stat.Crew` (`[ContentName("crew")]`) at the end of the `Stat` enum, for "required
  workers of the matching buildings". Appending keeps every existing serialised value stable.
- Semantics: `IProgressionQueries.BonusPercent(Stat.Crew, ctx)` returns a percentage P
  (negative reduces). A matching building with content count N needs
  `max(1, (N × (1000 + 10 × P) + 500) / 1000)` workers in integer maths (half up,
  floored at 1). Positive P (more workers required) is allowed by the code but content
  never uses it in Era I.
- `WorkforceSystem` computes demand from the effective crew, not the content spec, every
  day start. `FillOf`, stepping down (ADR-0016), the hold and `PlannedDemand` all see the
  reduced numbers. `ExplainBuilding` shows the crew line with its source, like any modifier.
- `ModifierTarget` needs no change: `BuildingsTarget` selectors already pick the buildings.
- EVL010 (M2-14) checks upgrade **variants**, which may change the staffing tier; a variant
  staffing a basic good at its own tier fails lint. Crew reductions only change the count,
  never the tier, so they cannot break the rule — the linter still reads the crew-reduced
  counts so headcounts stay exact.

## Consequences

- Small staffing improvements become data (`modifier` effects with `stat: crew`), never code.
- Demand maths must convert with `Ratio.FromBonusPercent`, never subtract from 1000 (units rule).
- Unit tests: −25% on a 4-worker building needs 3; −90% on a 1-worker building stays 1;
  the reduced demand flows into supply/demand queries and the hold.

## Alternatives

A per-building `crew` content field instead of a stat: rejected, because tech-gated
reductions must come from the trees through the shared modifier path, with sources the
explain queries can name.
