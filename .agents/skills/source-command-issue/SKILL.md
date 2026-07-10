---
name: "source-command-issue"
description: "Create a GitHub issue, assign to current user, and set status to In Progress"
---

# source-command-issue

Use this skill when the user asks to run the migrated source command `issue`.

## Command Template

# Create GitHub Issue

Create a GitHub issue in `Actual-Chat/actual-chat`, assign it to the current user, and add it to the org's GitHub Project board with **In Progress** status.

## Arguments

The argument is the issue title. Optionally, use `--` to separate title from body:
- `/issue Fix login redirect loop` — title only
- `/issue Fix login redirect loop -- The redirect happens when...` — title + body

If no arguments are provided, ask the user for at least a title.

## Steps

### 1. Parse arguments and craft title

Split the argument on ` -- ` (space-dash-dash-space):
- Everything before `--` is the **title input**
- Everything after `--` is the **body** (optional)

**Title must be laconic** — short, punchy, lowercase (except proper nouns). Strip filler words. Examples:
- "There is an issue where the login page redirects in a loop" → `fix login redirect loop`
- "We need to add the ability to upload avatars" → `avatar upload`
- "The chat message list is very slow when there are many messages" → `fix chat list perf with many messages`

Use standard prefixes when intent is clear: `fix:`, `feat:`, `refactor:`, `chore:`. If the user already provided a short title, keep it as-is.

### 1b. Craft the body

An issue describes the **problem**, not the solution. Cover the symptom (what's broken, with concrete evidence — logs, crash reports, repro steps) and, when known, the root cause (why). **Do not include a fix, a recommended approach, or a list of files to change** — those belong in the PR. The team picks the approach; the issue tracks the problem.

Same laconic register as the title: short sections, no filler. Skip "Open follow-ups" / "Future work" sections unless the user asked for them — separate issues are better than to-do lists tucked inside one.

### 2. Check for similar issues

Before creating, search for existing issues that might be duplicates or related. Use `mcp__github__search_issues` with key words from the title:

- `owner`: `"Actual-Chat"`, `repo`: `"actual-chat"`
- `query`: 2-3 most distinctive keywords from the title

If similar **open** issues are found, list them to the user with number, title, and URL. Ask whether to:
- **Skip** — the issue already exists
- **Continue** — create anyway (not a duplicate)
- **Link** — create and reference the related issue in the body

If no similar issues are found, proceed silently.

### 3. Get current user

Use `mcp__github__get_me` to get the authenticated user's login.

### 4. Create the issue

Pick the org-level **issue type** (not labels — labels are ignored in this repo). Try `mcp__github__list_issue_types` with `owner: "Actual-Chat"` to get valid values. If the call returns 403 (PAT can't read org issue types), ask the user which type to use rather than guessing.

Use `mcp__github__issue_write` with:
- `method`: `"create"`
- `owner`: `"Actual-Chat"`
- `repo`: `"actual-chat"`
- `title`: from arguments
- `body`: from arguments (if provided)
- `assignees`: `[current_user_login]`
- `type`: org issue type name (e.g. `Bug` for defects)

Do **not** pass `labels` — this repo doesn't use them for triage.

Record the created issue number from the response.

### 5. Add to project board with "In Progress" status

Run the following bash commands (`GH_TOKEN` is set automatically by `c.ps1` from `AC_GITHUB_TOKEN`).

#### 4a. Find the org project

```bash
gh project list --owner Actual-Chat --format json --limit 10
```

Pick the first open project. Note the project **number**.

#### 4b. Add issue to project

```bash
gh project item-add <PROJECT_NUMBER> --owner Actual-Chat --url https://github.com/Actual-Chat/actual-chat/issues/<ISSUE_NUMBER> --format json
```

Record the item **id** from the response.

#### 4c. Get the Status field ID and "In Progress" option ID

```bash
gh project field-list <PROJECT_NUMBER> --owner Actual-Chat --format json
```

Find the field named "Status" and get its **id**. Then get the option ID for "In Progress":

```bash
gh api graphql -f query='
query($projectId: ID!) {
  node(id: $projectId) {
    ... on ProjectV2 {
      field(name: "Status") {
        ... on ProjectV2SingleSelectField {
          id
          options { id name }
        }
      }
    }
  }
}' -f projectId="<PROJECT_NODE_ID>"
```

Note: The project node ID can be obtained from `gh project view <NUMBER> --owner Actual-Chat --format json`.

#### 4d. Set status to "In Progress"

```bash
gh project item-edit --project-id <PROJECT_NODE_ID> --id <ITEM_ID> --field-id <STATUS_FIELD_ID> --single-select-option-id <IN_PROGRESS_OPTION_ID>
```

### 6. Error handling

- If project board operations fail (e.g., token lacks `project` scope), still report the created issue as success and warn that the project board update failed. Suggest the user regenerate their PAT with Organization > Projects: Read and write permission (see `docs-internal/set-local-env.ps1`).
- Never fail silently — always show what happened.

### 7. Output

Report:
- Issue URL: `https://github.com/Actual-Chat/actual-chat/issues/<NUMBER>`
- Assignee
- Project board status (or warning if it failed)
