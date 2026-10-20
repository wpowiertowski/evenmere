# 0001. Godot 4.7 .NET with C#, and an engine-free simulation

Status: Accepted · Date: 2026-10-18

## Context

Evenmere is built largely by coding agents, supervised by one developer on macOS. Agents need text-based assets they can diff, fast feedback without a GPU or an editor, and a type system that catches mistakes. The engines considered were Godot 4.7 + C#, Unity 6, Unreal Engine 5 and Bevy; the comparison is in the [architecture plan](../design/architecture.md#engine).

## Decision

- Use **Godot 4.7 (.NET edition)** for rendering, UI, audio, input and export.
- Use **C# everywhere**: the simulation, the tools and the Godot layer. No GDScript.
- Keep the economy, research and culture in **engine-free .NET libraries** (`Evenmere.Core`, `Evenmere.Content`, `Evenmere.Sim`) that build and test with the plain .NET SDK.
- **Target .NET 10 (LTS)** in every project. Task M0-01 verifies that Godot 4.7's `Godot.NET.Sdk` builds a `net10.0` game project. If it can't, use the newest framework Godot supports in all projects and amend this ADR.
- Build the Godot project as part of `Evenmere.sln` through the NuGet `Godot.NET.Sdk`, so `dotnet build` type-checks the game layer without the editor.
- **Pinned versions:** Godot 4.7.2 .NET (`Godot.NET.Sdk/4.7.2`) and .NET SDK 10.0.4xx. Upgrades are deliberate, single-purpose changes.

**Verified 2026-10-19** on the development Mac (Apple Silicon, macOS 27): a throwaway `Godot.NET.Sdk/4.7.2` project targeting `net10.0`, with a `ProjectReference` to a `net10.0` class library, built with `dotnet build` and ran headless in Godot 4.7.2.stable.mono on the .NET 10.0.13 runtime. No fallback is needed.

## Consequences

- Agents verify their work with `dotnet test` in seconds. Only presentation work needs the Godot binary, through the headless smoke test.
- C# web export is still experimental in Godot, so the web is not a target ([ADR-0008](0008-platform-targets.md)).
- Terrain, instancing and city-builder tooling are ours to build; there are few ready-made assets.
- Replacing the engine would mean rewriting only `game/`.
