# 0007. Save format, ID remapping and migrations

Status: Accepted · Date: 2026-10-18

## Context

Content IDs are interned into integers in ordinal order ([ADR-0004](0004-data-driven-content.md)), so adding a good shifts the integer IDs. Saves must survive content updates and format changes across months of development and post-launch updates.

## Decision

- **File layout:**
  - Magic `EVMR`, then a `uint16` format version, then a length-prefixed **header**, then the **body**.
  - The **header** is MessagePack with: game version, content hash, map seed, island size, play time, tick, era, population, thumbnail (from M4), and **string-ID tables** per content type, each listing the string IDs in the order the save's integer IDs refer to them.
  - The **body** is the MessagePack-CSharp serialisation of `WorldState`, LZ4-compressed (`Lz4BlockArray`). A debug flag writes indented JSON with the same structure instead.
- **On load:**
  1. Read the header.
  2. Run format migrations from the save's version up to the current one.
  3. Build an integer-ID remap from the save's string tables to the current `ContentDb`.
  4. Deserialise and remap.
- **Unknown content:** buildings become *ruins* that can be demolished for a full material refund, and goods and points stay in place as data. Each is reported once to the player.
- **Migrations:** one class per format version step (`Migration_0003_to_0004`). A fixture save from every released format version lives in `tests/fixtures/saves/`, and a test loads each one.
- **Command log:** a `.evlog` file next to the save, listing (tick, command) pairs in MessagePack. Replay = load the save, then feed the logged commands.
- **Canonical serialisation:** the state hash uses the same uncompressed body bytes, so save and hash can't drift apart.

## Consequences

- Content changes never break saves; format changes need a migration and a fixture.
- `WorldState` types carry MessagePack attributes with explicit integer keys. Renaming a field is free; reusing a key is not, and an architecture test checks that keys are unique and never removed.

## Addendum (M1-14)

Status: Accepted · Date: 2026-10-21 · What M1-14 built, and where it knowingly deviates.

### What was built

- **Files** (`src/Evenmere.Sim/Persistence/`): `SaveGame` (`Write`, `WriteJson`, `Read`, `ReadStateHash`), `SaveMeta`,
  `SaveHeader`, `SaveReport`, `SaveTables`, `RemapPass`, `ConditionRemap`, `CommandFormatter`, `CommandLogWriter`,
  `CommandLogReader`, `Replay`, `IMigration`/`MigrationChain`, `SaveFormat`, and the `ContentKind`,
  `ContentIndexed`, `ContentValued` and `NotContentIndexed` markers. `evenmere hash --save <file>` prints a save's
  state hash in the same 16-hex format as `run`.
- **Header:** magic `EVMR`, `uint16` version 1, then length-prefixed MessagePack header and LZ4 body. The header holds
  the game version, content hash, seed, map ID, tick, era, population, the stored state hash (loads verify the body
  against it) and one string-ID table per content kind. Thumbnail and play-time/island-size stay M4/undecided; the
  header stores tick and population instead.
- **Fast path:** when the save's tables equal the current content's (same names, same order, every kind), the body
  deserialises and restores directly and the report stays clean. Otherwise deserialisation keeps the save's raw IDs
  and one explicit pass (`RemapPass`) permutes every content-indexed array, rewrites every content-valued member and
  maps every typed ID member by member, so every use can still name its old item for the report.
- **Markers:** every array-typed member of every `[MessagePackObject]` type reachable from `WorldState` carries
  `[ContentIndexed]`, `[ContentValued]` or `[NotContentIndexed(reason)]`. An architecture test fails the build on a new
  unmarked array. `tests/fixtures/saves/state-keys.json` baselines every key, member and member type with the same
  guarantee for keys.
- **Commands:** the log envelope is MessagePack `[tick, typeName, payload]` with a hand-written per-command formatter
  (primitives only, content IDs as raw values); the reader remaps them and drops commands about gone items, because a
  stale raw ID would otherwise point at a different item. `Replay.Run(save, log, content, untilTick)` takes the content
  alongside the streams, which the card's signature leaves implicit.
- **Migrations:** `IMigration` plus an ordered `MigrationChain` runner. There are no migrations yet; the chain is
  covered by a test with fake steps. `tests/fixtures/saves/v1-slice.evsave` (slice to tick 20,000) is the first fixture
  save; regenerate it with `EVENMERE_UPDATE_GOLDEN=1`.

### Deviations from the decision above, and why

- **No remapping resolver.** The card approves remapping typed IDs during deserialisation. M1-14 deserialises with the
  save's IDs intact and remaps everything in the explicit pass instead: an unknown raw ID that collides with a valid
  new ID (old bread 1 vs new cherry 1) is undetectable after resolver mapping, but trivially detectable when the old raw
  is still in hand, and one pass keeps every per-item report in one place.
- **Unknown goods, techs and civics are dropped, not kept as data.** The decision says they "stay in place as data".
  Keeping them would mean variable-length state (a stock array longer than the content, bitsets with trailing words)
  that every system must then ignore, plus orphaned producer buffers and conditions referencing gone IDs. V1 drops
  them and reports each once with what was lost. Keeping orphaned data properly is deferred to M2-15, which owns
  migrations and save compat next.
- **Ruin refunds are held buffers, not bills.** The decision promises ruins "demolished for a full material refund".
  A load cannot reconstruct the old bill (only string IDs survive), so reusing the demolish math is impossible for
  unknown types. V1 turns the building into a ruin (a valid standing type, state `Ruin`, stage 0), removes its site,
  producer, house and workplace records, drops jobs to it, and refunds the producer's held input/output buffers to the
  pool in full, reporting each ruin with its refund. Site-drawn materials and the half-bill demolish refund need the
  old definitions and are reported as dropped. Demolishing the ruin later works through the existing handler.
- **Unknown road types drop tiles; unknown terrain resets to the first terrain.** Both are reported per type with
  counts. A road type can only go missing in incoherent test content (the loader requires the dirt road), so the road
  drop is covered by a unit test while the ruin path is covered on a slice save.
