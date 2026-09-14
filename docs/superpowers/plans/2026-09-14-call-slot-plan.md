# One Call at a Time: Client Call Slot — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One call slot on the client shared by incoming and outgoing calls: every ring that arrives while the slot is held is answered `Busy` and doesn't ring, and no second outgoing call can start.

**Architecture:** A new `CallUI` service owns the slot (`_callChatId`, `_activeCall`), the ring candidates and two loops — holding (derives the held call's phase from session facts and releases the slot when the call ends) and searching (claims a free slot for a ringing candidate, answers `Busy` otherwise). `IncomingCallUI` becomes presentation that reacts to the slot; `LiveSessionUI` claims the slot before `StartCall` and plays the ringback from it. On Android the native side only decides whether to show the call notification and rings with every notification it shows.

**Tech Stack:** .NET 11, Blazor, ActualLab.Fusion (`MutableState`, `[ComputeMethod]`, `Computed.Capture`/`When`/`Update`), MAUI Android, xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-09-14-call-slot-design.md` — read it before Task 1.

## Global Constraints

- Branch `fix/update-ui-287`, issue #690: every commit message ends with `Refs #690` and the attribution trailer `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>` + `Claude-Session: https://claude.ai/code/session_01LB31iLqy17KbPQAe7NaGSX`. Multi-line messages go through repeated `-m` flags, never newlines inside one argument.
- Read `docs/CODING_STYLE.md` before touching C#: no `Async` suffix, no XML docs on members, comments only for what the code can't say (2 lines max on a member), 120-char lines, LF, mixed braces (Allman for types/methods, K&R otherwise), blank line after `return`/`throw`/`break` except before `}`/`case`/`else`.
- A style hook reviews every `.cs`/`.razor`/`.css` edit and reports violations; fix them all by default. The only accepted exception expected here is an explicit constructor that initializes fields from `StateFactory` (CS0236) — record it in `.claude/style-bypasses.md` in that file's format if the hook flags it.
- Calls exist in peer chats only. Ambient live sessions never hold the slot.
- `RingAck.Busy` is sent by the Blazor side only; the Android native side never sends anything for a second call.
- Localization: a new key goes into `Strings.en.json` and every hand-written real-language catalog (`bg bs cs de es fr hi id it ja ko pl pt ru tr uk vi zh`), then `scripts/derive-bcms.cmd` and `scripts/derive-max.cmd` regenerate `cnr hr sr max`. Never hand-edit the derived catalogs.
- Tests: FluentAssertions, AAA comments (`// arrange`, `// act`, `// assert`), `…Should…` method names, no underscores.
- Build check for C#: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj -v q -nologo` must end with `0 Error(s)`.

## File Map

| File | Change | Responsibility |
|---|---|---|
| `src/dotnet/UI.Blazor.App/Services/ActiveCall.cs` | create | Slot types: `IncomingCall`, `ActiveCall`, `CallOrigin`, `CallPhase`, `CallSessionState`, `CallFacts` |
| `src/dotnet/UI.Blazor.App/Services/CallUI.Decisions.cs` | create | Pure decisions `DecideSearch` / `DecideHolding` + `SearchOutcome`, `HoldingAction`, `HoldingMemory` |
| `src/dotnet/UI.Blazor.App/Services/CallUI.cs` | create | The slot, candidates, holding and searching loops, `ConfirmRing` |
| `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs` | rewrite | Screens and ringtone driven by the slot |
| `src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs` | modify | `StartCall` claims the slot, `CancelCall` releases it, ringback follows the slot |
| `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs`, `Module/BlazorUIAppModule.cs` | modify | Register and expose `CallUI` |
| `src/dotnet/UI.Blazor.App/Components/ChatHeaderCallButton.razor` | modify | Disabled with "You're already in a call" while another chat holds the slot |
| `src/dotnet/Localization/Resources/Strings.*.json`, `LocalizedStringsLocalizerExt.cs` | modify | `Call_AlreadyInCall`; translate `Call_Outgoing` / `Call_OutgoingTo` |
| `src/dotnet/App.Maui/Platforms/Android/IncomingCallRinger.cs` | modify | `Start(TimeSpan? autoStopAfter)` with a generation guard |
| `src/dotnet/App.Maui/Platforms/Android/Notifications/FirebaseMessagingService.cs` | modify | Show-or-skip rule, native ringtone with the notification, stop on dismissal |
| `src/dotnet/App.Maui/Platforms/Android/Notifications/IncomingCallNotifications.cs`, `CallActionReceiver.cs` | modify | Stop the native ringtone on Answer / Decline |
| `tests/Chat.UI.Blazor.UnitTests/CallUIDecisionsTest.cs` | create | Decision tests |
| `tests/Chat.UI.Blazor.UnitTests/IncomingCallUITest.cs` → `CallUIRingTest.cs` | rename | `FindRingingCall` moved to `CallUI` |
| `docs/calls/incoming-call-flow.md`, the spec | modify | Describe the slot as built |

---

### Task 1: Slot types and pure decisions

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/ActiveCall.cs`
- Create: `src/dotnet/UI.Blazor.App/Services/CallUI.Decisions.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs:12` (the `IncomingCall` record moves out)
- Test: `tests/Chat.UI.Blazor.UnitTests/CallUIDecisionsTest.cs`

**Interfaces:**
- Produces: `IncomingCall(ChatId ChatId, AuthorId Caller, bool HasVideo)`; `ActiveCall(ChatId ChatId, CallOrigin Origin, CallPhase Phase, AuthorId? PeerId, bool HasVideo)`; `enum CallOrigin { Incoming, Outgoing }`; `enum CallPhase { Ringing, Dialing, Active }`; `enum CallSessionState { None, Dialing, Connected }`; `readonly record struct CallFacts(IncomingCall? Ring, CallSessionState Session, bool IsInConversation)`; `internal static SearchOutcome CallUI.DecideSearch(ChatId? slotChatId, ActiveCall? activeCall, ChatId foundChatId, bool isBusyAcked)`; `internal static HoldingAction CallUI.DecideHolding(ActiveCall? call, CallFacts facts, HoldingMemory memory)`; `internal enum SearchOutcome { Claim, Wait, None, Busy }`; `internal enum HoldingAction { Keep, Confirm, Join, Release }`; `internal readonly record struct HoldingMemory(bool HasSeenDialing, bool WasInConversation, bool IsDialingWaitOver)` with `HoldingMemory Observe(CallFacts facts)`.

`UI.Blazor.App.csproj` already has `<InternalsVisibleTo Include="ActualChat.Chat.UI.Blazor.UnitTests" />`, so the tests see the `internal` members.

- [ ] **Step 1: Write the failing tests**

Create `tests/Chat.UI.Blazor.UnitTests/CallUIDecisionsTest.cs`:

```csharp
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CallUIDecisionsTest
{
    private static readonly ChatId ChatA = ChatId.Parse("the-actual-one");
    private static readonly ChatId ChatB = ChatId.Parse("0NYND2MfRb");
    private static readonly AuthorId CallerA = AuthorId.New(ChatA, 1);
    private static readonly IncomingCall RingA = new(ChatA, CallerA, false);

    [Fact]
    public void SearchShouldClaimFreeSlot()
    {
        // act
        var outcome = CallUI.DecideSearch(null, null, ChatA, false);

        // assert
        outcome.Should().Be(SearchOutcome.Claim);
    }

    [Fact]
    public void SearchShouldIgnoreRingOfClaimedChat()
    {
        // act
        var outcome = CallUI.DecideSearch(ChatA, null, ChatA, false);

        // assert
        outcome.Should().Be(SearchOutcome.None, "the slot already belongs to this very ring");
    }

    [Fact]
    public void SearchShouldIgnoreRingOfHeldChat()
    {
        // act
        var outcome = CallUI.DecideSearch(ChatA, Call(CallOrigin.Incoming, CallPhase.Active), ChatA, false);

        // assert
        outcome.Should().Be(SearchOutcome.None, "an accepted call's invite still reads Ringing for a moment");
    }

    [Fact]
    public void SearchShouldWaitWhileClaimIsUnconfirmed()
    {
        // act
        var outcome = CallUI.DecideSearch(ChatA, null, ChatB, false);

        // assert
        outcome.Should().Be(SearchOutcome.Wait);
    }

    [Fact]
    public void SearchShouldAnswerBusyForAnotherChat()
    {
        // act
        var outcome = CallUI.DecideSearch(ChatA, Call(CallOrigin.Incoming, CallPhase.Active), ChatB, false);

        // assert
        outcome.Should().Be(SearchOutcome.Busy);
    }

    [Fact]
    public void SearchShouldAnswerBusyDuringOutgoingCall()
    {
        // act
        var outcome = CallUI.DecideSearch(ChatA, Call(CallOrigin.Outgoing, CallPhase.Dialing), ChatB, false);

        // assert
        outcome.Should().Be(SearchOutcome.Busy);
    }

    [Fact]
    public void SearchShouldNotRepeatBusy()
    {
        // act
        var outcome = CallUI.DecideSearch(ChatA, Call(CallOrigin.Incoming, CallPhase.Ringing), ChatB, true);

        // assert
        outcome.Should().Be(SearchOutcome.None);
    }

    [Fact]
    public void HoldingShouldConfirmClaimedRing()
    {
        // act
        var action = CallUI.DecideHolding(null, Facts(RingA, CallSessionState.Dialing), default);

        // assert
        action.Should().Be(HoldingAction.Confirm);
    }

    [Fact]
    public void HoldingShouldReleaseClaimWithoutRing()
    {
        // act
        var action = CallUI.DecideHolding(null, Facts(), default);

        // assert
        action.Should().Be(HoldingAction.Release);
    }

    [Fact]
    public void HoldingShouldKeepRingingCall()
    {
        // act
        var action = CallUI.DecideHolding(
            Call(CallOrigin.Incoming, CallPhase.Ringing), Facts(RingA, CallSessionState.Dialing), default);

        // assert
        action.Should().Be(HoldingAction.Keep);
    }

    [Fact]
    public void HoldingShouldReleaseEndedRing()
    {
        // act
        var action = CallUI.DecideHolding(Call(CallOrigin.Incoming, CallPhase.Ringing), Facts(), default);

        // assert
        action.Should().Be(HoldingAction.Release);
    }

    [Fact]
    public void HoldingShouldJoinAnsweredOutgoingCall()
    {
        // act
        var action = CallUI.DecideHolding(
            Call(CallOrigin.Outgoing, CallPhase.Dialing), Facts(session: CallSessionState.Connected), default);

        // assert
        action.Should().Be(HoldingAction.Join);
    }

    [Fact]
    public void HoldingShouldKeepDialingBeforeSessionAppears()
    {
        // act
        var action = CallUI.DecideHolding(Call(CallOrigin.Outgoing, CallPhase.Dialing), Facts(), default);

        // assert
        action.Should().Be(HoldingAction.Keep, "the StartCall RPC may still be in flight");
    }

    [Fact]
    public void HoldingShouldReleaseDialingThatEnded()
    {
        // arrange
        var memory = new HoldingMemory(HasSeenDialing: true, WasInConversation: false, IsDialingWaitOver: false);

        // act
        var action = CallUI.DecideHolding(Call(CallOrigin.Outgoing, CallPhase.Dialing), Facts(), memory);

        // assert
        action.Should().Be(HoldingAction.Release);
    }

    [Fact]
    public void HoldingShouldReleaseDialingThatNeverAppeared()
    {
        // arrange
        var memory = new HoldingMemory(HasSeenDialing: false, WasInConversation: false, IsDialingWaitOver: true);

        // act
        var action = CallUI.DecideHolding(Call(CallOrigin.Outgoing, CallPhase.Dialing), Facts(), memory);

        // assert
        action.Should().Be(HoldingAction.Release);
    }

    [Fact]
    public void HoldingShouldKeepAcceptedCallUntilAudioStarts()
    {
        // act
        var action = CallUI.DecideHolding(
            Call(CallOrigin.Incoming, CallPhase.Active), Facts(session: CallSessionState.Dialing), default);

        // assert
        action.Should().Be(HoldingAction.Keep, "Accept commits Active before the audio starts");
    }

    [Fact]
    public void HoldingShouldReleaseWhenConversationLeft()
    {
        // arrange
        var memory = new HoldingMemory(HasSeenDialing: false, WasInConversation: true, IsDialingWaitOver: false);

        // act
        var action = CallUI.DecideHolding(
            Call(CallOrigin.Incoming, CallPhase.Active), Facts(session: CallSessionState.Connected), memory);

        // assert
        action.Should().Be(HoldingAction.Release);
    }

    [Fact]
    public void HoldingShouldReleaseWhenSessionStopsBeingCall()
    {
        // arrange
        var memory = new HoldingMemory(HasSeenDialing: false, WasInConversation: true, IsDialingWaitOver: false);

        // act
        var action = CallUI.DecideHolding(
            Call(CallOrigin.Outgoing, CallPhase.Active), Facts(isInConversation: true), memory);

        // assert
        action.Should().Be(HoldingAction.Release);
    }

    [Fact]
    public void MemoryShouldRememberDialingAndConversation()
    {
        // act
        var memory = default(HoldingMemory)
            .Observe(Facts(session: CallSessionState.Dialing))
            .Observe(Facts(session: CallSessionState.Connected, isInConversation: true))
            .Observe(Facts());

        // assert
        memory.HasSeenDialing.Should().BeTrue();
        memory.WasInConversation.Should().BeTrue();
    }

    private static CallFacts Facts(
        IncomingCall? ring = null,
        CallSessionState session = CallSessionState.None,
        bool isInConversation = false)
        => new(ring, session, isInConversation);

    private static ActiveCall Call(CallOrigin origin, CallPhase phase)
        => new(ChatA, origin, phase, origin == CallOrigin.Incoming ? CallerA : null, false);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~CallUIDecisionsTest" -nologo -v q`
Expected: build FAILS — `CallUI`, `ActiveCall`, `CallFacts`, `SearchOutcome` … are not defined.

- [ ] **Step 3: Create the slot types**

Create `src/dotnet/UI.Blazor.App/Services/ActiveCall.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public sealed record IncomingCall(ChatId ChatId, AuthorId Caller, bool HasVideo);

public enum CallOrigin { Incoming, Outgoing }

public enum CallPhase { Ringing, Dialing, Active }

// PeerId is the caller of an incoming call; an outgoing call holds the slot before anyone answers.
public sealed record ActiveCall(ChatId ChatId, CallOrigin Origin, CallPhase Phase, AuthorId? PeerId, bool HasVideo);

// What the chat's live session says about a call: none at all, still unanswered, or answered.
public enum CallSessionState { None, Dialing, Connected }

public readonly record struct CallFacts(IncomingCall? Ring, CallSessionState Session, bool IsInConversation);
```

Delete line 12 of `IncomingCallUI.cs` (`public sealed record IncomingCall(ChatId ChatId, AuthorId Caller, bool HasVideo);`) and the blank line after it.

- [ ] **Step 4: Create the decisions**

Create `src/dotnet/UI.Blazor.App/Services/CallUI.Decisions.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public partial class CallUI
{
    internal static SearchOutcome DecideSearch(
        ChatId? slotChatId,
        ActiveCall? activeCall,
        ChatId foundChatId,
        bool isBusyAcked)
    {
        if (slotChatId is null)
            return SearchOutcome.Claim;
        if (slotChatId == foundChatId)
            return SearchOutcome.None;
        if (activeCall is null)
            return SearchOutcome.Wait;

        return isBusyAcked ? SearchOutcome.None : SearchOutcome.Busy;
    }

    internal static HoldingAction DecideHolding(ActiveCall? call, CallFacts facts, HoldingMemory memory)
    {
        if (call is null)
            return facts.Ring is not null ? HoldingAction.Confirm : HoldingAction.Release;

        switch (call.Phase) {
        case CallPhase.Ringing:
            return facts.Ring is not null ? HoldingAction.Keep : HoldingAction.Release;
        case CallPhase.Dialing:
            if (facts.Session == CallSessionState.Connected)
                return HoldingAction.Join;
            if (facts.Session == CallSessionState.Dialing)
                return HoldingAction.Keep;

            // No session yet may just be the StartCall RPC in flight: only one seen and gone, or one
            // that never showed up in time, ends the call.
            return memory.HasSeenDialing || memory.IsDialingWaitOver ? HoldingAction.Release : HoldingAction.Keep;
        default:
            if (facts.Session == CallSessionState.None)
                return HoldingAction.Release;

            // Accept commits Active before the audio starts, so only leaving the conversation ends the call.
            return memory.WasInConversation && !facts.IsInConversation
                ? HoldingAction.Release
                : HoldingAction.Keep;
        }
    }
}

internal enum SearchOutcome { Claim, Wait, None, Busy }

internal enum HoldingAction { Keep, Confirm, Join, Release }

// What the holding loop remembers about the call it holds across wake-ups.
internal readonly record struct HoldingMemory(bool HasSeenDialing, bool WasInConversation, bool IsDialingWaitOver)
{
    public HoldingMemory Observe(CallFacts facts)
        => this with {
            HasSeenDialing = HasSeenDialing || facts.Session == CallSessionState.Dialing,
            WasInConversation = WasInConversation || facts.IsInConversation,
        };
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~CallUIDecisionsTest" -nologo -v q`
Expected: `Passed!  - Failed: 0, Passed: 19`.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/ActiveCall.cs src/dotnet/UI.Blazor.App/Services/CallUI.Decisions.cs src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs tests/Chat.UI.Blazor.UnitTests/CallUIDecisionsTest.cs
git commit -m "feat(call): add the call slot types and its pure decisions" -m "DecideSearch and DecideHolding are the two rules of the client call slot: what the search does with one ringing candidate, and what the holding loop does with the held call on each wake-up." -m "Refs #690" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01LB31iLqy17KbPQAe7NaGSX"
```

---

### Task 2: `CallUI` service

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/CallUI.cs`
- Modify: `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs:114`, `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs:69`
- Modify: `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs` — `GetRingingCall` calls `CallUI.FindRingingCall`; `FindRingingCall` is deleted there
- Rename: `tests/Chat.UI.Blazor.UnitTests/IncomingCallUITest.cs` → `CallUIRingTest.cs`

**Interfaces:**
- Consumes: everything Task 1 produces; `LiveSessionUI.Get(ChatId, CancellationToken)`, `LiveSessionUI.AmIInLiveConversation(ChatId, CancellationToken)`, `LiveSessionUI.ConfirmRing(ChatId, RingAck, CancellationToken)`, `IIncomingCallsBridge`, `INotifications.ListActive(Session, CancellationToken)`.
- Produces (used by Tasks 4–6):
  - `[ComputeMethod] Task<ActiveCall?> GetActiveCall(CancellationToken)`
  - `[ComputeMethod] Task<ChatId?> GetCallChatId(CancellationToken)` and `ChatId? GetCallChatIdNonComputed()`
  - `[ComputeMethod] Task<bool> CanStartCall(CancellationToken)`
  - `[ComputeMethod] Task<ChatId?> GetDialingOutChatId(CancellationToken)` — my outgoing call's chat once the server has it dialing; drives the outgoing screens and the ringback
  - `[ComputeMethod] Task<IncomingCall?> GetRingingCall(ChatId, CancellationToken)`
  - `static IncomingCall? FindRingingCall(LiveSession?, AuthorId)`
  - `void AddCandidate(ChatId)`, `bool TryClaimOutgoing(ChatId, bool hasVideo)`, `bool TryCommitAccept(IncomingCall)`, `void DropRing(ChatId)`, `void Release(ChatId)`
  - `AppUIHub.CallUI`

Nothing resolves `CallUI` until Task 4, so its worker doesn't start yet and `IncomingCallUI` keeps working as before.

- [ ] **Step 1: Create `CallUI.cs`**

```csharp
using ActualChat.Live;
using ActualChat.Notifications;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// The one call this client is in - incoming or outgoing, ringing, dialing or connected. While the slot
/// is held every other ring is answered Busy and no new call can start.
/// </summary>
public partial class CallUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    // Give up on an outgoing call whose session never shows up as dialing.
    private static readonly TimeSpan DialingWaitTimeout = TimeSpan.FromSeconds(15);

    private readonly Lock _lock = new();
    private readonly MutableState<ImmutableList<ChatId>> _ringingChatIds;
    private readonly MutableState<ChatId?> _callChatId;
    private readonly MutableState<ActiveCall?> _activeCall;
    // Rings answered Busy while the slot is held - ListActive repeats a ring on every change.
    private readonly HashSet<ChatId> _busyAckedChatIds = [];

    private IIncomingCallsBridge? Bridge { get; }
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IAuthors Authors => Hub.Authors;
    private INotifications Notifications => Hub.Notifications;
    private Moment Now => Clocks.CpuClock.Now;
    private ILogger? CallDebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.AndroidIncomingCalls);

    public CallUI(AppUIHub hub) : base(hub)
    {
        Bridge = hub.Services.GetService<IIncomingCallsBridge>();
        _ringingChatIds = StateFactory.NewMutable(
            ImmutableList<ChatId>.Empty,
            StateCategories.Get(GetType(), "RingingChatIds"));
        _callChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "CallChatId"));
        _activeCall = StateFactory.NewMutable(
            (ActiveCall?)null,
            StateCategories.Get(GetType(), "ActiveCall"));
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual Task<ActiveCall?> GetActiveCall(CancellationToken cancellationToken)
        => _activeCall.Use(cancellationToken);

    [ComputeMethod]
    public virtual Task<ChatId?> GetCallChatId(CancellationToken cancellationToken)
        => _callChatId.Use(cancellationToken);

    public ChatId? GetCallChatIdNonComputed()
        => _callChatId.Value;

    [ComputeMethod]
    public virtual async Task<bool> CanStartCall(CancellationToken cancellationToken)
        => await GetCallChatId(cancellationToken).ConfigureAwait(false) is null;

    [ComputeMethod]
    public virtual async Task<ChatId?> GetDialingOutChatId(CancellationToken cancellationToken)
    {
        // The slot is claimed before the StartCall RPC, but the outgoing screens read the invitee from the
        // session, and a refused call must not ring back first - so they wait for the server's dialing.
        var call = await GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is not { Origin: CallOrigin.Outgoing, Phase: CallPhase.Dialing })
            return null;

        var live = await LiveSessionUI.Get(call.ChatId, cancellationToken).ConfigureAwait(false);
        return live is { Kind: LiveSessionKind.Call, Conversation: null } ? call.ChatId : null;
    }

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetRingingCall(ChatId chatId, CancellationToken cancellationToken)
    {
        // Straight from the session, for any chat - whether that ring may hold the slot is the search's call.
        var live = await LiveSessionUI.Get(chatId, cancellationToken).ConfigureAwait(false);
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var call = ownAuthor is null ? null : FindRingingCall(live, ownAuthor.Id);
        CallDebugLog?.LogInformation(
            "CALL_TRACE: GetRingingCall #{ChatId} → hasCall={HasCall}; liveNull={LiveNull}, "
            + "liveKind={Kind}, host={Host}, ownNull={OwnNull}, own={Own}, invites=[{Invites}]",
            chatId, call is not null, live is null, live?.Kind, live?.Host, ownAuthor is null, ownAuthor?.Id,
            live is null ? "" : live.Invites.Select(i => $"{i.InviteeId}:{i.Status}").ToDelimitedString(","));
        return call;
    }

    public static IncomingCall? FindRingingCall(LiveSession? live, AuthorId ownAuthorId)
    {
        // No conversation yet means nobody has answered; once someone does, it's no longer an incoming ring.
        if (live is not { Kind: LiveSessionKind.Call, Conversation: null })
            return null;
        if (live.Host == ownAuthorId)
            return null;

        var invite = live.Invites.FirstOrDefault(i => i.InviteeId == ownAuthorId);
        if (invite is not { Status: CallInviteStatus.Ringing })
            return null;

        return new IncomingCall(live.ChatId, live.Host, live.Rules.VideoAllowed);
    }

    public void AddCandidate(ChatId chatId)
    {
        lock (_lock) {
            var chatIds = _ringingChatIds.Value;
            if (!chatIds.Contains(chatId))
                _ringingChatIds.Value = chatIds.Add(chatId);
        }
    }

    public bool TryClaimOutgoing(ChatId chatId, bool hasVideo)
    {
        lock (_lock) {
            if (_callChatId.Value is not null)
                return false;

            _callChatId.Value = chatId;
            _activeCall.Value = new ActiveCall(chatId, CallOrigin.Outgoing, CallPhase.Dialing, null, hasVideo);
            return true;
        }
    }

    public bool TryCommitAccept(IncomingCall call)
    {
        // From a free slot this claims it too: Answer on a notification can land before the search does.
        var chatId = call.ChatId;
        lock (_lock) {
            var slotChatId = _callChatId.Value;
            if (slotChatId is not null && slotChatId != chatId)
                return false;

            _callChatId.Value = chatId;
            _activeCall.Value = new ActiveCall(chatId, CallOrigin.Incoming, CallPhase.Active, call.Caller, call.HasVideo);
            RemoveCandidate(chatId);
            _busyAckedChatIds.Remove(chatId);
            return true;
        }
    }

    public void DropRing(ChatId chatId)
    {
        // The slot goes with the ring only while that ring is what holds it: the dismissal push our own
        // accept triggers must not end the call it just started.
        lock (_lock) {
            RemoveCandidate(chatId);
            _busyAckedChatIds.Remove(chatId);
            if (_callChatId.Value == chatId && _activeCall.Value is null or { Phase: CallPhase.Ringing })
                ReleaseUnsafe();
        }
    }

    public void Release(ChatId chatId)
    {
        lock (_lock) {
            if (_callChatId.Value == chatId)
                ReleaseUnsafe();
        }
    }

    // Protected/internal methods

    [ComputeMethod]
    protected virtual async Task<HoldingInput> GetHoldingInput(ChatId chatId, CancellationToken cancellationToken)
    {
        var slotChatId = await _callChatId.Use(cancellationToken).ConfigureAwait(false);
        var call = await _activeCall.Use(cancellationToken).ConfigureAwait(false);
        var ring = await GetRingingCall(chatId, cancellationToken).ConfigureAwait(false);
        var live = await LiveSessionUI.Get(chatId, cancellationToken).ConfigureAwait(false);
        var session = live switch {
            { Kind: LiveSessionKind.Call, Conversation: null } => CallSessionState.Dialing,
            { Kind: LiveSessionKind.Call } => CallSessionState.Connected,
            _ => CallSessionState.None,
        };
        var isInConversation = await LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken)
            .ConfigureAwait(false);
        return new HoldingInput(slotChatId, call, new CallFacts(ring, session, isInConversation));
    }

    [ComputeMethod]
    protected virtual async Task<SearchInput> GetSearchInput(CancellationToken cancellationToken)
    {
        var chatIds = await _ringingChatIds.Use(cancellationToken).ConfigureAwait(false);
        // Read only to wake the search when the slot moves: ApplySearch decides against the live slot.
        await _callChatId.Use(cancellationToken).ConfigureAwait(false);
        await _activeCall.Use(cancellationToken).ConfigureAwait(false);
        var rings = ImmutableList.CreateBuilder<IncomingCall>();
        for (var i = chatIds.Count - 1; i >= 0; i--)
            if (await GetRingingCall(chatIds[i], cancellationToken).ConfigureAwait(false) is { } ring)
                rings.Add(ring);
        return new SearchInput(chatIds, rings.ToImmutable());
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(HoldCalls),
            AsyncChain.From(SearchRings),
            AsyncChain.From(SyncActiveCallNotifications),
        };
        var retryDelays = RetryDelaySeq.Exp(0.5, 10);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).Run(cancellationToken);
    }

    // Private methods

    private async Task HoldCalls(CancellationToken cancellationToken)
    {
        var cChatId = await Computed
            .Capture(() => GetCallChatId(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested) {
            cChatId = await cChatId.When(chatId => chatId is not null, cancellationToken).ConfigureAwait(false);
            if (cChatId.Value is { } chatId)
                await Hold(chatId, cancellationToken).ConfigureAwait(false);
            cChatId = await cChatId.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task Hold(ChatId chatId, CancellationToken cancellationToken)
    {
        var memory = default(HoldingMemory);
        var dialingDeadline = Now + DialingWaitTimeout;
        var cInput = await Computed
            .Capture(() => GetHoldingInput(chatId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (true) {
            var input = cInput.Value;
            if (input.SlotChatId != chatId)
                return;

            memory = memory.Observe(input.Facts) with { IsDialingWaitOver = Now >= dialingDeadline };
            var action = DecideHolding(input.Call, input.Facts, memory);
            CallDebugLog?.LogInformation("CALL_TRACE: Hold #{ChatId} {Origin}/{Phase} → {Action}",
                chatId, input.Call?.Origin, input.Call?.Phase, action);
            switch (action) {
            case HoldingAction.Confirm:
                TryConfirm(input.Facts.Ring!);
                break;
            case HoldingAction.Join:
                if (TryCommitActive(chatId))
                    _ = StartAnsweredCallAudio(chatId, cancellationToken);
                break;
            case HoldingAction.Release:
                Release(chatId);
                return;
            }

            if (input.Call is { Phase: CallPhase.Dialing } && !memory.HasSeenDialing) {
                // A session that never shows up as dialing must still time out, with nothing to invalidate it.
                using var cts = cancellationToken.CreateLinkedTokenSource();
                var remaining = dialingDeadline - Now;
                if (remaining < TimeSpan.Zero)
                    remaining = TimeSpan.Zero;
                await Task.WhenAny(
                        cInput.WhenInvalidated(cts.Token),
                        Clocks.CpuClock.Delay(remaining, cts.Token))
                    .ConfigureAwait(false);
                cts.CancelAndDisposeSilently();
            }
            else
                await cInput.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cInput = await cInput.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private void TryConfirm(IncomingCall ring)
    {
        var chatId = ring.ChatId;
        lock (_lock) {
            if (_callChatId.Value != chatId || _activeCall.Value is not null)
                return;

            _activeCall.Value = new ActiveCall(chatId, CallOrigin.Incoming, CallPhase.Ringing, ring.Caller, ring.HasVideo);
        }
        _ = ConfirmRing(chatId, RingAck.Ringing);
    }

    private bool TryCommitActive(ChatId chatId)
    {
        lock (_lock) {
            if (_callChatId.Value != chatId || _activeCall.Value is not { } call)
                return false;

            _activeCall.Value = call with { Phase = CallPhase.Active };
            return true;
        }
    }

    private async Task StartAnsweredCallAudio(ChatId chatId, CancellationToken cancellationToken)
    {
        // Placing a call is itself the intent to talk, so answering it puts the caller on the line.
        // A denied mic still joins them - listening only, same as anywhere else.
        try {
            await ChatAudioUI.SetListeningState(chatId, true).ConfigureAwait(false);
            var hasMic = await Hub.AudioRecorder.MicrophonePermission
                .CheckOrRequest(cancellationToken)
                .ConfigureAwait(false);
            if (hasMic)
                await ChatAudioUI.SetRecordingChatId(chatId).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Couldn't join the answered call in chat #{ChatId}", chatId);
            Release(chatId);
        }
    }

    private async Task SearchRings(CancellationToken cancellationToken)
    {
        if (Bridge is not null) {
            // A call push may have landed while the app was killed and the user opened it
            // from the launcher - pick the ring up from the still-active system notification.
            foreach (var chatId in await Bridge.ListActiveCallChatIds(cancellationToken).ConfigureAwait(false))
                AddCandidate(chatId);
        }

        var cInput = await Computed
            .Capture(() => GetSearchInput(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested) {
            ApplySearch(cInput.Value);
            await cInput.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cInput = await cInput.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private void ApplySearch(SearchInput input)
    {
        var busyChatIds = new List<ChatId>();
        lock (_lock) {
            // Checked and not ringing: dropped, or dead candidates would pile up for the scope's lifetime.
            foreach (var chatId in input.CheckedChatIds)
                if (!input.Rings.Any(r => r.ChatId == chatId)) {
                    RemoveCandidate(chatId);
                    _busyAckedChatIds.Remove(chatId);
                }

            foreach (var ring in input.Rings) {
                var chatId = ring.ChatId;
                // Against the live slot, not the input's: a claim earlier in this pass has to count.
                var outcome = DecideSearch(
                    _callChatId.Value, _activeCall.Value, chatId, _busyAckedChatIds.Contains(chatId));
                switch (outcome) {
                case SearchOutcome.Claim:
                    _callChatId.Value = chatId;
                    break;
                case SearchOutcome.Busy:
                    _busyAckedChatIds.Add(chatId);
                    busyChatIds.Add(chatId);
                    break;
                }
            }
        }
        foreach (var chatId in busyChatIds) {
            CallDebugLog?.LogInformation("CALL_TRACE: Busy #{ChatId}", chatId);
            _ = ConfirmRing(chatId, RingAck.Busy);
            Bridge?.DismissCallNotification(chatId);
        }
    }

    private async Task SyncActiveCallNotifications(CancellationToken cancellationToken)
    {
        // Off Android the primary ring trigger; on Android the safety net for a push dropped while the
        // scope is alive. The search confirms each ring against the session.
        var cNotifications = await Computed
            .Capture(() => Notifications.ListActive(Session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in cNotifications.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            foreach (var notification in c.Value)
                if (notification is CallNotification call)
                    AddCandidate(call.ChatId);
        }
    }

    private async Task ConfirmRing(ChatId chatId, RingAck ack)
    {
        // Telemetry only (see RingAck), so it's fire-and-forget: a slow or failed ack never holds up the ring.
        try {
            await LiveSessionUI.ConfirmRing(chatId, ack, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "ConfirmRing({Ack}) #{ChatId} failed", ack, chatId);
        }
    }

    // Caller must hold _lock.
    private void ReleaseUnsafe()
    {
        if (_callChatId.Value is { } chatId)
            RemoveCandidate(chatId);
        _activeCall.Value = null;
        _callChatId.Value = null;
        _busyAckedChatIds.Clear();
    }

    // Caller must hold _lock.
    private void RemoveCandidate(ChatId chatId)
    {
        var chatIds = _ringingChatIds.Value;
        if (chatIds.Contains(chatId))
            _ringingChatIds.Value = chatIds.Remove(chatId);
    }

    // Nested types

    protected sealed record HoldingInput(ChatId? SlotChatId, ActiveCall? Call, CallFacts Facts);

    protected sealed record SearchInput(ImmutableList<ChatId> CheckedChatIds, ImmutableList<IncomingCall> Rings);
}
```

- [ ] **Step 2: Register and expose it**

In `Module/BlazorUIAppModule.cs`, right after `fusion.AddService<IncomingCallUI>(ServiceLifetime.Scoped);`:

```csharp
        fusion.AddService<CallUI>(ServiceLifetime.Scoped);
```

In `Services/AppUIHub.cs`, right after the `IncomingCallUI` property:

```csharp
    public CallUI CallUI => field ??= Services.GetRequiredService<CallUI>();
```

- [ ] **Step 3: Point `IncomingCallUI` at the moved `FindRingingCall`**

In `IncomingCallUI.cs`, delete the whole `public static IncomingCall? FindRingingCall(LiveSession? live, AuthorId ownAuthorId)` method, and in `GetRingingCall` change `FindRingingCall(live, ownAuthor.Id)` to `CallUI.FindRingingCall(live, ownAuthor.Id)`.

- [ ] **Step 4: Move the ring tests**

```bash
git mv tests/Chat.UI.Blazor.UnitTests/IncomingCallUITest.cs tests/Chat.UI.Blazor.UnitTests/CallUIRingTest.cs
```

Replace the content of `CallUIRingTest.cs` with the same three cases, now against `CallUI` and in the house test style (`Should` names, AAA sections) — the style hook reviews the whole file on this edit:

```csharp
using ActualChat.Live;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CallUIRingTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly AuthorId Host = AuthorId.New(TestChatId, 1);
    private static readonly AuthorId Me = AuthorId.New(TestChatId, 2);

    [Fact]
    public void FindRingingCallShouldReturnMyRingingInvite()
    {
        // arrange
        var live = NewCall(new CallInvite { InviteeId = Me, Status = CallInviteStatus.Ringing });

        // act
        var call = CallUI.FindRingingCall(live, Me);

        // assert
        call.Should().NotBeNull();
        call!.ChatId.Should().Be(TestChatId);
        call.Caller.Should().Be(Host);
    }

    [Fact]
    public void FindRingingCallShouldIgnoreNonRingingStates()
    {
        // arrange
        var statuses = new[] { CallInviteStatus.Accepted, CallInviteStatus.Declined, CallInviteStatus.Missed };

        // act
        var calls = statuses
            .Select(status => CallUI.FindRingingCall(NewCall(new CallInvite { InviteeId = Me, Status = status }), Me))
            .ToList();

        // assert
        calls.Should().AllSatisfy(call => call.Should().BeNull());
    }

    [Fact]
    public void FindRingingCallShouldIgnoreForeignInviteNullSessionNonCallAndOwnCall()
    {
        // arrange
        var other = AuthorId.New(TestChatId, 3);
        var foreignInvite = NewCall(new CallInvite { InviteeId = other, Status = CallInviteStatus.Ringing });
        var ambient = NewCall(new CallInvite { InviteeId = Me, Status = CallInviteStatus.Ringing })
            with { Kind = LiveSessionKind.Ambient };
        var ownCall = NewCall(new CallInvite { InviteeId = Host, Status = CallInviteStatus.Ringing });

        // act
        var calls = new[] {
            CallUI.FindRingingCall(null, Me),
            CallUI.FindRingingCall(foreignInvite, Me),
            CallUI.FindRingingCall(ambient, Me),
            CallUI.FindRingingCall(ownCall, Host),
        };

        // assert
        calls.Should().AllSatisfy(call => call.Should().BeNull());
    }

    private static LiveSession NewCall(params CallInvite[] invites)
        => new() {
            ChatId = TestChatId,
            Host = Host,
            Kind = LiveSessionKind.Call,
            Invites = invites,
        };
}
```

- [ ] **Step 5: Build and run the tests**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj -v q -nologo`
Expected: `0 Error(s)`.

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~CallUI" -nologo -v q`
Expected: `Passed!  - Failed: 0, Passed: 22`.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/CallUI.cs src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs src/dotnet/UI.Blazor.App/Services/AppUIHub.cs src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs tests/Chat.UI.Blazor.UnitTests/CallUIRingTest.cs
git commit -m "feat(call): add CallUI, the client call slot" -m "Holds the one call this client is in, with a holding loop that follows the held call to its end and a searching loop that claims a free slot for a ring or answers Busy. Registered but not wired in yet." -m "Refs #690" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01LB31iLqy17KbPQAe7NaGSX"
```

---

### Task 3: `Call_AlreadyInCall` and the untranslated outgoing-call keys

**Files:**
- Modify: `src/dotnet/Localization/Resources/Strings.{en,bg,bs,cs,de,es,fr,hi,id,it,ja,ko,pl,pt,ru,tr,uk,vi,zh}.json`
- Regenerate: `Strings.{cnr,hr,sr,max}.json`, `Messages.{cnr,hr,sr,max}.json`
- Modify: `src/dotnet/Localization/Resources/LocalizedStringsLocalizerExt.cs`

**Interfaces:**
- Produces: `L.Call_AlreadyInCall` ("You're already in a call"), used by Tasks 4 and 5.

`Call_Outgoing` / `Call_OutgoingTo` were added in English to every catalog by `6cbb451eaf`, and the derived catalogs were hand-edited; that is why `derive-bcms --check` / `derive-max --check` fail on this branch today. This task fixes both.

- [ ] **Step 1: Write the catalog script**

Create `tmp/l10n-call-slot.py`:

```python
import pathlib
import re

root = pathlib.Path("src/dotnet/Localization/Resources")
already_in_call = {
    "en": "You're already in a call",
    "bg": "Вече сте в разговор",
    "bs": "Već ste u pozivu",
    "cs": "Už jste v hovoru",
    "de": "Sie sind bereits in einem Anruf",
    "es": "Ya estás en una llamada",
    "fr": "Vous êtes déjà en appel",
    "hi": "आप पहले से कॉल पर हैं",
    "id": "Anda sudah dalam panggilan",
    "it": "Sei già in una chiamata",
    "ja": "すでに通話中です",
    "ko": "이미 통화 중입니다",
    "pl": "Jesteś już w trakcie rozmowy",
    "pt": "Você já está em uma chamada",
    "ru": "Вы уже в звонке",
    "tr": "Zaten bir aramadasınız",
    "uk": "Ви вже в дзвінку",
    "vi": "Bạn đang trong một cuộc gọi",
    "zh": "你已在通话中",
}
outgoing = {
    "bg": ("Изходящо обаждане", "Изходящо обаждане към "),
    "bs": ("Odlazni poziv", "Odlazni poziv prema "),
    "cs": ("Odchozí hovor", "Odchozí hovor pro "),
    "de": ("Ausgehender Anruf", "Ausgehender Anruf an "),
    "es": ("Llamada saliente", "Llamada saliente a "),
    "fr": ("Appel sortant", "Appel sortant vers "),
    "hi": ("आउटगोइंग कॉल", "आउटगोइंग कॉल: "),
    "id": ("Panggilan keluar", "Panggilan keluar ke "),
    "it": ("Chiamata in uscita", "Chiamata in uscita a "),
    "ja": ("発信", "発信: "),
    "ko": ("발신 전화", "발신 전화: "),
    "pl": ("Połączenie wychodzące", "Połączenie wychodzące do "),
    "pt": ("Chamada efetuada", "Chamada efetuada para "),
    "ru": ("Исходящий звонок", "Исходящий звонок: "),
    "tr": ("Giden arama", "Giden arama: "),
    "uk": ("Вихідний дзвінок", "Вихідний дзвінок: "),
    "vi": ("Cuộc gọi đi", "Cuộc gọi đi tới "),
    "zh": ("去电", "去电："),
}


def set_value(lines, key, value):
    pattern = re.compile(r'^(\s*)"' + key + r'":\s*".*?"(,?)\s*$')
    hits = [i for i, line in enumerate(lines) if pattern.match(line)]
    assert len(hits) == 1, (key, hits)
    indent, comma = pattern.match(lines[hits[0]]).groups()
    lines[hits[0]] = f'{indent}"{key}": "{value}"{comma}'


for lang, text in already_in_call.items():
    path = root / f"Strings.{lang}.json"
    raw = path.read_bytes().decode("utf-8")
    newline = "\r\n" if "\r\n" in raw else "\n"
    lines = raw.split(newline)
    anchor = [i for i, line in enumerate(lines) if line.lstrip().startswith('"Call_NoMicrophoneAccess":')]
    assert len(anchor) == 1, (lang, anchor)
    assert lines[anchor[0]].rstrip().endswith(","), lang
    assert not any('"Call_AlreadyInCall"' in line for line in lines), lang
    lines.insert(anchor[0] + 1, f'  "Call_AlreadyInCall": "{text}",')
    if lang in outgoing:
        set_value(lines, "Call_Outgoing", outgoing[lang][0])
        set_value(lines, "Call_OutgoingTo", outgoing[lang][1])
    path.write_bytes(newline.join(lines).encode("utf-8"))
    print(f"{path.name}: done")
```

- [ ] **Step 2: Run it and regenerate the derived catalogs**

```powershell
python tmp/l10n-call-slot.py
scripts/derive-bcms.cmd
scripts/derive-max.cmd
scripts/derive-bcms.cmd --check
scripts/derive-max.cmd --check
```

Expected: 19 `done` lines; both `--check` runs print that the catalogs match. `git diff --stat` shows all 23 `Strings.*.json` plus possibly the `Messages.{cnr,hr,sr,max}.json`; the derived diffs may also carry older drift (`Call_IsCallingYou`, `Call_AppIncomingTitle`, `Call_Mute`) — that is the regeneration catching up and is expected.

- [ ] **Step 3: Add the typed member**

In `LocalizedStringsLocalizerExt.cs`, right after the `Call_NoMicrophoneAccess` member:

```csharp
        public string Call_AlreadyInCall => l["Call_AlreadyInCall"].Value;
```

- [ ] **Step 4: Run the localization tests**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~Localization" -nologo -v q`
Expected: `Passed!  - Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Localization/Resources
git commit -m "feat(l10n): add Call_AlreadyInCall and translate the outgoing-call keys" -m "Call_Outgoing and Call_OutgoingTo reached every catalog in English and the derived catalogs were edited by hand; they are translated now and cnr/hr/sr/max are regenerated, so derive-bcms and derive-max checks pass again." -m "Refs #690" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01LB31iLqy17KbPQAe7NaGSX"
```

---

### Task 4: Switch incoming and outgoing calls to the slot

**Files:**
- Rewrite: `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs`

**Interfaces:**
- Consumes: `CallUI` API from Task 2, `L.Call_AlreadyInCall` from Task 3.
- Produces: `IncomingCallUI.GetIncomingCall` = `CallUI.GetActiveCall()` filtered to `Incoming/Ringing`; `IncomingCallUI.OnRing` feeds `CallUI.AddCandidate`. Removed public members: `PrepareForegroundCall`, `CancelPreparedCall`, `ShowForegroundCall`, `ShowOutgoingCall`, `EndOutgoingCall` (only `LiveSessionUI` called them; `EndOutgoingCall` had no caller).

The three `Reset*` loops are replaced by `SyncCallScreens`, which maps each of their branches to a slot transition:

| Old loop branch | New trigger |
|---|---|
| `ResetOverLockScreen`: over-lock ring ended unaccepted → `ClearOverLock` + `MoveBehindLockScreen` | the slot let go of an `Incoming/Ringing` call whose chat is the over-lock chat |
| `ResetForegroundCallScreen`: narrow dialing ended unanswered → `HangUpForegroundCall` | the slot let go of an `Outgoing/Dialing` call whose chat is the foreground chat |
| `ResetActiveCall`: the conversation ended → `TeardownInCall` | the slot let go of an `Active` call |
| `WatchOutgoingCall` dialing seen → `ShowOutgoingCall` | `CallUI.GetDialingOutChatId` turns to this chat — the slot is claimed before the RPC, but the screens need the server's dialing session to show the invitee |
| `JoinAnsweredCall` finally → `ShowForegroundCall` | `Outgoing/Dialing` → `Outgoing/Active` in the same chat |

A teardown after the user's own hang-up is harmless: the hang-up paths already cleared the screen flags, so `TeardownInCall` falls through to `HangUpQuietly`, which only stops the audio again.

- [ ] **Step 1: Replace `IncomingCallUI.cs` with this content**

```csharp
using ActualChat.Localization;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Module;
using ActualLab.Diagnostics;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Screens and ringing for the call <see cref="CallUI"/> holds: the incoming modal, the island, the
/// over-lock and narrow full-screen views, the ringtone. The call mechanics live in <see cref="CallUI"/>.
/// </summary>
public class IncomingCallUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    private static readonly string JSStartRingtone = $"{BlazorUIAppModule.ImportName}.IncomingCallRingtone.start";
    private static readonly string JSStopRingtone = $"{BlazorUIAppModule.ImportName}.IncomingCallRingtone.stop";

    // Ring-time-only signal: OnRing sets it when the device is locked. Left stale once the ring/call
    // it names ends - OverLockChatId's derivation stops matching it by then either way.
    private readonly MutableState<ChatId?> _overLockRingChatId;
    // The raw candidate ForegroundCallChatId derives from.
    private readonly MutableState<ChatId?> _foregroundRawChatId;
    // The two raw flags above, kept only while CallUI holds that chat.
    private readonly ComputedState<ChatId?> _overLockChatId;
    private readonly ComputedState<ChatId?> _foregroundCallChatId;
    // The ring collapsed into the draggable island (foreground only); its modal is closed while set.
    private readonly MutableState<ChatId?> _collapsedChatId;
    // My own outgoing call, collapsed into the draggable island (wide screens only); its modal is
    // closed while set - the outgoing-call counterpart of _collapsedChatId above.
    private readonly MutableState<ChatId?> _collapsedOutgoingChatId;
    // The ring whose ringtone the user silenced; the ring itself keeps going.
    private readonly MutableState<ChatId?> _mutedRingChatId;
    private int _ringGeneration;

    public IState<ChatId?> OverLockChatId => _overLockChatId;
    public IState<ChatId?> ForegroundCallChatId => _foregroundCallChatId;
    public IState<ChatId?> CollapsedChatId => _collapsedChatId;
    public IState<ChatId?> CollapsedOutgoingChatId => _collapsedOutgoingChatId;
    public IState<ChatId?> MutedRingChatId => _mutedRingChatId;

    private IIncomingCallsBridge? Bridge { get; }
    private CallUI CallUI => Hub.CallUI;
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private ILogger? CallDebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.AndroidIncomingCalls);

    public IncomingCallUI(AppUIHub hub) : base(hub)
    {
        Bridge = hub.Services.GetService<IIncomingCallsBridge>();
        _overLockRingChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "OverLockRingChatId"));
        _foregroundRawChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "ForegroundRawChatId"));
        _overLockChatId = StateFactory.NewComputed<ChatId?>(
            new ComputedState<ChatId?>.Options {
                UpdateDelayer = FixedDelayer.NextTick,
                Category = StateCategories.Get(GetType(), "OverLockChatId"),
            },
            ComputeOverLockChatId);
        _foregroundCallChatId = StateFactory.NewComputed<ChatId?>(
            new ComputedState<ChatId?>.Options {
                UpdateDelayer = FixedDelayer.NextTick,
                Category = StateCategories.Get(GetType(), "ForegroundCallChatId"),
            },
            ComputeForegroundCallChatId);
        Hub.RegisterDisposable(_overLockChatId);
        Hub.RegisterDisposable(_foregroundCallChatId);
        _collapsedChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "CollapsedChatId"));
        _collapsedOutgoingChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "CollapsedOutgoingChatId"));
        _mutedRingChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "MutedRingChatId"));
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    public void OnRing(ChatId chatId, bool showOverLockScreen = false)
    {
        if (chatId.Value.IsNullOrEmpty())
            return;

        CallDebugLog?.LogInformation("CALL_TRACE: OnRing #{ChatId}, showOverLockScreen={ShowOverLockScreen}",
            chatId, showOverLockScreen);
        CallUI.AddCandidate(chatId);
        // A ring that can't hold the slot must not take the screen over the lock: it would swap the held
        // call's screen for its own.
        var slotChatId = CallUI.GetCallChatIdNonComputed();
        if (showOverLockScreen && (slotChatId is null || slotChatId == chatId))
            _overLockRingChatId.Value = chatId;
    }

    // Called by the over-lock call screen after it has rendered. The render callback fires before the
    // WebView actually paints, so wait a beat before removing the native cover — otherwise the app's
    // restored route flashes through for a frame on a cold start.
    public void OnOverLockScreenRendered()
    {
        CallDebugLog?.LogInformation("CALL_TRACE: OnOverLockScreenRendered");
        _ = RevealCallScreenAfterPaint();
    }

    private async Task RevealCallScreenAfterPaint()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
        CallDebugLog?.LogInformation("CALL_TRACE: RevealCallScreen (after paint delay), Bridge={HasBridge}",
            Bridge is not null);
        Bridge?.RevealCallScreen();
    }

    public void OnCallDismissed(ChatId chatId)
    {
        if (chatId.Value.IsNullOrEmpty())
            return;

        EndRing(chatId);
        _ = Bridge?.OnCallHandled(false);
    }

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetIncomingCall(CancellationToken cancellationToken)
    {
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        return call is { Origin: CallOrigin.Incoming, Phase: CallPhase.Ringing, PeerId: { } callerId }
            ? new IncomingCall(call.ChatId, callerId, call.HasVideo)
            : null;
    }

    public async Task Accept(ChatId chatId)
    {
        var isOverLockScreen = _overLockRingChatId.Value == chatId;
        // Straight from the session, not the slot: Answer on an Android notification can land before the
        // search claimed the ring.
        var call = await CallUI.GetRingingCall(chatId, default).ConfigureAwait(true);
        if (call is null) {
            EndRing(chatId);
            _ = Bridge?.OnCallHandled(false);
            Hub.ToastUI.Show(L.Call_Ended, "icon-phone", ToastDismissDelay.Short);
            return;
        }

        // Committed before the ring is dropped and before the accept RPC starts: screen visibility, derived
        // from the slot, must not blink off between "ring ended" and "audio started".
        if (!CallUI.TryCommitAccept(call)) {
            Hub.ToastUI.Show(L.Call_AlreadyInCall, "icon-phone", ToastDismissDelay.Short);
            return;
        }

        ClearRingFlags(chatId);
        Bridge?.DismissCallNotification(chatId);
        // A narrow view gets the same full-screen call view as over-lock instead of dropping straight
        // into the chat; DismissForegroundCall/HangUpForegroundCall (or its own auto-teardown) opens
        // the chat once it closes.
        var showsForegroundCall = !isOverLockScreen && Hub.BrowserInfo.ScreenSize.Value.IsNarrow();
        try {
            await LiveSessionUI.AcceptCall(chatId, default).ConfigureAwait(true);
        }
        catch (Exception e) {
            CallUI.Release(chatId);
            _ = Bridge?.OnCallHandled(false);
            Log.LogWarning(e, "AcceptCall failed for chat #{ChatId}", chatId);
            Hub.ToastUI.Show(L.Call_Ended, "icon-phone", ToastDismissDelay.Short);
            return;
        }

        try {
            // Accept over the lock screen keeps the call activity visible over the keyguard and starts
            // audio without unlocking: the mic FGS is allowed because the activity (shown via
            // SetShowWhenLocked) counts as foreground. Otherwise dismiss the keyguard first, since the
            // FGS can't start from a background state.
            var canStartAudio = isOverLockScreen
                || Bridge is null
                || await Bridge.OnCallHandled(true).ConfigureAwait(true);
            if (!isOverLockScreen && !showsForegroundCall)
                await Hub.History.NavigateTo(Links.Chat(chatId)).ConfigureAwait(true);
            if (canStartAudio) {
                // Listen unconditionally first; pending OS mic prompt won't gate EnforceCallConnectGrace check.
                await ChatAudioUI.SetListeningState(chatId, true).ConfigureAwait(true);
                var micPermission = Hub.AudioRecorder.MicrophonePermission;
                if (await micPermission.CheckOrRequest(CancellationToken.None).ConfigureAwait(true))
                    await ChatAudioUI.SetRecordingChatId(chatId).ConfigureAwait(true);
            }
            if (showsForegroundCall)
                _foregroundRawChatId.Value = chatId;
        }
        catch {
            CallUI.Release(chatId);
            throw;
        }
    }

    // Collapses the outgoing-call modal into the draggable island. The call keeps dialing - the
    // island's hang-up still works and a tap on it re-opens the modal.
    public void CollapseOutgoing(ChatId chatId)
        => _collapsedOutgoingChatId.Value = chatId;

    public void ExpandOutgoing(ChatId chatId)
    {
        if (_collapsedOutgoingChatId.Value != chatId)
            return;

        // Closing the modal (an explicit hang-up or collapsing itself) disposes that component
        // instance - clearing the flag alone won't bring it back, so re-show it here. If dialing has
        // since ended, the fresh instance's own ComputeState closes it right back (see its "resolved
        // close" branch).
        _collapsedOutgoingChatId.Value = null;
        ShowOutgoingCallModal(chatId);
    }

    // Hangs up my own still-dialing outgoing call from the full-screen view.
    public async Task CancelForegroundCall(ChatId chatId)
    {
        ClearForegroundCall(chatId);
        try {
            await LiveSessionUI.CancelCall(chatId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "CancelCall failed for chat #{ChatId}", chatId);
        }
    }

    public async Task Decline(ChatId chatId)
    {
        var isOverLockScreen = _overLockRingChatId.Value == chatId;
        ClearOverLock();
        EndRing(chatId);
        if (isOverLockScreen)
            Bridge?.MoveBehindLockScreen();
        else
            _ = Bridge?.OnCallHandled(false);
        try {
            await LiveSessionUI.DeclineCall(chatId, default).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "DeclineCall failed for chat #{ChatId}", chatId);
        }
    }

    // Silences / restores the ringtone without ending the ring: the call keeps ringing and the UI stays,
    // only the sound is toggled.
    public void ToggleMuteRing(ChatId chatId)
    {
        if (_mutedRingChatId.Value == chatId) {
            _mutedRingChatId.Value = null;
            StartRinging();
        }
        else {
            _mutedRingChatId.Value = chatId;
            StopRinging();
        }
    }

    // Collapses the ringing modal into the draggable island and silences the ringtone. The call keeps
    // ringing - the island's Accept/Decline still work and a tap on it re-opens the modal.
    public void Collapse(ChatId chatId)
    {
        if (_mutedRingChatId.Value != chatId) {
            _mutedRingChatId.Value = chatId;
            StopRinging();
        }
        _collapsedChatId.Value = chatId;
    }

    public void Expand(ChatId chatId)
    {
        if (_collapsedChatId.Value == chatId)
            _collapsedChatId.Value = null;
    }

    // "Message" action: decline the call and open the chat to type a reply instead.
    public async Task DeclineAndOpenChat(ChatId chatId)
    {
        await Decline(chatId).ConfigureAwait(true);
        await Hub.History.NavigateTo(Links.Chat(chatId)).ConfigureAwait(true);
    }

    // From the over-lock in-call screen: dismiss the keyguard (PIN) and, once unlocked, close the
    // in-call screen and open the chat. On a cancelled PIN we stay on the in-call screen.
    public async Task GoToChat(ChatId chatId)
    {
        var isUnlocked = Bridge is null || await Bridge.OnCallHandled(true).ConfigureAwait(true);
        if (!isUnlocked)
            return;

        ClearOverLock();
        await Hub.History.NavigateTo(Links.Chat(chatId)).ConfigureAwait(true);
    }

    public async Task HangUp(ChatId chatId)
    {
        ClearOverLock();
        Bridge?.MoveBehindLockScreen();
        await HangUpQuietly(chatId).ConfigureAwait(true);
    }

    // From the foreground in-call screen (narrow layout, not over the lock screen): just closes the
    // screen and opens the chat - unlike GoToChat, no keyguard/backgrounding call is involved.
    public Task DismissForegroundCall(ChatId chatId)
    {
        ClearForegroundCall(chatId);
        return Hub.History.NavigateTo(Links.Chat(chatId));
    }

    public async Task HangUpForegroundCall(ChatId chatId)
    {
        ClearForegroundCall(chatId);
        await HangUpQuietly(chatId).ConfigureAwait(true);
        await Hub.History.NavigateTo(Links.Chat(chatId)).ConfigureAwait(true);
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(SyncRings),
            AsyncChain.From(SyncCallScreens),
            AsyncChain.From(SyncIncomingCallModal),
        };
        var retryDelays = RetryDelaySeq.Exp(0.5, 10);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).Run(cancellationToken);
    }

    // Private methods

    private async Task<ChatId?> ComputeOverLockChatId(CancellationToken cancellationToken)
    {
        var chatId = await _overLockRingChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return null;

        return await CallUI.GetCallChatId(cancellationToken).ConfigureAwait(false) == chatId ? chatId : null;
    }

    private async Task<ChatId?> ComputeForegroundCallChatId(CancellationToken cancellationToken)
    {
        var chatId = await _foregroundRawChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return null;

        return await CallUI.GetCallChatId(cancellationToken).ConfigureAwait(false) == chatId ? chatId : null;
    }

    private async Task SyncCallScreens(CancellationToken cancellationToken)
    {
        var cInput = await Computed
            .Capture(() => GetScreenInput(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var last = new ScreenInput(null, null);
        while (!cancellationToken.IsCancellationRequested) {
            var input = cInput.Value;
            if (input != last) {
                OnScreenInputChanged(last, input);
                last = input;
            }

            await cInput.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cInput = await cInput.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnScreenInputChanged(ScreenInput last, ScreenInput input)
    {
        if (input.DialingOutChatId is { } dialingChatId && dialingChatId != last.DialingOutChatId)
            ShowOutgoingCall(dialingChatId);
        var (lastCall, call) = (last.Call, input.Call);
        if (call is { Origin: CallOrigin.Outgoing, Phase: CallPhase.Active }
            && lastCall is { Phase: CallPhase.Dialing }
            && lastCall.ChatId == call.ChatId)
            ShowForegroundCall(call.ChatId);
        if (lastCall is null || lastCall.ChatId == call?.ChatId)
            return;

        var chatId = lastCall.ChatId;
        CallDebugLog?.LogInformation("CALL_TRACE: slot released #{ChatId} from {Phase}", chatId, lastCall.Phase);
        ClearRingFlags(chatId);
        if (_collapsedOutgoingChatId.Value == chatId)
            _collapsedOutgoingChatId.Value = null;
        switch (lastCall.Phase) {
        case CallPhase.Ringing:
            if (_overLockRingChatId.Value == chatId) {
                ClearOverLock();
                Bridge?.MoveBehindLockScreen();
            }
            break;
        case CallPhase.Dialing:
            if (_foregroundRawChatId.Value == chatId)
                _ = Hub.Dispatcher.InvokeAsync(() => HangUpForegroundCall(chatId));
            break;
        case CallPhase.Active:
            _ = Hub.Dispatcher.InvokeAsync(() => TeardownInCall(chatId));
            break;
        }
    }

    [ComputeMethod]
    protected virtual async Task<ScreenInput> GetScreenInput(CancellationToken cancellationToken)
    {
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        var dialingOutChatId = await CallUI.GetDialingOutChatId(cancellationToken).ConfigureAwait(false);
        return new ScreenInput(call, dialingOutChatId);
    }

    // Shows the outgoing-call UI while still dialing: the full-screen view on narrow screens, or the
    // OutgoingCallModal on wide screens.
    private void ShowOutgoingCall(ChatId chatId)
    {
        // A prior call to this same chat may have left this collapsed - without clearing it here,
        // this fresh dial would inherit that flag and jump straight to the island.
        if (_collapsedOutgoingChatId.Value == chatId)
            _collapsedOutgoingChatId.Value = null;

        if (Hub.BrowserInfo.ScreenSize.Value.IsNarrow()) {
            _foregroundRawChatId.Value = chatId;
            return;
        }

        ShowOutgoingCallModal(chatId);
    }

    // Same full-screen call view as an accepted incoming call, narrow layout only.
    private void ShowForegroundCall(ChatId chatId)
    {
        if (Hub.BrowserInfo.ScreenSize.Value.IsNarrow())
            _foregroundRawChatId.Value = chatId;
    }

    // ModalUI.Show needs the Blazor dispatcher; callers can be background loops or a UI event handler.
    private void ShowOutgoingCallModal(ChatId chatId)
        => _ = Hub.Dispatcher.InvokeAsync(() => Hub.ModalUI.Show(new OutgoingCallModal.Model(chatId)));

    private async Task SyncRings(CancellationToken cancellationToken)
    {
        var cCall = await Computed
            .Capture(() => GetIncomingCall(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var isRinging = false;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var call = cCall.Value;
                if (call is not null != isRinging) {
                    isRinging = call is not null;
                    if (isRinging)
                        StartRinging();
                    else
                        StopRinging();
                }

                await cCall.WhenInvalidated(cancellationToken).ConfigureAwait(false);
                cCall = await cCall.Update(cancellationToken).ConfigureAwait(false);
            }
        }
        finally {
            if (isRinging)
                StopRinging();
        }
    }

    // The ring to show as a foreground modal: null while it's shown over the lock screen (native view)
    // or collapsed into the island. Reactive to all three, so collapse/expand re-drive the modal.
    [ComputeMethod]
    protected virtual async Task<IncomingCall?> GetModalCall(CancellationToken cancellationToken)
    {
        var call = await GetIncomingCall(cancellationToken).ConfigureAwait(false);
        if (call is null)
            return null;

        var overLock = await _overLockChatId.Use(cancellationToken).ConfigureAwait(false);
        if (overLock == call.ChatId)
            return null;

        var collapsed = await _collapsedChatId.Use(cancellationToken).ConfigureAwait(false);
        if (collapsed == call.ChatId)
            return null;

        return call;
    }

    private async Task SyncIncomingCallModal(CancellationToken cancellationToken)
    {
        // Skipped while the ring is over the lock screen or collapsed into the island (see GetModalCall).
        // The modal closes itself when GetModalCall drops to null; the per-chat guard stops it re-popping.
        var cCall = await Computed
            .Capture(() => GetModalCall(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        ChatId? shownForChatId = null;
        while (!cancellationToken.IsCancellationRequested) {
            var call = cCall.Value;
            if (call is null)
                shownForChatId = null;
            else if (shownForChatId != call.ChatId) {
                shownForChatId = call.ChatId;
                var caller = call.Caller;
                // ModalUI.Show needs the Blazor dispatcher; this runs on a worker chain.
                _ = Hub.Dispatcher.InvokeAsync(
                    () => Hub.ModalUI.Show(new IncomingCallModal.Model(caller), cancellationToken));
            }

            await cCall.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cCall = await cCall.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private void StartRinging()
    {
        // Routes the ring melody to the platform ringer: the native bridge on Android, the looping web
        // ringtone everywhere else. Fire-and-forget to mirror the sync Bridge calls (and keep the finally
        // teardown sync); the JS invocation swallows its own errors.
        if (Bridge is not null)
            _ = StartNativeRinging(Interlocked.Increment(ref _ringGeneration));
        else
            _ = PlayWebRingtone(true);
    }

    private void StopRinging()
    {
        if (Bridge is not null) {
            // Bumped first: a start still waiting on the audio mode drops instead of ringing on.
            Interlocked.Increment(ref _ringGeneration);
            Bridge.StopRinging();
            _ = RestoreAudioMode();
        }
        else
            _ = PlayWebRingtone(false);
    }

    private async Task StartNativeRinging(int generation)
    {
        // The ringer stream follows the call route while the mode is InCommunication, so an armed
        // session holding it would put the whole ring in the earpiece. Nothing on the line - nothing
        // to protect: hand the mode back for the ring, exactly as a Normal-mode ring would sound.
        var liveChatIds = GetLiveAudioChatIds();
        Log.LogInformation("Incoming ring: live audio in [{ChatIds}]", liveChatIds.ToDelimitedString(","));
        if (liveChatIds.Count == 0) {
            try {
                await Hub.AudioFocusUI.YieldCommunicationMode().ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Couldn't yield the communication mode to the incoming ring");
            }
        }

        if (Volatile.Read(ref _ringGeneration) != generation)
            return;

        Bridge!.StartRinging();
    }

    private async Task RestoreAudioMode()
    {
        try {
            await Hub.AudioFocusUI.RestoreCommunicationMode().ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't restore the communication mode after the incoming ring");
        }
    }

    private List<ChatId> GetLiveAudioChatIds()
        => Hub.ActiveChatsUI.ActiveChats.Value
            .Where(c => c.IsListening || c.IsRecording)
            .Select(c => c.ChatId)
            .ToList();

    private async Task PlayWebRingtone(bool mustStart)
    {
        try {
            await Hub.JS.InvokeVoidAsync(mustStart ? JSStartRingtone : JSStopRingtone).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Web ringtone {Action} failed", mustStart ? "start" : "stop");
        }
    }

    private Task TeardownInCall(ChatId chatId)
    {
        if (_overLockRingChatId.Value == chatId)
            return HangUp(chatId);

        return _foregroundRawChatId.Value == chatId
            ? HangUpForegroundCall(chatId)
            : HangUpQuietly(chatId);
    }

    private void EndRing(ChatId chatId)
    {
        CallUI.DropRing(chatId);
        ClearRingFlags(chatId);
        Bridge?.DismissCallNotification(chatId);
    }

    private void ClearRingFlags(ChatId chatId)
    {
        if (_collapsedChatId.Value == chatId)
            _collapsedChatId.Value = null;
        if (_mutedRingChatId.Value == chatId)
            _mutedRingChatId.Value = null;
    }

    private void ClearOverLock()
        => _overLockRingChatId.Value = null;

    private void ClearForegroundCall(ChatId chatId)
    {
        if (_foregroundRawChatId.Value == chatId)
            _foregroundRawChatId.Value = null;
    }

    // Stops local audio, with no screen-specific side effect - the desktop plain-chat view has no call
    // screen to close, so this is all it needs on hang-up. Leaving the call server-side follows from
    // this via the same SetParticipation path as any other presence change - see RunParticipationSync.
    private async Task HangUpQuietly(ChatId chatId)
    {
        CallUI.Release(chatId);
        await StopCallAudio(chatId).ConfigureAwait(true);
    }

    private async Task StopCallAudio(ChatId chatId)
    {
        await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(true);
        await ChatAudioUI.SetListeningState(chatId, false).ConfigureAwait(true);
    }

    // Nested types

    protected sealed record ScreenInput(ActiveCall? Call, ChatId? DialingOutChatId);
}
```

If the build reports a missing namespace for any identifier, re-add the corresponding `using` from the old file's list (`ActualChat.Live`, `ActualChat.Notifications`); if it reports an unused one, drop it.

- [ ] **Step 2: Rework `LiveSessionUI.cs`**

1. Delete the fields `DialingWaitTimeout`, `_callWatches`, `_ringbackLock`, `_ringbackOwner`, and the property `private IncomingCallUI IncomingCallUI => Hub.IncomingCallUI;`. Add, next to the other hub-backed properties:

```csharp
    private CallUI CallUI => Hub.CallUI;
```

2. Replace the body of `StartCall` with:

```csharp
        // Ask on the click itself: it's a real user gesture, the request can't yet race the ringback,
        // and the answered call's join re-reads the (now cached) verdict without prompting again.
        // A call the caller can't be heard on isn't worth ringing the other side for, so a denial
        // stops it here instead of falling back to a listen-only call as the callee side does.
        if (!await AudioRecorder.MicrophonePermission.CheckOrRequest(cancellationToken).ConfigureAwait(false)) {
            Hub.ToastUI.Show(L.Call_NoMicrophoneAccess, "icon-phone-hang-up", ToastDismissDelay.Short);
            return;
        }
        // The slot is taken before the RPC, so a ring arriving meanwhile is already answered Busy.
        if (!CallUI.TryClaimOutgoing(chatId, hasVideo)) {
            Hub.ToastUI.Show(L.Call_AlreadyInCall, "icon-phone-hang-up", ToastDismissDelay.Short);
            return;
        }

        try {
            await LiveSessions.StartCall(Session, chatId, invitees, hasVideo, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            CallUI.Release(chatId);
            if (e is OperationCanceledException)
                throw;

            // Only StandardError.Constraint (e.g. the peer-call gate) carries user-facing text.
            Log.LogWarning(e, "StartCall failed for chat #{ChatId}", chatId);
            var message = e is InvalidOperationException ? e.Message : L.Call_CouldntStart;
            Hub.ToastUI.Show(message, "icon-phone-hang-up", ToastDismissDelay.Short);
        }
```

3. Replace `CancelCall` with:

```csharp
    public Task CancelCall(ChatId chatId, CancellationToken cancellationToken)
    {
        CallUI.Release(chatId);
        return LiveSessions.CancelCall(Session, chatId, cancellationToken);
    }
```

4. In `OnRun`, add `AsyncChain.From(SyncRingback),` to `baseChains`.

5. Delete `StartCallWatch`, `StopCallWatch`, `WatchOutgoingCall`, `StartRingback`, `StopRingback` and `JoinAnsweredCall`. Keep `PlayRingback`. Add, right before `PlayRingback`:

```csharp
    private async Task SyncRingback(CancellationToken cancellationToken)
    {
        // Follows the server's dialing, not the slot's: the slot is claimed before the StartCall RPC, and a
        // refused call must not ring back first.
        var cDialing = await Computed
            .Capture(() => CallUI.GetDialingOutChatId(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var isRingbackOn = false;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var mustPlay = cDialing.Value is not null;
                if (mustPlay != isRingbackOn) {
                    isRingbackOn = mustPlay;
                    _ = PlayRingback(mustPlay);
                }

                await cDialing.WhenInvalidated(cancellationToken).ConfigureAwait(false);
                cDialing = await cDialing.Update(cancellationToken).ConfigureAwait(false);
            }
        }
        finally {
            if (isRingbackOn)
                _ = PlayRingback(false);
        }
    }
```

6. Grep `LiveSessionUI.cs` for `Now` and `IncomingCallUI`; delete the `Now` property if nothing uses it any more, and make sure no `IncomingCallUI.` call remains.

- [ ] **Step 3: Build and run the tests**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj -v q -nologo`
Expected: `0 Error(s)`.

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~CallUI" -nologo -v q`
Expected: `Passed!  - Failed: 0, Passed: 22`.

Run: `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net11.0-android -v q -nologo` — the Android side calls `IncomingCallUI.OnRing` / `Accept` / `Decline` / `OnCallDismissed`, whose signatures are unchanged.
Expected: `0 Error(s)`. If the Android workload isn't installed in this environment, say so in the task report instead of skipping silently.

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs
git commit -m "feat(call): run incoming and outgoing calls through the CallUI slot" -m "IncomingCallUI becomes presentation: rings feed CallUI's candidates, the ringtone, the modal and the call screens follow the held call, and one screen loop replaces the three Reset loops. LiveSessionUI claims the slot before StartCall, releases it on cancel, and plays the ringback while the held call dials; its per-chat outgoing watch and JoinAnsweredCall are gone." -m "Refs #690" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01LB31iLqy17KbPQAe7NaGSX"
```

---

### Task 5: The header call button refuses while another chat holds the slot

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatHeaderCallButton.razor`

**Interfaces:**
- Consumes: `CallUI.CanStartCall(CancellationToken)`, `L.Call_AlreadyInCall`.

The "Tap to call back" button in `CallMessageView` is a plain component with no reactive state; it stays as is and relies on `LiveSessionUI.StartCall`'s own refusal toast from Task 4.

- [ ] **Step 1: Replace the file content**

```razor
@inherits ComputedStateComponent<AppUIHub, ChatHeaderCallButton.Model>
@{
    if (State.IsInitial(out var m))
        return;
    // Hidden while a live conversation is active — the ChatActivityPanel owns that state.
    if (!m.EnableIncompleteUI || m.IsBusy)
        return;
}

<HeaderButton
    Class="btn-start-call"
    Tooltip="@(m.IsInAnotherCall ? L.Call_AlreadyInCall : m.CanCall ? L.ChatHeader_StartCall : L.ChatHeader_CallUnavailable)"
    TooltipPosition="FloatingPosition.Bottom"
    IsDisabled="@(!m.CanCall || m.IsInAnotherCall)"
    Click="OnClick">
    <i class="icon-phone text-2xl"></i>
</HeaderButton>

@code {
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private ChatActivityUI ChatActivityUI => Hub.ChatActivityUI;

    private Chat Chat => ChatContext.Chat;

    [CascadingParameter] public ChatContext ChatContext { get; set; } = null!;

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var enableIncompleteUI = await Features.IsIncompleteUIEnabled(cancellationToken).ConfigureAwait(false);
        if (!enableIncompleteUI)
            return new Model(false, false, false, false);

        var chatId = Chat.Id;
        var activity = await ChatActivityUI.GetCallActivity(chatId, cancellationToken).ConfigureAwait(false);
        // Mirror the server's peer-call gate: a peer call is allowed only while the audio-stream
        // permission is granted (the recipient added the caller or replied, and neither side blocked).
        var canCall = true;
        if (chatId.Kind == ChatKind.Peer) {
            var chat = await Hub.Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
            canCall = chat?.Rules.CanWriteAudio() ?? false;
        }
        // A dialing call counts as busy: the ring is already ours, and offering the button again would
        // re-notify every invitee and extend the ring window.
        var isBusy = activity.IsActive || activity.IsDialing;
        // One call at a time: a call held in another chat disables the button here.
        var canStartCall = await Hub.CallUI.CanStartCall(cancellationToken).ConfigureAwait(false);
        return new Model(true, isBusy, canCall, !isBusy && !canStartCall);
    }

    private Task OnClick()
        => LiveSessionUI.StartCall(Chat.Id, default, false, CancellationToken.None);

    // Nested types

    public sealed record Model(bool EnableIncompleteUI, bool IsBusy, bool CanCall, bool IsInAnotherCall);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj -v q -nologo`
Expected: `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/ChatHeaderCallButton.razor
git commit -m "feat(call): disable the call button while a call is held in another chat" -m "Refs #690" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01LB31iLqy17KbPQAe7NaGSX"
```

---

### Task 6: Android native — one notification, and it always rings

**Files:**
- Modify: `src/dotnet/App.Maui/Platforms/Android/IncomingCallRinger.cs`
- Modify: `src/dotnet/App.Maui/Platforms/Android/Notifications/FirebaseMessagingService.cs`
- Modify: `src/dotnet/App.Maui/Platforms/Android/Notifications/IncomingCallNotifications.cs` (`HandleViewIntent`)
- Modify: `src/dotnet/App.Maui/Platforms/Android/CallActionReceiver.cs`

**Interfaces:**
- Consumes: `CallUI.GetCallChatIdNonComputed()` (Task 2), `IncomingCallNotifications.ListActiveCallChatIds()`, `IncomingCallNotifications.TryParseCallTag(string?)`, `Constants.Call.RingTimeout`.
- Produces: `IncomingCallRinger.Start(TimeSpan? autoStopAfter = null)`. `AndroidIncomingCallsBridge.StartRinging()` keeps calling `IncomingCallRinger.Start()` with no timeout: once Blazor drives the ring, it owns stopping it.

- [ ] **Step 1: Give `IncomingCallRinger` a guarded auto-stop**

Add a field after `_log`:

```csharp
    private static int _generation;
```

Replace `Start` and `Stop` with:

```csharp
    public static void Start(TimeSpan? autoStopAfter = null)
    {
        int generation;
        lock (Lock) {
            generation = ++_generation;
            try {
                // DND is deliberately not consulted: an incoming call is the user's own contact
                // reaching them, not a background alert.
                var ringerMode = AndroidRingerMode.Mode;
                if (ringerMode != DeviceRingerMode.Silent)
                    StartVibration();
                if (ringerMode == DeviceRingerMode.Normal)
                    StartRingtone();
            }
            catch (Exception e) {
                Log.LogWarning(e, "Start failed");
            }
        }
        if (autoStopAfter is { } delay)
            _ = StopAfter(delay, generation);
    }

    public static void Stop()
    {
        lock (Lock) {
            _generation++;
            // Released independently: a throwing player must not leave the vibrator buzzing forever.
            var player = _player;
            _player = null;
            var vibrator = _vibrator;
            _vibrator = null;
            try {
                player?.Release(); // Valid from any state, incl. Error - no preceding Stop needed
            }
            catch (Exception e) {
                Log.LogWarning(e, "Stop: player release failed");
            }
            try {
                vibrator?.Cancel();
            }
            catch (Exception e) {
                Log.LogWarning(e, "Stop: vibrator cancel failed");
            }
        }
    }
```

Add to the private methods:

```csharp
    private static async Task StopAfter(TimeSpan delay, int generation)
    {
        // Only the start that armed this may be stopped by it: any later Start or Stop moved the generation on.
        await Task.Delay(delay).ConfigureAwait(false);
        bool isCurrent;
        lock (Lock)
            isCurrent = _generation == generation;
        if (isCurrent)
            Stop();
    }
```

Update the class comment's first line to say the ringer is driven by `IncomingCallUI` via the bridge **and** started natively together with a shown call notification.

- [ ] **Step 2: Show-or-skip and ring in `FirebaseMessagingService.HandleIncomingCall`**

Replace the method with:

```csharp
    private static void HandleIncomingCall(NotificationData data)
    {
        var chatId = data.ChatId;
        if (chatId is null) {
            Log.LogWarning("Can't handle incoming-call push. Invalid ChatId. Ref messageId: '{MessageId}'", data.MessageId);
            return;
        }

        var scopeAlive = TryGetScopedServices(out var scopedServices);
        var isForeground = AndroidUtils.IsAppForeground();
        DebugLog?.LogInformation("CALL_TRACE: HandleIncomingCall push #{ChatId}, scopeAlive={ScopeAlive}, foreground={Foreground}",
            chatId, scopeAlive, isForeground);
        // Foreground + unlocked: the in-app call UI and ringer own the ring, so the system CallStyle
        // notification would only stack a heads-up banner over them. Show it just when the app is
        // backgrounded, killed, or locked - where its full-screen intent is the only way to reach the user.
        if (scopeAlive && isForeground == true) {
            _ = DispatchToBlazor(
                c => c.GetRequiredService<IncomingCallUI>().OnRing(chatId),
                "IncomingCallUI.OnRing");
            return;
        }

        // One call at a time: a ring behind another chat's call shows nothing here and doesn't ring. Busy
        // is the Blazor side's to send once it sees the ring - native does nothing for it at all.
        if (!IsAnotherCallHeld(chatId, scopedServices)) {
            // The channel is silent, so the ringtone starts with the notification: heads-up or full-screen,
            // a call notification without one is a missed call.
            IncomingCallNotifications.Show(data);
            IncomingCallRinger.Start(Constants.Call.RingTimeout);
        }
        if (scopeAlive)
            _ = DispatchToBlazor(
                c => c.GetRequiredService<IncomingCallUI>().OnRing(chatId),
                "IncomingCallUI.OnRing");
    }

    private static bool IsAnotherCallHeld(ChatId chatId, IServiceProvider? scopedServices)
    {
        // Fail-open like ShouldSuppressForDevice: a check that throws must never hide a call.
        try {
            if (IncomingCallNotifications.ListActiveCallChatIds().Any(id => id != chatId))
                return true;

            var callChatId = scopedServices?.GetRequiredService<CallUI>().GetCallChatIdNonComputed();
            return callChatId is not null && callChatId != chatId;
        }
        catch (Exception e) {
            Log.LogWarning(e, "IsAnotherCallHeld failed for chat #{ChatId}; showing the call", chatId);
            return false;
        }
    }
```

- [ ] **Step 3: Stop the native ringtone when its call is dismissed**

In `OnMessageReceived`, replace the dismissal block with:

```csharp
        if (data.DismissedTags.Count > 0) {
            // Read before cancelling: afterwards nothing tells which call notification was on screen.
            var shownCallChatIds = IncomingCallNotifications.ListActiveCallChatIds();
            var notificationManager = NotificationManagerCompat.From(this)!;
            foreach (var tag in data.DismissedTags)
                notificationManager.Cancel(tag, 0);
            ClearAttentionRequests(data.DismissedTags);
            ClearForegroundCallRings(data.DismissedTags);
            StopRingForDismissedCalls(data.DismissedTags, shownCallChatIds);

            return;
        }
```

Add next to `ClearForegroundCallRings`:

```csharp
    private static void StopRingForDismissedCalls(IReadOnlyList<string> dismissedTags, ChatId[] shownCallChatIds)
    {
        // The ringtone that started with a shown call notification goes with it. One Blazor drives stops
        // on its own through IncomingCallUI; stopping it twice is harmless.
        var isShownCallDismissed = dismissedTags
            .Select(IncomingCallNotifications.TryParseCallTag)
            .Any(chatId => chatId is not null && shownCallChatIds.Contains(chatId));
        if (isShownCallDismissed)
            IncomingCallRinger.Stop();
    }
```

- [ ] **Step 4: Stop it on Answer and Decline**

In `IncomingCallNotifications.HandleViewIntent`, in the `AcceptExtraKey` branch, right after `Dismiss(chatId);` add:

```csharp
            // Blazor starting up sees the call already Active and would never stop a ring it didn't start.
            IncomingCallRinger.Stop();
```

In `CallActionReceiver.OnReceive`, right after `IncomingCallNotifications.Dismiss(chatId);` add:

```csharp
        IncomingCallRinger.Stop();
```

- [ ] **Step 5: Build**

Run: `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net11.0-android -v q -nologo`
Expected: `0 Error(s)`. If the Android workload is missing here, report it; the device checks in Task 8 then need the developer's own build.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/App.Maui/Platforms/Android
git commit -m "feat(android): ring with every shown call notification, show one at a time" -m "The call channel is silent, so a killed or backgrounded app used to show a silent call until Blazor started. The ringtone now starts with the notification and stops on dismissal, timeout, Answer and Decline. A ring behind another chat's call shows no notification at all; Busy stays with the Blazor side." -m "Refs #690" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01LB31iLqy17KbPQAe7NaGSX"
```

---

### Task 7: Docs follow the code

**Files:**
- Modify: `docs/calls/incoming-call-flow.md`
- Modify: `docs/superpowers/specs/2026-09-14-call-slot-design.md`

- [ ] **Step 1: Replace the "Latching one ring" section of `incoming-call-flow.md`**

Replace everything from `## Latching one ring` up to (not including) `## Presenting the ring` with:

```markdown
## The call slot

`CallUI` holds the one call this client is in, incoming or outgoing: `_callChatId` names the
chat, `_activeCall` the confirmed call with its origin and phase (`Ringing`, `Dialing`,
`Active`). While the slot is held, every other ring is answered `Busy` and doesn't ring, and
no new outgoing call can start. Ambient live sessions never hold it.

**Searching.** Whenever the candidate list, a candidate's ring or the slot changes, the
search walks the ringing candidates newest first:

| Slot | Outcome |
|---|---|
| free | claim it for this chat |
| claimed, call not confirmed yet | wait for the next pass |
| held by this chat | nothing |
| held by another chat | `ConfirmRing(Busy)` once per chat, and close its Android notification |

Candidates that aren't ringing on a pass are dropped.

**Holding.** While the slot is held, a second loop follows that chat's session and releases
the slot when the call ends:

| Held call | Released when |
|---|---|
| claimed by the search | confirmed as `Incoming/Ringing` (and acked `Ringing`) if the chat rings, else released |
| `Incoming/Ringing` | the ring ends — canceled, timed out, answered or declined elsewhere |
| `Outgoing/Dialing` | no answer, declined, canceled, the session vanished; an answer joins the call and moves it to `Active` |
| `Active` | the session stops being a call, or I was in the conversation and left it |

`StartCall` claims the slot as `Outgoing/Dialing` before its RPC; `Accept` commits the ring to
`Active` before its RPC, claiming a free slot itself when Answer on a notification beat the
search. Hanging up releases the slot right away.

Everything that shows a call reads the slot: the modal, the island, the over-lock and narrow
full-screen views, the ringtone, the ringback. `GetIncomingCall` is the slot filtered to
`Incoming/Ringing`.
```

- [ ] **Step 2: Update the rest of `incoming-call-flow.md`**

- In "Presenting the ring", replace the `SyncRings` bullet with `**SyncRings** starts or stops the ringtone as the slot enters or leaves Incoming/Ringing.`, and replace the three teardown-loop bullets with one: `**SyncCallScreens** follows the slot: an outgoing call that starts dialing gets its modal or full-screen view, and a released call gets its screens closed and its audio stopped.`
- In the "How the client learns about the ring" table, change the "Android, backgrounded, killed or locked" row to: `IncomingCallNotifications.Show posts a CallStyle notification with Answer/Decline and a full-screen intent, and IncomingCallRinger starts the ringtone with it. Neither happens when another chat's call notification is shown or the slot is held by another chat. If the Blazor scope is alive, OnRing is dispatched as well.`
- In "Answering → Accept", replace step 2 with: `Commits the ring to Active in the slot (refusing with "You're already in a call" if another chat holds it), then ends the local ring and cancels the Android system notification. Both happen before the RPC, so the call screen doesn't blink between "ring ended" and "audio started".`
- In "Known gaps", delete the `RingAck.Received` bullet's second sentence (the Busy race is gone) and delete the "client's call state is still several independent fields" bullet. Add: `**A ring during an active call doesn't ring at all.** It gets Busy; answering it means hanging up first. Holding the current call to take another is a later feature.` and `**The slot is per client scope.** Each app instance — and on the web each browser tab — holds its own, so the same user can still be in a call in one tab and take another in a second tab.`

- [ ] **Step 3: Record the two as-built differences in the spec**

In `docs/superpowers/specs/2026-09-14-call-slot-design.md`, append a section:

```markdown
## As built

- **The call card isn't reactive.** `CallMessageView` renders once, so its "Tap to call back"
  stays enabled while a call is held; `LiveSessionUI.StartCall` refuses with the
  `Call_AlreadyInCall` toast instead. Only the header button is disabled.
- **Hanging up releases the slot at once.** `HangUpQuietly` calls `CallUI.Release` itself
  rather than waiting for the holding loop to notice the conversation ended; the loop's
  release then finds an empty slot.
- **Outgoing screens and the ringback wait for the server.** The slot is claimed before the
  `StartCall` RPC, but the outgoing modal reads the invitee from the session and a refused
  call must not ring back first, so both follow `CallUI.GetDialingOutChatId` — the held
  outgoing call once its session is dialing.
```

- [ ] **Step 4: Commit**

```bash
git add docs/calls/incoming-call-flow.md docs/superpowers/specs/2026-09-14-call-slot-design.md
git commit -m "docs(calls): describe the call slot as built" -m "Refs #690" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01LB31iLqy17KbPQAe7NaGSX"
```

---

### Task 8: Verify on the running app

**Files:** none — produces a report.

The two-Chrome rig for slot `ws1`: `mcp__chrome1__*` is `er5ir9` ("Mega Tuna", admin, the only one who can call), `mcp__chrome2__*` is `ILqcWq` ("Naval Wolf"). Peer chat `p-ILqcWq-er5ir9`; group `0NYND2MfRb` gives a second concurrent call via `/test/voice-call` (set chat id, Tab, tick the invitee with `input.click()` through `evaluate_script`). `RingTimeout` is 20 s, so start the second call right after the first.

**Two callers from one user.** The slot now refuses a second call from the same client, so one tab of `er5ir9` can't ring `ILqcWq` twice. The slot lives in the Blazor scope, and in render mode `'s'` every tab has its own circuit and its own `CallUI`: use **tab A** (`/chat/p-ILqcWq-er5ir9`) and **tab B** (`/test/voice-call`) of chrome1 as two independent callers, switching with `mcp__chrome1__select_page`.

An invite's `Ack` is readable from Valkey:

```powershell
$keys = docker exec actual-chat-infra-redis-1 valkey-cli --scan --pattern '*live-session:invites*'
foreach ($k in $keys) { "== $k"; docker exec actual-chat-infra-redis-1 valkey-cli --no-raw HGETALL $k }
```

The value is a MessagePack array of 8: InviteeId, Status (1 Ringing, 3 Active), RingingAt, RespondedAt, ActiveAt, EndedAt, **Ack (0 Received, 1 Ringing, 2 Busy)**, AckAt. Accepting a call starts paid transcription — hang up (`.c-hangup`) right after checking.

- [ ] **Step 1: Rebuild the slot server**

GET `https://ws1.local.voxt.ai/health/stop` and wait for `Watchdog: started` in `tmp/server-loop.log`. Reload both Chrome tabs with `ignoreCache`; keep render mode `'s'` (`debugUI.setRenderMode('s')`).

- [ ] **Step 2: Run the scenarios**

| # | Scenario | Expected |
|---|---|---|
| 1 | tab A calls; chrome2 declines | `.incoming-call-modal` appears, `Ack = 1`; after decline the modal closes and tab A's dialing screen goes away |
| 2 | tab A calls; nobody answers | the modal closes after ~20 s, no invites left in Valkey, tab A's slot is free again (its header button is back) |
| 3 | tab A calls, then tab B calls in the group; chrome2 declines the first | one modal, peer `Ack = 1`, group `Ack = 2`; after the decline the group ring surfaces and its `Ack` turns `1` |
| 4 | chrome2 accepts tab A's call, then tab B calls in the group | peer invite `Status = 3`; group `Ack = 2`; chrome2 shows **no** modal and plays no ringtone. Hang up at once |
| 5 | tab A dials the peer chat; in the same tab `debugUI.navigateTo('/test/voice-call')` (keeps the circuit — `location.assign` would reload it and drop the slot), set up the group call, Start Call | toast "You're already in a call"; no group invite appears in Valkey |
| 6 | after 4 and 5 everyone hangs up / cancels | recording off on both, tab A's header call button enabled again, no invites in Valkey |

The disabled header button with its "You're already in a call" tooltip needs a second peer chat of `er5ir9` to look at while a call is held; if one exists, check it there, otherwise say in the report that only the toast path was verified.

Check `tmp/server-loop-server-run.log` for `CALL_TRACE`, `WRN` and `ERR` lines from `CallUI` / `IncomingCallUI` / `LiveSessionUI` after each scenario.

- [ ] **Step 3: Hand the Android checks to the developer**

These need a real device and can't run on the rig; list them in the final report:

1. App killed, screen unlocked, one call → heads-up notification **with** ringtone; the caller cancels → the ringtone stops.
2. App killed, two calls in a row → one notification; after opening the app the second invite has `Ack = 2`.
3. Screen locked during an accepted call, a second call → no notification, no ringtone, `Ack = 2`.
4. Answer from the notification of a killed app → the ringtone stops at the tap, the call connects.

- [ ] **Step 4: Report**

Summarize which scenarios passed, anything that didn't with its log lines, and the Android checklist for the developer.
