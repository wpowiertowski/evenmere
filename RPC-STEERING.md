# Steering Pi via `--mode rpc` — Launch & Interaction Guide

This is a local operator note (not part of Evenmere design docs).
It tells an external process how to launch this agent in RPC mode
and steer it mid-run. Authoritative sources: `pi --help`,
`docs/rpc.md`, `docs/rpc-commands.md`, `docs/json.md` in the
installed Pi package (`@earendil-works/pi-coding-agent`).

## 1. What RPC mode is

* Pi runs as a **long-lived subprocess** you control over **JSONL on stdin/stdout**.
* For language-independent integration, process isolation, IDEs, custom UIs.
* Alternative: in-process Node/Bun → use the **SDK** (`docs/sdk.md`).
  Subprocess TypeScript → use the exported **`RpcClient`**
  (`examples/rpc-client.ts`, `src/modes/rpc/rpc-client.ts`).
* RPC mode **rejects `@file` CLI prompt args**. Send everything via the
  `prompt` command.

## 2. Launch

Basic:

```bash
pi --mode rpc --no-session
```

Common variants:

```bash
# Pinned provider/model + named session (replace with any `pi list-models` entry)
pi --mode rpc --provider <provider> --model <model-id> \
  --name "worker-1"

# Ephemeral (no session file written)
pi --mode rpc --no-session

# Resume / fork / custom dir
pi --mode rpc --continue
pi --mode rpc --resume <session-id-or-file> --session-dir ~/.pi/agent/sessions
pi --mode rpc --session /path/to/session.jsonl
pi --mode rpc --tools read,bash,edit,write
pi --mode rpc --thinking high
```

Working directory matters: Pi uses `cwd` for relative paths, tools,
and (with `remote-pi`) agent-network room scoping. `cd` first or spawn
with the right `cwd`.

Check flags on your install — `pi --help` is authoritative; extension
flags (e.g. `--plan`) may add more.

## 3. Process plumbing

| Stream | Use |
|---|---|
| `stdin` (write) | One JSON object + `\n` per **command** |
| `stdout` (read) | `response` records + **session events**, one JSON object + `\n` each |
| `stderr` | Human diagnostics / logs only. **Never parse as protocol.** |

Framing rules (strict):

1. One complete JSON object per record, terminated by LF (`\n`). Accept optional preceding `\r` (CRLF).
2. Split **only on LF** as bytes/UTF-8. Do **not** use line readers that split on `U+2028` / `U+2029` — valid inside JSON strings. Node `readline` is explicitly unsuitable.
3. **Read stdout continuously.** Pi respects backpressure; a stalled reader stalls Pi when the pipe fills.
4. Honor stdin backpressure when writing.
5. `stdout` is reserved for protocol records.

Shutdown:

* Close child `stdin` → orderly shutdown (Pi disposes runtime, then exits).
* Also handle signals / unexpected exits / startup failure in the parent.
* An extension can request shutdown via its context; Pi finishes the current command or waits for `agent_settled` after a run.

## 4. Command / response correlation

Every command accepts an optional string `id`. The matching `response` repeats it:

```json
{"id":"req-1","type":"get_state"}
{"id":"req-1","type":"response","command":"get_state","success":true,"data":{"...":"..."}}
```

* Use unique IDs when >1 command can be outstanding. Handling is async — correlate by `id`, not order.
* `success:false` → `{"type":"response","command":"<cmd>","success":false,"error":"..."}`.
* Malformed JSON → `{"type":"response","command":"parse","success":false,"error":"..."}` with no `id`.
* `success:true` on `prompt`/`steer` only means **accepted / queued / handled**, not work-complete. Post-accept failures surface in the event stream.
* Exception: `bash_execution_update` events repeat the originating `bash` command's `id`. `extension_ui_response` uses the `extension_ui_request` ID and produces no normal `response`.

## 5. Run lifecycle — what to wait for

```
prompt → response{disposition: started|queued|handled}
      → agent_start, turn_start, message_start/update/end, tool_execution_*,
        turn_end, ..., agent_end, ..., agent_settled
```

* `prompt` response `disposition`:
  * `started` — accepted, new run beginning.
  * `queued` — accepted during a run, will run after.
  * `handled` — consumed by an extension command / input handler; **no run starts, do not wait for `agent_settled`**.
* `agent_end` = one low-level run ended (`messages`, `willRetry`).
  Retries, overflow recovery, compaction, steering, follow-ups can still continue.
* `agent_settled` (`aborted: bool`) = Pi will not continue automatically. **Wait for this** when you need quiescence.
* Install the event listener **before** `prompt` (fast completions can race). `RpcClient.promptAndWait()` does this; manual clients: subscribe, then `prompt()`, then `waitForIdle()` only while a run is active.

## 6. Steering an active run

### 6a. `prompt` with `streamingBehavior` (idle or busy)

```json
{"id":"p2","type":"prompt","message":"New instruction","streamingBehavior":"steer"}
```

* Omit `streamingBehavior` while streaming → error.
* `"steer"` — deliver after current assistant turn finishes its tool calls, before next LLM call.
* `"followUp"` — deliver only when agent stops.
* `prompt` also expands `/skill:name`, `/template`, and executes `/extension-commands` immediately even during streaming (extension runs its own `pi.sendMessage()`).

With image:

```json
{"type":"prompt","message":"What's in this image?",
 "images":[{"type":"image","data":"<base64>","mimeType":"image/png"}]}
```

### 6b. `steer` (preferred mid-run redirect)

Delivered after current turn's tool calls, before next LLM call. Expands skills/templates. Extension commands **not** allowed (use `prompt`).

```json
{"type":"steer","message":"Stop and do X instead"}
{"type":"response","command":"steer","success":true,"data":{"disposition":"queued"}}
```

`disposition`: `queued` (sitting in queue, may still be cleared/reordered) or `handled` (input handler consumed it).

### 6c. `follow_up` (run-after-idle)

Delivered only when no more tool calls or steering messages remain.

```json
{"type":"follow_up","message":"After you're done, also summarize"}
{"type":"response","command":"follow_up","success":true,"data":{"disposition":"queued"}}
```

### 6d. Queue modes

```json
{"type":"set_steering_mode","mode":"one-at-a-time"}
{"type":"set_follow_up_mode","mode":"one-at-a-time"}
```

* `steeringMode`: `"all"` (drain all steers after each turn) vs `"one-at-a-time"` (default, one steer per completed turn).
* `followUpMode`: `"all"` vs `"one-at-a-time"` (default, one per completion).
* Watch `queue_update` events: `{"type":"queue_update","steering":[...],"followUp":[...]}` — full current queues.

### 6e. `abort` / `clear_queue` (Esc semantics)

```json
{"type":"clear_queue"}
{"type":"response","command":"clear_queue","success":true,
 "data":{"steering":["..."],"followUp":["..."]}}
{"type":"abort"}
{"type":"response","command":"abort","success":true}
```

* Send `clear_queue` **before** `abort` to implement Esc; restore returned text in your editor. `abort` alone leaves queued messages in place and they will still run.
* `abort` waits for idle before responding. `agent_settled` arrives with `"aborted":true`.
* `abort_retry` cancels a transient-error retry delay specifically.

## 7. Other useful commands (same pipe)

* State: `get_state` (model, thinkingLevel, isStreaming/isCompacting, steeringMode/followUpMode, sessionFile/Id/Name, messageCount), `get_messages` (full `AgentMessage[]`).
* Model: `set_model` (`provider`+`modelId`), `cycle_model`, `get_available_models`, `set_thinking_level` (`off|minimal|low|medium|high|xhigh|max`), `cycle_thinking_level`, `get_available_thinking_levels`.
* Context: `compact` (optional `customInstructions`), `set_auto_compaction`, `set_auto_retry`.
* Shell: `bash` (`command`, optional `excludeFromContext`, optional `id` to tag `bash_execution_update` stream events) → response has `output|exitCode|cancelled|truncated (+fullOutputPath)`. Result is stored as a context message and folded into the next `prompt` as `Ran \`...\`` unless excluded.
* Session: `new_session` (optional `parentSession`), `switch_session`, `fork`, `clone`, `get_entries`, `get_fork_messages`, `get_tree`, `get_last_assistant_text`, `set_session_name`, `get_session_stats`, `export_html`.
* See `docs/rpc-commands.md` for the full list and `docs/rpc-extension-ui.md` for dialog/notification forwarding.

## 8. Events to consume (`docs/json.md`)

Same shapes in `--mode json` (one-shot + leading `session` header) and `--mode rpc` (long-lived, no header — use `get_state`).

* `agent_start` / `agent_end{messages,willRetry}` / `agent_settled{aborted}`
* `turn_start` / `turn_end{message,toolResults}` (one assistant response + its tool calls)
* `message_start{message}` / `message_update{usage,assistantMessageEvent}` / `message_end{message}` (`message_end.message` is authoritative; `message_update` deltas are `text_start|text_delta|text_end`, `thinking_*`, `toolcall_start|delta|end`, keyed by `contentIndex`; buffer deltas for display, replace with `*_end.content` / `message_end`)
* `tool_execution_start|update|end{toolCallId,toolName,args,result,isError,durationMs,partialResult}`
* `bash_execution_update` (only for `bash` with `id`)
* `queue_update`, `session_info_changed`, `thinking_level_changed`, `entry_appended`
* `compaction_start{reason: manual|threshold|overflow}` / `compaction_end{result:{summary,firstKeptEntryId,tokensBefore,estimatedTokensAfter,usage}}`
* Retry: `summarization_retry_scheduled|attempt_start|finished`

Minimal Python client (binary-safe, from `docs/rpc.md`):

```python
import json, subprocess
p = subprocess.Popen(["pi","--mode","rpc","--no-session"],
                     stdin=subprocess.PIPE, stdout=subprocess.PIPE)
assert p.stdin and p.stdout
p.stdin.write(json.dumps({"id":"prompt-1","type":"prompt",
                          "message":"Hello"}).encode()+b"\n")
p.stdin.flush()
while line := p.stdout.readline():
    r = json.loads(line)
    if r.get("type")=="message_update":
        u = r["assistantMessageEvent"]
        if u["type"]=="text_delta":
            print(u["delta"], end="", flush=True)
    elif r.get("type")=="agent_settled":
        print(); break
p.stdin.close(); p.wait()
```

TypeScript: use `RpcClient` from `examples/rpc-client.ts` (needs built CLI at `dist/cli.js`) — handles spawn, IDs, `promptAndWait()` / `waitForIdle()`.

## 9. Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| `prompt` rejected while streaming | Add `"streamingBehavior":"steer"` or `"followUp"` |
| Never see completion | Waiting on `agent_end` not `agent_settled`; or missed fast event (subscribe before `prompt`) |
| Stall / hang | Not draining `stdout`, or stdin backpressure ignored; `stderr` mis-parsed as protocol |
| Garbled framing | Splitting on Unicode separators; split bytes on LF only, strip optional `\r` |
| `parse` error response | Malformed JSON or missing `\n` terminator |
| `steer` seems lost | `disposition:handled` by input handler, or drained/cleared; check `queue_update`, `get_state.pendingMessageCount` |
| Abort doesn't clear intent | Queued steers/follow-ups remain — `clear_queue` first |

## 10. Security / scope notes

* Whoever holds the child `stdin` can run prompts, tools (`bash`), model switches, and session ops with your user privileges. Treat the parent like a local operator — same trust as the terminal itself.
* `remote-pi` relay / mobile-app plaintext visibility (`remote-pi/README.md`, `PROTOCOL.md`) is a separate channel; RPC over anonymous pipes has no network exposure unless the parent adds it.
* Pin `--tools` / `--no-tools`, `--session-dir`, and provider keys to least privilege for unattended parents.
