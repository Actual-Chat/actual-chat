---
name: prepare-merge
description: |
  Use when a feature branch is complete and its history must be cleaned up
  before merging back to dev — as the final step before creating or merging
  a PR, or when the user says "prepare for merge", "squash the branch",
  "clean up branch history", or "/prepare-merge".
allowed-tools:
  - Bash
  - Read
  - Grep
  - Glob
  - AskUserQuestion
---

# /prepare-merge — Clean up a feature branch before merging to dev

Rebase the current feature branch onto `origin/dev`, regroup its commits into
a history worth keeping, and drop this branch's superpowers working docs — so
dev history records the work, not the fix trail.

**How much to squash is the user's call, not yours.** The skill works out the
branch's real concerns, presents up to three plans — Perfect, Medium, Min.
commits — and builds whichever one the user picks. There is no default commit
count and no target to squash down to.

## User-invocable

When the user types `/prepare-merge`, run this skill.

## Preconditions (abort and tell the user if violated)

- Current branch is a feature branch — not `dev`, `master`, or `release/*`.
- Working tree is clean (`git status --short` empty).
- `git fetch origin dev` succeeded.

## Steps

### 1. Safety backup

```bash
git branch <branch-name>-bak HEAD
```

Never rewrite history without this. If `<branch-name>-bak` already exists
from a previous run, keep it as-is — it points at the true original, and
overwriting it with already-rewritten history would defeat the backup.

### 2. Rebase onto origin/dev

`git rebase origin/dev` (skip if already based on its tip). On conflicts,
resolve preserving both sides' intent and `GIT_EDITOR=true git rebase
--continue`; if a resolution is not obvious from the code, `git rebase
--abort` and ask the user. Interactive git (`git rebase -i` with an editor,
`git add -p`/`-i`) hangs in this environment — never use it.

Then capture the parity baseline: `REBASED=$(git rev-parse HEAD)`.

### 3. Plan the commit structure — always offer the choice

Read `git log --oneline origin/dev..HEAD` and the full diff
`git diff origin/dev..HEAD`, and work out what genuinely distinct
concerns the branch holds — separate features, separate fixes,
infrastructure the feature merely builds on.

That list of concerns is the input to the three plans below. **Deciding on
a structure yourself and presenting it as "the plan" is the one thing this
step must not do** — the plans are cheap to draw up, and which trade-off
is right depends on how the user wants this branch to read in dev history.

Original commit boundaries are not the answer: `fix`/review-comment
commits always fold into what they fix, and several commits on one
concern fold together. But **never merge two unrelated concerns just to
get the count down** — a smaller history is not the goal, an honest one
is.

Split at whatever granularity gives the cleanest history, **including
within a single file**: a file touched by two concerns gets its hunks
split between them. `git add -p` hangs in this environment, so stage
hunks through a patch instead:

```bash
git diff <base> HEAD -- <file> > tmp/f.patch   # then trim it to the wanted hunks
git apply --cached tmp/f.patch
```

Then offer the user **up to three plans** and let them choose:

| Plan | What it does |
|---|---|
| **Perfect** | One commit per distinct concern. Nothing unrelated is ever squashed together. The most commits, and the most faithful history. **This can come out identical to the branch's existing history** — if the commits already map one-to-one onto the concerns, Perfect means "leave the splits alone", and say so rather than inventing a regrouping. |
| **Medium** | Each real feature keeps its own commit, but secondary work is consolidated — all the small fixes into one `fix(...)`, all the docs into one `docs(...)`, and so on. |
| **Min. commits** | Aggressively minimal, typically ≤3: the whole feature as one commit, plus only those concerns that genuinely could not ride along with it. |

Offer only the plans that actually differ for this branch — a
single-concern branch collapses all three into the same one commit, and
then there is nothing to ask. Present each plan as its list of commit
messages so they can be compared, ask which to use, and build that one.
Label Perfect as "unchanged" when it reproduces the current history, so
the user can see that one of the options is to keep what is already there.

Messages follow repo convention: `type(scope): summary`, scopes as used
on this branch and recent dev history; when folding several commits loses
important detail, keep the summary line and add a short body naming what
was folded in.

### 4. Identify superpowers docs to drop

```bash
git diff --diff-filter=A --name-only $(git merge-base HEAD origin/dev) HEAD -- docs/superpowers/
```

Only files **added** by this branch are deleted — plans and specs are
working documents that die when the feature ships. Pre-existing files under
`docs/superpowers/`, including ones this branch merely modified, are living
docs and stay untouched.

### 5. Squash

If the chosen plan reproduces the branch's existing history **and** step 4
found no docs to drop, there is nothing to rewrite: skip to step 6, and
tell the user the history was already right. Otherwise the mixed reset
below rebuilds it — that is also how the superpowers docs get stripped
out of history rather than deleted in a trailing commit.

```bash
git reset --mixed $(git merge-base HEAD origin/dev)
rm <files from step 4>
# then per planned group:
git add <group paths> && git commit -m "type(scope): summary"
```

Every change must belong to exactly one group before you start — a file
split across groups is staged hunk-by-hunk via `git apply --cached`, as
in step 3. After the last commit `git status --short` must be empty.

### 6. Verify (mandatory before claiming done)

- `git branch --show-current` → still the feature branch. A `git rebase
  --onto` given a non-branch revision (like `HEAD~0`) rebases a detached
  HEAD and leaves the branch ref behind — later steps would then rewrite
  and push a stale tip. If detached, repoint: `git branch -f <branch>
  <verified-tip>` and check it out.
- `git status --short` → empty.
- `git diff $REBASED HEAD` → shows *only* the step-4 doc deletions.
  Compare against the post-rebase tip, not the backup branch — the backup
  predates the rebase, so diffing it mixes in upstream changes.
- Show `git log --oneline origin/dev..HEAD` to the user.

### 7. Confirm, then push with the remote backup

Show the user the final commit list and the deleted doc files, and ask
whether the new history is fine. Do not push anything before that
confirmation. Once the user confirms:

```bash
git push origin <branch-name>-bak
git push --force-with-lease origin <branch-name>
```

The backup goes up first — with the pre-rewrite history on origin, no
outcome of the force-push can lose code, and anyone can check the old
branch out from the remote. Afterwards verify the branch tracks its
remote cleanly (`git status` shows "up to date with origin/<branch>").

Report both pushed refs and remind the user to delete the backup after
the merge lands: `git branch -D <branch-name>-bak && git push origin
:<branch-name>-bak`.

## Common mistakes

| Mistake | Fix |
|---|---|
| Rewriting with no backup ref | Step 1 first, always |
| Parity-diffing against the backup branch | Use `$REBASED` (post-rebase tip) |
| Deleting pre-existing `docs/superpowers/` files | Only `--diff-filter=A` files from this branch |
| `git rebase -i` / `git add -p` | They hang here; mixed-reset + regroup, and `git apply --cached` for hunks |
| One commit per original "meaningful" commit | Boundaries come from the concerns in the diff, not from the old history |
| Squashing unrelated concerns to hit a lower count | Only the plan the user picked decides how much gets folded |
| Building a plan without offering the alternatives | Present Perfect / Medium / Min. commits and let the user choose |
| Regrouping a history that was already correct | Perfect may well be "unchanged" — offer it as such |
| Pushing before the user confirms the result | Ask first; then `-bak` goes up before the force-push |
| Overwriting an existing `-bak` on a rerun | It holds the true original; leave it untouched |
