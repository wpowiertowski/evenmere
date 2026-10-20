# Scenario files

A scenario is a JSON file that scripts commands over time against a fresh simulation, then asserts outcomes and a golden state hash. `dotnet test` runs every `*.json` under this folder, subfolders included, each as its own test case named after the file (`empty_world_10k.json`, `harness/test_commands.json`). Adding a scenario is adding a file; no test code changes.

The harness lives in `tests/Evenmere.Sim.Tests/Scenarios/`.

## Format

```json
{
  "description": "optional text for people",
  "content": "tests/fixtures/content/minimal",
  "map": null,
  "seed": 42,
  "ticks": 10000,
  "commands": [
    { "tick": 5, "type": "GrantGoods", "args": { "good": "timber", "amount": "12.5" } }
  ],
  "expect": {
    "hash": "0123456789abcdef",
    "events": [
      { "type": "GoodsGranted", "atLeast": 1 },
      { "type": "CommandRejected", "exactly": 0 }
    ],
    "stock": { "timber": ">= 10" }
  }
}
```

`GrantGoods` is one of the harness's test-only commands; real commands such as `PlaceBuilding` are used the same way. Until their system's task lands, they are rejected with `NotImplemented`. The hash above is a placeholder: use the one your run reports (see Golden updates).

The file is read strictly, like content: no comments, no trailing commas, no duplicate properties, and no unknown properties, so a typo such as `"tick"` for `"ticks"` is an error. Every error message starts with the file and the property it is about, and names the index of a bad command (`commands[2].args: ...`).

| Property | Type | Meaning |
| --- | --- | --- |
| `description` | string, optional | Free text for people. Shown in failure reports. |
| `content` | string, required | The content folder, relative to the repository root. |
| `map` | string or null, optional | A map fixture id, or `null` (or absent) for the empty world. **Maps do not exist until M1-01**, so a non-null value fails with a clear "not supported until M1-01" error. |
| `seed` | integer, required | The world seed (an unsigned 64-bit integer). |
| `ticks` | integer, required | How many ticks to run, 0 or more. |
| `commands` | array, optional | The scripted commands (below). |
| `expect` | object, optional | What the run must satisfy (below). With nothing in it the scenario only has to run. |

### Commands

Each entry is `{ "tick": n, "type": "<CommandTypeName>", "args": { ... } }`. `args` is optional for a command with no parameters.

**When a command runs.** A command whose `tick` is T is enqueued just before the simulation ticks from T-1 to T. The simulation applies queued commands at the start of the next tick, so the command is applied on tick T, and anything it emits carries tick T. Consequences:

- `tick` runs from 1 to `ticks`. There is no tick 0 (the world starts at tick 0 and nothing has run), so use 1 for "before anything has run". A `tick` outside 1 to `ticks` is an error.
- A command at tick `ticks` is applied on the last tick.
- Commands with the same `tick` are enqueued in file order. Commands may appear in any tick order in the file; they are applied in tick order, and in file order within a tick.
- Commands are queued, not instant. A command that fails validation produces a `CommandRejected` event on tick T, and the world is unchanged.

**`type`** is the simple type name of a `Command` record, case-sensitive. The harness finds them by reflection:

- every concrete `Command` in `Evenmere.Sim` is available automatically, so a new command needs no registration;
- test-only commands in the test assembly are available when marked `[ScenarioCommand]` (see `harness/` below).

An unknown name fails with the list of known names, and a "did you mean" hint when only the case differs. Two commands with the same simple name make the registry fail loudly, so keep command names unique.

**`args`** are read with System.Text.Json into the record's constructor parameters:

- property names match the parameter names ignoring case (`"good"`, `"Good"`);
- a missing parameter, an unknown property, a `null` for a non-nullable parameter and a value of the wrong type are all errors;
- content IDs (`GoodId`, `BuildingTypeId`, ...) are written as the item's string id (`"timber"`), and unknown ids fail;
- `Amount` is written as a decimal string (`"12.5"`) or a JSON number, with the same grammar as content quantities (at most three decimals);
- `EntityId` is a JSON integer.

Every command is resolved and deserialised before the first tick, so a mistake in the last command fails at once. A command parameter of any other value type needs a converter in `ScenarioJson.cs`; without one System.Text.Json would silently read it as zero. The task that adds such a command adds the converter.

### Expectations

Checked in this order, and the report shows the first that fails (and lists the rest):

1. `events`: a list of `{ "type": "<SimEvent type name>", "atLeast": n }` or `{ "type": "...", "exactly": n }`, counted over every event the run drained. `type` is the simple name of a `SimEvent` record in `Evenmere.Sim` (or the test assembly); an unknown name fails with the list of known ones. Use `{ "type": "CommandRejected", "exactly": 0 }` to assert that nothing was refused.
2. `stock`: an object from a good's string id to `"<op> <quantity>"`, read from the island storage after the last tick. The operators are `>=`, `<=`, `==`, `>` and `<`; the quantity is read with `Amount.Parse`. Whitespace around the quantity is optional (`">=10"`).
3. `hash`: 16 lowercase hex characters, the state hash (`ComputeStateHash()`) after the last tick. Absent (or `null`) means no golden check.

## Failure reports

A failed scenario reports the file, the scenario's settings, the first failing expectation, the expected and actual hash, and the last 20 events with their ticks and types. Example (the sample's seed changed from 42 to 43):

```
Scenario FAILED: tests/fixtures/scenarios/empty_world_10k.json
  description: An empty world on the minimal content, ticked 10,000 times with no commands. ...
  content: tests/fixtures/content/minimal   seed: 43   ticks: 10000
  commands: 0

First failing expectation: hash: the state hash is 6efda26911e0e8ff, but the file expects 6114913bc26ea39a.

  expected hash: 6114913bc26ea39a
  actual hash:   6efda26911e0e8ff

Events: none were emitted.

If the behaviour change is intended, rewrite the golden hash with EVENMERE_UPDATE_GOLDEN=1 dotnet test tests/Evenmere.Sim.Tests -c Release, and explain the change in the commit body.
```

## Golden updates

```sh
EVENMERE_UPDATE_GOLDEN=1 dotnet test tests/Evenmere.Sim.Tests -c Release
```

With the variable set to `1`, a scenario whose hash differs from `expect.hash`, or has none, gets `expect.hash` rewritten in the file and passes. Only that value changes: the file is edited as text, so property order, spacing, line endings and the description stay as they were. When the file has no `hash` yet it is added as the first property of `expect` (creating `expect` if needed), indented by the step the file already uses, which is two spaces in a file that follows `.editorconfig`.

- The hash is rewritten only when every other expectation held. A failing event or stock expectation still fails the test, and the file is left alone.
- The notice (`EVENMERE_UPDATE_GOLDEN=1: rewrote expect.hash in <file>: <old> -> <new>`) goes to the test's output, which the default `dotnet test` summary hides. Add `--output Detailed` to see it; `git diff` always shows the change.
- Update only for an intended behaviour change, and say why in the commit body (AGENTS.md hard rule 6). Review the diff: a hash that moves in a scenario you did not expect to affect is a bug, not a golden to refresh.
- Without the variable a mismatch fails the test, and a missing hash is simply not checked.

## Folders

- `*.json` at the top level are gameplay and skeleton scenarios. Gameplay scenarios are added by each M1 task and by M1-13.
- `harness/` holds scenarios for the harness itself. They use the test-only commands in `tests/Evenmere.Sim.Tests/Scenarios/HarnessCommands.cs` (`RecordTick`, `GrantGoods`, `UnservedCommand`), which prove the command path before real commands exist. They are discovered and run like any other scenario, so they must pass.
- A scenario that is meant to fail must not be committed here, since every file here must pass. The harness's own tests write such files to a temporary folder instead.

## Samples

- `empty_world_10k.json`: the empty world on the minimal content, seed 42, 10,000 ticks, no commands, with its golden hash. It pins the bare skeleton: clock, RNG streams and state layout.
- `harness/test_commands.json`: scripted commands applied on their ticks, goods landing in the island stock, a handler rejection and an unserved command, with event and stock expectations and a golden hash.

## Adding a test-only command

```csharp
[ScenarioCommand(typeof(MyHandler))]
internal sealed record MyCommand(string Label) : Command;

internal sealed class MyHandler : ICommandHandler<MyCommand> { ... }
```

The handler needs a public parameterless constructor and must implement `ICommandHandler<MyCommand>`. The runner registers a new instance on each simulation, only for scenarios that use the command. `[ScenarioCommand]` without a handler registers the name only, so the simulation answers it with `CommandRejected` (`UnknownCommand`).
