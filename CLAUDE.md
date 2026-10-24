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
- After approval, commit as `<ID>: <title>` and land it through a PR (see below). Update the task's status in the milestone index table in the same change.
- **PRs are stacked, monorepo-style.** `main` is protected by a ruleset: a PR is required, both `verify` checks must pass, the branch must be up to date, history is linear, and force pushes are blocked. When several tasks are in flight:
  - **One commit per task.** Squash each task to a single commit (`git reset --soft <base>` and commit) before pushing.
  - **Stack the branches.** Each task branch sits on top of the one below it, and its PR's base is that branch. The bottom PR's base is `main`. Each PR shows only its own diff, but its CI runs on the whole stack below it, so check results always match the linear history that will land.
  - **Rebase, never merge, to update.** When a lower task changes, or `main` moves, rebase every branch above it, bottom up, and `git push --force-with-lease`. Force pushes are allowed on task branches; only `main` is protected. CI then reruns for each layer. Never merge `main` into a task branch.
  - **Land atomically.** When every PR in the stack is green, fast-forward the bottom branch to the top commit (`git push origin <top-sha>:<bottom-branch>`), wait for the bottom PR's checks on that commit, and **rebase-merge** the bottom PR. `main` then moves once and gains the whole stack, still one commit per task. Close the upper PRs with a note naming the PR they landed in, and delete their branches.
  - A single, unstacked PR can be squash-merged once green.
- This machine's non-login shells don't have `dotnet` on `PATH`. Use `export PATH=/usr/local/share/dotnet:$PATH` before .NET commands, and tell subagents to do the same.
