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
git branch -f backup/<branch-name> HEAD
```

Never rewrite history without this. It is the recovery point and later the
parity baseline's ancestor.

### 2. Rebase onto origin/dev

`git rebase origin/dev` (skip if already based on its tip). On conflicts,
resolve preserving both sides' intent and `GIT_EDITOR=true git rebase
--continue`; if a resolution is not obvious from the code, `git rebase
--abort` and ask the user. Interactive git (`git rebase -i` with an editor,
`git add -p`/`-i`) hangs in this environment — never use it.

Then capture the parity baseline: `REBASED=$(git rev-parse HEAD)`.

### 3. Plan the commit structure

Read `git log --oneline origin/dev..HEAD` and the full diff
`git diff origin/dev..HEAD`. Group the changes into meaningful commits
(1–5 is typical; one is fine for a single logical change — "several" is not
a quota). Keep the original commit boundaries where they were already
meaningful; fold `fix`/review-comment commits into the commit they fix.
Group at file level only — a file whose changes span groups goes where its
primary change belongs; don't attempt hunk splitting. Messages follow repo
convention: `type(scope): summary`, scopes as used on this branch and
recent dev history. Show the plan (groups + messages) in one short list,
then proceed — the backup branch makes this reversible.

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

- `git status --short` → empty.
- `git diff $REBASED HEAD` → shows *only* the step-4 doc deletions.
  Compare against the post-rebase tip, not the backup branch — the backup
  predates the rebase, so diffing it mixes in upstream changes.
- Show `git log --oneline origin/dev..HEAD` to the user.

### 7. Report — do not push

History was rewritten, so the remote branch now requires
`git push --force-with-lease`. Never push unless the user explicitly asks
in this conversation. Report: the final commit list, deleted doc files, the
backup branch name (suggest deleting it after the merge lands), and the
force-push command the user can run.

## Common mistakes

| Mistake | Fix |
|---|---|
| Rewriting with no backup ref | Step 1 first, always |
| Parity-diffing against the backup branch | Use `$REBASED` (post-rebase tip) |
| Deleting pre-existing `docs/superpowers/` files | Only `--diff-filter=A` files from this branch |
| `git rebase -i` / `git add -p` | They hang here; mixed-reset + regroup instead |
| Pushing (even force-with-lease) unprompted | Report the command; the user runs or requests it |
