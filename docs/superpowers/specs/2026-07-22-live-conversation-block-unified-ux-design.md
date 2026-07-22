# Live conversation block — unified UX

**Status:** Approved (design) · 2026-07-22
**Branch target:** `feat/*` off `dev`
**Predecessors:** `2026-07-20-live-block-ux-polish-design.md`,
`2026-07-21-live-block-sticky-header-footer-design.md` (both merged in PR #4062).

## Goal

Make the live conversation block one coherent, self-consistent element across
every state — unjoined, joined-collapsed, joined-expanded, and the pre-first-summary
"no title yet" phase — and make the collapsed reading experience actually usable
while a live session streams: the block swallows what scrolls above the viewport,
and a reader can pull back a little context on demand instead of expanding to the
very first message.

## Context (current behaviour)

Two structurally different layouts exist today:

- **Joined**: sticky `live-conversation-header` + a folded (summarised) range +
  the live tail rendered inside the VirtualList; expandable.
- **Unjoined**: a non-expandable `c-live-card` whose whole live range is hidden
  (`[V, ∞)`), replaced by a 2-message `LastEntriesPreview` and a "Tap to join" row.

Folding is **summary-driven**: `LiveBlockUI`'s governor advances a fold boundary
behind the newest summary, held back by a freshness lag and a viewport clamp
(`LiveFoldMath`). The collapsed card shows a title band, a description
(summary), and a `c-lc-meta-row` (author heads + `c-lc-started` "Started at
HH:MM · N messages"). Pre-first-summary the meta line reads "· 0 messages", the
expand icon appears with nothing to expand, and (per the recent review) a
title-less folded block can show two expand affordances.

Files in play: `Components/ChatView/Items/Conversation/ConversationMessageView.razor`,
`LiveConversationHeaderView.razor`, `LiveConversationHeaderState.cs`,
`ConversationLiveState.cs`; `Services/LiveBlockUI.cs`, `LiveFoldMath.cs`,
`ChatUI.Tiles.cs`, `ChatUI.cs`, `LiveSessionUI.cs`; styles `conversation.css`,
`last-entries-preview.css`. Real CSS hooks: `.conversation-message.live(.joined)`,
`.c-live-card`, `.c-lc-name/summary/meta-row/authors/meta/started/expand`,
`.c-join-row`, `.live-conversation-header`, `.live-conversation-footer`, and the
`.group:has(…)` tint rules.

## Design

### 1. One shell, tint distinguishes join-state

Joined and unjoined use the **same** block chrome (sticky header, card body,
message rows, footer). The only visual difference is the background:

- **Joined**: the existing violet border-box tint on the
  `.group:has(> .item > .conversation-message.live.joined)` /
  `…live-conversation-header` wrapper (unchanged).
- **Unjoined**: the existing `.conversation-message.live:not(.joined)` tint —
  `linear-gradient(90deg, rgba(255,202,255,.3) 2%, rgba(130,104,255,.3) 100%)`
  — applied to the whole shell as the "you haven't joined" signal.

The distinct unjoined preview/`c-join-row` layout is removed.

### 2. Sticky header

The sticky `live-conversation-header` is used in **both** collapsed and expanded
states and holds only: the activity icon (equalizer), the title (`c-lc-name`,
falling back to the participant-names string pre-title), and the expand/collapse
**chevron**. For an **unjoined** block the chevron's place is taken by a **Join**
button (§5). No author heads and no message count live in the header.

### 3. Header/meta content by phase

- **No title yet (state D):** header = icon + participant-names + chevron/Join.
  **No** description, **no** `c-lc-meta-row` (so no "0 messages", no orphan heads).
- **Title/summary present:** header shows the title; below the description the
  existing `c-lc-meta-row` returns in its current position — author heads
  (`c-lc-authors`) + `c-lc-started` "Started at HH:MM · N messages".

### 4. Collapsed joined block swallows above the viewport

While collapsed, the fold boundary **tracks the viewport top continuously and
monotonically**: everything that scrolls above the viewport is swallowed into the
card — summarised or not. The boundary advances as the reader stays with the live
tail and older lines flow up out of view; it never retreats on its own, so the
block stays a compact card. Swallowed messages are **folded (not rendered)**;
scrolling up lands on the card, not on the folded region. This replaces the
summary-driven `[V, summaryEnd)` fold as the collapsed fold rule.

The description shown in the card remains the latest summary (a recap of older
content); the swallowed **count** is exact and may run ahead of what the summary
covers — that is expected.

### 5. Join = join **and** record (block only)

The unjoined header's **Join** button:

- Label **"Join"**, no icon.
- Styled like the activity-panel join control (`btn-transparent`): violet-60
  text, subtle background, 8px radius, ~40px tall, 13.6px/500 — reuse the app's
  button component/classes rather than a bespoke button.
- Action: **joins the session and turns recording on** (active participation),
  unlike the activity panel's listen-only "Join muted".
- The activity panel's own buttons and behaviour are **unchanged**.

### 6. Unjoined = fixed-height, bottom-anchored, faded preview

An unjoined viewer sees a **limited** preview, not the full live tail:

- The last ~N messages (default 5) in a **fixed-height** region so incoming
  messages never grow the block or jump the layout.
- **Bottom-anchored** (`justify-content: flex-end`): the newest line is always
  fully visible at the bottom; older lines rise up and **fade out** under the card
  via an opacity gradient at the top of the region.
- This is a self-contained card region (an evolution of `LastEntriesPreview`),
  **not** VirtualList swallowing — so it sidesteps the no-jump machinery entirely.

### 7. "Show more" pill — read context without expanding

Full expand jumps the reader to the conversation's first message, losing their
place. Instead, a collapsed joined block offers an in-place reveal:

- A **rounded pill** (the Call/Map-switch container style: white, 1px border,
  soft shadow, ~30px tall, tight padding) **straddles the collapsed card's bottom
  edge** — half over the tinted card, half over the messages below.
- Label: **"▲ Show N earlier messages of M"** — N = the reveal batch
  (≈ one viewport's worth of messages), M = the total still swallowed.
- Each click **reveals another batch in place** (older messages rendered above
  the current view, bottom-anchored so nothing jumps; the reader scrolls up into
  them), decrementing M by the batch. Repeat until none remain, then the pill
  disappears.
- Mechanically this **retreats the fold boundary by a "revealed" count**; the
  reveal persists until the block is expanded or re-collapsed (re-latched).

The header **chevron** remains the separate full expand/collapse control. There is
**no** segmented/toggle switch.

### 8. Expanded joined block = a regular expanded conversation

When expanded: sticky header (icon + title + collapse chevron), all messages
rendered like normal conversation rows (avatar left, author line, message text on
the next line), and the thin rounded `live-conversation-footer`. **No description
box. No message count in the header** — identical to a regular expanded
conversation. (The recent duplicate-expand-button fix already lives on `dev`.)

## Reuse

### Existing abstractions to reuse (research done)

- **Fold governor** — extend `LiveBlockUI` / `LiveFoldMath`, do **not** build a
  new folding path. The governor already consumes `ItemVisibility`; the change is
  to derive the fold boundary from the **viewport-top lid** (monotonic max) plus a
  reader-controlled "revealed" offset, instead of the summary end + lag. Its
  overlay/freeze/materialisation machinery and @key stability stay as-is.
- **Preview** — evolve `LastEntriesPreview.razor` / `last-entries-preview.css`
  into the fixed-height, bottom-anchored, top-faded variant rather than a new
  component.
- **Meta-row** — reuse the existing `c-lc-meta-row` / `c-lc-authors` /
  `c-lc-started` markup and CSS unchanged (just gate its visibility on
  title/summary presence).
- **Join button** — reuse the activity-panel button component/classes
  (`btn btn-sm btn-transparent`, violet-60), not a hand-rolled button.
- **Join-to-record** — reuse the existing recording-start path
  (`ChatAudioUI` recording + `LiveSessionUI` join) rather than new plumbing;
  the block's Join composes join + start-recording.
- **Tint scoping** — reuse the existing `.group:has(…)` CSS pattern already used
  for the joined tint and sticky header.
- **Show-more base style** — start from the existing `.show-more-btn` colour token
  (`--cr-item-badge-selected-text`) and the switch's rounded-pill visual.

If any of the above turns out not to fit during planning, that must be called out
explicitly rather than silently forked.

### New components & placement

- **Show-more straddling pill** — a small view + CSS. It is specific to the live
  block, so default placement is
  `Components/ChatView/Items/Conversation/` alongside the other `c-lc-*` styling.
  *Reusability note:* "a pill button centred on a container's bottom edge" is a
  generic pattern; if a second use appears, promote the CSS to a shared
  `conversation`/`components` stylesheet. Recommend **local** now, with the
  promotion path noted.
- **Fixed-height faded preview** — folded into `LastEntriesPreview` (already a
  shared-ish item component), not a new file.
- **Revealed-count state** — small client-side state on the fold governor
  (`LiveBlockUI`), not a new service.

No server, protocol, DB, or Fusion-contract changes are anticipated: this is a
client rendering + governor change plus a join-action composition. (If join-to-record
needs a participation-kind nuance, that is the only candidate for a backend touch and
must be flagged in the plan.)

## Risks & open questions

- **Swallow-above-viewport is the core risk.** Making the fold boundary follow the
  viewport top (vs. the summarised range) is the subtlest change — it is
  VirtualList-coupled and must preserve the no-jump invariant while folding
  above-viewport rows and growing the card. This likely deserves its own plan
  phase with `/virtual-list-debug` verification. Folding **un-summarised** rows is
  new (today only summarised rows fold).
- **Revealed-batch lifecycle.** Persist a reveal until expand/re-collapse; define
  precisely when it resets (block rebuild, session close/re-latch) so it can't
  strand a half-revealed card. Batch size ≈ one viewport of messages — confirm the
  exact measure (visible message count vs. a fixed fallback) during planning.
- **Description vs. swallowed-count skew** is intentional (recap lags the exact
  count); make sure copy ("of M") reads from the true swallowed count, not the
  summary's `MessageCount`.
- **Unjoined preview privacy** — unjoined viewers now see ~5 recent lines (faded),
  a limited teaser, not the full transcript; this matches the prior "preview"
  intent, just restyled.

## Out of scope

- Changing the activity panel.
- The expanded-block footer showing author heads/count (regular conversations put
  these in a footer; the live footer stays a thin band for now).
- The tier-1/2/3 close choreography, materialisation, and dissolve (unchanged).
- Any recomputation of summaries / `ContextStartLid` behaviour.

## Acceptance (manual, two-device)

Solo talk → a second device joins → a third **unjoined** viewer sees the unified
shell with the unjoined tint, a **Join** button, and a fixed-height faded 5-message
preview that never jumps as new lines arrive. The joined reader, collapsed, sees
the block swallow lines above the viewport as they read; the **"Show N of M"** pill
straddles the card edge and reveals a batch in place (no jump) with M decrementing;
the meta-row shows heads + "Started at HH:MM · N messages" only once a title/summary
exists (no "0 messages" before). Expanding reads as a regular expanded conversation
(no description, no header count); collapsing returns to the card. Tapping **Join**
joins **and** starts recording. Watch the viewport at every transition; use
`/virtual-list-debug` if anything jumps.
