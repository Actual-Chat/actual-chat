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

### 3. Push both branches

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
```

`version.json` **will conflict** (release branch has `X.Y`, dev has the next
`-alpha`). Resolve by keeping dev's version, then finish the merge:

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

If the tool isn't wired up in this session, say so and print the notes for the
user to paste manually.

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
| Push | `git push origin dev && git push origin release/vX.Y` |
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
