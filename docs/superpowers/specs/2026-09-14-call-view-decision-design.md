# One place decides the call's screen — design

Issue #690, branch `fix/update-ui-287`. Builds on the call slot
([2026-09-14-call-slot-design.md](./2026-09-14-call-slot-design.md)).

## Motivation

`CallUI` holds one call, but which screen shows it is decided in eight places:

| Where | What it decides |
|---|---|
| `GetModalCall` → `SyncIncomingCallModal` | the incoming modal: ringing, not over the lock screen, not collapsed |
| `ShowOutgoingCall` | dialing out: full-screen view when narrow, `OutgoingCallModal` when wide |
| `Expand` | re-shows the outgoing modal |
| `JoinAcceptedCall` | an accepted ring: full-screen view when narrow, open the chat when wide |
| `OnScreenInputChanged` | an answered outgoing call gets the full-screen view when narrow |
| `FullScreenCallView.ComputeState` | derives its phase from the over-lock and foreground flags, then again from the session |
| `CollapsedCallView.ComputeState` | collapsed and ringing, or collapsed and dialing out |
| both modals | close themselves once they see the collapsed flag |

The screen width is read once, when an event fires (`IsNarrowScreen`), so rotating
or resizing mid-call doesn't switch the screen. Two cases have no screen at all:
collapsing a narrow dialing call leaves it with no UI, and between "accepted" and
"my audio started" `FullScreenCallView` returns `None` for a beat, because
`AmIInLiveConversation` is still `false` and `GetCallStatus` is caller-only.

## The rule

The screen is a function of the call in the slot, the screen width and three flags.
One pure function computes it; everything that shows a call reads its result.

### Types

In `UI.Blazor.App/Services`, next to `ActiveCall`:

```csharp
public enum CallViewKind { None, Modal, FullScreen, Collapsed }

// Call is set even when Kind is None: that's how the view loop sees the slot released.
public sealed record CallView(ActiveCall? Call, CallViewKind Kind, bool IsOverLock);

internal readonly record struct CallScreenFlags(
    ChatId? CollapsedChatId, ChatId? InChatChatId, ChatId? OverLockChatId);
```

`Kind` is the form the call is shown in, not which call it is; the content of each
form follows `call.Origin` and `call.Phase`.

### `CallScreensUI.DecideView`

In a new `CallScreensUI.Decisions.cs`, modeled on `CallUI.Decisions.cs`:

```csharp
internal static CallView DecideView(
    ActiveCall? call, bool isDialingConfirmed, bool isNarrow, CallScreenFlags flags)
```

A flag counts only when it names the slot's chat. The first matching rule wins:

| # | Condition | Result |
|---|---|---|
| 1 | the slot is free | `None` |
| 2 | over-lock flag, incoming call (any phase) | `FullScreen`, `IsOverLock` |
| 3 | ringing, collapsed | `Collapsed` |
| 4 | ringing | `Modal` |
| 5 | dialing, not yet confirmed by the server | `None` |
| 6 | dialing, collapsed | `Collapsed` |
| 7 | dialing, narrow | `FullScreen` |
| 8 | dialing | `Modal` |
| 9 | active, in chat | `None` |
| 10 | active, narrow | `FullScreen` |
| 11 | active | `None` |

Rule 5 keeps today's behavior: the outgoing screens name the invitee from the
session, so they wait for the server's dialing (`GetDialingOutChatId`), and a refused
call never flashes a screen.

As a table by width:

| Call | Wide | Narrow |
|---|---|---|
| Over the lock screen (any phase) | full-screen view | full-screen view |
| Incoming, ringing | modal | modal |
| Outgoing, dialing | modal | full-screen view |
| Active | nothing — the call is in the chat | full-screen view |
| Collapsed: ringing or dialing | island | island |
| Active, in chat | nothing | nothing |

### Flags

`CallScreensUI` keeps three chat-scoped flags; `DecideView` ignores a flag for any
other chat.

| Flag | Set by | Counts for |
|---|---|---|
| `_collapsedChatId` | `Collapse` — the modal's collapse button, dismissing the modal, the full-screen view's collapse button while dialing | ringing and dialing; an answered collapsed call ignores it and gets the full-screen view when narrow, as today |
| `_inChatChatId` (new) | "go to chat" in the full-screen view of an active call | active |
| `_overLockRingChatId` | `OnRing(showOverLockScreen: true)` | incoming, any phase |

`_foregroundCallChatId` is removed: width and `_inChatChatId` replace it.
`_mutedRingChatId` is unchanged and isn't an input of the view.

### `GetCallView`

A compute method on `CallScreensUI` reads `CallUI.GetActiveCall`,
`CallUI.GetDialingOutChatId` (for `isDialingConfirmed`),
`BrowserInfo.ScreenSize.Use()` (for `isNarrow`, through `IsNarrow()`) and the three
flags, and calls `DecideView`. Width is reactive: narrowing the window mid-call shows
the full-screen view, widening it swaps the dialing full-screen view for the modal.

## Who reads the view

### Components

Each component reads `GetCallView()` and shows only for its own `Kind`.

- **`CallModal`** replaces `IncomingCallModal` and `OutgoingCallModal`. Its model is
  `CallModal.Model(ChatId)`. An incoming ring gets the "Incoming (video) call" title,
  Decline, Mute, Message and Accept; a dialing call gets the "Outgoing call" title
  and Hang up. It closes itself once the view is no longer `Modal` for its chat; that
  close is resolved and doesn't collapse. Dismissing it (tap outside, Esc) still
  collapses the call into the island. Hang up goes through `CallScreensUI.HangUp`,
  not straight to `CallUI.CancelCall`. `IncomingCallModalHeader` becomes
  `CallModalHeader`; `incoming-call-modal.css` and `outgoing-call-modal.css` merge
  into `call-modal.css`, and `CallTestPage` follows the class rename.
- **`FullScreenCallView`** takes its phase from `call.Phase`: `Ringing` shows the ring
  buttons, `Dialing` the call toolbar with a "Dialing..." status, `Active` the toolbar
  with the timer from my own join (hidden until it's known). `AmIInLiveConversation`,
  `GetCallStatus` and the nested `enum CallPhase { Ringing, InCall }` go. The top-left
  button is `Collapse` while dialing and "go to chat" while active.
- **`CollapsedCallView`** keeps its layout; it reads the view instead of
  `CollapsedChatId`, the ring and the dialing chat.

The peer shown by the modal and the island comes from a new
`[ComputeMethod] GetCallPeerId()` on `CallScreensUI`: `call.PeerId` for an incoming
call, the first invitee from the live session for an outgoing one.

### Loops

`CallScreensUI.OnRun` runs two loops:

- **`SyncRingtone`**, unchanged: rings while the slot holds an unmuted incoming ring.
- **`SyncCallView`** replaces `SyncIncomingCallModal` and `SyncCallScreens`. It
  follows `GetCallView` changes and remembers the last view:
  - **Opening the modal.** The view became `Modal` and the last view wasn't `Modal`
    for that chat: `ShowModal(new CallModal.Model(chatId))`. This covers a new call,
    `Expand` and widening the window alike.
  - **Releasing the slot.** The last view held a call that's gone. This is the only
    place screens are torn down:
    - clear the chat's flags (collapsed, in chat, over-lock, muted);
    - the last view was over the lock screen → `Bridge.MoveBehindLockScreen()`;
    - the last view was `FullScreen` and not over the lock screen → open the chat;
    - the call was `Active` → `CallUI.HangUp(chatId)`, which stops the audio.

  UI work runs on the Blazor dispatcher, as today.

### Actions

Actions change the slot or a flag; they don't tear screens down themselves.

- **`Accept`.** As today, minus the width branch: an accepted ring not over the lock
  screen opens the chat whatever the width — on a narrow screen the full-screen view
  covers it. The foreground flag goes.
- **`Decline`.** Ends the ring; no longer clears the over-lock flag first. Otherwise
  the loop could see "ringing, no longer over the lock screen" and flash the modal.
  The release teardown moves the app behind the lock screen. Declining a ring not
  over the lock screen still calls `Bridge.OnCallHandled(false)`.
- **`HangUp`.** `CallUI.CancelCall` while dialing out, `CallUI.HangUp` otherwise.
  `CloseCall` and `CancelDialing` go.
- **`LeaveCallScreen`** ("go to chat", active call only). Over the lock screen it
  unlocks first and gives up if the PIN is cancelled. Then it sets `_inChatChatId`,
  only then clears the over-lock flag — so no in-between view flashes — and opens the
  chat.
- **`Collapse`.** Unchanged: a collapsed ring also mutes its ringtone.
- **`Expand`.** Only clears the collapsed flag; the loop re-opens the modal.

Removed: `GetModalCall`, `GetScreenInput`, `ScreenInput`, `OnScreenInputChanged`,
`ShowOutgoingCall`, `CloseCall`, `CancelDialing`, `IsNarrowScreen`,
`GetForegroundCallChatId`, and `GetOverLockChatId` as a public method (its check moves
into `GetCallView`). `GetIncomingCall` stays for the ringtone.

### Known edge

Flags are cleared by the loop, a tick after the slot is released. Redialing the same
chat within that tick could flash a stale island. Today `ShowOutgoingCall` closes the
same window; it isn't reachable by hand, so it stays open.

## Out of scope

- A collapsed active call has no island, so there's no way back to the full-screen
  view once in the chat. `_inChatChatId` makes a later "expand" button in the chat a
  one-liner.
- The incoming ring on a narrow screen stays a modal; the full-screen ring stays
  over-lock only.

## Reuse

Existing abstractions this builds on:

- `ActiveCall`, `CallOrigin`, `CallPhase`, `CallUI.GetActiveCall`,
  `CallUI.GetDialingOutChatId`, `CallUI.HangUp`, `CallUI.CancelCall` — the slot.
- `BrowserInfo.ScreenSize` (`IState<ScreenSize>`) and `ScreenSizeExt.IsNarrow()` — width.
- `ModalUI.Show`, `ComputedStateComponent`, `Computed.Capture(...).Changes()` — the
  modal and the loops, as today.
- The `Decide*` pattern of `CallUI.Decisions.cs` and its unit test.

New components and where they live: `CallView`, `CallViewKind`, `DecideView` and
`CallModal` are specific to the call screens, so they stay in `UI.Blazor.App`; nothing
in them is useful outside calls.

## Testing

- **Unit.** `tests/Chat.UI.Blazor.UnitTests/CallScreensUIDecisionsTest.cs`, a
  `[Theory]` over the eleven rules, plus: a flag for another chat is ignored;
  collapsed doesn't affect an active call and in-chat doesn't affect dialing;
  over-lock overrides collapsed and in-chat; over-lock is ignored for an outgoing call.
- **Build.** `ActualChat.CI.slnf`, the unit tests, `npm run build:Verify` (CSS and
  classes change).
- **Live**, through `/debug-ui` on the call test rig, two users, wide and narrow:
  incoming modal → collapse → expand → accept; dialing → collapse → answered; active
  full-screen view → go to chat → resize both ways; hang up from every surface.
- The over-lock path needs an Android device; it's covered by the unit test and
  checked on a device on request.

## Docs

`docs/calls/incoming-call-flow.md`, "Presenting the ring": three loops become two,
the "Surface / When it shows" table becomes the rule table above, and teardown is
described as happening only on release. `IncomingCallModal` / `OutgoingCallModal`
become `CallModal` throughout.
