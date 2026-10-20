@AGENTS.md

## Claude Code notes

- When orchestrating a milestone, follow the dispatch protocol in [docs/implementation/README.md](docs/implementation/README.md).
- **Send coding tasks to Sonnet subagents** (Agent tool with `model: "sonnet"` and `isolation: "worktree"`). Never run two tasks whose **Owns** paths overlap at the same time.
- **Always review the subagent's code before committing.** Subagents leave their changes uncommitted. Read the full diff and run `scripts/verify.sh` in the worktree yourself.
- **If you have feedback, send it back to the same subagent** with SendMessage, and repeat until the change is right. Don't silently fix it yourself or start a fresh agent.
- After approval, commit as `<ID>: <title>` and merge into `main` (before M1-29) or open a PR (after it). Update the task's status in the milestone index table in the same change.
- This machine's non-login shells don't have `dotnet` on `PATH`. Use `export PATH=/usr/local/share/dotnet:$PATH` before .NET commands, and tell subagents to do the same.
