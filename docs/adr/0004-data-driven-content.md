# 0004. Data-driven content and ContentDb

Status: Accepted · Date: 2026-10-18

## Context

The game will hold about 47 goods, more than 120 buildings, 50 techs, 31 civics and over 40 traditions, and agents will add them in parallel. Content written in code would create merge conflicts in core logic and make modding expensive.

## Decision

- **One JSON file per item** under `content/<type>/`, validated by a JSON Schema in `content/schemas/<type>.schema.json`.
- **String IDs** in `snake_case`, unique per type. The loader interns them into typed integer IDs (`GoodId`, `BuildingTypeId`, `RecipeId`, `TierId`, `TechId`, `CivicId`, `TraditionId`, …), assigned in ordinal string order.
- **`ContentDb` is immutable,** built once at startup. Systems receive it through `SimContext` and never read files.
- **Unlocks have one source of truth.** Tech, civic and monument effects list what they unlock. Items available from the start set `"start": true`. Buildings never name their own tech.
- **Behaviour comes from data.** Systems branch on categories, tags and effects, never on specific IDs. The only allowed exception is the IDs exposed as named constants in `ContentDb.Required` (for example the starting hall), and the linter checks that these exist.
- **Shared vocabularies.** Effects and conditions are small, closed vocabularies (listed in the [architecture plan](../design/architecture.md#content-shapes-illustrative-the-schemas-are-authoritative)) and are interpreted by `Systems/Progression` and `Systems/Modifiers`.
- **Display text** lives in `content/locale/<lang>/<type>.json` and never inside content files.
- **Content hash:** XxHash64 over every file's normalised bytes (LF line endings, sorted by path). The hash is stored in saves and printed by `evenmere lint`.
- **Content packs** load in order after the base `content/` folder. A later pack may add items or override whole items by ID, but cannot delete them.

## Consequences

- A new building is a JSON file, a locale entry and a visuals mapping, with no C#.
- The linter rules EVL001–EVL008 are the guardrails, and every content task must pass them.
- Schema changes are interface changes: they are versioned with the loader and need an ADR when they change existing fields.
