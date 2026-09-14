# One call at a time: the client call slot — design

Issue #690, branch `fix/update-ui-287`. Supersedes the single-ring latch in
`IncomingCallUI` (`0b6fa70def`).

## Motivation

The client has no single notion of "the call I'm in". Incoming and outgoing calls
are tracked by different services with different state:

- `IncomingCallUI` keeps the ring candidates, the latched incoming ring,
  `_inCallChatId`, and the screen flags (over-lock, foreground, collapsed, muted).
- `LiveSessionUI` watches each outgoing call in its own loop (`_callWatches`,
  `WatchOutgoingCall`), plays the ringback, and joins the answered call
  (`JoinAnsweredCall`).

Two problems follow.

1. **A ring can slip past the busy check.** The latch from `0b6fa70def` searches
   the candidates only while nothing is latched, so a ring that arrives while the
   search is still checking a candidate never gets `Busy`.
2. **Nothing enforces "one call at a time".** A ring during my outgoing call rings
   normally; a ring during an accepted call rings over the conversation; I can place
   a second outgoing call while the first is still dialing in another chat.

## Rules

Calls exist only in peer chats for now. The rules this design enforces:

- **I can't take a new incoming call while I have an active call**, incoming or
  outgoing. Every such ring gets `RingAck.Busy` and doesn't ring on this device.
  Putting the current call on hold to answer another is a later feature.
- **I can't start a new outgoing call while one is already started.**
- **Ambient live sessions don't count.** Recording or listening in a chat without a
  call doesn't occupy the slot.

## Model

`CallUI`, a new service in `UI.Blazor.App/Services`, owns one call slot.

```csharp
public enum CallOrigin { Incoming, Outgoing }
public enum CallPhase { Ringing, Dialing, Active }

public sealed record ActiveCall(
    ChatId ChatId,
    CallOrigin Origin,
    CallPhase Phase,
    AuthorId PeerId,   // the caller for an incoming call; default for an outgoing one
    bool HasVideo);
```

- **`_callChatId: MutableState<ChatId?>`** — the chat holding the slot. While it is
  set, every other call is refused.
- **`_activeCall: MutableState<ActiveCall?>`** — the confirmed call.
  - Invariant: `_activeCall != null` ⇒ `_activeCall.ChatId == _callChatId`.
  - "`_callChatId` set, `_activeCall` empty" is the short window between claiming
    the slot and confirming the call.
- **The busy set** — chats already answered with `Busy`, so a repeated `OnRing` for
  the same chat doesn't repeat the ack.
- **`_ringingChatIds`** — the ring candidates, moved here from `IncomingCallUI`.

Releasing the slot means: reset `_callChatId`, `_activeCall` and the busy set, and
remove the chat from the candidates.

### Transitions

| Origin | Slot claimed by | Phases | Slot released when |
|---|---|---|---|
| Incoming | the search, for a ringing candidate while the slot is free; or `Accept` itself | `Ringing` → `Active` on `Accept` | the ring ends unaccepted; or the conversation ends |
| Outgoing | `StartCall`, before the RPC; a failed RPC releases at once | `Dialing` → `Active` when answered | no answer, declined, canceled, the session vanished; or the conversation ends |

## Two loops

### Holding

Runs while `_callChatId` is set. It reactively watches three facts about that chat:

- **does it ring me** — `GetRingingCall`: my invite is `Ringing`;
- **my outgoing status** — `LiveSessionUI.GetCallStatus`;
- **am I in the conversation** — the session is a call (`Kind == Call`) and I listen
  or talk in it (`AmIInLiveConversation`).

| `_activeCall` | What the loop does |
|---|---|
| empty (the search just claimed the slot) | The chat rings → write `Incoming/Ringing` and send `ConfirmRing(Ringing)` once. It doesn't → release |
| `Incoming/Ringing` | The ring ended (canceled, timed out, answered on another device) → release |
| `Outgoing/Dialing` | Answered → join the conversation (today's `JoinAnsweredCall`) and move to `Active`. No answer, declined, canceled, the session vanished → release |
| `*/Active` | The session stopped being a call → release. I was in the conversation and left it → release |

"Was in the conversation and left it" is the `wasActive` check `ResetActiveCall` does
today. It keeps the slot through the gap between accepting and the audio starting.
The move `Ringing → Active` is made by `Accept` itself, the way `_inCallChatId` is
set today; the loop only notices the end.

### Searching

A `[ComputeMethod]` over the candidates yields the ringing calls among
`_ringingChatIds`. The loop wakes when that result or the slot changes. For each
ringing candidate, newest first:

| Slot | Outcome |
|---|---|
| free | Claim it for this chat — one claim per pass |
| `_callChatId` set, `_activeCall` empty | Nothing; the next pass resolves it |
| held by this chat | Nothing |
| held by another chat | If the chat isn't in the busy set: `ConfirmRing(Busy)`, add it to the set, dismiss its Android system notification |

- Candidates that aren't ringing on a pass are dropped from the list and from the
  busy set. Without this the list grows for the scope's lifetime.
- Rings that arrive during my outgoing call get `Busy` too: the slot is held.
- The notification dismissal is the cold-start backstop — see "Android native".

## Actions and guards

| Action | Slot free | Slot held by this chat | Slot held by another chat |
|---|---|---|---|
| **`StartCall`** (header button, "Tap to call back") | Microphone check → claim as `Outgoing/Dialing` → RPC. A failed RPC releases and shows a toast | No button, as today (`IsBusy`) | The button is disabled with a "You're already in a call" tooltip; `StartCall` itself refuses too |
| **`Accept`** | Verify against the session, then claim straight into `Incoming/Active` — Answer on a notification before the search claimed it | Verify against the session → `Active` before the RPC. A failed RPC releases | Refuse with a toast; the ring is not declined |
| **`Decline`** | RPC | Release + RPC | RPC — declining a waiting ring is fine |
| **`CancelCall`** (I cancel my dialing) | — | Release + RPC | — |
| **Hang up** (every `HangUp*`, the activity panel's button) | — | Stop the audio; the holding loop releases the slot | — |

`CallUI.CanStartCall()` is a `[ComputeMethod]` returning "the slot is free". The
header button and the call card add it to their existing checks.

The tooltip and the toast need a new key, `Call_AlreadyInCall` ("You're already in
a call"). Adding it regenerates the derived catalogs, so the untranslated
`Call_Outgoing` / `Call_OutgoingTo` from `6cbb451eaf` get translated in the same
pass — until then `derive-bcms --check` and `derive-max --check` fail on the branch.

## Responsibilities

**`CallUI`** — mechanics only, no screens.
- State: the slot, the busy set, the candidates.
- Candidate sources: `AddCandidate(chatId)`, `ListActive`
  (`SyncActiveCallNotifications`), and on start the call notifications still shown
  by the system — all move here from `IncomingCallUI`.
- Loops: holding and searching. Joining an answered outgoing call moves here from
  `LiveSessionUI` (`JoinAnsweredCall`).
- API:
  - `GetActiveCall()` and `CanStartCall()` — `[ComputeMethod]`;
  - `TryClaimOutgoing(chatId)`, `TryCommitAccept(chatId, call)`, `Release(chatId)` —
    synchronous, under one lock;
  - every `ConfirmRing`.

**`IncomingCallUI`** — becomes presentation. Renaming it to `CallScreenUI` is a
separate, later change.
- `OnRing` stays the entry point for Android and the web: it hands the chat to
  `CallUI.AddCandidate` and sets the over-lock flag.
- `Accept` / `Decline` / `Collapse` / `Mute` / `GoToChat` / `HangUp*` stay. Their
  mechanics go through `CallUI`; the screen side stays here (the keyguard via the
  bridge, navigation, starting the audio).
- Screen flags — over-lock, foreground, collapsed islands, muted ring — derive from
  `CallUI.GetActiveCall()`. `GetIncomingCall()` is `GetActiveCall()` filtered to
  `Incoming/Ringing`.
- The ringtone (`SyncRings`) follows `Incoming/Ringing`.
- One screen loop replaces `ResetOverLockScreen`, `ResetForegroundCallScreen` and
  `ResetActiveCall`, reacting to slot transitions:
  - `Outgoing/Dialing` appears → show the outgoing modal or full-screen view;
  - `Outgoing` → `Active` → the full-screen view on a narrow screen;
  - the slot is released after `Active` → close the screens and stop the audio.
- Removed: `_inCallChatId`; `PrepareForegroundCall`, `CancelPreparedCall`,
  `ShowForegroundCall`, `ShowOutgoingCall`, `EndOutgoingCall` as outside entry points.

**`LiveSessionUI`**
- `StartCall`: microphone → `CallUI.TryClaimOutgoing` (a refusal shows the toast) →
  RPC; a failed RPC → `Release`.
- `CancelCall`: `Release` + RPC.
- The ringback follows `Outgoing/Dialing` instead of `WatchOutgoingCall`.
- Removed: `_callWatches`, `StartCallWatch` / `StopCallWatch` / `WatchOutgoingCall`,
  `JoinAnsweredCall`.

## Android native

The system call notification appears whenever the app isn't in the foreground —
backgrounded, killed, or behind the lock screen — even with Blazor alive. Its
full-screen intent launches the activity, and with it Blazor, only on a locked or
off screen and only while the full-screen-intent permission holds (Android 14+ lets
the user revoke it). On an unlocked screen it is a heads-up banner, and Blazor
doesn't start until the user taps it or Answer.

The native side decides only **whether to show** the notification. `Busy` is sent by
Blazor alone; for a second call the native side does nothing at all.

- **`FirebaseMessagingService.HandleIncomingCall`** doesn't show the notification when
  a call notification for another chat is already shown, or when Blazor is alive and
  the slot is held by another chat (read non-reactively: the notification is decided
  synchronously, before any full-screen intent fires). It still calls `OnRing` whenever
  Blazor is alive, so the Blazor side sends `Busy`.
- **Whenever it shows the notification it starts `IncomingCallRinger`** — full-screen
  or heads-up, Blazor or not. A notification without a ringtone is a missed call. Blazor's
  `StartRinging` on top of it is a no-op: `IncomingCallRinger.Start` returns when the
  player and the vibration already run.
- **The native ringtone stops** on:
  - a dismissal push for the chat whose notification is shown;
  - the ring timeout (`RingTimeout`, the same as the notification's `SetTimeoutAfter`);
  - Answer (`HandleViewIntent`) — Blazor starting up sees the call already `Active` and
    would never stop it;
  - Decline (`CallActionReceiver`);
  - Blazor's `StopRinging`.
- **Cold start with two calls.** The native side showed one notification and ignored
  the second push. Blazor learns about the second call from `ListActive` and answers
  it with `Busy`. Dismissing the system notification of a `Busy` chat in the search
  loop covers the rare case where two notifications are shown anyway.

Actively launching Blazor from the background is out of scope.

## Reuse

**Existing abstractions:**
- Android: `IncomingCallRinger` (idempotent `Start`), `IncomingCallNotifications`,
  `AndroidIncomingCallsBridge` / `IIncomingCallsBridge`.
- Session facts: `IncomingCallUI.FindRingingCall` / `GetRingingCall`,
  `LiveSessionUI.GetCallStatus`, `LiveSessionUI.AmIInLiveConversation`, `ChatAudioUI`
  (listening and recording), `LiveSessionUI.ConfirmRing`.
- Fusion: `MutableState`, `Computed.Capture` / `When` / `Update`, the
  `Snapshot` + `WhenUpdated()` wait from `SyncedState.ReadCycle`, the `baseChains`
  worker pattern from `docs/CODING_STYLE.md`.

**New components and their placement:**
- `CallUI`, `ActiveCall`, `CallOrigin`, `CallPhase` — call-specific UI mechanics that
  depend on `ChatAudioUI` and `LiveSessionUI`, so they belong in `UI.Blazor.App`, not in
  `ActualChat.Core`. Nothing here is reusable outside calls.
- The pure decision functions below — `internal static` on `CallUI`, next to their only
  caller.

## Testing

Two decisions are pure functions of their inputs and get unit tests in
`Chat.UI.Blazor.UnitTests`:

- the search outcome for one candidate: `(slot chat, active call, found call,
  busy set) → Claim | Wait | Nothing | Busy`;
- the holding step: `(active call, rings me, caller status, in conversation, was in
  conversation) → Keep | Update(call) | Join | Release`.

The loops around them need a Fusion host and are checked by hand on the two-Chrome
rig (`er5ir9` calls, `ILqcWq` answers, group `0NYND2MfRb` for a second concurrent
call, invite `Ack` read from Valkey):

1. a single ring; decline; timeout;
2. a second ring while the first rings → `Busy`, surfaces after the first is declined;
3. a ring during an accepted call → `Busy`, doesn't ring;
4. a ring during my own dialing → `Busy`;
5. the call button in another chat is disabled while a call is held; enabled again
   once it ends;
6. an outgoing call answered, then hung up by either side → the slot is free.

Android, by hand on a device:

1. app killed, screen unlocked → a heads-up notification **with** the ringtone;
   cancel from the caller stops it;
2. app killed, two calls → one notification; after opening the app the second gets
   `Busy`;
3. screen locked during an accepted call, a second call → no notification, `Busy`.

## Deferred until group calls

Only peer calls exist, so these can't happen yet; each is needed once group calls
land.

- **Blocking Join into another chat's call** — the activity panel's Join muted, the
  conversation block's Join, `JoinVideoCallModal`. Today no Join is offered for a call
  that would compete with the slot: a ring for me in another peer chat gets `Busy`, and
  a dialing caller doesn't count as activity (`GetCallActivity` skips them).
- **A `Joined` origin and adopting participation** — claiming the slot for a call
  session I'm in without having rung or dialed.
- **Reload mid-call.** `ActiveChatsUI` turns recording off on start but restores recent
  listening, so after a reload I may be listening to a call session with an empty slot.
  The call is already broken then — the peer can't hear me — so this waits for the
  adoption above.

## Known gaps

- **One over-lock ring.** If two rings arrive together on a locked device and the search
  claims the other one, the over-lock screen the first one asked for shows nothing.
- **Dropping non-ringing candidates.** A push that outruns the client's session read
  loses that ring until `ListActive` announces it again.
- **Same-chat glare.** If I dial a peer while they dial me in the same peer chat, the
  found ring's chat equals the slot's, so the search does nothing; the server already
  merges both into one session.

## Open items for the plan

1. `TryCommitAccept` from a free slot races the search claiming the same chat: both
   must go through the slot lock, and the loser must see "held by this chat".
2. The screen loop must reproduce today's teardown order exactly (`HangUp` over the
   lock screen, `HangUpForegroundCall`, `HangUpQuietly`) — map each `Reset*` loop branch
   to a slot transition before deleting them.
3. `JoinAnsweredCall` currently calls `IncomingCallUI.PrepareForegroundCall` /
   `ShowForegroundCall`; after the move it only commits `Active`, and the screen loop
   shows the view.

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
