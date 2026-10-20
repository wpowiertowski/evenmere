# 0002. Project layering and boundary enforcement

Status: Accepted · Date: 2026-10-18

## Context

The v0.1 plan had four .NET projects: Sim, Content, Sim.Tests and Tools. Content definitions need typed IDs and fixed-point quantities, but those types belonged to Sim, while Sim had to load Content. That creates a dependency cycle unless the shared primitives move out. Rules held only by convention also erode quickly when many agents work in parallel.

## Decision

Projects and their allowed references:

| Project | May reference |
| --- | --- |
| `Evenmere.Core` | BCL plus `System.IO.Hashing` only |
| `Evenmere.Content` | Core, System.Text.Json, JsonSchema.Net (pinned to 8.x, the last MIT release; 9.x+ ships under a revenue-based fee EULA, so never upgrade without a licence review) |
| `Evenmere.Sim` | Content, Core, MessagePack |
| `Evenmere.Tools` | Sim, Content, Core, System.CommandLine |
| `Evenmere.Game` (in `game/`) | Sim, Content, Core, Godot |
| `tests/*` | the project under test, plus test libraries |

Enforcement:

1. **Project references:** a disallowed reference does not compile.
2. **Architecture tests** in `Evenmere.Sim.Tests` that reflect over the assemblies:
   - No Core, Content or Sim assembly references `GodotSharp`, and Core references nothing beyond the BCL and `System.IO.Hashing`.
   - No type reachable from `WorldState`'s fields (recursively) has a field of type `float`, `double`, `decimal`, `Dictionary<,>` or `HashSet<>`.
   - Every `Command` subtype has exactly one handler.
3. **`Microsoft.CodeAnalysis.BannedApiAnalyzers`** in Core and Sim, with the list in `src/BannedSymbols.txt`.
4. **System folders:** each gameplay system lives in `src/Evenmere.Sim/Systems/<Name>/` and exposes an `I<Name>Queries` interface. A system reads another system's state only through that interface.

The test framework is xUnit v3, with no FluentAssertions (its licence changed in v8). Shouldly is allowed.

## Consequences

- One task maps to one folder, so parallel agents rarely conflict.
- Cross-system needs surface as query-interface changes, which are small and reviewable.
- `Evenmere.Core` must stay tiny; adding to it needs an ADR.
