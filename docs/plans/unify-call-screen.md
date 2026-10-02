# One call screen — unify the full-screen call view and the full-screen video (#5049)

> Status: proposed (2026-10-02). Nothing implemented yet.

## Goal

**One component, `CallScreen`, is the full-screen view of a call and of a chat's live video.** It
is used for a ring over the lock screen, for dialing, for an active call with or without video,
and for the expanded video of an ambient live session (someone streams into a chat, nobody
called). It has one header, one control bar and one *stage* between them; the stage shows the
peer's avatar when there is no video and the video tiles when there is. Turning a camera on or
off, a remote stream arriving or ending, a screen share starting — none of them switches screens,
because there is no second screen to switch to.

The same component instance is also the inline video panel in the chat header and the floating
video island: those are modes of it, so the video players survive every mode change.

It is hosted globally, not in the chat page, so a call answered over the lock screen shows its
video without an unlock.

## Decisions

- **One component for calls and for ambient expanded video** — not two components sharing bars,
  and not a chrome layer over a video layer.
- **Wide, audio-only call stays in the chat.** Wide gets the full-screen mode only while video is
  expanded; an opt-in expand for audio-only calls is a possible later addition.
- **Wide, video ends while full-screen: back to the chat, as today.** On wide the full-screen mode
  exists only while there is video. The 500 ms ending-grace still bridges a codec switch or a
  camera off/on. On narrow the screen stays, with the avatar stage.
- **Back and Esc collapse the call screen, with or without video.** The user lands in the call's
  chat with the in-call island. Back never hangs up, and does nothing while ringing or over the
  lock screen.
- **The video is hoisted out of the chat page in this issue**, so it works over the lock screen
  and the "chat must be mounted under the call screen" rule goes.
- **AOT regen is not a cost to weigh**: `FullScreenCallView` and `VideoPanel` are replaced by
  `CallScreen`, the word the code around them already uses (`CallScreensUI`, `LeaveCallScreen`,
  `z-call-screen`, `CALL_SCREEN` in the e2e helpers).

## Current behavior

Two components draw a full-screen call, and a call moves between them:

| | `FullScreenCallView` | `VideoPanel` in `Expanded` mode |
|---|---|---|
| Mounted | `AlwaysVisibleComponents` — global | `ChatHeader` → `VisualActivityPanel`; reparented to `body` by `video-panel.ts` |
| Shown when | `CallScreensUI.DecideView` → `FullScreen` (over-lock ring, narrow dialing, narrow active) | `ChatActivityUI` panel mode is `Expanded` and there are streams |
| State | `_collapsedChatId`, `_overLockRingChatId` in `CallScreensUI` | `_panelModes` in `ChatActivityUI`, `_watchingChatId` in `ChatVideoUI` |
| Z-layer | `z-call-screen` (199) | `z-button` (100) |
| Header | collapse · hang-up | collapse · grid toggle · focused name + "N members" · ⋮ menu · hang-up · pin row |
| Footer (mobile) | switch · camera · mic · speaker · react · options | camera · mic · switch (camera on only) · react |
| Footer (desktop) | share · camera · mic · react · options | share · mic · camera · chat · react |
| Mic button | own `btn-rec`, disabled while dialing | `RecorderToggle Compact` |
| Audio output | speaker toggle / `AudioOutputMenu` | none |
| Call timer | yes | no |
| Tap hides bars | no | yes (`toolbar-hidden`) |
| Back / Esc | nothing | collapse (`HistoryStepper`, Esc) |
| Safe areas | raw `env(safe-area-inset-*)` | `--safe-area-*` / `--vp-safe-*` |
| Hang-up | `CallScreensUI.HangUp` → `CallUI.HangUp` (releases the slot first) | stops mic, listening and video, closes the panel; the slot is released only when the server notices |

Because the call screen sits above the video, the two hand the screen over through the collapsed
flag (`docs/calls/incoming-call-flow.md`, "Presenting the ring"): `OnVideoExpanded` hides the call
screen once the panel covers it, `VideoPanel.CloseWithoutStreams` calls `CallScreensUI.Expand` to
bring it back, `ExpandToFullScreen` picks one of the two for the island, `IsOnCallScreen` tells
`VisualActivityPanel` to open the video expanded.

The video panel exists only inside the chat page. `VisualActivityPanel` (in `ChatHeader`) decides
when it opens — own streams, or listening while someone streams — and closes it when the user
moves to another chat; both carry a `TODO(DF): move this logic to ChatVideoUI`. Hence
`OpenChatUnderCallScreen`, and hence no video over the lock screen, where the app never navigates
to the chat.

They already share `.btn-video-panel`, `CallReactionsOverlay` and `CallReactionsMenu`.

## Design

### One component

`CallScreen` lives in `AlwaysVisibleComponents`, where `FullScreenCallView` is today. It replaces
`FullScreenCallView` and the shell of `VideoPanel`; the tile grid of `VideoPanel` becomes its
child, `VideoStage`.

```
CallScreen                     one instance per subject chat, global
├ background                   blurred avatar + scrim       full-screen, no video
├ CallScreenHeader             full bar in full-screen; expand + ⋮ in inline and island
├ stage
│  ├ VideoStage                tiles, connecting overlay    whenever the chat has video
│  └ avatar · name · status    otherwise (full-screen only)
├ CallControls | ring buttons  full-screen only; ring buttons while ringing
├ chat column                  wide, full-screen, never over the lock screen
└ CallReactionsOverlay
```

**Subject.** The chat the screen is for: the call in the slot when its view is full-screen or it
has video, otherwise the chat being watched (`ChatVideoUI.GetWatchingChatId`). No subject — the
component renders nothing, as `FullScreenCallView` does today.

**Modes** are classes on the one root element, as `VideoPanel` does it now:

| Mode | When | Where the root is |
|---|---|---|
| Full-screen | a ring over the lock, narrow dialing, narrow active call; any expanded video | at home, `fixed inset-0` |
| Inline | has video, not full-screen, its chat is on screen | moved into the chat header's slot |
| Island | has video, floated by the user or forced by the keyboard / landscape | at home, `fixed` |
| Hidden | has video, minimized to the activity pill | at home, `display: none` |
| — | no video and not full-screen | not rendered; `CollapsedCallView` is the in-call island |

`VideoStage` stays mounted across all four modes, so `VideoTrackPlayer` instances never remount
and no stream restarts. Going from "audio call, full-screen" to "video call, full-screen" mounts
`VideoStage` inside the same root: the bars are the same DOM nodes before and after.

**Phases** are a second class on the root (`ringing`, `dialing`, `in-call`, `ambient`) and decide
what the bars show: ring buttons instead of the control bar while ringing; the mic disabled while
dialing; no status line or timer for ambient video.

**The inline slot.** The reparenting inverts. Today the panel's home is the chat header and it is
moved to `body` for full-screen and island; now the home is global and the root is moved *into*
the slot `VisualActivityPanel` renders — an empty element carrying the panel's `data-child`
names, so `data-has-open-video-panel` on the layout header keeps working — when the mode is
inline, the Call tab is selected and that chat is on screen. `homeMarker`, `restoreToParent` and
`setupHomeGuard` flip accordingly; the island no longer needs a move.

Blazor can remove the slot with the root inside it (the user leaves the chat). The node is only
detached, not destroyed, so a guard pulls it home: a `MutationObserver` on the slot's removal plus
placement re-run after every render. If that proves fragile, the fallback is to never reparent —
keep the root at home and position it over a height-reserving placeholder in the header.

`VisualActivityPanel` keeps everything that is the chat's: the map panel, the Call/Map switch,
the activity pill, the drag handle, and the slot.

**Script.** `video-panel.ts` becomes `call-screen.ts`: gestures, island drag, compact-layout
forcing, slot placement, tap-to-hide-bars. `incoming-call-swipe.ts` attaches to the ring buttons
as now.

**Cascades.** The chat column (`ChatView` + `ChatMessageEditor`) and `RecorderToggle` need what
they inherit from the chat page today — `ChatContext`, `ScreenSize`, `RegionVisibility`.
`CallScreen` builds the `ChatContext` from `Chats.Get` (as `ChatVideoUI.JoinVideoSession` does)
and supplies the rest.

Rejected alternatives:

- **Two components sharing the bars** (`CallControls` used by both, the screens still swap). The
  swap stays visible as a remount, and two layouts still have to be kept in step.
- **Chrome layer over a video layer** (`CallScreen` transparent above a chrome-less expanded
  `VideoPanel`). One set of bars, but two components that must agree on a "video layer is up"
  flag, on tap-to-hide and on the chat column's width — coordination a single component doesn't
  need.
- **`CallScreen` renders its own tiles, the inline panel renders others.** Inline ↔ full-screen
  would remount the players: every stream restarts and waits for a keyframe.

### One control set

- `CallScreenHeader` — collapse; grid/speaker toggle and the pin row when there is video; centre:
  chat title, or the focused participant when there is video, with the status or the timer under
  it; hang-up on the right. In inline and island modes only expand and ⋮ remain.
- `CallControls` — the footer, one order on every stage:

  | | Mobile / narrow | Desktop |
  |---|---|---|
  | 1 | camera | screen share |
  | 2 | switch camera (camera on; long-press → `CameraMenu`) | camera |
  | 3 | mic (`RecorderToggle Compact`) | mic (`RecorderToggle Compact`) |
  | 4 | audio output (where `AudioFocusUI.OutputRoutes` exists) | chat column toggle (when there is video) |
  | 5 | reactions / hand | reactions / hand |
  | 6 | ⋮ more | ⋮ more |

  "More" opens the existing `VideoPanelMenu` (Float, Hide, Video settings, Voice settings,
  Diagnostics), fed by `ChatVideoUI.GetVideoPanelActions`; the dedicated options button goes.
  While dialing the mic is disabled, as now — `RecorderToggle` gets an `IsDisabled` parameter.

These two are separate components only to keep `CallScreen` readable; nothing else uses them.

### One state model

`CallScreensUI` owns "which chat is the subject and in which mode":

- `GetScreen()` (compute method) returns the subject chat, its call (null for ambient video), the
  mode and `IsOverLock`. It combines `GetCallView` with the watched chat and its panel mode;
  `CallScreen` reads nothing else to decide what it is.
- `Expand(chatId)` / `LeaveCallScreen(chatId)` are the two verbs. They set the collapsed flag and
  the panel mode together, so the two stores can't disagree. The header's expand/collapse, the
  island, the inline expand button and Esc/back all call them.
- `DecideView` gets one new row: **wide + active + expanded video → `FullScreen`**. A wide active
  call otherwise stays in the chat, and returns there when its video ends.
- **Ambient video**: no call in the slot, the watched chat's panel mode is `Expanded` → full-screen
  with `Call = null`.
- **Hang-up** is one method: `CallScreensUI.HangUp(chatId)` — cancel a dial, `CallUI.HangUp` for
  a call, `CallUI.StopCallMedia` when there is no call.
- **Whether the chat has video** stays `ChatVideoUI`'s: the rules now in
  `VisualActivityPanel.ComputeState` and its render move to a worker chain there, as the TODOs
  ask — open for the chat with own camera or screencast; open for the chat the user is in a call
  or listening in once it has remote streams; close when the user leaves the chat with the panel
  inline or hidden (today's behaviour; own streams keep publishing and `OwnVideoIsland` shows
  them). A full-screen or island panel no longer depends on the selected chat.

Deleted with the second screen: `OnVideoExpanded`, `IsOnCallScreen`, `ExpandToFullScreen`,
`OpenChatUnderCallScreen`, `VideoPanel.CloseWithoutStreams`' call to `Expand`,
`VideoPanel.OnHangUpClick` and its `_skipCloseAnimation` path. Turning the camera on from the call
screen is `ChatVideoUI.StartVideoCapture(chatId)` and nothing else; over the lock screen it no
longer asks for the keyguard first.

### Behaviour, unified

- **Tap hides the bars** — only while there is video; the avatar stage keeps its bars.
- **Back / Esc collapse** — one `HistoryStepper` step for the full-screen mode, audio-only
  included. Not over the lock screen, not while ringing.
- **Safe areas** — `--safe-area-*` (`docs/ui/safe-areas.md`) for the bars; video still runs under
  the insets.
- **Mac titlebar** — the `native-titlebar` header inset applies to the one header.
- **Z-layers** — full-screen keeps `z-call-screen`, a ring `z-modal-overlay`; island `z-button`;
  the DOM-order rule in `AlwaysVisibleComponents` (before `MenuHost`) stays.
- **Rotation freeze, compact-layout island** — as in `video-panel.ts` today.

## Reuse

### Existing abstractions

| What | Used for |
|---|---|
| `CallScreensUI.GetCallView` / `DecideView`, `CallScreenFlags` | The decision of which screen a call gets; extended, not replaced |
| `CallUI.HangUp` / `StopCallMedia`, `ChatVideoUI.LeaveVideoSession` | The single hang-up path |
| `ChatActivityUI.SetPanelMode` / `TogglePanelMode` / `GetPanelMode` | The video's mode; shared with the map panel, so it stays |
| `ChatVideoUI` (`UIWorkerBase`) | Home of the open/close rules, as its own worker chain — no new service |
| `ChatVideoUI.GetWatchingChatId`, `OpenVideoPanel`, `CloseVideoPanel`, `GetOwnSourceKind`, `HasRemoteStreams` | Inputs of those rules and of the subject |
| `VideoPanelLayoutCalculator`, `RemoteStreamPlayer`, `VideoTrackPlayer`, `VideoStreamingPreview` | `VideoStage` is today's grid, moved, not rewritten |
| `video-panel.ts` gestures, island drag, compact forcing | Carried into `call-screen.ts` |
| `ChatContext`, `Chats.Get` | The component's context for the chat column and `RecorderToggle` |
| `ChatUI.SelectedChatId` | "The user left the chat", as `OwnVideoIsland` reads it |
| presence tracker (`data-child` / `data-children`) | The slot keeps the header's `data-has-open-video-panel` |
| `ChatVideoUI.GetVideoPanelActions`, `VideoPanelActions`, `VideoPanelMenu`, `VideoPanelMenuContent` | The "more" menu, unchanged |
| `RecorderToggle` (`Compact`) | Mic button on every stage |
| `AudioOutputMenu`, `CallUI.GetOutputRoutes` / `SelectOutputRoute` | Audio output control |
| `CallReactionsOverlay`, `CallReactionsMenu`, `LiveSessionUI.CanReact` / `IsOwnHandRaised` | Reactions |
| `CameraMenu`, `CameraUI.SwitchCamera` | Camera switch |
| `HeaderButton` + `.btn-video-panel` | Every button in both bars |
| `LiveDuration`, `ChatIcon` | Timer, avatar stage |
| `HistoryStepper` | Back-button collapse |
| `ScreenSize.freeze`, `CompactLayout`, `DraggableIsland` | Unchanged |
| `incoming-call-swipe.ts` | Ringing stays as is |

`CallModal` and `CollapsedCallView` have their own ring buttons and are out of scope. Nothing
existing hosts a chat-scoped component globally with a reparented inline position; the panel's own
home-marker code is the closest and is what gets inverted.

### New components

| New | Reusable elsewhere? | Placement |
|---|---|---|
| `CallScreen` (+ `call-screen.ts`, `call-screen.css`) | It is the reuse: one component for every call and video surface | `UI.Blazor.App/Components/CallScreen/` |
| `VideoStage` | Only inside `CallScreen` | same folder |
| `CallScreenHeader`, `CallControls` | Only inside `CallScreen` | same folder |
| Slot placement + removal guard | Possibly — the map or `OwnVideoIsland` could use "global component with an inline slot" later | In `call-screen.ts` now. The shared option is a small helper under `src/nodejs/src/`; recommended against until a second user exists |
| `CallScreensUI.GetScreen` | Call-screen state | `CallScreensUI` |
| `RecorderToggle.IsDisabled` | Generic, but a parameter on an existing component | in place |

Nothing here has a second consumer today, so nothing goes to `ActualChat.Core` or
`src/nodejs/src/`.

## Steps

Each step builds and ships on its own. Steps 1–4 change nothing the user sees, so the existing
call and video e2e specs are their safety net; the merge itself is step 6.

1. **Hang-up** — route `VideoPanel` hang-up through `CallScreensUI.HangUp`; add the no-call branch.
2. **Open/close rules → `ChatVideoUI`** — move the auto-open and leave-chat logic out of
   `VisualActivityPanel` into a worker chain; the panel is still mounted in the chat.
3. **Hoist** — mount `VideoPanel` from `AlwaysVisibleComponents`; add the slot in
   `VisualActivityPanel`; invert the reparenting in `video-panel.ts`; supply the cascades.
4. **Extract `VideoStage`** from `VideoPanel`: the grid, the connecting / ending-grace logic, the
   layout calculator, pin handling.
5. **Shared bars** — `CallScreenHeader` and `CallControls`, adopted by both `FullScreenCallView`
   and the expanded `VideoPanel`. The two screens still swap, but now look the same; the control
   order, `RecorderToggle` and the ⋮ menu land here.
6. **Merge** — `CallScreen` replaces `FullScreenCallView` and `VideoPanel`; `GetScreen`; the two
   verbs drive both stores; delete the hand-off methods listed above; back/Esc and tap-to-hide
   unified. `call-screen.ts` and `call-screen.css` replace `video-panel.*` and
   `full-screen-call-view.css`; AOT regen (`App.AotHelper -g`); selectors in `peer-call.ts` /
   `video-call.ts`. From here there is one screen.
7. **Wide and ambient** — the new `DecideView` row and the ambient branch of `GetScreen`.
8. **Cleanup** — dead `VideoPanelActions` flags, the `btn-rec` CSS, safe-area vars and the titlebar
   inset; localize "N members" (`docs/i18n.md`); rewrite "Presenting the ring" in
   `docs/calls/incoming-call-flow.md` and the panel-hosting part of `docs/live-video/07-receiver.md`
   if it describes the mount.

## Risks

- **Size of the merge.** `VideoPanel.razor` (717 lines), `video-panel.css` (1116) and
  `video-panel.ts` (1036) are folded into the new component. Steps 4 and 5 exist to make step 6
  mostly a move: by then the stage and the bars are already separate components both screens use.
- **Foreign node in Blazor-managed DOM.** The inline slot holds a node its owner component didn't
  render. The slot must stay childless in markup, and the removal guard must win every time; the
  placeholder-overlay fallback is described above.
- **`<video>` elements pause when moved.** The move already happens today in the other direction,
  and `mstg-playback-watchdog.ts` covers it; the inline ↔ home move must go through the same path.
- **The ring over the lock screen must stay fast.** `FullScreenCallView` is light; `CallScreen`
  must not mount `VideoStage`, the chat column or the layout calculator while ringing.
  `OnOverLockScreenRendered` keeps its place.
- **Subject change remounts the component** (a call accepted in one chat while watching video in
  another). Intended — the old chat's players have to go — but it must not flash the page.
- **Camera over the keyguard on Android.** The mic foreground service starts from the over-lock
  activity; whether the camera does is unverified. If Android refuses, the camera button over the
  lock screen keeps the unlock step, and only remote video shows without it.
- **Leave-chat rule as a worker** reads `ChatUI.SelectedChatId` instead of a render; it must not
  close a panel that is full-screen over the lock screen, where no chat is selected.

## Tests

- `CallScreensUIDecisionsTest` — the wide + expanded row; new cases for `GetScreen`'s mode and
  subject as a pure function, next to `DecideView`.
- `OutgoingCallScreensTest`, `IncomingCallAcceptTest` — unchanged expectations must still hold.
- After steps 3 and 4, the whole existing video e2e set must pass untouched. Add: leave the chat
  with the panel inline and come back; switch Call/Map tabs; inline → island → full-screen →
  inline without a stream restart (same `data-stream-id` canvas node throughout).
- `call-screen-follows-video.test.ts` — rewritten: the call screen's root and its control bar are
  the same DOM nodes before, during and after the camera is on, on both sides.
- `call-expand-buttons.test.ts` — island and inline expand both land on the call screen, from
  another chat too, with no navigation.
- New e2e: the control bar's buttons and order are identical with and without video, and in an
  ambient session; hang-up from a video call releases the slot at once; back collapses an
  audio-only call screen.
- Existing specs that click expanded-panel buttons (`raise-hand-video`, `call-reactions-picker`,
  `call-reaction-author-name`, `call-video-no-preview`, `call-audio-output-web`) move to the
  call-screen selectors.
- Safe-area and notch check with the phone-frame emulation (`debugUI.emulatePhone`).
- Device pass, Android: answer a video call over the lock screen — the ring shows as fast as
  before, remote video shows, own camera starts (or asks to unlock), no chat content is visible,
  hang-up returns behind the keyguard. iPhone and Android: rotation, the keyboard island, inline
  drag-to-minimize.

## Follow-ups (not in this issue)

- `CollapsedCallView` (the "In call" island) and `OwnVideoIsland` could become modes of
  `CallScreen` too.
- `CallModal`'s ring buttons share markup with the call screen's ring row.
- Opt-in full-screen for an audio-only call on wide.
