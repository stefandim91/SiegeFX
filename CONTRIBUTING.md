# Contributing to the Utraea fork

This is a fork of [SiegeFX](https://github.com/codingncaffeine/SiegeFX) focused on making the
original Dungeon Siege 1 **Utraean Peninsula** (`MpWorld.dsmap`) fully playable offline.
See [AGENTS.md](AGENTS.md) for the engineering principles and
[docs/UTRAEA_PROJECT.md](docs/UTRAEA_PROJECT.md) for the current milestone.

## Branch protection

`main` is protected. Nothing lands on it except through a merged pull request — direct pushes,
force-pushes, and branch deletion are all rejected. Repository admins can bypass in an emergency;
treat that as an escape hatch, not a workflow.

Reviews are not required to merge, so a solo change is `branch → PR → merge`. Head branches are
deleted automatically once merged.

## Which merge button to press

Rebase-merge is disabled. The remaining two are **not** interchangeable:

| PR contents | Merge method |
| --- | --- |
| A sync from `upstream/main` (codingncaffeine/SiegeFX) | **Create a merge commit** |
| Everything else — our own work | **Squash and merge** |

**Why this matters.** Squashing an upstream sync flattens it into a single new commit with no link
back to upstream's history. Git then loses the merge base, and *every* later sync re-presents the
same conflicts from scratch, forever. A real merge commit preserves the ancestry, so the next sync
only has to reconcile what actually changed since the last one.

Keeping the fork cheap to sync is a standing project constraint, so this rule is load-bearing.
For our own branches squash is the default: one reviewable commit per change on `main`, clean
revert, clean `git bisect`, and the work-in-progress noise stays in the PR.

Squash commit messages are generated from the **PR title and body**, not from the branch's
individual commits — so the PR description *is* the commit message on `main`. Write it that
way: short, explaining what changed and why, with no checklists or review chatter that you
would not want to read in `git log` a year from now. `.github/pull_request_template.md` is
shaped for this; delete its comments before merging.

## Syncing from upstream

```
git fetch upstream
git switch -c sync/upstream-YYYY-MM-DD
git merge upstream/main
git push -u origin HEAD
```

Open the PR, resolve any conflicts on the branch, and land it with **Create a merge commit**.

## Agent harness

Agent tooling is your own choice. Harness directories — `.codex/`, `.claude/`, `.cursor/` — are
gitignored, so nothing about how you orchestrate is imposed on anyone else.

What *is* shared is in [AGENTS.md](AGENTS.md) under **Subagent roles**: three delegated roles
(`explorer`, `implementation`, `reviewer`) and the contract each one has to satisfy. Bind them
to whatever models you use; keep the read-only sandboxing, which is what makes the explorer and
reviewer roles worth having.

## Scope

Prefer generic Dungeon Siege engine semantics over quest-specific workarounds, and keep
fork-specific opinionated features (Solo/Party Adventure UX, exploration-guidance overlays,
remastered assets) separate from generic engine fixes that could go upstream. AGENTS.md has the
detail.
