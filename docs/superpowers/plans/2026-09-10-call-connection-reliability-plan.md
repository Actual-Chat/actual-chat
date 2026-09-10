# Call connection reliability — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the server the source of truth for "is this call still real" — close the two races/gaps found while root-causing today's accept-then-drop bug, without touching wire contracts.

**Architecture:** Three independent layers, in dependency order: (1) couple `GetCallState`'s Fusion computed to `GetState`'s so the two stop invalidating independently over RPC; (2) make `_participants` presence connection-lifetime-bound (register on stream/listen start, remove on stream/listen end) instead of a 90s timestamp, and lean on that removal path to enforce "a `Call` needs ≥2 genuinely present participants" both immediately (on a presence drop) and via a one-shot 3s grace timer scheduled at accept; (3) reorder the client's accept flow so listening starts before mic permission resolves, so the 3s grace window is realistic.

**Tech Stack:** ActualLab.Fusion (compute services, RPC), Redis (`RedisScope<T>`, `RedisMultiHashMap<T>`), xUnit + `Testing.Host` (`SharedAppHostTestBase`), Blazor/C# client (`UI.Blazor.App`).

**Spec:** [`docs/superpowers/specs/2026-09-10-call-connection-reliability-design.md`](../specs/2026-09-10-call-connection-reliability-design.md)

## Global Constraints

- No wire contract changes: no new RPC methods, no new `CallOutcome`/`CallStatus`/`ParticipationKind` values (spec's Rollout section).
- The ≥2 invariant applies only to `LiveSessionState.Kind == LiveSessionKind.Call`. `Dialing` keeps `ExpireRings`/`IsCallAbandoned` unchanged; `Ambient` is exempt entirely (spec Goals).
- `OutgoingCallBanner.OnClose`'s race is explicitly out of scope (spec Non-goals).
- A call that fails the grace check records the existing `CallOutcome.Ended` — no new outcome value (spec Non-goals).
- Follow `docs/CODING_STYLE.md`: no `Async` suffix, no XML docs, comments only where the WHY is non-obvious, 2-line comment limit.

## Reuse

**Existing abstractions this plan reuses (found by reading the actual source, not the curated index — the exact current call sites mattered more than a summary):**

- `ILiveSessionsBackend.SetParticipation(chatId, authorId, kind, isActive, ct)` (already public, already used by the client's own `LiveSessionUI.RunParticipationSync` heartbeat) is the *existing* primitive for registering/removing a participant. Tasks 5 and 6 call it directly from the new connection-lifetime hooks — no new backend RPC method is introduced.
- `IAuthors.GetOwn(session, chatId, ct)` — non-mutating own-author lookup, already used the same way in `AudioStreamingBackend.ProcessAudio` (via `EnsureJoined`) and throughout `LiveSessionsTest.cs`. Used in Task 5 instead of inventing a new resolution path.
- `BackgroundTask.Run(Func<Task>, ILogger, string, CancellationToken)` (`src/dotnet/Core/BackgroundTask.cs`) — the codebase's standard fire-and-forget-with-logging primitive, already used in `AudioStreamingBackend.ProcessAudio.cs` and `VideoStreamingBackend.cs`. Used in Task 4 for the one-shot grace timer instead of `FlowHub`. `FlowHub`/`WakeSummaryFlow`'s pattern (`NewResumeEvent(...).WithDelay(...).Schedule(...)`) is *not* used: `FlowHub` exists for durable, checkpointed, cross-restart-surviving work (summary flows), and the spec's own Rollout section explicitly says a lost grace-timer job on a mid-deploy restart is fine because the 30s self-heal backstop still catches it — durability would be solving a problem the spec says doesn't need solving.
- `internal` visibility + direct test invocation, mirroring the existing `ExpireRings` convention ("Internal rather than private so a test can drive it directly, without a real ring timeout") — applied to the new `EnforceCallConnectGrace` so tests don't wait out a real 3-second delay. `Chat.IntegrationTests` already has `InternalsVisibleTo` from `Streaming.Service.csproj`.
- `ComputedTest.When` / `Computed.Capture` + `.WhenInvalidated` — already used throughout `LiveSessionsTest.cs` for exactly this kind of "does this computed value converge / invalidate" assertion. No new test infrastructure needed.
- `_changeLocks` (`AsyncLockSet<ChatId>`, non-reentrant) and `CloseCall`/`CloseNow`/`ParticipantCount` — existing, reused as-is by the new headcount checks; no new closing logic anywhere in this plan. `ParticipantCount` is a raw headcount, not peer-chat-specific, so it already generalizes to a hypothetical >2-participant group call without any extra work — resolving the spec's open question on this point.

**Deviation from the spec's Testing section:** the spec suggested extending `tests/Chat.IntegrationTests/CallEntryTest.cs` for everything. Reading the actual test suites first (per this file's own mandate) found `tests/Chat.IntegrationTests/LiveSessionsTest.cs` already covers exactly this backend's state machine (`AcceptCall`, `LeaveCall`, `SetParticipation`, `Computed.Capture`/`ComputedTest.When` patterns) and `tests/Streaming.IntegrationTests/LiveAudioStreamsTest.cs` already covers real `ProcessAudio`/`GetListeningStream` stream plumbing (`GetFrames`, `Chats_Change`). `CallEntryTest.cs` is specifically about the resulting `CallEntry`/chat card, which none of these tasks change (the grace-window failure still records as an ordinary `Ended` outcome, already covered by `CallEntryTest.cs`'s existing `AnsweredCallShouldWriteEndedAndMaterializeACallConversation`). Tasks 1-4 add tests to `LiveSessionsTest.cs`; Tasks 5-6 add tests to `LiveAudioStreamsTest.cs`; no new tests are needed in `CallEntryTest.cs`.

**New components and their placement:** everything new here (the grace-timer scheduling helper, the kind-aware removal guard, the two connection-lifetime hooks) is a small addition to an existing backend method or an existing RPC service class in `Streaming.Service`/`Streaming.Contracts` — none of it is reusable outside the live-session/call domain, so nothing is proposed for `ActualChat.Core`/`ActualChat.Core.Server`. The `CallConnectGrace` constant follows the existing convention of `SelfHealDelay`/`ParticipantStaleness` (private `static readonly TimeSpan` fields directly on `LiveSessionsBackend`), not `Constants.Call` (which is reserved for values the client also needs, like `RingTimeout`).

---

## Task 1: Couple `GetCallState`'s invalidation to `GetState`'s

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs:238-256` (`GetCallState`)
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: nothing new — this changes `GetCallState`'s internal dependency graph only; its signature and behavior for callers are unchanged.

This is the root-cause fix for the "accept then instant drop" bug's underlying fragility (spec section "Root-causing today's race"): `GetCallState` reads Redis directly and self-invalidates purely off its own TTL, with zero Fusion dependency on `GetState`. `AcceptCall` writes both under the same lock, but they reach RPC clients as two unrelated invalidation notifications with no ordering guarantee.

- [ ] **Step 1: Write the failing test**

Add to `tests/Chat.IntegrationTests/LiveSessionsTest.cs` (anywhere among the other `[Fact]`s):

```csharp
    [Fact]
    public async Task CallStateShouldInvalidateWheneverStateDoes()
    {
        // Regression test for the root cause behind 9e0b87186c: GetCallState used to invalidate
        // completely independently of GetState/Kind, so an RPC client's two subscriptions could
        // observe them out of order. GetCallState now depends on GetState, so any invalidation of
        // the session state also invalidates the call state - even one that never touches CallState.

        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        var cCallState = await Computed.Capture(() => backend.GetCallState(chatId, default));
        cCallState.Value!.Status.Should().Be(CallStatus.Dialing);

        // act - SetRules never touches CallState at all, only LiveSessionState
        await backend.SetRules(chatId, new SessionRules { VoiceModeOverride = Users.VoiceMode.JustText }, default);

        // assert - GetCallState still invalidates, because it now depends on GetState
        await cCallState.WhenInvalidated(default).WaitAsync(TimeSpan.FromSeconds(1));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest.CallStateShouldInvalidateWheneverStateDoes"`
Expected: FAIL with a `TimeoutException` from `WaitAsync` — `SetRules` invalidates `GetState`/`Get` but never touches `CallState`, and today `GetCallState` has no dependency on `GetState`, so its captured computed never invalidates from this action.

- [ ] **Step 3: Implement the fix**

In `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`, replace `GetCallState` (lines 238-256):

```csharp
    // [ComputeMethod]
    public virtual async Task<CallState?> GetCallState(ChatId chatId, CancellationToken cancellationToken)
    {
        // Captured before the awaits below — see GetState.
        var computed = Computed.GetCurrent();
        // Depend on GetState so CallState and Kind invalidate together over RPC instead of drifting
        // independently - this used to race (9e0b87186c): Accepted could land before Kind == Call did.
        await GetState(chatId, cancellationToken).ConfigureAwait(false);

        var callState = await SafeGetCallState(chatId).ConfigureAwait(false);
        if (callState is null)
            return null;

        // Nothing invalidates a Redis TTL expiry, so age the observed value out alongside the key.
        var expiresIn = callState.ChangedAt + CallStateTtl(callState.Status) - Clocks.SystemClock.Now;
        if (expiresIn <= TimeSpan.Zero)
            return null;

        computed.Invalidate(expiresIn);
        return callState;
    }
```

(The standalone `ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken)` call is removed — `GetState` already performs it, with a dependency, so keeping both would just wait on shard ownership twice.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest.CallStateShouldInvalidateWheneverStateDoes"`
Expected: PASS

- [ ] **Step 5: Run the existing call-state tests to check for regressions**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest&(FullyQualifiedName~CallStatus|FullyQualifiedName~CallState|FullyQualifiedName~Dialing)"`
Expected: PASS (in particular `StartCallShouldSetDialingStatus`, `AcceptShouldSetAcceptedStatus`, `DeclineShouldLeaveDeclinedStatus`, `CancelShouldClearStatus`, `FreshDialingCallShouldNotBeClosedBySelfHeal`, `CallStatusShouldGoToTheCallerOnly`, `CallStatusShouldInvalidateAnAlreadyObservedValue`)

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs tests/Chat.IntegrationTests/LiveSessionsTest.cs
git commit -m "fix(call): couple GetCallState's invalidation to GetState's"
```

## Task 2: Make `SetParticipation`'s removal kind-aware

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs:343-388` (`SetParticipation`)
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `SetParticipation`'s existing signature is unchanged; its `isActive: false` branch now only removes a participant's record if that record's `Kind` still matches the `kind` being cleared.

`_participants` holds *one* `ParticipationInfo` record per `(chatId, authorId)` — not one per kind. Today, `SetParticipation(..., isActive: false)` unconditionally removes that record. Tasks 5 and 6 wire this method up to fire automatically when a stream's underlying connection ends. Once that's live, two independent streams for the same author (e.g. a recorder stream ending while a separate listening stream for the same author stays open) could race: the ending recorder's cleanup would delete the *whole* record, including the still-live listening registration. This task closes that gap before it's reachable.

- [ ] **Step 1: Write the failing test**

Add to `tests/Chat.IntegrationTests/LiveSessionsTest.cs`:

```csharp
    [Fact]
    public async Task SetParticipationRemovalShouldNotStompANewerKind()
    {
        // A recorder stream ending must not blow away a concurrently-open listening registration for
        // the same author - _participants stores one record per author, keyed by chatId+authorId, so
        // an unconditional Remove from the ending Record registration would also delete the still-live
        // AudioListen one if nothing guards it.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);

        // act - the author is upgraded to listening (their recording stream is being replaced), then
        // the OLD recording stream's own teardown fires its removal after the fact
        await backend.SetParticipation(chatId, author.Id, ParticipationKind.AudioListen, true, default);
        await backend.SetParticipation(chatId, author.Id, ParticipationKind.Record, false, default);

        // assert - the listening registration survives; only a same-kind removal may clear it
        await ComputedTest.When(async ct =>
            (await backend.ListParticipants(chatId, ct)).Should().Contain(author.Id));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest.SetParticipationRemovalShouldNotStompANewerKind"`
Expected: FAIL — the unconditional `_participants.Remove(...)` in the `else` branch deletes the `AudioListen` record that the `Record: false` call didn't own.

- [ ] **Step 3: Implement the fix**

In `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`, in `SetParticipation`, replace:

```csharp
            else
                await _participants.Remove(chatId.Value, authorId.Value).ConfigureAwait(false);
```

with:

```csharp
            else {
                // Kind-guarded: a stream ending must only clear the registration it itself owns. Two
                // independent streams for the same author (e.g. a recorder stopping while a separate
                // listening stream stays open) would otherwise let the ending one delete the record the
                // still-open one relies on - _participants holds one record per author, not per kind.
                var existing = await SafeGetParticipant(chatId, authorId).ConfigureAwait(false);
                if (existing is { } info && info.Kind == kind)
                    await _participants.Remove(chatId.Value, authorId.Value).ConfigureAwait(false);
            }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest.SetParticipationRemovalShouldNotStompANewerKind"`
Expected: PASS

- [ ] **Step 5: Run the existing participation tests to check for regressions**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest&(FullyQualifiedName~Participation|FullyQualifiedName~Recorder|FullyQualifiedName~Listener)"`
Expected: PASS (in particular `ParticipationShouldBeTracked`, `TrailingUtteranceShouldNotResurrectRecorder`, `ListenerShouldNotKeepSessionAlive`, `RecorderLeavingWithOnlyListenerLeftShouldCloseSession`)

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs tests/Chat.IntegrationTests/LiveSessionsTest.cs
git commit -m "fix(call): guard SetParticipation's removal against clearing a newer kind"
```

## Task 3: Enforce the ≥2 invariant on a presence drop in `SetParticipation`

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs:343-388` (`SetParticipation`, building on Task 2)
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs`

**Interfaces:**
- Consumes: `ParticipantCount(chatId)` (existing private method), `CloseCall(chatId)` (existing private method).
- Produces: `SetParticipation` now closes a `Kind == Call` session immediately when a presence removal drops the count below 2 — this is what Tasks 5 and 6's connection-lifetime hooks rely on for "path 1: immediate, event-driven" from the spec's Detection mechanism section.

`LeaveCall` already enforces "a call needs ≥2" for an *explicit* hang-up. `SetParticipation` (called by the client's heartbeat today, and by the new connection-lifetime hooks from Tasks 5/6) has no equivalent — a presence drop with no `LeaveCall` behind it (a connection that just died) goes unnoticed until the 30s self-heal.

- [ ] **Step 1: Write the failing test**

Add to `tests/Chat.IntegrationTests/LiveSessionsTest.cs`:

```csharp
    [Fact]
    public async Task PresenceDropBelowTwoShouldCloseTheCall()
    {
        // The mid-call symmetric-hangup path: SetParticipation is what the connection-lifetime hooks
        // (LiveAudioStreams, AudioStreamingBackend) call when a stream's connection actually drops, so
        // it must enforce the same ">= 2" invariant LeaveCall already does for an explicit hang-up.

        // arrange - Bob calls Alice; both connect as listeners (simplest way to reach 2 real participants)
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);
        await backend.SetParticipation(chatId, bobAuthor.Id, ParticipationKind.AudioListen, true, default);
        await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.AudioListen, true, default);

        // act - Alice's listening stream drops (connection lost) - the same call SetParticipation(false) makes
        await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.AudioListen, false, default);

        // assert - the call closes at once, same as an explicit LeaveCall would
        (await backend.GetState(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task PresenceDropOnAnAmbientSessionShouldNotCloseIt()
    {
        // The new invariant is scoped to Kind == Call only - an Ambient live conversation losing a
        // listener while a recorder stays on must keep running exactly as before.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        await backend.SetParticipation(chatId, AuthorId.New(chatId, 777_050), ParticipationKind.AudioListen, true, default);

        // act - the listener leaves; the recorder is still streaming
        await backend.SetParticipation(chatId, AuthorId.New(chatId, 777_050), ParticipationKind.AudioListen, false, default);

        // assert - unaffected: still live, not closing
        var live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeFalse();
    }
```

- [ ] **Step 2: Run tests to verify they fail/pass as expected**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest&(FullyQualifiedName~PresenceDropBelowTwoShouldCloseTheCall|FullyQualifiedName~PresenceDropOnAnAmbientSessionShouldNotCloseIt)"`
Expected: `PresenceDropBelowTwoShouldCloseTheCall` FAILs (today `SetParticipation` never checks headcount for `Call`, so the session survives with Bob alone). `PresenceDropOnAnAmbientSessionShouldNotCloseIt` already PASSes (it's a regression guard for the next step, not a new behavior).

- [ ] **Step 3: Implement the fix**

In `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`, replace the whole `SetParticipation` method body with:

```csharp
    public virtual async Task SetParticipation(
        ChatId chatId,
        AuthorId authorId,
        ParticipationKind kind,
        bool isActive,
        CancellationToken cancellationToken)
    {
        bool emptiedByLeave;
        var closeAsCall = false;
        var startedClosing = false;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            if (isActive) {
                // Preserve mute flags and the original join time across heartbeats / kind changes:
                // RegisteredAt is refreshed by every heartbeat, so it can't double as JoinedAt.
                var now = Clocks.SystemClock.Now;
                var existing = await SafeGetParticipant(chatId, authorId).ConfigureAwait(false);
                var joinedAt = existing is { JoinedAt: var j } && j != default ? j : now;
                var info = new ParticipationInfo(kind, now, existing?.MicMuted ?? false, joinedAt);
                await _participants.Set(chatId.Value, authorId.Value, info).ConfigureAwait(false);
                // The participants hash refreshes its own TTL on write, but the session state key only
                // does so on Set - which a steady-state session never reaches. Without this the state
                // silently expires mid-call and the next stream rebuilds it as a brand-new session.
                await _redisScope.Refresh(chatId.Value).ConfigureAwait(false);
            }
            else {
                // Kind-guarded: a stream ending must only clear the registration it itself owns. Two
                // independent streams for the same author (e.g. a recorder stopping while a separate
                // listening stream stays open) would otherwise let the ending one delete the record the
                // still-open one relies on - _participants holds one record per author, not per kind.
                var existing = await SafeGetParticipant(chatId, authorId).ConfigureAwait(false);
                if (existing is { } info && info.Kind == kind)
                    await _participants.Remove(chatId.Value, authorId.Value).ConfigureAwait(false);
            }
            InvalidateListParticipants(chatId);
            InvalidateHasRecorder(chatId);
            InvalidateGet(chatId);
            // A Call needs >= 2 genuinely present participants - LeaveCall already enforces this for an
            // explicit hang-up; this is the same rule for a presence drop with no LeaveCall behind it (a
            // connection that just died). Scoped to Call: Dialing keeps its own ExpireRings path, and
            // Ambient has no such invariant (solo dictation is legitimate).
            if (!isActive) {
                var state = await SafeGet(chatId).ConfigureAwait(false);
                if (state is { Kind: LiveSessionKind.Call } && await ParticipantCount(chatId).ConfigureAwait(false) < 2)
                    closeAsCall = true;
            }
            // A join/heartbeat, or a leave with someone still streaming, just re-evaluates liveness; the
            // grace there is the safety net for crashed/stale clients. A leave that stops the last stream
            // closes it outright below - no waiting on the grace or on a UI observer. EvaluateLiveness only
            // marks a still-populated session closing (recoverable if a recorder returns), so a transient
            // not-live blip never tears down a live recording - unlike an unconditional CloseNow here would.
            emptiedByLeave = !isActive && !closeAsCall && !await IsSessionLive(chatId).ConfigureAwait(false);
            if (!emptiedByLeave && !closeAsCall)
                startedClosing = await EvaluateLiveness(chatId).ConfigureAwait(false);
        }
        if (closeAsCall)
            await CloseCall(chatId).ConfigureAwait(false);
        else if (emptiedByLeave)
            await CloseNow(chatId).ConfigureAwait(false);
        else if (startedClosing)
            // The last recorder stayed on as a listener: no stream left to trip CloseNow, but the session is
            // no longer live. Wake the summary flow to finalize the just-closing session now, rather than
            // leaving the block and Call tab up until the 90s SelfClose backstop fires.
            await WakeSummaryFlow(chatId).ConfigureAwait(false);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest&(FullyQualifiedName~PresenceDropBelowTwoShouldCloseTheCall|FullyQualifiedName~PresenceDropOnAnAmbientSessionShouldNotCloseIt|FullyQualifiedName~SetParticipationRemovalShouldNotStompANewerKind)"`
Expected: PASS

- [ ] **Step 5: Run the full `LiveSessionsTest` and `CallEntryTest` suites to check for regressions**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest|FullyQualifiedName~CallEntryTest"`
Expected: PASS (all existing cases, including `LeaveWithOthersPresentShouldKeepSessionLive`, `RecorderLeavingWithOnlyListenerLeftShouldCloseSession`, `LastRecorderDowngradingToListenerShouldCloseSession`, `ClaimedCloseShouldTearDownEvenWhenItsTokenIsCanceled`)

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs tests/Chat.IntegrationTests/LiveSessionsTest.cs
git commit -m "feat(call): close a Call session immediately when presence drops below 2"
```

## Task 4: Grace-timer for "accepted but never actually connected"

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs` (top of class, `AcceptCall` at lines 590-632, new private/internal methods)
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs`

**Interfaces:**
- Consumes: `ParticipantCount(chatId)`, `CloseCall(chatId)`, `BackgroundTask.Run(Func<Task>, ILogger, string, CancellationToken)`.
- Produces: `internal Task EnforceCallConnectGrace(ChatId chatId)` — callable directly by tests (mirrors `ExpireRings`), and scheduled once, fire-and-forget, by `AcceptCall`.

`AcceptCall` currently calls `EnsureParticipant(chatId, inviteeAuthorId)` synchronously, marking the invitee "present" for the full 90s `ParticipantStaleness` window *before their client has done anything with audio*. That defeats any grace-window check that reuses `ParticipantCount`: the invitee would always read as present immediately after accepting, even if their client is stuck on a permission prompt forever. This task removes that premature registration (the invitee's real presence now comes from Tasks 5/6's connection-lifetime hooks) and adds the 3-second one-shot check from the spec.

Note: `StartCall`'s caller-side `EnsureParticipant` is deliberately left untouched — the ≥2 invariant only starts applying once `Kind == Call` (post-accept), and the caller's own presence is expected to be refreshed for real moments later via `LiveSessionUI.JoinAnsweredCall` (Task 4 doesn't touch the client; that flow already calls `ChatAudioUI.SetListeningState` immediately on the caller's side, unrelated to Task 7's `IncomingCallUI.Accept()` reorder).

- [ ] **Step 1: Write the failing tests**

Add to `tests/Chat.IntegrationTests/LiveSessionsTest.cs`:

```csharp
    [Fact]
    public async Task AcceptedCallWithNoConnectionShouldCloseAfterGraceWindow()
    {
        // The invitee accepted but never actually opened a listening or recording stream (stuck mic
        // prompt, dead network, client bug) - nothing else would ever notice, since Kind == Call forever
        // otherwise. EnforceCallConnectGrace is internal so the test can drive it directly instead of
        // waiting out the real 3s delay - see AcceptCall's scheduling call for context.

        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = (LiveSessionsBackend)bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // act - Alice accepts but her client never streams or listens
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);
        (await backend.GetState(chatId, default))!.Kind.Should().Be(LiveSessionKind.Call);
        await backend.EnforceCallConnectGrace(chatId);

        // assert - the grace window found only Bob genuinely present, so the call closes
        (await backend.GetState(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task AcceptedCallWithAListenerShouldSurviveTheGraceWindow()
    {
        // A denied/pending mic permission must not fail the grace check: listening alone is enough
        // presence, per the accept-flow reorder that starts it before the mic prompt resolves.

        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = (LiveSessionsBackend)bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // act - Alice accepts and starts listening (mic still pending/denied); Bob is already present
        // from StartCall
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);
        await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.AudioListen, true, default);
        await backend.EnforceCallConnectGrace(chatId);

        // assert - both are genuinely present, so the call survives the grace check
        (await backend.GetState(chatId, default)).Should().NotBeNull();
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest&(FullyQualifiedName~AcceptedCallWithNoConnectionShouldCloseAfterGraceWindow|FullyQualifiedName~AcceptedCallWithAListenerShouldSurviveTheGraceWindow)"`
Expected: `AcceptedCallWithNoConnectionShouldCloseAfterGraceWindow` FAILs to compile (`EnforceCallConnectGrace` doesn't exist yet). Once stubbed in (Step 3 below), it FAILs at runtime: `AcceptCall` still pre-registers Alice via `EnsureParticipant`, so `ParticipantCount` reads 2 and the call survives.

- [ ] **Step 3: Implement the fix**

In `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`, add a new constant next to the other `TimeSpan` fields near the top of the class (after `ResolvedStateTtl`):

```csharp
    // How long AcceptCall waits before checking that the invitee genuinely connected (see
    // EnforceCallConnectGrace) - short enough that a stalled connect surfaces fast, long enough to
    // cover the accept-flow reorder's round trip (client starts listening immediately on accept).
    private static readonly TimeSpan CallConnectGrace = TimeSpan.FromSeconds(3);
```

Replace `AcceptCall` (lines 590-632):

```csharp
    public virtual async Task AcceptCall(ChatId chatId, AuthorId inviteeAuthorId, CancellationToken cancellationToken)
    {
        ConversationId? conversationId = null;
        var justConnected = false;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
            if (invite is not { Status: CallInviteStatus.Ringing })
                return;

            var now = Clocks.SystemClock.Now;
            await _invites.Set(chatId.Value, inviteeAuthorId.Value,
                    invite with { Status = CallInviteStatus.Accepted, RespondedAt = now })
                .ConfigureAwait(false);

            var state = await SafeGet(chatId).ConfigureAwait(false);
            if (state is { SessionStartedAt: null }) {
                // The first answer latches a dialing call to Connected: it's now a live conversation, so
                // surface the block from the chat end at answer time and make it genuinely two-party.
                // The invitee's own presence is NOT registered here (unlike before) - it now comes only
                // from a real listening/recording stream, so EnforceCallConnectGrace below can actually
                // tell "accepted" apart from "accepted and connected".
                var visibleStartLid = (await ChatsBackend
                    .GetLidRange(chatId, false, cancellationToken)
                    .ConfigureAwait(false)).End;
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
                // The latch is the caller's "accepted" moment - a brief confirmation before this fades.
                await SetCallState(chatId, NewCallState(state, CallStatus.Accepted)).ConfigureAwait(false);
                justConnected = true;
            }
            conversationId = state?.RingConversationId;
            InvalidateState(chatId);
        }
        if (conversationId is { } cid)
            await DismissRing(cid, [inviteeAuthorId], cancellationToken).ConfigureAwait(false);
        if (justConnected)
            _ = ScheduleCallConnectGraceCheck(chatId);
    }
```

In the "Private methods" region, right after `ReassignHost` (before `SafeGet`), add:

```csharp
    private Task ScheduleCallConnectGraceCheck(ChatId chatId)
        => BackgroundTask.Run(async () => {
            await Task.Delay(CallConnectGrace).ConfigureAwait(false);
            await EnforceCallConnectGrace(chatId).ConfigureAwait(false);
        }, Log, $"Call-connect grace check failed for chat #{chatId}");

    // AcceptCall schedules this once, fire-and-forget, CallConnectGrace after promoting Kind to Call.
    // Internal so a test can drive it directly, without a real wait - mirrors ExpireRings.
    internal async Task EnforceCallConnectGrace(ChatId chatId)
    {
        try {
            var shouldClose = false;
            using (Computed.BeginIsolation())
            using (await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false)) {
                var state = await SafeGet(chatId).ConfigureAwait(false);
                if (state is { Kind: LiveSessionKind.Call } && await ParticipantCount(chatId).ConfigureAwait(false) < 2)
                    shouldClose = true;
            }
            if (shouldClose)
                await CloseCall(chatId).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "EnforceCallConnectGrace failed for chat #{ChatId}", chatId);
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest&(FullyQualifiedName~AcceptedCallWithNoConnectionShouldCloseAfterGraceWindow|FullyQualifiedName~AcceptedCallWithAListenerShouldSurviveTheGraceWindow)"`
Expected: PASS

- [ ] **Step 5: Run the full `LiveSessionsTest` and `CallEntryTest` suites to check for regressions**

Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest|FullyQualifiedName~CallEntryTest"`
Expected: PASS — in particular `AcceptCallShouldJoinCall` (asserts `ListParticipants` contains Alice right after accept: verify this still holds once Task 5's real listening hook is in place; until then this specific assertion is expected to now read Alice as *not yet* a participant immediately after `AcceptCall` alone, since her registration no longer happens synchronously — if `AcceptCallShouldJoinCall` fails here, that is expected and gets resolved by Task 5, not by changing Task 4's code) and `AnsweredCallShouldWriteEndedAndMaterializeACallConversation` (drives `AcceptCall` then `LeaveCall` without ever registering the invitee as a listener/recorder in between — this still passes because `LeaveCall` operates on whoever is actually registered, and Bob's own `StartCall`-time registration is untouched).

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs tests/Chat.IntegrationTests/LiveSessionsTest.cs
git commit -m "feat(call): add a 3s connect grace check after accept, stop pre-registering the invitee"
```

## Task 5: Connection-lifetime listener presence in `LiveAudioStreams`

**Files:**
- Modify: `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs`
- Test: `tests/Streaming.IntegrationTests/LiveAudioStreamsTest.cs`

**Interfaces:**
- Consumes: `ILiveSessionsBackend.SetParticipation` (existing), `IAuthors.GetOwn` (existing).
- Produces: nothing new for other tasks — this is the listener side of the presence signal the grace check (Task 4) and event-driven close (Task 3) rely on to actually see a connected listener.

`GetListeningStream` constructs a `ListeningStreamMuxer` per listening session but never registers a participant for it. `ToLiveAsyncEnumerable`'s existing `finally` only disposes the muxer. This wires `SetParticipation(..., AudioListen, ...)` into both ends of that lifetime.

- [ ] **Step 1: Write the failing test**

Add to `tests/Streaming.IntegrationTests/LiveAudioStreamsTest.cs`:

```csharp
    [Fact(Timeout = 60_000)]
    public async Task GetListeningStreamShouldTrackListenerPresence()
    {
        // arrange
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        await appHost.SignIn(session, new AccountFull("Bobby"));

        var chat = await commander.Call(new Chats_Change {
            Session = session,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff {
                    Title = "GetListeningStreamPresenceTest",
                    Kind = ChatKind.Group,
                },
            },
        });
        chat.Require();

        var liveAudioStreams = services.GetRequiredService<ILiveAudioStreams>();
        var liveSessionsBackend = services.GetRequiredService<ILiveSessionsBackend>();
        var authors = services.GetRequiredService<IAuthors>();
        var author = await authors.GetOwn(session, chat.Id, default);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // act - open the listening stream and start pulling from it
        var stream = await liveAudioStreams.GetListeningStream(session, chat.Id, default, cts.Token);
        var enumerator = stream.GetAsyncEnumerator(cts.Token);
        var moveNextTask = enumerator.MoveNextAsync();

        // assert - presence is registered while the stream is open
        await ComputedTest.When(async ct =>
            (await liveSessionsBackend.ListParticipants(chat.Id, ct)).Should().Contain(author!.Id));

        // act - the caller stops enumerating (the connection is torn down)
        await enumerator.DisposeAsync();
        await moveNextTask.SilentAwait(false);

        // assert - presence goes with it, not with the 90s ParticipantStaleness backstop
        await ComputedTest.When(async ct =>
            (await liveSessionsBackend.ListParticipants(chat.Id, ct)).Should().NotContain(author!.Id));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Streaming.IntegrationTests --filter "FullyQualifiedName~LiveAudioStreamsTest.GetListeningStreamShouldTrackListenerPresence"`
Expected: FAIL at the first `ComputedTest.When` (times out) — nothing registers `AudioListen` presence today.

- [ ] **Step 3: Implement the fix**

In `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs`, add two lazily-resolved services next to the existing ones:

```csharp
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    private ILiveSessionsBackend LiveSessionsBackend => field ??= Services.GetRequiredService<ILiveSessionsBackend>();
```

Replace `GetListeningStream`:

```csharp
    public async Task<RpcStream<MuxedAudioStreamItem>> GetListeningStream(
        Session session,
        ChatId chatId,
        Moment catchUpFrom,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        chat.Rules.Require(ChatPermissions.ReadAudio);

        Log.LogInformation("GetListeningStream: chat '{ChatId}', catchUpFrom={CatchUpFrom}", chatId, catchUpFrom);
        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author != null)
            await SetListenerPresence(chatId, author.Id, true, cancellationToken).ConfigureAwait(false);
        var muxer = new ListeningStreamMuxer(Services, session, chatId, catchUpFrom);
        var stream = ToLiveAsyncEnumerable(muxer, muxer.Output, chatId, author?.Id, cancellationToken);
        return StandardRpcStream.NewAudioDelivery(stream, allowReconnect: false);
    }
```

Replace `ToLiveAsyncEnumerable` (drop `static`, add the two new parameters and the unregister call):

```csharp
    private async IAsyncEnumerable<MuxedAudioStreamItem> ToLiveAsyncEnumerable(
        ListeningStreamMuxer muxer,
        ChannelReader<MuxedAudioStreamItem> reader,
        ChatId chatId,
        AuthorId? authorId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally {
            await muxer.DisposeAsync().ConfigureAwait(false);
            if (authorId is { } id)
                await SetListenerPresence(chatId, id, false, CancellationToken.None).ConfigureAwait(false);
        }
    }
```

Add a helper in the "Private methods" region:

```csharp
    private async Task SetListenerPresence(ChatId chatId, AuthorId authorId, bool isActive, CancellationToken cancellationToken)
    {
        try {
            await LiveSessionsBackend
                .SetParticipation(chatId, authorId, ParticipationKind.AudioListen, isActive, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to update listener presence for chat #{ChatId}", chatId);
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Streaming.IntegrationTests --filter "FullyQualifiedName~LiveAudioStreamsTest.GetListeningStreamShouldTrackListenerPresence"`
Expected: PASS

- [ ] **Step 5: Re-run Task 4's `AcceptCallShouldJoinCall` concern and the full `LiveAudioStreamsTest`/`LiveSessionsTest` suites**

Run: `dotnet test tests/Streaming.IntegrationTests --filter "FullyQualifiedName~LiveAudioStreamsTest"`
Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest"`
Expected: PASS. `AcceptCallShouldJoinCall` (flagged in Task 4 as a call site to watch) does **not** call `GetListeningStream`, so it is unaffected by this task either — if it's still failing after Task 4, update that test's assertion to reflect that participation now requires an actual stream, not just `AcceptCall`, by adding an explicit `SetParticipation(..., AudioListen, true, ...)` call before the assertion (matching how `ClaimedCloseShouldTearDownEvenWhenItsTokenIsCanceled` already does this).

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs tests/Streaming.IntegrationTests/LiveAudioStreamsTest.cs
git commit -m "feat(call): tie listener presence to the listening stream's connection lifetime"
```

## Task 6: Connection-lifetime recorder presence in `AudioStreamingBackend.ProcessAudio`

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs:220-226`
- Test: `tests/Streaming.IntegrationTests/LiveAudioStreamsTest.cs`

**Interfaces:**
- Consumes: `ILiveSessionsBackend.SetParticipation` (existing, already injected in `AudioStreamingBackend` as `LiveSessionsBackend`).
- Produces: nothing new for other tasks — this is the recorder-side half of the presence signal.

`OnStreamRegistered` (called from `ProcessAudio` when `mustStreamVoice || isSummarized`) registers the recorder via `EnsureParticipant`, but nothing ever un-registers them when their stream ends — they rely on the 90s `ParticipantStaleness` timeout today. The existing "Unregisters at end of audio" `finally` block is the symmetric place to add it.

- [ ] **Step 1: Write the failing test**

Add to `tests/Streaming.IntegrationTests/LiveAudioStreamsTest.cs`:

```csharp
    [Fact(Timeout = 60_000)]
    public async Task ProcessAudioEndingShouldClearRecorderPresence()
    {
        // arrange
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        await appHost.SignIn(session, new AccountFull("Bobby"));

        var chat = await commander.Call(new Chats_Change {
            Session = session,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff {
                    Title = "ProcessAudioPresenceTest",
                    Kind = ChatKind.Group,
                },
            },
        });
        chat.Require();
        await services.UserSettingsUI(session)
            .ChatUserSettings(chat.Id)
            .Set(new ChatUserSettings { VoiceMode = VoiceMode.JustVoice }, CancellationToken.None);

        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var liveSessionsBackend = services.GetRequiredService<ILiveSessionsBackend>();
        var authors = services.GetRequiredService<IAuthors>();
        var record = new AudioRecord(
            StreamId.New(services.MeshWatcher().ThisNode.Ref),
            session,
            chat.Id,
            SystemClock.Instance.Now.EpochOffset.TotalSeconds,
            null);

        // act - the stream runs to completion naturally (GetFrames yields 25 frames, then ends)
        await backend.ProcessAudio(record, 0, new RpcStream<AudioFrame>(GetFrames()), CancellationToken.None);

        // assert - the recorder's presence must not linger for the full 90s ParticipantStaleness window
        var author = await authors.GetOwn(session, chat.Id, default);
        await ComputedTest.When(async ct =>
            (await liveSessionsBackend.ListParticipants(chat.Id, ct)).Should().NotContain(author!.Id));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Streaming.IntegrationTests --filter "FullyQualifiedName~LiveAudioStreamsTest.ProcessAudioEndingShouldClearRecorderPresence"`
Expected: FAIL — `ListParticipants` still contains the author after the stream ends (nothing removes the `Record` registration).

- [ ] **Step 3: Implement the fix**

In `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs`, in the private `ProcessAudio` overload, replace the "Unregisters at end of audio" `finally` block (lines 220-226):

```csharp
                finally {
                    // Unregisters at end of audio rather than after the blob save below: the chat
                    // would otherwise keep reporting activity for the whole persistence latency.
                    await LiveAudioBackend
                        .Unregister(chatId, openSegment.StreamId.Value, CancellationToken.None)
                        .ConfigureAwait(false);
                }
```

with:

```csharp
                finally {
                    // Unregisters at end of audio rather than after the blob save below: the chat
                    // would otherwise keep reporting activity for the whole persistence latency.
                    await LiveAudioBackend
                        .Unregister(chatId, openSegment.StreamId.Value, CancellationToken.None)
                        .ConfigureAwait(false);
                    // Connection-lifetime presence: this recorder's stream just ended, so their
                    // participation record should go with it instead of lingering for up to
                    // ParticipantStaleness (90s) - mirrors the registration OnStreamRegistered made above.
                    if (mustStreamVoice || isSummarized)
                        try {
                            await LiveSessionsBackend
                                .SetParticipation(chatId, author.Id, ParticipationKind.Record, false, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (Exception e) when (e is not OperationCanceledException) {
                            Log.LogWarning(e, "Failed to clear recorder presence for chat #{ChatId}", chatId);
                        }
                }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Streaming.IntegrationTests --filter "FullyQualifiedName~LiveAudioStreamsTest.ProcessAudioEndingShouldClearRecorderPresence"`
Expected: PASS

- [ ] **Step 5: Run the full `Streaming.IntegrationTests` and `Chat.IntegrationTests` suites to check for regressions**

Run: `dotnet test tests/Streaming.IntegrationTests`
Run: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest|FullyQualifiedName~CallEntryTest"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs tests/Streaming.IntegrationTests/LiveAudioStreamsTest.cs
git commit -m "feat(call): tie recorder presence to the audio stream's connection lifetime"
```

## Task 7: Client accept-flow reorder — listen before mic permission

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs:210-231` (`Accept`)

**Interfaces:**
- Consumes: `ChatAudioUI.SetListeningState(ChatId, bool)` (existing), `AudioRecorder.MicrophonePermission.CheckOrRequest(CancellationToken)` (existing), `ChatAudioUI.SetRecordingChatId(ChatId)` (existing) — all already used in this exact method today, just reordered.
- Produces: nothing new for other tasks.

`LiveSessionUI.JoinAnsweredCall` (the caller's side, once their outgoing call is answered) already calls `SetListeningState` before awaiting mic permission — verified current, no change needed there. `IncomingCallUI.Accept()` (the accepting side) is the one that still gates *both* recording and listening behind `CheckOrRequest` resolving. If that OS prompt sits unanswered, the new 3s grace check (Task 4) would find nobody listening and close a call that's really just waiting on the user to click through a permission dialog.

- [ ] **Step 1: Make the change**

In `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs`, in `Accept`, replace:

```csharp
            if (canStartAudio) {
                var micPermission = Hub.AudioRecorder.MicrophonePermission;
                if (await micPermission.CheckOrRequest(CancellationToken.None).ConfigureAwait(true))
                    await ChatAudioUI.SetRecordingChatId(chatId).ConfigureAwait(true);
                else {
                    // Mic denied: still join the call as a listener.
                    await ChatAudioUI.SetListeningState(chatId, true).ConfigureAwait(true);
                }
            }
```

with:

```csharp
            if (canStartAudio) {
                // Listen first, unconditionally - mic permission is negotiated in parallel/after, so a
                // pending OS prompt doesn't leave the grace check (EnforceCallConnectGrace) seeing nobody
                // connected. Mirrors LiveSessionUI.JoinAnsweredCall's already-correct ordering.
                await ChatAudioUI.SetListeningState(chatId, true).ConfigureAwait(true);
                var micPermission = Hub.AudioRecorder.MicrophonePermission;
                if (await micPermission.CheckOrRequest(CancellationToken.None).ConfigureAwait(true))
                    await ChatAudioUI.SetRecordingChatId(chatId).ConfigureAwait(true);
            }
```

- [ ] **Step 2: Trigger a server-loop rebuild and confirm it builds clean**

If `/server-loop` is running: touch a keypress/`/health/stop` to restart, then check `tmp/server-loop-dotnet-build.log` for `error CS`.
Otherwise: `dotnet build ActualChat.CI.slnf`

- [ ] **Step 3: Live two-browser verification**

Using `chrome1`/`chrome2` against the running `/server-loop` instance (per the spec's Testing section and the same rig used to root-cause and verify `9e0b87186c`):
1. Sign in as two different users on `chrome1` and `chrome2`.
2. Place a call from `chrome1` to `chrome2`.
3. On `chrome2`, accept the call and immediately deny (or leave pending) the microphone permission prompt.
4. Confirm the call stays connected past 3+ seconds (the grace check must not fire) and `chrome2` shows as a listener.
5. Repeat the original bug's repro (accept right around the moment the server promotes `Kind` to `Call`) a few times to confirm no regression of `9e0b87186c`'s fix.
6. Kill `chrome2`'s tab outright (not a clean hangup click) mid-call; confirm `chrome1` ends the call promptly (within a few seconds, not 30s+) — this exercises Tasks 3/5/6 together.

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs
git commit -m "fix(call): start listening before mic permission resolves in Accept()"
```

---

## Final verification (after all tasks)

- [ ] Run the full affected suites once more: `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~LiveSessionsTest|FullyQualifiedName~CallEntryTest"` and `dotnet test tests/Streaming.IntegrationTests`.
- [ ] Re-run the Task 7 two-browser live verification once all six backend/client changes are in, since Tasks 3-6 only compose correctly together (the grace timer needs the listener/recorder hooks to have anything real to find).
- [ ] Confirm no new `docs/CODING_STYLE.md` violations were introduced (the style hook runs automatically on each edit; check `.claude/style-bypasses.md` was not touched unless a violation was explicitly kept by decision).
