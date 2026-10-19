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
