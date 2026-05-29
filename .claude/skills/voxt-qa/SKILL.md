---
name: voxt-qa
version: 1.0.0
description: |
  Answer questions about Voxt — its features, what shipped and when, and how to use
  it — grounded in three sources: the public "Voxt Announcements" chat, the Landing
  pages, and the user guide. Use when asked things like "what features shipped this
  year", "list new features", "how do I use <feature>", "what is Voxt", or any
  product/usage question about Voxt. Answers are cited to the source.
allowed-tools:
  - Read
  - Glob
  - Grep
  - mcp__voxt-robokitty__list_messages
  - mcp__voxt-robokitty__get_id_range
  - AskUserQuestion
---

# /voxt-qa — Answer questions about Voxt

Answer the user's question about Voxt using **only** the three grounded sources
below. Do not rely on prior assumptions about the product — read the sources,
then answer. Cite which source each fact came from.

## The question

`$ARGUMENTS` is the question. If it's empty, ask the user what they'd like to
know (one short `AskUserQuestion`), then proceed.

## Sources (read these before answering)

### 1. "Voxt Announcements" chat (voxt-robokitty MCP)

The chronological, authoritative timeline of what was delivered and when.

- **chatId:** `announcements` (a public group chat — readable even though the
  RoboKitty bot isn't a member).
- Read it with `mcp__voxt-robokitty__list_messages`. Call
  `mcp__voxt-robokitty__get_id_range` first to learn the LID range, then page
  with `list_messages(chatId, afterId, limit)` (limit capped at 1024) until you
  reach `lastId`. Start with `afterId: null`.
- Each message has `createdAt` (epoch **milliseconds**), `text`, `authorName`,
  and `attachments`. Use `createdAt` for any date/time filtering — e.g. "last
  year" means `createdAt` within the trailing 12 months from today's date.
  Today's date is provided in your context; convert it to an epoch-ms cutoff.
- Skip messages where `isRemoved` is true. `isSystem` messages are usually not
  features — include them only if relevant.
- Treat consecutive messages from the same author near the same time as one
  announcement when summarizing (the author often splits one update across
  several short messages).

### 2. Landing pages

`src/dotnet/UI.Blazor.App/Pages/Landing/` — the marketing description of
features (what they are, how they're positioned). Read **all** of it,
including the `Docs/` subfolder. The `.razor` files hold the user-facing copy;
`landing.ts` / `*.ts` / `landing.css` are presentation — skim only if needed.

- Use `Glob` (`src/dotnet/UI.Blazor.App/Pages/Landing/**/*.razor`) to enumerate,
  then `Read` the relevant ones. `LandingPage*.razor`, `PremiumFeaturesModal.razor`,
  and `Docs/Docs*Content.razor` carry the most feature/FAQ content.

### 3. User guide

`docs/user-guide/` — the how-to documentation.

- **This folder may not exist yet** (it's planned). Use `Glob`
  (`docs/user-guide/**/*.md`) to check. If it's empty or absent, say so briefly
  and answer from sources 1 and 2 — do **not** treat the absence as an error.

## How to answer

1. Read the sources relevant to the question (always the announcements chat for
   "what/when shipped" questions; always Landing + user guide for "how do I…"
   questions; read all three when in doubt).
2. Answer directly and concisely. Prefer the user's framing (a list when they
   ask for a list, steps when they ask how-to).
3. **Cite the source** for each non-obvious claim — `(Announcements, 2025-03-14)`,
   `(Landing: LandingPage4)`, `(User guide: getting-started.md)`. For
   announcements, include the message date so the timeline is verifiable.
4. If sources **conflict** (e.g. Landing describes a feature the announcements
   never mention, or vice versa), surface the discrepancy rather than silently
   picking one.
5. If the answer isn't in any source, say so plainly — don't invent it.

## Notes

- This is read-only. Never post to, edit, or remove anything in the chat.
- For "features delivered in the last year" style asks, the deliverable is a
  deduplicated, chronological (or grouped-by-theme) list with dates from the
  announcements timeline, cross-referenced against Landing copy where it adds
  detail.

Arguments: $ARGUMENTS
