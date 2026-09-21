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

## Merging

**Squash and merge** is the only method enabled, so there is nothing to choose. Merge commits and
rebase-merge are both off: one reviewable commit per change on `main`, clean revert, clean
`git bisect`, and the work-in-progress noise stays in the PR.

Squash commit messages are generated from the **PR title and body**, not from the branch's
individual commits — so the PR description *is* the commit message on `main`. Write it that
way: short, explaining what changed and why, with no checklists or review chatter that you
would not want to read in `git log` a year from now. `.github/pull_request_template.md` is
shaped for this; delete its comments before merging.

## Syncing from upstream

Upstream syncs are the one exception to everything above, and they must **never** be squashed.
Squashing a sync flattens it into a commit with no link back to upstream's history: git loses the
merge base, and every later sync then re-presents the same conflicts from scratch, forever. A real
merge commit preserves the ancestry, so the next sync only reconciles what actually changed since
the last one. Keeping the fork cheap to sync is a standing project constraint, so this is
load-bearing.

Since squash is the only method the merge button offers, a sync is done locally and pushed
straight to `main` using the repository-admin bypass:

```
git switch main
git fetch upstream
git merge upstream/main      # resolve conflicts here
git push origin main
```

This is the only sanctioned direct push to `main`. If the merge is large or risky, do it on a
branch first and verify it builds, then fast-forward `main` onto that branch — still a real merge
commit, still no squash, just checked before it lands.

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
