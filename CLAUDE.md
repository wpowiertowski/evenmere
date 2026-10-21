@AGENTS.md

## Claude Code notes

- When orchestrating a milestone, follow the dispatch protocol in [docs/implementation/README.md](docs/implementation/README.md).
- **Send coding tasks to the external Pi coder first**, to keep the Claude token spend down. Use Sonnet subagents (Agent tool with `model: "sonnet"` and `isolation: "worktree"`) as the fallback. Never run two tasks whose **Owns** paths overlap at the same time.
  - Make a worktree per task: `git worktree add .claude/worktrees/pi-<ID> -b task/<ID> main`.
  - Write the brief to a file outside the repo, and run one round with `pi -p --mode json --session-dir <dir> --session-id evenmere-<ID> @<brief>`. Run it from the worktree, through an interactive zsh so the user's `pi` alias applies, with stdin closed.
  - Feedback rounds reuse the same `--session-id`, so Pi keeps its context. Read the final assistant message from the JSON event log.
- **Always review the implementing agent's code before committing.** Agents leave their changes uncommitted. Read the full diff and run `scripts/verify.sh` in the worktree yourself.
- **If you have feedback, send it back to the same agent**: SendMessage for a subagent, or another round on the same session ID for Pi. Repeat until the change is right. Don't silently fix it yourself or start a fresh agent.
- **In the main checkout, stage explicit paths**, never `git add -A`. Local untracked files, such as operator notes, must not be swept into commits.
- After approval, commit as `<ID>: <title>` and merge into `main` (before M1-29) or open a PR (after it). Update the task's status in the milestone index table in the same change.
- This machine's non-login shells don't have `dotnet` on `PATH`. Use `export PATH=/usr/local/share/dotnet:$PATH` before .NET commands, and tell subagents to do the same.
