# Live Conversation Block — UX Polish (Round 3)

**Date:** 2026-07-20
**Status:** Approved
**Predecessor:** `2026-07-13-live-conversation-block-ux-design.md` (shipped as
`6c0551b55` + follow-up fixes, published in the latest release)

## Feedback driving this round

Source: Voxt conversation Alexey Kochetov ↔ Александр Якунин, 2026-07-20
(entries 52521–52651). Distilled:

1. **Close-time collapse jump** (52645, 52649, 52651): a participant hangs up
   and the expanded call snaps into a summary card — "you were reading the
   bottom message and it vanished." Observed as two visible steps: the card
   restyles, the tail messages linger, then they disappear.
2. **Summary-landing jumps** (52525, 52560): on long calls the block "jumps
   terribly" when a summary pass folds more entries; the fold feels too early
   and jerky ("подворачивается рановато, дёргается").
3. **No transition on card size changes** (52538): when the first
   summary/title lands, or the summary updates, the card height changes with
   a hard cut.

Scope decision: pure UX polish, no feature gate, no behavior removal. The bar
is "no user frustration / no unexpected viewport motion." Server-side design
from round 2 is correct and stays untouched.

## Decisions

- **Close policy — no live-viewport collapse.** The no-hide rule extends to
  session close: anyone currently rendering the block (joined, or watching it
  expanded) keeps their exact visual state when the session ends; only the
  card restyles live → regular. The tier-3 "collapsed by default" outcome
  applies solely to renders that start after close (fresh loads, later
  visits). This supersedes round 2's "collapse for everyone at close" for the
  currently-watching viewport; the persisted tiering (`IsExpandedByDefault`)
  is unchanged.
- **Mid-call fold — lag + viewport guard.** Progressive folding stays, but it
  may never touch recent content (freshness lag, default 3 min) nor content
  currently on screen (viewport guard). Folding becomes observable only
  off-screen.
- **Transitions — card-scoped only.** Animate the card's own height change on
  summary appear/update and in-card tail trim. The live → regular restyle
  needs no animation. Entry folding is never animated — the guard makes it
  invisible instead. Must be verified in the browser against the
  VirtualList's no-jump invariant, with an opacity-only fallback.
- **Mechanism — client-side fold governor** (approach A). All stability rules
  live in the client tile pipeline; zero server/protocol/DB changes; the lag
  is instantly tunable.

## Design

### 1. Fold governor

A per-chat latched fold boundary consumed by `ChatUI.Tiles` in place of the
raw `LiveFoldRange` end.

- **Base:** the server fold range as computed today —
  `[EffectiveVisibleStartLid, EndEntryLid + 1)`, gated on `LastSummaryAt`.
- **Freshness lag:** the boundary never advances past the first entry younger
  than `FoldLag` (default 3 min). Entry age is measured against `BeginsAt`
  with the server-synced clock, so viewers converge on the same boundary.
- **Viewport guard:** the boundary never advances past
  `ItemVisibility.MinMessageLid` while any of the block's entries are on
  screen. Folding claims only off-screen entries; a deferred fold happens on
  a later recompute, after the reader scrolls away.
- **Monotonic latch:** the effective boundary only moves forward and is held
  per chat in an in-memory `MutableState<long>`. Scrolling never re-folds or
  unfolds anything, and visibility churn cannot cause tile-recompute storms.
- **Scheduling:** a small update loop (in `ChatUI`/`LiveSessionUI`)
  recomputes on live-state and visibility changes and arms one timer at the
  next lag-expiry moment, so the boundary advances without polling.

The unjoined path (`hiddenLiveTailRange`, in-card live tail) is untouched;
its perceived jumpiness is the card-height problem addressed in §3.

### 2. Close snapshot — no live-viewport collapse

When the client is rendering a live block and the session closes:

- `ChatUI` captures a session-local
  `ClosedLiveSnapshot(conversationId, foldBoundary)` at the moment the live
  state disappears.
- While the snapshot exists, the tile builder renders the materialized
  conversation exactly as the live block looked: card (regular-styled) +
  folded range up to the snapshot boundary + visible tail — ignoring
  `IsExpandedByDefault`. Nothing on screen moves.
- **Lifetime:** until the user navigates away from the chat, or manually
  toggles the conversation; the toggle clears the snapshot and hands over to
  normal expanded/collapsed rendering. App restart or fresh render elsewhere
  → shipped tiered defaults.
- **Tier-1 close** (below the title gate): entries already stay; only the
  card vanishes — kept, optionally with a fade-out.

This dissolves the observed two-step close: step 2 (messages disappearing)
no longer happens for a watching viewer.

**Determinism (freeze reacts, not latches).** The freeze must never lag the
leave/close by even one render, or a hang-up can flash a collapsed frame
before the overlay lands. So the overlay is a **reactive derivation** inside
`LiveBlockUI.GetBlockState` from three signals — a per-viewer monotonic
"was attending this block" latch (set at join), `AmIInLiveConversation`, and
the raw live state — not an async write from the governor loop. The instant
`AmIInLiveConversation` flips (or the raw state disappears), `GetBlockState`
recomputes with the overlay already present. The governor keeps only what is
not race-sensitive: advancing the fold boundary and refreshing the frozen
template (V, fold range, materialized id, tail start) while the viewer is
joined. The "attending" latch and the template are seeded together at state
creation so a join-then-immediate-leave still freezes. Scope: joined viewers
only — a never-joined watcher still follows the shipped tiered default at
close.

### 3. Card size transitions

In `ConversationMessageView` (including the unjoined in-card tail): animate
the card's own height change when the first summary/title appears, when the
summary text updates, and when the in-card tail trims — ~200 ms ease plus a
crossfade of the swapped text.

**Verification is part of the design:** a browser pass (chrome-devtools MCP +
`/virtual-list-debug`) must confirm the VirtualList's ResizeObserver and
anchoring stay clean during the transition. If the animation fights the
no-jump invariant, fall back to an opacity-only crossfade with an instant
height change.

### 4. No server changes

Flows, backends, wire models, and DB stay as shipped in round 2. `FoldLag`
is a client-side constant.

## Reuse

Existing abstractions:

- `ChatUI.ItemVisibility` (`MutableState<ChatViewItemVisibility>`, fed by the
  VirtualList) — viewport-guard input; `VisibleMessageLids` /
  `MinMessageLid` already exist.
- `LiveSessionUI.GetState` (raw `LiveSessionState`) and the `LiveFoldRange`
  computation in `ChatUI.Tiles.GetChatItemsInternal` — the governor slots in
  where the raw range is consumed.
- Conversation expansion overrides in `ChatUI`
  (`ToggleExpandConversation`) — the snapshot-clearing hook on manual toggle.
- Fusion `MutableState` + `MomentClockSet` (server clock) — governor state
  and lag math.
- `/virtual-list-debug` skill — animation verification.

New components and placement: the fold governor and close snapshot are
chat-view presentation state, specific to this feature — they live in
`src/dotnet/UI.Blazor.App/Services/` beside `ChatUI` (no `Core` promotion;
nothing else consumes per-viewport fold state). `FoldLag` goes to the
existing client-side constants where the live block's other tunables live.

## Testing

- **Unit:** governor math — lag clamp, viewport clamp, monotonic latch,
  next-wake-up scheduling.
- **UI integration** (`Chat.UI.Blazor.IntegrationTests`): close with an
  active snapshot keeps the exact rendered item set; fold defers while
  entries are visible and completes after they leave the viewport; manual
  toggle clears the snapshot.
- **Manual:** two-device pass over hang-up, long-call summary landings, and
  the card animation, watched with `/virtual-list-debug`.
