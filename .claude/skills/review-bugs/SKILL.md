---
name: review-bugs
version: 1.0.0
description: |
  Use when the user says "review bugs", "/review-bugs", "pull new bugs", "what was
  reported in Bugs", "update the bug list", or asks for an auto-review of reported
  bugs. Pulls the new messages from the Voxt Bugs and Bugs (Mobile) chats, turns
  them into entries in docs/bugs.md, reconciles the list with GitHub issues, pull
  requests and dev commits, moves finished bugs to docs/bugs-closed.md, and lists
  what needs a human decision.
allowed-tools:
  - Bash
  - Read
  - Edit
  - Write
  - Grep
  - Glob
  - AskUserQuestion
  - ToolSearch
  - mcp__voxt-robokitty__list_messages
  - mcp__voxt-robokitty__get_id_range
  - mcp__voxt-robokitty__get_chat
---

# /review-bugs — keep the bug list in step with the chats and GitHub

Two documents hold the state:

- `docs/bugs.md` — scan state, reconciliation state, a summary table, and one
  entry per active bug. Its "How to read this file" section defines the format
  (ID, priority and the trailing `*`, review states, GitHub column). Follow it;
  do not reword it.
- `docs/bugs-closed.md` — bugs that are fixed, rejected or not bugs, each with
  a verdict and evidence.

The skill reads the scan state from `docs/bugs.md`, so a rerun only handles what
is new. It edits only those two files. It never posts to a chat, never files or
comments on a GitHub issue, and never commits unless asked.

## Arguments

- no arguments — full run: pull, extract, reconcile, update, report.
- `--auto-review <scope>` — additionally try to give a verdict to unreviewed
  bugs. `<scope>` is `new` (entries added by this run, the default), `today`
  (entries whose first message is from today), `all` (every entry with review
  `Needs review`), or a comma-separated list of IDs such as `B26936,M9665`.
- `--dry-run` — do everything except editing the two files; print the report.

## Constants

| What | Value |
|---|---|
| Place `Voxt` | `pmMsV1UVKG` |
| Chat `Bugs` | `s-pmMsV1UVKG-v3m8jr8kuj` (ID prefix `B`) |
| Chat `Bugs (Mobile)` | `s-pmMsV1UVKG-fPHVtB5Zz0` (ID prefix `M`) |
| Message link | `https://voxt.ai/chat/<chatId>?n=<messageId>` |
| GitHub repo | `Actual-Chat/actual-chat` |
| Helper | `.claude/skills/review-bugs/digest.py` |

More chats can be added by adding a row to the scan table in `docs/bugs.md` and
a prefix letter here and in the table's "How to read" section.

## Steps

### 1. Load the state

Read both documents completely. From `docs/bugs.md` take the scan table (last
scanned message id per chat) and the reconciliation table (issues up to which
number, commit and date). Check `gh auth status` and run
`git fetch origin dev`. If either fails, say so and stop.

### 2. Pull the new messages

The `voxt-*` MCP tools talk to production. Reading is all this skill does with
them. Use `mcp__voxt-robokitty__*`; both chats are public, so the bot can read
them. If those tools are not loaded, load them with `ToolSearch`
(`select:mcp__voxt-robokitty__list_messages,mcp__voxt-robokitty__get_id_range`).

For each chat in the scan table:

1. `get_id_range(chatId)` gives `lastId`. If it equals the last scanned id, the
   chat has nothing new.
2. Page with `list_messages(chatId, afterId=<last scanned>, limit=256)` and
   repeat with `afterId=<range.lastId of the previous page>` until the page
   reaches `lastId`.
3. A page is usually too large for the conversation, so the tool saves it to a
   file and prints the path. Collect all paths for the chat and run
   `python .claude/skills/review-bugs/digest.py <paths...> --after <last scanned id>`
   (use `python`, not `python3`, on the Windows host). It prints one line per
   message: `#id MM-DD HH:MMZ Author [rN] [attN]: text`, where `rN` is the
   message replied to and `attN` the attachment count. Times are UTC. If a
   page arrives inline, save it as JSON to the scratchpad first and pass that
   file.

Attachments (screenshots) are not visible in the digest. When a report depends
on one, say so in the entry ("screenshot only") rather than guessing.

### 3. Extract reports, follow-ups and verdicts

Read the whole digest; do not skim it. Classify each stretch of conversation:

- **A new bug report** — a defect, regression, crash, glitch or surprising
  behavior somebody describes or shows. One thread is one entry, even when it
  runs for dozens of messages. Feature requests, design arguments, test-flake
  notices from CI bots and plain chat are not bug reports; skip them.
- **A follow-up to an existing entry** — more detail, a repro, a cause, a
  statement that it is fixed, or a decision by Alex. Update that entry: add the
  message links, the people who joined, and a dated note. Do not create a
  second entry.
- **A regression of a closed bug** — create a new entry and name the closed ID
  in its notes.

Entry fields (see "How to read this file"):

- **ID**: chat prefix plus the id of the first message that describes the
  problem. Never renumber an existing ID.
- **Reported by**: the author of that first message. People who joined the
  discussion go in "also". Someone who was only tagged to take it is not the
  reporter.
- **Messages**: the first message first, then the few that carry the cause,
  the repro or a decision. Always at least one link.
- **Title**: under about 80 characters, names the symptom.
- **What it is**: at most two sentences, understandable without opening the
  chat: what happens, where, and what is known about the cause. Longer
  explanations go in Notes.
- **Notes**: decisions with the name and date (`Alex (2026-10-09): ...`),
  hypotheses, related entries or issues.

### 4. Reconcile with GitHub and dev

People do not always file issues, so check both.

1. New issues since the last reconciliation:
   `gh issue list --repo Actual-Chat/actual-chat --state all --search "created:>=<date>" --limit 1000 --json number,title,state,createdAt,closedAt,author,stateReason`
2. Every issue already linked in `docs/bugs.md`:
   `gh issue view <n> --repo Actual-Chat/actual-chat --json state,closedAt,stateReason,title`
3. Commits and merged PRs since the last reconciliation:
   `git log origin/dev --since=<date> --format='%h %ci %s'` and
   `gh pr list --repo Actual-Chat/actual-chat --state merged --search "merged:>=<date>" --json number,title,mergeCommit`
4. Match each active bug without a link against the issue titles and commit
   subjects, and run `gh issue list --search "<symptom keywords>" --state all`
   for the unmatched ones. Link only when it is the same defect. A similar but
   different issue is linked as `(related)`; an uncertain match as `(maybe)`.
5. Closure. Move an entry to `docs/bugs-closed.md` when its issue is closed
   and the fixing commit is reachable from `origin/dev`
   (`git merge-base --is-ancestor <sha> origin/dev`), or when a commit on dev
   names the bug and clearly fixes it. Verdicts: `Fixed`, `Fixed (probable)`,
   `Fixed (per Alex)` (no code check), `Not a bug`, `Won't fix`, `Duplicate`,
   `Out of scope`. The evidence column holds the commit, PR or issue. An issue
   closed without a fix on dev (for example "not planned") is reported, not
   moved silently.
6. When the evidence is weaker than that, leave the entry where it is and list
   it in the report as "probably fixed — confirm".
7. Update the reconciliation table with the highest issue number seen, the
   `origin/dev` commit and today's date.

### 5. Classify new entries

New entries get review `Needs review` and a proposed priority with a `*`:

- High — crash, data loss, cannot sign in, messaging or live voice/video broken
  for many users, security.
- Medium — a real defect with a workaround, or one that affects a narrower
  group or platform.
- Low — cosmetic, rare, one-off, or too vague to act on.

Never change an entry that a human has decided: keep review states
`Confirmed`, `Confirmed (investigate)`, `Needs check`, `Postponed` and
`Kept in the list`, and priorities without a `*`, exactly as they are unless a
new message carries a new decision. When Alex states a decision in the chat,
record it as a dated note and update the state and priority.

### 6. Auto-review (only with `--auto-review`)

For each entry in scope with review `Needs review`, try to reach a verdict from
evidence, not from the wording of the report:

- Does the code on `origin/dev` still have the defect? Read the code path.
- Is there a fix commit or issue already (step 4)?
- Is it expected behavior (docs, code, a team decision in the chat)?
- Is it a duplicate of another entry?

Set review to `Proposed` and put the verdict in Notes as
`Auto: <real bug | not a bug | duplicate of ID | already fixed> — <evidence>`
only when the evidence is clear. When it is not, keep `Needs review` and write
`Auto: unsure — <what is missing>`; say in the report that a human review is
needed. Never set `Confirmed`: only a human does that. A `Proposed` entry stays
in `docs/bugs.md` until a human confirms it, except where step 4.5 applies.

### 7. Write the documents

Skip this step with `--dry-run`.

In `docs/bugs.md`: advance the scan table to the last message pulled (first
and last id, UTC time range, scan date), update the reconciliation table, add
or update entries, and regenerate the summary table and the three priority
sections. Order within a priority: `Confirmed` first, then `Confirmed
(investigate)`, `Needs check`, `Proposed`, `Needs review`, `Kept in the list`,
`Postponed`. Postponed entries sit in the Low section. Keep every entry's
sections in the same order as the existing entries.

In `docs/bugs-closed.md`: add a row for each moved entry. Do not rewrite older
rows.

Dates are UTC. Links to files outside `docs/` break the docs site build, so
use plain text for commits and files, and full URLs for GitHub and voxt.ai.

### 8. Report

Print, in this order, with a message link on every bug (`[B26936](url)`):

1. **New since the last scan** — a table grouped by proposed priority: ID,
   title, reporter, link.
2. **Updates picked up** — follow-ups, decisions, and anything marked fixed in
   the chat.
3. **Moved to closed** — ID, verdict, evidence.
4. **Probably fixed — confirm** (step 4.6).
5. **Review queue** — the entries that are `Needs review`, then `Needs check`,
   with links, and the count of `Confirmed` ones that are ready to fix.
6. With `--auto-review`: what got a `Proposed` verdict, and what needs a human.
7. Confirmed bugs that have no GitHub issue. Offer `/issue` for them; do not
   file anything unprompted.

End with the two files that changed and the scan boundaries now recorded. Do
not commit; if the user asks, commit the two docs on their own.
