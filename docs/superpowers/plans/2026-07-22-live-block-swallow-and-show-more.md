# Live Block Swallow-Above-Viewport + Show-More Implementation Plan (Plan 2 of 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the collapsed joined live block swallow everything that scrolls above the viewport (summarised or not) into a compact card, and give the reader an in-place "Show N earlier messages of M" pill to pull back context a batch at a time without expanding to the first message.

**Architecture:** Two client-side phases on the existing fold governor (`LiveBlockUI` / `LiveFoldMath`), no server/DB/contract changes. **Phase A (§4)** replaces the summary-driven fold rule with a pure viewport-top tracker: the fold boundary is the monotonic max of the topmost visible lid in the block, so un-summarised rows above the viewport fold too. **Phase B (§7)** adds a reader-controlled `RevealedBoundaryLid` that pins the *effective* fold below the governor's monotonic boundary, a `GetSwallowedCount` for the true "of M", and the straddling pill view. Expansion already suppresses the fold at the tile-builder level (`ChatUI.Tiles.cs:467-472` filters an expanded block out of the excluded ranges), and the monotonic boundary never retreats while expanded (expand jumps to the first message, viewport-top = V), so no jump on collapse and no governor expansion-awareness is needed.

**Tech Stack:** C# (`LiveFoldMath`, `LiveBlockUI` — `IComputeService` / `UIWorkerBase` / `MutableState`), Blazor Server (`.razor`, `ConversationMessageView`, `ConversationLiveState`), Tailwind-compiled CSS (`conversation.css`), xUnit (`LiveFoldMathTest` in `Chat.UI.Blazor.UnitTests`; `LiveConversationDisplayTest` in `Chat.UI.Blazor.IntegrationTests`).

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing C#/Razor/CSS. No `Async` suffix on async methods; no XML docs on members; comments only for non-obvious constraints; follow the surrounding brace/format style; ≤120 columns.
- Build with `dotnet build ActualChat.CI.slnf`. CSS/TS-touching changes also run `npm run build:Verify` (or the `/server-loop` rebuild if the watch loop is running — check `tmp/watch-dotnet.log`).
- A user-owned `dotnet watch` may own the server on port 7080 — do **not** `/server-start`/`/server-restart`. The user restarts it.
- Branch `feat/live-block-swallow-show-more` off `dev`. Do not push unless asked. (Plan 1's `feat/live-block-unified-ux` is a separate, already-open PR — do not build on top of it unless it has merged; if it has, branch off `dev` after the merge so the unified shell is present.)
- Reuse before adding (spec Reuse section): extend `LiveBlockUI`/`LiveFoldMath` — do **not** fork a new folding path; reuse the `.show-more-btn` colour token (`--cr-item-badge-selected-text`) and the Call/Map switch rounded-pill visual for the pill; reuse `ItemVisibility.VisibleMessageLids` for the viewport measure; reuse `Chats.ReadReverse` for entry counts.
- **Batch size (user decision):** each reveal batch N = the current viewport message count **rounded up to the nearest 5**, floored at 5: `N = Math.Max(5, ((visibleCount + 4) / 5) * 5)`. The final click reveals only the remainder (`Math.Min(N, M)`).
- **Reveal lifecycle (spec §7):** a reveal persists until the block is **expanded or re-collapsed** (re-latched). Reset is triggered imperatively from `ChatUI.ToggleExpandConversation`. It also resets when the block rebuilds (chat state recreated) or the session closes.
- Spec: `docs/superpowers/specs/2026-07-22-live-conversation-block-unified-ux-design.md` (§4 swallow, §7 show-more pill). The "of M" copy reads the **true swallowed count**, never the summary's `MessageCount`.
- Design tokens (light theme): `--violet-60 #A533FF`, `--cr-item-badge-selected-text #582EFF`, `--background-01`. Pill visual reference: Call/Map switch container — white bg, 1px border, soft shadow, ~30px tall, tight padding.

---

## File Structure

- `src/dotnet/UI.Blazor.App/Services/LiveFoldMath.cs` — the pure fold-advance rule. Phase A rewrites it from the summary-lag `PendingFold` model to viewport-top tracking. **Primary Phase A surface.**
- `src/dotnet/UI.Blazor.App/Services/LiveBlockUI.cs` — the governor. Phase A rewires `ProcessChat` to the new rule and drops the dead `Pending`/`FoldLag`/`LastObservedFoldEndLid` machinery. Phase B adds `RevealedBoundaryLid` to `LiveBlockState`, `RevealMore`/`ResetReveal`, and `GetSwallowedCount`.
- `src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs` — Phase B threads the effective boundary (`min(FoldBoundaryLid, RevealedBoundaryLid)`) into `liveFoldRange` (~L80-85).
- `src/dotnet/UI.Blazor.App/Services/ChatUI.cs` — Phase B calls `LiveBlockUI.ResetReveal` from `ToggleExpandConversation` (~L357-372).
- `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationLiveState.cs` — Phase B adds `int SwallowedCount` + `int RevealBatch` for the pill label.
- `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageView.razor` — Phase B renders the pill and wires its click to `LiveBlockUI.RevealMore`.
- `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/conversation.css` — Phase B pill styling (`.c-lc-showmore`).
- Tests: `tests/Chat.UI.Blazor.UnitTests/LiveFoldMathTest.cs` (rewrite), `tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs` (extend).

Testing note: the fold *rule* and the reveal *state math* are unit/integration-testable at the `ChatItems` / `LiveFoldMath` model level. The **no-jump invariant** and the **pill visual** are verified manually via chrome-devtools MCP + `/virtual-list-debug` against the running app (a live block with a joined reader). The harness asserts the model, not rendered pixels or scroll offsets.

---

# Phase A — Swallow above the viewport (§4)

Phase A is independently shippable: after it, a collapsed joined block folds everything above the viewport into the card (no reveal yet). Verify the no-jump invariant before starting Phase B.

## Task 1: Rewrite `LiveFoldMath` as a viewport-top tracker

Today `LiveFoldMath.Advance` computes `max(boundary, min(ripeSummaryEnd, viewportTop))` — the boundary can never cross the summarised range. §4 makes the boundary track the viewport top directly, so un-summarised rows above the viewport fold too. The summary-lag `PendingFold` model is removed.

**Files:**
- Rewrite: `src/dotnet/UI.Blazor.App/Services/LiveFoldMath.cs`
- Rewrite: `tests/Chat.UI.Blazor.UnitTests/LiveFoldMathTest.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `long LiveFoldMath.Advance(long boundaryLid, long? minVisibleLidInBlock)` — returns the new monotonic boundary. `PendingFold`, the `Result` record, `foldLag`, `serverNow`, and `pending` parameters are gone.

- [ ] **Step 1: Rewrite the test first** in `LiveFoldMathTest.cs`. The new rule is small: advance to the viewport top, monotonic, and never below the current boundary; a null viewport (nothing visible in the block) holds the boundary.

```csharp
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public class LiveFoldMathTest
{
    [Fact]
    public void AdvancesToViewportTop()
        => LiveFoldMath.Advance(10, 15).Should().Be(15);

    [Fact]
    public void FoldsUnsummarizedRowsAboveViewport()
        // No summary gate: a viewport top well above any summarised range still advances the boundary.
        => LiveFoldMath.Advance(10, 500).Should().Be(500);

    [Fact]
    public void IsMonotonic_ViewportBelowBoundaryDoesNotRetreat()
        // Reader scrolled up (viewport top below the boundary) must not move the boundary back.
        => LiveFoldMath.Advance(20, 5).Should().Be(20);

    [Fact]
    public void NullViewportHoldsBoundary()
        // Nothing visible in the block (e.g. not-joined, or block off-screen): boundary is unchanged.
        => LiveFoldMath.Advance(20, null).Should().Be(20);

    [Fact]
    public void EqualViewportIsStable()
        => LiveFoldMath.Advance(20, 20).Should().Be(20);
}
```

- [ ] **Step 2: Run the test — expect a compile error** (the new signature does not exist yet):

`dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj -c Debug --filter "FullyQualifiedName~LiveFoldMathTest"`
Expected: build FAILS (old `Advance` signature / `PendingFold` referenced). This is the RED state.

- [ ] **Step 3: Rewrite `LiveFoldMath.cs`** to the viewport tracker. Delete `PendingFold` and `Result`.

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public static class LiveFoldMath
{
    // The collapsed live block swallows everything above the viewport. The fold boundary is the
    // topmost visible lid in the block, advanced monotonically - it never retreats when the reader
    // scrolls up, so the block stays a compact card. A null viewport (nothing of the block visible)
    // holds the current boundary. Summaries no longer gate this: un-summarised rows fold too.
    public static long Advance(long boundaryLid, long? minVisibleLidInBlock)
        => minVisibleLidInBlock is { } lid ? Math.Max(boundaryLid, lid) : boundaryLid;
}
```

- [ ] **Step 4: Build the test project's dependency** and re-run the test. `LiveBlockUI` still references the old API, so build the app project first to confirm the compile break is localized, then Task 2 fixes it. For now, run only the unit test after Task 2 compiles. Skip to Task 2 (the two must land together in build terms); return here to confirm:

`dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj -c Debug --filter "FullyQualifiedName~LiveFoldMathTest"`
Expected (after Task 2 compiles the app): PASS (5/5).

- [ ] **Step 5: Commit** together with Task 2 (they share a build). See Task 2 Step 6.

---

## Task 2: Rewire the governor to the viewport-tracking rule

Replace the `Pending`/`FoldLag` advance in `ProcessChat` with a direct call to the new `LiveFoldMath.Advance`, and remove the now-dead summary-fold plumbing (`Pending`, `LastObservedFoldEndLid`, `FoldLag`, the `PendingFold` construction). The boundary now advances purely from the viewport top. `GetBlockState` → `LiveBlockState.FoldBoundaryLid` → `ChatUI.Tiles.liveFoldRange` is unchanged downstream.

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/LiveBlockUI.cs`
- Test: `tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs`

**Interfaces:**
- Consumes: `LiveFoldMath.Advance(long, long?)` (Task 1); `visibility.VisibleMessageLids`; `raw.EffectiveVisibleStartLid`.
- Produces: `LiveBlockState.FoldBoundaryLid` now = monotonic max of the block's viewport-top lid.

- [ ] **Step 1: Replace the advance block** in `ProcessChat` (`LiveBlockUI.cs:266-283`). Keep the `minVisibleLid` computation; drop the `Pending`/`foldEndLid`/`LiveFoldMath` lag call:

```csharp
            if (raw is { SessionStartedAt: not null }) {
                var v = raw.EffectiveVisibleStartLid;
                var minVisibleLid = visibility.ChatId == chatId && !visibility.IsEmpty
                    ? visibility.VisibleMessageLids.Where(lid => lid >= v).DefaultIfEmpty(long.MaxValue).Min()
                    : long.MaxValue;
                var boundaryLid = LiveFoldMath.Advance(
                    state.FoldBoundaryLid, minVisibleLid == long.MaxValue ? null : minVisibleLid);
                state = new LiveBlockState(boundaryLid, null, chatState.WasAttending);
            }
```

- [ ] **Step 2: Remove the dead summary-fold machinery.** In `LiveBlockUI.cs`:
  - Delete `internal TimeSpan FoldLag = TimeSpan.FromMinutes(3);` (line 35) and its comment.
  - In `ChatFoldState` (nested class ~L309-319) delete `public IReadOnlyList<PendingFold> Pending = [];` and `public long LastObservedFoldEndLid;`.
  - Delete the seeding of `LastObservedFoldEndLid` in `GetOrCreateChatState` (~L169) — replace the initializer with just the fields that remain.
  - Keep `GetRawFoldEndLid` **only if** still used to seed the initial boundary (see Step 3). The `Pending`/`wakeAt` timer path is gone; `wakeAt` for the fold advance is no longer produced (the tier-1 dissolve still uses `wakeAt`, so keep that branch).

- [ ] **Step 3: Decide the initial boundary seed.** The chat state is created with `FoldBoundaryLid = foldEndLid` (from `GetRawFoldEndLid`). With viewport tracking, the first governor tick advances it to the real viewport top. Seeding from the summary end is a reasonable pre-visibility guess that avoids a one-frame "nothing folded" flash when opening an already-long session. **Keep** the `GetRawFoldEndLid` seed in `GetOrCreateChatState` (it is a lower bound; the monotonic advance corrects it upward on the first tick with visibility). Verify `GetRawFoldEndLid` is still referenced only there; if the build warns it is unused, it means the seed was removed — restore the seed rather than deleting the method.

- [ ] **Step 4: Confirm `wakeAt` handling.** After removing the fold timer, the only remaining `wakeAt` producer is the tier-1 dissolve window (`chatState.DissolveEndsAt`). Ensure `ProcessChat` still returns that `wakeAt` and the `raw is { SessionStartedAt: not null }` branch no longer overwrites it with a fold timer. Re-read the method top-to-bottom to confirm the dissolve `wakeAt` is not clobbered by the fold branch (the fold branch previously set `wakeAt = result.NextWakeAt`; that assignment is now gone).

- [ ] **Step 5: Write the integration test** — a joined reader whose viewport top is above an **un-summarised** range must fold that range (today it would not, because no summary covers it). Add to `LiveConversationDisplayTest.cs` (pattern: existing joined tests that drive `ILiveSessionsBackend` + `ChatUI.SetItemVisibility`/`ItemVisibility`).

```csharp
[Fact]
public async Task CollapsedBlockFoldsUnsummarizedRowsAboveViewport()
{
    await Tester.SignInAsUniqueBob();
    var (chat, _) = await Tester.CreateAndGetChat(false, "swallow-above-viewport-test");
    var author = await Tester.GetOwnAuthor(chat.Id).Require();
    var peerId = AuthorId.New(chat.Id, 777_400);
    var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
    await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
    await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
    var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
    var v = live!.EffectiveVisibleStartLid;
    // A title so the block has a card, but the summary covers only V (EndEntryLid = v) - the entries
    // below are UN-summarised. Viewport tracking must still fold them once they scroll above the top.
    await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
        Title = "Recap", Description = "d", Summary = "s", EndEntryLid = v, MessageCount = 1,
    }, CancellationToken.None);
    var lids = new List<long>();
    for (var i = 0; i < 8; i++)
        lids.Add((await Tester.CreateTextEntry(chat.Id, $"m-{i}")).LocalId);

    var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
    var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
    await chatAudioUI.SetRecordingChatId(chat.Id);   // Bob is a recorder => joined
    chatUI.SelectChatOnNavigation(chat.Id);

    // Viewport top sits at the 6th entry: everything above it (incl. un-summarised rows) must fold.
    var viewportTop = lids[5];
    chatUI.SetItemVisibility(new VirtualListItemVisibility(
        chat.Id, [new ChatMessageKey(chat.Id, viewportTop)], false));

    var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
    var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
    await ComputedTest.When(async ct => {
        var blockState = await AppHost.Services.GetRequiredService<LiveBlockUI>()
            .GetBlockState(chat.Id, ct);
        blockState.FoldBoundaryLid.Should().BeGreaterThanOrEqualTo(viewportTop,
            "the boundary tracks the viewport top, folding un-summarised rows above it");
    }, TimeSpan.FromSeconds(15));
}
```

> Note: adapt the visibility call to the real `ChatUI` API — inspect how existing tests set visibility (`SetItemVisibility` / assigning `ItemVisibility`) and the exact `VirtualListItemVisibility` / `ChatMessageKey` constructors before finalizing. If no test-side visibility setter exists, drive it through the same `MutableState<ChatViewItemVisibility>` the governor reads (`ChatUI._itemVisibility` via a test accessor) — mirror the closest existing pattern rather than inventing one.

- [ ] **Step 6: Build, test, commit** (Tasks 1 + 2 together).

```bash
dotnet build ActualChat.CI.slnf   # 0 errors
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj -c Debug --filter "FullyQualifiedName~LiveFoldMathTest"
dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~LiveConversationDisplayTest"
```
Expected: unit 5/5 PASS; `LiveConversationDisplayTest` all PASS (fix any existing fold-range assertion that assumed the summary-lag rule — with §4 the boundary can now sit at the viewport top rather than the summary end; update those expectations to the viewport-tracking rule, and remove any test that asserted `FoldLag` timing).

```bash
git add src/dotnet/UI.Blazor.App/Services/LiveFoldMath.cs \
  src/dotnet/UI.Blazor.App/Services/LiveBlockUI.cs \
  tests/Chat.UI.Blazor.UnitTests/LiveFoldMathTest.cs \
  tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs
git commit -m "feat(live): collapsed block swallows everything above the viewport (§4)"
```

- [ ] **Step 7: Manual no-jump verification (chrome-devtools + `/virtual-list-debug`).** With the running app and a live block joined as a recorder: scroll so older lines rise above the viewport and confirm they fold into the card **without the viewport jumping**, the card stays compact, and the boundary never retreats when you scroll back up (the folded rows re-render below, no snap). This is the core §4 risk — if any jump appears, capture it with `/virtual-list-debug` before proceeding to Phase B. Phase A is shippable on its own once this passes.

---

# Phase B — "Show N earlier messages of M" pill (§7)

Built on Phase A's viewport-tracking boundary. A reader-controlled `RevealedBoundaryLid` pins the *effective* fold below the governor's monotonic boundary so revealed messages stay revealed; a straddling pill reveals a batch per click.

## Task 3: Reveal state — `RevealedBoundaryLid`, `RevealMore`, `ResetReveal`

Add the reveal offset to the governor and thread the effective boundary into the tile builder. The governor's monotonic `FoldBoundaryLid` keeps advancing; the *rendered* fold uses `min(FoldBoundaryLid, RevealedBoundaryLid)`, so revealed rows survive new tail arrivals.

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/LiveBlockUI.cs` (`LiveBlockState`, `ChatFoldState`, new `RevealMore`/`ResetReveal`, `GetBlockState`)
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs` (effective boundary in `liveFoldRange`)
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatUI.cs` (`ToggleExpandConversation` → `ResetReveal`)
- Test: `tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs`

**Interfaces:**
- Consumes: `Chats.ReadReverse(Session, chatId, ct)`; `Hub.ChatUI.ItemVisibility`.
- Produces:
  - `LiveBlockState` gains `long RevealedBoundaryLid = long.MaxValue` (no reveal ⇒ effective = `FoldBoundaryLid`).
  - `Task LiveBlockUI.RevealMore(ChatId chatId, CancellationToken ct = default)` — retreats `RevealedBoundaryLid` by one batch.
  - `void LiveBlockUI.ResetReveal(ChatId chatId)` — clears the reveal (called on expand/collapse).

- [ ] **Step 1: Add `RevealedBoundaryLid` to `LiveBlockState`** (`LiveBlockUI.cs:21-25`):

```csharp
public sealed record LiveBlockState(
    long FoldBoundaryLid,
    LiveBlockOverlay? Overlay,
    bool WasAttending = false,
    bool IsDissolving = false,
    long RevealedBoundaryLid = long.MaxValue)
{
    public static readonly LiveBlockState None = new(0, null);
}
```

- [ ] **Step 2: Store the reveal on `ChatFoldState`** and surface it from `GetBlockState`. Add to the nested `ChatFoldState`:

```csharp
        public long RevealedBoundaryLid = long.MaxValue;
```

In `GetBlockState`, carry it onto the returned state (inside the existing `lock (Lock)` return):

```csharp
        lock (Lock)
            return baseState with {
                Overlay = DeriveOverlay(chatState, baseState.FoldBoundaryLid, raw, amInLive),
                RevealedBoundaryLid = chatState.RevealedBoundaryLid,
            };
```

- [ ] **Step 3: Implement `RevealMore` and `ResetReveal`.** `RevealMore` computes the batch from the current viewport count (rounded up to 5, floor 5), reads that many entries backward from the current effective boundary within `[V, effectiveBoundary)`, and sets `RevealedBoundaryLid` to the batch-th entry's lid (or `V` if fewer remain). Add to `LiveBlockUI`:

```csharp
    public async Task RevealMore(ChatId chatId, CancellationToken cancellationToken = default)
    {
        ChatFoldState chatState;
        long v, effectiveBoundary;
        lock (Lock) {
            if (!_chatStates.TryGetValue(chatId, out var s) || s.Template is not { } t)
                return;
            chatState = s;
            v = t.V;
            effectiveBoundary = Math.Min(chatState.State.Value.FoldBoundaryLid, chatState.RevealedBoundaryLid);
        }
        if (effectiveBoundary <= v)
            return;

        var visibleCount = Hub.ChatUI.ItemVisibility.Value.VisibleMessageLids.Count;
        var batch = Math.Max(5, ((visibleCount + 4) / 5) * 5);

        // Walk back `batch` real messages from just below the current effective boundary; the batch-th
        // one becomes the new revealed boundary (clamped to V when fewer remain).
        var revealed = v;
        using (Computed.BeginIsolation()) {
            var taken = 0;
            await foreach (var entry in Chats.ReadReverse(Session, chatId, cancellationToken).ConfigureAwait(false)) {
                if (entry.LocalId >= effectiveBoundary || entry.IsSystemEntry)
                    continue;
                if (entry.LocalId < v)
                    break;
                revealed = entry.LocalId;
                if (++taken >= batch)
                    break;
            }
        }

        lock (Lock)
            chatState.RevealedBoundaryLid = Math.Min(chatState.RevealedBoundaryLid, revealed);
        using (Invalidation.Begin())
            _ = GetBlockState(chatId, default);
    }

    public void ResetReveal(ChatId chatId)
    {
        lock (Lock) {
            if (!_chatStates.TryGetValue(chatId, out var chatState) || chatState.RevealedBoundaryLid == long.MaxValue)
                return;
            chatState.RevealedBoundaryLid = long.MaxValue;
        }
        using (Invalidation.Begin())
            _ = GetBlockState(chatId, default);
    }
```

> Verify `Chats.ReadReverse` yields ascending-local-id-descending order and its signature (`IAsyncEnumerable<ChatEntry>`), matching `GetThreadPreviewEntries` usage in `ChatUI.Tiles.cs`. `ChatEntry.LocalId` and `IsSystemEntry` are the fields used there.

- [ ] **Step 4: Thread the effective boundary into the tile builder.** In `ChatUI.Tiles.cs` (~L80-85), fold to the effective boundary so revealed rows render below the card:

```csharp
        var effectiveFoldBoundaryLid = Math.Min(blockState.FoldBoundaryLid, blockState.RevealedBoundaryLid);
        var liveFoldRange = rawLive is { SessionStartedAt: not null }
            && effectiveFoldBoundaryLid > rawLive.EffectiveVisibleStartLid
                ? new Range<long>(
                    rawLive.EffectiveVisibleStartLid,
                    Math.Min(effectiveFoldBoundaryLid, rawLive.EndEntryLid + 1))
                : default;
```

- [ ] **Step 5: Reset the reveal on expand/collapse.** In `ChatUI.cs` `ToggleExpandConversation` (~L364-371), after flipping the override, reset the reveal for that conversation so a re-collapse re-latches to a compact card:

```csharp
        _conversationExpansionOverrides.Value = mustRemove
            ? overrides.Remove(conversationId)
            : overrides.Add(conversationId);
        Hub.LiveBlockUI.ResetReveal(conversationId.ChatId);
```

(The overlay-collapse early-return path above it does not touch the reveal — a closed block has no reveal.)

- [ ] **Step 6: Write the integration test** — after `RevealMore`, the effective fold boundary retreats so more rows render, and it survives a subsequent tail advance; `ResetReveal` restores it. Add to `LiveConversationDisplayTest.cs`:

```csharp
[Fact]
public async Task RevealMoreRetreatsEffectiveFoldAndPersists()
{
    await Tester.SignInAsUniqueBob();
    var (chat, _) = await Tester.CreateAndGetChat(false, "reveal-more-test");
    var author = await Tester.GetOwnAuthor(chat.Id).Require();
    var peerId = AuthorId.New(chat.Id, 777_410);
    var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
    await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
    await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
    var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
    var v = live!.EffectiveVisibleStartLid;
    await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
        Title = "Recap", Description = "d", Summary = "s", EndEntryLid = v, MessageCount = 1,
    }, CancellationToken.None);
    for (var i = 0; i < 20; i++) await Tester.CreateTextEntry(chat.Id, $"m-{i}");

    var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
    var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
    var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
    await chatAudioUI.SetRecordingChatId(chat.Id);
    chatUI.SelectChatOnNavigation(chat.Id);

    // Drive the boundary up near the tail (viewport at the last entry), so a large range is folded.
    var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
    chatUI.SetItemVisibility(new VirtualListItemVisibility(
        chat.Id, [new ChatMessageKey(chat.Id, idRange.End - 1)], false));

    long foldedBoundary = 0;
    await ComputedTest.When(async ct => {
        var s = await liveBlockUI.GetBlockState(chat.Id, ct);
        s.FoldBoundaryLid.Should().BeGreaterThan(v + 5);
        foldedBoundary = s.FoldBoundaryLid;
    }, TimeSpan.FromSeconds(15));

    await liveBlockUI.RevealMore(chat.Id);
    await ComputedTest.When(async ct => {
        var s = await liveBlockUI.GetBlockState(chat.Id, ct);
        Math.Min(s.FoldBoundaryLid, s.RevealedBoundaryLid).Should().BeLessThan(foldedBoundary,
            "revealing a batch retreats the effective fold boundary");
    }, TimeSpan.FromSeconds(10));

    liveBlockUI.ResetReveal(chat.Id);
    await ComputedTest.When(async ct => {
        var s = await liveBlockUI.GetBlockState(chat.Id, ct);
        s.RevealedBoundaryLid.Should().Be(long.MaxValue, "reset clears the reveal");
    }, TimeSpan.FromSeconds(10));
}
```

- [ ] **Step 7: Build + test.**

```bash
dotnet build ActualChat.CI.slnf   # 0 errors
dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~LiveConversationDisplayTest"
```
Expected: all PASS.

- [ ] **Step 8: Commit.**

```bash
git add src/dotnet/UI.Blazor.App/Services/LiveBlockUI.cs \
  src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs \
  src/dotnet/UI.Blazor.App/Services/ChatUI.cs \
  tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs
git commit -m "feat(live): reveal-boundary state — RevealMore/ResetReveal + effective fold (§7 state)"
```

---

## Task 4: Swallowed count + the straddling "Show N of M" pill

Show the pill on a collapsed joined block whenever messages are swallowed. Label "▲ Show N earlier messages of M": M = the true swallowed count in `[V, effectiveBoundary)`, N = the next batch (`min(roundUpTo5(viewportCount), M)`). Clicking calls `RevealMore`; when M reaches 0 the pill disappears.

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/LiveBlockUI.cs` — add `[ComputeMethod] GetSwallowedCount`
- Modify: `.../Conversation/ConversationLiveState.cs` — add `int SwallowedCount`, `int RevealBatch`
- Modify: `.../Conversation/ConversationMessageView.razor` — compute the two counts; render the pill; wire click
- Modify: `.../Conversation/conversation.css` — `.c-lc-showmore` pill styling
- Test: `tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs`

**Interfaces:**
- Consumes: `LiveBlockUI.GetBlockState`; `Chats.ReadReverse`; `Hub.ChatUI.ItemVisibility`; `LiveBlockUI.RevealMore`.
- Produces: `ConversationLiveState.SwallowedCount` (M) and `RevealBatch` (N); a `.c-lc-showmore` pill button in the collapsed joined card.

- [ ] **Step 1: Add `GetSwallowedCount` to `LiveBlockUI`.** It counts real messages in `[V, effectiveBoundary)` for the pill's "of M". It is a `[ComputeMethod]` so it caches and re-computes only as the boundary/reveal move.

```csharp
    [ComputeMethod]
    public virtual async Task<int> GetSwallowedCount(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var blockState = await GetBlockState(chatId, cancellationToken).ConfigureAwait(false);
        var raw = await LiveSessionUI.GetState(chatId, cancellationToken).ConfigureAwait(false);
        if (raw is not { SessionStartedAt: not null })
            return 0;
        var v = raw.EffectiveVisibleStartLid;
        var effectiveBoundary = Math.Min(blockState.FoldBoundaryLid, blockState.RevealedBoundaryLid);
        if (effectiveBoundary <= v)
            return 0;

        var count = 0;
        await foreach (var entry in Chats.ReadReverse(Session, chatId, cancellationToken).ConfigureAwait(false)) {
            if (entry.LocalId >= effectiveBoundary || entry.IsSystemEntry)
                continue;
            if (entry.LocalId < v)
                break;
            count++;
        }
        return count;
    }
```

> Perf note (call it out in the review): `GetSwallowedCount` reads the swallowed range each recompute. It is cached (`[ComputeMethod]`) and the block is singular, but a very long collapsed session counts many entries. Acceptable for now; if it shows up in profiling, back it with `ChatRangeMeta` entry-count sums instead of a full read. Do **not** substitute a lid-span (`effectiveBoundary - v`) — lids have gaps; the count must be real messages.

- [ ] **Step 2: Add the fields to `ConversationLiveState.cs`:**

```csharp
public sealed record ConversationLiveState(
    TranslatedConversation Conversation,
    bool IsLive,
    bool IsJoined,
    bool IsVoiceOnly,
    string ParticipantsText = "",
    bool HasFoldedEntries = false,
    bool IsExpanded = false,
    IReadOnlyList<PreviewEntry>? TailPreview = null,
    bool HasSummary = false,
    int SwallowedCount = 0,
    int RevealBatch = 0);
```

- [ ] **Step 3: Populate them in `ConversationMessageView.ComputeState`.** The pill only matters for a joined, collapsed block, so compute the count only then (avoid the read otherwise):

```csharp
var swallowedCount = 0;
var revealBatch = 0;
if (isJoined && !isExpanded) {
    swallowedCount = await Hub.LiveBlockUI.GetSwallowedCount(chatId, cancellationToken).ConfigureAwait(false);
    if (swallowedCount > 0) {
        var visibleCount = Hub.ChatUI.ItemVisibility.Value.VisibleMessageLids.Count;
        revealBatch = Math.Min(Math.Max(5, ((visibleCount + 4) / 5) * 5), swallowedCount);
    }
}
return new ConversationLiveState(
    translated, isLive, isJoined, isVoiceOnly, participantsText, hasFoldedEntries, isExpanded,
    tailPreview, hasSummary, swallowedCount, revealBatch);
```

> Match the actual `ComputeState` locals (`isJoined`, `isExpanded`, `chatId`) already present from Plan 1's Task 3/4. `ItemVisibility.Value` is a non-reactive read here — that is fine for the batch label (a display heuristic); the pill's *visibility* is gated on `SwallowedCount` which IS reactive.

- [ ] **Step 4: Render the pill** in `ConversationMessageView.razor`, at the bottom edge of the collapsed live card (after the meta-row, inside the `c-live-card` so it can straddle the card's bottom border). Show only when `state.SwallowedCount > 0`:

```razor
@if (!state.IsExpanded && state.SwallowedCount > 0) {
    <button type="button" class="c-lc-showmore" @onclick="@OnRevealMore">
        <i class="icon-chevron-up"></i>
        <span>Show @state.RevealBatch earlier message@(state.RevealBatch == 1 ? "" : "s") of @state.SwallowedCount</span>
    </button>
}
```

Add the handler (mirrors `OnJoin`):

```csharp
private async Task OnRevealMore() {
    var chatId = Message.Conversation!.Id.ChatId;
    await Hub.LiveBlockUI.RevealMore(chatId).ConfigureAwait(true);
}
```

> Confirm the up-chevron icon class against the icon set (`rg "icon-chevron-up|icon-expand" src/dotnet/UI.Blazor.App` — use the one already in the codebase; the header expand chevron's icon is the reference).

- [ ] **Step 5: Style `.c-lc-showmore`** in `conversation.css` — the Call/Map-switch rounded pill, straddling the card's bottom edge. The card wrapper must be `position: relative` and must not clip (the sticky-header work already dropped `overflow:hidden` on the joined group — verify no ancestor clips). Reuse the `--cr-item-badge-selected-text` colour token.

```css
.c-live-card {
    @apply relative;   /* anchor for the straddling pill; add only if not already present */
}
.c-lc-showmore {
    @apply absolute left-1/2 bottom-0 flex-x items-center gap-x-1;
    @apply px-3 h-7 rounded-full;
    @apply text-caption-1 text-[var(--cr-item-badge-selected-text)];
    @apply bg-[var(--background-01)] border border-solid;
    border-color: var(--separator-01, rgba(0, 0, 0, 0.08));
    box-shadow: 0 1px 4px rgba(0, 0, 0, 0.12);
    transform: translate(-50%, 50%);
    z-index: 5;
}
.c-lc-showmore i {
    @apply text-icons-05;
}
body.hoverable .c-lc-showmore:hover {
    @apply bg-[var(--background-02)];
}
```

> Adjust tokens/utilities to the ones the file actually uses (grep the existing `.show-more-btn` and switch container rules for the exact border/shadow the design uses). Keep it ≤120 cols. Verify the pill visually straddles (half over the tint, half over the first message row) in the visual pass; nudge `bottom`/`transform` if the card's bottom padding hides it.

- [ ] **Step 6: Write the test** — a collapsed joined block with swallowed messages exposes a positive `SwallowedCount` on its card state; an expanded one does not. Assert at the model level. Add to `LiveConversationDisplayTest.cs`:

```csharp
[Fact]
public async Task CollapsedJoinedBlockReportsSwallowedCount()
{
    await Tester.SignInAsUniqueBob();
    var (chat, _) = await Tester.CreateAndGetChat(false, "swallowed-count-test");
    var author = await Tester.GetOwnAuthor(chat.Id).Require();
    var peerId = AuthorId.New(chat.Id, 777_420);
    var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
    await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
    await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
    var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
    var v = live!.EffectiveVisibleStartLid;
    await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
        Title = "Recap", Description = "d", Summary = "s", EndEntryLid = v, MessageCount = 1,
    }, CancellationToken.None);
    for (var i = 0; i < 12; i++) await Tester.CreateTextEntry(chat.Id, $"m-{i}");

    var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
    var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
    var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
    await chatAudioUI.SetRecordingChatId(chat.Id);
    chatUI.SelectChatOnNavigation(chat.Id);
    var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
    chatUI.SetItemVisibility(new VirtualListItemVisibility(
        chat.Id, [new ChatMessageKey(chat.Id, idRange.End - 1)], false));

    await ComputedTest.When(async ct => {
        var count = await liveBlockUI.GetSwallowedCount(chat.Id, ct);
        count.Should().BeGreaterThan(0, "a collapsed joined block with tail above the viewport swallows messages");
    }, TimeSpan.FromSeconds(15));
}
```

- [ ] **Step 7: Build + verify.**

```bash
dotnet build ActualChat.CI.slnf   # 0 errors
npm run build:Verify              # CSS touched — tsc + eslint + debug build clean
dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~LiveConversationDisplayTest"
```
Expected: build 0 errors; `build:Verify` clean; tests all PASS.

- [ ] **Step 8: Commit.**

```bash
git add src/dotnet/UI.Blazor.App/Services/LiveBlockUI.cs \
  src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationLiveState.cs \
  src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageView.razor \
  src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/conversation.css \
  tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs
git commit -m "feat(live): Show N earlier messages of M pill (§7)"
```

- [ ] **Step 9: Manual visual + no-jump pass (chrome-devtools + `/virtual-list-debug`).** Joined, collapsed, with a backlog above the viewport: the pill straddles the card's bottom edge (half over the tint, half over the first message), reads "▲ Show N earlier messages of M" with M = the true swallowed count; each click reveals a batch **in place with no viewport jump** (older rows appear above, bottom-anchored; scroll up into them), M decrements, and when it hits 0 the pill disappears. Expand → collapse resets the reveal (pill returns for the full backlog). Confirm the number reads from the true swallowed count, not the summary's MessageCount.

---

## Verification (whole plan)

1. `dotnet build ActualChat.CI.slnf` — 0 errors.
2. `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj -c Debug --filter "FullyQualifiedName~LiveFoldMathTest"` — 5/5.
3. `dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~LiveConversationDisplayTest"` — all PASS (existing + 3 new).
4. `npm run build:Verify` — clean.
5. Manual two-identity pass against the running app (joined reader): collapsed block swallows lines above the viewport as you read (no jump, compact card); the "Show N of M" pill reveals a batch in place per click (no jump), M decrements to 0 then the pill vanishes; expanding reads as a plain conversation and collapsing re-latches to a compact card with the pill back. Use `/virtual-list-debug` at every transition.

## Self-review notes (spec coverage)

- §4 swallow-above-viewport → Phase A (Tasks 1-2): boundary = monotonic viewport-top max, un-summarised rows fold. §7 show-more → Phase B (Tasks 3-4): `RevealedBoundaryLid` retreat + straddling pill + true swallowed count. Batch = viewport count rounded up to 5 (user decision). Reveal persists until expand/re-collapse (reset from `ToggleExpandConversation`).
- **Reused, not forked:** `LiveBlockUI`/`LiveFoldMath` (extended, summary-lag path removed per spec), `ItemVisibility`, `Chats.ReadReverse`, the `--cr-item-badge-selected-text` token + switch pill visual. New: `RevealedBoundaryLid` state (on the existing governor, not a new service) and the `.c-lc-showmore` pill (local to the live block, promotion path noted in the spec if a second use appears).
- **Out of scope (spec):** unjoined preview (Plan 1 §6 — pill is joined-only), close/materialisation tiers, activity panel, summary/ContextStartLid recomputation.

## Risks & mitigations

- **No-jump on fold (core §4 risk).** Folding un-summarised above-viewport rows is new. Mitigation: the boundary is monotonic and clamped to the viewport top; expansion already suppresses the fold at the tile builder, so no jump on collapse. Verified with `/virtual-list-debug` at Task 2 Step 7 before Phase B.
- **No-jump on reveal (§7).** Revealing retreats the *effective* boundary while the governor's monotonic boundary keeps advancing — so new tail arrivals never un-reveal, and revealed rows render bottom-anchored. Verified at Task 4 Step 9.
- **Existing fold-timing tests.** Phase A removes `FoldLag`; any `LiveConversationDisplayTest` that asserted summary-lag fold ranges must move to the viewport-tracking expectation (Task 2 Step 6).
- **`GetSwallowedCount` cost.** Cached; reads the swallowed range per recompute. Perf caveat flagged for review; back with `ChatRangeMeta` counts if it profiles hot. Never approximate with a lid span.
- **Reveal stranding.** `ResetReveal` fires on expand/collapse; the reveal also resets when the chat state is recreated (block rebuild) and is absent for closed/overlay blocks (which have no `Template` reveal path). Confirm no half-revealed card survives a session close in the manual pass.
