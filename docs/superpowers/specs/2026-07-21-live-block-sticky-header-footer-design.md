# Live Conversation Block — Sticky Header, Scrollable Body, Footer

**Date:** 2026-07-21
**Status:** Approved
**Predecessor:** `2026-07-20-live-block-ux-polish-design.md` (round-3 polish, shipped
on `feat/live-block-ux-polish`)

## Motivation

Feedback: in an **expanded, joined** live conversation, when you scroll through a
run of messages from one author, that author's badge/avatar does **not** stay
pinned like it does in a regular expanded conversation. Regular conversations
pin the author avatar with `position: sticky` so it rides the top of its author
group until the next author pushes it up.

Root cause (verified in code): the joined-live block's entries already render
with the **same** author-group markup as regular conversations — `.group` →
`ChatEntryAuthorGroupView` → the sticky `.avatar-badge`
(`chat-view.css:717-747`). The sticky is broken only because the joined-live
group wrapper carries `overflow: hidden`
(`conversation.css:315-320`) to clip the fixed-height purple tint gradient, and
`position: sticky` is killed by any `overflow: hidden` ancestor between the
sticky element and the scroll container.

Goal: make the joined/expanded live block behave and read like a regular expanded
conversation — a short sticky header, a scrollable body, sticky author badges,
and a closed box — with no VirtualList jump regressions.

## Decisions

- **Close the box with a footer, don't inner-clip.** Give the live block a real
  footer so it becomes a bounded box; the tint then becomes the block's own
  background (no fixed-height gradient, no spill), the `overflow: hidden` goes
  away, and sticky lights up. Rejected alternative: keep clipping but move
  `overflow: hidden` onto an inner tint-only layer — smaller, but a hack that
  doesn't move the block toward the regular-conversation structure.
- **Live-styled minimal footer.** Reuse the block-closing *structure* of the
  regular footer, but render a minimal live band (rounded bottom + subtle
  live/pulse indicator, no authors / message-count / "ended at X" summary), so an
  ongoing call never reads as finished. On close, the block materializes and gets
  the real `ConversationMessageFooter` through the existing path.
- **Split the card into a sticky title band + a scrollable description.** The
  monolithic card can be multiple lines (summary) and would eat the viewport if
  pinned whole. Instead:
  - **Sticky title band** = live/pulse indicator + title (falls back to
    participant names before a summary lands) + expand/collapse chevron (only
    when there are folded entries). Short, solid background — the
    `ConversationHeader` equivalent.
  - **Scrollable description** = summary text (keeps the appear/update height
    animation), participant/meta row, and call controls. Scrolls out of view; a
    recent joiner scrolls up to read the recap.

## Design

### 1. Structure (top → bottom), joined/expanded live block

1. **Sticky title band** — replaces the monolithic card as the block's sticky
   element. `position: sticky` at the top of the scroll container with a solid
   background (occludes the description scrolling up behind it), styled like the
   regular sticky `ConversationHeader` (`virtual-list.css:143`). Short.
2. **Scrollable description** — the remaining card content (summary + meta +
   controls; the tail preview for the unjoined case). A normal block body row.
3. **Author-grouped entries** — unchanged (`.group` → `ChatEntryAuthorGroupView`
   → sticky `.avatar-badge`). Badges pin below the title band via the existing
   `top-20/top-16` offset (`chat-view.css:736`).
4. **Live footer** — a minimal live-styled rounded bottom band, emitted as the
   block's **last child** so it rides the live tail as new messages arrive.

The whole block is one `ExpandedConversationMessage` `<li>` — the sticky
containing element the code already intends
(`GroupExpandedConversations`, `ChatUI.Tiles.cs:896-902`).

### 2. Data path — footer emission

Emit the live footer as the **last child** of the live block in
`GroupExpandedConversations.FinalizeBlock` (`ChatUI.Tiles.cs:939`) when
`blockConversation.Id == liveBlockId`. Because the live block's grouping range is
`[V, ∞)`, it is always the final block and is finalized only by the terminal
`FinalizeBlock()` call — so the footer is always the true last row, after the
newest tail entry. The regular footer path (`ChatUI.Tiles.cs:774`, gated
`!= liveBlockId`) stays bypassed for the live block, so there is no double footer
and none stuck at `Conversation.EndEntryLid`. The footer item sorts after the
last tail entry and carries a stable render key.

This also covers the **frozen (left/closed-but-not-materialized) overlay** block,
which renders under the same `liveBlockId` path until it materializes.

### 3. Components

- Split `ConversationMessageView`'s card markup into a **sticky title-band**
  sub-view and a **scrollable description** sub-view. The title band shows the
  live indicator, title (participant-name fallback pre-summary), and the
  expand/collapse chevron (gated on `HasFoldedEntries`, as today). The
  description keeps the summary box + its `@starting-style`/`grid-template-rows`
  appear/update animation, the meta row, and controls. The unjoined compact
  preview keeps the tail preview inside the description; sticky is inert there
  (nothing scrolls within the small card), so that path is visually unchanged.
- New `LiveConversationFooter : ChatMessage` item + `LiveConversationFooterView.razor`
  rendering the minimal live band.

### 4. CSS

`conversation.css`:
- Drop `overflow: hidden` and the fixed-height `::before` tint gradient on
  `.group:has(> .item > .conversation-message.live.joined)` (`:315-347`).
- Make the tint the block's own background, bounded by the title band at the top
  and the footer at the bottom (the footer supplies the rounded bottom edge).
- Make the title band sticky with a solid background; verify the author
  `.avatar-badge` sticky rules (`chat-view.css:731-737`) engage and the
  `top-20/md:top-16` offset clears the band's height.

The sticky header/footer work itself needs no server, protocol, DB, or Fusion
changes. The recorder-driven liveness and immediate close/finalize changes that
ship in the same PR belong to the companion live-block UX polish work, not to
this spec.

### 5. Scope

Joined/expanded live block and its frozen overlay variant. The unjoined,
collapsed preview card is functionally unchanged (non-scrolling; sticky inert).
The determinism/fold/overlay work from the predecessor spec is untouched.

## Reuse

Existing abstractions to reuse:
- `chat-view.css:717-747` sticky author-badge rules and
  `ChatEntryAuthorGroupView` / `ChatMessageAuthorCircle` (`.avatar-badge`) group
  markup — reused as-is; the fix is removing the clipping ancestor, not new
  sticky code.
- The regular sticky-header pattern (`virtual-list.css:143`,
  `.item:has(.conversation-header) { sticky }`) — the title band mirrors it.
- `ExpandedConversationMessage` block structure and
  `GroupExpandedConversations` (`ChatUI.Tiles.cs`) — the block is already the
  sticky containing element; only the footer append is added.
- `ConversationMessageFooter` markup — reused for the real footer on close (no
  change); the live footer borrows its block-closing role, not its content.
- Existing summary appear/update animation (`conversation.css`,
  `ConversationMessageView.razor` from the predecessor) — moves onto the
  scrollable description unchanged.

Reusability of new components: `LiveConversationFooter` (item + view) is
live-block-specific — nothing else renders a live tail bottom edge — so it lives
beside the other live-block components in
`src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/`. No `Core`
promotion (UI-only, feature-specific). Local placement recommended.

## Testing

- **UI integration** (`Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs`):
  the joined live block emits exactly one live footer as its last child; the
  regular footer is not emitted for the live block; the block's author entries
  carry the `.group`/`BlockStart` structure (sticky markup present); the sticky
  title band item is present and distinct from the scrollable description.
- **Browser (two-session)**: scroll a multi-author expanded live call and confirm
  author badges pin like a regular conversation, the title band stays put while
  the summary scrolls out, the tint reads live (not ended), the summary
  appear/update animation is still clean, and `/virtual-list-debug` stays at zero
  violations across the scroll.
