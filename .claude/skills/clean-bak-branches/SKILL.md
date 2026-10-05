---
name: clean-bak-branches
description: |
  Delete the `-bak` backup branches that `/prepare-merge` leaves behind, local
  and on origin, once the work they back up has merged. Use when the user says
  "remove my bak branches", "clean up bak branches", "delete the backups",
  or "/clean-bak-branches".
allowed-tools:
  - Bash
  - AskUserQuestion
---

# /clean-bak-branches — Delete merged `-bak` backup branches

`/prepare-merge` saves the pre-squash history as `<branch>-bak` (`-bak2`,
`-bak-20260917`, … on reruns) and pushes it. Nothing removes those refs after
the PR merges, so they pile up. This skill removes **the current user's** ones.

## Arguments

- none — every `-bak` branch of the current user whose source PR is merged.
- `--dry-run` — list what would be deleted and stop.
- `<pattern>` — only branches whose name contains it (e.g. an issue number).

## Steps

### 1. Collect

```bash
git fetch --prune origin
me=$(git config user.email)
git for-each-ref --format='%(refname)|%(objectname:short)|%(authoremail)' \
    refs/heads refs/remotes/origin \
  | grep -E -- '-bak[0-9a-z-]*\|' | grep -F "<$me>"
```

Ownership is the author of the branch tip. The `-bak` refs of other people
are theirs to delete — never touch them, even when asked for "all".

A branch checked out in a worktree (`git worktree list`) is skipped and
reported.

### 2. Check that the backup is no longer needed

Strip the `-bak…` suffix to get the source branch, then:

```bash
gh pr list --head <source-branch> --state all --json number,state
```

| Source PR | Action |
|---|---|
| `MERGED` | delete |
| `OPEN`, `CLOSED` unmerged, or no PR | keep, unless the user confirms via AskUserQuestion — the backup may be the only copy of the unsquashed history |

### 3. Delete

Print the `name sha` list first: it is the only way back
(`git branch <name> <sha>` works while the commits are still unreachable but
not yet collected).

```bash
git branch -D <local names…>
git push origin --delete <remote names…>
```

One `git push --delete` for all remote names, not one per branch.

### 4. Report

Counts of local and remote refs deleted, what was kept and why, and a rerun
of step 1 showing nothing of the user's is left.
