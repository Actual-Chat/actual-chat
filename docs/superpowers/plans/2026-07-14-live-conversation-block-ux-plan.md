# Implementation Plan: Live Conversation Block UX — Joined Mode & Seamless Close

**Spec:** `docs/superpowers/specs/2026-07-13-live-conversation-block-ux-design.md` (approved 2026-07-14)

## Context

Live session conversation blocks frustrate users in joined mode and around lifecycle transitions: the expand icon renders with nothing to expand; joined viewers see live transcript entries as disconnected plain messages below the card; titles never appear during short calls (first-summary gate of 1200 mature words is unreachable); close is non-seamless (no-title sessions vanish and reappear minutes later via the offline split flow); the 2-peer latch swallows pre-latch messages viewers already saw; and below-threshold close makes collapsed messages pop back.

The approved spec introduces: a **no-hide rule** (entries rendered before the block appeared never fold while live; sanctioned exceptions: mid-call progressive folding of block-born entries, tier-3 collapse at close); **two start points** (`ContextStartLid` for summary context via split-flow pause heuristics; `VisibleStartLid` = chat end at latch, bounding all hiding/folding); **lowered first-summary gates** (150 mature words + 3 entries, 1-min maturity); **tiered explicit close** over `[ContextStartLid, end]` (<150w/3e → vanish; <1200w/10e → materialize expanded via new `Conversation.IsExpandedByDefault`; ≥1200w/10e → materialize collapsed); **joined rendering** as one group block (card + folded range + live tail); split-flow guard widened to `[ContextStartLid, ∞)`.

## Core design resolution — three lids, three roles

- **`StartEntryLid`** (existing): unchanged — raw session start, set at first stream.
- **`VisibleStartLid`** (new): set once at latch to the chat-end lid. **The live block's `ConversationId` keys on it** (`ConversationId.New(ChatId, EffectiveVisibleStartLid)`, falling back to `StartEntryLid` for old Redis states). This makes the existing client machinery produce exactly the spec'd behavior: card placed after pre-latch entries, fold range `[V, EndEntryLid]`, not-joined hidden = `[V, ∞)` combined with the existing `hiddenLiveTailRange`. Id is stable for the block's whole visible lifetime — no mid-call re-keying.
- **`ContextStartLid`** (new, server-only): computed once by `LiveConversationSummaryFlow` post-latch (backward pause scan); feeds the summarizer, the split-flow guard, and the **materialized** conversation id/range at close.

Rejected: shifting the live id to `ContextStartLid` while live — it would move the card above visible pre-latch entries, fold them (violating no-hide), and re-key `@key`s mid-call.

**Finalize sequencing** (Streaming.Service has no `FlowHub`): the flow owns the final pass, the backend owns the close. `CloseNow` for a latched transcription session marks `IsClosing` instead of closing instantly; the flow (throttle lowered 30s→15s) detects `IsClosing`, runs the final pass (`matureOnly: false`, full `[ContextStartLid, end]`), decides the tier, writes the summary + `IsExpandedByDefault`, then calls new `ILiveSessionsBackend.FinalizeSession` → `CloseWithFinal` (materialize-then-close, no gap). LLM failure → short flow retry; the existing 90s `SelfClose` backstop still closes with whatever summary exists.

## Phase 1 — API models & settings

- `src/dotnet/Api/Chat/Conversation.cs`: add `bool IsExpandedByDefault` — order **13** on `Conversation`, order **10** on `ConversationDiff` **plus** its copy-ctor line (triple-attribute pattern: `[DataMember] + [MemoryPackOrder(N)] + [Key(N)]`).
- `src/dotnet/Api/Live/LiveSessionState.cs`: add orders **19** `long VisibleStartLid`, **20** `long ContextStartLid`, **21** `bool IsExpandedByDefault`. Add ignored helpers `EffectiveVisibleStartLid => VisibleStartLid > 0 ? VisibleStartLid : StartEntryLid` and `VisibleEntryLidRange`. Re-key `ConversationId` on `EffectiveVisibleStartLid`. `ToConversation()`: clamp `EndEntryLid = Math.Max(EndEntryLid, EffectiveVisibleStartLid)` (keeps the block passing `GetTile`'s non-empty-intersect filter pre-first-summary), forward the flag. New `ToMaterializedConversation()`: id = `ConversationId.New(ChatId, ContextStartLid > 0 ? ContextStartLid : EffectiveVisibleStartLid)`, unclamped `EndEntryLid`, flag forwarded.
- `src/dotnet/Api/Live/LiveSessionSummary.cs`: add order 6 `bool IsExpandedByDefault`.
- `src/dotnet/Chat.Service/Module/ChatSettings.cs` (Summarization): `MinLiveConversationWords = 150`, `MinLiveConversationEntries = 3`, `FirstLiveSummaryDelay = 1 min`.

## Phase 2 — DB + migration

- `src/dotnet/Chat.Service/Db/DbConversation.cs`: add `bool IsExpandedByDefault`; map in **both** `ToModel()` and `UpdateFrom()`.
- Migration: `./ef-migrations.cmd Chat.Service add Add_Conversation_IsExpandedByDefault` → expect `AddColumn<bool>(name: "is_expanded_by_default", table: "conversations", type: "boolean", nullable: false, defaultValue: false)` (pattern: `20260630125740_Add_SharedLocation_Version.cs`).

## Phase 3 — LiveSessionsBackend (Streaming.Service)

`src/dotnet/Streaming.Contracts/ILiveSessionsBackend.cs` + `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`:

- Latch block in `OnStreamRegistered` (~L256): set `VisibleStartLid = ChatsBackend.GetLidRange(...).End` in the same `with`. Same for the `StartCall` latch path.
- `UpdateSummary`: copy `IsExpandedByDefault` from the summary.
- New `SetContextStart(chatId, lid, ct)`: lock + isolation, idempotent (no-op if already set), write + `InvalidateState`.
- New `FinalizeSession(chatId, ct)`: skip if state null or a participant rejoined (`!IsClosing && HasParticipant`); else `CloseWithFinal`.
- `CloseNow` (~L709): for `{ TranscriptionOn: true, SessionStartedAt: not null, Kind: not Call }` → `StartClosingGrace` instead of instant `CloseWithFinal` (flow finalizes in ~15–25s; `SelfClose` at 90s stays the backstop). Other shapes unchanged.
- `CloseWithFinal` (~L754): materialize `ToMaterializedConversation()` instead of `ToConversation()`; empty-title vanish and FINAL notification unchanged.

## Phase 4 — ConversationsBackend overlay

`src/dotnet/Chat.Service/ConversationsBackend.cs`:
- `GetRangeMeta` (L86-99): use `lc.VisibleEntryLidRange` for the overlay/overlap math — pre-latch persisted conversations stay visible (server half of defect 5).
- `GetTile` (L129-139): no change — already uses `ToConversation().EntryLidRange`, now visible-based.

## Phase 5 — Flows

**New `src/dotnet/Chat.ML/ContextStartScanner.cs`** — pure static `FindContextStartLid(precedingEntries, anchorEntry)`: backward walk applying the extractor boundary rules (`minPause = IsTranscript ? Constants.Audio.MaxStreamDuration : MinPauseBetweenTextEntries (5 min)`, 10× running average pause, 12h hard cap). Promote `EntryGroupExtractor.MinPauseBetweenTextEntries`/`MaxPauseBetweenEntries` to `public const`; pause math mirrors `EntryGroupBuilder.GetPauseBetween`.

**`src/dotnet/Chat.Service/Flows/LiveConversationSummaryFlow.cs`**:
1. `Throttle` 30s → 15s; `[Flow(ResumeTimeout = 60, DelayQuanta = 15)]`.
2. `Resume`: `live is null` → return; `live.IsClosing` → `Finalize(live, ct)` for latched transcription sessions (replaces the early return); otherwise the regular pass now reads from `EnsureContextStart(live, ct)` and, when `neverSummarized`, gates on `MinLiveConversationEntries`/`MinLiveConversationWords` with `FirstLiveSummaryDelay` maturity; re-summaries keep existing gates/cadence.
3. `EnsureContextStart`: return `live.ContextStartLid` if set; else fetch preceding entries tile-by-tile backward from `StartEntryLid` (budget ~200 entries, bounded by `GetLidRange().Start`), run `ContextStartScanner`, **clamp to `PreviousConversationLidRange.End + 1`** (via `ConversationsBackend.GetRangeMeta`) so a persisted conversation's range is never re-claimed, then `LiveSessionsBackend.SetContextStart`.
4. `Finalize`: full-transcript entries from context start (`matureOnly: false`); **tier 1** (below 150w/3e) → `FinalizeSession` (title empty → vanish); **tier 2/3** → `Summarize` (on failure `StageResumeIn(5s)` retry inside the backstop window); `IsExpandedByDefault = words < MinConversationWords || entries.Count < MinConversationEntries` (tier 2 true, tier 3 false); `UpdateSummary` then `FinalizeSession`.

**`src/dotnet/Chat.Service/Flows/ConversationSplitFlow.cs`**: widen the live guard in both places (L73-78 `Process`, L235-236 `GetEntries`) to `lc.ContextStartLid > 0 ? lc.ContextStartLid : lc.StartEntryLid`. `IsAlreadyCovered` already prevents post-close re-summarization.

## Phase 6 — Client tile builder

**`src/dotnet/UI.Blazor.App/Services/ChatUI.cs`**:
- `_expandedConversations` → `_conversationExpansionOverrides` (in-memory as today); membership = *opposite of* `IsExpandedByDefault`. `ToggleExpandConversation` keeps its signature (call sites unchanged).
- Helper `IsConversationExpanded(conversation)` = `IsExpandedByDefault ^ overrides.Contains(Id)`.

**`src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs`**:
1. `ConversationViewState` += `ConversationId JoinedLiveConversationId` and `Range<long> LiveFoldRange` (value-equal — compute-cache friendly; no `Conversation` object).
2. `GetChatItemsInternal`:
   - Effective expansion set = `(defaultExpanded ∖ overrides) ∪ (overrides ∖ defaultExpanded)`, resolving `IsExpandedByDefault` from `Conversations.GetTile` results; navigation auto-expand (L88-101) becomes "ensure effective-expanded". When not joined, drop `liveConversation.Id` from the set (stale joined-era expand must not leak hidden entries).
   - `LiveFoldRange` from raw live state (`LiveSessionUI.GetState`): `raw.EndEntryLid >= raw.EffectiveVisibleStartLid ? [V, raw.EndEntryLid] : empty` — **the joined skip range comes from here, not `EntryLidRange`**, so nothing folds before the first summary lands (pre-summary `ToConversation` clamps `EndEntryLid` to V, which must not hide entry V for joined viewers).
   - `hiddenLiveTailRange` formula unchanged (not-joined combined hidden = `[V, ∞)`).
3. `GetTile`:
   - Live card always emitted, even when expanded: include the live conversation in the `entries.Merge(...)` right side regardless of expansion, and **handle the paired-tuple case** — `Merge` is a full outer join, so when a loaded entry shares the card's key lid (`V`), the tuple arrives as `(entry, conversation)` and the card must be emitted *before* processing the entry (today's code only emits cards from `(null, conversation)` tuples).
   - For `joinedLiveId`: skip range = `LiveFoldRange` (not `EntryLidRange`); suppress the regular `ConversationHeader`/`ConversationFooter` (the card is the header); entries still get `Conversation` set for grouping; force `BlockStart` on the first entry at/after `V` so a pre-latch author group can't swallow tail entries.
4. `GroupExpandedConversations(messages, joinedLive)`: a `ConversationMessage` with `Conversation.Id == joinedLive.Id` **starts** a block (today `ConversationMessage` never does, L807); `belongs` for the live block uses grouping range `[V, ∞)` (`item.Id >= V` for `Conversation == null` items — the un-summarized tail and the trailing `AudioRecordingMessage` land inside). Non-live blocks keep `EntryLidRange.Contains`. Invoke when `expandedConversations.Count > 0 || joinedLive != null`. Result: one `ExpandedConversationMessage` keyed `ConversationBlock:{V}`, stable all call.

## Phase 7 — UI & CSS

- `src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs`: add `[ComputeMethod] GetState(chatId)` passthrough (raw `LiveSessionState` for fold-range/icon computations).
- `ConversationLiveState.cs`: += `bool HasFoldedEntries`, `bool IsExpanded`.
- `ConversationMessageView.razor`: expand button `.c-lc-expand` gated on `HasFoldedEntries` (= `raw.EndEntryLid >= raw.EffectiveVisibleStartLid && !isVoiceOnly` — only `UpdateSummary` advances `EndEntryLid`) instead of `hasSummary = !IsVoiceOnly`; icon from `IsExpanded` (`ChatUI.IsConversationExpanded`), not `_isOpen`. Pre-first-summary card: no icon; title fallback, meta row, "You on the call"/"Tap to join" only.
- `conversation.css`: `live joined` group variant scoped via `:has()` on the VirtualList group wrapper (pattern: `virtual-list.css` L141-145): continuous background/rounded border owning card + tail; card flush as first row.

## Phase 8 — Tests

- **`tests/Chat.UnitTests/ContextStartScannerTest.cs`** (new): text 5-min boundary, transcript `MaxStreamDuration` boundary, 10× average pause, 12h cap, empty-preceding → anchor, budget stop.
- **`tests/Chat.UnitTests`**: extend `Conversation` serialization round-trip with `IsExpandedByDefault = true` (SerializationCodeGen/ChatModelSerialization tests).
- **`tests/Chat.IntegrationTests/LiveSessionsTest.cs`** (existing pattern — drive `ILiveSessionsBackend`): `LatchSetsVisibleStartLidToChatEnd`; `RangeMetaKeepsPreLatchConversationsVisible`; `CloseNowKeepsLatchedTranscriptionSessionClosing` (adjust existing instant-close facts for this shape only); `FinalizeSessionMaterializesContextRange` (materialized at `ContextStartLid` with flag; live state dropped).
- **`tests/Chat.IntegrationTests/LiveConversationFinalizeFlowTest.cs`** (new; summarizer stub per `ConversationSummarizationTest`): first-summary gate boundaries (150w/3e/1-min); re-summary cadence unchanged; context-start scan; close tiers 1/2/3 outcomes.
- **`tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs`** (new; pattern `SendingMessagesDisplayTest.cs`): `NotJoinedHidesOnlyFromVisibleStart`; `JoinedGroupsCardAndTailIntoOneBlock`; `JoinedFoldsSummarizedRangeAndTogglesMidCall`; `ExpandedByDefaultConversationRendersExpanded` + local overrides both directions; `RegularConversationsUnchanged`.

## Phase 9 — Verification

1. `dotnet build ActualChat.CI.slnf` after phases 1–5 and again after 6–7 (or trigger the `run-watch` rebuild if the watch loop is running — check `tmp/watch-dotnet.log`).
2. Generate + inspect the migration; rebuild.
3. `npm run build:Verify` (CSS touched).
4. `dotnet test` on `Chat.UnitTests`, `Chat.IntegrationTests`, `Chat.UI.Blazor.IntegrationTests`.
5. Manual two-device pass: solo talk ≥1 min → second device joins → a third not-joined viewer keeps solo messages in place at latch; title by ~2 min; tail inside the block for joined; expand/collapse mid-call flips icon and reveals/hides the folded range; both stop → tier outcomes (short vanishes to plain messages; mid-size swaps in expanded; long collapses for everyone) with no gap and no orphaned tail. Watch the viewport at every transition; use `/virtual-list-debug` if anything jumps.

## Risks & mitigations

- **VirtualList key stability**: latch inserts the card (nothing removed); mid-call id fixed at `V`; close materializes *before* dropping Redis state (single invalidation wave, no empty frame). Key changes only at close when `ContextStartLid != V` (tier 2/3 with pre-latch context) — a sanctioned visual transition, covered by the manual pass.
- **Id shift at close**: local expansion overrides keyed on `V` stop matching — harmless (tier 3 wants collapsed default; tier 2 renders expanded via the persisted flag).
- **Split-flow races**: guard widens once `ContextStartLid` lands; scanner clamps to `PreviousConversationLidRange.End + 1`; `OnChange` overlap-delete remains the last-resort self-heal.
- **Pre-first-summary fold**: joined skip range comes from `LiveFoldRange` (empty until a summary passes `V`), never from the clamped `EntryLidRange`.
- **Merge pairing**: card emission handles the `(entry, conversation)` equal-key tuple explicitly.
- **Old Redis states** (`VisibleStartLid == 0`): `EffectiveVisibleStartLid` falls back to `StartEntryLid` — current behavior.
- **`ApplyDiff` validation** (requires non-empty Title/Description/Summary, MessageCount > 0): tiers 2/3 always materialize after a successful summarize, so the invariant holds; tier 1 never materializes.
