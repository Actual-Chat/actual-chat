# Audio Diagnostics UI (#4029)

## Problem

On iOS, incoming audio playback sometimes **suddenly stops mid-conversation**:
the user can no longer hear other people, yet still hears "tunes" (notification
/ UI sounds). This is the classic signature of an **AVAudioSession
interruption/suspension that never fully recovered**: the `Playback` category
session is dead while the `Tune`/`Ambient` path still produces sound.

Today there is no way to inspect the live audio state on a device. We have a
rich **Video Diagnostics** modal; we want an analogous **Audio Diagnostics**
modal that surfaces:

- the native **AVAudioSession** state (iOS / Mac Catalyst): category, mode,
  active/suspended/interrupted flags, current route/output, focus scopes;
- the JS **Web Audio** playback state: `AudioContext` state
  (running/suspended/closed), per-track feeder state (playing / starving /
  ended / paused), buffer level, presentation lag;
- the active inbound **streams** and their players.

The goal is a **read-only troubleshooting panel** the user can open on the
device when audio dies, take a screenshot / copy text, and hand to us — plus a
small set of recovery actions (force session reactivate / resume AudioContext).

## Root-cause context (why these fields matter)

The suspected failure lives in the interaction of three layers. The panel is
designed to make the break visible at a glance:

| Layer | File | State that reveals the bug |
|---|---|---|
| Native session | `App.Maui/MaciOS/Audio/AppleAudioFocusUI.cs` | `_isInterrupted`, `_isSuspended`, `_isSessionConfigured`, active-scope mode (`_activeScopes.GetMode()`), per-mode scope counts |
| Native session | `App.Maui/MaciOS/Audio/AudioSession.cs` | category, current route outputs, receiver-vs-speaker override |
| JS playback | `UI.Blazor.App/Services/audio-context-source.ts` | `AudioContext.state` (running/**suspended**/closed), `isActive`, background-activity state, ready/maintain-loop status |
| JS playback | `UI.Blazor.App/Components/AudioPlayer/audio-player.ts` | per-track `playbackState` (`playing`/`starving`/`ended`/`paused`), `authorId`, buffer/lag |

The tell-tale pattern for this bug: **AVAudioSession `_isSuspended == true`
(or `AudioContext.state == 'suspended'`) while inbound streams are live and
feeders are `starving`.** `AppleAudioFocusUI.TryRecover` / route-change
re-arming exists precisely for this, so the panel should also show *whether
recovery has been attempted* and expose a manual "Reactivate" button.

## Reuse (mandatory section)

### Existing abstractions to reuse

- **Modal pattern** — mirror `VideoDiagnosticsModal` almost 1:1:
  - `ComputedStateComponent<AppUIHub, ComputedModel>` + `IModalView<Model>`
    (`Components/VideoPanel/VideoDiagnosticsModal.razor`).
  - Registration: `BlazorUIAppModule.cs:144`
    `modals.Add<VideoDiagnosticsModal.Model, VideoDiagnosticsModal>()` — add an
    analogous `.Add<AudioDiagnosticsModal.Model, AudioDiagnosticsModal>()`.
  - Open via `ModalUI.Show(new AudioDiagnosticsModal.Model(...))` (cf.
    `ChatVideoUI.ShowDiagnostics`, `ChatVideoUI.cs:218`).
  - JS polling on a 1s `Timer` reading `JS.InvokeAsync<JsonElement>` +
    `GetStr/GetNum/GetBool` JSON helpers — copy verbatim from
    `VideoDiagnosticsModal.razor` (`PollJsDiagnostics`, `EnsurePollingState`,
    the `Get*` helpers, `CopyTrigger` "Copy" button, the `.diag-*` CSS
    classes in `video-diagnostics-modal.css`).
- **Enable flag** — reuse the `LocalAppSettings` pattern: add
  `IsAudioDiagnosticsEnabled` next to `IsVideoDiagnosticsEnabled`
  (`Api/LocalAppSettings.cs:26,34`) with an `...OrDefault => ?? false`, and a
  toggle in `DeveloperTools.razor` (mirror `OnVideoDiagnosticsEnabledClick`).
- **Audio focus abstraction** — extend the existing base
  `AudioFocusUI` (`UI.Blazor/Services/AudioFocusUI.cs`) rather than inventing a
  new bridge. Add a `virtual AudioFocusDiagnostics GetDiagnostics()` returning a
  cross-platform record; `AppleAudioFocusUI` overrides it (and gets a matching
  snapshot from `AudioSession`), `AndroidAudioFocusUI` optionally overrides,
  the web/default base returns "always focused". **Do not restructure the
  `_activeScopes` / `_byMode` dictionary** (see memory
  `iosaudio_keep_modes`) — the panel only *reads* it.
- **Data model** — reuse `System.Text.Json.JsonElement` transport for the JS
  side exactly like video (no new DTO codegen).
- **Feeder / player state** — already emitted by the feeder worklet and cached
  on `AudioPlayer` (`playbackState`, `authorId`, `recordedAtMs`,
  `targetBufferSizeMs`, `audioLatencyEma`); the collector just reads them.
- **AudioContext state** — `AudioContextSource` already tracks
  `context.state`, `isActive`, `_backgroundActivityState`, ready/maintain loop
  — expose via a small getter, don't duplicate.

### New components — placement (local vs shared)

| New component | Placement | Rationale |
|---|---|---|
| `AudioDiagnosticsModal.*` (razor + partials + css) | **local** — `UI.Blazor.App/Components/AudioPanel/` (new folder, parallel to `VideoPanel/`) | Feature-specific UI, mirrors `VideoPanel/`. |
| `AudioFocusDiagnostics` record + `AudioFocusUI.GetDiagnostics()` | **shared** — declare the record and the `virtual` in `UI.Blazor/Services/AudioFocusUI.cs` (same file/project as the base) | Cross-platform contract; every platform impl fills it. |
| `AudioSessionDiagnostics` snapshot | native — `App.Maui/MaciOS/Audio/AudioSession.cs` | iOS/Mac-only native reads; folded into the Apple `GetDiagnostics()`. |
| JS `collectAudioPlaybackDiagnostics()` collector | **local** — new `audio-diagnostics.ts` under `Components/AudioPanel/`, exported through `BlazorUIAppModule.ImportName` like `collectRemoteStreamDiagnostics` | Feature-specific; mirrors `video-diagnostics.ts`. |
| Active-player **registry** (live set of playing `AudioPlayer`s) | **local** — a `static Set<AudioPlayer>` in `audio-player.ts` | `AudioPlayer` uses an `ObjectPool`, so the pool holds *idle* instances too; the collector needs the *currently active* set. Maintained in `create()` / end/return-to-pool. |
| `LocalAppSettings.IsAudioDiagnosticsEnabled` | shared — `Api/LocalAppSettings.cs` | Same store as the video flag. |

No suitable existing "audio diagnostics" type exists — confirmed by searching
`api-index*.md` and the tree (only `PlaybackStats`, `RecorderStats`,
`AudioRecorderState`/`AudioDiagnosticsState` exist; those are recorder-side).

## Design

### Modal layout

`AudioDiagnosticsModal` with a `DialogFrame` titled "Audio Diagnostics", a
`CopyTrigger` "Copy" button, and tabs (reuse `.diag-tabs`):

1. **Session** (default; the money tab for this bug)
   - **AVAudioSession** (iOS/Mac only): category, mode, `isActive`,
     `isInterrupted`, `isSuspended`, `isSessionConfigured`, current route
     outputs (port type + name), speaker-override state.
   - **Audio focus**: active mode (`Tune`/`Playback`/`Recording`), per-mode
     scope counts, last recover/rebuild markers.
   - **Web Audio context** (all platforms): `state`
     (running/suspended/closed), `isActive`, background-activity state, ready
     flag, last resume attempt result.
   - On non-Apple platforms the AVAudioSession block is hidden (record fields
     null), Web Audio block always shown.
2. **Playback** (per-track)
   - One row per active `AudioPlayer`: `authorId`/title, `playbackState`
     (playing/starving/ended/paused) with a colored chip, presentation lag,
     target buffer, recordedAt age. Plus a header count.
3. **Streams**
   - Reuse `ChatAudioUI` to list active inbound live-audio streams for the
     chat (author, streamId, age). Ties a "starving" feeder to "stream is
     live" → confirms the break is local, not upstream.
4. **Actions** (**dev-instance / admin only** — like video's Settings tab; the
   tab is hidden for everyone else, who still get the Copy button in the header)
   - **Reactivate audio session** → `AudioFocusUI.TryRecover()`.
   - **Resume AudioContext** → JS `interactiveResume` on the playback context.
   - **Copy diagnostics** (in the header, available to everyone).
   - These are the on-device "try to un-stick it" levers, and each is a
     natural experiment for confirming the root cause.

### C# ↔ native bridge

```
AudioDiagnosticsModal (ComputedStateComponent)
   ├─ ComputeState: reads AudioFocusUI.GetDiagnostics()  → session/focus fields
   │                reads ChatAudioUI active streams      → Streams tab
   └─ PollJsDiagnostics (1s Timer):
        JS collectAudioPlaybackDiagnostics()  → AudioContext + per-player state
```

`AudioFocusUI.GetDiagnostics()` (new virtual):

```
record AudioFocusDiagnostics(
    bool IsSupported,            // false on web/default
    AudioFocusMode ActiveMode,
    IReadOnlyDictionary<AudioFocusMode,int> ScopeCounts,
    bool IsInterrupted, bool IsSuspended, bool IsSessionConfigured,
    AudioSessionDiagnostics? Session);   // iOS/Mac only
```

Apple override reads its own private flags and calls a new
`AudioSession.GetDiagnostics()` (category / route outputs / override) on the
main thread. Base returns `IsSupported: false`.

### JS collector

`collectAudioPlaybackDiagnostics()` returns:

```
{
  context: { state, isActive, backgroundActivity, isReady, lastResumeError },
  players: [ { authorId, playbackState, presentationLagMs,
               targetBufferSizeMs, recordedAtAgeMs } ]
}
```

Backed by a `static activePlayers: Set<AudioPlayer>` maintained in
`AudioPlayer.create()` and the end/return-to-pool path. `AudioContextSource`
gains a `getDiagnostics()` returning its state fields.

### Entry points

- **Primary: call-header button** — a small `HeaderButton` in the chat activity
  panel's header, **next to the hang-up button**
  (`ChatActivityPanel.razor`, the `.c-buttons` block at ~line 60–84, beside the
  `icon-phone-off` hang-up `HeaderButton`). This is the ideal on-device entry:
  when audio dies mid-conversation the header is already on screen, so the user
  can open diagnostics without leaving the call or hunting through settings.
  - **Visibility gate**: rendered **only when audio diagnostics are allowed** —
    `IsAudioDiagnosticsEnabledOrDefault || IsDevelopmentInstance`. When the flag
    is off, the button does not exist (no layout impact next to hang-up).
  - Wire `Click` → `ChatAudioUI.ShowAudioDiagnostics(Chat.Id)`. Reuse an
    existing waveform/stethoscope-style `icon-*` glyph.
  - `ChatActivityPanel.ComputeState` already builds a `Model`; extend it with a
    `CanShowAudioDiagnostics` bool (read the `LocalAppSettings` flag +
    `HostInfo.IsDevelopmentInstance`, cf. `ChatVideoUI.cs:147`
    `CanShowDiagnostics`). The `@if` in `.c-buttons` renders the button on that
    flag, independent of the join/hang-up/listening branches so it shows
    whenever the panel is up.
- **Secondary: global** — a button in `DeveloperTools.razor` ("Audio
  Diagnostics"), reachable even when *not* in a call (same gate). Backstop for
  reproducing state outside an active listening session.
- **Tertiary: per-chat menu** — optional item in the audio/voice menu
  (`ChatAudioPanel` / `VoiceSettingsModal`).
- `Model` takes an **optional `ChatId`** (nullable). Session/Context tabs work
  with no chat; Streams/Playback populate when a chat is provided or fall back
  to "all active players".

## Implementation steps

1. **Contract** — add `AudioFocusDiagnostics` + `AudioSessionDiagnostics`
   records and `virtual AudioFocusUI.GetDiagnostics()` (base: unsupported) in
   `UI.Blazor/Services/AudioFocusUI.cs`.
2. **Native** — implement `AppleAudioFocusUI.GetDiagnostics()` (read
   `_isInterrupted`/`_isSuspended`/`_isSessionConfigured`/scope modes) and
   `AudioSession.GetDiagnostics()` (category/route/override). Expose the
   private flags via a small `lock`-guarded snapshot to stay thread-safe.
   Optionally an `AndroidAudioFocusUI` override.
3. **JS** — add `AudioPlayer.activePlayers` registry + `getDiagnostics()` on
   `AudioContextSource`; add `audio-diagnostics.ts` `collectAudioPlaybackDiagnostics()`;
   export `collectAudioPlaybackDiagnostics` (and `audioDebugResumeContext`) via
   `BlazorUIAppModule`/`exports.ts`. Run `npm run build:Verify`.
4. **Modal** — create `Components/AudioPanel/AudioDiagnosticsModal.razor`
   (+ `.Actions.cs` partial, `audio-diagnostics-modal.css`) mirroring the
   video modal's polling/JSON-helper/Copy scaffolding. Reuse `.diag-*` CSS
   (consider promoting shared `.diag-*` rules, but simplest first pass is to
   copy the subset used).
5. **Wiring** — register in `BlazorUIAppModule.cs`; add
   `ShowAudioDiagnostics(ChatId?)` on `ChatAudioUI` (parallel to
   `ChatVideoUI.ShowDiagnostics`).
6. **Enable flag + entry points** — add `IsAudioDiagnosticsEnabled` (+
   `...OrDefault`) to `LocalAppSettings`; toggle in `DeveloperTools.razor`
   (mirror `OnVideoDiagnosticsEnabledClick`). **Primary entry point**: add the
   header `HeaderButton` next to hang-up in `ChatActivityPanel.razor` gated by a
   new `Model.CanShowAudioDiagnostics` (populated in its `ComputeState` from the
   flag + `IsDevelopmentInstance`). Also add the `DeveloperTools` global button
   and, optionally, the per-chat menu item.
7. **Actions** — wire "Reactivate session" → `AudioFocusUI.TryRecover()` and
   "Resume AudioContext" → JS resume.
8. **Verify** — build (`*.CI.slnf`), `npm run build:Verify`, then run on an iOS
   device: start listening, trigger an interruption (incoming phone call /
   Siri), end it, and confirm the panel shows the suspended→recovered
   transition and that "Reactivate" restores audio.

## Resolved decisions

- **Scope** — **diagnostics only.** Make the bug observable + add manual
  recovery levers; fixing `AppleAudioFocusUI` recovery is a follow-up once the
  panel confirms the stuck state.
- **Recovery actions** — **included, but dev-instance / admin only** (Actions
  tab hidden otherwise). The read-only panel + Copy button are available to
  anyone who enabled the flag.
- **Platforms** — **iOS/Mac full, others minimal.** Full AVAudioSession detail
  on Apple; Web Audio context state on all platforms; `AndroidAudioFocusUI`
  reports focus-mode only (`IsSupported` true but `Session: null`).

## Remaining minor decision

- **Shared `.diag-*` CSS** — copy the used subset now vs. extract a shared
  stylesheet. *Recommendation: copy the subset for the first PR; extract later
  if a third diagnostics panel appears.* (Low impact, reversible.)

## Non-goals

- Fixing the underlying iOS recovery bug (this PR only makes it *observable*
  and adds manual recovery levers).
- Server-side audio meters (already covered by `AppMeters.Audio*` /
  `docs/live-audio/08-diagnostics-and-tuning.md`).
- Recorder-side diagnostics (already exist as `AudioRecorder.AudioDiagnosticsState`).
