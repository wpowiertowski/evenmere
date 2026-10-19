# 0009. Goods-only Era I; coin introduced by Coinage

Status: Accepted · Date: 2026-10-18

## Context

The developer wants no coin early on, with gentle coin mechanics added later. A coin economy from the first minute also adds upkeep pressure, which works against the calm pillar during onboarding.

## Decision

- **In the game:**
  - Era I is a **pure goods economy**: buildings cost materials only, and nothing has upkeep.
  - The Era II tech **Coinage** unlocks the coin economy and the Counting House.
  - From then on, Townsfolk and higher tiers pay taxes, visitors spend, and buildings that declare `upkeepCoin` (only Era II-and-later content) cost upkeep.
  - Era I buildings stay upkeep-free forever.
- **Gentle coin:**
  - The treasury cannot go negative.
  - If upkeep can't be paid, unpaid buildings run at 50% productivity (stall reason `Underfunded`) until coin recovers.
  - There is no debt and no shutdown.
- **In development:** the `Economy` system folder exists from M0 as a stub that does nothing. Coin is implemented in **M3** alongside Era II content. M1 and M2 never reference coin.

## Consequences

- Early balance concerns only goods and workforce, which keeps the vertical slice small.
- Introducing coin is a visible, celebrated moment: an advisor note explains taxes and upkeep.
- Content authors must not put `upkeepCoin` on Era I buildings; lint rule EVL009 is added in M3.
