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
---

# /prepare-merge — Clean up a feature branch before merging to dev

Rebase the current feature branch onto `origin/dev`, squash its commits into
a few meaningful commits, and drop this branch's superpowers working docs —
so dev history records the feature, not the fix trail.

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

### 3. Plan the commit structure

Read `git log --oneline origin/dev..HEAD` and the full diff
`git diff origin/dev..HEAD`. Default to **one commit for the whole
feature**. Split out a second or third commit only for a genuinely
separate concern — shared infrastructure the feature merely builds on
(e.g. a change to a core component like VirtualList), or a self-contained
change that could land or be reverted independently. Never produce more
than 3 commits without the user's explicit ok. Original commit boundaries
are not a reason to keep commits: well-written same-feature commits still
get folded, and `fix`/review-comment commits always fold into what they
fix. Group at file level only — a file whose changes span groups goes
where its primary change belongs; don't attempt hunk splitting. Messages
follow repo convention: `type(scope): summary`, scopes as used on this
branch and recent dev history; when folding several commits loses
important detail, keep the summary line and add a short body naming what
was folded in. Show the plan (groups + messages) in one short list, then
proceed — the backup branch makes this reversible.

### 4. Identify superpowers docs to drop

```bash
git diff --diff-filter=A --name-only $(git merge-base HEAD origin/dev) HEAD -- docs/superpowers/
```

Only files **added** by this branch are deleted — plans and specs are
working documents that die when the feature ships. Pre-existing files under
`docs/superpowers/`, including ones this branch merely modified, are living
docs and stay untouched.

### 5. Squash

```bash
git reset --mixed $(git merge-base HEAD origin/dev)
rm <files from step 4>
# then per planned group:
git add <group paths> && git commit -m "type(scope): summary"
```

Every changed file must be assigned to exactly one group before you start;
after the last commit `git status --short` must be empty.

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
| `git rebase -i` / `git add -p` | They hang here; mixed-reset + regroup instead |
| One commit per original "meaningful" commit | Fold same-feature commits; split only independently-landable concerns |
| Pushing before the user confirms the result | Ask first; then `-bak` goes up before the force-push |
| Overwriting an existing `-bak` on a rerun | It holds the true original; leave it untouched |
