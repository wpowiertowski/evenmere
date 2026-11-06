# Terrain-stage golden hashes (M2-02a)

The seeded terrain stage (`IslandGenerator.GenerateTerrain`) is a pure function of
`(seed, size, content)`: the same arguments give the same arrays on every CPU. These hashes pin
that contract for seeds 1–5 at each supported size (256, 320, 384 tiles across).

**Hash method:** XxHash64 (`StableHasher`, seed 0) over the river-width bytes, then the deposit
bytes, then the fertility words as little-endian bytes, in tile order. `FertilityGoldenTests`
recomputes them. The M2-01 heightmap goldens pin the terrain and elevation the stage starts
from; these pin the M2-02a layers (rivers, deposits with reefs, filled pockets, fertility).

**Re-baselined in review round 3:** rivers are traced from priority-flood flow accumulation
instead of highland spring walks, so the river, deposit-adjacency and fertility patterns
changed for that reason.

**Re-baselined in review round 5:** a pre-paint straight-run veto discards any walk that
would complete a 13-step straight river run, so river courses (and downstream deposits and
fertility) shifted for that reason.

**Re-baselined in M2-39:** every diagonal river step is bridged with a side tile so networks are
4-connected, the same-direction walk cap drops from 12 to 11 to leave headroom for the bridges,
and the prune re-runs on the finished geometry; every river, deposit-adjacency and fertility pattern
changed for that reason.

| Seed | Size | Hash |
|------|------|------|
| 1 | 256 | 07e9a5e9ccff028b |
| 1 | 320 | 19644cb4e4bdc5b6 |
| 1 | 384 | 69432e1922b6e515 |
| 2 | 256 | a5dc90ec1bde3e99 |
| 2 | 320 | 23a6f9ffdd84699e |
| 2 | 384 | 6314efe455f28421 |
| 3 | 256 | 24935dd521b30e4e |
| 3 | 320 | 9781cee4305edd57 |
| 3 | 384 | 0611e4112d6a6d29 |
| 4 | 256 | b1707e63b409fe28 |
| 4 | 320 | db9ecd3041c157ed |
| 4 | 384 | 2de28706f22a0006 |
| 5 | 256 | 8f3424e930e6c38f |
| 5 | 320 | f62a4e6268be09f3 |
| 5 | 384 | 4a4ec89cf1ecbe3c |
