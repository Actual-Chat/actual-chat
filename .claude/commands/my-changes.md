---
allowed-tools: Bash, Read, Glob, Grep
description: Summarize the current GitHub user's commits across all branches today (or a given window), grouped by category and branch.
argument-hint: [time window | starting from <sha> | as table | 2x | compact | ...]
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

`$ARGUMENTS` is free-form. Parse out three independent things — a **time
window**, a **format override**, and a **detail level** — in any order.

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

### 1. Identify the current user

Run in parallel and union the results:

```bash
gh api user --jq .login
git config user.name
git config user.email
```

A commit "belongs" to the user if its author name **or** email **or** the
linked GitHub login matches any of these. When filtering with `git log`, pass
multiple `--author=` filters (git ORs them) covering all three values.

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

## Constraints

- **Read-only.** No `git push`, no `git fetch`, no checkouts, no commits, no
  branch creation. Just `git log`, `git branch --contains`, `git show`.
- **Don't change cwd.** Use `git -C <path>` for the sibling repo.
- **Across all branches** means `--all` — including remote-tracking refs.
  Branches that only exist on `origin` should still appear.
- Use parallel Bash calls when gathering independent data (identity lookup,
  current-repo log, sibling-repo log, branch lookups for distinct commits).
