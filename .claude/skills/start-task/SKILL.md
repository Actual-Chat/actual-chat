---
name: start-task
description: |
  Use when the user reports a problem or names a piece of work and wants to
  start on it in a fresh worktree — "start a task", "new task", "file this and
  make a branch", "/start-task". Finds or files the GitHub issue, creates the
  feat/… or bugfix/… branch linked to it, and prints the paste-ready
  `ai fwt|bwt` (Docker) and `ai os fwt|bwt` (host) commands that open the
  worktree. Linear flow with hard stops; asks nothing.
allowed-tools:
  - Bash
  - Read
  - Grep
  - Glob
---

# /start-task — From a described problem to a linked branch

Input: a description of an issue, or an issue number. Output: an issue in
`Actual-Chat/actual-chat` that is yours, a `feat/<N>-<slug>` or
`bugfix/<N>-<slug>` branch linked to it, and the one command to paste next.
Everything runs through `gh` and `git`; no MCP server is needed.

**This skill never asks.** Every ambiguity is either resolved by a rule below or
is a stop: print what was found and end the turn. The user re-runs with `#N`
or fixes things by hand. It never checks out a branch, never touches the
working tree, never re-assigns an issue away from someone else.

## Arguments

- Free text — the problem, as the user described it. Becomes the search terms
  and, on the create path, the issue title and body.
- `#4321` / `4321` anywhere in the text — use this issue, skip the search.
- `--assignee <login>` or "assign to <login>" / "for <login>" naming a GitHub
  login — the assignee of a **newly created** issue. Without it the issue is
  assigned to the current user. It does not change an existing issue.

## Steps

### 1. Preconditions

```bash
gh auth status
ME=$(gh api user --jq .login)
git fetch origin --quiet
```

Run from the main checkout (`~/actual/ActualChat`), whatever branch it is on.
The current branch and dirty tree are irrelevant and stay untouched.

### 2. Find the issue

**Given a number** — fetch it and go to step 3.

**Otherwise** — search open issues with 2–3 distinctive terms from the text
(a component, an error string, a visible symptom; not "fix", "bug", "chat"):

```bash
gh search issues --repo Actual-Chat/actual-chat --state open "<terms>" \
  --json number,title,assignees,author,url --limit 10
```

Run it twice with different term sets and merge. Then decide, in this order:

| Result | Action |
|---|---|
| One candidate whose title plainly describes the same problem | use it → step 3 |
| Several plausible candidates, or one that is only loosely related | **stop**: print them (number, title, assignee, URL) and say "re-run with `#N` to use one, or `/issue` to file anyway" |
| Nothing plausible | create → step 2b |

"Plainly the same problem" means the same component and the same symptom, not
a shared keyword. When unsure, it is the stop row — a wrong pick becomes a
wrong `Closes #N` on the PR.

### 2b. Create path

Run the `/issue` command (`.claude/commands/issue.md`) with `--todo`, a
laconic capitalized prefix-free title, and a problem-only body drafted from
the text. It searches for duplicates again, creates the issue, assigns it to
the current user, sets the type, and puts it on the board in **ToDo**.

If an explicit assignee was given and it is not `$ME`:

```bash
gh issue edit <N> --add-assignee <login> --remove-assignee @me
```

then report the issue and **stop** — the branch and worktree are for the
person who will do the work, and that is not the current user.

Continue at step 4 with the new number; the issue is yours by construction.

### 3. Ownership check — stop unless it is yours

```bash
gh api graphql -F n=<N> -f query='query($n:Int!){
  repository(owner:"Actual-Chat",name:"actual-chat"){ id issue(number:$n){
    id state title author{login} issueType{name}
    assignees(first:5){nodes{login}}
    linkedBranches(first:10){nodes{ref{name}}}
  }}}'
```

| Issue state | Action |
|---|---|
| closed | **stop** — say so |
| assignees include `$ME` | yours → step 4 |
| no assignees, author is `$ME` | yours; `gh issue edit <N> --add-assignee @me` → step 4 |
| no assignees, author is someone else | **stop** — nobody's; print it and say the user can self-assign on GitHub and re-run with `#N` |
| assigned to someone else | **stop** — print the assignee |

Keep `id` (issue node id), `repository.id`, `issueType.name` and
`linkedBranches` for the next steps.

### 4. Branch name

| `issueType.name` | Prefix | Launcher |
|---|---|---|
| `Bug` | `bugfix/` | `ai bwt` (Docker) / `ai os bwt` (host) |
| `Feature`, `Task`, none | `feat/` | `ai fwt` (Docker) / `ai os fwt` (host) |

Slug: the issue title lowercased, every non-alphanumeric run collapsed to `-`,
leading/trailing `-` trimmed, cut to the first four or five words, then any
trailing stopword (`a`, `an`, `the`, `in`, `on`, `of`, `to`, `is`) dropped.
Branch name: `<prefix><N>-<slug>`, e.g. "Login page redirects in a loop" →
`bugfix/4321-login-page-redirects`.
Existing branches follow this shape (`feat/4274-quote-selected-text`,
`bugfix/4530-realtime-translation-…`); match it.

### 5. Existing-branch check — stop if anything exists

```bash
NAME=<prefix><N>-<slug>
git rev-parse --verify --quiet "refs/heads/$NAME"
git rev-parse --verify --quiet "refs/remotes/origin/$NAME"
git branch -a --list "*${N}-*"                 # any branch for this issue, any slug
git worktree list | grep -E "\[(feat|bugfix|fix|hotfix)/${N}-"
git config --get-regexp 'branch\..*\.issue' | grep -E " ${N}$"
```

If **any** of these hits, or `linkedBranches` from step 3 names a ref, **stop**:
print the branch(es) and worktree path(s) found and both matching
`ai fwt|bwt <branch>` / `ai os fwt|bwt <branch>` commands, since that is what
the user most likely wants to run. Do not create a second branch for the same issue.

### 6. Create the branch, linked to the issue

Primary path — GitHub creates the branch on `origin` at the head of `dev` and
records it under the issue's *Development* panel in one call. A PR opened from
a branch linked this way is attached to the issue and closes it on merge even
without a `Closes #N` line:

```bash
OID=$(git rev-parse origin/dev)
gh api graphql -F issueId=<issue node id> -F repositoryId=<repository id> \
  -F oid="$OID" -F name="$NAME" -f query='
  mutation($issueId:ID!,$repositoryId:ID!,$oid:GitObjectID!,$name:String!){
    createLinkedBranch(input:{issueId:$issueId,repositoryId:$repositoryId,oid:$oid,name:$name}){
      linkedBranch{ ref{ name } } } }'
git fetch origin --quiet
git branch --track "$NAME" "origin/$NAME"
```

Fallback — if the mutation fails (permissions, name rejected), create the
branch locally only and say the GitHub-side link was not made:

```bash
git branch "$NAME" origin/dev
```

Either way, persist the local link that `/create-pr` reads to add
`Closes #N` and `/prepare-merge` uses to find the issue:

```bash
git config "branch.$NAME.issue" <N>
```

`git branch`, never `git checkout -b`: the branch must not be checked out
here, or `git worktree add` in the launcher refuses it.

### 7. Board column

`/issue --todo` already landed a new issue in **ToDo**. For an existing issue
that is not on the board or is in **Backlog**, move it to **ToDo** with the
ids and commands from `/track-issue` step 6 (b)–(c); never move an issue
backward and never touch **In Progress**/**Done**. If the board update fails
(token lacks `project` scope), report it and continue — it does not block the
branch.

## Report

```
Issue    #4321 Login page redirects in a loop (Bug) — https://github.com/Actual-Chat/actual-chat/issues/4321
Assignee frolyo (created)                     | frolyo (existing) | frolyo (claimed, was unassigned)
Branch   bugfix/4321-login-page-redirects ← origin/dev, linked on GitHub, branch.<name>.issue = 4321
Board    Team project · ToDo
Next     ai bwt bugfix/4321-login-page-redirects       # Docker (sandboxed)
         ai os bwt bugfix/4321-login-page-redirects    # host OS
```

The two `Next` lines are the deliverable: the launcher opens a Claude session
in the new worktree, which this session cannot do. Print both, Docker first,
each with the full prefixed branch name, so either is a copy-paste. Never
print just one — which environment the user wants varies per task.

On a stop, print the reason, the issue or branch it hit, and the command(s)
that resolve it (re-run with `#N`, self-assign, or both `ai …` lines for a
branch that already exists).

## Common mistakes

| Mistake | Fix |
|---|---|
| Asking the user to pick among candidates | This skill stops instead; `/track-issue` is the interactive one |
| Picking a loosely related issue because it is the only hit | Same component *and* same symptom, or stop |
| `git checkout -b` | `git branch`; the worktree checks it out |
| Creating a second branch for an issue that has one | Step 5 stops on any `<N>-` branch or worktree |
| Claiming an unassigned issue someone else filed | Stop; the user self-assigns and re-runs with `#N` |
| Re-assigning an issue away from its assignee | Never; stop |
| Assigning a new issue to someone else on a hunch | Only an explicit `--assignee`/"assign to" wins; default is the current user |
| `Closes #N` left to memory | `branch.<name>.issue` is set every time |
| Trying to run `ai fwt` / `ai os fwt` from here | Print both; the user pastes one |
| Printing only the `os` variant | Always both lines, Docker first |
