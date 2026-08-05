# Walkie-Talkie Cleanup: De-static Session, Flag Removal, Doc Consolidation

Status: Implemented
Branch: feat/walkie-talkie-push

## Goal

Four post-implementation cleanups for the walkie-talkie feature:

1. The long-deferred `WalkieTalkieSession` de-static refactor (parent design
   decision 1, deferred E2 → E3 → E4, closed by E4 decision 9; now reopened
   deliberately).
2. Remove `Features_EnableWalkieTalkiePush`; the IncompleteUI gate stays the
   single UI switch.
3. Delete implemented plan docs from `docs/superpowers/plans/`; specs become
   the durable record.
4. Add walkie-talkie coverage to `docs/live-audio/`.

## Non-Goals

- Any behavior change in the wake, reply, or transmit paths. Part 1 is a
  pure refactor; part 2 keeps the ops kill switch semantics identical.
- New UI. The DeveloperTools "Early access UI features" toggle (admin-only,
  per-user, default off) already controls walkie UI visibility via
  `Features_EnableIncompleteUI`; that stays as-is.
- Restructuring `WalkieTalkieWakeHandler` or `IosPushToTalk`. Their nested
  `WalkieTalkiePlatform` singletons stay where they are.
- The device-verification program. Unchanged and still owed.

## Part 1 — `WalkieTalkieSession` two-layer split

Today `WalkieTalkieSession` (`App.Maui/Services/WalkieTalkieSession.cs`) is a
static class whose five call sites are all native/static entry points with no
DI scope (FCM service, iOS PTT delegate, notification action, MainActivity).
Its only mutable static is the teardown watcher; everything else is per-call
locals over scoped services.

### Core: `WalkieTalkieSessionCore` (new, `UI.Blazor.App/Services/`)

An instance service, registered scoped in `BlazorUIAppModule`, exposed off
`AppUIHub`, next to `WalkieTalkie.cs`. Constructor takes `AppUIHub`. Absorbs
everything that operates purely on scoped services:

- `StartPlayback` (incl. stale-wake branch, cue, armed-restore set)
- `StopOrphanedReply`, `PlayFailureCue`, `StopReplyAndWaitForRecorder`
- `WatchAudioFocus` — the denial-count baseline becomes instance state; the
  watcher gets a cancellation owner tied to scope disposal instead of being
  a detached task (the one deliberate deviation from behavior-preservation:
  today the detached task can outlive the scope it observes)
- the transmit body after app-ready: practice-mode and already-recording
  guards, the startup budget, `RequestReply`, returning `WalkieTalkieReply?`

`WalkieTalkiePlatform` (abstract hooks; references only `AppUIHub`, `ChatId`,
`Moment`) moves to `UI.Blazor.App/Services/` so the core can take it as a
parameter. The Android (`WalkieTalkieWakeHandler.AndroidPlatform`) and iOS
(`IosPushToTalk.IosPlatform`) implementations stay nested in `App.Maui`.

### Facade: `WalkieTalkieSession` (stays static, `App.Maui/Services/`)

Keeps its name, file, and all call-site signatures — zero churn at the five
native call sites. Retains only what is process-global by necessity:

- entry points `HandleWake` / `HandleTransmit` / `StopHeadless` /
  `StopAndDispose(Current)`
- the `BlazorWebViewApp.WhenAppReady` waits (a scoped service cannot await
  the readiness of the container it lives in)
- scope resolution — unified onto
  `AppScopeAccessor.Current ?? HeadlessBlazorScope.GetOrCreate()?.Services`,
  replacing the duplicated logic in the current `ResolveScope` while
  preserving its create-and-race-recheck semantics
- the teardown watcher (`_teardownWatcher` + `Lock`) — it watches the
  process-singleton `HeadlessBlazorScope.Current` and must be able to
  dispose the very scope a core instance lives in

After resolving a scope, the facade resolves `WalkieTalkieSessionCore` from
it and delegates.

### Verification

- No behavior change: verified by diffing the moved bodies against the
  originals (same discipline as the C-epic Task 4 extraction) plus the
  existing sweep (`Chat.UI.Blazor.UnitTests`, `UI.Blazor.UnitTests`, build).
- New unit tests in `Chat.UI.Blazor.UnitTests` for the core's transmit-guard
  decisions — the first concrete win of MAUI-free testability.
- `scripts/csc-ios-probe.sh` and `scripts/csc-android-probe.sh` stubs updated
  to the new shapes; both probes must exit 0.

### Reuse

- Reuses `AppScopeAccessor` (drops the facade's duplicate scope-resolution
  logic), `HeadlessBlazorScope`, `AppUIHub`, the existing `WalkieTalkie`
  pure helpers, and the `WalkieTalkiePlatform` hook shape unchanged.
- New component placement: `WalkieTalkieSessionCore` and
  `WalkieTalkiePlatform` go to `UI.Blazor.App` (shared, platform-neutral) —
  exactly the promotion the parent design prescribed. Nothing new lands in
  `App.Maui` beyond what must stay there.

## Part 2 — Remove `Features_EnableWalkieTalkiePush`

- Delete `src/dotnet/Notifications.Service/Features_EnableWalkieTalkiePush.cs`.
- `NotificationsBackend.OnSpeechStartedEvent` reads
  `Settings.EnableWalkieTalkiePush` directly instead of
  `ServerFeatures.Get<Features_EnableWalkieTalkiePush>(...)`. The
  `NotificationsSettings.EnableWalkieTalkiePush` config property stays as the
  ops kill switch (non-reactive is fine — it is startup-bound config).
- Remove the `ServerFeatures` field from `NotificationsBackend` if nothing
  else uses it.
- `WalkieTalkiePushTest` needs no changes: its flag-off coverage flips the
  config value, not the feature.
- UI gating unchanged: the four IncompleteUI gates from commit `0823125a93`
  (`SettingsModal`, `VoiceSettingsStartModalPage`, `WalkieReplyToggle`,
  `PrivacySettings`) remain the visibility switch.

## Part 3 — Delete implemented plan docs

Policy change: implementation plans are deleted once their work has shipped;
specs are the durable record. (Previously finished plans were kept in-repo
for reuse.)

- Delete, verified implemented already: all 8 walkie plans
  (`2026-07-13-walkie-talkie-{android,ios,server-trigger}`,
  `2026-07-20-walkie-talkie-{heard-receipts,reply-e1-core}`,
  `2026-08-03-walkie-talkie-{headset-button-e3,ptt-settings-e2}`,
  `2026-08-04-walkie-talkie-ios-transmit-e4`), plus
  `2026-07-07-android-incoming-call-ui` (header: done),
  `2026-07-14-live-conversation-block-ux-plan` and
  `2026-07-20-live-block-ux-polish` (shipped).
- Verify per-plan against git, delete only if implemented:
  `2026-06-18-live-session-2plus-peers`,
  `2026-07-21-live-block-sticky-header-footer`,
  `2026-07-22-live-block-{swallow-and-show-more,unified-shell}`,
  `2026-07-23-live-session-dialing-state`,
  `2026-07-24-notification-newest-first`,
  `2026-07-24-receiver-wedge-diagnostics-and-self-heal`,
  `2026-07-24-virtual-list-pivot-scoping`,
  `2026-07-27-maui-embedded-auth`.
- Keep: `2026-05-26-video-qc-decision-log` (a decision log, not a plan).
- Since specs become the sole record, fix the stale walkie spec Status
  lines: the Android, iOS, and server-trigger specs still read
  "pre-implementation"; the parent reply-to-voice design still lists E2–E4
  as pending; this cleanup's own spec status updates on completion.

## Part 4 — `docs/live-audio/` walkie-talkie coverage

New `docs/live-audio/10-walkie-talkie.md`, written from current source in the
style of the existing numbered docs, covering:

- Server trigger: `SpeechStartedEvent` emission from `OnStreamRegistered`
  (voice-only, failure-insulated), `NotificationsBackend.OnSpeechStartedEvent`
  fan-out — armed predicate (`PttChatIds`), member cap, speaker/active
  exclusion, mute check, wake-dedup TTL, kill switch.
- Wake transports: data-only FCM (Android) and direct-APNs `pushtotalk`
  (iOS) incl. the config the APNs path needs.
- Client wake paths: Android FGS + headless Blazor scope + teardown watcher;
  iOS PTT channel join/restoration; armed/hot lifecycle and the background
  idle drop.
- Reply pipeline: `WalkieTalkieReplyUI`, `ReplyTargetResolver`, gestures
  (`GestureUI`, detectors, sensor feed), headset button, iOS transmit with
  pre-roll, `AudioSession` typed ownership.
- Heard receipts: `ReportPlayback` → `ChatPositionKind.Heard`.
- Settings and gating: `UserWalkieTalkieSettings`, IncompleteUI visibility.
- README index row + top-level architecture diagram update, cross-links to
  the walkie specs.

## Testing

- Part 1: existing suites green (`Chat.UI.Blazor.UnitTests`,
  `UI.Blazor.UnitTests`), new core guard tests, both csc probes exit 0,
  `dotnet build ActualChat.CI.slnf` 0 errors.
- Part 2: `Notifications.IntegrationTests --filter WalkieTalkiePushTest`
  unchanged and green.
- Parts 3–4: docs-only; build not affected. Doc content verified against
  source while writing (file:line spot checks).

## Execution order

Parts are independent; execute 2 (smallest) → 1 (riskiest) → 3 → 4, each as
its own commit(s), so the refactor lands against a quiet baseline and the
docs describe the post-refactor shape.

## Deviations found during implementation

- `ResolveScope` was kept as-is: `AppScopeAccessor.Current` cannot report
  the `IsHeadless` flag its callers need, so the planned unification would
  have changed behavior.
- No `AppUIHub` accessor for `WalkieTalkieSessionCore`: the facade resolves
  the core from the specific scope it picked; a hub accessor would be dead
  code.
