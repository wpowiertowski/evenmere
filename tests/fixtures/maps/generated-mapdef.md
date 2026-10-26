# Finished-island golden hashes (M2-02b)

The finished island generator (`IslandGenerator.Generate`) is a pure function of
`(seed, size, content)`: the same arguments give the same island on every CPU. These hashes pin
that contract for seeds 1–5 at each supported size (256, 320, 384 tiles across).

**Hash method:** XxHash64 (`StableHasher`, seed 0) over the terrain string IDs in tile order
(`content.NameOf`) followed by the raw elevation, river-width, region, deposit and fertility bytes,
the hall site, the sub-seed, the discoveries, the region names and the start stock. Terrain names
(not numeric IDs) are hashed so the goldens survive content ID reassignment.
`MapDefGoldenTests` recomputes them.

**Re-baselined in review:** the start guarantee counts its M1-03 thresholds over the hall's
road-connected bank (flood-reachable tiles within 24 tiles) instead of the bare radius, and the
hall site is picked bank-aware, so the bank always carries the farmland it promises. Eight of the
fifteen hashes moved (seeds whose site or sub-seed changed); the heightmap and terrain goldens are
untouched.

| Seed | Size | Hash |
|------|------|------|
| 1 | 256 | e1c62581fd419dc8 |
| 2 | 256 | 33a16040a28ea4cf |
| 3 | 256 | 20a0a634defccf1d |
| 4 | 256 | 22bc0dccd08242bc |
| 5 | 256 | e520cffdd562369d |
| 1 | 320 | 01107486b97f3d3a |
| 2 | 320 | b4b5f374c8217bd9 |
| 3 | 320 | c60d9d7b5d2e6331 |
| 4 | 320 | e75ba7722761dd26 |
| 5 | 320 | bfd8dcee1a25094c |
| 1 | 384 | fae3873f0d689c90 |
| 2 | 384 | d4ace439673433a1 |
| 3 | 384 | e43f08feeedfa28f |
| 4 | 384 | 4d30f54249d75c30 |
| 5 | 384 | 12ad8235f0e25015 |
