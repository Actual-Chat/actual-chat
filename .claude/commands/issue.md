---
allowed-tools: Bash, Read, AskUserQuestion
description: Create a GitHub issue, assign to current user, and set status to In Progress (or ToDo with --todo)
argument-hint: [--todo] <title> [-- <body>]
---

# Create GitHub Issue

Create a GitHub issue in `Actual-Chat/actual-chat`, assign it to the current user, and add it to the org's GitHub Project board with **In Progress** status — or **ToDo** when `--todo` is given (work that is claimed but not yet started; `/track-issue` passes this).

Everything runs through `gh`, which is authenticated in every AgentCli environment (`GH_TOKEN` comes from `AC_GITHUB_TOKEN` in Docker). Do not depend on a GitHub MCP server — it is not wired up in most sessions.

## Arguments

- `--todo` — optional, first token only: land the issue in **ToDo** instead of **In Progress**.
- The rest is the issue title. Optionally, use `--` to separate title from body:
  - `/issue Fix login redirect loop` — title only
  - `/issue Fix login redirect loop -- The redirect happens when...` — title + body
  - `/issue --todo avatar upload` — claimed, not started

If no arguments are provided, ask the user for at least a title.

## Steps

### 1. Parse arguments and craft title

Split the argument on ` -- ` (space-dash-dash-space):
- Everything before `--` is the **title input**
- Everything after `--` is the **body** (optional)

**Title must be laconic** — short, punchy, a sentence fragment that starts with a capital letter. Strip filler words. **No `fix:`/`feat:`/`chore:` prefixes** — those are commit and branch conventions, an issue title is plain English (the issue *type* carries the Bug/Feature/Task distinction). Examples:
- "There is an issue where the login page redirects in a loop" → `Login page redirects in a loop`
- "We need to add the ability to upload avatars" → `Avatar upload`
- "The chat message list is very slow when there are many messages" → `Chat list is slow with many messages`

If the user already provided a short title, keep its wording; only capitalize the first letter and drop a commit-style prefix.

### 1b. Craft the body

An issue describes the **problem**, not the solution. Cover the symptom (what's broken, with concrete evidence — logs, crash reports, repro steps) and, when known, the root cause (why). **Do not include a fix, a recommended approach, or a list of files to change** — those belong in the PR. The team picks the approach; the issue tracks the problem.

Same laconic register as the title: short sections, no filler. Skip "Open follow-ups" / "Future work" sections unless the user asked for them — separate issues are better than to-do lists tucked inside one.

Write the body to a file under the project's `tmp/` (never the repo root) and pass it with `--body-file`.

### 2. Check for similar issues

Before creating, search for existing open issues that might be duplicates or related, with the 2–3 most distinctive keywords from the title:

```bash
gh search issues --repo Actual-Chat/actual-chat --state open "<keywords>" \
  --json number,title,assignees,url --limit 10
```

If similar **open** issues are found, list them to the user with number, title, and URL. Ask whether to:
- **Skip** — the issue already exists
- **Continue** — create anyway (not a duplicate)
- **Link** — create and reference the related issue in the body

If no similar issues are found, proceed silently.

### 3. Get current user

```bash
gh api user --jq .login
```

### 4. Create the issue

```bash
gh issue create --repo Actual-Chat/actual-chat --assignee @me \
  --title "<title>" --body-file tmp/issue-body.md
```

Record the issue number from the returned URL.

Then set the org-level **issue type** (not labels — labels are ignored in this repo). The org defines `Task`, `Bug`, and `Feature`; `gh issue create` has no flag for them, so fetch the ids and set the type with one mutation:

```bash
gh api graphql -f query='{ organization(login:"Actual-Chat"){ issueTypes(first:10){ nodes{ id name } } } }'
ISSUE_ID=$(gh api graphql -F n=<NUMBER> -f query='query($n:Int!){ repository(owner:"Actual-Chat",name:"actual-chat"){ issue(number:$n){ id } } }' --jq .data.repository.issue.id)
gh api graphql -F id="$ISSUE_ID" -F typeId=<TYPE_ID> \
  -f query='mutation($id:ID!,$typeId:ID!){ updateIssue(input:{id:$id, issueTypeId:$typeId}){ issue{ number issueType{name} } } }'
```

Pick `Bug` for defects, `Feature` for new user-visible capability, `Task` for everything else. If the type query returns 403 (token cannot read org issue types), ask the user which type to use rather than guessing — and if they cannot help, leave the type unset and say so.

Do **not** pass `--label` — this repo doesn't use labels for triage.

### 5. Add to project board and set Status

The board is the org's **Team project**, number `1`. Its ids are stable; re-fetch with `gh project view 1 --owner Actual-Chat --format json` and `gh project field-list 1 --owner Actual-Chat --format json` only if a command below says it cannot resolve one.

| What | Value |
|---|---|
| Project node id | `PVT_kwDOBSwBWs4AA3Ej` |
| Status field id | `PVTSSF_lADOBSwBWs4AA3EjzgAc2xg` |
| `In Progress` option | `47fc9ee4` |
| `ToDo` option | `f75ad846` |

```bash
ITEM=$(gh project item-add 1 --owner Actual-Chat \
  --url https://github.com/Actual-Chat/actual-chat/issues/<NUMBER> --format json --jq .id)
gh project item-edit --project-id PVT_kwDOBSwBWs4AA3Ej --id "$ITEM" \
  --field-id PVTSSF_lADOBSwBWs4AA3EjzgAc2xg --single-select-option-id 47fc9ee4   # f75ad846 with --todo
```

`item-edit` exits 0 even with a wrong option id, so verify the column landed:

```bash
gh api graphql -F n=<NUMBER> -f query='query($n:Int!){ repository(owner:"Actual-Chat",name:"actual-chat"){ issue(number:$n){
  projectItems(first:5){ nodes{ fieldValueByName(name:"Status"){ ... on ProjectV2ItemFieldSingleSelectValue{ name } } } } } } }'
```

### 6. Error handling

- If project board operations fail (e.g., token lacks `project` scope), still report the created issue as success and warn that the project board update failed. Suggest `gh auth refresh -s project`, or regenerating the PAT with Organization > Projects: Read and write permission (see `docs-internal/set-local-env.ps1`).
- Never fail silently — always show what happened.

### 7. Output

Report:
- Issue URL: `https://github.com/Actual-Chat/actual-chat/issues/<NUMBER>`
- Assignee
- Issue type
- Project board status (or warning if it failed)
