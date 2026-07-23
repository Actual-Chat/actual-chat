# Live-Session Dialing State Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A call creates its live session while ringing (so the Call tab works) but shows **no** live conversation block until the first invitee answers.

**Architecture:** Add an explicit `LiveSessionKind.Dialing` phase. A fresh call enters Dialing with `SessionStartedAt == null`; the first `AcceptCall` latches it to `Kind == Call` with `SessionStartedAt` set. Because `SessionStartedAt != null` is already the codebase-wide "there is a live conversation" signal (~20 gates), keeping it null while dialing suppresses the block everywhere for free. `Get` (the call projection) is relaxed to still surface a dialing call for the Call tab.

**Tech Stack:** C# / .NET, ActualLab.Fusion, Redis-backed `LiveSessionState`, xUnit + FluentAssertions integration tests (`SharedAppHostTestBase`).

**Spec:** `docs/superpowers/specs/2026-07-23-live-session-dialing-state-design.md`

## Global Constraints

- **Read `docs/CODING_STYLE.md` before writing C#.** No `Async` suffix on async methods; no XML docs on members; comments only for non-obvious constraints; ≤120 columns; mixed brace style per surrounding code.
- **Invariant (must hold everywhere):** for a call, `Kind == Dialing ⟺ SessionStartedAt == null`. The two fields are written together only in `StartCall` (enter Dialing) and `AcceptCall` (latch), plus the ambient-latch backstop.
- **Latch is monotonic:** once `SessionStartedAt` is set, a session never returns to Dialing. Promoting an already-latched ambient session to a call yields `Kind == Call`, not `Dialing`.
- **Latch trigger:** the first invitee to accept (`AcceptCall`). Decline / cancel / no-answer never latch → no block, no notification.
- **Calls don't send the "voice chat started" conversation banner** (they ring); only ambient sessions banner.
- **`LiveSessionKind` is a Redis-persisted enum, not a DB column** — adding a value needs no migration.
- **Build:** `dotnet build ActualChat.CI.slnf`, or trigger the running `dotnet watch` (poll `tmp/watch-dotnet.log` for `C# and Razor changes applied` / `error`). Do not start/restart the server yourself — the user's watch owns it.
- **Test project:** `tests/Chat.IntegrationTests/LiveSessionsTest.cs` (drives `ILiveSessionsBackend` directly). Run a single test with `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~<TestName>"`.

---

### Task 1: `Dialing` enum, state helpers, `StartCall` enters Dialing, `Get` surfaces it

**Files:**
- Modify: `src/dotnet/Api/Live/LiveSessionKind.cs`
- Modify: `src/dotnet/Api/Live/LiveSessionState.cs` (add computed helpers after the existing `ConversationId` computed, ~line 63)
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs` (`StartCall` ~L477-486; `Get` early-out ~L131 and projection `Conversation` ~L199)
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs` (`StartCallRingsInvitee` ~L597; `StartCallPromotesExistingSession` ~L754 stays green unchanged)

**Interfaces:**
- Produces: `LiveSessionKind.Dialing = 2`; `LiveSessionState.IsCall => Kind is Call or Dialing`; `LiveSessionState.IsDialing => Kind == Dialing`. Task 2/3/4 consume these.

- [ ] **Step 1: Update the `StartCallRingsInvitee` test to the new dialing contract**

In `tests/Chat.IntegrationTests/LiveSessionsTest.cs`, replace the assertion block in `StartCallRingsInvitee` (currently lines ~614-621):

```csharp
        // assert — a fresh call is Dialing: the session exists (Call tab works) but no live conversation
        // is surfaced yet, so SessionStartedAt stays null until someone answers.
        var state = await backend.GetState(chatId, default);
        state.Should().NotBeNull();
        state!.Kind.Should().Be(LiveSessionKind.Dialing);
        state.SessionStartedAt.Should().BeNull();
        // the Call tab still gets a projection while dialing, with the ring visible and no conversation
        var live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.Conversation.Should().BeNull();
        live.Invites.Should().ContainSingle(i =>
            i.InviteeId == aliceAuthor.Id && i.Status == CallInviteStatus.Ringing);
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~StartCallRingsInvitee"`
Expected: FAIL — `Kind` is `Call` (not `Dialing`) and `SessionStartedAt` is not null (old `StartCall` behavior).

- [ ] **Step 3: Add the `Dialing` enum value**

`src/dotnet/Api/Live/LiveSessionKind.cs`:

```csharp
namespace ActualChat.Live;

public enum LiveSessionKind
{
    Ambient = 0,
    Call = 1,
    Dialing = 2,
}
```

- [ ] **Step 4: Add `IsCall` / `IsDialing` computed helpers**

`src/dotnet/Api/Live/LiveSessionState.cs`, immediately after the `ConversationId` computed property (the last `[IgnoreDataMember, MemoryPackIgnore, IgnoreMember]` member, ~line 63):

```csharp
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsCall => Kind is LiveSessionKind.Call or LiveSessionKind.Dialing;
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsDialing => Kind == LiveSessionKind.Dialing;
```

- [ ] **Step 5: `StartCall` — enter Dialing instead of latching**

`src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`, the `with` block in `StartCall` (~L477-486). Replace the two comment lines and the `with` initializer as follows (leave `lidRangeEnd`/`startEntryLid` above it unchanged — `lidRangeEnd` is still used for `startEntryLid`):

```csharp
            // A fresh call is Dialing: the session exists (for the Call tab and the ring) but no live
            // conversation is surfaced until someone answers, so SessionStartedAt stays null. Promoting an
            // already-latched (ambient) session keeps it connected so its block and ring/close paths persist.
            state = (state ?? new LiveSessionState { ChatId = chatId, StartEntryLid = startEntryLid }) with {
                EndEntryLid = state?.EndEntryLid ?? startEntryLid,
                StartedAt = state?.StartedAt ?? now,
                SessionStartedAt = state?.SessionStartedAt,
                VisibleStartLid = state?.VisibleStartLid ?? 0,
                AuthorIds = state?.AuthorIds is { Count: > 0 } ids ? ids : [callerAuthorId],
                Host = callerAuthorId,
                Kind = state?.SessionStartedAt is not null ? LiveSessionKind.Call : LiveSessionKind.Dialing,
                Version = VersionGenerator.NextVersion(state?.Version ?? 0),
            };
```

- [ ] **Step 6: `Get` — surface a dialing call, with no conversation**

`src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`. In `Get`, replace the early-out (~L131):

```csharp
        if (state.SessionStartedAt is null && !state.IsCall)
            return null;
```

and in the returned `LiveSession` (~L199), make the conversation absent while dialing:

```csharp
            Conversation = state.IsDialing ? null : state.ToConversation(),
```

- [ ] **Step 7: Run the updated test — verify it passes**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~StartCallRingsInvitee"`
Expected: PASS.

- [ ] **Step 8: Fix the promotion test (unlatched → Dialing) and add a latched-promotion test**

`StartCallPromotesExistingSession` registers only one streamer, so its ambient session is **unlatched** (`SessionStartedAt == null`). Promoting an unlatched session is now Dialing (a solo recorder ringing others is not a connected conversation). Replace that test's assertion block (~L773-776) with:

```csharp
        // assert — promoting an unlatched (solo) ambient session gives a Dialing call: ring/close paths
        // apply (via IsCall) but no block is surfaced until someone answers.
        var state = await backend.GetState(chatId, default);
        state!.Kind.Should().Be(LiveSessionKind.Dialing);
        state.SessionStartedAt.Should().BeNull();
        state.Host.Should().Be(bobAuthor.Id);
```

Then add a test for the latched case (an already-2-party ambient session promotes to a connected `Call`, keeping its block), after `StartCallPromotesExistingSession`:

```csharp
    [Fact]
    public async Task StartCallOnLatchedSessionStaysConnected()
    {
        // arrange — a 2-party ambient session is already latched (block visible) when a call starts
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(true);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, bobAuthor!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, aliceAuthor!.Id, null, true, default);
        var latched = await backend.GetState(chatId, default);
        latched!.SessionStartedAt.Should().NotBeNull("two streamers latched the ambient session");
        var startedAt = latched.SessionStartedAt;

        // act — Bob rings a third-party author id while that session is live
        await backend.StartCall(
            chatId, bobAuthor.Id, new[] { AuthorId.New(chatId, 777_055) }.ToApiArray(), false, default);

        // assert — monotonic: it stays a connected Call with its latch preserved (block stays)
        var state = await backend.GetState(chatId, default);
        state!.Kind.Should().Be(LiveSessionKind.Call);
        state.SessionStartedAt.Should().Be(startedAt);
    }
```

- [ ] **Step 9: Run both promotion tests — verify they pass**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~StartCallPromotesExistingSession|FullyQualifiedName~StartCallOnLatchedSessionStaysConnected"`
Expected: PASS — unlatched promotion → `Dialing`; latched promotion → `Call` with `SessionStartedAt` unchanged.

- [ ] **Step 10: Commit**

```bash
git add src/dotnet/Api/Live/LiveSessionKind.cs src/dotnet/Api/Live/LiveSessionState.cs \
  src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs \
  tests/Chat.IntegrationTests/LiveSessionsTest.cs
git commit -m "feat(live): calls enter a Dialing phase with no live conversation block"
```

---

### Task 2: `AcceptCall` latches Dialing → Connected

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs` (`AcceptCall` ~L505-524)
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs` (`AcceptCallJoinsCall` ~L625; add `AcceptLatchesDialingCallToConnected`)

**Interfaces:**
- Consumes: `LiveSessionKind.Dialing`, `LiveSessionState.IsDialing` (Task 1).
- Produces: after the first `AcceptCall`, `Kind == Call`, `SessionStartedAt` set, `VisibleStartLid` = chat end at answer, invitee in `AuthorIds`.

- [ ] **Step 1: Extend `AcceptCallJoinsCall` and add the latch test**

In `tests/Chat.IntegrationTests/LiveSessionsTest.cs`, add to `AcceptCallJoinsCall` (after its existing assertions, ~L647) assertions that the call is now connected:

```csharp
        // the answer latches the dialing call to Connected: block now surfaced
        var state = await backend.GetState(chatId, default);
        state!.Kind.Should().Be(LiveSessionKind.Call);
        state.SessionStartedAt.Should().NotBeNull();
        state.AuthorIds.Should().Contain(aliceAuthor.Id);
```

Then add a dedicated test after `AcceptCallJoinsCall`. `SessionStartedAt` is the backend-level block signal (`ToConversation` is only surfaced when it is non-null — see `LiveSessionUI.GetConversation`), so the test asserts it directly rather than the client `GetConversation` (which is on `LiveSessionUI`, not `ILiveSessionsBackend`):

```csharp
    [Fact]
    public async Task AcceptLatchesDialingCallToConnected()
    {
        // arrange — Bob dials Alice; while ringing the session is Dialing (no block: SessionStartedAt null)
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var chatsBackend = bob.AppServices.GetRequiredService<IChatsBackend>();
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // assert — dialing: no live conversation is surfaced (SessionStartedAt gates every block path)
        (await backend.GetState(chatId, default))!.SessionStartedAt.Should().BeNull();

        // act — Alice answers
        var chatEnd = (await chatsBackend.GetLidRange(chatId, false, default)).End;
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);

        // assert — latched: Connected, block surfaced (SessionStartedAt set), VisibleStartLid = answer's chat end
        var state = await backend.GetState(chatId, default);
        state!.Kind.Should().Be(LiveSessionKind.Call);
        state.SessionStartedAt.Should().NotBeNull();
        state.VisibleStartLid.Should().Be(chatEnd);
        state.AuthorIds.Should().Contain(aliceAuthor.Id);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~AcceptLatchesDialingCallToConnected|FullyQualifiedName~AcceptCallJoinsCall"`
Expected: FAIL — `AcceptCall` does not set `Kind`/`SessionStartedAt`/`VisibleStartLid` yet, so the call stays Dialing.

- [ ] **Step 3: Implement the latch in `AcceptCall`**

`src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`, replace the body of `AcceptCall` (~L505-524) with:

```csharp
    public virtual async Task AcceptCall(ChatId chatId, AuthorId inviteeAuthorId, CancellationToken cancellationToken)
    {
        ConversationId? conversationId = null;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
            if (invite is not { Status: CallInviteStatus.Ringing })
                return;

            var now = Clocks.SystemClock.Now;
            await _invites.Set(chatId.Value, inviteeAuthorId.Value,
                    invite with { Status = CallInviteStatus.Accepted, RespondedAt = now })
                .ConfigureAwait(false);
            // Answering joins the call - register now so it's two-party and stays alive before the client streams.
            await EnsureParticipant(chatId, inviteeAuthorId).ConfigureAwait(false);

            var state = await SafeGet(chatId).ConfigureAwait(false);
            if (state is { SessionStartedAt: null }) {
                // The first answer latches a dialing call to Connected: it's now a live conversation, so
                // surface the block from the chat end at answer time and make it genuinely two-party.
                var visibleStartLid = (await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false)).End;
                var authorIds = state.AuthorIds.Contains(inviteeAuthorId)
                    ? state.AuthorIds
                    : [..state.AuthorIds, inviteeAuthorId];
                state = state with {
                    Kind = LiveSessionKind.Call,
                    SessionStartedAt = now,
                    VisibleStartLid = visibleStartLid,
                    AuthorIds = authorIds,
                    Version = VersionGenerator.NextVersion(state.Version),
                };
                await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
            }
            conversationId = state?.ConversationId;
            InvalidateState(chatId);
        }
        if (conversationId is { } cid)
            await DismissRing(cid, [inviteeAuthorId], cancellationToken).ConfigureAwait(false);
    }
```

- [ ] **Step 4: Run the tests — verify they pass**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~AcceptLatchesDialingCallToConnected|FullyQualifiedName~AcceptCallJoinsCall"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs tests/Chat.IntegrationTests/LiveSessionsTest.cs
git commit -m "feat(live): first AcceptCall latches a dialing call to Connected"
```

---

### Task 3: Ring/close guards honor Dialing; ambient-latch keeps the invariant

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs` (ring-timeout guard ~L118; ambient latch ~L290-302; `CloseNow` guard ~L821; `CloseAndMaterialize` call-branch ~L867)
- Modify: `src/dotnet/Chat.Service/Flows/LiveConversationSummaryFlow.cs` (guard ~L47)
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs` (add `StreamBeforeAcceptLatchesDialingCallToConnected`; `AllDeclinedEndsCall` / `CancelCallEndsTheCall` stay green)

**Interfaces:**
- Consumes: `LiveSessionState.IsCall`, `LiveSessionKind.Dialing` (Task 1).

- [ ] **Step 1: Add the backstop-latch test (stream before a formal accept)**

In `tests/Chat.IntegrationTests/LiveSessionsTest.cs`, add:

```csharp
    [Fact]
    public async Task StreamBeforeAcceptLatchesDialingCallToConnected()
    {
        // A dialing call reaching the 2-party stream latch (both parties stream before a formal Accept)
        // must become Connected - never left as Dialing with SessionStartedAt set (invariant).
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);
        (await backend.GetState(chatId, default))!.Kind.Should().Be(LiveSessionKind.Dialing);

        // act — both parties stream (no explicit AcceptCall)
        await backend.OnStreamRegistered(chatId, bobAuthor.Id, null, false, default);
        await backend.OnStreamRegistered(chatId, aliceAuthor.Id, null, false, default);

        // assert — invariant holds: latched → Connected, not Dialing-with-SessionStartedAt
        var state = await backend.GetState(chatId, default);
        state!.SessionStartedAt.Should().NotBeNull();
        state.Kind.Should().Be(LiveSessionKind.Call);
    }
```

- [ ] **Step 2: Run it — verify it fails**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~StreamBeforeAcceptLatchesDialingCallToConnected"`
Expected: FAIL — the ambient latch sets `SessionStartedAt` but leaves `Kind == Dialing`, violating the invariant.

- [ ] **Step 3: Ambient latch — keep the invariant and gate the banner**

`src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`, the latch block (~L290-301). Replace it with:

```csharp
        if (state.SessionStartedAt is null && state.AuthorIds.Count >= 2) {
            var visibleStartLid = (await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false)).End;
            state = state with {
                // A dialing call latching here (streamed before a formal accept) becomes a connected call,
                // never a Dialing session with SessionStartedAt set.
                Kind = state.Kind == LiveSessionKind.Dialing ? LiveSessionKind.Call : state.Kind,
                SessionStartedAt = now,
                VisibleStartLid = visibleStartLid,
                Version = VersionGenerator.NextVersion(state.Version),
            };
            // Calls announce themselves by ringing, not a conversation banner; only ambient sessions banner.
            if (state.Kind == LiveSessionKind.Ambient)
                await EnqueueLiveNotification(
                    state, ConversationNotificationPhase.Started, "Voice chat started", cancellationToken)
                    .ConfigureAwait(false);
        }
```

- [ ] **Step 4: Ring-timeout and close guards — treat Dialing as a call**

Same file. Ring-timeout guard in `GetState` (~L118):

```csharp
        if (state.IsCall && await HasStaleRinging(chatId).ConfigureAwait(false))
            _ = ExpireRings(chatId);
```

`CloseAndMaterialize` call-branch (~L867):

```csharp
        if (state.IsCall) {
```

`CloseNow` grace decision (~L821) — a latched transcription session that hands its close to the flow is only ever ambient (a latched call is `Kind == Call`), so state the positive kind for clarity and to keep dialing out:

```csharp
            if (state is { TranscriptionOn: true, SessionStartedAt: not null, Kind: LiveSessionKind.Ambient }) {
```

- [ ] **Step 5: Summary-flow guard — same clarity change**

`src/dotnet/Chat.Service/Flows/LiveConversationSummaryFlow.cs` (~L47):

```csharp
            if (live is { TranscriptionOn: true, SessionStartedAt: not null, Kind: LiveSessionKind.Ambient })
```

- [ ] **Step 6: Run the new test plus the abandoned-close regressions**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~StreamBeforeAcceptLatchesDialingCallToConnected|FullyQualifiedName~AllDeclinedEndsCall|FullyQualifiedName~CancelCallEndsTheCall"`
Expected: PASS — the backstop latches to Connected; a fully-declined or cancelled dialing call still tears down (routes through `CloseAndMaterialize`'s `IsCall` branch, dismisses rings, closes).

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs \
  src/dotnet/Chat.Service/Flows/LiveConversationSummaryFlow.cs \
  tests/Chat.IntegrationTests/LiveSessionsTest.cs
git commit -m "fix(live): ring/close guards honor Dialing; ambient latch keeps the invariant"
```

---

### Task 4: Call tab shows "Dialing…"

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/RightPanel/CallList.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/RightPanel/call-list.css` (only if a new class is introduced; otherwise skip)

**Interfaces:**
- Consumes: `LiveSession.Kind` (already on the projection) and `LiveSessionKind.Dialing` (Task 1). The projection carries `Kind = state.Kind`, so no new plumbing is needed.

- [ ] **Step 1: Render a "Dialing…" state when the call is dialing**

`src/dotnet/UI.Blazor.App/Components/RightPanel/CallList.razor`. At the top of the markup, after `var live = m.Session;` and the null guard (~L6-10), the `live` is non-null for a dialing call (Task 1 relaxed `Get`). Add a dialing banner just inside `<div class="call-list-tab">` (before `<div class="c-rules">`):

```razor
    @if (live.Kind == LiveSessionKind.Dialing) {
        <div class="c-dialing">Dialing…</div>
    }
```

The ringing invitees already render from `live.Invites` in the existing member/invite sections, so no further markup is required. If `call-list.css` exists next to the component, add a minimal rule (padding + muted color) mirroring `.c-empty`; otherwise the default text styling is acceptable and this step needs no CSS.

- [ ] **Step 2: Validate the frontend build**

If the user's `dotnet watch` is running, edit and watch `tmp/watch-web.log` / `tmp/watch-dotnet.log` for `C# and Razor changes applied` (no `error`). Otherwise run:

Run: `npm run build:Verify`
Expected: clean (no tsc/eslint/build errors). A `.razor`-only change compiles via the .NET build; `build:Verify` is required only if CSS/TS changed.

- [ ] **Step 3: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/RightPanel/CallList.razor
git commit -m "feat(live): Call tab shows a Dialing… state while a call rings"
```

---

## Verification (after all tasks)

1. `dotnet build ActualChat.CI.slnf` (or the watch loop) — clean.
2. `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest"` — all green (existing + new/updated: `StartCallRingsInvitee`, `AcceptCallJoinsCall`, `AcceptLatchesDialingCallToConnected`, `StreamBeforeAcceptLatchesDialingCallToConnected`, `StartCallPromotesExistingSession` (now → Dialing), `StartCallOnLatchedSessionStaysConnected`, `AllDeclinedEndsCall`, `CancelCallEndsTheCall`, `LeaveCallEndsCallBelowTwo`).
3. Manual two-device pass (peer chat): Bob dials Alice → **Bob sees no conversation block**, Call tab shows "Dialing…", Alice gets the incoming-call ring → Alice accepts → the block appears for both, starting at the answer point. Alice declines/Bob cancels instead → the call vanishes, no block, no notification.

## Reuse

- **Existing abstractions reused:** `SessionStartedAt` (the block/latch signal — untouched at its ~20 gates), `LiveSessionKind` (extended by one value), the invite state machine (`CallInviteStatus`), `IsSessionLive` / `IsCallAbandoned` (keepalive + abandoned-close — unchanged), `LiveSession.Kind` (already projected to the client), `ChatsBackend.GetLidRange` (visible-start lid), `CloseAndMaterialize`/`CloseCall` (close path). No new service, storage, or DB migration.
- **New components:** `LiveSessionKind.Dialing` (enum value) and the `IsCall` / `IsDialing` computed helpers — both on the shared API model `LiveSessionState` in `ActualChat.Api`, so they are inherently shared; no feature-local-vs-shared placement decision to make.
```