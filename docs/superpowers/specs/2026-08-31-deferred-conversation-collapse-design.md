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
- `_suppressedAutoExpansions` — set of `ConversationId` the user manually toggled this visit;
  the auto-expand rule never re-adds these.

All three are cleared by `ClearAutoExpansionState`, keyed on the selected chat actually
*changing*: `SelectChatInternal` returns early when the id is unchanged, so a same-chat
detour (re-selecting the chat you are already in) does not clear — accepted, since nothing
about the visit changed either. They are also empty on app restart, being in-memory.
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

Coverage is enforced **explicitly**, against the effective-collapsed conversation ranges taken
from the same `chatRangeMetaList` the rule reads — not inferred from what ChatItems happens to
contain. Presence in the item list is not proof the user saw the row: a transitional build
served from last-known range meta and conversation tiles can emit boundary entries of a
collapsed conversation next to that conversation's card, and treating those as witnessed makes
the next build's rule re-expand a conversation the user just watched collapse. The capture
therefore skips any message whose lid falls inside a conversation range that is not in
`expandedConversations`.

Two details of that exclusion are load-bearing:

- `expandedConversations` at that point already includes the auto set, so entries inside an
  auto-expanded or manually expanded conversation keep being witnessed — which is what lets
  range growth re-trigger the rule.
- the live and materialized block ids are deliberately **excluded from the exclusion**: a
  participant's live entries must stay witnessable, or the conversation materialized out of
  that block would collapse under the rows they were just reading.

`ChatDataQuery.VisibleLidRange` is populated on only one of `GetChatDataQuery`'s four
branches (the has-query one), so on a quiet visit — no query, retained data — it arrives
empty and the capture would never run, leaving the feature inert. The capture therefore
falls back to `ChatUI._itemVisibility`, the same state `ChatView` derives `VisibleLidRange`
from, reading `MinMessageLid`..`MaxEntryLid + 1` for the current chat. It reads `.Value`
rather than `.Use()` on purpose: a Fusion dependency on `ItemVisibility` would rebuild the
whole chat on every scroll. The range is half-open at both ends.

Both the capture and the auto-set write are gated on a **visit epoch**.
`ClearAutoExpansionState` increments `_autoExpansionEpoch` before it wipes; every build
snapshots the epoch before its first await and refuses to witness or to write auto-expansions
if it has moved since. A `_selectedChatId` check alone is not enough: it matches again once
the user leaves and returns to *the same chat*, so a build still in flight from the previous
visit would write its covered lids into the freshly cleared witnessed set and the next build's
rule would re-expand the conversation the return was supposed to collapse. The chat-id check
is kept as the cheaper first-line filter. Both guards sit under the same `Lock` the clear runs
under, so the write-side check is exact.

### Auto-expand rule

Before resolving `expandedConversations` (the `showConversations` branch):

For each conversation in the loaded tiles that is

1. effective-collapsed (`defaultExpanded XOR overrides` says collapsed), and
2. not in `_suppressedAutoExpansions` and has no manual override entry, and
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

- add the id to `_suppressedAutoExpansions`;
- if the id is in `_autoExpandedConversations`, remove it and **return** — that removal alone
  IS the collapse, unconditionally. No override flip.

The unconditional return rests on this invariant: **every id in `_autoExpandedConversations`
is effective-collapsed by XOR**, so dropping it from the set lands on "collapsed" without
touching the override set. The invariant holds because no writer flips an id to
XOR-expanded while it sits in the auto set:

- the rule only ever adds ids that are effective-collapsed, and now also skips any id
  carrying a manual override entry (suppression dies with the visit, an override does not —
  without this skip, later range growth would undo an earlier visit's deliberate collapse);
- the navigation-expansion path checks the auto set before flipping an override, so it never
  stacks one on an already-auto-expanded conversation;
- the toggle returns before reaching its flip;
- `EnsureConversationCollapsed` removes from the auto set and forces the override to whatever
  makes XOR collapsed for the given default.

Expanding manually works unchanged (the id just also lands in the suppressed set, which is
harmless — suppression only blocks *auto* re-adding).

### Live→regular swap

The rule does not participate: it explicitly skips both `liveBlockId` and
`materializedBlockId`, so neither identity of a block in transition can be auto-expanded.
The freeze-overlay machinery in `LiveBlockUI` owns that transition end to end — during the
close window the overlay's `RenderId` *is* the live block's identity even though
`liveConversation` is already null, which is why the rule is handed the method's
`liveBlockId` local (overlay-aware) rather than `liveConversation?.Id`.

`TryCollapseOverlay` — the dismiss gesture — makes the collapse total across both
identities: `EnsureConversationCollapsed(MaterializedId, …)` suppresses, removes from the
auto set, and normalizes the override; when `LiveRenderId` differs (the `ContextStartLid > 0`
case) it additionally calls `SuppressAutoExpansion(LiveRenderId)`, which suppresses and
removes but deliberately does *not* touch the override set — a frozen render id has no
conversation behind it once materialized, so `defaultExpanded` never contains it and adding
an override would flip it to *expanded* rather than collapsed.

Once the block has fully materialized into an ordinary conversation, the ordinary rule
applies again: participants saw the session's entries as plain rows → witnessed → the
conversation intersects and auto-expands. Non-participants saw a collapsed live block and a
hidden tail → nothing witnessed → it collapses right away.

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
