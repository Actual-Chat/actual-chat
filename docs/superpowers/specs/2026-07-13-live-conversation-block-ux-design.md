# Live Conversation Block UX — Joined Mode & Seamless Close

**Date:** 2026-07-13
**Status:** Approved design, pending implementation plan

## Problem

The live session conversation block works well for not-joined viewers, but the
joined-viewer experience has four defects observed in a real 6-minute
two-person call:

1. **Premature expand icon.** The card shows an expand icon from the start
   (`hasSummary = !IsVoiceOnly` in `ConversationMessageView.razor`), but before
   the first summary there are no collapsed entries — expanding does nothing.
2. **Disconnected live tail.** For a joined viewer, live transcript entries
   render below the card as ordinary standalone messages; nothing shows they
   belong to the live conversation.
3. **No title/description during the call.** The first LLM summary is gated on
   1200 mature words + 10 entries with a 3-minute maturity lag
   (`ChatSettings.Summarization`) — unreachable in a short call, so the card
   never gets a title and other members have nothing to base a join decision on.
4. **Non-seamless close.** `LiveSessionsBackend.CloseWithFinal` materializes a
   persisted `Conversation` only when a title exists. Without one the live block
   vanishes and the offline `ConversationSplitFlow` recreates the conversation
   minutes later. Even with a title, `EndEntryLid` is frozen at the last
   mid-call summary, so the tail spoken after it stays outside the block.

## Decisions (from brainstorm)

- Joined mode keeps **progressive collapse**: the summarized range keeps
  folding into the card mid-call, the live tail renders **inside a group
  container** under the card, and the collapsed part can still be expanded.
- Not-joined rendering is unchanged (card only, tail hidden).
- Expand icon appears **only when there is something to expand** — i.e. the
  first summary has landed and the collapsed summarized range contains entries.
- First live summary gates lowered to **150 mature words + 3 entries**, with a
  **1-minute** maturity lag; re-summaries keep the existing cadence
  (5-minute `ResummarizationDelay`, 3-minute maturity, existing gates).
- On close, run a **final full-transcript summarization pass** (no maturity
  lag), gated on the same lowered threshold. Sessions below threshold still
  vanish (their entries remain plain messages).
- Grouping is implemented in the tile builder (Approach 1 below), reusing the
  existing `ExpandedConversationMessage` group mechanism.

## Approach choice

**Chosen: extend the grouping range in the tile builder.**
`ChatUI.Tiles.GetTile` already knows the live conversation and whether the
viewer is joined (it builds `hiddenLiveTailRange` there). For live + joined,
the live conversation's *grouping* range is treated as `[StartEntryLid, ∞)`;
the `Conversation` contract is untouched (`EndEntryLid` still means
"summarized up to here").

Rejected alternatives:
- *Stretch the synthetic `Conversation`* (`LiveSessionUI.GetConversation`
  returning `EntryLidRange = [Start, chatEnd]` for joined viewers): overloads
  `EndEntryLid` semantics the summary/materialization path relies on and makes
  the conversation object viewer-dependent.
- *Render the tail inside `ConversationMessageView`*: bypasses the VirtualList
  (breaks virtualization, read tracking, scroll anchoring).

## Design

### 1. Joined-mode rendering (client)

While a session is live and the viewer is joined, the chat view renders one
group block:

- The live card (`ConversationMessage` / `ConversationMessageView`, current
  live style) is emitted as the **first item inside** an
  `ExpandedConversationMessage`-style group instead of a standalone message.
- Entries in the summarized range `[StartEntryLid, EndEntryLid]` remain
  skipped by default (progressive collapse, as today).
- The live tail (`EndEntryLid + 1 … ∞`) renders inside the same group
  container below the card: tail entries get `Conversation` set in `GetTile`,
  and `GroupExpandedConversations` wraps card + (expanded summarized entries)
  + tail into one group. Sticky-header behavior comes free from the existing
  group mechanism.
- `ToggleExpandConversation` on the live conversation works mid-call: expanding
  reveals the collapsed summarized entries between the card and the tail;
  collapsing re-hides them. Same `_expandedConversations` state as regular
  blocks.
- CSS: a `live joined` variant of the existing group / expanded-conversation
  styles gives the block a continuous background/border that visually owns the
  card and the tail (`conversation.css`).
- Not-joined view unchanged: card only, `hiddenLiveTailRange` keeps hiding the
  tail.

Touched code: `ChatUI.Tiles.cs` (`GetChatItemsInternal`, `GetTile`,
`GroupExpandedConversations`, `ConversationViewState`),
`ConversationMessageView.razor`, `conversation.css`.

### 2. Expand icon gating (client)

The expand/collapse icon on the live card renders **iff the collapsed
summarized range contains at least one entry** (the first summary has landed —
only `LiveSessionsBackend.UpdateSummary` advances `EndEntryLid`). While
expanded, it renders as the collapse icon. Before the first summary the card
shows no icon — just the title fallback (participants), meta row, and
"You on the call" / "Tap to join".

### 3. First-summary thresholds (server)

In `LiveConversationSummaryFlow` only (`ConversationSplitFlow` untouched):

- First summary (`neverSummarized`): gate on **150 mature words and
  3 entries**, maturity lag **1 minute** (matches
  `ChatEntrySummarizationDelayQuanta`).
- Re-summaries: unchanged.
- New settings in `ChatSettings.Summarization`:
  `MinLiveConversationWords = 150`, `MinLiveConversationEntries = 3`,
  `FirstLiveSummaryDelay = 1 min`.

Net effect: title/description appear ~1½–2 minutes into a real conversation.

### 4. Close-time finalize (server)

- When `LiveSessionsBackend` marks a latched transcription session
  `IsClosing`, it fires a resume event for `LiveConversationSummaryFlow`.
- The flow, instead of returning early on `IsClosing`, runs a **final pass**:
  summarize the full transcript (`matureOnly: false`, resummary throttle
  ignored), gated on the lowered threshold (150 words / 3 entries over the
  whole session), then `UpdateSummary` with the true final `EndEntryLid`.
- The backend's existing grace window (30–90 s) gives the pass time to land;
  `SelfClose` / `CloseWithFinal` then materializes exactly as today. The final
  pass is best-effort: if it doesn't land within the grace, the session
  materializes with whatever summary state exists (current behavior).
- `CloseNow` (explicit last leave) gets a short finalize grace (~20 s) instead
  of closing instantly, so the final pass isn't skipped on the common path.
- Result: the live→regular transition is an in-place swap — same block
  position, same title, block covers the whole session, no orphaned tail.
  Sessions below threshold still vanish with "Voice chat ended"; their entries
  remain plain messages (offline `ConversationSplitFlow` may fold them later,
  as today).

Touched code: `LiveConversationSummaryFlow.cs`, `LiveSessionsBackend.cs`
(closing path + `CloseNow` grace), `ChatSettings.cs`.

## Reuse

Existing abstractions reused (no new shared components introduced):

- `ExpandedConversationMessage` / `GroupExpandedConversations` — the group
  container mechanism (per explicit user direction).
- `_expandedConversations` / `ToggleExpandConversation` — mid-call expansion
  state.
- `hiddenLiveTailRange` construction site in `GetChatItemsInternal` — the
  existing viewer-dependent branch point, extended for the joined case.
- `LiveConversationSummaryFlow` + `IConversationSummarizer` — the finalize
  pass is a new mode of the existing flow, not a new flow.
- `LiveSessionsBackend.UpdateSummary` / `CloseWithFinal` /
  `ConversationBackend_Materialize` — unchanged materialization path.
- `ChatSettings.Summarization` — new thresholds live next to the existing ones.

No new components are candidates for shared placement: every change extends an
existing type in place.

## Testing

- **Flow unit tests:** first-summary gate (150 words / 3 entries / 1-min
  maturity boundary), re-summary cadence unchanged, finalize pass triggers on
  `IsClosing` and uses full transcript, below-threshold session summarizes
  nothing at close.
- **Tile-builder unit tests:** joined live tail grouped under the card;
  expanded mid-call shows summarized entries inside the group; not-joined
  output unchanged; regular (non-live) conversations unchanged.
- **UI:** expand icon absent before first summary, present after; icon state
  flips with expansion.
- **Manual two-device call:** title by ~2 min; tail visually inside the block;
  expand/collapse mid-call; both stop → block swaps in place to a regular
  conversation with no gap and no orphaned tail.
