# Deferred Conversation Collapse — Design

2026-08-31

## Problem

A conversation summary block "swallows" (collapses) the chat entries it covers. Two paths do
this while the user is looking at those very entries:

1. **Live materialization.** During a live session, the anti-collapse logic keeps the
   session's entries visible to participants. When the session ends, the live block is
   materialized into a regular `Conversation`; its tier decides `IsExpandedByDefault`
   globally (tier 3 = collapsed), so the entries a participant just watched vanish into a
   collapsed block in place.
2. **Regular summarization.** `ConversationSplitFlow` creates a conversation over mature
   entries. If the user is sitting in the chat (scrolled up, reading), a new
   collapsed-by-default block swallows on-screen entries mid-read.

Collapsing is fine on a *later* visit — the user no longer sees those entries. The defect is
only the in-place collapse of content currently on screen.

## Decisions

- **Defer collapse until the user leaves the chat.** No per-user server state; on the next
  chat visit the conversation renders per its tier (typically collapsed).
- **General rule, not live-only.** Applies to any conversation that appears (or grows) over
  entries the user is currently seeing — both the live-materialization and split-flow paths.
- **Auto-overrides are cleared on chat switch.** Manual expand/collapse toggles keep their
  current behavior (session-lived `ConversationExpansionOverrides`); only the auto-added
  expansions die when the user leaves the chat.
- **Witnessed = actually on screen.** `ChatDataQuery.VisibleLidRange` (the VirtualList's
  currently-visible entry range, already flowing into every `GetChatItems` build) is the
  source; no loaded-window approximation, no new JS↔C# plumbing. An entry 1–2 viewports
  off-screen that gets summarized collapses immediately — correct, the user never saw it.

## Design

All changes live in `ChatUI` (`src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs` plus the
toggle handler), consistent with the cross-surface-state-in-a-UI-service rule. No server,
schema, or contract changes.

### State (per chat, per visit)

- `_witnessedLids` — accumulated lid ranges the user has actually seen rendered as plain
  entry rows this visit. Stored as a merged, ordered list of `Range<long>`.
- `_autoExpandedConversations` — set of `ConversationId` auto-expanded this visit.
- `_autoExpansionSuppressed` — set of `ConversationId` the user manually toggled this visit;
  the auto-expand rule never re-adds these.

All three are cleared when the selected chat changes (and on app restart, being in-memory).
VirtualList tile unload/reload within a visit must NOT clear them — same reasoning as the
existing `_knownConversationDefaultExpanded` cache.

### Witness capture

At the end of each non-prefetch `GetChatItems` build:

```
witnessed += VisibleLidRange ∩ (lids rendered as plain entry rows in this build)
```

The intersection matters: a collapsed block sitting inside the visible range covers lids
whose entries were never on screen; those must not count as witnessed. "Plain entry rows"
includes entries inside expanded conversation blocks and the live block's participant view.

### Auto-expand rule

Before resolving `expandedConversations` (the `showConversations` branch):

For each conversation in the loaded tiles that is

1. effective-collapsed (`defaultExpanded XOR overrides` says collapsed), and
2. not in `_autoExpansionSuppressed` and has no manual override entry, and
3. whose `EntryLidRange` intersects `_witnessedLids`

→ add its id to `_autoExpandedConversations`.

Resolved expansion becomes:

```
expandedConversations = (defaultExpanded XOR overrides) ∪ _autoExpandedConversations
```

Because the rule re-evaluates every build against the *current* `EntryLidRange`, range
growth re-triggers it — covering live resummary extension and `AppendReply` swallowing
freshly witnessed tail rows.

Ids in `_autoExpandedConversations` count as expanded for every consumer of
`ConversationViewState.ExpandedConversations` (fold/skip logic, hidden-live-tail lift,
grouping); no consumer changes needed since only the resolved set's computation changes.

### Manual toggle

The expand/collapse handler, before its current XOR-flip logic:

- add the id to `_autoExpansionSuppressed`;
- if the id is in `_autoExpandedConversations`, remove it — and if the conversation is
  default-collapsed with no override, that removal alone IS the collapse (no override flip,
  keeping XOR semantics intact); otherwise proceed with the normal flip.

Expanding manually works unchanged (the id just also lands in the suppressed set, which is
harmless — suppression only blocks *auto* re-adding).

### Live→regular swap

No special-casing. Participants saw the session's entries as plain rows → witnessed → the
materialized conversation intersects and auto-expands. Non-participants saw a collapsed live
block and a hidden tail → nothing witnessed → the materialized block collapses right away.

## Edge cases

- **Chat switch and back within one app session** → auto state cleared → collapses. By
  design (Q3 = A).
- **Prefetch builds** carry no `VisibleLidRange` semantics for rendering — witness capture
  and auto-expand evaluation are skipped on `isPrefetch`, mirroring how prefetch already
  skips the toggled-override one-shot.
- **Tier-2 conversations** (`IsExpandedByDefault = true`) are unaffected — they are not
  effective-collapsed, so the rule never fires.
- **Navigation-driven expansion** (navigate-to-entry flips an override) is untouched; it
  operates on the manual override set.
- **Memory** — all three collections are per-visit and bounded by what the user actually
  scrolled through; cleared on switch.
- **Scroll stability** — the rows→expanded-block swap keeps the entries rendered and adds
  only the block header row; no VirtualList position jump beyond one row height.

## Testing

Unit tests alongside the existing tiles/grouping tests (`GroupExpandedConversations`
pattern), driving `GetChatItems` state directly where practical:

1. Entries witnessed (visible range covers them) → conversation materializes collapsed →
   renders expanded.
2. Same conversation on a fresh visit (state cleared) → renders collapsed.
3. Manual collapse of an auto-expanded conversation → stays collapsed for the rest of the
   visit, including after its range grows.
4. Conversation over never-visible entries → collapses immediately.
5. Collapsed block inside the visible range does not mark its covered lids witnessed.
6. Range growth over freshly witnessed tail rows re-triggers auto-expand.

## Reuse

- **Existing abstractions:** `ChatDataQuery.VisibleLidRange`,
  `ConversationViewState.ExpandedConversations`, `ConversationExpansionOverrides`,
  `_knownConversationDefaultExpanded` lifetime pattern, `Range<long>`/`RangeExt` merging
  helpers (`src/dotnet/Core/Mathematics/RangeExt.Long.cs`). No fitting existing "witnessed
  ranges" abstraction was found.
- **New components:** none rise to shared-project level — the witnessed-range accumulator is
  a small private helper inside `ChatUI`; promote to `ActualChat.Core` only if a second
  consumer appears.
