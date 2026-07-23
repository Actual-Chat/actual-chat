---
name: prepare-release
description: |
  Cut a new Voxt release. Use when the user says "prepare a release", "cut a
  release", "release Voxt", "do a release", "/prepare-release", or wants to run
  nbgv prepare-release, bump the version, generate end-user release notes, and
  announce them in the Releases chat.
allowed-tools: Bash, Read, Write, Edit, Glob, Grep, mcp__voxt-robokitty__post_message, mcp__voxt-robokitty__list_place_chats
---

# prepare-release

Cut a Voxt release end to end: bump the version with `nbgv`, push `dev` + the
new `release/vX.Y` branch, generate **end-user** release notes from the commit
log, commit them to `docs/releases/`, and announce them in the **Releases**
chat via RoboKitty.

This is a release action that pushes branches and posts publicly. Do the git
mutations without stopping between mechanical steps, but **pause once** to let
the user review the drafted release notes before the notes commit and the
RoboKitty post (steps 6–8). Those are the hard-to-revert, outward-facing parts.

## Reuse

- **`nbgv`** (Nerdbank.GitVersioning) drives all versioning — never hand-edit
  `version.json`'s version field. Branch name comes from `version.json`'s
  `release.branchName` = `release/v{version}`.
- **RoboKitty MCP** (`mcp__voxt-robokitty__post_message`) posts the announcement
  — same path `/my-changes --post` and `/robokitty-post` use. No new HTTP code.
- Release-notes **style** is defined below; match prior notes' tone, don't
  reinvent a format.
- The **`configs`** sibling repo (`/proj/configs`) needs a matching
  `release/vX.Y` branch — CI loads config from it. Don't skip step 3b.

## Prerequisites

- Clean working tree on `dev` (`git status` is clean; if not, stop and ask).
- `nbgv` available. It's a **local** tool pinned in `.config/dotnet-tools.json`,
  so run `dotnet tool restore` once, then `dotnet nbgv get-version` to confirm.
- You are up to date: `git fetch origin && git switch dev && git pull --ff-only`.

## Steps

### 1. Read the target version

```bash
cat version.json      # e.g. "version": "2.13-alpha"
```

The release version is that value with any `-alpha`/prerelease tag dropped
(`2.13-alpha` → **2.13**). Call it `X.Y`. The new branch will be `release/vX.Y`.
After `prepare-release`, `dev` bumps to the next minor (`2.14-alpha`).

### 2. Run prepare-release

On `dev`:

```bash
dotnet nbgv prepare-release
```

This creates `release/vX.Y` (version set to `X.Y`) and advances `dev` to the
next `-alpha`, committing on both. Verify:

```bash
git branch --list 'release/v*' | tail; git log --oneline -1 dev
```

### 3. Cut and push the configs release branch FIRST

**The CI build loads configuration from the `configs` repo's `release/vX.Y`
branch, so it must exist before you push the app's release branch — otherwise
the release build the app-branch push triggers fails for lack of config.** The
repo is a sibling at `../configs` (`/proj/configs`); its remote is
`git@github.com:Actual-Chat/configs.git`.

Ensure it's cloned, then create `release/vX.Y` from the latest `master` and push:

```bash
cd /proj/configs 2>/dev/null || git clone git@github.com:Actual-Chat/configs.git /proj/configs && cd /proj/configs
git fetch origin
git switch master && git pull --ff-only
git switch -c release/vX.Y            # skip if it already exists
git push origin release/vX.Y
cd /proj/ActualChat                   # back to the app repo
```

If the SSH remote can't authenticate in this environment, push over HTTPS with
the token instead:
`git push "https://x-access-token:${GH_TOKEN}@github.com/Actual-Chat/configs.git" release/vX.Y`.
If `release/vX.Y` already exists on origin and equals `origin/master`, it's
already done — leave it.

### 3b. Push the app branches

Only after the configs branch is live:

```bash
git push origin dev
git push origin release/vX.Y
```

### 4. Get the one-line commit log since the previous release

The previous release branch is `release/vX.(Y-1)` on origin (e.g. `release/v2.12`).
Collect subjects for the notes-writing input:

```bash
git log --format='%s' origin/release/vX.(Y-1)..release/vX.Y > /tmp/release-commits-vX.Y.txt
wc -l /tmp/release-commits-vX.Y.txt
```

Ignore the housekeeping lines (`Set version to …`, `Merge branch …`, AOT/AGENTS
regeneration, pure `test:`/`build:`/`chore:` churn) when writing notes — they're
not user-facing.

### 5. Write end-user release notes

Read the full commit log and distill it into **end-user** notes. This is the
core judgment step — see **Release-notes style** below. Draft the file at
`docs/releases/release-notes-vX.Y.md`.

**STOP and show the drafted notes to the user for review before committing.**
Incorporate any edits they ask for.

### 6. Commit the notes on the release branch

```bash
git switch release/vX.Y
# write docs/releases/release-notes-vX.Y.md (mkdir -p docs/releases if needed)
git add docs/releases/release-notes-vX.Y.md
git commit -m "docs: add release notes vX.Y"
git push origin release/vX.Y
```

### 7. Merge the notes into dev, push dev

```bash
git switch dev
git merge --no-ff release/vX.Y -m "Merge release notes vX.Y into dev"
git push origin dev
```

**Usually this merges cleanly** — git's `ort` strategy keeps dev's newer
`version.json`, so the only change that lands on dev is the notes file. Verify
after: `grep '"version"' version.json` should still show the next `-alpha`.

Only if `version.json` **does** conflict (release branch has `X.Y`, dev has the
next `-alpha`), resolve by keeping dev's version before pushing:

```bash
git checkout --ours version.json && git add version.json
# resolve any other conflicts by keeping dev's side unless it's the notes file
git commit --no-edit
git push origin dev
```

If the only thing you actually need on `dev` is the notes file and the merge is
noisy, the equivalent clean alternative is:
`git switch dev && git checkout release/vX.Y -- docs/releases/release-notes-vX.Y.md && git commit -m "docs: add release notes vX.Y" && git push origin dev`.

### 8. Announce in the Releases chat via RoboKitty

Only if the RoboKitty MCP is available (`mcp__voxt-robokitty__post_message`
tool present). The **Releases** chat is:

- URL: `https://voxt.ai/chat/s-pmMsV1UVKG-dCKQXnYpX9`
- `chatId`: `s-pmMsV1UVKG-dCKQXnYpX9` (place `pmMsV1UVKG`)

Post the release notes wrapped in a fenced code block so they render verbatim:

```
mcp__voxt-robokitty__post_message(
  chatId = "s-pmMsV1UVKG-dCKQXnYpX9",
  text   = "```\n<the full release-notes markdown>\n```"
)
```

Confirm with a one-liner: `Posted release notes vX.Y → Releases (LID: <id>).`
On any MCP failure, surface the error verbatim and stop — don't retry blindly.

**If the `mcp__voxt-robokitty__*` tools aren't loaded this session** (common —
the server is declared in `.mcp.json` but not always auto-connected), call the
HTTP endpoint directly instead of asking the user to paste. It's the same
RoboKitty server over plain JSON-RPC, authed with `ActualChat_RoboKitty_API_Key`:

```bash
# wrap the committed notes in a code fence
{ echo '```'; cat docs/releases/release-notes-vX.Y.md; echo '```'; } > /tmp/rk-text.txt
# build the JSON-RPC body with jq (safe escaping of backticks/emoji/newlines)
jq -n --rawfile t /tmp/rk-text.txt \
  '{jsonrpc:"2.0",id:2,method:"tools/call",params:{name:"post_message",
    arguments:{chatId:"s-pmMsV1UVKG-dCKQXnYpX9",text:$t}}}' > /tmp/rk-body.json
curl -s -X POST "https://voxt.ai/api/mcp" \
  -H "Authorization: Bearer ${ActualChat_RoboKitty_API_Key}" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  --data-binary @/tmp/rk-body.json
```

The response is an SSE `data:` line; success looks like
`"structuredContent":{"result":<LID>}`. Report that LID. (Use the prod key/URL
above; `ActualChat_RoboKitty_Dev_API_Key` + `https://dev.voxt.ai/api/mcp` target
the dev instance.) Only if neither the tool nor the key is available, print the
notes for the user to paste manually.

## Release-notes style

The notes are for **end users**, not engineers. Translate commits into user
value; drop implementation detail entirely.

- **Header:** `**🎉 Voxt vX.Y is here! 🎉**` then a one/two-sentence summary of
  the release's theme.
- **Body:** grouped sections under bold headings with an emoji, e.g.
  `**🚀 What's New**`, then feature blocks. Use short prose or tight bullet
  lists. Lead with the biggest user-visible wins.
- Group many small commits into one plain-language line ("Dozens of small
  fixes — quiet polish across UI, animations, and edge cases").
- No commit hashes, file names, class names, RPC/codec/internal terms.
- Roughly one screenful; short releases can be ~15 lines, big ones ~40.
- **Footer:** a warm thanks + `— The Voxt.ai team 🎧`.

Keep the voice friendly and confident. When in doubt about whether a change is
worth mentioning, ask: "would a user notice or care?" If no, fold it into the
"small fixes" line or drop it.

## Quick reference

| Step | Command / action |
|---|---|
| Target version | `cat version.json` → drop `-alpha` → `X.Y` |
| Bump | `dotnet nbgv prepare-release` (on `dev`) |
| Config branch (first!) | `/proj/configs`: `release/vX.Y` from latest `master`, push — CI loads it |
| Push app branches | `git push origin dev && git push origin release/vX.Y` (after config branch) |
| Commit log | `git log --format='%s' origin/release/vX.(Y-1)..release/vX.Y` |
| Notes file | `docs/releases/release-notes-vX.Y.md` |
| Merge to dev | `git merge --no-ff release/vX.Y`, keep dev's `version.json` |
| Announce | `mcp__voxt-robokitty__post_message` → `s-pmMsV1UVKG-dCKQXnYpX9`, code-fenced |

## Common mistakes

- **Hand-editing `version.json`'s version.** Let `nbgv` own it. Only ever
  resolve it in the merge by keeping dev's side.
- **Copying commit subjects into the notes.** Notes are user value, not a
  changelog. Rewrite everything.
- **Forgetting the code fence** in the RoboKitty post — the notes must be inside
  triple-backticks so markdown renders literally.
- **Wrong previous-release branch** for the diff → notes miss or double-count
  commits. Confirm `origin/release/vX.(Y-1)` is the actual prior release.
- **Skipping the review pause.** The notes are public; show them first.
- **Forgetting the `configs` release branch (step 3b).** The CI release build
  loads config from `configs`' `release/vX.Y`; without it the build fails.
