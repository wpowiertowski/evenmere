@AGENTS.md

## Claude Code notes

- When orchestrating a milestone, follow the dispatch protocol in [docs/implementation/README.md](docs/implementation/README.md). Dispatch each task card to a subagent in its own worktree (`isolation: "worktree"`), never run two tasks whose **Owns** paths overlap at the same time, and merge only after `scripts/verify.sh` passes on the task branch.
- Update the task's status in the milestone file's index table in the same commit that merges it.
