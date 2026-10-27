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

| Seed | Size | Hash |
|------|------|------|
| 1 | 256 | b9cbbe12273ec81f |
| 2 | 256 | 82b19bf3f9642d9f |
| 3 | 256 | 07f4468689fee908 |
| 4 | 256 | 78d523d917e14af4 |
| 5 | 256 | 4e3efe6b84244bfa |
| 1 | 320 | 560af86bd3c816af |
| 2 | 320 | 0fd75e3b9f6c6597 |
| 3 | 320 | b3b1f2cd55ccc9f8 |
| 4 | 320 | 4178d0903a927115 |
| 5 | 320 | aef2f0642259c625 |
| 1 | 384 | 9b82d411c9c3001b |
| 2 | 384 | 6305e091f1d33f4f |
| 3 | 384 | 5989f624353c10f5 |
| 4 | 384 | 9328ccfc9eab7eee |
| 5 | 384 | 1c09cecabb7e2e9e |
