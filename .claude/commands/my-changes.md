---
allowed-tools: Bash, Read, Glob, Grep, mcp__voxt-robokitty__post_message, mcp__voxt-robokitty__list_place_chats
description: Summarize the current GitHub user's commits across all branches today (or a given window), grouped by category and branch. Supports `--all` (every human author), `--post` (send to Standup chat), and a `minimal` detail level.
argument-hint: [time window | starting from <sha> | as table | 2x | compact | minimal | --all | --post | ...]
---

# /my-changes

Summarize commits authored by the **current GitHub user** in the current repo
(and the sibling `ActualLab.Fusion` repo at `/proj/ActualLab.Fusion`, if
present), across **all branches**, grouped by category and branch.

The unit of summary is a **change**, not a commit:

- One commit may contain multiple unrelated changes → split into separate items.
- Several small follow-up commits on the same topic → collapse into one item
  (or a tight cluster of items under the same category/branch).
- The commit message is a hint, not the source of truth. If the message is
  short, vague (`wip`, `fix`, `chore: misc`, `update`), or doesn't match what
  the diff actually does, **read the diff** before describing the change.

## Arguments

`$ARGUMENTS` is free-form. Parse out five independent things — a **time
window**, a **format override**, a **detail level**, a **scope flag**, and
a **delivery flag** — in any order.

**Time window:**

- (none) — today, since local midnight.
- `last 8 hours`, `last 2 days`, `last week` — relative window.
- `since 2026-05-01`, `since yesterday` — absolute or named date.
- `starting from <sha>` — git revision range that **includes `<sha>` itself**
  (translates to `<sha>^..`). This is the default for plain "starting from"
  / "since commit X" phrasings. If the user explicitly says "exclusively"
  or "after <sha>", use `<sha>..` instead.
- `<sha>..` or `<shaA>..<shaB>` — literal git range syntax, taken as-is
  (exclusive lower bound, per git's normal semantics).

**Format override:**

- `as table` / `as markdown table` — render as a Markdown table instead of
  the default bulleted layout.

**Detail level** (default is "normal" — see *Default output format* below):

- `minimal` — the smallest possible report. See *Detail level → minimal* below.
- `compact` — at most one short sentence per item, no follow-on detail.
- `2x`, `3x`, `4x`, … — produce roughly that multiple of the normal depth.
  Read the diffs more carefully and surface specific knobs, file or class
  names, numeric thresholds, etc. Don't pad — if a change genuinely doesn't
  warrant more text, leave it short.
- Free-form requests like "more detail on Video", "expand the streaming
  section", "go deep on RPC" — apply the higher detail only to the named
  category (or categories), keep the rest at normal depth.
- Combinations are fine: `last 8 hours 3x`, `compact as table`,
  `2x more detail on video`.

**Scope:**

- `--all` — produce a report for **every human author** with commits in the
  window. Exclude bot/AI authors (see *Multi-user mode (`--all`)* below).
  Without `--all`, the report covers only the current GitHub user (the
  original behaviour).

**Delivery:**

- `--post` — post the report to the **Standup** chat instead of printing
  it inline, using the same path `/robokitty-post` uses (resolve `Standup`
  → `s-pmMsV1UVKG-xigkwj28ql`, then call
  `mcp__voxt-robokitty__post_message`). With `--all`, send **one message
  per user**. See *Posting to Standup (`--post`)* below.

The most common standup-digest combination is `--all minimal --post`.

If the argument can't be parsed, say so and ask for clarification rather than
guessing.

## Default output format

```
**<Category>** — in <branch>:
- <change 1, one sentence>
- <change 2>

**<Category>** — in <branch>:
- ...
```

Rules:

- `<Category>` — inferred from the changes (e.g. *Video*, *Streaming*, *RPC*,
  *Audio*, *Build*, *Docs*, *Tests*). Use commit message prefixes
  (`feat(video)`, `fix(audio):`, `chore:`) and the touched file paths as
  hints. Bold the category name.
- `<branch>` — the most specific branch containing the commit. Prefer feature
  branches over `master`, `main`, or `dev`. If a change genuinely spans
  multiple branches, emit one section per branch under the same category.
- **Section order: most important categories first.** Rank by impact and
  size — user-visible features and significant refactors come before bug
  fixes, and bug fixes before chores/build/docs/tooling. Within a category,
  feature branches come before `dev`/`main`/`master`. The sibling
  `ActualLab.Fusion` category always sits at the bottom regardless of its
  importance. (Do *not* sort categories alphabetically.)
- `<change>` — by default, **a sentence, or 2–3 sentences if the change is
  worth explaining** (a non-obvious mechanism, a numeric threshold worth
  stating, a subtle reason). Don't pad — many changes really are one-liners.
  No commit hashes, no exhaustive file path lists. Detail-level overrides
  from the args (`compact`, `2x`, `more on Video`, …) modify this — see
  *Detail level* below.
- If the user passed a format override (e.g. `as table`), use that instead
  while keeping the same data. For tables, use **two columns**: the first
  cell stacks the category name (bold) and the branch on two lines using
  a `<br/>` separator, the second cell is the change. For sibling-repo
  rows, prefix the category with the repo name (e.g.
  `**ActualLab.Fusion / Core**`). This renders nicely in Claude's own
  Markdown output where each category cell shows two lines.

## Detail level

The default ("normal") is described above: 1 sentence, or 2–3 when something
is genuinely worth explaining.

- **`minimal`** — the **smallest** possible report. Per user, render:

  ```
  **<UserName>** — <period>, `+<added>`/`-<removed>`:
  - <item, ≤ 8 words>
  - <item, ≤ 8 words>
  - ...
  ```

  Rules:
  - **Drop all cosmetic changes** before listing — whitespace/formatting,
    pure renames (no behaviour change), comment-only edits, typo fixes,
    dependency version bumps, CI / tooling tweaks without behaviour impact,
    generated-file changes. Detect via subject keywords (`format`, `lint`,
    `typo`, `rename`, `bump`, `chore: comments`, …) **and** by reading
    `--stat`/`--numstat` when the subject is generic.
  - Each item is **≤ 8 words**, present tense. No commit hashes, no file
    paths, no numeric thresholds (unless the number IS the change).
  - **Aggressively collapse** follow-ups (3 small RPC fixups → one item:
    `Hardened RPC reconnect path`).
  - No category headers, no branch headers, no sibling-repo grouping — all
    items fold into one flat list per user. Mention `ActualLab.Fusion` in
    the item text only when it's load-bearing.
  - Order items most-impactful first (the same ranking rule that orders
    categories in the default layout).
  - **Header line carries the period + LOC** in the form
    `` **<UserName>** — <period>, `+<added>`/`-<removed>`: `` —
    em-dash separator, trailing colon, all on a single line.
    The period uses the same rounded units as the standard footer
    (minutes/hours/days/weeks). LOC is **scoped to the user** and uses
    backticks around each side. No commit count, no branch list, no SHA.
    Examples:
    `` **Alex Yakunin** — 11h, `+3208`/`-1054`: ``,
    `` **Iq Mulator** — a single commit, `+246`/`-61`: ``.
  - **No separate summary line at the bottom**, and **no blank line
    between the header and the first bullet, or between the last bullet
    and anything that follows**. The block is `header → bullets`, period.
  - No combined cross-user footer in `--all` mode — each user's block is
    self-contained.

- **`compact`** — at most **one short sentence per item, no exceptions**.
  Strip numeric thresholds, class names, and "why" notes unless they're the
  whole point of the change. Prefer fewer items by collapsing tightly
  related commits aggressively.
- **`2x` / `3x` / `Nx`** — aim for roughly that multiple of the normal
  word count *across the whole report*. Achieve depth by:
  - Reading more of the diff (`git show <sha>` for the touched files), not
    by repeating yourself.
  - Naming the specific knobs that changed: constants, thresholds, types,
    method/file names, before→after values.
  - Adding a brief "why" or context line when the diff supports it.
  - Splitting one bullet into multiple bullets if the commit really
    contained several distinct sub-changes.
  Don't fabricate detail. If a change doesn't justify more text at the
  requested multiple, leave it short and put the extra depth into the
  changes that do.
- **Per-category requests** (e.g. "more detail on Video", "expand
  streaming", "go deep on RPC and Audio") — apply the higher detail level
  *only* to the named categories. Other categories stay at normal depth.
- **Be honest.** If the user asks about something that isn't actually in
  the diffs in the window (e.g. "tell me about the L1T2 change" when there
  is no L1T2 change in range), say so explicitly rather than inventing
  detail. Offer to widen the window if appropriate.

## Steps

### 1. Identify the target author(s)

**Without `--all`** — single-user mode. Run in parallel and union the results:

```bash
gh api user --jq .login
git config user.name
git config user.email
```

A commit "belongs" to the user if its author name **or** email **or** the
linked GitHub login matches any of these. When filtering with `git log`, pass
multiple `--author=` filters (git ORs them) covering all three values.

**With `--all`** — multi-user mode. Don't pre-filter by author. Instead,
enumerate every distinct author in the window (both repos), then drop
bot/AI authors:

```bash
git log --all --no-merges --since="<window>" --format='%an%x09%ae' \
  | sort -u
```

(For revision ranges, swap `--all --since=…` for the range; for the
sibling repo add `-C /proj/ActualLab.Fusion`. Combine both repos' author
sets before de-duping.)

Coalesce idents for the same human: if two `(name, email)` rows share a
canonical GitHub login (look up via `gh api -X GET search/users -f q="<email>"`
only when in doubt — don't burn API on obvious cases), treat them as one
author. Otherwise keep them separate.

Then run steps 3–6 once **per remaining author**, passing that author's
full `--author=` ident set into `git log`.

### 2. Determine the time window

Parse `$ARGUMENTS`:

| Input | Translate to |
|---|---|
| (empty) | `--since="00:00"` (today, local midnight) |
| `last <N> hours` / `last <N> days` / `last <N> weeks` | `--since="<N> <unit> ago"` |
| `since <date>` | `--since="<date>"` |
| `starting from <sha>` (default: inclusive) | `<sha>^..` revision range, no `--since` |
| `starting from <sha> exclusively` / `after <sha>` | `<sha>..` revision range, no `--since` |
| `<sha>..` or `<shaA>..<shaB>` (literal range) | use as-is, no `--since` |

Strip the format keywords (`as table`, `as markdown table`, …) before parsing
the window.

### 3. Gather commits — current repo, all branches

```bash
git log --all --no-merges \
  --author="<login>" --author="<name>" --author="<email>" \
  --since="<window>" \
  --format='%H%x09%an%x09%ae%x09%ai%x09%s'
```

Or, for a revision range:

```bash
git log <range> --no-merges \
  --author="<login>" --author="<name>" --author="<email>" \
  --format='%H%x09%an%x09%ae%x09%ai%x09%s'
```

Dedupe by commit hash (a commit reachable from multiple branches still
appears once in `--all`, but be defensive).

### 4. Annotate each commit with branch + real intent

For each commit hash:

- **Branch:** `git branch -a --contains <sha>`. Pick the most specific:
  skip `HEAD`, prefer local feature branches, then remote feature branches
  (`origin/<feature>`), then `dev`, then `main`/`master`. If the commit only
  exists on `dev`/`main`/`master`, use that.
- **Real intent:** if the subject is short (<35 chars), generic (`wip`,
  `fix`, `update`, `tweak`, `chore: misc`, `cleanup`), or appears to span
  unrelated areas based on `--stat`, dig in:
  - `git show --stat --format= <sha>` — see touched files.
  - `git show <sha> -- <path>` — read the diff for the most relevant files.
  - Use this to write an accurate summary at the requested detail level
    (see *Detail level*), not the commit subject verbatim.

### 5. Synthesize changes

- Group commits by topic (same feature, same subsystem, same bug).
- Collapse follow-up fix-ups into the parent change.
- Split commits that touched unrelated areas into multiple items, each
  filed under the appropriate category and branch.
- Write each item at the **detail level requested by the args** (default:
  a sentence, or 2–3 if the change is worth explaining; `compact`: one
  short sentence max; `Nx`: roughly N× the normal depth, possibly limited
  to named categories). Present tense, no commit hashes.

### 6. Sibling ActualLab.Fusion repo

If `/proj/ActualLab.Fusion/.git` exists, repeat steps 3–5 against that repo:

```bash
git -C /proj/ActualLab.Fusion log --all --no-merges \
  --author="<login>" --author="<name>" --author="<email>" \
  --since="<window>" \
  --format='%H%x09%an%x09%ae%x09%ai%x09%s'
```

(Always pass `-C /proj/ActualLab.Fusion` — never `cd` into it.)

Render the resulting changes under a single category named **`ActualLab.Fusion`**,
still grouped by branch within that category. Place this category at the
bottom of the output.

If the sibling repo is not present, silently skip it.

### 7. Render

Default layout: the bulleted format described above.

If `minimal` is set, render the minimal layout from *Detail level → minimal*
(per-user block, no category/branch headers, ≤ 8-word items, period+LOC
**inlined into the header**). Skip Step 8 entirely — each user block is
self-contained.

If `--all` is set, repeat the render once per remaining (non-bot) author.
Separate blocks with a blank line. In default / `compact` / `Nx` modes,
prefix each block with `## <UserName>` and put the per-user footer (Step 8)
**inside** each block — there is no combined cross-user footer. In
`minimal` mode, the `` **<UserName>** — <period>, `+<added>`/`-<removed>`: ``
line is the header; no `##` needed, no Step-8 footer.
In `as table` mode, add a leading `Author` column.

If `--post` is set, do not print the report inline — see *Posting to
Standup (`--post`)* below.

If `as table` (or `as markdown table`) was in the args, render as a
**two-column** Markdown table. The first column stacks the bold category
name and the branch on two lines (separated by `<br/>`); the second
column holds the change. Example:

```
| Category | Change |
|---|---|
| **Video**<br/>`feature/foo` | Adds an idle-session prompt that auto-stops recording after no response. |
| **Video**<br/>`feature/foo` | Rounds float fields in `PlaybackHealthSnapshot` before JS interop. |
| **Streaming**<br/>`dev` | Drops `IStreamClient` and routes consumers through `ILiveAudioStreams`. |
| **ActualLab.Fusion / Core**<br/>`main` | ... |
```

Group rows so all entries sharing the same category+branch sit together.
Order rows the same way as the bulleted layout — most important categories
first (not alphabetical), feature branches before `dev`/`main`/`master`,
sibling-repo rows last.

If no commits match the filter, output exactly one line, e.g.:

```
No commits by <user> in <window>.
```

Don't pad with empty sections, and don't invent activity.

### 8. Footer

End the report with this exact two-line footer (and nothing after it):

```
**Window:** **<N> commits** in `<repo>` (<branch summary>)[ + **<M> commits** in `<sibling-repo>` (<branch summary>)], starting at `<short-sha>` and covering a timespan of **<duration>**.
**LOCs:** `+<added>`, `-<removed>`
```

Concrete example:

```
**Window:** **15 commits** in `ActualChat` (all on `dev`) + **3 commits** in `ActualLab.Fusion` (all on `main`), starting at `c0f1ac45` and covering a timespan of **6 hours**.
**LOCs:** `+1297`, `-561`
```

Rules:

- **Counts.** `<N>` and `<M>` are the deduped commit counts from steps 3
  and 6. Bold them. Drop the sibling clause entirely if the sibling repo
  contributed nothing (or isn't present).
- **Repo names.** Use the directory basename (`ActualChat`,
  `ActualLab.Fusion`, …) wrapped in backticks.
- **Branch summary.** If every commit in a repo lives on a single branch,
  write `all on \`<branch>\``. If they span multiple branches, write
  `across \`<a>\`, \`<b>\`, …` with branches in the same priority order
  used in the body (feature branches first, then `dev`/`main`/`master`).
- **Starting SHA.** Use the oldest commit in the report's combined set
  (across both repos), short-form (≥ 7 chars, whatever `git rev-parse
  --short` returns), in backticks. If the user passed `starting from <sha>`
  literally, use that SHA instead — it's what they anchored on.
- **Timespan.** Wallclock distance from the oldest commit's author date to
  the newest commit's author date across both repos. Round to a friendly
  unit: minutes (< 1 h), hours (< 1 d, rounded to nearest hour),
  days (< 14 d, rounded to nearest day), weeks otherwise. Bold it.
  If only one commit, write `**a single commit**` instead of a duration.
- **LOCs.** Sum `--numstat` rows across both repos, skipping binary
  rows (where numstat reports `-` for both columns). Wrap each side in
  backticks: `` `+1297` `` and `` `-561` ``.

Compute LOCs from the same `git log` invocations used in steps 3 and 6:

```bash
git log <range_or_since> --no-merges \
  --author="<login>" --author="<name>" --author="<email>" \
  --numstat --format= \
  | awk '$1 != "-" { add += $1; del += $2 } END { print add, del }'
```

Run the same against `/proj/ActualLab.Fusion` with `-C` and add the totals
before printing the footer.

The footer is the last thing in the output — no closing prose, no
trailing summary sentence after it.

In `minimal` mode this footer is **suppressed** — the period+LOC is
inlined into each user's header line. In `--all` mode (non-minimal) the
footer is emitted **per user inside each block** (see Step 7); there is
no combined cross-user footer.

## Multi-user mode (`--all`)

When `--all` is set, produce one report block per **human author** with
commits in the window. Run steps 3–6 once per author.

### Identifying bots and AI authors

Exclude any author whose name OR email matches any of:

- `[bot]` suffix in the author name or login (`dependabot[bot]`,
  `github-actions[bot]`, `renovate[bot]`, `mergify[bot]`, …).
- Substring (case-insensitive) `claude`, `anthropic`, `chatgpt`, `openai`,
  `codex`, `copilot`, `cursor`, `aider` in name or email.
- Generic CI/release accounts: `noreply@github.com` alone, or accounts
  whose name is `GitHub`, `GitHub Actions`, `web-flow`, etc.

When a name/email looks human but ambiguous, you may call
`gh api users/<login> --jq .type` once per uncertain login — `Bot`
means skip. Don't burn API on obvious humans.

Commits authored by a human that carry a `Co-Authored-By: Claude …` (or
other AI) trailer **still belong to the human** — count them toward that
human's report; do not promote a parallel AI entry.

### Per-author rendering

- Default / `compact` / `Nx`: prefix each user's block with `## <UserName>`
  and keep the standard category/branch structure inside. Per-user footer
  (Step 8) goes at the bottom of each block; no combined cross-user footer.
- `minimal`: use the `` **<UserName>** — <period>, `+<added>`/`-<removed>`: ``
  header from *Detail level → minimal*. No `##`, no Step-8 footer, no
  trailing summary line.
- `as table`: add a leading `Author` column; one giant table is fine.

Order user blocks by **descending total LOC** (added + removed) in the
window. If a user has no surviving items after cosmetic filtering in
`minimal` mode, omit them entirely (don't print an empty block).

## Posting to Standup (`--post`)

When `--post` is set, do **not** print the report inline. Instead post
it to the **Standup** chat in the **Actual Chat** place using the same
path `/robokitty-post` uses.

1. Resolve `Standup` → `s-pmMsV1UVKG-xigkwj28ql` from the known-chats
   table baked into `/robokitty-post`. If that mapping is unavailable
   for any reason, fall back to listing `mcp__voxt-robokitty__list_place_chats`
   for place `pmMsV1UVKG` and matching the title case-insensitively.
2. **Single-user mode:** call `mcp__voxt-robokitty__post_message(chatId,
   text)` once with the entire report as `text`. Pass the Markdown
   verbatim — no extra prefix, no signature, no "Daily standup for …"
   intro.
3. **`--all` mode:** call `post_message` **once per user block**, in the
   same order the inline render would use. Each block is a standalone
   message — don't bundle multiple users into one message.
4. After each successful post, print a one-line confirmation to the
   current conversation: `Posted <user> → Standup (LID: <id>).`
   (For single-user mode, write `Posted → Standup (LID: <id>).`)
5. On any failure, surface the MCP error verbatim and **stop** — do not
   retry, do not continue posting remaining users without telling the
   user what failed and which users were already posted.

`--post` combines with every detail level. The expected standup-digest
invocation is `--all minimal --post`.

## Constraints

- **Read-only.** No `git push`, no `git fetch`, no checkouts, no commits, no
  branch creation. Just `git log`, `git branch --contains`, `git show`.
- **Don't change cwd.** Use `git -C <path>` for the sibling repo.
- **Across all branches** means `--all` — including remote-tracking refs.
  Branches that only exist on `origin` should still appear.
- Use parallel Bash calls when gathering independent data (identity lookup,
  current-repo log, sibling-repo log, branch lookups for distinct commits).
- `--post` is a **user-visible side effect.** Treat it the same as any
  outbound action — if the target chat resolution is ambiguous, if author
  enumeration looks wrong, or if the report would land in a public channel
  with surprising content, stop and confirm before sending.
