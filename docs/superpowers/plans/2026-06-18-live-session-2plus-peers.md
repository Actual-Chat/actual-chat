# Live Session = 2+ Peers, VAD-gap-tolerant — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A live session (Call tab, members, rules, mute) starts only when 2+ peers have streamed in one conversation, latches for the conversation's life, and survives VAD silence gaps.

**Architecture:** Add a one-way latch field `SessionStartedAt` to the existing `LiveConversation` Redis state. The backend latches it when a 2nd distinct streamer registers; the `GetLiveSession` projection returns null until it is set, which auto-gates every call surface (they all read `GetLiveSession`). Liveness reuses the existing 90s close-grace + reactivate; phone mode is folded into that same grace so no-transcription calls don't flap.

**Tech Stack:** C#/.NET, ActualLab.Fusion compute methods, Redis (`RedisScope`), MemoryPack VersionTolerant DTOs, xUnit + FluentAssertions integration tests against a shared AppHost.

---

## Reference: spec

Design spec: `docs/superpowers/specs/2026-06-18-live-session-2plus-peers-design.md`.

## File Structure

- `src/dotnet/Api/Live/LiveConversation.cs` — add `SessionStartedAt` (the latch).
- `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs` — latch in `OnStreamRegistered`; gate + `StartedAt` source in `GetLiveSession`; phone-mode close grace in `OnStreamsChanged`.
- `tests/Chat.IntegrationTests/LiveSessionsTest.cs` — new latch/gate tests; update 3 single-streamer session tests + the phone-mode close test.

No UI changes: `ShowCallTab` (`RightPanelContent.razor`) and `CallList.razor` already key off `GetLiveSession`.

## Notes for the implementer (codebase facts)

- `AuthorId.New(chatId, localId)` (`src/dotnet/Api/Identifiers/AuthorId.cs:35`) builds a valid `AuthorId` with no DB row. Tests use it to fabricate a 2nd distinct streamer: the latch only needs `AuthorIds.Count >= 2`, and `EnsureParticipant` (`LiveSessionsBackend.cs:370`) returns early when the author can't be resolved, so a fabricated author never appears as a member but does bump the streamer count.
- `OnStreamRegistered(chatId, authorId, entryLid, transcriptionOn, ct)` is the stream-start hook. `AuthorIds` holds distinct *streamers* only (listeners live in the `_participants` hash). `now` is already in scope (`LiveSessionsBackend.cs:161`).
- Liveness today: `OnStreamsChanged` marks `IsClosing/ClosingAt`; `Get` finalizes via `SelfClose` once `now - ClosingAt > CloseTimeout` (90s); a new `OnStreamRegistered` within the window clears `IsClosing/ClosingAt`. `SelfClose` (`:395`) already sends "Voice chat ended" + `Close` for any `IsClosing` state, mode-agnostic.
- Integration tests need Postgres/Redis/NATS — assume they are already running (Docker host). Test class is `[Collection(nameof(ChatCollection))]`.

---

### Task 1: Latch `SessionStartedAt` when a 2nd peer streams

**Files:**
- Modify: `src/dotnet/Api/Live/LiveConversation.cs` (add field after `Rules`, line 45)
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs` (`OnStreamRegistered`, before the `_redisScope.Set` at line 196)
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs`

- [ ] **Step 1: Write the failing test**

Add to `LiveSessionsTest.cs`:

```csharp
    [Fact]
    public async Task SessionLatchesOnSecondStreamer()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act — first (and only) streamer: not a session yet
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);

        // assert
        var live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.SessionStartedAt.Should().BeNull();

        // act — a second distinct peer starts streaming
        var peer2 = AuthorId.New(chatId, 777_001);
        await backend.OnStreamRegistered(chatId, peer2, null, true, default);

        // assert — the session latches
        live = await backend.Get(chatId, default);
        live!.AuthorIds.Should().HaveCount(2);
        live.SessionStartedAt.Should().NotBeNull();
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~LiveSessionsTest.SessionLatchesOnSecondStreamer"`
Expected: FAIL — compile error, `LiveConversation` has no `SessionStartedAt`.

- [ ] **Step 3: Add the data-model field**

In `src/dotnet/Api/Live/LiveConversation.cs`, after the `Rules` property (line 45), add:

```csharp
    [DataMember(Order = 17), MemoryPackOrder(17), Key(17)]
    public Moment? SessionStartedAt { get; init; }
```

- [ ] **Step 4: Add the latch in `OnStreamRegistered`**

In `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`, immediately before `await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);` (line 196), insert:

```csharp
        if (state.SessionStartedAt is null && state.AuthorIds.Count >= 2)
            state = state with {
                SessionStartedAt = now,
                Version = VersionGenerator.NextVersion(state.Version),
            };

```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~LiveSessionsTest.SessionLatchesOnSecondStreamer"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Api/Live/LiveConversation.cs src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs tests/Chat.IntegrationTests/LiveSessionsTest.cs
git commit -m "feat(live): latch SessionStartedAt when a 2nd peer streams

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Gate `GetLiveSession` on the latch + source `StartedAt` from it

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs` (`GetLiveSession`, lines 77-79 and the `return new LiveSession {...}` at line 127)
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs`

- [ ] **Step 1: Write the failing test**

Add to `LiveSessionsTest.cs`:

```csharp
    [Fact]
    public async Task GetLiveSessionNullUntilSecondStreamer()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act — single streamer: conversation exists, but no session yet
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);

        // assert
        (await backend.Get(chatId, default)).Should().NotBeNull();
        (await backend.GetLiveSession(chatId, default)).Should().BeNull();

        // act — 2nd peer streams
        var peer2 = AuthorId.New(chatId, 777_002);
        await backend.OnStreamRegistered(chatId, peer2, null, true, default);

        // assert — the session is now exposed, started at the latch moment
        var liveSession = await backend.GetLiveSession(chatId, default);
        liveSession.Should().NotBeNull();
        liveSession!.StartedAt.Should().NotBe(default);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~LiveSessionsTest.GetLiveSessionNullUntilSecondStreamer"`
Expected: FAIL — `GetLiveSession` returns non-null for the single streamer (assertion `Should().BeNull()` fails).

- [ ] **Step 3: Add the gate**

In `GetLiveSession`, after the existing null guard:

```csharp
        var state = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (state is null)
            return null;
```

insert:

```csharp
        if (state.SessionStartedAt is null)
            return null;
```

- [ ] **Step 4: Source `StartedAt` from the latch**

In the same method's `return new LiveSession {` block, change the line:

```csharp
            StartedAt = state.StartedAt,
```

to:

```csharp
            StartedAt = state.SessionStartedAt ?? state.StartedAt,
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~LiveSessionsTest.GetLiveSessionNullUntilSecondStreamer"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs tests/Chat.IntegrationTests/LiveSessionsTest.cs
git commit -m "feat(live): gate GetLiveSession on the 2+ peer latch

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Update the 3 single-streamer session tests to register a 2nd peer

These tests assert `GetLiveSession` is non-null after a single `OnStreamRegistered`. With the Task 2 gate that is now null, so each must add a 2nd streamer to latch the session.

**Files:**
- Modify: `tests/Chat.IntegrationTests/LiveSessionsTest.cs` (`LiveSessionExposesHostAndMembers`, `SetRulesPersistsVoiceModeOverride`, `MutePeerSetsForcedMuted`)

- [ ] **Step 1: Run the three tests to confirm they now fail**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~LiveSessionsTest.LiveSessionExposesHostAndMembers|FullyQualifiedName~LiveSessionsTest.SetRulesPersistsVoiceModeOverride|FullyQualifiedName~LiveSessionsTest.MutePeerSetsForcedMuted"`
Expected: FAIL — `GetLiveSession` is null (only one streamer), so the `Should().NotBeNull()` / `Single(...)` calls fail.

- [ ] **Step 2: Add a 2nd streamer to each test**

In `LiveSessionExposesHostAndMembers`, replace the single registration line:

```csharp
        // act
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
```

with:

```csharp
        // act — two peers stream, so a session latches
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_010), null, true, default);
```

In `SetRulesPersistsVoiceModeOverride`, replace:

```csharp
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
```

with:

```csharp
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_011), null, true, default);
```

In `MutePeerSetsForcedMuted`, replace:

```csharp
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
```

with:

```csharp
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_012), null, true, default);
```

(The `MutePeer`/member assertions still target the real `author.Id`, which projects as a member via the participant registry; the fabricated peer is skipped by `EnsureParticipant`.)

- [ ] **Step 3: Run the three tests to verify they pass**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~LiveSessionsTest.LiveSessionExposesHostAndMembers|FullyQualifiedName~LiveSessionsTest.SetRulesPersistsVoiceModeOverride|FullyQualifiedName~LiveSessionsTest.MutePeerSetsForcedMuted"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add tests/Chat.IntegrationTests/LiveSessionsTest.cs
git commit -m "test(live): latch a session (2 streamers) in session-projection tests

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Phone-mode close goes through the 90s grace (no flap on VAD gaps)

Today `OnStreamsChanged` removes phone-mode state immediately when no streams remain, so a no-transcription call would tear down on every VAD silence. Route it through the same `IsClosing/ClosingAt` grace; `SelfClose` finalizes it (vanish + "Voice chat ended") after the timeout.

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs` (`OnStreamsChanged`, lines 226-233 — the `if (!state.TranscriptionOn) { ... }` block)
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs` (`PhoneModeConversationVanishesOnClose`)

- [ ] **Step 1: Update the existing phone-mode test to the new expectation (failing)**

Replace the body of `PhoneModeConversationVanishesOnClose` with:

```csharp
    [Fact]
    public async Task PhoneModeConversationVanishesOnClose()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.OnStreamRegistered(chatId, author!.Id, null, false, default);

        // assert
        (await backend.Get(chatId, default)).Should().NotBeNull();

        // act — no streams remain; phone mode now uses the same close grace as transcription
        // (it does NOT vanish immediately, so a VAD gap between utterances doesn't flap the call)
        await backend.OnStreamsChanged(chatId, default);

        // assert — still present, marked closing, finalization deferred to the grace timeout
        var live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeTrue();
        live.ClosingAt.Should().NotBeNull();

        // act — explicit close removes it (stands in for the post-grace SelfClose)
        await backend.Close(chatId, default);

        // assert
        (await backend.Get(chatId, default)).Should().BeNull();
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~LiveSessionsTest.PhoneModeConversationVanishesOnClose"`
Expected: FAIL — current code removes the state in `OnStreamsChanged`, so `Get` returns null and `Should().NotBeNull()` fails.

- [ ] **Step 3: Remove the phone-mode immediate-remove branch**

In `OnStreamsChanged`, delete this entire block (lines 226-233):

```csharp
        if (!state.TranscriptionOn) {
            // Phone-mode call: nothing to materialize, the block just disappears.
            await _redisScope.Remove(chatId.Value).ConfigureAwait(false);
            await _participants.RemoveHashMap(chatId.Value).ConfigureAwait(false);
            InvalidateGet(chatId);
            await EnqueueLiveNotification(chatId, "Voice chat ended", isFinal: true, state.StartEntryLid, cancellationToken).ConfigureAwait(false);
            return;
        }

```

After deletion the method falls through to the shared closing path for both modes:

```csharp
        if (state.IsClosing)
            return;

        // Stream-less: mark closing; Get() finalizes after CloseTimeout. Transcription-on is
        // materialized by LiveConversationSummaryFlow; phone-mode is vanished by SelfClose.
        state = state with {
            IsClosing = true,
            ClosingAt = Clocks.SystemClock.Now,
            Version = VersionGenerator.NextVersion(state.Version),
        };
        await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
        InvalidateGet(chatId);
```

(Update the existing comment on the `state = state with {` line if it says "Transcription-on close is finalized by..."; the new comment above covers both modes. `SelfClose` at line 395 already sends "Voice chat ended" + `Close` for any closing state, so phone-mode finalization is preserved, just deferred by the grace.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~LiveSessionsTest.PhoneModeConversationVanishesOnClose"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs tests/Chat.IntegrationTests/LiveSessionsTest.cs
git commit -m "fix(live): route phone-mode close through the 90s grace (no VAD-gap flap)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Full build + test verification

**Files:** none (verification only)

- [ ] **Step 1: Build the touched projects**

Run: `dotnet build src/dotnet/Streaming.Service/Streaming.Service.csproj --no-restore`
Expected: Build succeeded (0 errors). If `Api` did not build transitively, also run `dotnet build src/dotnet/Api/Api.csproj --no-restore`.

- [ ] **Step 2: Run the full LiveSessions test suite**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~LiveSessionsTest"`
Expected: PASS — all tests green (the 5 pre-existing + `SessionLatchesOnSecondStreamer` + `GetLiveSessionNullUntilSecondStreamer` = 7+ total, plus the 3 updated session tests and the updated phone-mode test).

- [ ] **Step 3: Sanity-check no other caller depends on `GetLiveSession` being non-null for a single streamer**

Run: `rg -n "GetLiveSession" src/dotnet`
Expected: only `LiveSessionUI.GetLiveSession`, `RightPanelContent.razor`, `CallList.razor`, and the backend/contracts — all of which treat null as "no session" (no NRE on null). Confirm visually; no code change expected.

---

## Self-Review

**Spec coverage:**
- "Session = 2+ peers, latched" → Task 1 (latch) + Task 2 (gate). ✓
- "Persist until conversation ends" → latch is one-way; cleared only on `Close`/`SelfClose`. Covered by Task 1 field semantics; exercised by `RejoinClearsClosingState` (existing) staying green in Task 5. ✓
- "`StartedAt` from `SessionStartedAt`" → Task 2 Step 4. ✓
- "Single-talker surfaces unchanged" → no change to `Get` / `ChatActivityUI` / `ChatUI.Tiles`; Task 5 Step 3 confirms no null-dependent callers. ✓
- "Liveness via existing 90s grace" → reused, no code change; phone-mode folded in (Task 4). ✓
- "Phone-mode no immediate remove" → Task 4. ✓
- "Notification stays on first phone-mode streamer" → `OnStreamRegistered`'s "Voice chat started" untouched; verified by not editing that branch. ✓

**Placeholder scan:** no TBD/TODO; every code step shows full code. ✓

**Type consistency:** `SessionStartedAt` is `Moment?` everywhere; `AuthorId.New(chatId, long)` matches the factory signature; `GetLiveSession`/`Get`/`OnStreamRegistered`/`OnStreamsChanged`/`Close` signatures match the existing backend. ✓
