# Live Conversation Block UX — Joined Mode & Seamless Close

**Date:** 2026-07-13 (revised 2026-07-14)
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

Second-round findings (2026-07-14): removing or collapsing already-rendered
entries frustrates users.

5. **Disappearing solo-talker messages.** `LiveSessionsBackend.OnStreamRegistered`
   sets `StartEntryLid` when the *first* peer starts streaming
   (`LiveSessionsBackend.cs:227-232`), but the block only surfaces at the
   2-peer latch — entries a not-joined viewer watched arrive for possibly
   minutes get swallowed the moment the second peer joins.
6. **Reappearing messages after a short session.** A below-threshold close
   drops the live state, the synthetic `Conversation` disappears, and the
   previously collapsed range pops back into the list.

## The no-hide rule

Entries rendered **before the live block appeared** are protected — they never
fold into it while the session is live. Entries **born under the live block**
(≥ `VisibleStartLid`) may fold. Two sanctioned exceptions where rendered
entries do collapse:

- mid-call progressive folding of block-born entries for joined viewers
  (explicitly kept — the card is right there and expandable);
- the final collapse of a long session at close (tier 3 below) — the same
  outcome `ConversationSplitFlow` produces eventually; making it immediate and
  uniform was chosen over per-viewer expanded seeding.

## Decisions (from brainstorm)

- Two start points replace the single `StartEntryLid` role: `ContextStartLid`
  (summary context, split-flow boundary heuristics) and `VisibleStartLid`
  (visible block boundary, set at latch).
- Not-joined viewers: the card hides only `[VisibleStartLid, ∞)`.
- Joined viewers keep **progressive collapse** of `[VisibleStartLid,
  EndEntryLid]` into the card mid-call, expandable; the live tail renders
  inside a group container under the card.
- Expand icon appears **only when there is something to expand** — the fold
  range contains at least one entry.
- First live summary gates: **150 mature words + 3 entries**, **1-minute**
  maturity lag; re-summaries keep the existing cadence.
- Close is explicit and tiered (below); no waiting for `ConversationSplitFlow`.
- Tier-3 close collapses **for everyone** immediately.
- The materialized conversation covers `[ContextStartLid, end]` — what the
  split flow would have produced.
- Grouping is implemented in the tile builder (Approach 1 below), reusing the
  existing `ExpandedConversationMessage` group mechanism.

## Approach choice

**Chosen: extend the grouping range in the tile builder.**
`ChatUI.Tiles.GetTile` already knows the live conversation and whether the
viewer is joined (it builds `hiddenLiveTailRange` there). For live + joined,
the live conversation's *grouping* range is treated as `[VisibleStartLid, ∞)`;
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

### 1. Two start points (server)

- **`ContextStartLid`** — where the summary/title context begins: when the
  first peer started talking *or writing*. Computed by a backward scan from
  `StartEntryLid` using the `EntryGroupExtractor` pause constants (5-minute
  text pause, `Constants.Audio.MaxStreamDuration` for transcripts, 10× average
  pause), so it lands on the same group boundary `ConversationSplitFlow` would
  pick. Computed once in `LiveConversationSummaryFlow` on its first run (no
  cross-flow `ExtractorState` sharing), stored on `LiveSessionState`.
- **`VisibleStartLid`** — the chat end lid at the 2-peer **latch** moment
  (`SessionStartedAt` assignment in `OnStreamRegistered`), stored on
  `LiveSessionState`. The block's visible boundary: only entries at or after
  it are ever hidden or folded while live.
- The summarizer reads `[ContextStartLid, tail]`; the summary may describe
  slightly more than the block hides — accepted.

### 2. Live rendering (client)

**Not-joined:** the card hides only `[VisibleStartLid, ∞)`
(`hiddenLiveTailRange` construction in `GetChatItemsInternal` switches from
`StartEntryLid`/`EndEntryLid` to `VisibleStartLid`). Solo-period and earlier
messages stay exactly where they were — nothing rendered disappears at latch.

**Joined:** one group block:

- The live card (`ConversationMessage` / `ConversationMessageView`, current
  live style) is emitted as the **first item inside** an
  `ExpandedConversationMessage`-style group instead of a standalone message.
- Progressive collapse: entries in `[VisibleStartLid, EndEntryLid]` are
  skipped by default, absorbed into the card as summaries advance. Entries
  before `VisibleStartLid` never fold while live.
- The live tail (`EndEntryLid + 1 … ∞`) renders inside the same group
  container below the card: tail entries get `Conversation` set in `GetTile`,
  and `GroupExpandedConversations` wraps card + (expanded folded entries) +
  tail into one group. Sticky-header behavior comes free from the existing
  group mechanism.
- `ToggleExpandConversation` works mid-call: expanding reveals the folded
  entries between the card and the tail; collapsing re-hides them. Same
  `_expandedConversations` state as regular blocks.
- CSS: a `live joined` variant of the existing group / expanded-conversation
  styles gives the block a continuous background/border that visually owns the
  card and the tail (`conversation.css`).

Touched code: `ChatUI.Tiles.cs` (`GetChatItemsInternal`, `GetTile`,
`GroupExpandedConversations`, `ConversationViewState`),
`ConversationMessageView.razor`, `conversation.css`.

### 3. Expand icon gating (client)

The expand/collapse icon on the live card renders **iff the fold range
`[VisibleStartLid, EndEntryLid]` contains at least one entry** (the first
summary has landed — only `LiveSessionsBackend.UpdateSummary` advances
`EndEntryLid`). While expanded, it renders as the collapse icon. Before the
first summary the card shows no icon — just the title fallback (participants),
meta row, and "You on the call" / "Tap to join".

### 4. First-summary thresholds (server)

In `LiveConversationSummaryFlow` only (`ConversationSplitFlow` untouched):

- First summary (`neverSummarized`): gate on **150 mature words and
  3 entries**, maturity lag **1 minute** (matches
  `ChatEntrySummarizationDelayQuanta`).
- Re-summaries: unchanged (5-minute `ResummarizationDelay`, 3-minute maturity,
  existing gates).
- New settings in `ChatSettings.Summarization`:
  `MinLiveConversationWords = 150`, `MinLiveConversationEntries = 3`,
  `FirstLiveSummaryDelay = 1 min`.

Net effect: title/description appear ~1½–2 minutes into a real conversation.

### 5. Close tiers (server)

When `LiveSessionsBackend` marks a latched transcription session `IsClosing`,
it fires a resume event for `LiveConversationSummaryFlow`. The flow, instead
of returning early on `IsClosing`, runs the **final pass** over the full
transcript `[ContextStartLid, end]` (`matureOnly: false`, resummary throttle
ignored) and picks a tier using split-flow word/entry counting:

1. **Below the live gate** (< 150 words / 3 entries): no conversation. The
   live state drops; the hidden-since-latch messages appear as plain messages
   ("Voice chat ended"). Self-consistent: such a session never produced a
   summary, so `EndEntryLid` never advanced and nothing was ever folded —
   nothing previously hidden from a joined viewer reappears, and not-joined
   viewers only gain never-rendered messages.
2. **Between the live gate and the regular gate** (< 1200 words / 10 entries):
   summarize, then materialize a `Conversation` over `[ContextStartLid, end]`
   with **`IsExpandedByDefault = true`** — rendered as an expanded conversation
   (title header + all entries visible) for everyone.
3. **At or above the regular gate**: summarize, then materialize a regular
   **collapsed** conversation over `[ContextStartLid, end]`, for everyone,
   immediately at close.

Mechanics:

- The backend's existing grace window (30–90 s) gives the final pass time to
  land; `SelfClose` / `CloseWithFinal` then materializes. The pass is
  best-effort: if it doesn't land within the grace, the session materializes
  with whatever summary state exists.
- `CloseNow` (explicit last leave) gets a short finalize grace (~20 s) instead
  of closing instantly, so the final pass isn't skipped on the common path.
- Result: the live→regular transition is an in-place swap — same block
  position, same title, block covers the whole context group, no orphaned
  tail, and no dependency on the offline split flow's schedule.

Touched code: `LiveConversationSummaryFlow.cs`, `LiveSessionsBackend.cs`
(closing path + `CloseNow` grace), `ChatSettings.cs`.

### 6. Expanded-by-default conversations (server + client)

Tier 2 requires a conversation that renders expanded for *everyone*, while
expansion today is per-client session state (`_expandedConversations`):

- New persisted flag on `Conversation`: **`IsExpandedByDefault`** (default
  `false`; split-flow conversations keep the collapsed default).
- Client: effective expansion = `IsExpandedByDefault` XOR a local user
  override. `_expandedConversations` grows a collapsed-override counterpart so
  users can collapse a tier-2 block (or expand a tier-3 one) and have it stick
  locally.

### 7. Split-flow coordination (server)

- `ConversationSplitFlow`'s live-session guard widens from
  `[StartEntryLid, ∞)` to `[ContextStartLid, ∞)` once `ContextStartLid` is
  known, so it cannot summarize the pre-latch group concurrently with a live
  session that will claim it at close.
- The existing `IsAlreadyCovered` check keeps the split flow from
  re-summarizing materialized ranges afterwards.

## Reuse

Existing abstractions reused (the only new shared-model member is the
`Conversation.IsExpandedByDefault` flag):

- `ExpandedConversationMessage` / `GroupExpandedConversations` — the group
  container mechanism (per explicit user direction).
- `_expandedConversations` / `ToggleExpandConversation` — expansion state,
  extended with a collapsed-override set.
- `hiddenLiveTailRange` construction site in `GetChatItemsInternal` — the
  existing viewer-dependent branch point, rebased onto `VisibleStartLid`.
- `EntryGroupExtractor` pause constants / `GetPauseBetween` — the group
  boundary heuristics, reused by the `ContextStartLid` backward scan.
- `LiveConversationSummaryFlow` + `IConversationSummarizer` — the finalize
  pass and tier selection are a new mode of the existing flow, not a new flow.
- `LiveSessionsBackend.UpdateSummary` / `CloseWithFinal` /
  `ConversationBackend_Materialize` — the materialization path, extended with
  the tier decision and the `IsExpandedByDefault` flag.
- `ChatSettings.Summarization` — new thresholds live next to the existing ones.

## Testing

- **Flow unit tests:** first-summary gate (150 words / 3 entries / 1-minute
  maturity boundary); re-summary cadence unchanged; `ContextStartLid` backward
  scan lands on the same boundary the extractor picks (pause cases: text
  5-minute, transcript `MaxStreamDuration`, 10× average); finalize pass tier
  selection at the 150/3 and 1200/10 boundaries; below-gate session
  materializes nothing.
- **Tile-builder unit tests:** not-joined hides only `[VisibleStartLid, ∞)`
  (pre-latch entries stay); joined fold range is `[VisibleStartLid,
  EndEntryLid]` with the tail grouped under the card; expanded mid-call shows
  folded entries inside the group; `IsExpandedByDefault` conversations render
  expanded with local override both ways; regular (non-live) conversations
  unchanged.
- **UI:** expand icon absent before first summary, present after; icon state
  flips with expansion.
- **VirtualList stability:** explicit manual passes on every transition —
  latch (block appears, nothing moves), tier-1 close (block disappears, hidden
  messages appear), tier-2 close (expanded swap-in), tier-3 close (collapse
  for everyone) — watching for jumps/re-centers at the viewport bottom.
- **Manual two-device call:** solo talk → second peer joins → verify the
  solo messages don't disappear for a third not-joined viewer; title by
  ~2 minutes; tail visually inside the block; expand/collapse mid-call; both
  stop → per-tier outcome with no gap and no orphaned tail.
