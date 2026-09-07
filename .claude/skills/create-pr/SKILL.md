---
name: create-pr
description: Use when work on a branch is finished and the next step is a pull request — "create a PR", "open a PR", "push and PR this", "/create-pr" — or right after a PR has been created and the team has not been told about it yet.
allowed-tools:
  - Bash
  - Read
  - Write
  - Grep
  - AskUserQuestion
  - mcp__voxt-robokitty__post_message
---

# Opening a pull request

Three steps that always travel together: **push the branch → `gh pr create` → announce
it in the Review Requests chat.** The team picks review work out of that chat, not off
GitHub — a PR nobody announced is a PR nobody reviews. Stopping after step 2 is the
most common way this goes wrong.

## Invoking this skill is the permission to push

The standing rule everywhere else is: never `git push` unless the user asked in that
message. Asking for a PR *is* that ask — push and create without stopping to
re-confirm. It does **not** authorize a force-push over rewritten history; that belongs
to `/prepare-merge`.

## Preconditions

| Check | Command | If it fails |
|---|---|---|
| On a feature branch, not `dev`/`master`/`release/*` | `git branch --show-current` | Stop. Ask what to branch. |
| Working tree clean | `git status --short` | Commit or stash first — never PR a dirty tree. |
| Branch has commits over the base | `git log --oneline origin/dev..HEAD` | Nothing to PR. Stop. |
| No PR already open for it | `gh pr list --head "$(git branch --show-current)" --json url,state` | One exists → do **not** open a second, and do **not** re-announce it. Use `pr list`, not `pr view` — `pr view` errors on a branch with no upstream. |
| Build/tests actually run | — | You may still PR, but the Testing section must say plainly what was not run. |

Branch names follow the commit prefixes: `feat/…`, `fix/…`, `refactor/…` — never
`feature/…`.

## 1. Offer `/prepare-merge` first

If the branch carries a fix trail — review-comment commits, "wip", several commits on
one concern, added `docs/superpowers/` plans — ask once, via `AskUserQuestion`, whether
to run `/prepare-merge` before the PR. It rebases onto `origin/dev`, regroups the
commits, and strips the working docs. Ask; don't run it unprompted, and don't insist if
they decline.

**If they accept:** `/prepare-merge` ends by pushing the branch itself (a `-bak` ref,
then a `--force-with-lease`). Skip step 2 entirely when you come back and go straight
to step 3 — re-running the plain push would fail or fight the force-push.

**If they decline** and the branch adds files under `docs/superpowers/`, say so in one
line: those are working documents and they are about to land on `dev` for good. Offer
to drop them in a final `chore: drop the branch's working docs` commit. Their call —
do not delete anything unasked.

## 2. Push

```bash
git push -u origin "$(git branch --show-current)"
```

Never `--force` here. If the push is rejected as non-fast-forward, stop and report it —
that is someone else's work on the same branch.

## 3. Create the PR

Write the body to a file under the project's `tmp/`, never to the repo root, and never
pass a multi-KB body inline:

```bash
gh pr create --base dev --title "type(scope): summary" --body-file tmp/pr-body.md
```

- **Base is the repo's default branch** (`dev` on actual-chat) — confirm with
  `gh repo view --json defaultBranchRef` rather than assuming.
- **Title describes the whole branch**, as `type(scope): summary`. One commit (or a
  branch `/prepare-merge` just collapsed to one) → use its subject verbatim. Several
  commits → write the line the squashed commit would carry; do not just lift the
  biggest one, and never `--fill`, which staples subjects together.
- **Body shape**: `## Summary` (what was broken and why — the mechanism, not a diff
  restatement), `## Fix` (what the change does, and the invariants a reviewer must not
  break), `## Testing` (what ran, green or not, and what is still owed — device passes,
  manual verification). Then your harness's attribution footer, if it gives you one.
- Say plainly what was *not* verified. "Android device-verified; iOS compiles, device
  test still owed" is the useful sentence.

## 4. Announce in Review Requests — every time

Post once, via `mcp__voxt-robokitty__post_message`, to the **Review Requests** chat of
the Voxt place: `s-pmMsV1UVKG-gz3ymbh6n3`. If the RoboKitty MCP is not wired up in your
setup, stop and tell the user the PR is open but unannounced — do not treat the PR as
done.

The message posts under **your own account**, so write it in your voice:

```
PR #4378 — feat(ptt): make gestures reply-only and widen the stop surface
https://github.com/Actual-Chat/actual-chat/pull/4378

Narrows where PTT gestures can OPEN the mic and widens where they can CLOSE it.
Opening the app no longer arms flip/shake — only voice does; face-down and pocket
now both require the proximity sensor covered, which kills the mid-air false stop.

Tests 811 unit + 105 integration green; verified on device (CPH2747).
```

Title line, URL, blank line, then a few lines of what changed, what specifically needs
a reviewer's eyes (security-relevant commits, subtle invariants), and the test status.
Voxt renders GitHub-flavored Markdown — use real markup, but relative links resolve to
nothing, so link absolute or just name the file.

**One post per PR, ever.** No "added a second commit", no revised summaries, no
re-posting after a push. The chat is a review queue, not a changelog — a follow-up post
pushes other people's requests down and tells a reviewer nothing they wouldn't see on
the PR. New commits on an announced PR get mentioned in the session, to the user, not
to the chat.

Release announcements are a different thing entirely — Releases chat, and they need
their own confirmation. Never fold one into this step.

## Common mistakes

| Mistake | Fix |
|---|---|
| PR created, chat never posted | Step 4 is part of the deliverable, not a follow-up |
| Re-announcing after new commits | One post per PR; tell the user instead |
| `gh pr create --fill` | Write a real Summary/Fix/Testing body |
| Body file in the repo root | Put it in `tmp/` |
| Base branch assumed | `gh repo view --json defaultBranchRef` |
| Second PR opened for a branch that already has one | `gh pr list --head` first |
| "All tests pass" without running them | State what ran and what is owed |
| Pushing again after `/prepare-merge` | It already pushed; skip step 2 |
| `gh pr view` on a never-pushed branch | It errors; use `gh pr list --head` |
| Force-pushing to land the PR | That is `/prepare-merge`, and it asks first |
