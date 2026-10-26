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

**Title must be laconic, clear and self-describing** — short, a sentence fragment that starts with a capital letter, and readable on its own in a board column or a search hit: someone who sees only the title knows which part of the app it is about and what it asks for. Name the platform or component where it matters (`iPhone call`, `Mac app`, `share extension`); drop filler words, internal type names, and anything only the body explains. **No `fix:`/`feat:`/`chore:` prefixes** — those are commit and branch conventions, an issue title is plain English (the issue *type* carries the Bug/Feature/Task distinction).

**Decide the type first** (the rule in step 4), then word the title by it:

- `Bug` — the symptom: what goes wrong. "There is an issue where the login page redirects in a loop" → `Login page redirects in a loop`.
- `Task` / `Feature` — what should exist, as a noun phrase. Nothing is broken, so a title that states an absence or a complaint ("No …", "… is easy to miss") reads as a bug report against an issue typed as work. "We need to add the ability to upload avatars" → `Avatar upload`; "No banner when video starts in a background iPhone call" → `Banner when video starts in a background iPhone call`; "Raised hand is easy to miss" → `More visible raised hand`.

A Task title still names the outcome, not the implementation: `Banner when …`, not `Post a local notification from CallKit`.

If the user already provided a short title, keep its wording; only capitalize the first letter, drop a commit-style prefix, and reword a Task or Feature title that is phrased as a defect.

### 1b. Craft the body

An issue describes the **problem**, not the solution. Cover the symptom (what's broken, with concrete evidence — logs, crash reports, repro steps) and, when known, the root cause (why). For a `Task` or `Feature` nothing is broken: cover what happens now and what is missing, not a symptom. **Do not include a fix, a recommended approach, or a list of files to change** — those belong in the PR. The team picks the approach; the issue tracks the problem.

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

Then set the org-level **issue type** (not labels — labels are ignored in this repo). The org defines `Task`, `Bug`, and `Feature`; `gh issue create` has no flag for them, but the REST endpoint takes the type by name:

```bash
gh api -X PATCH repos/Actual-Chat/actual-chat/issues/<NUMBER> -f type=<Task|Bug|Feature> --jq '.type.name'
```

Pick the type by what actually happened, not by how the request is worded:

- `Bug` — something that worked, or was designed to work, does not: a regression, a crash or hang, an error, lost or wrong data, an action that does nothing, behavior that contradicts the design or spec. You must be able to name the intended behavior it breaks.
- `Feature` — a large new user-visible capability that spans several pieces of work.
- `Task` — everything else: improving something that works as designed (UX polish, layout, wording, making it faster than it ever was, an extra option), a small self-contained addition, refactoring, tooling, docs.

An improvement is a `Task` even when the request says "bug", "broken" or "fix" — "the button should be bigger", "the list should also show X", "loading feels slow" when it was never faster. It becomes a `Bug` only when the current behavior is a departure: it used to work, the design says otherwise, or it plainly fails. When unsure whether the behavior is by design, pick `Task`; when in doubt between `Feature` and `Task`, pick `Task`.

A **coverage gap** found while building a feature is a `Task`: a platform, process or component the feature never reached — an app extension that was never wired to it, a platform it was never built for. It is a `Bug` only when a part the feature *does* cover contradicts the feature's own design. Example: edge-node content (#5338) says content follows the host RPC is on. The Linux app keeping content on the origin while RPC is on the edge breaks that rule — `Bug` (#5477). The iOS share extension, which never had endpoint switching at all, keeps both RPC and content on the origin — `Task` (#5520).

Apply this rule whenever you name a type, including when you only propose one to the user in chat ("should I file it as a Bug?"). The type you suggest is the type that gets filed, so decide it here, not by instinct.

A failing, flaky or outdated **test** is a `Task`, not a `Bug` — e2e specs, integration and unit tests, test rigs and CI checks alike. `Bug` means the product misbehaves for a user; when only the test is wrong (a stale selector, a race in a wait, a spec that no longer matches intended behavior), nothing is broken for users, so the fix is maintenance work. If the failing test exposed a real product defect, file that defect as a `Bug` and describe the user-visible symptom, not the red test.

The type is mandatory: an issue is not done until the call above echoes the type back. Do not go through GraphQL for it — the `organization.issueTypes` lookup is refused for the usual personal access tokens, while the REST call is not.

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
