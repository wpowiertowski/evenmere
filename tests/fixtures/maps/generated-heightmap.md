# Generated heightmap golden hashes (M2-01)

The seeded island generator (`IslandGenerator.GenerateHeightmap`) is a pure function of
`(seed, size, content)`: the same arguments give the same arrays on every CPU. These hashes pin
that contract for seeds 1–5 at each supported size (256, 320, 384 tiles across).

**Hash method:** XxHash64 (`StableHasher`, seed 0) over the terrain string IDs in tile order
(`content.NameOf`) followed by the raw elevation bytes. Terrain names (not numeric IDs) are hashed
so the goldens survive content ID reassignment. `HeightmapGoldenTests` recomputes them.

**Re-baselined in review round 3:** the height shaping was rebuilt for irregular islands — a warped
anisotropic envelope, an off-centre massif with a flat top, short-wavelength shoreline roughness and
rocky skerries — replacing the radial bowl, so every hash below changed for that reason.

**Re-baselined again in the same round:** elevation quantisation changed from linear to a square
root, narrowing the beach fringe and broadening the middle bands; land shapes are unchanged, only
the elevation values moved.

| Seed | Size | Hash |
|------|------|------|
| 1 | 256 | 3fb08c5c8effc86a |
| 2 | 256 | 3052b71741b8fc76 |
| 3 | 256 | fd8ee95225fa7ddc |
| 4 | 256 | ee99407de04d6450 |
| 5 | 256 | 739cbf43386c7556 |
| 1 | 320 | d7af779bd5a045cb |
| 2 | 320 | 13de0b882d3ae26a |
| 3 | 320 | 93118eff0b7a6581 |
| 4 | 320 | 0dc14ada01ca7149 |
| 5 | 320 | 7ad093287a990b9a |
| 1 | 384 | 518b20726d4a4099 |
| 2 | 384 | e3e2f98e43c57603 |
| 3 | 384 | 8eb6a713743462b9 |
| 4 | 384 | db27b088025c8f13 |
| 5 | 384 | 9404099d5f76167d |
