# Live Conversation Block UX Polish (Round 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** `docs/superpowers/specs/2026-07-20-live-block-ux-polish-design.md` (approved 2026-07-20)

**Goal:** Make the live conversation block's fold/close behavior never mutate a watching viewport: governed mid-call folding (freshness lag + viewport guard), a leave/close overlay that freezes the rendered state, and animated card size changes.

**Architecture:** Everything is client-side in `UI.Blazor.App`. A new scoped compute service `LiveBlockUI` owns (a) a per-chat monotonic **fold boundary** advanced by pure math in `LiveFoldMath` (lag + viewport clamp), and (b) a per-chat **overlay** that freezes the block's rendered shape when the viewer leaves the call or the session closes. `ChatUI.Tiles` consumes `LiveBlockUI.GetBlockState` instead of the raw live-session fold range, and substitutes the materialized conversation's id with the live render id after close so the VirtualList `@key` (`ConversationBlock:{V}`) never changes under the viewer. Card size changes animate via CSS only.

**Tech Stack:** .NET 10 / Blazor, ActualLab.Fusion (compute services, `MutableState`), xUnit, modern CSS (`@starting-style`, `grid-template-rows` transition).

## Global Constraints

- **Read `docs/CODING_STYLE.md` before writing any code.** Non-negotiable specifics: no `Async` suffix on async methods; no XML doc comments; comments only for non-obvious constraints (see the "Regular comments…" section); match surrounding brace style; file-scoped namespaces.
- Build with `dotnet build ActualChat.CI.slnf` (never the full `.sln`).
- After TypeScript/CSS changes run `npm run build:Verify` (unless `/server-loop` runs — then trigger its rebuild instead).
- Work on branch `feat/live-block-ux-polish` created from `dev`.
- Commit after each task. **Never push** — the user pushes explicitly.
- Temporary files go to `<projectRoot>/tmp`, never the project root.
- No server, wire-model, or DB changes anywhere in this plan (spec §4).

## Reuse

- `ChatUI.ItemVisibility` (`MutableState<ChatViewItemVisibility>`, `src/dotnet/UI.Blazor.App/Services/ChatUI.cs:60`) — viewport-guard input (`VisibleMessageLids`).
- `ChatUI.SelectedChatId` (`ChatUI.cs:54`) — the "navigated away" signal that clears governor state.
- `LiveSessionUI.GetState` / `AmIInLiveConversation` (`src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs:35,74`) — raw live state + join detection.
- `ChatUI._conversationExpansionOverrides` + `ToggleExpandConversation` (`ChatUI.cs:26,357`) — the manual-toggle hook that clears the overlay.
- `Clocks.ServerClock` (offset-maintained by `ServerTimeSync`) — server-synced lag math against the server-stamped `LiveSessionState.LastSummaryAt`.
- `UIWorkerBase<AppUIHub>` + `Computed.Capture`/`WhenInvalidated` loop pattern (as in `LiveSessionUI.RunParticipationSync`).
- Test harnesses: `tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs` (BlazorTester + `ILiveSessionsBackend` driving), `tests/Chat.UI.Blazor.UnitTests` (references `UI.Blazor.App`).

New components: `LiveBlockUI` + `LiveFoldMath` are chat-view presentation state, feature-specific → they live in `src/dotnet/UI.Blazor.App/Services/` beside `ChatUI` (no `ActualChat.Core` promotion; nothing else consumes per-viewport fold state — evaluated per the planning rule, local placement wins).

---

## Background: the shipped round-2 mechanics you are changing

- `ChatUI.Tiles.cs` `GetChatItemsInternal` (~L59-76) computes, per chat:
  - `hiddenLiveTailRange = [EndEntryLid+1, MaxValue)` for not-joined viewers,
  - `liveFoldRange = [EffectiveVisibleStartLid, EndEntryLid+1)` (gated on `LastSummaryAt`) for joined viewers,
  - and passes them via `ConversationViewState` into `GetTile`, which skips `liveFoldRange` for the joined live block (`c.Id == joinedLiveId ? liveFoldRange : c.EntryLidRange`, ~L521).
- The moment a participant **hangs up**, `AmIInLiveConversation` flips false → `joinedLiveId` becomes null → the whole `EntryLidRange` is skipped and the tail hides. That is the "резко сворачивает в саммари" complaint. The moment the session **closes**, the live conversation is replaced by a materialized one keyed at `ContextStartLid` → the card re-keys and collapsed defaults apply. Both transitions currently yank the viewport; this plan freezes both.
- `LiveSessionState` (`src/dotnet/Api/Live/LiveSessionState.cs`) fields used here: `EffectiveVisibleStartLid` (V), `EndEntryLid`, `LastSummaryAt`, `SessionStartedAt`, `ContextStartLid`, `IsExpandedByDefault`.
- **Freshness-lag refinement vs. the spec:** the spec states the lag in terms of entry age (`BeginsAt`). This plan implements it via the server-stamped `LastSummaryAt` of the summary pass that produced each fold advance — every entry a summary covers began before that stamp, so "adopt the fold `FoldLag` after its stamp" guarantees every folded entry is at least `FoldLag` old, with no entry reads, one deterministic timestamp per fold, and one chunky fold event per summary pass instead of per-entry creep. This is a deliberate implementation choice, not a spec deviation in behavior.

---

### Task 1: LiveFoldMath — pure fold-boundary math

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/LiveFoldMath.cs`
- Create: `tests/Chat.UI.Blazor.UnitTests/LiveFoldMathTest.cs`

**Interfaces:**
- Produces: `readonly record struct PendingFold(long FoldEndLid, Moment SummaryAt)`; `static LiveFoldMath.Advance(long boundaryLid, IReadOnlyList<PendingFold> pending, Moment serverNow, TimeSpan foldLag, long? minVisibleLidInBlock) -> LiveFoldMath.Result(long BoundaryLid, IReadOnlyList<PendingFold> Pending, Moment? NextWakeAt)`. Task 2 consumes these exact names.

Semantics: a pending fold `(FoldEndLid, SummaryAt)` is **ripe** once `SummaryAt + foldLag <= serverNow`. The boundary adopts the max ripe fold end, clamped so it never crosses a visible lid (`minVisibleLidInBlock`), and never moves backward. Ripe-but-clamped folds stay pending (a later visibility change re-runs the math); un-ripe folds produce `NextWakeAt` for the governor's timer.

- [ ] **Step 1: Write the failing tests**

```csharp
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public class LiveFoldMathTest
{
    private static readonly Moment T0 = new(new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc));
    private static readonly TimeSpan Lag = TimeSpan.FromMinutes(3);

    [Fact]
    public void RipeFoldAdvancesBoundary()
    {
        var result = LiveFoldMath.Advance(10, [new(21, T0)], T0 + Lag, Lag, null);
        result.BoundaryLid.Should().Be(21);
        result.Pending.Should().BeEmpty();
        result.NextWakeAt.Should().BeNull();
    }

    [Fact]
    public void UnripeFoldStaysPendingAndSchedulesWake()
    {
        var result = LiveFoldMath.Advance(10, [new(21, T0)], T0 + TimeSpan.FromMinutes(1), Lag, null);
        result.BoundaryLid.Should().Be(10);
        result.Pending.Should().ContainSingle().Which.FoldEndLid.Should().Be(21);
        result.NextWakeAt.Should().Be(T0 + Lag);
    }

    [Fact]
    public void ViewportClampHoldsBoundaryAndKeepsFoldPending()
    {
        // Entry 15 is visible - the boundary must not cross it even though the fold is ripe
        var result = LiveFoldMath.Advance(10, [new(21, T0)], T0 + Lag, Lag, 15);
        result.BoundaryLid.Should().Be(15);
        result.Pending.Should().ContainSingle().Which.FoldEndLid.Should().Be(21);
        result.NextWakeAt.Should().BeNull(); // ripe - only visibility holds it, no timer needed
    }

    [Fact]
    public void BoundaryIsMonotonic()
    {
        // A visible lid below the current boundary (viewer expanded the fold) must not move it back
        var result = LiveFoldMath.Advance(20, [], T0, Lag, 5);
        result.BoundaryLid.Should().Be(20);
    }

    [Fact]
    public void MaxRipeFoldWinsAndEarliestUnripeSchedulesWake()
    {
        var pending = new PendingFold[] { new(15, T0), new(21, T0 + TimeSpan.FromSeconds(30)), new(30, T0 + TimeSpan.FromMinutes(2)) };
        var result = LiveFoldMath.Advance(10, pending, T0 + Lag + TimeSpan.FromMinutes(1), Lag, null);
        result.BoundaryLid.Should().Be(21);
        result.Pending.Should().ContainSingle().Which.FoldEndLid.Should().Be(30);
        result.NextWakeAt.Should().Be(T0 + TimeSpan.FromMinutes(2) + Lag);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~LiveFoldMathTest" 2>&1 | tail -20`
Expected: build FAILURE with "The type or namespace name 'LiveFoldMath' could not be found".

- [ ] **Step 3: Write the implementation**

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public readonly record struct PendingFold(long FoldEndLid, Moment SummaryAt);

public static class LiveFoldMath
{
    public sealed record Result(long BoundaryLid, IReadOnlyList<PendingFold> Pending, Moment? NextWakeAt);

    public static Result Advance(
        long boundaryLid,
        IReadOnlyList<PendingFold> pending,
        Moment serverNow,
        TimeSpan foldLag,
        long? minVisibleLidInBlock)
    {
        var ripeFoldEndLid = 0L;
        Moment? nextWakeAt = null;
        foreach (var fold in pending)
            if (fold.SummaryAt + foldLag <= serverNow)
                ripeFoldEndLid = Math.Max(ripeFoldEndLid, fold.FoldEndLid);
            else {
                var wakeAt = fold.SummaryAt + foldLag;
                if (nextWakeAt == null || wakeAt < nextWakeAt.GetValueOrDefault())
                    nextWakeAt = wakeAt;
            }

        var candidate = ripeFoldEndLid;
        if (minVisibleLidInBlock is { } minVisibleLid)
            candidate = Math.Min(candidate, minVisibleLid);
        var newBoundaryLid = Math.Max(boundaryLid, candidate);
        var remaining = pending.Where(f => f.FoldEndLid > newBoundaryLid).ToList();
        return new Result(newBoundaryLid, remaining, nextWakeAt);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~LiveFoldMathTest" 2>&1 | tail -5`
Expected: `Passed! - Failed: 0, Passed: 5`.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/LiveFoldMath.cs tests/Chat.UI.Blazor.UnitTests/LiveFoldMathTest.cs
git commit -m "feat(live): fold-boundary math with freshness lag and viewport clamp"
```

---

### Task 2: LiveBlockUI — fold governor + leave/close overlay

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/LiveBlockUI.cs`
- Modify: `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs` (register beside `fusion.AddService<LiveSessionUI>(ServiceLifetime.Scoped)` at ~L75)
- Modify: `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs` (property beside `LiveSessionUI` at ~L59)
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatUI.cs` (`ToggleExpandConversation` at ~L357 + new `EnsureConversationCollapsed`)
- Test: `tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs` (one governor-latch fact)

**Interfaces:**
- Consumes: `LiveFoldMath.Advance`, `PendingFold` (Task 1).
- Produces (Task 3 consumes these exact shapes):

```csharp
public sealed record LiveBlockOverlay(
    ConversationId RenderId,        // ConversationId.New(chatId, V) - the id the block keeps rendering under
    long CardLid,                   // V
    Range<long> FoldRange,          // entries hidden behind the card; empty if nothing folded
    Range<long> HiddenTailRange,    // entries hidden below the visible tail; empty when none
    long BlockEndLid,               // exclusive end of the block's grouping range; long.MaxValue while live
    ConversationId? MaterializedId, // set once closed: the persisted conversation to render as RenderId
    bool IsExpandedByDefault);      // captured from the last raw state at close

public sealed record LiveBlockState(long FoldBoundaryLid, LiveBlockOverlay? Overlay)
{
    public static readonly LiveBlockState None = new(0, null);
}

// On LiveBlockUI:
[ComputeMethod] Task<LiveBlockState> GetBlockState(ChatId chatId, CancellationToken ct);
bool TryCollapseOverlay(ConversationId conversationId);   // clears a closed overlay; returns true if handled
internal TimeSpan FoldLag = TimeSpan.FromMinutes(3);      // instance field so tests can shrink it
```

**Behavioral contract** (mirror of spec §1-2):

1. First observation of a chat (on-demand in `GetBlockState` or by the loop) latches `FoldBoundaryLid` to the raw fold end (`EndEntryLid + 1` when `LastSummaryAt > 0 && EndEntryLid >= V`, else `0`) — a fresh render starts fully folded, only *subsequent* folds are governed.
2. While the raw fold end advances, each advance becomes a `PendingFold(newFoldEnd, raw.LastSummaryAt)`; `LiveFoldMath.Advance` adopts it after `FoldLag`, clamped by the min visible lid `>= V` from `ChatUI.ItemVisibility` (only when `ItemVisibility.Value.ChatId == chatId`; otherwise pass `null`).
3. **Leave** (`wasJoined && !isJoined && raw != null`): create an overlay freezing the render — `RenderId = New(chatId, V)`, `FoldRange = boundary > V ? [V, boundary) : empty`, `HiddenTailRange = [chatEnd, MaxValue)` where `chatEnd` = `Chats.GetIdRange(Session, chatId, ct).End` read under `Computed.BeginIsolation()`, `BlockEndLid = chatEnd`, `MaterializedId = null`. The governed boundary keeps advancing and keeps `overlay.FoldRange` in sync.
4. **Rejoin** (overlay active, `MaterializedId == null`, `isJoined` again): drop the overlay (joining is a user action — revealing the tail is expected).
5. **Close** (`lastRaw is { SessionStartedAt: not null }` and raw becomes null): if the session never produced a summary (`lastRaw.LastSummaryAt.EpochOffsetTicks <= 0` — a tier-1 close, nothing materializes) **drop the overlay entirely** — the card vanishes and plain entries stay/appear, which round 2 already accepted; a kept overlay would hide entry V behind a card that no longer exists. Otherwise set/create the overlay with `MaterializedId = ConversationId.New(chatId, lastRaw.ContextStartLid > 0 ? lastRaw.ContextStartLid : V)` and `IsExpandedByDefault = lastRaw.IsExpandedByDefault`:
   - existing leaver overlay → keep `FoldRange`, bound the tail: `HiddenTailRange = [old.Start, lastRaw.EndEntryLid + 1)` (empty if `Start >= End`), `BlockEndLid = lastRaw.EndEntryLid + 1`;
   - was joined at close → `FoldRange` = current governed fold, `HiddenTailRange = empty`, `BlockEndLid = lastRaw.EndEntryLid + 1`;
   - was watching unjoined → `FoldRange = [V, lastRaw.EndEntryLid + 1)`, `HiddenTailRange = empty`, `BlockEndLid = lastRaw.EndEntryLid + 1`.
6. **Cleanup**: when `SelectedChatId` moves to a different chat, drop that chat's whole `ChatFoldState` (boundary + overlay) and invalidate its `GetBlockState` — a later visit renders the shipped tiered defaults.
7. `TryCollapseOverlay(conversationId)`: if some chat's overlay has `MaterializedId != null` and `conversationId` equals `RenderId` or `MaterializedId` → clear the overlay, call `Hub.ChatUI.EnsureConversationCollapsed(MaterializedId, IsExpandedByDefault)`, invalidate, return true. Overlays still live (`MaterializedId == null`) return false — the mid-call toggle keeps its existing expand/collapse semantics.

- [ ] **Step 1: Write the failing integration test** (append to `LiveConversationDisplayTest.cs`, mirroring its existing setup style)

```csharp
[Fact]
public async Task GovernorLatchesInitialFoldBoundary()
{
    await Tester.SignInAsUniqueBob();
    var (chat, _) = await Tester.CreateAndGetChat(false, "Governor latch");
    for (var i = 0; i < 5; i++)
        await Tester.CreateTextEntry(chat.Id, $"entry {i}");

    var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
    var authorId1 = new AuthorId(chat.Id, 1, AssumeValid.Option);
    var authorId2 = new AuthorId(chat.Id, 2, AssumeValid.Option);
    await liveBackend.OnStreamRegistered(chat.Id, authorId1, null, true, CT);
    await liveBackend.OnStreamRegistered(chat.Id, authorId2, null, true, CT);
    var live = await liveBackend.GetState(chat.Id, CT);
    live.Should().NotBeNull();
    var v = live!.EffectiveVisibleStartLid;
    await liveBackend.UpdateSummary(chat.Id,
        new LiveSessionSummary {
            Title = "Latch", Description = "d", Summary = "s",
            EndEntryLid = v + 2, MessageCount = 3,
        }, CT);

    var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
    var blockState = await liveBlockUI.GetBlockState(chat.Id, CT);
    // First observation latches to the raw fold end - fresh renders start fully folded
    blockState.FoldBoundaryLid.Should().Be(v + 3);
    blockState.Overlay.Should().BeNull();
}
```

Note: copy the exact `AuthorId`/`OnStreamRegistered`/`UpdateSummary` invocation shapes from the existing facts in this file — if they differ from the above (e.g. helper methods exist), prefer the file's own idiom.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~GovernorLatchesInitialFoldBoundary" 2>&1 | tail -20`
Expected: build FAILURE ("LiveBlockUI could not be found").

- [ ] **Step 3: Implement `LiveBlockUI`**

```csharp
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record LiveBlockOverlay(
    ConversationId RenderId,
    long CardLid,
    Range<long> FoldRange,
    Range<long> HiddenTailRange,
    long BlockEndLid,
    ConversationId? MaterializedId,
    bool IsExpandedByDefault);

public sealed record LiveBlockState(long FoldBoundaryLid, LiveBlockOverlay? Overlay)
{
    public static readonly LiveBlockState None = new(0, null);
}

public class LiveBlockUI(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub), IComputeService, INotifyInitialized
{
    internal TimeSpan FoldLag = TimeSpan.FromMinutes(3);

    private readonly Dictionary<ChatId, ChatFoldState> _chatStates = new();

    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private IChats Chats => Hub.Chats;

    private sealed class ChatFoldState
    {
        public MutableState<LiveBlockState> State = null!;
        public IReadOnlyList<PendingFold> Pending = [];
        public long LastObservedFoldEndLid;
        public LiveSessionState? LastRaw;
        public bool WasJoined;
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual async Task<LiveBlockState> GetBlockState(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var chatState = await GetOrCreateChatState(chatId, cancellationToken).ConfigureAwait(false);
        return await chatState.State.Use(cancellationToken).ConfigureAwait(false);
    }

    public bool TryCollapseOverlay(ConversationId conversationId)
    {
        lock (Lock) {
            foreach (var (chatId, chatState) in _chatStates) {
                var overlay = chatState.State.Value.Overlay;
                if (overlay is not { MaterializedId: { } materializedId })
                    continue;
                if (overlay.RenderId != conversationId && materializedId != conversationId)
                    continue;

                chatState.State.Value = chatState.State.Value with { Overlay = null };
                Hub.ChatUI.EnsureConversationCollapsed(materializedId, overlay.IsExpandedByDefault);
                return true;
            }
        }
        return false;
    }

    protected override Task OnRun(CancellationToken cancellationToken)
        => RunFoldGovernor(cancellationToken);

    // Private methods

    private async Task<ChatFoldState> GetOrCreateChatState(ChatId chatId, CancellationToken cancellationToken)
    {
        lock (Lock)
            if (_chatStates.TryGetValue(chatId, out var existing))
                return existing;

        // The initial latch adopts the raw fold end as-is: a fresh render starts fully folded,
        // and only later advances go through the lag + viewport governor. Isolated read - the
        // latch must not make GetBlockState reactive to the raw live state.
        LiveSessionState? raw;
        using (Computed.BeginIsolation())
            raw = await LiveSessionUI.GetState(chatId, cancellationToken).ConfigureAwait(false);
        var foldEndLid = GetRawFoldEndLid(raw);
        lock (Lock) {
            if (_chatStates.TryGetValue(chatId, out var existing))
                return existing;

            var chatState = new ChatFoldState {
                State = StateFactory.NewMutable(
                    new LiveBlockState(foldEndLid, null),
                    StateCategories.Get(GetType(), nameof(GetBlockState))),
                LastObservedFoldEndLid = foldEndLid,
                LastRaw = raw,
            };
            _chatStates.Add(chatId, chatState);
            return chatState;
        }
    }

    private static long GetRawFoldEndLid(LiveSessionState? raw)
        => raw is { SessionStartedAt: not null, LastSummaryAt.EpochOffsetTicks: > 0 }
            && raw.EndEntryLid >= raw.EffectiveVisibleStartLid
            ? raw.EndEntryLid + 1
            : 0;

    private async Task RunFoldGovernor(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            Moment? wakeAt = null;
            try {
                var cInputs = await Computed
                    .Capture(() => GetGovernorInputs(cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
                var inputs = cInputs.Value;
                if (!inputs.ChatId.IsNone) {
                    wakeAt = await ProcessChat(inputs, cancellationToken).ConfigureAwait(false);
                    CleanupOtherChats(inputs.ChatId);
                }
                var timeout = wakeAt is { } w
                    ? (w - Clocks.ServerClock.Now).Positive()
                    : TimeSpan.FromHours(1);
                try {
                    await cInputs.WhenInvalidated(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException) { }
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                Log.LogError(e, "Fold governor iteration failed");
                await Clocks.CpuClock.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [ComputeMethod]
    protected virtual async Task<GovernorInputs> GetGovernorInputs(CancellationToken cancellationToken)
    {
        var chatId = await Hub.ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false) ?? ChatId.None;
        if (chatId.IsNone)
            return GovernorInputs.None;

        var raw = await LiveSessionUI.GetState(chatId, cancellationToken).ConfigureAwait(false);
        var visibility = await Hub.ChatUI.ItemVisibility.Use(cancellationToken).ConfigureAwait(false);
        var isJoined = raw != null
            && await LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
        return new GovernorInputs(chatId, raw, visibility, isJoined);
    }

    private async Task<Moment?> ProcessChat(GovernorInputs inputs, CancellationToken cancellationToken)
    {
        var (chatId, raw, visibility, isJoined) = inputs;
        var chatState = await GetOrCreateChatState(chatId, cancellationToken).ConfigureAwait(false);

        // The leaver freeze needs the chat end outside the lock (isolated non-reactive read)
        Range<long> chatIdRange = default;
        var isLeaving = raw is { SessionStartedAt: not null } && chatState.WasJoined && !isJoined
            && chatState.State.Value.Overlay == null;
        if (isLeaving)
            using (Computed.BeginIsolation())
                chatIdRange = await Chats.GetIdRange(Session, chatId, cancellationToken).ConfigureAwait(false);

        lock (Lock) {
            var state = chatState.State.Value;
            var overlay = state.Overlay;
            Moment? wakeAt = null;

            if (raw is { SessionStartedAt: not null }) {
                var v = raw.EffectiveVisibleStartLid;

                // New summary coverage -> pending fold, adopted after FoldLag
                var foldEndLid = GetRawFoldEndLid(raw);
                if (foldEndLid > chatState.LastObservedFoldEndLid) {
                    chatState.Pending = [..chatState.Pending, new PendingFold(foldEndLid, raw.LastSummaryAt)];
                    chatState.LastObservedFoldEndLid = foldEndLid;
                }

                if (isLeaving)
                    overlay = new LiveBlockOverlay(
                        ConversationId.New(chatId, v), v,
                        FoldRangeOf(v, state.FoldBoundaryLid),
                        new Range<long>(chatIdRange.End, long.MaxValue),
                        chatIdRange.End, null, false);
                else if (overlay is { MaterializedId: null } && isJoined)
                    overlay = null; // rejoined - the live tail is expected to reappear

                // Advance the governed boundary
                var minVisibleLid = visibility.ChatId == chatId && !visibility.IsEmpty
                    ? visibility.VisibleMessageLids.Where(lid => lid >= v).DefaultIfEmpty(long.MaxValue).Min()
                    : long.MaxValue;
                var result = LiveFoldMath.Advance(
                    state.FoldBoundaryLid, chatState.Pending, Clocks.ServerClock.Now, FoldLag,
                    minVisibleLid == long.MaxValue ? null : minVisibleLid);
                chatState.Pending = result.Pending;
                wakeAt = result.NextWakeAt;
                if (overlay is { MaterializedId: null })
                    overlay = overlay with { FoldRange = FoldRangeOf(v, result.BoundaryLid) };
                state = new LiveBlockState(result.BoundaryLid, overlay);
            }
            else if (chatState.LastRaw is { SessionStartedAt: not null } lastRaw) {
                // Session closed - freeze whatever this viewer was rendering (spec: no live-viewport collapse).
                // Tier-1 (never summarized -> nothing materializes): no overlay - a kept fold range would
                // hide entry V behind a card that no longer renders.
                if (lastRaw.LastSummaryAt.EpochOffsetTicks <= 0) {
                    state = state with { Overlay = null };
                    if (!Equals(chatState.State.Value, state))
                        chatState.State.Value = state;
                    chatState.LastRaw = raw;
                    chatState.WasJoined = isJoined;
                    return null;
                }
                var v = lastRaw.EffectiveVisibleStartLid;
                var blockEndLid = lastRaw.EndEntryLid + 1;
                var materializedId = ConversationId.New(chatId,
                    lastRaw.ContextStartLid > 0 ? lastRaw.ContextStartLid : v);
                overlay = overlay is { MaterializedId: null } leaverOverlay
                    ? leaverOverlay with {
                        HiddenTailRange = leaverOverlay.HiddenTailRange.Start < blockEndLid
                            ? new Range<long>(leaverOverlay.HiddenTailRange.Start, blockEndLid)
                            : default,
                        BlockEndLid = blockEndLid,
                        MaterializedId = materializedId,
                        IsExpandedByDefault = lastRaw.IsExpandedByDefault,
                    }
                    : overlay ?? new LiveBlockOverlay(
                        ConversationId.New(chatId, v), v,
                        chatState.WasJoined
                            ? FoldRangeOf(v, state.FoldBoundaryLid)
                            : new Range<long>(v, blockEndLid),
                        default, blockEndLid, materializedId, lastRaw.IsExpandedByDefault);
                state = state with { Overlay = overlay };
            }

            if (!Equals(chatState.State.Value, state))
                chatState.State.Value = state;
            chatState.LastRaw = raw;
            chatState.WasJoined = isJoined;
            return wakeAt;
        }

        static Range<long> FoldRangeOf(long v, long boundaryLid)
            => boundaryLid > v ? new Range<long>(v, boundaryLid) : default;
    }

    private void CleanupOtherChats(ChatId selectedChatId)
    {
        List<ChatId>? removed = null;
        lock (Lock)
            foreach (var chatId in _chatStates.Keys.Where(id => id != selectedChatId).ToList()) {
                _chatStates.Remove(chatId);
                (removed ??= []).Add(chatId);
            }
        if (removed == null)
            return;

        using (Invalidation.Begin())
            foreach (var chatId in removed)
                _ = GetBlockState(chatId, default);
    }

    protected sealed record GovernorInputs(
        ChatId ChatId,
        LiveSessionState? Raw,
        ChatViewItemVisibility Visibility,
        bool IsJoined)
    {
        public static readonly GovernorInputs None = new(ChatId.None, null, ChatViewItemVisibility.Empty, false);
    }
}
```

Adaptation notes for the implementer (verify against the actual codebase, these are the only expected friction points):
- `Lock`, `StateFactory`, `Session`, `Clocks`, `Log` come from `UIWorkerBase`; check the exact member names in `src/dotnet/UI.Blazor/BaseTypes/` and mirror how `LiveSessionUI`/`ChatUI` use them.
- `lock` + `await` cannot mix — the code above structures all awaits outside `lock (Lock)`; keep it that way if you restructure.
- If `Range<long>` lacks `.Positive()` on `TimeSpan` etc., use the codebase's equivalent (`TimeSpanExt`); search before writing anything new.

- [ ] **Step 4: Register in DI + AppUIHub + wire the toggle**

In `BlazorUIAppModule.cs` after line ~75 (`fusion.AddService<LiveSessionUI>(ServiceLifetime.Scoped);`):

```csharp
fusion.AddService<LiveBlockUI>(ServiceLifetime.Scoped);
```

In `AppUIHub.cs` beside the `LiveSessionUI` property (~L59):

```csharp
public LiveBlockUI LiveBlockUI => field ??= Services.GetRequiredService<LiveBlockUI>();
```

In `ChatUI.cs`, `ToggleExpandConversation` (~L357) — first line, and the new helper:

```csharp
public void ToggleExpandConversation(ConversationId conversationId)
{
    // A closed live-block overlay intercepts its own toggle: collapsing it is "dismiss the
    // frozen view", not an expansion override on the overlay's render id.
    if (Hub.LiveBlockUI.TryCollapseOverlay(conversationId))
        return;
    ...existing body unchanged...
}

internal void EnsureConversationCollapsed(ConversationId conversationId, bool isExpandedByDefault)
{
    var overrides = _conversationExpansionOverrides.Value;
    _conversationExpansionOverrides.Value = isExpandedByDefault
        ? overrides.Add(conversationId)
        : overrides.Remove(conversationId);
}
```

- [ ] **Step 5: Build + run the new test**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -5` — expect `0 Error(s)`.
Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~GovernorLatchesInitialFoldBoundary" 2>&1 | tail -5` — expect PASS.
Also run task 1's unit tests again — expect PASS.

- [ ] **Step 6: Commit**

```bash
git add -A src/dotnet/UI.Blazor.App tests/Chat.UI.Blazor.IntegrationTests
git commit -m "feat(live): LiveBlockUI fold governor with leave/close overlay"
```

---

### Task 3: Tile-builder integration — governed fold + overlay rendering

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs` (`ConversationViewState` L10-15, `GetChatItemsInternal` ~L59-76/L147-148/L226/L284/L344-347, `GetTile` ~L499/L514-523/L583/L590/L614/L669/L725, `GroupExpandedConversations` ~L844-897)
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageView.razor` (`ComputeState` `hasFoldedEntries`, ~L199-201)
- Test: `tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs`

**Interfaces:**
- Consumes: `LiveBlockUI.GetBlockState`, `LiveBlockState`, `LiveBlockOverlay` (Task 2).
- Produces: new `ConversationViewState` shape (exact record below) — nothing outside this file consumes it, but keep the destructuring in `GetTile` in sync.

- [ ] **Step 1: Write the failing integration tests** (append to `LiveConversationDisplayTest.cs`; reuse the file's setup idiom — sign-in, chat with ~10 entries, two `OnStreamRegistered` authors, `UpdateSummary`, `SetListeningState`, `chatUI.GetChatItems` with a full-range `ChatDataQuery`; select the chat via `chatUI.SelectChatOnNavigation(chat.Id)` in each test so the governor loop tracks it)

Test 1 — the hang-up freeze (the core complaint):

```csharp
[Fact]
public async Task LeaveKeepsRenderedTailVisible()
{
    // Arrange: joined viewer, live session with a summary folding [V, V+2], tail entries visible below
    // (setup per the file's idiom; summary EndEntryLid = v + 2; then 3 more entries after the summary)
    // Snapshot the visible leaf entry lids while joined:
    var joinedItems = await chatUI.GetChatItems(chat.Id, query, 0, CT);
    var joinedLeafLids = LeafEntryLids(joinedItems);

    // Act: hang up
    await chatAudioUI.SetListeningState(chat.Id, false);

    // Assert: the governor freezes the view - same leaf entries, still one grouped block
    await ComputedTest.When(async ct => {
        var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
        LeafEntryLids(items).Should().Equal(joinedLeafLids);
        items.Items.OfType<ExpandedConversationMessage>().Should().ContainSingle();
    }, TimeSpan.FromSeconds(10));
}
```

Test 2 — new messages after leaving stay hidden:

```csharp
[Fact]
public async Task NewEntriesAfterLeaveStayHidden()
{
    // Arrange: as above, then hang up and wait for the frozen view
    // Act: another author posts a new entry
    await Tester.CreateTextEntry(chat.Id, "posted after leave");
    // Assert (sustained): the new entry's lid never appears in the items
    await Task.Delay(1000);
    var items = await chatUI.GetChatItems(chat.Id, query, 0, CT);
    LeafEntryLids(items).Should().Equal(frozenLeafLids);
}
```

Test 3 — close keeps the rendered items and the render key:

```csharp
[Fact]
public async Task CloseKeepsRenderedItemsAndKey()
{
    // Arrange: joined -> summary (IsExpandedByDefault = false, i.e. tier 3) -> leave (both authors
    // SetParticipation(chat.Id, authorId, false)) -> capture frozen leaf lids + the block's RenderKey
    // Act: drive the close the same way LiveSessionsTest's finalize tests do (FinalizeSession after
    // participants left; check that file for the exact choreography and mirror it)
    await liveBackend.FinalizeSession(chat.Id, CT);
    // Assert: same leaves, same single block, same RenderKey (ConversationBlock:{V}) - and the
    // conversation persisted (Conversations.GetTile now returns it) yet nothing collapsed
    await ComputedTest.When(async ct => {
        var live = await liveBackend.GetState(chat.Id, ct);
        live.Should().BeNull();
        var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
        LeafEntryLids(items).Should().Equal(frozenLeafLids);
        var block = items.Items.OfType<ExpandedConversationMessage>().Single();
        ((IVirtualListItem)block).RenderKey.Should().Be(frozenRenderKey);
    }, TimeSpan.FromSeconds(15));
}
```

Test 4 — the manual toggle dismisses the overlay:

```csharp
[Fact]
public async Task ToggleAfterCloseCollapsesBlock()
{
    // Arrange: run the close flow of the previous test, grab the block's conversation id
    chatUI.ToggleExpandConversation(blockConversationId);
    await ComputedTest.When(async ct => {
        var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
        // Overlay gone: the materialized conversation renders as a plain collapsed card,
        // its entries no longer in the list
        items.Items.OfType<ExpandedConversationMessage>().Should().BeEmpty();
        items.Items.OfType<ConversationMessage>().Should().ContainSingle();
    }, TimeSpan.FromSeconds(10));
}
```

Test 5 — the freshness lag defers a mid-call fold:

```csharp
[Fact]
public async Task FoldLagDefersMidCallFold()
{
    // Arrange: joined viewer, first summary latched (fold end = v + 3), then two more entries posted
    var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
    liveBlockUI.FoldLag = TimeSpan.FromSeconds(2);
    var beforeLids = LeafEntryLids(await chatUI.GetChatItems(chat.Id, query, 0, CT));

    // Act: a summary pass advances coverage over the two new entries
    await liveBackend.UpdateSummary(chat.Id,
        new LiveSessionSummary {
            Title = "Latch", Description = "d2", Summary = "s2",
            EndEntryLid = v + 4, MessageCount = 5,
        }, CT);

    // Assert 1 (sustained): within the lag window nothing folds
    await Task.Delay(700);
    LeafEntryLids(await chatUI.GetChatItems(chat.Id, query, 0, CT)).Should().Equal(beforeLids);

    // Assert 2: after the lag the two covered entries fold
    await ComputedTest.When(async ct => {
        var lids = LeafEntryLids(await chatUI.GetChatItems(chat.Id, query, 0, ct));
        lids.Should().NotContain(v + 3);
        lids.Should().NotContain(v + 4);
    }, TimeSpan.FromSeconds(10));
}
```

Test 6 — the viewport guard defers a fold while the entries are on screen (spec: "fold defers while entries are visible and completes after they leave the viewport"):

```csharp
[Fact]
public async Task ViewportGuardDefersFoldWhileVisible()
{
    // Arrange: as test 5, but FoldLag = zero and the about-to-fold entries marked visible
    var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
    liveBlockUI.FoldLag = TimeSpan.Zero;
    chatUI.ItemVisibility.Value = new ChatViewItemVisibility(
        chat.Id,
        new HashSet<ChatMessageKey> { new(v + 3, ChatMessageKind.None), new(v + 4, ChatMessageKind.None) },
        false);
    var beforeLids = LeafEntryLids(await chatUI.GetChatItems(chat.Id, query, 0, CT));

    // Act 1: summary covers the visible entries - the guard must hold the fold
    await liveBackend.UpdateSummary(chat.Id,
        new LiveSessionSummary {
            Title = "Latch", Description = "d2", Summary = "s2",
            EndEntryLid = v + 4, MessageCount = 5,
        }, CT);
    await Task.Delay(700);
    LeafEntryLids(await chatUI.GetChatItems(chat.Id, query, 0, CT)).Should().Equal(beforeLids);

    // Act 2: the entries scroll out of view - the fold completes
    chatUI.ItemVisibility.Value = ChatViewItemVisibility.Empty;
    await ComputedTest.When(async ct => {
        var lids = LeafEntryLids(await chatUI.GetChatItems(chat.Id, query, 0, ct));
        lids.Should().NotContain(v + 3);
        lids.Should().NotContain(v + 4);
    }, TimeSpan.FromSeconds(10));
}
```

(`ChatMessageKey` construction: check its actual ctor/parse shape in `src/dotnet/UI.Blazor.App` and build the visible-key set the way `ChatViewItemVisibility` expects; `ChatViewItemVisibility.Empty` resetting works because `AmIInLiveConversation`-driven recomputes don't depend on it — the governor loop does.)

Helper for all tests:

```csharp
private static List<long> LeafEntryLids(ChatItems items)
    => items.Items
        .SelectMany(i => i is ExpandedConversationMessage b ? b.GetLeafMessages() : [i])
        .OfType<ChatEntryMessage>()
        .Where(m => m.Kind == ChatMessageKind.None)
        .Select(m => m.Id)
        .ToList();
```

(Adjust the helper to the actual `ChatMessage`/leaf API in the file — `GetLeafMessages()` exists per the current tests.)

- [ ] **Step 2: Run the new tests — expect FAIL** (leave/close currently collapse the view)

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~LiveConversationDisplayTest" 2>&1 | tail -20`

- [ ] **Step 3: Implement the tile-builder changes**

3a. `ConversationViewState` (L10-15) — rename the joined field, add the overlay-derived fields:

```csharp
public sealed record ConversationViewState(
    bool ShowConversations,
    IImmutableSet<ConversationId> ExpandedConversations,
    Range<long> HiddenLiveTailRange,
    ConversationId? LiveBlockConversationId,
    Range<long> LiveFoldRange,
    ConversationId? MaterializedBlockId);
```

3b. `GetChatItemsInternal` (~L59-76) — read the governor and derive the block view once:

```csharp
var rawLive = await Hub.LiveSessionUI.GetState(chatId, cancellationToken).ConfigureAwait(false);
var blockState = await Hub.LiveBlockUI.GetBlockState(chatId, cancellationToken).ConfigureAwait(false);
var overlay = blockState.Overlay;

// The governed boundary replaces the raw fold end: folds advance only FoldLag after the summary
// pass that produced them, and never across the viewer's viewport (LiveBlockUI owns both rules).
var liveFoldRange = rawLive is { SessionStartedAt: not null }
    && blockState.FoldBoundaryLid > rawLive.EffectiveVisibleStartLid
        ? new Range<long>(
            rawLive.EffectiveVisibleStartLid,
            Math.Min(blockState.FoldBoundaryLid, rawLive.EndEntryLid + 1))
        : default;

ConversationId? liveBlockId;
Range<long> liveBlockFoldRange;
ConversationId? materializedBlockId = null;
if (overlay != null) {
    liveBlockId = overlay.RenderId;
    liveBlockFoldRange = overlay.FoldRange;
    materializedBlockId = overlay.MaterializedId;
}
else {
    liveBlockId = joinedLiveConversation?.Id;
    liveBlockFoldRange = liveFoldRange;
}

var hiddenLiveTailRange = overlay != null
    ? overlay.HiddenTailRange
    : liveConversation is { } liveConv && !amInLiveConversation
        ? new Range<long>(liveConv.EndEntryLid + 1, long.MaxValue)
        : default;
```

(The existing `rawLive`/`liveFoldRange` block at L69-76 is replaced by the above; the `hiddenLiveTailRange` computation at L64-68 moves below it.)

3c. Keep a leaver's own expansion (L146-148) — the overlay case must not force-collapse:

```csharp
if (liveConversation != null && !amInLiveConversation && overlay == null)
    expandedConversations = expandedConversations.Remove(liveConversation.Id);
```

3d. Both `ConversationViewState` constructions (L226 and L284):

```csharp
new ConversationViewState(showConversations, expandedConversations, hiddenLiveTailRange, liveBlockId, liveBlockFoldRange, materializedBlockId)
```

3e. Grouping call sites (L344-347) — group whenever a block id exists, bound the block after close:

```csharp
if (expandedConversations.Count == 0 && liveBlockId == null)
    return new ChatItems(groupedItems, hasMoreBefore, hasMoreAfter);

var liveBlockRange = liveBlockId is { } lbId
    ? new Range<long>(lbId.StartEntryLid, overlay?.BlockEndLid ?? long.MaxValue)
    : default;
var groupedTiles = GroupExpandedConversations(groupedItems, liveBlockId, liveBlockRange);
```

3f. `GroupExpandedConversations` (L844+) — signature `(IReadOnlyList<ChatMessage> messages, ConversationId? liveBlockId, Range<long> liveBlockRange)`; replace `joinedLive` usages:

```csharp
var isLiveBlock = liveBlockId != null && blockConversation?.Id == liveBlockId;
var belongs = blockConversation != null
    && (conversation != null
        ? conversation.Id == blockConversation.Id
        : isLiveBlock
            ? liveBlockRange.Contains(item.Id)
            : blockConversation.EntryLidRange.Contains(item.Id));
...
var startsBlock = conversation != null
    && (item is not ConversationMessage || conversation.Id == liveBlockId);
```

(`liveBlockRange.End == long.MaxValue` while live keeps today's `[V, ∞)` behavior including the trailing `AudioRecordingMessage` at `long.MaxValue`; note `Range.Contains(long.MaxValue)` is false for a `MaxValue`-ended range — if the trailing audio-recording message must stay inside while live, keep the old `item.Id >= liveStartLid` form for the `End == long.MaxValue` case: `liveBlockRange.End == long.MaxValue ? item.Id >= liveBlockRange.Start : liveBlockRange.Contains(item.Id)`.)

3g. `GetTile` — destructure (L499) with the new names, then after `conversations` is computed (L514-516) substitute the materialized conversation so the card keeps its `@key`:

```csharp
var (showConversations, expandedConversations, hiddenLiveTailRange, liveBlockId, liveFoldRange, materializedBlockId) = conversationView;
...
if (materializedBlockId is { } matBlockId && liveBlockId is { } renderBlockId)
    // The closed live block keeps rendering under its live-era id, so the VirtualList @key
    // (ConversationBlock:{V}) and every rendered row survive the close unchanged.
    conversations = conversations
        .Select(c => c.Id == matBlockId ? c with { Id = renderBlockId } : c)
        .ToArray();
```

Then rename `joinedLiveId` → `liveBlockId` at its remaining uses: the skip-range select (L521), the merge filter (L583), `shouldEmitCard` (L590), the forced block start (L614), and the header/footer suppressions (L669, L725). No logic change at those sites beyond the rename — the overlay flows through the same paths the joined live block already uses.

3h. `ConversationMessageView.razor` `ComputeState` (~L199-201) — the expand icon reflects the governed fold, not the raw one:

```csharp
var blockState = await Hub.LiveBlockUI.GetBlockState(chatId, cancellationToken);
var hasFoldedEntries = !isVoiceOnly
    && rawLive is { SessionStartedAt: not null }
    && blockState.FoldBoundaryLid > rawLive.EffectiveVisibleStartLid;
```

(Keep the rest of the existing gating; only the `EndEntryLid`-based term is replaced by the boundary term.)

- [ ] **Step 4: Build, run the whole display-test class + existing suites**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -5` — expect `0 Error(s)`.
Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~LiveConversationDisplayTest" 2>&1 | tail -10` — expect all facts (old three + new five) PASS. The three pre-existing facts are the regression net for the renames — if `ShouldNotDuplicateJoinedLiveCardAcrossTiles` or `ShouldFlagFirstEntryOfAnExpandedConversation` fail, the rename or the substitution broke a path; fix before proceeding.
Run: `dotnet test tests/Chat.UI.Blazor.UnitTests 2>&1 | tail -5` — expect PASS.

- [ ] **Step 5: Commit**

```bash
git add -A src/dotnet/UI.Blazor.App tests/Chat.UI.Blazor.IntegrationTests
git commit -m "feat(live): governed fold + leave/close overlay in the tile builder"
```

---

### Task 4: Card size transitions (CSS + minimal razor)

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageView.razor` (summary block, ~L43-49)
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/conversation.css` (live-card styles, ~L34-87)
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/LastEntriesPreview.razor` (per-entry `@key` if missing)

**Interfaces:** none — presentation only.

- [ ] **Step 1: Wrap the summary for the height transition** (razor ~L43-49)

Replace:

```razor
@if (hasDescription) {
    <div class="c-lc-summary">
        <CascadingValue Value="@_fakeEntry" IsFixed="true">
            <MarkupView Markup="@m.Description.Markup"/>
        </CascadingValue>
    </div>
}
```

with:

```razor
@if (hasDescription) {
    <div class="c-lc-summary-box">
        <div class="c-lc-summary" @key="@m.Description.Text">
            <CascadingValue Value="@_fakeEntry" IsFixed="true">
                <MarkupView Markup="@m.Description.Markup"/>
            </CascadingValue>
        </div>
    </div>
}
```

(The `@key` recreates the inner element when the summary text changes, so the fade-in animation replays on every update.)

- [ ] **Step 2: Add the CSS** (in `conversation.css`, after the `.c-lc-summary` rules ~L59-65)

```css
.c-live-card .c-lc-summary-box {
    display: grid;
    grid-template-rows: 1fr;
    transition: grid-template-rows 200ms ease;
}
@starting-style {
    .c-live-card .c-lc-summary-box {
        grid-template-rows: 0fr;
    }
}
.c-live-card .c-lc-summary {
    overflow: hidden;
    animation: lc-fade-in 200ms ease;
}
@keyframes lc-fade-in {
    from { opacity: 0; }
}
```

Keep the existing `@apply text-01 text-caption-4 leading-4;` on `.c-lc-summary` intact — only add `overflow: hidden;` and the animation to that rule.

- [ ] **Step 3: Fade the in-card tail entries**

In `LastEntriesPreview.razor`, ensure each rendered entry element carries `@key="@entry.Entry.Id"` (add if missing). In `conversation.css` (or the file holding `.last-entries-preview` styles — find it with `rg -n "last-entries-preview" src/dotnet`), add:

```css
.last-entries-preview > * {
    animation: lc-fade-in 200ms ease;
}
```

If `.last-entries-preview` styles live in a different css file, put this rule there and duplicate the `@keyframes lc-fade-in` block if needed.

- [ ] **Step 4: Validate the frontend build**

Run: `npm run build:Verify 2>&1 | tail -10`
Expected: build succeeds, no tsc/eslint errors. (If `/server-loop` is running, trigger its rebuild instead per project rules.)

- [ ] **Step 5: Commit**

```bash
git add -A src/dotnet/UI.Blazor.App
git commit -m "style(live): animate card summary appear/update and tail preview swaps"
```

---

### Task 5: Full verification — builds, suites, browser pass

**Files:** none created; fixes go where the failures point.

- [ ] **Step 1: Full builds**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -5` — expect `0 Error(s)`.
Run: `npm run build:Verify 2>&1 | tail -5` — expect success.

- [ ] **Step 2: Full test suites**

Run each, expect `Failed: 0`:

```bash
dotnet test tests/Chat.UI.Blazor.UnitTests 2>&1 | tail -3
dotnet test tests/Chat.UI.Blazor.IntegrationTests 2>&1 | tail -3
dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessions|FullyQualifiedName~LiveConversation" 2>&1 | tail -3
```

The third run guards the untouched server side — it must be green without changes; if it is not, something leaked outside the client scope, which this plan forbids.

- [ ] **Step 3: Browser pass (mandatory — spec §3 makes it part of the design)**

Start the server (`/server-start --watch`, or use the running `run-watch`/`server-loop` per project rules), then use the `/debug-ui` skill (chrome-devtools MCP, two signed-in sessions) and `/virtual-list-debug` (consistency checker) to verify, in a group chat:

1. **Hang-up freeze:** two devices talk (≥1 summary landed), device A hangs up → A's viewport shows the identical block (card restyles joined→unjoined only); messages posted by B afterwards do NOT appear for A.
2. **Close freeze:** both hang up, wait for finalize (~15-30s) → A's and B's viewports unchanged (card restyle only, no collapse, no re-key jump); a third device opening the chat fresh sees the tiered default (collapsed for tier 3).
3. **Mid-call fold calm:** during a long talk, scroll up into the summarized-but-unfolded region → entries never fold while visible; scroll back down → they fold within ~FoldLag with no viewport motion (watch the `/virtual-list-debug` checker: zero violations).
4. **Card animation:** watch the first summary land on the unjoined card → height animates ~200ms, text fades in; summary updates crossfade. If the VirtualList checker reports jumps during the animation, apply the spec's fallback: drop the `grid-template-rows` transition (keep the fade) and re-verify.
5. **Toggle:** after close, collapse the frozen block via its arrow → it becomes a plain conversation card; expand again → normal expanded conversation.

Record what you saw (pass/fail per item) in the task summary; do not claim success without having run this pass.

- [ ] **Step 4: Final commit (if the browser pass forced fixes)**

```bash
git add -A
git commit -m "fix(live): browser-pass adjustments for fold/close stability"
```

Do **not** push and do **not** merge — report completion and wait for the user.

---

## Risks & mitigations

- **Governor loop vs. first render race:** the initial latch happens synchronously inside `GetBlockState` (isolated read), so the first tile build never sees a zero boundary for an already-summarized session.
- **Scroll-driven recompute storms:** `GetGovernorInputs` depends on `ItemVisibility`, but only the governor loop consumes it; `GetBlockState` invalidates solely when the per-chat `MutableState` value actually changes.
- **`@key` stability at close:** the materialized conversation is re-keyed to the live render id (`ConversationBlock:{V}`) while the overlay lives; the id changes only when the overlay is dismissed (toggle/navigation) — a user action or a fresh render.
- **Stale overlay after app restart:** overlays are in-memory only; a restart renders shipped defaults — by design.
- **`Conversation with { Id = ... }`:** requires `Conversation.Id` to be an init/settable record property; if it is positional/computed differently, construct the copy the way `Conversation` supports (check `src/dotnet/Api/Chat/Conversation.cs` first).
- **`@starting-style` support:** the codebase already uses `transition-behavior: allow-discrete` (same browser baseline); if the css build chokes on `@starting-style`, fall back to the `lc-fade-in` animation only.
- **Time-based test flakiness:** integration tests shrink `FoldLag` via the internal instance field and assert through `ComputedTest.When` windows, never bare `Task.Delay` asserts (except the sustained-absence check, which is inherently a delay probe).
