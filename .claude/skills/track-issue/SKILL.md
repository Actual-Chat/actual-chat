---
name: track-issue
description: |
  Use when work on a task is about to start or has just started — after a design
  or plan is approved, when a feature branch is created or resumed in a new
  session, when the first commit lands on a branch that was only claimed — or
  when the user says "link this to an issue", "find the issue for this", "put it
  on the board", "claim the issue", or "/track-issue".
allowed-tools:
  - Bash
  - Read
  - Grep
  - Glob
  - AskUserQuestion
---

# /track-issue — Tie the current work to a GitHub issue and the board

Every task the team works on has one issue in `Actual-Chat/actual-chat`, on the
org's **Team project** board, assigned to whoever is doing it, in the column
that says how far along it is. This skill makes the work in front of you match
that: it finds the issue (or files one through `/issue`), claims it, sets the
column, and records the issue number on the branch so `/create-pr` and
`/prepare-merge` find it later.

**Nothing here happens silently.** The pick is always confirmed, and an issue
that belongs to someone else is never re-assigned without asking. Read-only
lookups run freely; every mutation is listed in step 6 and nothing else mutates.

## Arguments

`/track-issue` takes zero or one of:

- `#4321` or `4321` — link this issue, skip the search.
- free words — extra search terms on top of what the conversation already says.
- `todo` / `in-progress` — force the column (see step 5).

## Constants

These are the org's live ids. If any command below answers with "could not
resolve", re-fetch with the query in the `Refresh` column — do not guess.

| What | Value | Refresh |
|---|---|---|
| Project number / owner | `1` / `Actual-Chat` | `gh project list --owner Actual-Chat` |
| Project node id | `PVT_kwDOBSwBWs4AA3Ej` | `gh project view 1 --owner Actual-Chat --format json --jq .id` |
| Status field id | `PVTSSF_lADOBSwBWs4AA3EjzgAc2xg` | `gh project field-list 1 --owner Actual-Chat --format json` |
| Status option `ToDo` | `f75ad846` | same as above (options are listed under the field) |
| Status option `In Progress` | `47fc9ee4` | |
| Status option `Backlog` / `Done` | `86318b0b` / `98236657` | |

## Steps

### 1. Preconditions and the existing link

```bash
gh auth status                          # must be logged in
ME=$(gh api user --jq .login)
BRANCH=$(git branch --show-current)
git config --get "branch.$BRANCH.issue"  # empty on a fresh branch
```

If `branch.<name>.issue` is already set, the search is over: take that number,
skip to step 4, and re-run 4–6 as a check. Re-running the skill on a linked
branch is idempotent and cheap — do it rather than wondering.

### 2. Work out what the work is

Collect the signal before searching. In order of strength:

1. A number in the branch name (`feat/4362-…`) — a candidate, not a verdict.
2. What the user said the task is, in this conversation.
3. `git log --oneline origin/dev..HEAD` subjects and `git status --short` paths.
4. Any plan or spec the branch adds under `docs/superpowers/` — its title and
   first paragraph.

Turn that into 2–3 distinctive search terms: a component name, an error
string, a user-visible symptom. Not "fix", not "bug", not "chat".

### 3. Search, then confirm the pick — always

```bash
gh search issues --repo Actual-Chat/actual-chat --state open "<terms>" \
  --json number,title,assignees,updatedAt --limit 10
```

Run it two or three times with different term sets (the branch number, if any,
counts as one). Merge and rank: exact symptom match first, then same component
touched recently, then the rest. Drop anything closed.

Then `AskUserQuestion` with the top 3–5 candidates (number, title, assignee)
plus two fixed options: **"None of these — create a new issue"** and
**"Skip — no issue for this work"**. Ask even when one candidate looks perfect
and even when the branch name carries a number: ~200 open issues make near-misses
common, and the cost of a wrong link is a wrong `Closes #N` on the PR.

"Create" → step 7. "Skip" → report and stop; store nothing.

### 4. Assignment — never re-assign without asking

Fetch the issue's current state in one query (also used in steps 5–6):

```bash
gh api graphql -F n=<N> -f query='query($n:Int!){
  repository(owner:"Actual-Chat",name:"actual-chat"){ issue(number:$n){
    id state title issueType{name} assignees(first:5){nodes{login}}
    projectItems(first:5){nodes{ id project{id}
      fieldValueByName(name:"Status"){ ... on ProjectV2ItemFieldSingleSelectValue{name} } }}
  }}}'
```

| Assignees | Action |
|---|---|
| none, or only `$ME` | `gh issue edit <N> --add-assignee @me` (no-op if already there) |
| someone else, with or without `$ME` | `AskUserQuestion`, options in this order: **join as co-assignee** (`--add-assignee @me`), **take it over** (`--add-assignee @me --remove-assignee <them>`), **link without touching assignees**, **abort**. |

The reporter having self-assigned is not a reason to skip the question. It is
the most common case, and whether they keep it is their call and the user's,
not yours. If the issue is already **In Progress** under someone else, say so
in the question — they may be coding it right now, and taking it over is then
a conversation with them, not a board edit.

### 5. Pick the column

| Evidence | Column |
|---|---|
| `$BRANCH` is `dev`/`master`/`release/*`, or the branch has no commits over `origin/dev` and no code changes in the tree | **ToDo** — the work is claimed, not started |
| Commits over `origin/dev`, or a dirty tree touching code (not just `docs/`) | **In Progress** |
| Argument `todo` / `in-progress` | that column, overriding the evidence |

Two rules on top:

- **Never move backward.** An issue already In Progress stays there even if the
  evidence says ToDo. Backlog → ToDo/In Progress is forward.
- **Never touch Done or a closed issue.** If the pick is closed, say so and go
  back to step 3 — the user may want a fresh issue.

### 6. Update the board, then persist the link

Every mutation the skill performs is one of these five:

```bash
# a. assignee (step 4)
gh issue edit <N> --add-assignee @me [--remove-assignee <them>]

# b. add to the board if step 4's query showed no projectItems on PVT_kwDOBSwBWs4AA3Ej
ITEM=$(gh project item-add 1 --owner Actual-Chat \
  --url https://github.com/Actual-Chat/actual-chat/issues/<N> --format json --jq .id)

# c. set Status (skip if step 4 showed it already equal or further along)
gh project item-edit --project-id PVT_kwDOBSwBWs4AA3Ej --id "$ITEM" \
  --field-id PVTSSF_lADOBSwBWs4AA3EjzgAc2xg --single-select-option-id <f75ad846|47fc9ee4>

# d. persist the link on the branch
git config "branch.$BRANCH.issue" <N>
```

`item-edit` exits 0 on a wrong option id and changes nothing, so **verify**: re-run
step 4's query and check `fieldValueByName.name` is the column you meant.

**Still on `dev`?** There is no branch to write the link to. `AskUserQuestion`:
**create the branch now** (first option, with the proposed name) or **stay on
dev**. The name is `fix/<slug>` for a Bug, `feat/<slug>` for a Feature or Task,
never `feature/…`; the slug is the title lowercased, non-alphanumerics
collapsed to `-`, cut to the first four or five words. If a branch of that
name already exists, append the issue number. On
"create": `git checkout -b <name>`, then (d). On "stay": say plainly that the
link is not stored and the skill must be re-run once a branch exists.

### 7. Create path

Run the `/issue` command with a laconic, capitalized, prefix-free title and a
problem-only body drafted from step 2, and pass `--todo` when step 5 says ToDo (`/issue` defaults to In
Progress). `/issue` returns the new number; continue at step 6 (d). Assignment
and the board are already handled by `/issue` — do not redo (a)–(c).

## Report

Four lines, always:

```
Issue   #4397 Search does not find devops chat — https://github.com/Actual-Chat/actual-chat/issues/4397
Assign  alexis-kochetov (frolyo kept as co-assignee)
Board   Team project · ToDo (was: not on board)
Link    branch fix/search-devops-chat → branch.fix/search-devops-chat.issue = 4397
```

Say what was *not* done: "link not stored — still on dev", "board update failed:
token lacks `project` scope (run `gh auth refresh -s project`)".

## Common mistakes

| Mistake | Fix |
|---|---|
| Linking the single "obvious" match without asking | Step 3 always asks, even for one candidate |
| Adding yourself to someone else's issue because "they're just the reporter" | Step 4 asks; the answer is theirs and the user's |
| In Progress before a line of code exists | Step 5: claimed = ToDo; code = In Progress |
| Moving In Progress back to ToDo | Never backward |
| Trusting `item-edit`'s exit code | Re-query and read the Status name |
| Linking a closed issue | Closed → say so, return to step 3 |
| Forgetting `git config` because the board is already right | The link is the deliverable `/create-pr` needs; (d) runs every time |
| Running on `dev` and reporting success | No branch = no link; offer the branch or say it is not stored |
| Re-filing an issue `/issue` already searched for | The create path delegates; do not duplicate its duplicate check |
