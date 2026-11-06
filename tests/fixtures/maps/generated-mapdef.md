# Finished-island golden hashes (M2-02c)

The finished island generator (`IslandGenerator.Generate`) is a pure function of
`(seed, size, content)`: the same arguments give the same island on every CPU. These hashes pin
that contract for seeds 1–5 at each supported size (256, 320, 384 tiles across).

**Hash method:** XxHash64 (`StableHasher`, seed 0) over the terrain string IDs in tile order
(`content.NameOf`) followed by the raw elevation, river-width, region, deposit and fertility bytes,
the hall site, the sub-seed, the discoveries, the region names and the start stock. Terrain names
(not numeric IDs) are hashed so the goldens survive content ID reassignment.
`MapDefGoldenTests` recomputes them.

**Re-baselined in M2-02c round 3:** the start guarantee counts its M1-03 thresholds over the tiles a
dirt road can actually reach from the hall (4-connected, no river or water tiles, within the hall's
storehouse reach of 25 road-steps) and requires a fishing-hut site on reachable coast; the roomiest
verified site is picked so the Era-I settlement fits; and failing sub-seeds fall back to the start
repair (StartRepair: forest planted as compact groves near the hall, stone/clay deposits placed,
grain fertility raised — elevation, rivers and coast untouched). Seven cells moved (repaired islands,
including seed 1 at 256 which now generates instead of throwing); the other eight are byte-identical
to the M2-02b round-2 baseline. Heightmap goldens are untouched (elevation never changes); fertility
goldens are untouched (GenerateTerrain runs before repair).

**Re-baselined in M2-39:** every diagonal river step is bridged with a side tile so networks are
4-connected, the same-direction walk cap drops from 12 to 11, and the prune re-runs on the finished
geometry; every island changed for that reason. Heightmap goldens are untouched (GenerateHeightmap
runs before rivers). Seed 2 at 256 replaces seed 1 at 256 as the repair witness: seed 1 at 256 now
verifies outright.

| Seed | Size | Hash |
|------|------|------|
| 1 | 256 | 7464ed0ad75a5768 |
| 2 | 256 | 74391fb6768eb036 |
| 3 | 256 | 3bb3e4bcf8c4305c |
| 4 | 256 | c759e4356f83bb10 |
| 5 | 256 | 7e6cbe51cddb7e42 |
| 1 | 320 | 1ce48fe12ff93bcf |
| 2 | 320 | 5603eb09121c2dff |
| 3 | 320 | c095fddf68e3e013 |
| 4 | 320 | 50a79c1120409342 |
| 5 | 320 | 3709094efd5ec30b |
| 1 | 384 | 3bfd21565936cfb5 |
| 2 | 384 | d1da0c7885e1bf3a |
| 3 | 384 | 184f403ed0dcecb1 |
| 4 | 384 | 2d6f47c475d06601 |
| 5 | 384 | c5ba31d32ad7cc5d |
