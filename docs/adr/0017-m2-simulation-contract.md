# 0017. M2 simulation contract addendum: queries, services, commands and events

Status: Accepted · Date: 2026-10-25

## Context

M1-00 froze the cross-system surface and ADR-0011 recorded it. M2 adds seven gameplay
systems' behaviour (seasons, exploration, traditions, festivals, monuments, eras,
happenings), the overlay queries the view needs, and a modifier-removal path that
unslotting, festival endings, monument demolition and happening expiry all share. Each
addition is listed here once so M2 cards can reference it instead of re-proposing it.

## Decision

The frozen surface grows exactly as follows. Everything else in ADR-0011 still holds.

**New query methods on existing frozen interfaces:**

- `IProgressionQueries`: `bool IsTraditionUnlocked(TraditionId)`,
  `int TraditionSlotCount(TraditionSlotType)`, `GatewayStatus GatewayStatus(EraId)`
  (a small struct: tech done, civic done), keeping `CurrentEra` as the era that has begun.
- `ICultureQueries`: `Slots()`, `UnlockedTraditions()`, `ActiveStyle()` (M2-07);
  `bool IsFestivalRunning()`, `ActiveFestival()`, `int FestivalsHeldInYear(FestivalId, year)`
  (M2-08).
- `IExplorationQueries` (filled by M2-06): `bool IsRevealed(ushort region)`,
  `RevealedRegions()`, expedition status per lodge, `Discoveries()`.
- `IHappeningsQueries` (filled by M2-12): `PendingHappening()`, `IsOnCooldown(HappeningId)`.
- `IRoadsQueries`: `long RoadTiles(BuildingTypeId road)` per road type (M2-09), backed by a
  dense array in `RoadsState`.
- `IConstructionQueries`: `SiteInfo` gains the monument stage index and stage count.
- `IAdvisorsQueries`: no change (gateway advice reuses `ExplainGood`/existing views).

**New interface:** `IOverlayQueries` (M2-20) with one method per layer
(storehouse reach, market coverage, service coverage, fertility per crop, deposits,
buildable-with-access), each returning a cached per-tile byte layer.

**Services:** `IProgressionService` gains `RemoveSource(EffectSource)` (M2-07): removes
every active modifier of the source and every effect the source applied that has no
behavioural remainder. Unlock bitsets never go down; tradition/festival/monument sources
only ever contributed modifiers and recorded flags, so removal is exact.

**Structs:** `ModifierContext` gains `DiscoveryKind? Discovery` (M2-06); a default value
asks about no discovery, so every existing call site is unaffected.

**New `Command` subtypes** (records, one handler each; base type unchanged):
`SetTradition`, `HoldFestival`, `StartExpedition`, `ChooseHappeningOption`.
(Cheat commands are ADR-0012's, not this ADR's.)

**New `SimEvent` subtypes** (base type unchanged): `SeasonChanged`, `RegionRevealed`,
`DiscoveryRevealed`, `TraditionChanged`, `FestivalStarted`, `FestivalEnded`,
`MonumentStageCompleted`, `EraBegan`, `HappeningPending`, `HappeningResolved`.

## Consequences

- M2 cards cite this ADR for their interface members instead of proposing them; the
  contract tests (every `Command` a sealed record, fakes implementing every member)
  extend to the new members.
- `EffectSourceKind` is unchanged: tradition, festival, monument and happening sources
  already exist.
- No existing query signature changes, so M1 saves, fakes and the view keep compiling;
  new fakes implement the new members explicitly.

## Addendum (2026-10-26)

Two view-safe twins landed after the M2 cards were refined, because the game boundary
(ADR-0006) forbids view code from touching the MessagePack state records the M2-08 and
M2-12 queries return:

- `ICultureQueries.ActiveFestivalView() → FestivalRunView?` (M2-27): the running festival
  with its ground and days left, mirroring `FestivalRun` without the culture state.
- `IHappeningsQueries.PendingView() → PendingHappeningView?` (M2-27): the pending happening
  with its raised tick, mirroring `PendingHappening` without the happenings state.
- `IStatisticsQueries.SeasonalSeries(StatKey, Span<long>) → (int Written, long Seasons)`
  (M2-33): the per-season series beside the 40-day ring. It already landed and was missing
  from this addendum.

Both view records live in `Evenmere.Sim.Queries.Types`, which the boundary check allows;
both members are plain additive members with explicit implementations and fake coverage.

## Addendum (2026-10-26, M2-13b)

- `content/happenings/<id>.json` options gain an optional `label` locale key, shown by
  `game/ui/events/HappeningDialog.cs` beside the effect preview and linted by EVL007. It is
  display text only: the sim never reads it, so old saves and logs are unaffected.

## Alternatives

One ADR per system (seven small ADRs): rejected as noise, because every change is
additive and owned by exactly one card; this single addendum keeps the review in one place.
