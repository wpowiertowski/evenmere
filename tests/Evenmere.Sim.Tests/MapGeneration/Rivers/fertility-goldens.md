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

| Seed | Size | Hash |
|------|------|------|
| 1 | 256 | 0472d7a757025247 |
| 1 | 320 | b5baeb033986c371 |
| 1 | 384 | f5637fb0907f0023 |
| 2 | 256 | 416ca82913658f2d |
| 2 | 320 | 9873c5e0c6b25a0c |
| 2 | 384 | 51c843652d417101 |
| 3 | 256 | 673373737601c52f |
| 3 | 320 | 649f951cc4f9509a |
| 3 | 384 | 30fbfa32dd9b5f13 |
| 4 | 256 | d422dee7cc8f8501 |
| 4 | 320 | fac41a0c3f053add |
| 4 | 384 | c329bfa32c2e930c |
| 5 | 256 | 5e52c39431aa457b |
| 5 | 320 | 68752f4a5c09456f |
| 5 | 384 | 6b5acc5089425768 |
