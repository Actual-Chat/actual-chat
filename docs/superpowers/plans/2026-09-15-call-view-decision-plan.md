# One Place Decides the Call's Screen — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Which screen shows the call in the slot — modal, full-screen view, island or nothing — is decided by one pure function of the call, the screen width and three flags, and every call surface only reads its result.

**Architecture:** `CallScreensUI.DecideView` (pure, unit-tested) turns the slot's `ActiveCall`, the dialing confirmation, `isNarrow` and `CallScreenFlags` (collapsed, in chat, over-lock) into a `CallView`. The compute method `GetCallView` feeds it from `CallUI`, `BrowserInfo.ScreenSize` and the flags. `FullScreenCallView`, `CollapsedCallView` and the new `CallModal` render only for their own `Kind`; one loop, `SyncCallView`, opens the modal and is the only place that tears screens down when the slot is released.

**Tech Stack:** .NET 11, Blazor, ActualLab.Fusion (`MutableState`, `[ComputeMethod]`, `Computed.Capture(...).Changes()`), xUnit + FluentAssertions, Tailwind `@apply` CSS.

**Spec:** `docs/superpowers/specs/2026-09-14-call-view-decision-design.md` — read it before Task 1.

## Global Constraints

- Branch `fix/update-ui-287`, issue #690 (already linked in `branch.fix/update-ui-287.issue`). Every commit message ends with `Refs #690` and the trailer `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>` + `Claude-Session: https://claude.ai/code/session_01LB31iLqy17KbPQAe7NaGSX`. Multi-line messages go through repeated `-m` flags, never newlines inside one argument.
- Read `docs/CODING_STYLE.md` and `docs/ui/components.md` before touching C#, Razor or CSS: no `Async` suffix, no new `///` on members, comments only for what the code can't say (2 lines max on a member, inside the body), 120-char lines, LF, Allman braces for types/methods and K&R otherwise (all Razor K&R), blank line after `return`/`throw`/`continue` except before `}`/`case`/`else` and inside a run of guard clauses, button classes start with `btn-`, one `@apply` line per property category.
- A style hook reviews every `.cs`/`.razor`/`.css` edit; fix everything it reports by default. If it flags `c-call-btn` in `FullScreenCallView.razor` / `full-screen-call-view.css`, rename it to `btn-call` there too (and in the over-lock mock of `CallTestPage.razor` if present).
- `DecideView` rules, in order, first match wins (a flag counts only when it names the slot's chat): 1 free slot → `None`; 2 over-lock flag + incoming (any phase) → `FullScreen`, `IsOverLock`; 3 ringing + collapsed → `Collapsed`; 4 ringing → `Modal`; 5 dialing, not confirmed → `None`; 6 dialing + collapsed → `Collapsed`; 7 dialing + narrow → `FullScreen`; 8 dialing → `Modal`; 9 active + in chat → `None`; 10 active + narrow → `FullScreen`; 11 active → `None`.
- Screens are torn down only by `SyncCallView` on release. Actions (`Decline`, `HangUp`, `Accept`) change the slot or a flag and never clear the over-lock flag of a held call.
- Tests: FluentAssertions, AAA comments (`// arrange`, `// act`, `// assert`; arrange may be omitted when trivial), `…Should…` names, no underscores.
- C# build check: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj -v q -nologo` must end with `0 Error(s)`.
- Unit tests: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj -v q -nologo --filter "FullyQualifiedName~CallScreensUIDecisionsTest|FullyQualifiedName~CallUIDecisionsTest"`.

## File Map

| File | Change | Responsibility |
|---|---|---|
| `src/dotnet/UI.Blazor.App/Services/ActiveCall.cs` | modify | + `CallViewKind`, `CallView`, `CallScreenFlags` |
| `src/dotnet/UI.Blazor.App/Services/CallScreensUI.Decisions.cs` | create | Pure `DecideView` |
| `src/dotnet/UI.Blazor.App/Services/CallScreensUI.cs` | rewrite | Flags, `GetCallView`, `GetCallPeerId`, actions |
| `src/dotnet/UI.Blazor.App/Services/CallScreensUI.StateSync.cs` | rewrite | `SyncRingtone`, `SyncCallView`, release teardown |
| `src/dotnet/UI.Blazor.App/Components/FullScreenCallView/FullScreenCallView.razor` | modify | Reads the view; phase from `call.Phase` |
| `src/dotnet/UI.Blazor.App/Components/CollapsedCallView/CollapsedCallView.razor` | modify | Reads the view and `GetCallPeerId` |
| `src/dotnet/UI.Blazor.App/Components/CallModal/CallModal.razor` | create | One modal for a ring and for dialing |
| `src/dotnet/UI.Blazor.App/Components/CallModal/CallModalHeader.razor` | move | From `IncomingCallModal/IncomingCallModalHeader.razor` |
| `src/dotnet/UI.Blazor.App/Components/CallModal/call-modal.css` | create | Merged modal styles |
| `src/dotnet/UI.Blazor.App/Components/IncomingCallModal/*`, `OutgoingCallModal/*` | delete | Replaced by `CallModal` |
| `src/dotnet/UI.Blazor.App/styles.css`, `Module/BlazorUIAppModule.cs` | modify | CSS import, modal registration |
| `src/dotnet/UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs`, `src/dotnet/UI.Blazor/Module/BlazorUIAotSource.g.cs` | modify | AOT keep lists |
| `src/dotnet/UI.Blazor.App/Pages/Test/CallTestPage.razor`, `call-test-page.css` | modify | Class rename in the static mock |
| `tests/Chat.UI.Blazor.UnitTests/CallScreensUIDecisionsTest.cs` | create | Rule table tests |
| `docs/calls/incoming-call-flow.md` | modify | Describe the view as built |

---

### Task 1: View types and `DecideView`

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/ActiveCall.cs`
- Create: `src/dotnet/UI.Blazor.App/Services/CallScreensUI.Decisions.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/CallScreensUIDecisionsTest.cs`

**Interfaces:**
- Consumes: `ActiveCall(ChatId ChatId, CallOrigin Origin, CallPhase Phase, AuthorId? PeerId, bool HasVideo)`, `CallOrigin`, `CallPhase` (existing, `ActiveCall.cs`).
- Produces: `public enum CallViewKind { None, Modal, FullScreen, Collapsed }`; `public sealed record CallView(ActiveCall? Call, CallViewKind Kind, bool IsOverLock)` with `public static readonly CallView None`; `internal readonly record struct CallScreenFlags(ChatId? CollapsedChatId, ChatId? InChatChatId, ChatId? OverLockChatId)`; `internal static CallView CallScreensUI.DecideView(ActiveCall? call, bool isDialingConfirmed, bool isNarrow, CallScreenFlags flags)`.

`UI.Blazor.App.csproj` already has `<InternalsVisibleTo Include="ActualChat.Chat.UI.Blazor.UnitTests" />`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Chat.UI.Blazor.UnitTests/CallScreensUIDecisionsTest.cs`:

```csharp
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CallScreensUIDecisionsTest
{
    private static readonly ChatId ChatA = ChatId.Parse("the-actual-one");
    private static readonly ChatId ChatB = ChatId.Parse("0NYND2MfRb");
    private static readonly AuthorId CallerA = AuthorId.New(ChatA, 1);

    [Theory]
    [InlineData(CallOrigin.Incoming, CallPhase.Ringing, false, CallViewKind.Modal)]
    [InlineData(CallOrigin.Incoming, CallPhase.Ringing, true, CallViewKind.Modal)]
    [InlineData(CallOrigin.Outgoing, CallPhase.Dialing, false, CallViewKind.Modal)]
    [InlineData(CallOrigin.Outgoing, CallPhase.Dialing, true, CallViewKind.FullScreen)]
    [InlineData(CallOrigin.Incoming, CallPhase.Active, false, CallViewKind.None)]
    [InlineData(CallOrigin.Incoming, CallPhase.Active, true, CallViewKind.FullScreen)]
    [InlineData(CallOrigin.Outgoing, CallPhase.Active, false, CallViewKind.None)]
    [InlineData(CallOrigin.Outgoing, CallPhase.Active, true, CallViewKind.FullScreen)]
    public void ViewShouldFollowPhaseAndWidth(CallOrigin origin, CallPhase phase, bool isNarrow, CallViewKind expected)
    {
        // act
        var view = Decide(Call(origin, phase), isNarrow);

        // assert
        view.Kind.Should().Be(expected);
        view.IsOverLock.Should().BeFalse();
    }

    [Fact]
    public void FreeSlotShouldShowNothing()
    {
        // act
        var view = Decide(null, isNarrow: true);

        // assert
        view.Should().Be(CallView.None);
    }

    [Fact]
    public void UnconfirmedDialingShouldShowNothingButKeepCall()
    {
        // arrange
        var call = Call(CallOrigin.Outgoing, CallPhase.Dialing);

        // act
        var view = Decide(call, isNarrow: true, isDialingConfirmed: false);

        // assert
        view.Kind.Should().Be(CallViewKind.None, "there's no invitee to show before the server's dialing");
        view.Call.Should().Be(call, "the view loop tells a released slot by the call going away");
    }

    [Theory]
    [InlineData(CallOrigin.Incoming, CallPhase.Ringing, false)]
    [InlineData(CallOrigin.Incoming, CallPhase.Ringing, true)]
    [InlineData(CallOrigin.Outgoing, CallPhase.Dialing, false)]
    [InlineData(CallOrigin.Outgoing, CallPhase.Dialing, true)]
    public void CollapsedCallShouldShowIsland(CallOrigin origin, CallPhase phase, bool isNarrow)
    {
        // act
        var view = Decide(Call(origin, phase), isNarrow, Flags(collapsed: ChatA));

        // assert
        view.Kind.Should().Be(CallViewKind.Collapsed);
    }

    [Fact]
    public void CollapseShouldNotHideActiveCall()
    {
        // act
        var view = Decide(Call(CallOrigin.Outgoing, CallPhase.Active), isNarrow: true, Flags(collapsed: ChatA));

        // assert
        view.Kind.Should().Be(CallViewKind.FullScreen, "an answered collapsed call gets its full-screen view");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InChatShouldHideActiveCall(bool isNarrow)
    {
        // act
        var view = Decide(Call(CallOrigin.Incoming, CallPhase.Active), isNarrow, Flags(inChat: ChatA));

        // assert
        view.Kind.Should().Be(CallViewKind.None);
    }

    [Fact]
    public void InChatShouldNotHideDialing()
    {
        // act
        var view = Decide(Call(CallOrigin.Outgoing, CallPhase.Dialing), isNarrow: true, Flags(inChat: ChatA));

        // assert
        view.Kind.Should().Be(CallViewKind.FullScreen);
    }

    [Theory]
    [InlineData(CallPhase.Ringing)]
    [InlineData(CallPhase.Active)]
    public void OverLockShouldShowFullScreenOnAnyWidth(CallPhase phase)
    {
        // act
        var view = Decide(Call(CallOrigin.Incoming, phase), isNarrow: false, Flags(overLock: ChatA));

        // assert
        view.Kind.Should().Be(CallViewKind.FullScreen);
        view.IsOverLock.Should().BeTrue();
    }

    [Theory]
    [InlineData(CallPhase.Ringing)]
    [InlineData(CallPhase.Active)]
    public void OverLockShouldOverrideCollapseAndInChat(CallPhase phase)
    {
        // arrange
        var flags = Flags(collapsed: ChatA, inChat: ChatA, overLock: ChatA);

        // act
        var view = Decide(Call(CallOrigin.Incoming, phase), isNarrow: true, flags);

        // assert
        view.Kind.Should().Be(CallViewKind.FullScreen);
        view.IsOverLock.Should().BeTrue();
    }

    [Fact]
    public void OverLockShouldBeIgnoredForOutgoingCall()
    {
        // act
        var view = Decide(Call(CallOrigin.Outgoing, CallPhase.Dialing), isNarrow: false, Flags(overLock: ChatA));

        // assert
        view.Kind.Should().Be(CallViewKind.Modal);
        view.IsOverLock.Should().BeFalse();
    }

    [Fact]
    public void FlagsOfAnotherChatShouldBeIgnored()
    {
        // arrange
        var flags = Flags(collapsed: ChatB, inChat: ChatB, overLock: ChatB);

        // act
        var ringView = Decide(Call(CallOrigin.Incoming, CallPhase.Ringing), isNarrow: false, flags);
        var activeView = Decide(Call(CallOrigin.Incoming, CallPhase.Active), isNarrow: true, flags);

        // assert
        ringView.Kind.Should().Be(CallViewKind.Modal);
        activeView.Kind.Should().Be(CallViewKind.FullScreen);
    }

    private static CallView Decide(
        ActiveCall? call,
        bool isNarrow,
        CallScreenFlags flags = default,
        bool isDialingConfirmed = true)
        => CallScreensUI.DecideView(call, isDialingConfirmed, isNarrow, flags);

    private static CallScreenFlags Flags(ChatId? collapsed = null, ChatId? inChat = null, ChatId? overLock = null)
        => new(collapsed, inChat, overLock);

    private static ActiveCall Call(CallOrigin origin, CallPhase phase)
        => new(ChatA, origin, phase, origin == CallOrigin.Incoming ? CallerA : null, false);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj -v q -nologo --filter "FullyQualifiedName~CallScreensUIDecisionsTest"`
Expected: build FAILS with `CS0246`/`CS0117` — `CallViewKind`, `CallView`, `CallScreenFlags`, `DecideView` don't exist.

- [ ] **Step 3: Add the view types**

Append to `src/dotnet/UI.Blazor.App/Services/ActiveCall.cs` (after `CallFacts`):

```csharp

public enum CallViewKind { None, Modal, FullScreen, Collapsed }

// Call stays set when Kind is None: that's how the view loop tells the slot got released.
public sealed record CallView(ActiveCall? Call, CallViewKind Kind, bool IsOverLock)
{
    public static readonly CallView None = new(null, CallViewKind.None, false);
}

internal readonly record struct CallScreenFlags(
    ChatId? CollapsedChatId, ChatId? InChatChatId, ChatId? OverLockChatId);
```

- [ ] **Step 4: Write `DecideView`**

Create `src/dotnet/UI.Blazor.App/Services/CallScreensUI.Decisions.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public partial class CallScreensUI
{
    internal static CallView DecideView(
        ActiveCall? call, bool isDialingConfirmed, bool isNarrow, CallScreenFlags flags)
    {
        if (call is null)
            return CallView.None;

        var chatId = call.ChatId;
        if (call.Origin == CallOrigin.Incoming && flags.OverLockChatId == chatId)
            return new CallView(call, CallViewKind.FullScreen, true);

        var isCollapsed = flags.CollapsedChatId == chatId;
        var kind = call.Phase switch {
            CallPhase.Ringing => isCollapsed ? CallViewKind.Collapsed : CallViewKind.Modal,
            // The outgoing screens name the invitee from the session, so they wait for the server's dialing.
            CallPhase.Dialing when !isDialingConfirmed => CallViewKind.None,
            CallPhase.Dialing when isCollapsed => CallViewKind.Collapsed,
            CallPhase.Dialing => isNarrow ? CallViewKind.FullScreen : CallViewKind.Modal,
            _ when flags.InChatChatId == chatId => CallViewKind.None,
            // A wide screen keeps an active call in its chat.
            _ => isNarrow ? CallViewKind.FullScreen : CallViewKind.None,
        };
        return new CallView(call, kind, false);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj -v q -nologo --filter "FullyQualifiedName~CallScreensUIDecisionsTest"`
Expected: PASS, 24 tests (8 + 1 + 1 + 4 + 1 + 2 + 1 + 2 + 2 + 1 + 1).

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/ActiveCall.cs src/dotnet/UI.Blazor.App/Services/CallScreensUI.Decisions.cs tests/Chat.UI.Blazor.UnitTests/CallScreensUIDecisionsTest.cs
git commit -m "feat(call): decide the call's screen in one pure function" -m "CallScreensUI.DecideView picks the modal, the full-screen view, the island or nothing from the call in the slot, the dialing confirmation, the screen width and the collapsed, in-chat and over-lock flags." -m "Refs #690" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01LB31iLqy17KbPQAe7NaGSX"
```

---

### Task 2: `CallScreensUI` and the two views follow `GetCallView`

**Files:**
- Rewrite: `src/dotnet/UI.Blazor.App/Services/CallScreensUI.cs`
- Rewrite: `src/dotnet/UI.Blazor.App/Services/CallScreensUI.StateSync.cs`
- Modify: `src/dotnet/UI.Blazor.App/Components/FullScreenCallView/FullScreenCallView.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/CollapsedCallView/CollapsedCallView.razor`

**Interfaces:**
- Consumes: `DecideView`, `CallView`, `CallViewKind`, `CallScreenFlags` (Task 1); `CallUI.GetActiveCall`, `CallUI.GetDialingOutChatId`, `CallUI.GetActiveCallNonComputed`, `CallUI.HangUp(ChatId)`, `CallUI.CancelCall(ChatId, CancellationToken)`, `Hub.BrowserInfo.ScreenSize` (`IState<ScreenSize>`), `ScreenSize.IsNarrow()`.
- Produces: `[ComputeMethod] Task<CallView> CallScreensUI.GetCallView(CancellationToken)`; `[ComputeMethod] Task<AuthorId?> CallScreensUI.GetCallPeerId(CancellationToken)`; `void Collapse(ChatId)`, `void Expand(ChatId)`, `Task HangUp(ChatId)`, `Task LeaveCallScreen(ChatId)` (unchanged signatures). Removed: `GetOverLockChatId`, `GetForegroundCallChatId`, `GetModalCall`, `GetScreenInput`, `ScreenInput`. `CollapsedChatId` stays public until Task 3 (the old modals still read it).

The old `IncomingCallModal` / `OutgoingCallModal` stay in this task; `SyncCallView` opens them through a temporary `ShowCallModal` that Task 3 replaces with `CallModal`.

- [ ] **Step 1: Rewrite `CallScreensUI.cs`**

Replace the whole file with:

```csharp
using ActualChat.Localization;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Everything that shows the call <see cref="CallUI"/> holds: the modal, the island, the full-screen view,
/// the ringtone. <see cref="GetCallView"/> alone decides which of them shows it.
/// </summary>
public partial class CallScreensUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    // Screen requests that can outlive their call: DecideView honors them only while the slot holds that call.
    private readonly MutableState<ChatId?> _overLockRingChatId;
    private readonly MutableState<ChatId?> _collapsedChatId;
    // The active call whose full-screen view gave way to its chat.
    private readonly MutableState<ChatId?> _inChatChatId;
    // The ring whose ringtone the user silenced; the ring itself keeps going.
    private readonly MutableState<ChatId?> _mutedRingChatId;

    public IState<ChatId?> CollapsedChatId => _collapsedChatId;
    public IState<ChatId?> MutedRingChatId => _mutedRingChatId;

    private IIncomingCallsBridge? Bridge { get; }
    private CallUI CallUI => Hub.CallUI;
    private ILogger? CallDebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.AndroidIncomingCalls);

    public CallScreensUI(AppUIHub hub) : base(hub)
    {
        Bridge = hub.Services.GetService<IIncomingCallsBridge>();
        _overLockRingChatId = NewChatIdState("OverLockRingChatId");
        _collapsedChatId = NewChatIdState("CollapsedChatId");
        _inChatChatId = NewChatIdState("InChatChatId");
        _mutedRingChatId = NewChatIdState("MutedRingChatId");
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual async Task<CallView> GetCallView(CancellationToken cancellationToken)
    {
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is null)
            return CallView.None;

        var dialingOutChatId = await CallUI.GetDialingOutChatId(cancellationToken).ConfigureAwait(false);
        var screenSize = await Hub.BrowserInfo.ScreenSize.Use(cancellationToken).ConfigureAwait(false);
        var flags = new CallScreenFlags(
            await _collapsedChatId.Use(cancellationToken).ConfigureAwait(false),
            await _inChatChatId.Use(cancellationToken).ConfigureAwait(false),
            await _overLockRingChatId.Use(cancellationToken).ConfigureAwait(false));
        return DecideView(call, dialingOutChatId == call.ChatId, screenSize.IsNarrow(), flags);
    }

    [ComputeMethod]
    public virtual async Task<AuthorId?> GetCallPeerId(CancellationToken cancellationToken)
    {
        // The caller of a ring; for my own call, whoever I'm calling.
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is null)
            return null;
        if (call.Origin == CallOrigin.Incoming)
            return call.PeerId;

        var live = await Hub.LiveSessionUI.Get(call.ChatId, cancellationToken).ConfigureAwait(false);
        return live is { Invites.Count: > 0 } ? live.Invites[0].InviteeId : null;
    }

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetIncomingCall(CancellationToken cancellationToken)
    {
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        return call is { Origin: CallOrigin.Incoming, Phase: CallPhase.Ringing, PeerId: { } callerId }
            ? new IncomingCall(call.ChatId, callerId, call.HasVideo)
            : null;
    }

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

    public void OnCallDismissed(ChatId chatId)
    {
        if (chatId.Value.IsNullOrEmpty())
            return;

        EndRing(chatId);
        _ = Bridge?.OnCallHandled(false);
    }

    public void OnOverLockScreenRendered()
    {
        // The render callback fires before the WebView paints, so the native cover goes a beat later -
        // otherwise a cold start flashes the restored route for a frame.
        CallDebugLog?.LogInformation("CALL_TRACE: OnOverLockScreenRendered");
        _ = RevealCallScreenAfterPaint();
    }

    public async Task Accept(ChatId chatId)
    {
        var isOverLock = _overLockRingChatId.Value == chatId;
        // Straight from the session, not the slot: Answer on an Android notification can land before the
        // search claimed the ring.
        var call = await CallUI.GetRingingCall(chatId, CancellationToken.None).ConfigureAwait(true);
        if (call is null) {
            EndRing(chatId);
            _ = Bridge?.OnCallHandled(false);
            ShowToast(L.Call_Ended);
            return;
        }

        // Committed before the ring is dropped and before the accept RPC starts: the view, derived from
        // the slot, must not blink off between "ring ended" and "audio started".
        if (!CallUI.TryCommitAccept(call)) {
            ShowToast(L.Call_AlreadyInCall);
            return;
        }

        Bridge?.DismissCallNotification(chatId);
        try {
            await CallUI.AcceptCall(chatId, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception e) {
            CallUI.Release(chatId);
            _ = Bridge?.OnCallHandled(false);
            Log.LogWarning(e, "AcceptCall failed for chat #{ChatId}", chatId);
            ShowToast(L.Call_Ended);
            return;
        }

        try {
            await JoinAcceptedCall(chatId, isOverLock).ConfigureAwait(true);
        }
        catch {
            CallUI.Release(chatId);
            throw;
        }
    }

    public async Task Decline(ChatId chatId)
    {
        // Moving back behind the lock screen is the release teardown's, as for a ring that ends on its own.
        var isOverLock = _overLockRingChatId.Value == chatId;
        EndRing(chatId);
        if (!isOverLock)
            _ = Bridge?.OnCallHandled(false);
        try {
            await CallUI.DeclineCall(chatId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "DeclineCall failed for chat #{ChatId}", chatId);
        }
    }

    public async Task DeclineAndOpenChat(ChatId chatId)
    {
        await Decline(chatId).ConfigureAwait(true);
        await OpenChat(chatId).ConfigureAwait(true);
    }

    public void ToggleMuteRing(ChatId chatId)
        => _mutedRingChatId.Value = _mutedRingChatId.Value == chatId ? null : chatId;

    public void Collapse(ChatId chatId)
    {
        // A modal disposed after its call ended must not collapse whatever holds the slot next.
        if (CallUI.GetActiveCallNonComputed() is not { } call || call.ChatId != chatId)
            return;

        // A collapsed ring rings silently; the island still accepts and declines it.
        if (call.Phase == CallPhase.Ringing)
            _mutedRingChatId.Value = chatId;
        _collapsedChatId.Value = chatId;
    }

    public void Expand(ChatId chatId)
        => ClearIf(_collapsedChatId, chatId);

    public Task HangUp(ChatId chatId)
        => CallUI.GetActiveCallNonComputed() is { Origin: CallOrigin.Outgoing, Phase: CallPhase.Dialing } call
            && call.ChatId == chatId
            ? CancelCall(chatId)
            : CallUI.HangUp(chatId);

    public async Task LeaveCallScreen(ChatId chatId)
    {
        if (_overLockRingChatId.Value == chatId) {
            // The chat is behind the keyguard; a cancelled PIN keeps the call screen up.
            var isUnlocked = Bridge is null || await Bridge.OnCallHandled(true).ConfigureAwait(true);
            if (!isUnlocked)
                return;
        }

        // In chat goes first: the over-lock flag cleared alone would bring the narrow full-screen view back.
        _inChatChatId.Value = chatId;
        ClearIf(_overLockRingChatId, chatId);
        await OpenChat(chatId).ConfigureAwait(true);
    }

    // Private methods

    private async Task RevealCallScreenAfterPaint()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
        CallDebugLog?.LogInformation("CALL_TRACE: RevealCallScreen (after paint delay), Bridge={HasBridge}",
            Bridge is not null);
        Bridge?.RevealCallScreen();
    }

    private async Task JoinAcceptedCall(ChatId chatId, bool isOverLock)
    {
        // Over the lock screen the activity shown via SetShowWhenLocked counts as foreground, so the mic FGS
        // starts without unlocking; anywhere else the keyguard goes first, as the FGS can't start from the
        // background. The chat opens under the call; over the lock screen it waits for the user to unlock.
        var canStartAudio = isOverLock || Bridge is null || await Bridge.OnCallHandled(true).ConfigureAwait(true);
        if (!isOverLock)
            await OpenChat(chatId).ConfigureAwait(true);
        if (canStartAudio)
            await CallUI.StartCallAudio(chatId, CancellationToken.None).ConfigureAwait(true);
    }

    private async Task CancelCall(ChatId chatId)
    {
        try {
            await CallUI.CancelCall(chatId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "CancelCall failed for chat #{ChatId}", chatId);
        }
    }

    private void EndRing(ChatId chatId)
    {
        CallUI.DropRing(chatId);
        Bridge?.DismissCallNotification(chatId);
    }

    private Task OpenChat(ChatId chatId)
        => Hub.History.NavigateTo(Links.Chat(chatId));

    private void ShowToast(string text)
        => Hub.ToastUI.Show(text, "icon-phone", ToastDismissDelay.Short);

    private void ShowModal<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TModel>(
        TModel model, CancellationToken cancellationToken = default)
        where TModel : class
        // ModalUI.Show needs the Blazor dispatcher, and the worker loops run off it.
        => _ = Hub.Dispatcher.InvokeAsync(() => Hub.ModalUI.Show(model, cancellationToken));

    private MutableState<ChatId?> NewChatIdState(string name)
        => StateFactory.NewMutable((ChatId?)null, StateCategories.Get(GetType(), name));

    private static void ClearIf(MutableState<ChatId?> state, ChatId chatId)
    {
        if (state.Value == chatId)
            state.Value = null;
    }
}
```

Notes on what changed, for the reviewer: `_foregroundCallChatId`, `IsNarrowScreen`, `GetOverLockChatId`, `GetForegroundCallChatId`, `CloseCall`, `CancelDialing` and `IsOverLock` are gone; `EndRing` and `Accept` no longer clear flags (the release teardown does); `Decline` no longer clears the over-lock flag or moves the app behind the lock screen itself; `Expand` no longer re-shows the outgoing modal.

- [ ] **Step 2: Rewrite `CallScreensUI.StateSync.cs`**

Replace the whole file with:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public partial class CallScreensUI
{
    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(SyncRingtone),
            AsyncChain.From(SyncCallView),
        };
        var retryDelays = RetryDelaySeq.Exp(0.5, 10);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).Run(cancellationToken);
    }

    [ComputeMethod]
    protected virtual async Task<bool> MustRing(CancellationToken cancellationToken)
    {
        var call = await GetIncomingCall(cancellationToken).ConfigureAwait(false);
        if (call is null)
            return false;

        var mutedChatId = await _mutedRingChatId.Use(cancellationToken).ConfigureAwait(false);
        return mutedChatId != call.ChatId;
    }

    // Private methods

    private async Task SyncRingtone(CancellationToken cancellationToken)
    {
        var cMustRing = await Computed
            .Capture(() => MustRing(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var isRinging = false;
        try {
            await foreach (var c in cMustRing.Changes(cancellationToken).ConfigureAwait(false)) {
                if (c.Value == isRinging)
                    continue;

                isRinging = c.Value;
                if (isRinging)
                    StartRinging();
                else
                    StopRinging();
            }
        }
        finally {
            if (isRinging)
                StopRinging();
        }
    }

    private async Task SyncCallView(CancellationToken cancellationToken)
    {
        var cView = await Computed
            .Capture(() => GetCallView(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var last = CallView.None;
        await foreach (var c in cView.Changes(cancellationToken).ConfigureAwait(false)) {
            var view = c.Value;
            if (view == last)
                continue;

            OnCallViewChanged(last, view);
            last = view;
        }
    }

    private void OnCallViewChanged(CallView last, CallView view)
    {
        if (last.Call is { } lastCall && lastCall.ChatId != view.Call?.ChatId)
            OnCallReleased(lastCall, last);
        if (view is not { Kind: CallViewKind.Modal, Call: { } call })
            return;

        // The modal closes itself once the view moves on, so it opens only on the switch to it.
        var wasModal = last.Kind == CallViewKind.Modal && last.Call?.ChatId == call.ChatId;
        if (!wasModal)
            ShowCallModal(call);
    }

    private void OnCallReleased(ActiveCall call, CallView last)
    {
        // The one place a call's screens are torn down, however the call ended.
        var chatId = call.ChatId;
        CallDebugLog?.LogInformation("CALL_TRACE: slot released #{ChatId} from {Phase}", chatId, call.Phase);
        ClearCallFlags(chatId);
        if (last.IsOverLock)
            Bridge?.MoveBehindLockScreen();
        var mustOpenChat = last is { Kind: CallViewKind.FullScreen, IsOverLock: false };
        if (call.Phase == CallPhase.Active || mustOpenChat)
            _ = Hub.Dispatcher.InvokeAsync(() => CloseCall(call, mustOpenChat));
    }

    private async Task CloseCall(ActiveCall call, bool mustOpenChat)
    {
        if (call.Phase == CallPhase.Active)
            await CallUI.HangUp(call.ChatId).ConfigureAwait(true);
        if (mustOpenChat)
            await OpenChat(call.ChatId).ConfigureAwait(true);
    }

    private void ShowCallModal(ActiveCall call)
    {
        if (call.Origin == CallOrigin.Outgoing)
            ShowModal(new OutgoingCallModal.Model(call.ChatId));
        else if (call.PeerId is { } callerId)
            ShowModal(new IncomingCallModal.Model(callerId));
    }

    private void ClearCallFlags(ChatId chatId)
    {
        ClearIf(_collapsedChatId, chatId);
        ClearIf(_inChatChatId, chatId);
        ClearIf(_overLockRingChatId, chatId);
        ClearIf(_mutedRingChatId, chatId);
    }
}
```

- [ ] **Step 3: Make `FullScreenCallView` read the view**

In `src/dotnet/UI.Blazor.App/Components/FullScreenCallView/FullScreenCallView.razor`:

1. Root class — replace
   `<div class="full-screen-call-view @(m.Phase == CallPhase.InCall ? "in-call" : "ringing")">`
   with
   `<div class="full-screen-call-view @(m.Phase == CallPhase.Ringing ? "ringing" : "in-call")">`.
2. Top-left button — replace `<HeaderButton Class="btn-video-panel" Click="@OnGoToChatClick">` with `<HeaderButton Class="btn-video-panel" Click="@OnCollapseClick">`.
3. Delete the dead mic-off block (only a ring ever set `IsMuted`, and a ring has no name-row mic):
   ```razor
               @if (m is { Phase: CallPhase.InCall, IsMuted: true }) {
                   <i class="icon-mic-off c-name-mic"></i>
               }
   ```
4. Timer — replace `@if (m is { Phase: CallPhase.InCall, JoinedAt: { } joinedAt }) {` with `@if (m.JoinedAt is { } joinedAt) {`.
5. Record button — replace `IsDisabled="@m.IsDialing"` with `IsDisabled="@(m.Phase == CallPhase.Dialing)"`.
6. Delete the property line `private CallUI CallUI => Hub.CallUI;`.
7. Replace the whole `ComputeState` method (from `protected override async Task<ComputedModel> ComputeState(` through its closing `}` before `GetJoinedAt`) with:

```csharp
    protected override async Task<ComputedModel> ComputeState(CancellationToken cancellationToken) {
        var view = await CallScreensUI.GetCallView(cancellationToken).ConfigureAwait(false);
        if (view is not { Kind: CallViewKind.FullScreen, Call: { } call })
            return ComputedModel.None;

        var chatId = call.ChatId;
        var chat = await Hub.Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null) {
            DebugLog?.LogInformation(
                "CALL_TRACE: FullScreenCallView.ComputeState #{ChatId} → None (chat not loaded)", chatId);
            return ComputedModel.None;
        }

        DebugLog?.LogInformation(
            "CALL_TRACE: FullScreenCallView.ComputeState #{ChatId} → {Phase}", chatId, call.Phase);
        var m = new ComputedModel {
            Chat = chat, Phase = call.Phase, HasVideo = call.HasVideo, IsOverLock = view.IsOverLock,
        };
        if (call.Phase == CallPhase.Ringing) {
            var mutedChatId = await CallScreensUI.MutedRingChatId.Use(cancellationToken).ConfigureAwait(false);
            return m with { IsMuted = mutedChatId == chatId };
        }

        var recordingChatId = await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false);
        // Hidden until my own join is known: Accept commits the call before my audio starts.
        var joinedAt = call.Phase == CallPhase.Active
            ? await GetJoinedAt(chatId, cancellationToken).ConfigureAwait(false)
            : null;
        return m with { IsRecording = recordingChatId == chatId, JoinedAt = joinedAt };
    }
```

8. Replace
   ```csharp
       private Task OnGoToChatClick()
           => State.Value.Chat is { } chat ? CallScreensUI.LeaveCallScreen(chat.Id) : Task.CompletedTask;
   ```
   with
   ```csharp
       private Task OnCollapseClick() {
           var m = State.Value;
           if (m.Chat is not { } chat)
               return Task.CompletedTask;

           // A dialing call collapses into the island; an active one gives way to its chat.
           if (m.Phase == CallPhase.Active)
               return CallScreensUI.LeaveCallScreen(chat.Id);

           CallScreensUI.Collapse(chat.Id);
           return Task.CompletedTask;
       }
   ```
9. Replace `StatusText` with:
   ```csharp
       private string StatusText(ComputedModel m)
           => m.Phase switch {
               CallPhase.Ringing => L.Call_IsCallingYou,
               CallPhase.Dialing => L.Call_Dialing,
               _ => L.Call_InCall,
           };
   ```
10. In `// Nested types` delete `public enum CallPhase { Ringing, InCall }` (the Services `CallPhase` — `Ringing`, `Dialing`, `Active` — takes over) and delete from `ComputedModel` the `IsDialing` property with its comment. Keep `IsOverLock` and its comment.

- [ ] **Step 4: Make `CollapsedCallView` read the view**

In `src/dotnet/UI.Blazor.App/Components/CollapsedCallView/CollapsedCallView.razor`:

1. Delete the property lines `private CallUI CallUI => Hub.CallUI;` and `private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;`.
2. Replace the whole `ComputeState` method with:

```csharp
    protected override async Task<ComputedModel> ComputeState(CancellationToken cancellationToken) {
        var view = await CallScreensUI.GetCallView(cancellationToken).ConfigureAwait(false);
        if (view is not { Kind: CallViewKind.Collapsed, Call: { } call })
            return ComputedModel.None;

        var peerId = await CallScreensUI.GetCallPeerId(cancellationToken).ConfigureAwait(false);
        if (peerId is null)
            return ComputedModel.None;

        var isIncoming = call.Origin == CallOrigin.Incoming;
        return new ComputedModel { ChatId = call.ChatId, PeerId = peerId, IsIncoming = isIncoming };
    }
```

- [ ] **Step 5: Build and run the unit tests**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj -v q -nologo`
Expected: `0 Error(s)`.

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj -v q -nologo --filter "FullyQualifiedName~CallScreensUIDecisionsTest|FullyQualifiedName~CallUIDecisionsTest"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/CallScreensUI.cs src/dotnet/UI.Blazor.App/Services/CallScreensUI.StateSync.cs src/dotnet/UI.Blazor.App/Components/FullScreenCallView/FullScreenCallView.razor src/dotnet/UI.Blazor.App/Components/CollapsedCallView/CollapsedCallView.razor
git commit -m "refactor(call): let GetCallView alone decide the call's screen" -m "CallScreensUI.GetCallView feeds DecideView from the slot, the reactive screen width and the collapsed, in-chat and over-lock flags; the full-screen view and the island render only for their own kind, and the full-screen view takes its phase from the slot, so an accepted call no longer blinks out before its audio starts. The foreground flag goes: the width and the new in-chat flag replace it, and resizing mid-call now switches the screen." -m "One loop, SyncCallView, opens the modal and tears the screens down when the slot is released - moves the app behind the lock screen, opens the chat after a full-screen call, stops an active call's audio. Actions only change the slot or a flag; collapsing a narrow dialing call now gives the island instead of no UI." -m "Refs #690" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01LB31iLqy17KbPQAe7NaGSX"
```

---

### Task 3: One `CallModal` for both directions

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/CallModal/CallModal.razor`
- Move: `src/dotnet/UI.Blazor.App/Components/IncomingCallModal/IncomingCallModalHeader.razor` → `src/dotnet/UI.Blazor.App/Components/CallModal/CallModalHeader.razor`
- Create: `src/dotnet/UI.Blazor.App/Components/CallModal/call-modal.css`
- Delete: `Components/IncomingCallModal/IncomingCallModal.razor`, `Components/IncomingCallModal/incoming-call-modal.css`, `Components/OutgoingCallModal/OutgoingCallModal.razor`, `Components/OutgoingCallModal/outgoing-call-modal.css`
- Modify: `src/dotnet/UI.Blazor.App/Services/CallScreensUI.cs`, `CallScreensUI.StateSync.cs`
- Modify: `src/dotnet/UI.Blazor.App/styles.css`, `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs`
- Modify: `src/dotnet/UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs`, `src/dotnet/UI.Blazor/Module/BlazorUIAotSource.g.cs`
- Modify: `src/dotnet/UI.Blazor.App/Pages/Test/CallTestPage.razor`, `src/dotnet/UI.Blazor.App/Pages/Test/call-test-page.css`

**Interfaces:**
- Consumes: `CallScreensUI.GetCallView`, `GetCallPeerId`, `MutedRingChatId`, `Accept`, `Decline`, `DeclineAndOpenChat`, `HangUp`, `Collapse`, `ToggleMuteRing` (Task 2).
- Produces: `CallModal` with `public sealed record Model(ChatId ChatId)`; `CallModalHeader` (same parameters as `IncomingCallModalHeader`: `Author`, `IsOwn`, `OnCollapse`). Removed: `CallScreensUI.CollapsedChatId`, `ShowCallModal`.

- [ ] **Step 1: Move the header**

```bash
mkdir -p src/dotnet/UI.Blazor.App/Components/CallModal
git mv src/dotnet/UI.Blazor.App/Components/IncomingCallModal/IncomingCallModalHeader.razor src/dotnet/UI.Blazor.App/Components/CallModal/CallModalHeader.razor
```

The content stays as is — the component takes its name from the file.

- [ ] **Step 2: Create `CallModal.razor`**

```razor
@namespace ActualChat.UI.Blazor.App.Components
@implements IModalView<CallModal.Model>
@inherits ComputedStateComponent<AppUIHub, CallModal.ComputedModel?>
@{
    var m = State.Value;
    if (m is null)
        return;

    if (m.Author is not { } author) {
        // The call left the modal - it ended, collapsed into the island, got answered or moved to the
        // full-screen view. This is a resolved close, so it must NOT re-collapse from Dispose.
        if (!_isAutoClosed) {
            _isAutoClosed = true;
            _handled = true;
            Modal.Close();
        }
        return;
    }
}

<DialogFrame
    Title="@Title(m)"
    Class="author-modal modal-sm call-modal"
    HasCloseButton="false">
    <Body>
    <CallModalHeader
        Author="author"
        IsOwn="@(!m.IsIncoming)"
        OnCollapse="@OnCollapseClick"/>
    </Body>
    <Buttons>
        @if (m.IsIncoming) {
            <ButtonRound Class="btn-call c-decline" Tooltip="@L.Common_Decline" Click="@OnDeclineClick">
                <i class="icon-close text-2xl"></i>
            </ButtonRound>
            <ButtonRound Class="btn-call c-mute" Click="@OnMuteClick">
                <i class="@(m.IsMuted ? "icon-bell" : "icon-bell-off") text-2xl"></i>
            </ButtonRound>
            <ButtonRound Class="btn-call c-message" Tooltip="@L.Call_Message" Click="@OnMessageClick">
                <i class="icon-message-ellipse text-2xl"></i>
            </ButtonRound>
            <ButtonRound Class="btn-call c-accept" Tooltip="@L.Common_Accept" Click="@OnAcceptClick">
                <i class="icon-phone-take text-2xl"></i>
            </ButtonRound>
        } else {
            <ButtonRound Class="btn-call c-hang-up" Tooltip="@L.Call_HangUp" Click="@OnHangUpClick">
                <i class="icon-phone-off text-2xl"></i>
            </ButtonRound>
        }
    </Buttons>
</DialogFrame>

@code {
    private bool _isAutoClosed;
    // True once the close was resolved by an explicit action or the view moving on. A close with this
    // still false means the user dismissed the modal (tap outside / Esc), which collapses the call.
    private bool _handled;

    private IAuthors Authors => Hub.Authors;
    private CallScreensUI CallScreensUI => Hub.CallScreensUI;

    private ChatId ChatId => ModalModel.ChatId;  // Shortcut

    [CascadingParameter] public Modal Modal { get; set; } = null!;
    [Parameter] public Model ModalModel { get; set; } = null!;

    protected override async Task<ComputedModel?> ComputeState(CancellationToken cancellationToken) {
        var chatId = ChatId;
        var view = await CallScreensUI.GetCallView(cancellationToken).ConfigureAwait(false);
        if (view is not { Kind: CallViewKind.Modal, Call: { } call } || call.ChatId != chatId)
            return ComputedModel.None;

        var peerId = await CallScreensUI.GetCallPeerId(cancellationToken).ConfigureAwait(false);
        if (peerId is null)
            return ComputedModel.None;

        var author = await Authors.Get(Session, chatId, peerId, cancellationToken).ConfigureAwait(false);
        if (author is null)
            return ComputedModel.None;

        var mutedChatId = await CallScreensUI.MutedRingChatId.Use(cancellationToken).ConfigureAwait(false);
        return new() {
            Author = author,
            IsIncoming = call.Origin == CallOrigin.Incoming,
            HasVideo = call.HasVideo,
            IsMuted = mutedChatId == chatId,
        };
    }

    public override async ValueTask DisposeAsync() {
        // A tap outside (or Esc) closes the modal without touching the call; unless an explicit action
        // already resolved it, the call collapses into the island rather than losing its UI.
        if (!_handled)
            CallScreensUI.Collapse(ChatId);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private Task OnAcceptClick() {
        _handled = true;
        var chatId = ChatId;
        Modal.Close();
        return CallScreensUI.Accept(chatId);
    }

    private Task OnDeclineClick() {
        _handled = true;
        var chatId = ChatId;
        Modal.Close();
        return CallScreensUI.Decline(chatId);
    }

    private Task OnMessageClick() {
        _handled = true;
        var chatId = ChatId;
        Modal.Close();
        return CallScreensUI.DeclineAndOpenChat(chatId);
    }

    private Task OnHangUpClick() {
        _handled = true;
        var chatId = ChatId;
        Modal.Close();
        return CallScreensUI.HangUp(chatId);
    }

    // The modal closes on its own: the view turns Collapsed.
    private void OnCollapseClick() {
        _handled = true;
        CallScreensUI.Collapse(ChatId);
    }

    private void OnMuteClick()
        => CallScreensUI.ToggleMuteRing(ChatId);

    private string Title(ComputedModel m)
        => m switch {
            { IsIncoming: false } => L.Call_Outgoing,
            { HasVideo: true } => L.Call_IncomingVideo,
            _ => L.Call_Incoming,
        };

    // Nested types

    public sealed record ComputedModel {
        public static readonly ComputedModel None = new();

        public Author? Author { get; init; }
        public bool IsIncoming { get; init; }
        public bool HasVideo { get; init; }
        public bool IsMuted { get; init; }
    }

    public sealed record Model(ChatId ChatId);
}
```

If the style hook moves the one-line comment above `OnCollapseClick` into its body, follow it.

- [ ] **Step 3: Create `call-modal.css`**

```css
/* ── Modal action buttons: a centered row of round buttons — Decline · Mute · Message · Accept, or Hang up ── */
/* The second selector outweighs `body.narrow .dialog-buttons`, which would otherwise stack these
   into a column on mobile. */
.call-modal .dialog-buttons,
body.narrow .call-modal .dialog-buttons {
    @apply flex-x items-center justify-center gap-4;
}
.call-modal .btn-call {
    @apply flex flex-none items-center justify-center;
    @apply w-[3.25rem] h-[3.25rem];
    @apply rounded-full;
}
/* Solid gradients stay as plain CSS (same as .btn-video-panel): the shorthand also clears the
   default .btn-round background so the fill is exact. */
.call-modal .btn-call.c-decline,
.call-modal .btn-call.c-hang-up {
    background: linear-gradient(180deg, var(--magenta-60) 0%, var(--magenta-50) 100%);
    @apply text-white;
}
.call-modal .btn-call.c-accept {
    background: linear-gradient(180deg, var(--emerald-40) 0%, var(--emerald-30) 100%);
    @apply text-white;
}
.call-modal .btn-call.c-mute,
.call-modal .btn-call.c-message {
    /* Same neutral glass as the video-panel controls (.btn-video-panel). */
    background: linear-gradient(180deg, var(--black-20) -20.83%, var(--black-40) 100%);
    @apply text-white;
    text-shadow: 0 1px 3px rgb(0 0 0 / 60%);
}
```

- [ ] **Step 4: Delete the old modals**

```bash
git rm src/dotnet/UI.Blazor.App/Components/IncomingCallModal/IncomingCallModal.razor src/dotnet/UI.Blazor.App/Components/IncomingCallModal/incoming-call-modal.css src/dotnet/UI.Blazor.App/Components/OutgoingCallModal/OutgoingCallModal.razor src/dotnet/UI.Blazor.App/Components/OutgoingCallModal/outgoing-call-modal.css
```

- [ ] **Step 5: Point `CallScreensUI` at `CallModal`**

In `CallScreensUI.cs` delete the line `public IState<ChatId?> CollapsedChatId => _collapsedChatId;`.

In `CallScreensUI.StateSync.cs` delete the whole `ShowCallModal` method and, in `OnCallViewChanged`, replace

```csharp
        if (!wasModal)
            ShowCallModal(call);
```

with

```csharp
        if (!wasModal)
            ShowModal(new CallModal.Model(call.ChatId));
```

- [ ] **Step 6: Register the modal and its CSS**

In `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs` replace

```csharp
            .Add<IncomingCallModal.Model, IncomingCallModal>()
            .Add<OutgoingCallModal.Model, OutgoingCallModal>()
```

with

```csharp
            .Add<CallModal.Model, CallModal>()
```

In `src/dotnet/UI.Blazor.App/styles.css` replace the line `@import './Components/IncomingCallModal/incoming-call-modal.css';` with `@import './Components/CallModal/call-modal.css';` and delete the line `@import './Components/OutgoingCallModal/outgoing-call-modal.css';`.

- [ ] **Step 7: Update the AOT keep lists**

In `src/dotnet/UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs`:

- Delete the three lines
  `CodeKeeper.Keep<global::ActualChat.UI.Blazor.App.Components.IncomingCallModal>();`,
  `CodeKeeper.Keep<global::ActualChat.UI.Blazor.App.Components.IncomingCallModalHeader>();`,
  `CodeKeeper.Keep<global::ActualChat.UI.Blazor.App.Components.OutgoingCallModal>();`
  and insert right after `CodeKeeper.Keep<global::ActualChat.UI.Blazor.App.Components.CallMessageView>();`:
  ```csharp
          CodeKeeper.Keep<global::ActualChat.UI.Blazor.App.Components.CallModal>();
          CodeKeeper.Keep<global::ActualChat.UI.Blazor.App.Components.CallModalHeader>();
  ```
- Delete the three lines
  `(typeof(global::ActualChat.UI.Blazor.App.Components.IncomingCallModal), AotTypeKind.Component),`,
  `(typeof(global::ActualChat.UI.Blazor.App.Components.IncomingCallModalHeader), AotTypeKind.Component),`,
  `(typeof(global::ActualChat.UI.Blazor.App.Components.OutgoingCallModal), AotTypeKind.Component),`
  and insert right after `(typeof(global::ActualChat.UI.Blazor.App.Components.CallMessageView), AotTypeKind.Component),`:
  ```csharp
              (typeof(global::ActualChat.UI.Blazor.App.Components.CallModal), AotTypeKind.Component),
              (typeof(global::ActualChat.UI.Blazor.App.Components.CallModalHeader), AotTypeKind.Component),
  ```

In `src/dotnet/UI.Blazor/Module/BlazorUIAotSource.g.cs`:

- Delete the two `CodeKeeper.Keep("…CreateDefaultStateOptionsFactory`1[[ActualChat.UI.Blazor.App.Components.IncomingCallModal+ComputedModel, …` and `…OutgoingCallModal+ComputedModel, …` lines, and insert right after the `…Components.Banners+ComputedModel, …` line:
  ```csharp
          CodeKeeper.Keep("ActualLab.Fusion.Blazor.ComputedStateComponent+CreateDefaultStateOptionsFactory`1[[ActualChat.UI.Blazor.App.Components.CallModal+ComputedModel, ActualChat.UI.Blazor.App]], ActualLab.Fusion.Blazor");
  ```

Indentation matches the neighboring lines exactly (8 spaces in the `Keep` block, 12 in the `typeof` list).

- [ ] **Step 8: Update the call test page mock**

In `src/dotnet/UI.Blazor.App/Pages/Test/CallTestPage.razor` and `src/dotnet/UI.Blazor.App/Pages/Test/call-test-page.css` replace every `incoming-call-modal` with `call-modal` (this also turns `incoming-call-modal.css` into `call-modal.css` in the note). In `CallTestPage.razor` replace the four `class="c-call-btn ` occurrences in the modal mock with `class="btn-call `.

- [ ] **Step 9: Build and verify**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj -v q -nologo`
Expected: `0 Error(s)`.

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj -v q -nologo --filter "FullyQualifiedName~CallScreensUIDecisionsTest|FullyQualifiedName~CallUIDecisionsTest"`
Expected: PASS.

Run (repo root): `npm run build:Verify`
Expected: exit code 0 — `tsc`, `eslint` and the debug bundle (which compiles `styles.css`) all pass.

Run: `git grep -n "IncomingCallModal\|OutgoingCallModal\|incoming-call-modal\|outgoing-call-modal" -- src`
Expected: no output.

- [ ] **Step 10: Commit**

```bash
git add -A src/dotnet/UI.Blazor.App/Components/CallModal src/dotnet/UI.Blazor.App/Services/CallScreensUI.cs src/dotnet/UI.Blazor.App/Services/CallScreensUI.StateSync.cs src/dotnet/UI.Blazor.App/styles.css src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs src/dotnet/UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs src/dotnet/UI.Blazor/Module/BlazorUIAotSource.g.cs src/dotnet/UI.Blazor.App/Pages/Test/CallTestPage.razor src/dotnet/UI.Blazor.App/Pages/Test/call-test-page.css
git commit -m "refactor(call): one CallModal for a ring and for dialing" -m "The view kind is the form, not the direction, so IncomingCallModal and OutgoingCallModal become CallModal: Decline, Mute, Message and Accept on a ring, Hang up while dialing. It shows while GetCallView says Modal for its chat and closes itself otherwise - collapsed, answered, ended or moved to the full-screen view by a narrowed window - and hang-up goes through CallScreensUI.HangUp." -m "Refs #690" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01LB31iLqy17KbPQAe7NaGSX"
```

---

### Task 4: Describe the view in the call-flow doc

**Files:**
- Modify: `docs/calls/incoming-call-flow.md`

- [ ] **Step 1: Replace the slot section's last paragraph**

Replace

```markdown
Everything that shows a call reads the slot: the modal, the island, the over-lock and narrow
full-screen views, the ringtone, the ringback. `GetIncomingCall` is the slot filtered to
`Incoming/Ringing`.
```

with

```markdown
Everything that shows a call reads the slot. The modal, the island and the full-screen view
read it through `CallScreensUI.GetCallView`; the ringtone through `GetIncomingCall`, the slot
filtered to `Incoming/Ringing`; the ringback through `CallUI`.
```

- [ ] **Step 2: Replace the loop list of "Presenting the ring"**

Replace exactly this block (the `ConfirmRing`, **Ringtone.** and **Showing over the lock screen.** paragraphs after it stay):

```markdown
`CallScreensUI` is a UI worker; it runs these reactive loops:

- **`SyncRingtone`** plays the ringtone while the slot holds an Incoming/Ringing call the user
  hasn't muted; muting or collapsing the ring only changes that state.
- **`SyncIncomingCallModal`** shows `IncomingCallModal`, except when the ring is shown over
  the lock screen or collapsed into the island.
- **SyncCallScreens** follows the slot: an outgoing call that starts dialing gets its modal
  or full-screen view, and a released call gets its screens closed and its audio stopped.
```

with:

```markdown
`CallScreensUI.GetCallView` decides the one screen the call in the slot gets. It is a function
of the call's origin and phase, the screen width, and three chat-scoped flags; the rule itself is
the pure `DecideView`, and a flag counts only while the slot holds its chat.

| Flag | Set by | Counts for |
|---|---|---|
| over-lock | `OnRing(showOverLockScreen: true)` | an incoming call, any phase |
| collapsed | the modal's collapse button, dismissing the modal, collapsing the full-screen view while dialing | ringing and dialing |
| in chat | "go to chat" in the full-screen view of an active call | active |

The first matching row wins:

| Call | Wide | Narrow |
|---|---|---|
| Incoming, over the lock screen (any phase) | full-screen view | full-screen view |
| Ringing, collapsed | island | island |
| Ringing | modal | modal |
| Dialing, not yet confirmed by the server | nothing | nothing |
| Dialing, collapsed | island | island |
| Dialing | modal | full-screen view |
| Active, in chat | nothing | nothing |
| Active | nothing — the call is in the chat | full-screen view |

The width is reactive: narrowing the window mid-call brings up the full-screen view, widening it
swaps the dialing full-screen view for the modal.

| Surface | What it shows |
|---|---|
| `CallModal` | Decline, Mute, Message and Accept for a ring; Hang up while dialing. |
| `FullScreenCallView` | The ring over the keyguard, and dialing or the call on a narrow screen. |
| `CollapsedCallView` | The draggable island. Collapsing a ring also mutes its ringtone. |

`CallScreensUI` is a UI worker; it runs two reactive loops:

- **`SyncRingtone`** plays the ringtone while the slot holds an Incoming/Ringing call the user
  hasn't muted.
- **`SyncCallView`** follows `GetCallView`. It opens `CallModal` when the view switches to it;
  the modal closes itself once the view moves on. When the slot is released it tears the
  screens down, and it is the only place that does: it clears the chat's flags, moves the app
  back behind the lock screen if the call was shown over it, opens the chat if the call had the
  full-screen view, and stops an active call's audio. Decline, Hang up and the other actions
  only change the slot.
```

Then delete the old surface table (between the **Ringtone.** and **Showing over the lock screen.** paragraphs) together with the blank line after it:

```markdown
| Surface | When it shows |
|---|---|
| `IncomingCallModal` | The default foreground ring. |
| `FullScreenCallView` | An Android full-screen intent over the keyguard. On narrow screens it is also the full-screen call view: in a call or dialing out. |
| `CollapsedCallView` | The draggable island, after the user collapses the modal; my own dialing call collapses into the same one. Collapsing a ring also mutes the ringtone. |
```

- [ ] **Step 3: Update Accept step 4**

Replace

```markdown
   foreground, which is what the microphone foreground service needs. A wide screen then
   opens the chat; a narrow one shows the full-screen call view.
```

with

```markdown
   foreground, which is what the microphone foreground service needs. The chat then opens,
   unless the call was accepted over the lock screen; on a narrow screen the full-screen call
   view covers it.
```

- [ ] **Step 4: Check nothing stale is left**

Run: `git grep -n "IncomingCallModal\|OutgoingCallModal\|SyncIncomingCallModal\|SyncCallScreens\|foreground call" -- docs/calls`
Expected: no output.

- [ ] **Step 5: Commit**

```bash
git add docs/calls/incoming-call-flow.md
git commit -m "docs(calls): describe the call view as built" -m "Refs #690" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01LB31iLqy17KbPQAe7NaGSX"
```

---

### Task 5: Live check on the call test rig

No code; fix anything found as a follow-up commit on the task it belongs to.

- [ ] **Step 1: Run the server and two browsers**

Use `/server-loop` to rebuild and run the server in this worktree, then `/debug-ui` to sign in two users on `mcp__chrome1__*` and `mcp__chrome2__*`. Only the admin `er5ir9` can place calls; it's the caller in every scenario below.

- [ ] **Step 2: Walk the scenarios, wide (≥ 1280 px) and narrow (`resize_page` to 400×800)**

| # | Scenario | Expected |
|---|---|---|
| 1 | Caller dials | Caller: modal (wide) / full-screen "Dialing…" (narrow). Callee: modal with ringtone on both widths |
| 2 | Callee collapses the modal (X), then taps the island | Island, ringtone muted → modal again, still muted |
| 3 | Callee dismisses the modal by tapping outside | Island |
| 4 | Caller collapses while dialing (modal X wide, top-left button narrow) | Island on both widths |
| 5 | Callee accepts | Callee: chat opens (wide) / full-screen with timer once joined, no blank frame (narrow). Caller: modal closes (wide) / full-screen switches to the call (narrow) |
| 6 | Narrow callee in a call: top-left "go to chat" | Full-screen view goes away, the chat stays, audio continues |
| 7 | Active call, window resized wide → narrow → wide | Nothing → full-screen view → nothing; after scenario 6 it stays hidden on narrow |
| 8 | Dialing, window resized narrow → wide | Full-screen view → modal |
| 9 | Hang up from the modal, the island, the full-screen view | Screens close; after a narrow full-screen call the chat is open; audio stops |
| 10 | Callee declines; caller cancels while the callee's modal is up | Callee's screen and ringtone go away |

- [ ] **Step 3: Report**

List each scenario with pass/fail; for a failure, the observed screen and the console `CALL_TRACE` lines. The over-lock path needs an Android device — report it as not checked live unless the user asks for it.
