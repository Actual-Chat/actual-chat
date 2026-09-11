# Call State Model Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `LiveSessionKind.Dialing` and the action/state-mixed `CallStatus`/`CallInviteStatus` enums with flat "last event" state machines, driven by genuine presence facts (never a raw participant count), so a call's in-progress status can no longer drift out of sync with reality the way it did before this branch's earlier fixes.

**Architecture:** Every call-lifecycle concept (the aggregate `CallStatus`, the caller's own `CallerStatus`, each invitee's `CallInviteStatus`) becomes a flat enum whose value is simply the last thing that happened. Action RPCs (`AcceptCall`/`DeclineCall`/`ExpireRings`) record only their own participant's fact; a new pure function, `Derive`, recomputes the aggregate `CallStatus` from the current set of facts — never written directly by an action. Participant `Active`/`Ended` facts are synced from `GetConsolidatedParticipants` (the self-healing `[ComputeMethod]` already used for Ambient sessions) at `GetState`'s existing self-heal tick, not from `SetParticipation` directly — this is what lets a silent crash (not just an explicit hang-up) eventually resolve to `Ended`.

**Tech Stack:** C# / .NET, ActualLab.Fusion (`[ComputeMethod]`, `Computed.BeginIsolation`), Redis-backed records (`RedisScope`, `RedisMultiHashMap`), xUnit integration tests (`Chat.IntegrationTests`, `Chat.UI.Blazor.IntegrationTests`, `Chat.UI.Blazor.UnitTests`).

**Spec:** `docs/superpowers/specs/2026-09-11-call-state-model-cleanup-design.md`

## Global Constraints

- No Redis migration/backward-compat shim — the feature is still under active development, in-flight calls/rings can be lost on deploy (spec: Rollout).
- Client-side `IncomingCallUI`'s own state-machine cleanup (`_overLockRingChatId`/`_foregroundRawChatId`/etc.) is explicitly **out of scope** — touch client files only enough to compile against the new server types, no new client behavior beyond what's specified.
- `CallOutcome` (`None|NoAnswer|Declined|Canceled|Ended`) is **unchanged** — do not touch `src/dotnet/Api/Chat/CallEntry.cs`.
- `CallStatus.Canceled`/`CallerStatus.Canceled` are **kept** in both enums even though, given `CancelCall`'s decided behavior (clear `CallState` immediately, no lingering banner text), they are not currently observed by any client — this is a deliberate decision (model completeness/symmetry with `CallOutcome`), not an oversight. Do not remove them as "unreachable."
- Every renamed/removed enum value must be tracked to every consumer across `src/dotnet` and `tests` (grep for the old name before finishing a task) — this file lists every consumer found during planning, but re-verify at execution time since the codebase moves.
- `docs/CODING_STYLE.md` conventions apply throughout (no `Async` suffix, no XML docs, comment placement rules) — re-read it before writing code if unsure.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/dotnet/Api/Live/LiveSessionKind.cs` | Drops `Dialing` |
| `src/dotnet/Api/Live/LiveSessionState.cs` | `IsCall`/`IsDialing` redefined off `SessionStartedAt` |
| `src/dotnet/Api/Live/CallState.cs` | `CallStatus` reshaped; `CallState` gains `CallerActiveAt`/`CallerEndedAt`/`CanceledAt` |
| `src/dotnet/Api/Live/CallInvite.cs` | `CallInviteStatus` reshaped; `CallInvite` gains `ActiveAt`/`EndedAt`/`Ack`/`AckAt` |
| `src/dotnet/Api/Live/CallerStatus.cs` | **New** — the caller-facing projection enum |
| `src/dotnet/Api/Live/RingAck.cs` | **New** — client-ack enum |
| `src/dotnet/Streaming.Contracts/ILiveSessionsBackend.cs` | New `ConfirmRing` method |
| `src/dotnet/Api.Contracts/Streaming/ILiveSessions.cs` | New `ConfirmRing` method |
| `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs` | All the mechanics: `Derive`, `RecomputeCallStatus`, the `GetConsolidatedParticipants` sync, signal validation, `ConfirmRing`, `ParticipantCount` removal |
| `src/dotnet/Streaming.Service/Services/LiveSessions.cs` | `GetCallStatus` return type + projection; `ConfirmRing` passthrough |
| `src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs` | `GetCallStatus` return type; `Kind == Dialing` check replaced |
| `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs` | Same two kinds of fixups |
| `src/dotnet/UI.Blazor.App/Services/ChatActivityUI.cs` | `Kind == Dialing` check replaced |
| `src/dotnet/UI.Blazor.App/Components/Banners/OutgoingCallBanner.razor` | `CallStatus` switch → `CallerStatus` switch |
| `src/dotnet/UI.Blazor.App/Components/IncomingCallModal/IncomingCallOverLockView.razor` | Same |
| `src/dotnet/UI.Blazor.App/Components/RightPanel/LiveSessionMemberList.razor` | `Kind == Dialing` check replaced |
| Tests (see per-task lists) | Updated assertions, new coverage for `Derive`/the presence sync/signal validation/`ConfirmRing` |

---

## Reuse

**Existing abstractions reused, not reinvented:**
- `GetConsolidatedParticipants` (`LiveSessionsBackend.cs:776`) — the self-healing `[ComputeMethod]` already backing Ambient's `ListParticipants`, reused as the presence source for `Active`/`Ended` instead of a new mechanism.
- `GetState`'s existing self-heal tick (`computed.Invalidate(SelfHealDelay)`) — the new presence sync piggybacks on it rather than adding a background loop.
- `LiveSessionState.Conversation is null` (client-visible) replaces `Kind == Dialing` on the client, since the client `LiveSession` DTO has no `SessionStartedAt` of its own.

**New components and placement:** `CallerStatus`/`RingAck` are small, `Streaming`-domain-specific enums with no use outside call handling — placed in `ActualChat.Live` (`src/dotnet/Api/Live/`) alongside `CallStatus`/`CallInviteStatus`, not promoted to a shared project.

---

### Task 1: Reshape the enums and records; fix every resulting compile error

**Files:**
- Modify: `src/dotnet/Api/Live/LiveSessionKind.cs`
- Modify: `src/dotnet/Api/Live/LiveSessionState.cs:76,78`
- Modify: `src/dotnet/Api/Live/CallState.cs`
- Modify: `src/dotnet/Api/Live/CallInvite.cs`
- Create: `src/dotnet/Api/Live/CallerStatus.cs`
- Create: `src/dotnet/Api/Live/RingAck.cs`
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs` (every reference to the old shapes — see steps below)
- Modify: `src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs:356`
- Modify: `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs:169`
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatActivityUI.cs:57`
- Modify: `src/dotnet/UI.Blazor.App/Components/RightPanel/LiveSessionMemberList.razor:27`
- No test file changes yet — this task only makes the solution compile again with the new shapes; behavior changes (and their tests) come in later tasks. Existing tests that assert on old enum *values* will fail to compile and are fixed here too (listed in Step 8), but their *assertions* aren't rewritten to the new semantics until the task that actually changes that behavior.

**Interfaces:**
- Produces: `LiveSessionKind { Ambient, Call }`; `CallStatus { None, Dialing, Connecting, Active, NoAnswer, Declined, Canceled, Ended }`; `CallerStatus { Dialing, Active, Canceled, NoAnswer, Ended }`; `CallInviteStatus { New, Ringing, Accepted, Active, Declined, Missed, Ended }`; `RingAck { Received, Ringing, Busy }`; `CallState` with `CallerActiveAt`/`CallerEndedAt`/`CanceledAt`; `CallInvite` with `ActiveAt`/`EndedAt`/`Ack`/`AckAt`.

- [ ] **Step 1: `LiveSessionKind` drops `Dialing`**

Replace the whole file:

```csharp
namespace ActualChat.Live;

public enum LiveSessionKind
{
    Ambient = 0,
    Call = 1,
}
```

- [ ] **Step 2: `LiveSessionState.IsCall`/`IsDialing` redefined**

In `src/dotnet/Api/Live/LiveSessionState.cs`, replace:

```csharp
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsCall => Kind is LiveSessionKind.Call or LiveSessionKind.Dialing;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsDialing => Kind == LiveSessionKind.Dialing;
```

with:

```csharp
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsCall => Kind == LiveSessionKind.Call;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsDialing => Kind == LiveSessionKind.Call && SessionStartedAt is null;
```

- [ ] **Step 3: `CallState.cs` — reshape `CallStatus`, add new fields**

Replace the whole file:

```csharp
namespace ActualChat.Live;

public enum CallStatus
{
    None = 0,
    Dialing = 1,
    Connecting = 2,
    Active = 3,
    NoAnswer = 4,
    Declined = 5,
    Canceled = 6,
    Ended = 7,
}

/// <summary>
/// The caller-facing status of an outgoing call, kept for a short while past the session itself
/// so the caller can be told how it went. <see cref="CallStatus.None"/> is never stored - it is
/// the absence of this record.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record CallState
{
    [DataMember(Order = 0), Key(0)]
    public AuthorId CallerId { get; init; } = null!;
    [DataMember(Order = 1), Key(1)]
    public CallStatus Status { get; init; }
    [DataMember(Order = 2), Key(2)]
    public Moment ChangedAt { get; init; }
    [DataMember(Order = 3), Key(3)]
    public Moment? CallerActiveAt { get; init; }
    [DataMember(Order = 4), Key(4)]
    public Moment? CallerEndedAt { get; init; }
    [DataMember(Order = 5), Key(5)]
    public Moment? CanceledAt { get; init; }
}
```

- [ ] **Step 4: `CallInvite.cs` — reshape `CallInviteStatus`, add new fields**

Replace the whole file:

```csharp
namespace ActualChat.Live;

public enum CallInviteStatus
{
    New = 0,
    Ringing = 1,
    Accepted = 2,
    Active = 3,
    Declined = 4,
    Missed = 5,
    Ended = 6,
}

[DataContract, MessagePackObject]
public sealed partial record CallInvite
{
    [DataMember(Order = 0), Key(0)]
    public AuthorId InviteeId { get; init; } = null!;
    [DataMember(Order = 1), Key(1)]
    public CallInviteStatus Status { get; init; }
    [DataMember(Order = 2), Key(2)]
    public Moment RingingAt { get; init; }
    [DataMember(Order = 3), Key(3)]
    public Moment? RespondedAt { get; init; }
    [DataMember(Order = 4), Key(4)]
    public Moment? ActiveAt { get; init; }
    [DataMember(Order = 5), Key(5)]
    public Moment? EndedAt { get; init; }
    [DataMember(Order = 6), Key(6)]
    public RingAck? Ack { get; init; }
    [DataMember(Order = 7), Key(7)]
    public Moment? AckAt { get; init; }
}
```

- [ ] **Step 5: New `CallerStatus.cs`**

```csharp
namespace ActualChat.Live;

public enum CallerStatus
{
    Dialing = 0,
    Active = 1,
    Canceled = 2,
    NoAnswer = 3,
    Ended = 4,
}
```

- [ ] **Step 6: New `RingAck.cs`**

```csharp
namespace ActualChat.Live;

public enum RingAck
{
    Received = 0,
    Ringing = 1,
    Busy = 2,
}
```

- [ ] **Step 7: Build and fix every compile error in `LiveSessionsBackend.cs`**

Run: `dotnet build src/dotnet/Streaming.Service/Streaming.Service.csproj -c Debug` and fix each error. The known ones (line numbers as of this plan — re-locate at execution time):

  - `GetState` (~line 330, `OnStreamRegistered`): delete the now-meaningless branch
    ```csharp
    Kind = state.Kind == LiveSessionKind.Dialing ? LiveSessionKind.Call : state.Kind,
    ```
    — just drop the `Kind = ...,` line entirely (the record's `Kind` doesn't need touching here anymore; it's already `Call` once a call has started, and this method never runs for a fresh Dialing call before `StartCall`).
  - `StartCall` (~line 597): replace
    ```csharp
    Kind = state?.SessionStartedAt is not null ? LiveSessionKind.Call : LiveSessionKind.Dialing,
    ```
    with:
    ```csharp
    Kind = LiveSessionKind.Call,
    ```
  - `StartCall` (~line 603): replace
    ```csharp
    await SetCallState(chatId, state.IsDialing ? NewCallState(state, CallStatus.Dialing) : null)
        .ConfigureAwait(false);
    ```
    with:
    ```csharp
    await SetCallState(chatId, NewCallState(state, CallStatus.Dialing)).ConfigureAwait(false);
    ```
    (a promoted-from-ambient session is never mid-Dialing when `StartCall` runs on it, so the old guard was already always true in the interesting case — this task only needs it to compile; Task 4 revisits `NewCallState`'s signature.)
  - `AcceptCall` (~line 659): `CallStatus.Accepted` → leave as `CallStatus.Connecting` for now (Task 4 replaces this whole call with `RecomputeCallStatus`; for this task, just rename the enum reference so it compiles):
    ```csharp
    await SetCallState(chatId, NewCallState(state, CallStatus.Connecting)).ConfigureAwait(false);
    ```
  - `NewCallState` (~line 972): unchanged signature for this task (Task 4 extends it) — no edit needed here yet, it already takes `(LiveSessionState state, CallStatus status)`.
  - `NewCallState`'s TTL helper (~line 980): unchanged for this task — still keys off `CallStatus.Dialing`, still compiles.
  - `ExpireRings` (~line 1074): replace
    ```csharp
    if (freshState is { IsDialing: true } current
        && await SafeGetCallState(chatId).ConfigureAwait(false)
            is null or { Status: CallStatus.Dialing }) {
    ```
    — this already compiles unchanged (`IsDialing` still exists, `CallStatus.Dialing` still exists) — no edit needed.
  - `IsCallAbandoned`/`ParticipantCount`/`shouldCloseAsCall` (~lines 393, 868, 1130-1145): unchanged for this task (Task 2 removes `ParticipantCount`) — no edit needed here, they don't reference removed enum values.

  After these edits, `dotnet build` should report zero errors in `Streaming.Service`. If it reports any not listed above, fix them the same way (rename the enum value, don't change behavior) and note the new line numbers.

- [ ] **Step 8: Build and fix every compile error in `UI.Blazor.App` and tests**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj -c Debug` and fix:

  - `LiveSessionUI.cs:356`: replace
    ```csharp
    if (live is { Kind: LiveSessionKind.Dialing }) {
    ```
    with:
    ```csharp
    if (live is { Kind: LiveSessionKind.Call, Conversation: null }) {
    ```
  - `IncomingCallUI.cs:169`: replace
    ```csharp
    if (live is not { Kind: LiveSessionKind.Dialing })
    ```
    with:
    ```csharp
    if (live is not { Kind: LiveSessionKind.Call, Conversation: null })
    ```
  - `ChatActivityUI.cs:57`: replace
    ```csharp
    var isDialing = liveSession?.Kind == LiveSessionKind.Dialing;
    ```
    with:
    ```csharp
    var isDialing = liveSession is { Kind: LiveSessionKind.Call, Conversation: null };
    ```
  - `LiveSessionMemberList.razor:27`: replace
    ```csharp
    @if (live.Kind == LiveSessionKind.Dialing) {
    ```
    with:
    ```csharp
    @if (live.Kind == LiveSessionKind.Call && live.Conversation is null) {
    ```
  - `IncomingCallOverLockView.razor:261,266`, `IncomingCallUI.cs:386,449`: these check `callStatus is CallStatus.Dialing or CallStatus.Accepted` — leave the `CallStatus` references unchanged for this task (they still compile, since `Dialing` still exists and `Accepted` was renamed to `Connecting` — fix the rename):
    ```csharp
    return callStatus is CallStatus.Dialing or CallStatus.Connecting;
    ```
    (all three call sites — `IncomingCallOverLockView.razor:261`, `IncomingCallUI.cs:386`, `IncomingCallUI.cs:449` — get the same `Accepted` → `Connecting` rename). Task 7 replaces these three sites entirely with a `CallerStatus` check once `GetCallStatus`'s return type changes.

  Run: `dotnet build src/dotnet/Streaming.Contracts/Streaming.Contracts.csproj src/dotnet/Api.Contracts/Api.Contracts.csproj -c Debug` too (no changes expected yet — `ConfirmRing` is added in Task 6 — this just confirms nothing else references the old shapes from those projects).

  Then build the two test projects and fix any compile errors the same way (rename only, no assertion-value changes yet):

  ```bash
  dotnet build tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
  dotnet build tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
  dotnet build tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
  ```

  (Use `-p:ArtifactsPath` pointed at a scratch folder under `tmp/` if `/server-loop` is running, to avoid file-lock conflicts with its own build output — see `/server-loop`'s guidance. Delete the scratch folder when done.)

  Known compile fixes:
  - `tests/Chat.IntegrationTests/LiveSessionsTest.cs:669,1146,1180`: `LiveSessionKind.Dialing` → these assertions check the state right after `StartCall`, before any accept — under the new model `Kind` is `Call` immediately. Change `state!.Kind.Should().Be(LiveSessionKind.Dialing);` to `state!.Kind.Should().Be(LiveSessionKind.Call);` at all three sites (verify each still makes sense in context — a couple of these may need an added `state.SessionStartedAt.Should().BeNull();` alongside, to keep asserting "hasn't connected yet"; add it where the surrounding test's comment says that's the intent).
  - `tests/Chat.IntegrationTests/LiveSessionsTest.cs:939`: `CallStatus.Accepted` → `CallStatus.Connecting`.
  - `tests/Chat.IntegrationTests/LiveSessionsTest.cs:914,1017,1063,1096`: `CallStatus.Dialing` — unchanged, still compiles.
  - `tests/Chat.IntegrationTests/LiveSessionsTest.cs:1014`: `state!.IsDialing.Should().BeTrue();` — unchanged, still compiles (property redefined, same call site).
  - `tests/Chat.UI.Blazor.UnitTests/IncomingCallUITest.cs:55`: `Kind = LiveSessionKind.Dialing,` in a test fixture builder — replace with `Kind = LiveSessionKind.Call,` and check whether that fixture also needs a `Conversation = null` alongside it to keep representing "still dialing" for whatever the test exercises (read the surrounding fixture to confirm — if it builds a `LiveSession` with no `Conversation` set, the default is already `null` and nothing else is needed).
  - `tests/Chat.UI.Blazor.IntegrationTests/OutgoingCallStatusTest.cs:38,46`: leave `CallStatus.Dialing`/`CallStatus.Declined` unchanged for THIS task even though they'll semantically need to become `CallerStatus.Dialing`/`CallerStatus.NoAnswer` — that test doesn't compile-break in this task since `GetCallStatus` still returns `CallStatus` until Task 7. Task 7 rewrites this test's assertions.

- [ ] **Step 9: Run the full affected test suite to confirm no regressions from the rename alone**

```bash
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~LiveSessionsTest|FullyQualifiedName~CallEntryTest|FullyQualifiedName~CallModerationTest" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --filter "FullyQualifiedName~OutgoingCallStatusTest|FullyQualifiedName~CallConversationCardTest" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~IncomingCallUITest" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
```

Expected: all pass (this task changes no behavior — every value that used to be `Accepted` reads as `Connecting` now, every `Dialing`-Kind check reads as `Call`+`Conversation:null` now, but the underlying facts are identical). If anything fails, it's either a missed rename or a genuine behavior difference introduced by mistake — fix the former, stop and investigate the latter before proceeding.

- [ ] **Step 10: Commit**

```bash
git add src/dotnet/Api/Live/LiveSessionKind.cs src/dotnet/Api/Live/LiveSessionState.cs \
  src/dotnet/Api/Live/CallState.cs src/dotnet/Api/Live/CallInvite.cs \
  src/dotnet/Api/Live/CallerStatus.cs src/dotnet/Api/Live/RingAck.cs \
  src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs \
  src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs \
  src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs \
  src/dotnet/UI.Blazor.App/Services/ChatActivityUI.cs \
  src/dotnet/UI.Blazor.App/Components/RightPanel/LiveSessionMemberList.razor \
  src/dotnet/UI.Blazor.App/Components/IncomingCallModal/IncomingCallOverLockView.razor \
  tests/Chat.IntegrationTests/LiveSessionsTest.cs \
  tests/Chat.UI.Blazor.UnitTests/IncomingCallUITest.cs
git commit -m "$(cat <<'EOF'
refactor(call): drop LiveSessionKind.Dialing, reshape CallStatus/CallInviteStatus

Dialing becomes a phase of Call (Kind==Call && SessionStartedAt is
null) rather than a separate Kind. CallStatus gains Active/Canceled/
Ended and renames Accepted to Connecting (the invitee-level Accepted
keeps its name). CallInviteStatus gains New/Active/Ended. New
CallerStatus and RingAck types added, not yet wired up.

No behavior change - this task only reshapes types and fixes the
resulting compile errors.
EOF
)"
```

---

### Task 2: Collapse `ParticipantCount` into `GetConsolidatedParticipants`

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs:393,868,1130-1145`
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs` (existing coverage of `shouldCloseAsCall`/`EnforceCallConnectGrace` already exercises this path — no new test needed, just confirm the existing ones still pass)

**Interfaces:**
- Consumes: `GetConsolidatedParticipants(ChatId chatId, CancellationToken cancellationToken) -> Task<ApiArray<AuthorId>>` (already exists, `LiveSessionsBackend.cs:776`).
- Produces: `ParticipantCount` private helper removed; its three call sites now call `GetConsolidatedParticipants` directly.

- [ ] **Step 1: Replace all three `ParticipantCount` call sites**

In `SetParticipation` (~line 393):
```csharp
if (state is { Kind: LiveSessionKind.Call } callState) {
    if ((await GetConsolidatedParticipants(chatId, cancellationToken).ConfigureAwait(false)).Count < 2)
        shouldCloseAsCall = true;
```
(replace `await ParticipantCount(chatId).ConfigureAwait(false) < 2` — `SetParticipation` already has a `cancellationToken` parameter in scope, pass it through).

In `EnforceCallConnectGrace` (~line 868):
```csharp
if (state is { Kind: LiveSessionKind.Call }
    && (await GetConsolidatedParticipants(chatId, CancellationToken.None).ConfigureAwait(false)).Count < 2)
    shouldClose = true;
```
(`EnforceCallConnectGrace` is a fire-and-forget background method with no cancellation token of its own — use `CancellationToken.None`, matching the rest of that method's body).

In `IsCallAbandoned` (~line 1145):
```csharp
private async Task<bool> IsCallAbandoned(ChatId chatId)
{
    var invites = await SafeGetInvites(chatId).ConfigureAwait(false);
    if (invites.Values.Any(i => i is { Status: CallInviteStatus.Ringing }))
        return false;

    return (await GetConsolidatedParticipants(chatId, CancellationToken.None).ConfigureAwait(false)).Count < 2;
}
```

- [ ] **Step 2: Delete the now-unused `ParticipantCount` method**

Delete this method entirely (~lines 1130-1135):
```csharp
private async Task<int> ParticipantCount(ChatId chatId)
{
    var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
    var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
    return participants.Values.Count(p => IsFreshParticipant(p, cutoff));
}
```

- [ ] **Step 3: Build to confirm no remaining references**

```bash
dotnet build src/dotnet/Streaming.Service/Streaming.Service.csproj -c Debug
```
Expected: 0 errors. (`grep -n "ParticipantCount" src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs` should return nothing.)

- [ ] **Step 4: Run the existing tests that exercise these three call sites**

```bash
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~LiveSessionsTest" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
```
Expected: all pass, including `PresenceDropBelowTwoShouldCloseTheCall`, `PresenceDropOnAnAmbientSessionShouldNotCloseIt`, `AcceptedCallWithNoConnectionShouldCloseAfterGraceWindow`, `AcceptedCallWithAListenerShouldSurviveTheGraceWindow` — these are the tests that exercise `shouldCloseAsCall`/`EnforceCallConnectGrace`/`IsCallAbandoned` and must still behave identically (the filter logic is byte-for-byte the same, just no longer duplicated).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs
git commit -m "$(cat <<'EOF'
refactor(call): collapse ParticipantCount into GetConsolidatedParticipants

Both did the same SafeGetHashMap + IsFreshParticipant + staleness-
cutoff filter, one as a plain helper and one as a self-healing
[ComputeMethod]. One source of truth for "how many are active" now.
EOF
)"
```

---

### Task 3: `Derive` — the pure `CallStatus` recompute function, with unit coverage

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs` (add `Derive`, `RecomputeCallStatus`, extend `NewCallState`)
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs` (new tests calling `Derive` — see below for how to make it testable)

**Interfaces:**
- Produces:
  ```csharp
  internal static CallStatus Derive(CallState? callState, IReadOnlyCollection<CallInvite?> invites)
  private async Task RecomputeCallStatus(ChatId chatId, LiveSessionState state, CancellationToken cancellationToken)
  private CallState NewCallState(LiveSessionState state, CallStatus status, CallState? previous = null)
  ```
- Consumes: nothing new — reads `CallState`/`CallInvite` records already defined in Task 1.

`Derive` is `internal static` (not `private`) specifically so `LiveSessionsTest.cs`, which is in a different assembly (`Chat.IntegrationTests`), can call it directly via `[InternalsVisibleTo]` — check `src/dotnet/Streaming.Service/Streaming.Service.csproj` for an existing `InternalsVisibleTo` entry covering `Chat.IntegrationTests` before assuming one exists; if none does, this task adds one (see Step 1).

- [ ] **Step 1: Confirm (or add) `InternalsVisibleTo` for the test project**

```bash
grep -n "InternalsVisibleTo" src/dotnet/Streaming.Service/Streaming.Service.csproj
```

If it doesn't already include `ActualChat.Chat.IntegrationTests`, add it (check the exact assembly name other `InternalsVisibleTo` entries use in this file, e.g. via `<ItemGroup><InternalsVisibleTo Include="ActualChat.Chat.IntegrationTests" /></ItemGroup>`, and match that pattern rather than guessing the syntax).

- [ ] **Step 2: Write the failing tests for `Derive`**

Add to `tests/Chat.IntegrationTests/LiveSessionsTest.cs` (near the other `CallStatus`-related tests):

```csharp
[Fact]
public void DeriveReturnsDialingWithNoFactsYet()
{
    var status = LiveSessionsBackend.Derive(callState: null, invites: []);
    status.Should().Be(CallStatus.Dialing);
}

[Fact]
public void DeriveReturnsConnectingWhenAnInviteeAccepted()
{
    var invite = new CallInvite { InviteeId = AuthorId.New(), Status = CallInviteStatus.Accepted };
    var status = LiveSessionsBackend.Derive(callState: null, invites: [invite]);
    status.Should().Be(CallStatus.Connecting);
}

[Fact]
public void DeriveReturnsActiveWhenTwoAreGenuinelyPresent()
{
    var callState = new CallState { CallerId = AuthorId.New(), CallerActiveAt = Moment.Now };
    var invite = new CallInvite { InviteeId = AuthorId.New(), Status = CallInviteStatus.Active };
    var status = LiveSessionsBackend.Derive(callState, invites: [invite]);
    status.Should().Be(CallStatus.Active);
}

[Fact]
public void DeriveReturnsEndedOnceItWasActiveEvenIfNoLongerActive()
{
    // Caller was active, then dropped (CallerEndedAt set, CallerActiveAt still stamped from before) -
    // the invitee never became active at all.
    var callState = new CallState {
        CallerId = AuthorId.New(),
        CallerActiveAt = Moment.Now - TimeSpan.FromMinutes(1),
        CallerEndedAt = Moment.Now,
    };
    var invite = new CallInvite { InviteeId = AuthorId.New(), Status = CallInviteStatus.Ended };
    var status = LiveSessionsBackend.Derive(callState, invites: [invite]);
    status.Should().Be(CallStatus.Ended);
}

[Fact]
public void DeriveReturnsCanceledWhenCallerCanceledBeforeEverConnecting()
{
    var callState = new CallState { CallerId = AuthorId.New(), CanceledAt = Moment.Now };
    var status = LiveSessionsBackend.Derive(callState, invites: []);
    status.Should().Be(CallStatus.Canceled);
}

[Fact]
public void DeriveReturnsDeclinedWhenAnInviteeDeclinedAndNoneEverAccepted()
{
    var invite = new CallInvite { InviteeId = AuthorId.New(), Status = CallInviteStatus.Declined };
    var status = LiveSessionsBackend.Derive(callState: null, invites: [invite]);
    status.Should().Be(CallStatus.Declined);
}

[Fact]
public void DeriveReturnsNoAnswerWhenEveryInviteeMissed()
{
    var invite1 = new CallInvite { InviteeId = AuthorId.New(), Status = CallInviteStatus.Missed };
    var invite2 = new CallInvite { InviteeId = AuthorId.New(), Status = CallInviteStatus.Missed };
    var status = LiveSessionsBackend.Derive(callState: null, invites: [invite1, invite2]);
    status.Should().Be(CallStatus.NoAnswer);
}
```

- [ ] **Step 3: Run to verify they fail (Derive doesn't exist yet)**

```bash
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~DeriveReturns" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
```
Expected: compile error (`Derive` not found) — that's the "fails" state for a not-yet-existing static method; confirm the error names `Derive`/`LiveSessionsBackend`, then proceed.

- [ ] **Step 4: Implement `Derive`, `RecomputeCallStatus`, extend `NewCallState`**

In `LiveSessionsBackend.cs`, replace the `NewCallState` method (~line 972):

```csharp
private CallState NewCallState(LiveSessionState state, CallStatus status, CallState? previous = null)
    => new() {
        CallerId = state.Host ?? state.AuthorIds[0],
        Status = status,
        ChangedAt = Clocks.SystemClock.Now,
        CallerActiveAt = previous?.CallerActiveAt,
        CallerEndedAt = previous?.CallerEndedAt,
        CanceledAt = previous?.CanceledAt,
    };
```

Add `Derive` and `RecomputeCallStatus` right after it:

```csharp
internal static CallStatus Derive(CallState? callState, IReadOnlyCollection<CallInvite?> invites)
{
    var everActive = callState?.CallerActiveAt is not null
        || invites.Any(i => i?.ActiveAt is not null);
    var activeCount = (callState?.CallerActiveAt is not null && callState.CallerEndedAt is null ? 1 : 0)
        + invites.Count(i => i is { Status: CallInviteStatus.Active });

    if (activeCount >= 2)
        return CallStatus.Active;
    if (everActive)
        return CallStatus.Ended;
    if (callState?.CanceledAt is not null)
        return CallStatus.Canceled;
    if (invites.Any(i => i is { Status: CallInviteStatus.Accepted }))
        return CallStatus.Connecting;
    if (invites.Any(i => i is { Status: CallInviteStatus.Declined }))
        return CallStatus.Declined;
    if (invites.Count > 0 && invites.All(i => i is { Status: CallInviteStatus.Missed }))
        return CallStatus.NoAnswer;
    return CallStatus.Dialing;
}

private async Task RecomputeCallStatus(ChatId chatId, LiveSessionState state, CancellationToken cancellationToken)
{
    if (!state.IsCall)
        return;

    var callState = await SafeGetCallState(chatId).ConfigureAwait(false);
    var invites = (await SafeGetInvites(chatId).ConfigureAwait(false)).Values;
    var status = Derive(callState, invites);
    if (callState is null && status == CallStatus.Dialing)
        return; // nothing to persist yet - StartCall already wrote the initial Dialing CallState
    await SetCallState(chatId, NewCallState(state, status, callState)).ConfigureAwait(false);
}
```

(`RecomputeCallStatus` isn't wired into any call site yet — that's Task 4. This task only makes `Derive` exist and pass its own unit tests, per TDD: smallest unit first.)

- [ ] **Step 5: Run the new tests to verify they pass**

```bash
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~DeriveReturns" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
```
Expected: 7/7 pass.

- [ ] **Step 6: Full build to confirm `RecomputeCallStatus`/`NewCallState`'s new signature don't break existing callers**

```bash
dotnet build src/dotnet/Streaming.Service/Streaming.Service.csproj -c Debug
```
Expected: 0 errors (`NewCallState`'s new `previous` parameter has a default, so the two existing call sites from Task 1 — `StartCall`, `AcceptCall` — still compile unchanged).

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Streaming.Service/Streaming.Service.csproj tests/Chat.IntegrationTests/LiveSessionsTest.cs \
  src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs
git commit -m "$(cat <<'EOF'
feat(call): add Derive, the pure CallStatus recompute function

Not wired into any RPC handler yet - this task only introduces the
function and proves it correct in isolation, per TDD. Also extends
NewCallState to carry forward the presence facts (CallerActiveAt/
CallerEndedAt/CanceledAt) a fresh CallState would otherwise drop.
EOF
)"
```

---

### Task 4: Wire `RecomputeCallStatus` into `AcceptCall`, `DeclineCall`, `ExpireRings`, `EnforceCallConnectGrace`

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs`

**Interfaces:**
- Consumes: `RecomputeCallStatus(ChatId, LiveSessionState, CancellationToken)` from Task 3.
- Produces: `AcceptCall`/`DeclineCall`/`ExpireRings`/`EnforceCallConnectGrace` no longer call `SetCallState(..., CallStatus.X)` directly for these transitions.

- [ ] **Step 1: Write the failing test — accept advances to `Connecting`, second accept (group call) to `Active`**

Add to `tests/Chat.IntegrationTests/LiveSessionsTest.cs`:

```csharp
[Fact]
public async Task AcceptCallRecomputesStatusToConnecting()
{
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

    // act
    await backend.AcceptCall(chatId, aliceAuthor.Id, default);

    // assert
    var callState = await backend.GetCallState(chatId, default);
    callState!.Status.Should().Be(CallStatus.Connecting);
}

[Fact]
public async Task ExpireRingsRecomputesStatusToNoAnswer()
{
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

    // act - drive the timeout directly, mirroring how ExpireRingsTest-style tests already do
    await backend.ExpireRings(chatId);

    // assert - the ring hasn't actually timed out yet (RingTimeout is 20s), so nothing should
    // have changed; this proves RecomputeCallStatus is a no-op when nothing new happened
    var callState = await backend.GetCallState(chatId, default);
    callState!.Status.Should().Be(CallStatus.Dialing);
}
```

Check whether `ILiveSessionsBackend` already exposes `GetCallState` publicly (it's used as `backend.GetCallState(...)` above) — if it's `internal`/private today, add a public `Task<CallState?> GetCallState(ChatId chatId, CancellationToken cancellationToken)` to `ILiveSessionsBackend` (check first with `grep -n "GetCallState" src/dotnet/Streaming.Contracts/ILiveSessionsBackend.cs` — if a public wrapper already exists under a different name, e.g. it's exposed indirectly through `LiveSessions.GetCallStatus`, use that existing accessor pattern instead of adding a new one, and adjust the test assertions to go through it).

- [ ] **Step 2: Run to verify the first test fails (accept still writes `Connecting` today, so this might already pass — verify by temporarily reverting to see red, or reason about it: today `AcceptCall` already sets `CallStatus.Connecting` via `NewCallState(state, CallStatus.Connecting)` from Task 1's rename, so this specific assertion is not yet a meaningful red — the point of this task's wiring is what the SECOND accept in a group call does, and what a presence-driven change does, neither of which today's direct-write handles)**

Given the direct-write from Task 1 already produces the right value for a *first* accept, add one more test that only passes once `RecomputeCallStatus` (not a direct write) is wired in — a **second** accept in a 3-person call, verifying the status becomes `Connecting` from BOTH invites' facts, not just the most recent write:

```csharp
[Fact]
public async Task SecondInviteeAcceptingStaysConnectingNotOverwritten()
{
    // arrange - a group call: Owner calls Moderator and Member
    await using var owner = AppHost.NewBlazorTester(Out);
    await using var member1 = AppHost.NewBlazorTester(Out);
    await using var member2 = AppHost.NewBlazorTester(Out);
    await owner.SignInAsUniqueBob();
    await member1.SignInAsUniqueAlice();
    await member2.SignInAsUniqueAlice();
    var (chatId, inviteId) = await owner.CreateChat(false);
    var member1Author = await member1.JoinChat(chatId, inviteId);
    var member2Author = await member2.JoinChat(chatId, inviteId);
    var ownerAuthor = await owner.GetOwnAuthor(chatId);
    var backend = owner.AppServices.GetRequiredService<ILiveSessionsBackend>();
    var invitees = new[] { member1Author.Id, member2Author.Id }.ToApiArray();
    await backend.StartCall(chatId, ownerAuthor!.Id, invitees, false, default);

    // act
    await backend.AcceptCall(chatId, member1Author.Id, default);
    await backend.AcceptCall(chatId, member2Author.Id, default);

    // assert - still Connecting (nobody's presence has actually registered yet), not reverted
    // to Dialing by the second accept overwriting the first's effect
    var callState = await backend.GetCallState(chatId, default);
    callState!.Status.Should().Be(CallStatus.Connecting);
}
```

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~SecondInviteeAccepting" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"`. This should currently PASS too (today's direct write also happens to leave it at `Connecting` after either accept) — that's fine, TDD's "see it fail" step isn't always available when refactoring an already-working path; the value of Steps 1-2's tests is regression coverage for Step 3's change, not a strict red-green cycle here. Proceed to Step 3.

- [ ] **Step 3: Wire `RecomputeCallStatus` into the four call sites**

In `AcceptCall`, replace:
```csharp
await SetCallState(chatId, NewCallState(state, CallStatus.Connecting)).ConfigureAwait(false);
```
with:
```csharp
await RecomputeCallStatus(chatId, state, cancellationToken).ConfigureAwait(false);
```
(`AcceptCall` already has `cancellationToken` in scope.)

In `DeclineCall`, replace:
```csharp
await SetCallState(chatId, NewCallState(state, CallStatus.Declined)).ConfigureAwait(false);
```
with:
```csharp
await RecomputeCallStatus(chatId, state, cancellationToken).ConfigureAwait(false);
```

In `ExpireRings`'s abandon-check block, replace:
```csharp
await SetCallState(chatId, NewCallState(current, CallStatus.NoAnswer)).ConfigureAwait(false);
await SetOutcome(chatId, current, CallOutcome.NoAnswer).ConfigureAwait(false);
```
with:
```csharp
await RecomputeCallStatus(chatId, current, CancellationToken.None).ConfigureAwait(false);
await SetOutcome(chatId, current, CallOutcome.NoAnswer).ConfigureAwait(false);
```
(keep `SetOutcome` — that's the permanent `CallOutcome` record, unrelated to `CallState.Status`.)

In `EnforceCallConnectGrace`, after the existing `shouldClose` check, add a call to recompute on the non-closing path too — the method currently only ever sets `shouldClose`; extend it:
```csharp
internal async Task EnforceCallConnectGrace(ChatId chatId)
{
    try {
        var shouldClose = false;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false)) {
            var state = await SafeGet(chatId).ConfigureAwait(false);
            if (state is { Kind: LiveSessionKind.Call }) {
                if ((await GetConsolidatedParticipants(chatId, CancellationToken.None).ConfigureAwait(false)).Count < 2)
                    shouldClose = true;
                else
                    await RecomputeCallStatus(chatId, state, CancellationToken.None).ConfigureAwait(false);
            }
        }
        if (shouldClose)
            await CloseCall(chatId).ConfigureAwait(false);
    }
    catch (Exception e) when (e is not OperationCanceledException) {
        Log.LogWarning(e, "EnforceCallConnectGrace failed for chat #{ChatId}", chatId);
    }
}
```

- [ ] **Step 4: Run all four new/updated tests plus the full `LiveSessionsTest`/`CallEntryTest`/`CallModerationTest` suite**

```bash
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~LiveSessionsTest|FullyQualifiedName~CallEntryTest|FullyQualifiedName~CallModerationTest" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
```
Expected: all pass, including the new tests from Steps 1-2 and every pre-existing test (this task must not change any observable behavior for the cases already covered — it only changes *how* the same values get produced).

- [ ] **Step 5: Commit**

```bash
git add tests/Chat.IntegrationTests/LiveSessionsTest.cs src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs \
  src/dotnet/Streaming.Contracts/ILiveSessionsBackend.cs
git commit -m "$(cat <<'EOF'
feat(call): wire RecomputeCallStatus into AcceptCall/DeclineCall/ExpireRings/EnforceCallConnectGrace

Each now records its own fact (CallInviteStatus already did; nothing
new to record for Accept/Decline/ExpireRings beyond what they already
write) and calls RecomputeCallStatus instead of picking CallStatus
directly. EnforceCallConnectGrace additionally now advances status to
Active on success, not just closes on failure.
EOF
)"
```

---

### Task 5: The `GetConsolidatedParticipants`-driven `Active`/`Ended` sync

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs`

This is the task that makes a real hang-up (or a real crash) during a call actually move the invitee's/caller's status to `Ended`, and the aggregate `CallStatus` to `Active`/`Ended` for real, not just from the artificial `Accept`/`Decline` cases Task 4 covered.

**Interfaces:**
- Consumes: `GetConsolidatedParticipants` (existing), `RecomputeCallStatus` (Task 3).
- Produces:
  ```csharp
  internal async Task SyncCallParticipantActivity(ChatId chatId, LiveSessionState state, CancellationToken cancellationToken)
  ```
  called from `GetState`'s self-heal block; `internal` (not `private`) so a test can drive it directly without waiting on `GetState`'s own reactive tick, mirroring `EnforceCallConnectGrace`'s and `ExpireRings`'s existing convention (see the comment on each: "Internal so a test can drive it directly, without a real wait").

- [ ] **Step 1: Write the failing test — a genuinely-present invitee gets marked `Active`, then `Ended` when they drop**

Add to `tests/Chat.IntegrationTests/LiveSessionsTest.cs`:

```csharp
[Fact]
public async Task SyncMarksInviteeActiveThenEndedFromRealPresence()
{
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
    await backend.AcceptCall(chatId, aliceAuthor.Id, default);

    // act - Alice's presence genuinely registers
    await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, true, default);
    var state = await backend.GetState(chatId, default);
    await backend.SyncCallParticipantActivity(chatId, state!, default);

    // assert - promoted straight to Active, not stuck at Accepted
    var invites = await backend.SafeGetInvitesForTest(chatId);
    invites[aliceAuthor.Id.Value]!.Status.Should().Be(CallInviteStatus.Active);

    // act - Alice's presence deactivates
    await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, false, default);
    state = await backend.GetState(chatId, default);
    await backend.SyncCallParticipantActivity(chatId, state!, default);

    // assert - Ended, not reverted to Accepted or left at Active
    invites = await backend.SafeGetInvitesForTest(chatId);
    invites[aliceAuthor.Id.Value]!.Status.Should().Be(CallInviteStatus.Ended);
}

[Fact]
public async Task SyncNeverReactivatesAnEndedInvitee()
{
    // arrange - same setup as above, driven straight to Ended
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
    await backend.AcceptCall(chatId, aliceAuthor.Id, default);
    await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, true, default);
    var state = await backend.GetState(chatId, default);
    await backend.SyncCallParticipantActivity(chatId, state!, default);
    await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, false, default);
    state = await backend.GetState(chatId, default);
    await backend.SyncCallParticipantActivity(chatId, state!, default);

    // act - Alice's presence somehow comes back (e.g. a stray late heartbeat)
    await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, true, default);
    state = await backend.GetState(chatId, default);
    await backend.SyncCallParticipantActivity(chatId, state!, default);

    // assert - stays Ended, not resurrected to Active
    var invites = await backend.SafeGetInvitesForTest(chatId);
    invites[aliceAuthor.Id.Value]!.Status.Should().Be(CallInviteStatus.Ended);
}
```

`SafeGetInvitesForTest` doesn't exist yet — `SafeGetInvites` is `private`. Either (a) add an `internal` passthrough `internal Task<Dictionary<string, CallInvite?>> SafeGetInvitesForTest(ChatId chatId) => SafeGetInvites(chatId);` right next to `SafeGetInvites` for this test's use, matching the existing `internal`-for-testability convention (`ExpireRings`, `EnforceCallConnectGrace`), or (b) if `ILiveSessionsBackend`/`GetState` already exposes invite data through `Get(chatId, ...)`'s `LiveSession.Invites` list (it does — `LiveSession.cs:30`, `IReadOnlyList<CallInvite> Invites`), read through that instead and drop the internal helper. Prefer (b) if `Get(chatId, ct)` already surfaces `Status`/enough fields on each `CallInvite` in `Invites` to assert on — check `LiveSessionsBackend.Get`'s body (~line 213-218, where it builds `invites`) to confirm the full `CallInvite` record (not just a projection) ends up in `LiveSession.Invites`; if so, use `(await backend.Get(chatId, default))!.Invites.Single(i => i.InviteeId == aliceAuthor.Id).Status` in the tests instead of a new internal helper.

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~SyncMarksInviteeActive|FullyQualifiedName~SyncNeverReactivates" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
```
Expected: compile error (`SyncCallParticipantActivity` doesn't exist).

- [ ] **Step 3: Implement `SyncCallParticipantActivity`**

Add to `LiveSessionsBackend.cs`, near `EnforceCallConnectGrace`:

```csharp
// Drives CallInviteStatus.Active/Ended (and the caller-side equivalent on CallState) from genuine
// presence, via the same GetConsolidatedParticipants Ambient sessions already use - never from a
// raw participant count, and never written inline from SetParticipation, which has no way to detect
// a silent crash. Internal so a test can drive it directly, without a real self-heal wait.
internal async Task SyncCallParticipantActivity(ChatId chatId, LiveSessionState state, CancellationToken cancellationToken)
{
    if (!state.IsCall)
        return;

    var freshAuthorIds = (await GetConsolidatedParticipants(chatId, cancellationToken).ConfigureAwait(false))
        .ToHashSet();
    bool changed;
    using (Computed.BeginIsolation())
    using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
        changed = await SyncCallerActivity(chatId, freshAuthorIds).ConfigureAwait(false);
        changed |= await SyncInviteeActivity(chatId, freshAuthorIds).ConfigureAwait(false);
    }
    if (changed)
        await RecomputeCallStatus(chatId, state, cancellationToken).ConfigureAwait(false);
}

// Caller must hold the change lock.
private async Task<bool> SyncCallerActivity(ChatId chatId, HashSet<AuthorId> freshAuthorIds)
{
    var state = await SafeGet(chatId).ConfigureAwait(false);
    if (state is null)
        return false;

    var callerId = state.CallerId ?? state.Host;
    var callState = await SafeGetCallState(chatId).ConfigureAwait(false);
    var isFresh = freshAuthorIds.Contains(callerId);
    var wasActive = callState is { CallerActiveAt: not null, CallerEndedAt: null };
    var isTerminal = callState?.CanceledAt is not null;

    if (isFresh && !wasActive && !isTerminal) {
        await SetCallState(chatId, (callState ?? NewCallState(state, CallStatus.Dialing)) with {
            CallerActiveAt = callState?.CallerActiveAt ?? Clocks.SystemClock.Now,
        }).ConfigureAwait(false);
        return true;
    }
    if (!isFresh && wasActive) {
        await SetCallState(chatId, callState! with { CallerEndedAt = Clocks.SystemClock.Now })
            .ConfigureAwait(false);
        return true;
    }
    if (isFresh && isTerminal)
        Log.LogWarning(
            "SyncCallParticipantActivity: caller #{AuthorId} of chat #{ChatId} reactivated after a terminal status - ignored",
            callerId, chatId);
    return false;
}

// Caller must hold the change lock.
private async Task<bool> SyncInviteeActivity(ChatId chatId, HashSet<AuthorId> freshAuthorIds)
{
    var changed = false;
    var invites = await SafeGetInvites(chatId).ConfigureAwait(false);
    foreach (var (authorIdValue, invite) in invites) {
        if (invite is null || !AuthorId.TryParse(authorIdValue, out var authorId))
            continue;

        var isFresh = freshAuthorIds.Contains(authorId);
        var now = Clocks.SystemClock.Now;
        switch (invite.Status) {
            case CallInviteStatus.Ringing or CallInviteStatus.Accepted when isFresh:
                await _invites.Set(chatId.Value, authorIdValue,
                        invite with { Status = CallInviteStatus.Active, ActiveAt = now })
                    .ConfigureAwait(false);
                changed = true;
                break;
            case CallInviteStatus.Active when !isFresh:
                await _invites.Set(chatId.Value, authorIdValue,
                        invite with { Status = CallInviteStatus.Ended, EndedAt = now })
                    .ConfigureAwait(false);
                changed = true;
                break;
            case CallInviteStatus.Declined or CallInviteStatus.Missed or CallInviteStatus.Ended when isFresh:
                Log.LogWarning(
                    "SyncCallParticipantActivity: invitee #{AuthorId} of chat #{ChatId} reactivated "
                    + "after status {Status} - ignored", authorId, chatId, invite.Status);
                break;
        }
    }
    if (changed)
        InvalidateGet(chatId);
    return changed;
}
```

- [ ] **Step 4: Run the new tests to verify they pass**

```bash
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~SyncMarksInviteeActive|FullyQualifiedName~SyncNeverReactivates" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
```
Expected: both pass.

- [ ] **Step 5: Wire the sync into `GetState`'s self-heal**

In `GetState` (~lines 116-122, right where `ExpireRings` is already fired):

```csharp
if (state.IsCall)
    _ = SyncCallParticipantActivity(chatId, state, CancellationToken.None);
if (state.IsCall && await HasStaleRinging(chatId).ConfigureAwait(false))
    _ = ExpireRings(chatId);
else if (state.IsDialing && !await HasFreshRing(chatId).ConfigureAwait(false))
    _ = ExpireRings(chatId);
```

(fire-and-forget, matching `ExpireRings`'s own `_ = ExpireRings(chatId);` style right below it — `GetState` must not await either, since both are meant to run without blocking the read that triggered them.)

- [ ] **Step 6: Write an end-to-end test through `GetState`'s self-heal (not calling the internal sync directly)**

```csharp
[Fact]
public async Task GetStateSelfHealSyncsCallParticipantActivity()
{
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
    await backend.AcceptCall(chatId, aliceAuthor.Id, default);
    await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, true, default);

    // act - GetState's own self-heal should pick this up without any direct sync call
    await backend.GetState(chatId, default);

    // assert
    var live = await backend.Get(chatId, default);
    live!.Invites.Single(i => i.InviteeId == aliceAuthor.Id).Status.Should().Be(CallInviteStatus.Active);
}
```

(`GetState` is likely `private`/`protected` — if it's not callable from the test, exercise it through `backend.Get(chatId, default)` instead, which already calls `GetState` internally per the earlier reading of `Get`'s body — adjust the "act" line to `await backend.Get(chatId, default);` if `GetState` itself isn't reachable, and keep the assertion as-is.)

- [ ] **Step 7: Run it, plus the full `LiveSessionsTest`/`CallEntryTest`/`CallModerationTest` suite**

```bash
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~LiveSessionsTest|FullyQualifiedName~CallEntryTest|FullyQualifiedName~CallModerationTest" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
```
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs tests/Chat.IntegrationTests/LiveSessionsTest.cs
git commit -m "$(cat <<'EOF'
feat(call): sync CallInviteStatus.Active/Ended from GetConsolidatedParticipants

Wired into GetState's existing self-heal tick, alongside ExpireRings.
A real hang-up (SetParticipation invalidates GetConsolidatedParticipants
immediately) and a silent crash (its own staleness self-heal) both now
resolve to Ended - previously nothing would ever do this for a crash.
Reactivation after a terminal status is a ratchet: logged, not applied.
EOF
)"
```

---

### Task 6: Signal validation — reject and log out-of-order participant signals

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs`

**Interfaces:**
- Produces: `private bool EnsureValidTransition(ChatId chatId, AuthorId authorId, string signalName, bool isValid, object currentStatus)` (a tiny logging helper — returns `isValid` unchanged so call sites can write `if (!EnsureValidTransition(...)) return;`).

- [ ] **Step 1: Write the failing test — a decline after already-accepted logs a warning and no-ops**

Add to `tests/Chat.IntegrationTests/LiveSessionsTest.cs`. This test needs to observe a log message — follow the existing pattern in this test class for asserting on logged warnings (search the file first: `grep -n "LogWarning\|ITestOutputHelper\|LogEntries" tests/Chat.IntegrationTests/LiveSessionsTest.cs` to find how other tests in this same file already capture/assert log output, and mirror that exact pattern here rather than inventing a new one). If no existing test in this file asserts on log content, fall back to asserting only the observable *behavior* (the decline is a no-op) and skip asserting the log line itself:

```csharp
[Fact]
public async Task DeclineAfterAcceptIsRejectedAsInvalidTransition()
{
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
    await backend.AcceptCall(chatId, aliceAuthor.Id, default);

    // act - a stale Decline arrives after Alice already accepted
    await backend.DeclineCall(chatId, aliceAuthor.Id, default);

    // assert - no-op, stays Accepted (today's behavior, must not regress)
    var live = await backend.Get(chatId, default);
    live!.Invites.Single(i => i.InviteeId == aliceAuthor.Id).Status.Should().Be(CallInviteStatus.Accepted);
}

[Fact]
public async Task CancelCallAfterActiveIsRejectedAsInvalidTransition()
{
    // arrange - a connected call
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
    await backend.SetParticipation(chatId, bobAuthor.Id, ParticipationKind.Record, true, default);
    await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, true, default);
    await backend.Get(chatId, default); // let GetState's self-heal promote both invites to Active

    // act - CancelCall arrives late, after the call is genuinely connected
    await backend.CancelCall(chatId, bobAuthor.Id, default);

    // assert - the session is untouched by CancelCall; it's still there and still Active
    var state = await backend.GetState(chatId, default);
    state.Should().NotBeNull();
}
```

- [ ] **Step 2: Run to verify current behavior (these may already pass by accident — today's silent guards already no-op both cases; the point of this task is adding the *log*, not changing the no-op)**

```bash
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~IsRejectedAsInvalidTransition" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
```
If both already pass, that confirms the no-op behavior is intact before this task's logging is added — proceed to Step 3. If `CancelCallAfterActiveIsRejectedAsInvalidTransition` fails because `CancelCall` doesn't currently guard against being called after `Active` at all (re-read `CancelCall`'s current body — it has no status check today, only `if (state is null) return;`), that's the actual gap this task closes — note it and proceed; the test's assertion should still hold once Step 3 adds the guard.

- [ ] **Step 3: Add `EnsureValidTransition` and wire it into `AcceptCall`, `DeclineCall`, `CancelCall`**

Add near `RecomputeCallStatus`:

```csharp
private bool EnsureValidTransition<TStatus>(ChatId chatId, AuthorId authorId, string signalName, TStatus currentStatus, bool isValid)
    where TStatus : Enum
{
    if (isValid)
        return true;

    Log.LogWarning(
        "{SignalName} rejected for chat #{ChatId}, author #{AuthorId}: not valid from status {CurrentStatus}",
        signalName, chatId, authorId, currentStatus);
    return false;
}
```

In `AcceptCall`, the existing guard:
```csharp
var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
if (invite is not { Status: CallInviteStatus.Ringing })
    return;
```
becomes:
```csharp
var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
if (!EnsureValidTransition(chatId, inviteeAuthorId, nameof(AcceptCall),
        invite?.Status ?? CallInviteStatus.New, invite is { Status: CallInviteStatus.Ringing }))
    return;
```

In `DeclineCall`, the same shape:
```csharp
var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
if (!EnsureValidTransition(chatId, inviteeAuthorId, nameof(DeclineCall),
        invite?.Status ?? CallInviteStatus.New, invite is { Status: CallInviteStatus.Ringing }))
    return;
```

In `CancelCall`, add the guard that doesn't exist today (right after `var state = await SafeGet(chatId).ConfigureAwait(false); if (state is null) return;`):
```csharp
var callState = await SafeGetCallState(chatId).ConfigureAwait(false);
var validStatuses = callState is null or { Status: CallStatus.Dialing or CallStatus.Connecting };
if (!EnsureValidTransition(chatId, callerAuthorId, nameof(CancelCall),
        callState?.Status ?? CallStatus.None, validStatuses))
    return;
```

- [ ] **Step 4: Run the two tests from Step 1 plus the full suite**

```bash
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~IsRejectedAsInvalidTransition|FullyQualifiedName~LiveSessionsTest|FullyQualifiedName~CallEntryTest|FullyQualifiedName~CallModerationTest" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
```
Expected: all pass. Pay particular attention to any existing test that calls `CancelCall` on an already-`Active` call expecting it to actually cancel — if one exists and now fails, that test was relying on behavior this task deliberately closes off (per the spec's Signal Validation section: "hanging up an `Active` call goes through the ordinary presence path, not `CancelCall`") — fix the test to use the presence path (`SetParticipation(..., isActive: false, ...)`) instead of `CancelCall`, don't loosen the new guard.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs tests/Chat.IntegrationTests/LiveSessionsTest.cs
git commit -m "$(cat <<'EOF'
feat(call): validate signals against current status, log instead of silent no-op

AcceptCall/DeclineCall already silently no-opped an out-of-order
signal; this makes the check explicit and logs a warning (signal +
current status) instead. CancelCall gains a guard it didn't have
before: it's no longer valid once the call reached Active.
EOF
)"
```

---

### Task 7: `ConfirmRing`/`RingAck` — the new client-ack RPC

**Files:**
- Modify: `src/dotnet/Api.Contracts/Streaming/ILiveSessions.cs`
- Modify: `src/dotnet/Streaming.Contracts/ILiveSessionsBackend.cs`
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`
- Modify: `src/dotnet/Streaming.Service/Services/LiveSessions.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs`
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs`

**Interfaces:**
- Produces:
  ```csharp
  // ILiveSessions
  Task ConfirmRing(Session session, ChatId chatId, RingAck ack, CancellationToken cancellationToken);
  // ILiveSessionsBackend
  Task ConfirmRing(ChatId chatId, AuthorId inviteeAuthorId, RingAck ack, CancellationToken cancellationToken);
  // LiveSessionUI
  Task ConfirmRing(ChatId chatId, RingAck ack, CancellationToken cancellationToken);
  ```

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task ConfirmRingRecordsAckOnTheInvite()
{
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

    // act
    await backend.ConfirmRing(chatId, aliceAuthor.Id, RingAck.Ringing, default);

    // assert
    var live = await backend.Get(chatId, default);
    var invite = live!.Invites.Single(i => i.InviteeId == aliceAuthor.Id);
    invite.Ack.Should().Be(RingAck.Ringing);
    invite.AckAt.Should().NotBeNull();

    // assert - does not change the invite's own business status or the call's aggregate status
    invite.Status.Should().Be(CallInviteStatus.Ringing);
    (await backend.GetCallState(chatId, default))!.Status.Should().Be(CallStatus.Dialing);
}

[Fact]
public async Task ConfirmRingIsANoOpOnceAlreadyResponded()
{
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
    await backend.AcceptCall(chatId, aliceAuthor.Id, default);

    // act - a late ack arrives after Alice already accepted
    await backend.ConfirmRing(chatId, aliceAuthor.Id, RingAck.Busy, default);

    // assert - ignored, invite unchanged
    var live = await backend.Get(chatId, default);
    var invite = live!.Invites.Single(i => i.InviteeId == aliceAuthor.Id);
    invite.Ack.Should().BeNull();
}
```

- [ ] **Step 2: Run to verify they fail**

```bash
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~ConfirmRing" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
```
Expected: compile error (`ConfirmRing` doesn't exist on `ILiveSessionsBackend`).

- [ ] **Step 3: Add `ConfirmRing` to the contracts**

`src/dotnet/Streaming.Contracts/ILiveSessionsBackend.cs`, add next to `DeclineCall`:
```csharp
Task DeclineCall(ChatId chatId, AuthorId inviteeAuthorId, CancellationToken cancellationToken);
Task ConfirmRing(ChatId chatId, AuthorId inviteeAuthorId, RingAck ack, CancellationToken cancellationToken);
```

`src/dotnet/Api.Contracts/Streaming/ILiveSessions.cs`, add next to `DeclineCall`:
```csharp
Task DeclineCall(Session session, ChatId chatId, CancellationToken cancellationToken);
Task ConfirmRing(Session session, ChatId chatId, RingAck ack, CancellationToken cancellationToken);
```

- [ ] **Step 4: Implement the backend method**

In `LiveSessionsBackend.cs`, add next to `DeclineCall`:
```csharp
public virtual async Task ConfirmRing(
    ChatId chatId, AuthorId inviteeAuthorId, RingAck ack, CancellationToken cancellationToken)
{
    using (Computed.BeginIsolation())
    using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
        var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
        if (!EnsureValidTransition(chatId, inviteeAuthorId, nameof(ConfirmRing),
                invite?.Status ?? CallInviteStatus.New, invite is { Status: CallInviteStatus.Ringing }))
            return;

        await _invites.Set(chatId.Value, inviteeAuthorId.Value,
                invite! with { Ack = ack, AckAt = Clocks.SystemClock.Now })
            .ConfigureAwait(false);
        InvalidateGet(chatId);
    }
}
```

- [ ] **Step 5: Implement the service passthrough**

In `src/dotnet/Streaming.Service/Services/LiveSessions.cs`, add next to `DeclineCall`:
```csharp
public async Task DeclineCall(Session session, ChatId chatId, CancellationToken cancellationToken)
{
    if (await RequireOwnAuthorId(session, chatId, cancellationToken).ConfigureAwait(false) is { } authorId)
        await Backend.DeclineCall(chatId, authorId, cancellationToken).ConfigureAwait(false);
}

public async Task ConfirmRing(Session session, ChatId chatId, RingAck ack, CancellationToken cancellationToken)
{
    if (await RequireOwnAuthorId(session, chatId, cancellationToken).ConfigureAwait(false) is { } authorId)
        await Backend.ConfirmRing(chatId, authorId, ack, cancellationToken).ConfigureAwait(false);
}
```

(Check `DeclineCall`'s exact current body first with `grep -n "public async Task DeclineCall" -A 4 src/dotnet/Streaming.Service/Services/LiveSessions.cs` — reproduce its exact pattern, not the guess above, if it differs.)

- [ ] **Step 6: Add the client-side `LiveSessionUI` wrapper**

In `src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs`, add next to `DeclineCall`:
```csharp
public Task ConfirmRing(ChatId chatId, RingAck ack, CancellationToken cancellationToken)
    => LiveSessions.ConfirmRing(Session, chatId, ack, cancellationToken);
```

(No caller wired up yet — per the spec's Open Item 2, the actual client-side hook that fires this on ring receipt is separate client work, out of scope for this plan. This step only makes the RPC reachable from the client for whenever that follow-up lands.)

- [ ] **Step 7: Run the tests to verify they pass, plus the full suite**

```bash
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~ConfirmRing|FullyQualifiedName~LiveSessionsTest|FullyQualifiedName~CallEntryTest|FullyQualifiedName~CallModerationTest" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
```
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/Api.Contracts/Streaming/ILiveSessions.cs src/dotnet/Streaming.Contracts/ILiveSessionsBackend.cs \
  src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs src/dotnet/Streaming.Service/Services/LiveSessions.cs \
  src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs tests/Chat.IntegrationTests/LiveSessionsTest.cs
git commit -m "$(cat <<'EOF'
feat(call): add ConfirmRing/RingAck client-ack RPC

Purely informational - records Ack/AckAt on the CallInvite, never
changes CallInviteStatus/CallStatus or server behavior. The client
hook that actually calls this on ring receipt is separate, later work.
EOF
)"
```

---

### Task 8: `CallerStatus` projection and client call-site updates

**Files:**
- Modify: `src/dotnet/Streaming.Service/Services/LiveSessions.cs:95-105`
- Modify: `src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs:147-148`
- Modify: `src/dotnet/UI.Blazor.App/Components/Banners/OutgoingCallBanner.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/IncomingCallModal/IncomingCallOverLockView.razor:261,266`
- Modify: `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs:386,449`
- Test: `tests/Chat.UI.Blazor.IntegrationTests/OutgoingCallStatusTest.cs`

**Interfaces:**
- Produces: `LiveSessions.GetCallStatus` and `LiveSessionUI.GetCallStatus` now return `Task<CallerStatus?>` instead of `Task<CallStatus>` (nullable — `null` means "no outgoing call to show").

- [ ] **Step 1: Write the updated test — this is the one place the new projection is directly observable**

Replace `tests/Chat.UI.Blazor.IntegrationTests/OutgoingCallStatusTest.cs`'s body:

```csharp
    [Fact]
    public async Task DeclineSurfacesStatusThroughLiveSessionUI()
    {
        // arrange — Bob rings Alice; Bob's banner observes the status via the client-side LiveSessionUI
        await Bob.SignInAsUniqueBob();
        await Alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Bob.CreateChat(false);
        await Alice.JoinChat(chatId, inviteId);
        var bobAuthor = await Bob.GetOwnAuthor(chatId);
        var aliceAuthor = await Alice.GetOwnAuthor(chatId);
        var liveSessionUI = Bob.ScopedAppServices.AppUIHub().LiveSessionUI;
        var backend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();

        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);
        var cStatus = await Computed.Capture(
            () => liveSessionUI.GetCallStatus(chatId, CancellationToken.None));
        cStatus.Value.Should().Be(CallerStatus.Dialing);

        // act — Alice declines
        await backend.DeclineCall(chatId, aliceAuthor.Id, default);

        // assert — the client compute flips Dialing → NoAnswer on its own, without a fresh Capture
        // (CallerStatus doesn't distinguish "declined" from "timed out" - both read the same to the caller)
        await ComputedTest.When(async ct => {
            var status = await liveSessionUI.GetCallStatus(chatId, ct);
            status.Should().Be(CallerStatus.NoAnswer);
        }, TimeSpan.FromSeconds(10));
    }
```

(Only the two `.Should().Be(...)` lines and the comment above the second one change — everything else in the file is untouched.)

- [ ] **Step 2: Run to verify it fails to compile (return type is still `CallStatus`)**

```bash
dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --filter "FullyQualifiedName~OutgoingCallStatusTest" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
```
Expected: compile error (`CallStatus.Dialing` vs. the expression's actual `CallStatus` type is fine today, but `CallerStatus.Dialing` doesn't match `GetCallStatus`'s still-`CallStatus` return type yet).

- [ ] **Step 3: Change `LiveSessions.GetCallStatus`'s return type and projection**

`CallerStatus` has no `None` value (unlike `CallStatus`) — "is there a call to show at all" and "what's its status" are better expressed as a nullable return than by adding a sentinel value to the enum. In `src/dotnet/Streaming.Service/Services/LiveSessions.cs`, replace:
```csharp
    // [ComputeMethod]
    public virtual async Task<CallStatus> GetCallStatus(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        var callState = await Backend.GetCallState(chatId, cancellationToken).ConfigureAwait(false);
        // Only the caller sees the status of their outgoing call.
        return callState is not null && callState.CallerId == chat.Rules.Author?.Id
            ? callState.Status
            : CallStatus.None;
    }
```
with:
```csharp
    // [ComputeMethod]
    public virtual async Task<CallerStatus?> GetCallStatus(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        var callState = await Backend.GetCallState(chatId, cancellationToken).ConfigureAwait(false);
        // Only the caller sees the status of their outgoing call.
        if (callState is null || callState.CallerId != chat.Rules.Author?.Id)
            return null;
        return callState.Status switch {
            CallStatus.Connecting => CallerStatus.Dialing,
            CallStatus.Declined => CallerStatus.NoAnswer,
            CallStatus.Active => CallerStatus.Active,
            CallStatus.Canceled => CallerStatus.Canceled,
            CallStatus.NoAnswer => CallerStatus.NoAnswer,
            CallStatus.Ended => CallerStatus.Ended,
            _ => CallerStatus.Dialing,   // Dialing (None can't reach here - callState is null then)
        };
    }
```

Check `DismissCallStatus` right below it (~line 107-111 today) — it compares `GetCallStatus(...) != CallStatus.None`; read its current body first (`grep -n "DismissCallStatus" -A 4 src/dotnet/Streaming.Service/Services/LiveSessions.cs`) and change the condition to `await GetCallStatus(session, chatId, cancellationToken).ConfigureAwait(false) is not null`.

- [ ] **Step 4: Update `LiveSessionUI.GetCallStatus`'s return type**

In `src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs`:
```csharp
    [ComputeMethod]
    public virtual Task<CallerStatus?> GetCallStatus(ChatId chatId, CancellationToken cancellationToken)
        => LiveSessions.GetCallStatus(Session, chatId, cancellationToken);
```

- [ ] **Step 5: Fix `OutgoingCallBanner.razor`**

Replace the markup block:
```csharp
@{
    var m = State.Value;
    if (!m.EnableIncompleteUI)
        return;

    var status = m.CallStatus;
}

<Banner
    IsVisible="@(status != CallStatus.None)"
    Class="outgoing-call-banner"
    ShowDismissButton="true"
    DismissTooltip="@(status == CallStatus.Dialing ? L.Banner_CancelCall : L.Common_Close)"
    Dismiss="@OnClose">
```
with:
```csharp
@{
    var m = State.Value;
    if (!m.EnableIncompleteUI)
        return;

    var status = m.CallerStatus;
}

<Banner
    IsVisible="@(status is not null)"
    Class="outgoing-call-banner"
    ShowDismissButton="true"
    DismissTooltip="@(status == CallerStatus.Dialing ? L.Banner_CancelCall : L.Common_Close)"
    Dismiss="@OnClose">
```

Replace the `@code` block:
```csharp
    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var enableIncompleteUI = await Features.IsIncompleteUIEnabled(cancellationToken).ConfigureAwait(false);
        if (!enableIncompleteUI)
            return Model.None;

        var callStatus = await LiveSessionUI.GetCallStatus(Chat.Id, cancellationToken).ConfigureAwait(false);
        return new Model(true, callStatus);
    }

    private Task OnClose()
        => State.Value.CallStatus == CallStatus.Dialing
            ? LiveSessionUI.CancelCall(Chat.Id, CancellationToken.None)
            : LiveSessionUI.DismissCallStatus(Chat.Id, CancellationToken.None);

    private string Text(CallStatus status) => status switch {
        CallStatus.Dialing => L.Banner_CallDialing,
        CallStatus.Accepted => L.Banner_CallAccepted,
        CallStatus.Declined => L.Banner_CallDeclined,
        CallStatus.NoAnswer => L.Banner_CallNoAnswer,
        _ => "",
    };

    private static string Icon(CallStatus status) => status switch {
        CallStatus.Accepted => "icon-phone-call",
        CallStatus.Declined => "icon-phone-hang-up",
        CallStatus.NoAnswer => "icon-phone-missed",
        _ => "icon-phone-call",
    };

    // Nested types

    public sealed record Model(bool EnableIncompleteUI, CallStatus CallStatus) {
        public static readonly Model None = new(false, CallStatus.None);
    }
```
with:
```csharp
    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var enableIncompleteUI = await Features.IsIncompleteUIEnabled(cancellationToken).ConfigureAwait(false);
        if (!enableIncompleteUI)
            return Model.None;

        var callStatus = await LiveSessionUI.GetCallStatus(Chat.Id, cancellationToken).ConfigureAwait(false);
        return new Model(true, callStatus);
    }

    private Task OnClose()
        => State.Value.CallerStatus == CallerStatus.Dialing
            ? LiveSessionUI.CancelCall(Chat.Id, CancellationToken.None)
            : LiveSessionUI.DismissCallStatus(Chat.Id, CancellationToken.None);

    private string Text(CallerStatus? status) => status switch {
        CallerStatus.Dialing => L.Banner_CallDialing,
        CallerStatus.Active => L.Banner_CallAccepted,
        CallerStatus.NoAnswer => L.Banner_CallNoAnswer,
        _ => "",
    };

    private static string Icon(CallerStatus? status) => status switch {
        CallerStatus.Active => "icon-phone-call",
        CallerStatus.NoAnswer => "icon-phone-missed",
        _ => "icon-phone-call",
    };

    // Nested types

    public sealed record Model(bool EnableIncompleteUI, CallerStatus? CallerStatus) {
        public static readonly Model None = new(false, null);
    }
```

Note `Declined` no longer has its own banner text (`CallerStatus` folds it into `NoAnswer`) — `L.Banner_CallDeclined` becomes an unused localization key; leave the key itself in place (removing unused localization keys is a separate, larger cleanup this plan doesn't own) but stop referencing it from this switch.

- [ ] **Step 6: Fix `IncomingCallOverLockView.razor:261,266` and `IncomingCallUI.cs:386,449`**

These three sites currently read:
```csharp
if (callStatus is CallStatus.Dialing or CallStatus.Connecting) {
```
(after Task 1's rename) and:
```csharp
return callStatus is CallStatus.Dialing or CallStatus.Connecting;
```
(twice, in `IncomingCallUI.cs`). Since `GetCallStatus` now returns `CallerStatus?` and `CallerStatus.Dialing` alone already covers what used to require checking two `CallStatus` values, simplify all three to:
```csharp
if (callStatus == CallerStatus.Dialing) {
```
and:
```csharp
return callStatus == CallerStatus.Dialing;
```
Update the local variable's declared/inferred type at each call site accordingly (it now comes from `LiveSessionUI.GetCallStatus`, which returns `CallerStatus?` — `callStatus` was already just inferred `var`, so no explicit type annotation needs updating, but re-check `IncomingCallOverLockView.razor:266`'s `IsDialing = callStatus == CallStatus.Dialing,` line too — it becomes `IsDialing = callStatus == CallerStatus.Dialing,`).

- [ ] **Step 7: Run the full affected suite**

```bash
dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --filter "FullyQualifiedName~OutgoingCallStatusTest|FullyQualifiedName~CallConversationCardTest" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~IncomingCallUITest" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
```
Expected: build succeeds, all tests pass.

- [ ] **Step 8: Full regression pass — every test touched anywhere in this plan**

```bash
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~LiveSessionsTest|FullyQualifiedName~CallEntryTest|FullyQualifiedName~CallModerationTest" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --filter "FullyQualifiedName~OutgoingCallStatusTest|FullyQualifiedName~CallConversationCardTest" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~IncomingCallUITest" -c Debug -p:ArtifactsPath="<repo>/tmp/verify-artifacts"
```
Expected: 100% pass. Clean up any scratch `tmp/verify-artifacts` folder used during this plan's verification (`rm -rf tmp/verify-artifacts`).

- [ ] **Step 9: Commit**

```bash
git add src/dotnet/Streaming.Service/Services/LiveSessions.cs src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs \
  src/dotnet/UI.Blazor.App/Components/Banners/OutgoingCallBanner.razor \
  src/dotnet/UI.Blazor.App/Components/IncomingCallModal/IncomingCallOverLockView.razor \
  src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs \
  tests/Chat.UI.Blazor.IntegrationTests/OutgoingCallStatusTest.cs
git commit -m "$(cat <<'EOF'
feat(call): project CallStatus down to CallerStatus for client consumers

GetCallStatus now returns CallerStatus? instead of the internal
CallStatus - the caller's own UI never needs Connecting vs Dialing or
Declined vs NoAnswer distinctions. Client call sites simplify
accordingly (two CallStatus values collapse into one CallerStatus
check in IncomingCallOverLockView/IncomingCallUI).
EOF
)"
```

---

## Self-Review Notes

- **Spec coverage:** every section of the spec maps to a task — `LiveSessionKind`/enum reshaping → Task 1; `ParticipantCount` merge (Open Item 4, confirmed) → Task 2; `Derive` → Task 3; wiring into action RPCs → Task 4; the `GetConsolidatedParticipants` sync (the spec's core correction) → Task 5; Signal Validation → Task 6; `ConfirmRing`/`RingAck` → Task 7; `CallerStatus` projection → Task 8. Open Item 1 (`CancelCall`) resolved during planning: clear `CallState` immediately, no localization work, `Canceled` kept in both enums per explicit decision even though not currently produced by any call site. Open Item 2 (client hook for `ConfirmRing`) stays explicitly out of scope, noted in Task 7. Open Item 3 (`OnStreamRegistered`) needed no task — already resolved by Task 5's mechanism reading the shared `_participants` map regardless of write path.
- **Type consistency:** `RecomputeCallStatus`/`Derive`/`SyncCallParticipantActivity`/`EnsureValidTransition` signatures are used identically across Tasks 3-7 wherever referenced; `GetCallStatus`'s `CallerStatus?` return type (Task 8) is consistent between `LiveSessions.cs` and `LiveSessionUI.cs`, and every client consumer (`OutgoingCallBanner`, `IncomingCallOverLockView`, `IncomingCallUI`) is updated to the nullable type in the same task.
