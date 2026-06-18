# Location Sharing (#3952)

Telegram/WhatsApp-style **live location sharing**. A user taps a new
**"Share location"** item in the message-editor "+" menu and starts sharing
their live position to the current chat. Position keeps updating **even when the
app is in the background**. Everyone in the chat sees the sharer's current
position on a map.

## Scope (v1)

- **Sharing is iOS/Android only.** The "Share location" menu item appears only
  on MAUI Android/iOS builds (same gating as "Choose Photo" / "Select Video"
  in `ChatMessageEditorMenu.razor:7-18`).
- **Viewing is universal.** Web, desktop and mobile can all see the live
  positions of users who are sharing.
- **Background sharing** works via an Android foreground service and iOS
  background location mode.
- Sharing is **time-boxed** (Telegram-style: 15 min / 1 h / 8 h) and
  auto-expires; the user can stop early.
- Live location is **ephemeral reactive state**, not a permanent chat message
  (see "Data model" for why). Posting a live-location *chat entry* is deferred
  to a later phase.

Out of scope for v1: one-shot "static" pin sharing, place/venue search,
ETA/directions, history/playback, geofencing.

---

## Reuse (mandatory section)

### Existing abstractions to reuse

The feature is structurally a **chat-scoped clone of user presence**. Presence
already solves "ephemeral, frequently-changing, auto-expiring state broadcast
reactively over Fusion" — we mirror it almost 1:1.

| Concern | Reuse | Path |
|---|---|---|
| Ephemeral reactive state + TTL via `Computed.Invalidate(delay)` | `IUserPresences` / `UserPresences` / `UserPresencesBackend` | `src/dotnet/Api.Contracts/Users/IUserPresences.cs`, `src/dotnet/Users.Service/UserPresences*.cs` |
| Client periodic push loop (the "report a heartbeat every N s" worker) | `AppPresenceReporter : UIWorkerBase<AppUIHub>` | `src/dotnet/UI.Blazor.App/Services/AppPresenceReporter.cs` |
| Timing constants | `Constants.Presence` | `src/dotnet/Api/Constants.cs` |
| Chat-scoped compute-service pair (API + backend), `AddApi`/`AddBackend`, invalidation, author/membership auth | `Reactions` / `ReactionsBackend` (host inside `Chat.Service`) | `src/dotnet/Api.Contracts/Chat/IReactions.cs`, `src/dotnet/Chat.Contracts/IReactionsBackend.cs`, `src/dotnet/Chat.Service/Reactions*.cs`, `Chat.Service/Db/DbReaction.cs` |
| Runtime permission flow (check → request → troubleshoot, cached `IState<bool?>`) | `PermissionHandler` + `MauiMicrophonePermissionHandler` | `src/dotnet/UI.Blazor/Services/Permissions/PermissionHandler.cs`, `src/dotnet/App.Maui/Services/MauiMicrophonePermissionHandler.cs` |
| Android background work (foreground service + persistent notification) | `AndroidAudioWidgetForegroundService` (`[Service(ForegroundServiceType=...)]`) | `src/dotnet/App.Maui/Platforms/Android/Audio/AndroidAudioWidgetForegroundService.cs` |
| Background/foreground transitions | `MauiBackgroundState`, `AppDelegate.DidEnterBackground` | `src/dotnet/Maui/Services/MauiBackgroundState.cs`, `src/dotnet/App.Maui/Platforms/iOS/AppDelegate.cs` |
| Native event → Fusion command bridge | `AppServicesAccessor.DispatchToBlazor` | `src/dotnet/App.Maui/AppServicesAccessor.cs` |
| Menu entry + event publish | `MenuEntry` + `UIEventHub.Publish`; `AttachButtonClickEvent` | `src/dotnet/UI.Blazor.App/Components/ChatMessageEditor/ChatMessageEditorMenu.razor`, `.../Events/AttachButtonClickEvent.cs` |
| Modal (duration picker / map) | `ModalUI.Show(model)` + `IModalView` (cf. `EmojiModal`) | `src/dotnet/UI.Blazor/Services/ModalUI.cs` |
| Wire-level TTL value if needed | `Expiring<T>` / `ExpiringEntry<TKey,TValue>` | `src/dotnet/Core/Expiring.cs`, `ExpiringEntry.cs` |
| Collections / time types | `ApiArray<T>`, `Moment`, `ApiNullable8<T>` | Core |
| Permission requests on device | MAUI Essentials `Geolocation` + `Permissions.LocationWhenInUse` / `LocationAlways` | (NuGet, already available) |

**No map rendering exists** anywhere (no Leaflet/Mapbox/Google/OpenLayers in
`package.json`, no lat/long types, no geo components). The map UI is genuinely
new — see "Open questions".

> **Licensing.** This project is **AGPL-3.0** (root `LICENSE`). Any added
> dependency must be AGPL-compatible. Permissive licenses (MIT/BSD/Apache-2.0)
> are compatible and already used throughout (`rxjs` MIT, `swiper` MIT, `lit`
> BSD, `firebase` Apache-2.0); the only obligation is preserving the dep's
> license/copyright notice in the bundle. **Avoid proprietary map SDKs** as
> *dependencies*: Mapbox GL JS v2+ and the Google Maps JS SDK are proprietary
> (usable only under their own TOS + billing, not as OSS). Chosen renderer:
> **MapLibre GL (`maplibre-gl`, BSD-3-Clause)** — the OSS fork of Mapbox GL v1.
> Note OSM-derived *tile data* is ODbL and requires visible "© OpenStreetMap
> contributors" attribution — a display obligation, separate from the JS
> library's license.

> Naming note: `Place` in this codebase is a **community/workspace** container,
> not a geographic place. Do **not** reuse or overload it. New geo types must use
> distinct names (`LiveLocation`, `GeoPoint`, …).

### Reusability of new components

- **`GeoPoint` (lat/long/accuracy/bearing) + `LiveLocation` DTO** — useful beyond
  this feature (future venue pins, profile location, etc.).
  → **Place in `ActualChat.Api`** (alongside other shared API models) so both
  contracts and UI can use them. *Recommended over* burying them in a
  feature-only Contracts project.
- **Map component (TypeScript + Blazor wrapper)** — clearly reusable.
  → **Place the TS module under `src/nodejs/src/`** (shared), with a thin Blazor
  wrapper in `UI.Blazor` (not inside one feature folder). *Recommended over*
  the local-only `UI.Blazor.App/Components/<feature>` placement.
- **`LocationPermissionHandler` (abstract)** → `UI.Blazor/Services/Permissions`
  (shared, mirrors `MicrophonePermissionHandler`); MAUI impl in `App.Maui`.
- **Platform location tracker** is inherently platform-specific → stays in
  `App.Maui` (shared C# interface, per-platform impl), mirroring audio capture.
- **`ILiveLocations` service** — a distinct bounded context; recommend a dedicated
  service trio (below), with the lighter alternative noted.

---

## Architecture

```
 iOS/Android (MAUI)                         Server (Fusion)              All clients
 ┌─────────────────────────┐                ┌───────────────────┐        ┌──────────────┐
 │ LiveLocationReporter     │  Update cmd    │ ILiveLocations     │  RPC   │ ChatLocation │
 │  (UIWorkerBase loop)     │ ─────────────▶ │  OnStart/Update/   │ ◀────▶ │  Banner +    │
 │   ↑ reads position       │   every ~10 s  │  Stop (CommandH.)  │ Compute│  Map modal   │
 │ ILocationTracker         │                │ List/Get/IsSharing │ methods│ (MapLibre GL)│
 │  ├ Android FG service    │                │  (ComputeMethod,   │        └──────────────┘
 │  └ iOS CLLocationManager │                │   TTL-invalidated) │
 │ LocationPermissionHandler│                │ DbLiveLocation     │
 └─────────────────────────┘                └───────────────────┘
```

### Data model — why ephemeral state, not a chat message

Live location changes every few seconds and must auto-expire. Modeling it as a
mutable chat entry would spam the message log and the edit/invalidation
machinery. Presence already proves the ephemeral-state pattern at this codebase,
so we follow it: one row per `(ChatId, AuthorId)` holding the **latest** position,
upserted on each update, filtered out once stale/expired by the compute methods.

Within a chat, a participant is an **`AuthorId`** (not a raw `UserId`) — matching
how `Reactions`/`Mentions` key their data; this also supports anonymous authors.

```
GeoPoint            : (double Latitude, double Longitude, float? Accuracy, float? Bearing)
LiveLocation        : (ChatId, AuthorId, GeoPoint Point, Moment StartedAt,
                       Moment UpdatedAt, Moment ExpiresAt)
DbLiveLocation      : key = $"{ChatId}:{AuthorId}", + columns above (single upserted row)
```

Persisting to Postgres (like `DbUserPresence`/`DbReaction`) keeps it simple and
durable across restarts. Write rate ≈ one row/sharer/~10 s — comparable to
presence check-ins and read-position updates already in the Chat DB.
*Optimization option (note, not v1):* Redis-only / in-memory `ExpiringEntry` to
avoid DB writes if scale demands it.

**Privacy / retention.** The upserted row may live in the DB indefinitely, but the
*coordinates* must not outlive the share. On `OnStop` **and** on expiry, **clear
the lat/long** (set `Point` to null / delete the row) so a stopped share leaves no
residual position — matching Telegram/WhatsApp. Concretely: `OnStop` nulls
coordinates immediately; expired rows are scrubbed lazily on next read and/or by a
periodic cleanup. Compute methods already hide expired entries, so this is about
*data-at-rest*, not visibility.

### Backend service — host inside `Chat.Service`

`Chat.Service` is not just the message store: it hosts ~15 small sibling
compute-services (`Authors`, `Reactions`, `Mentions`, `Roles`, `Aliases`,
`Translations`, `ReadPositions`, …), each a `Foo.cs` + `FooBackend.cs` pair that
shares the one `ChatDbContext` and the one `Chat.Service.Migration`. Location is
chat-scoped, shards by `ChatId` (already Chat.Service's shard key), and needs chat
membership/author resolution that lives right here — so add it as **one more
sibling pair**, using **`Reactions`** as the closest template. **No new service
trio.**

New files (mirroring `Reactions`/`ReactionsBackend`):
- `Api.Contracts/Chat/ILiveLocations.cs`, `Chat.Contracts/ILiveLocationsBackend.cs`
- `Chat.Service/LiveLocations.cs`, `Chat.Service/LiveLocationsBackend.cs`
- `Chat.Service/Db/DbLiveLocation.cs` + `DbSet` in `ChatDbContext` + a
  `Chat.Service.Migration` migration
- register both in `Chat.Service/Module/ChatServiceModule.cs` (`AddApi` + `AddBackend`)

**`ILiveLocations : IComputeService`** (`Api.Contracts/Chat`):
```
[ComputeMethod] Task<ApiArray<LiveLocation>> List(Session session, ChatId chatId, CT)
[ComputeMethod] Task<LiveLocation?>          Get(Session session, ChatId chatId, AuthorId authorId, CT)
[ComputeMethod] Task<bool>                   IsSharing(Session session, ChatId chatId, CT)  // current author
[CommandHandler] Task OnStart  (LiveLocations_Start  cmd, CT)   // Session, ChatId, Duration
[CommandHandler] Task OnUpdate (LiveLocations_Update cmd, CT)   // Session, ChatId, GeoPoint
[CommandHandler] Task OnStop   (LiveLocations_Stop   cmd, CT)   // Session, ChatId
```
Commands are `ISessionCommand`; the API layer resolves the caller's `AuthorId`
for the chat and authorizes membership (reuse `IAuthors`/`IRoles` exactly as
`Reactions` does), then forwards to `ILiveLocationsBackend` (`AuthorId`-keyed,
`IHasShardKey<ChatId>`) via `ICommander`.

**Auto-expiry** mirrors `UserPresences.Get` (`UserPresences.cs`): `List`/`Get`
drop entries past `ExpiresAt`, and call
`Computed.GetCurrent().Invalidate(timeUntilNextExpiry)` so the result flips to
"not sharing" exactly when a share lapses, with no background job. `OnUpdate`
extends `UpdatedAt`; a missed-update grace (`StaleTimeout`) hides a sharer whose
device went silent before `ExpiresAt`.

### Client — capture & reporting (MAUI)

1. **`ILocationTracker`** (shared interface in `App.Maui`, per-platform impl):
   `StartAsync(accuracy)`, `StopAsync()`, `IState<GeoPoint?> LastKnown` (or an
   event/channel). Mirrors the `IAudioCapture` split.
   - **Android:** `FusedLocationProviderClient` for periodic updates, driven from
     a foreground service `[Service(ForegroundServiceType = ForegroundService.TypeLocation)]`
     modeled on `AndroidAudioWidgetForegroundService`, with a persistent
     "Sharing your location" notification.
   - **iOS:** `CLLocationManager` with `RequestWhenInUseAuthorization`,
     `AllowsBackgroundLocationUpdates = true`, `StartUpdatingLocation` (When-In-Use
     + background mode keeps updates flowing while backgrounded during the share).
2. **`LocationPermissionHandler`** (abstract, `UI.Blazor`) + `MauiLocationPermissionHandler`
   (`App.Maui`) — copy `MauiMicrophonePermissionHandler` exactly, using
   `Permissions.LocationWhenInUse`; Troubleshoot opens a settings modal like
   `RecordingTroubleshooterModal`.
3. **`LiveLocationReporter : UIWorkerBase<AppUIHub>`** — copy `AppPresenceReporter`:
   while any local share is active, every ~10 s read `ILocationTracker.LastKnown`
   and `Commander.Call(new LiveLocations_Update(...))`; stop the tracker + FG
   service when all shares end or expire. Starting/stopping is driven by
   observing `ILiveLocations.IsSharing`.
4. **DI:** register tracker + permission handler in `MauiProgram.iOS.cs` /
   `MauiProgram.Android.cs` and `MauiAppModule.cs` (same spots as mic/notifications).

### Client — UI

1. **Menu entry** in `ChatMessageEditorMenu.razor`, inside the existing
   MAUI-Android/iOS `@if` block: `MenuEntry Icon="icon-location" Text="Share location"`
   → publishes a new `ShareLocationButtonClickEvent(EditorId)`.
2. **Editor handler** in `ChatMessageEditor.razor` (`<OnUIEvent TEvent="ShareLocationButtonClickEvent" ...>`)
   → ensure permission (handler above) → open **`ShareLocationModal`** (duration
   picker 15 m / 1 h / 8 h, or "Stop sharing" if already active) →
   `Commander.Call(new LiveLocations_Start(...))`.
3. **Chat banner** (new `ChatLiveLocationBanner.razor`) pinned at top of the chat
   view, reactive on `ILiveLocations.List(chatId)`: shows sharer avatars + count
   ("You and 2 others are sharing location"), tap → opens the map modal; shows a
   "Stop" control when the current user is sharing.
4. **Map view** (`LiveLocationMapModal.razor` + map component): renders each
   active `LiveLocation` as an avatar marker, updating live as `List` re-computes.

### Map rendering (new)

No mapping capability exists. Use **MapLibre GL (`maplibre-gl`, BSD-3-Clause)** —
the OSS community fork of Mapbox GL v1, AGPL-compatible, polished vector maps,
no proprietary SDK. Add `maplibre-gl` to `package.json`, build a TS module under
`src/nodejs/src/` exposing `init/setMarkers/dispose`, and a Blazor wrapper
component in `UI.Blazor` calling it via JS interop (pattern: existing TS modules
wired into Blazor). Markers (avatar pins) update from the reactive `List`.

MapLibre renders **vector tiles**, so it needs a **style + tile source** (unlike
Leaflet's plain raster URL). Options, isolated behind our module so the source is
swappable without touching UI:
- A free/demo public style for early dev (e.g. the MapLibre demo style or a free
  OpenFreeMap/MapTiler-style endpoint),
- A self-hosted style + tiles, or
- A paid vector-tile host (e.g. MapTiler) via config/secret — same swap pattern
  as the licensing note's tile discussion.

Render the required attribution for whichever tile/data source is used
("© OpenStreetMap contributors" for OSM-derived tiles). Decide the concrete tile
source as part of phase 2 (see open questions).

### Permissions / manifests

- **iOS** `Platforms/iOS/Info.plist`: add `NSLocationWhenInUseUsageDescription`
  (When-In-Use only; no `Always` key needed) and add `location` to
  `UIBackgroundModes` (currently `audio/voip/fetch/remote-notification`).
- **Android** `Platforms/Android/AndroidManifest.xml`: add `ACCESS_FINE_LOCATION`,
  `ACCESS_COARSE_LOCATION`, `FOREGROUND_SERVICE_LOCATION`, and declare the
  foreground service with `foregroundServiceType="location"`.
  **Do not** request `ACCESS_BACKGROUND_LOCATION` for v1 — the running
  foreground service (started while the app is foregrounded) already grants
  location access while backgrounded for the active share, and
  `ACCESS_BACKGROUND_LOCATION` triggers a special, heavier Play Store review.
  This mirrors the iOS "When In Use" decision: rely on the user-started,
  foreground-initiated session, not OS relaunch-after-termination.

### Constants

Add `Constants.LiveLocation`: `UpdatePeriod` (~10 s), `StaleTimeout` (~30 s),
`MinCacheDuration` (mirror presence's 30 s), allowed durations
(15 m / 1 h / 8 h), `MaxDuration`.

---

## Implementation phases

Each phase ends at a **verification gate** and leaves the tree
building/green. Phases 1–3 need no device; 4–5 need physical iOS + Android.
Dependency order: 1 → (2 ∥ 3) → 4 → 5 → 6. Phase 2 (map) and phase 3 (viewing
UI shell) can proceed in parallel once phase 1's contracts exist.

### Phase 0 — Scaffolding & shared types ✅ done
- Add `GeoPoint` (`Latitude`, `Longitude`, `float? Accuracy`, `float? Bearing`)
  and `LiveLocation` DTO to **`ActualChat.Api`** (`[DataContract]` +
  `[MemoryPackable]` + `[MessagePackObject]`, like `Reaction`).
- Add `Constants.LiveLocation` (`UpdatePeriod` ~10 s, `StaleTimeout` ~30 s,
  `MinCacheDuration` 30 s, allowed durations 15 m / 1 h / 8 h, `MaxDuration`).
- **Gate:** `dotnet build ActualChat.CI.slnf` green.

### Phase 1 — Backend (`Chat.Service`), no device ✅ done
1. `Chat.Contracts/ILiveLocationsBackend.cs` — `Get`/`List`/`IsSharing` compute
   methods + `LiveLocationsBackend_Start/Update/Stop` commands
   (`IHasShardKey<ChatId>`), mirroring `IReactionsBackend`.
2. `Api.Contracts/Chat/ILiveLocations.cs` — session-scoped `Get`/`List`/
   `IsSharing` + `LiveLocations_Start/Update/Stop` (`ISessionCommand`,
   `IApiCommand`), mirroring `IReactions`.
3. `Chat.Service/Db/DbLiveLocation.cs` — key `"{ChatId}:{AuthorId}"`, columns for
   coords + `StartedAt`/`UpdatedAt`/`ExpiresAt`; `ToModel()`/`UpdateFrom()`;
   add `DbSet` to `ChatDbContext`; index on `ChatId`.
4. `Chat.Service/LiveLocationsBackend.cs` — compute methods filter expired/stale
   rows and `Computed.GetCurrent().Invalidate(timeUntilNextExpiry)`; command
   handlers upsert + invalidate (`Invalidation.IsActive` pattern); `OnStop`/
   expiry **scrub coordinates** (privacy).
5. `Chat.Service/LiveLocations.cs` — resolve caller's `AuthorId`, authorize chat
   membership via `IAuthors`/`IRoles` (as `Reactions` does), forward to backend
   via `ICommander`.
6. Register both in `Chat.Service/Module/ChatServiceModule.cs`
   (`AddApi` + `AddBackend`).
7. EF migration in **`Chat.Service.Migration`** (`dotnet ef migrations add LiveLocations`).
- **Gate:** integration tests below pass (`Chat.IntegrationTests` or equivalent).

### Phase 2 — Map component (web/TS), no device  *(∥ phase 3)*
1. Add `maplibre-gl` to root `package.json`.
2. TS module `src/nodejs/src/.../live-location-map.ts` exposing
   `init(el, style)`, `setMarkers(markers)`, `dispose()`; render avatar pins,
   render attribution.
3. Blazor wrapper `UI.Blazor/.../LiveLocationMap.razor` calling it via JS interop.
4. Wire a chosen vector-tile **style/source** (open question 1) — start with a
   free public style.
- **Gate:** `npm run build:Verify` green; component renders static sample markers.

### Phase 3 — Viewing UI shell (web), no device  *(∥ phase 2)*
1. `ChatLiveLocationBanner.razor` — pinned in chat view, reactive on
   `ILiveLocations.List(chatId)`: sharer avatars + count; tap opens map; "Stop"
   when current author is sharing.
2. `LiveLocationMapModal.razor` (`IModalView`, via `ModalUI.Show`) — hosts the
   phase-2 map, feeds it the reactive `List`, updates markers live.
- **Gate:** seed shares server-side; markers render and move on web/desktop.

### Phase 4 — MAUI capture (iOS + Android), device
1. `ILocationTracker` (shared, `App.Maui`) — `StartAsync(accuracy)`/`StopAsync()`
   + `IState<GeoPoint?> LastKnown`.
2. **Android:** `FusedLocationProviderClient` driven by a foreground service
   `[Service(ForegroundServiceType = ForegroundService.TypeLocation)]` (template:
   `AndroidAudioWidgetForegroundService`) + persistent notification; manifest
   permissions (`ACCESS_FINE/COARSE_LOCATION`, `FOREGROUND_SERVICE_LOCATION`;
   **no** `ACCESS_BACKGROUND_LOCATION`).
3. **iOS:** `CLLocationManager` (`RequestWhenInUseAuthorization`,
   `AllowsBackgroundLocationUpdates = true`); Info.plist
   `NSLocationWhenInUseUsageDescription` + `location` background mode.
4. `LocationPermissionHandler` (abstract, `UI.Blazor`) + `MauiLocationPermissionHandler`
   (`App.Maui`, copy of `MauiMicrophonePermissionHandler`) +
   troubleshoot modal; DI in `MauiProgram.{iOS,Android}.cs` + `MauiAppModule.cs`.
- **Gate (device):** tracker emits positions foreground **and** backgrounded;
  permission grant/deny/limited handled.

### Phase 5 — MAUI control flow (start/stop), device
1. Menu entry "Share location" in `ChatMessageEditorMenu.razor` (inside the
   existing MAUI-Android/iOS `@if`) → publishes `ShareLocationButtonClickEvent`.
2. `ShareLocationButtonClickEvent` + `<OnUIEvent>` handler in
   `ChatMessageEditor.razor` → ensure permission → open `ShareLocationModal`
   (duration picker / "Stop").
3. `LiveLocationReporter : UIWorkerBase<AppUIHub>` (shape of `AppPresenceReporter`)
   — while a local share is active, push `LiveLocations_Update` every
   `UpdatePeriod`; start/stop the tracker + FG service driven by
   `ILiveLocations.IsSharing`; stop on expiry.
- **Gate (device):** start → others see live movement on web; lock phone /
  switch apps → keeps updating; stop or expiry → marker disappears, coords scrubbed.

### Phase 6 — Polish
- Banner/notification copy; own-marker vs others styling; marker clustering;
  expiry/countdown UX; battery tuning (update cadence, accuracy mode); error/no-fix
  and permission-revoked-mid-share handling.
- **Gate:** battery sanity over a 1 h share; QA pass on web + both platforms.

---

## Testing

- **Backend (integration):** start/update/stop; auto-expiry flips `List`/`IsSharing`
  at `ExpiresAt`; stale-timeout hides a silent sharer; non-member is rejected;
  multiple sharers in one chat. Follow the presence/Contacts test patterns.
- **Map/UI:** seed shares server-side, verify markers render and move on web.
- **MAUI (manual, device):** foreground + **background** sharing on a physical
  iOS and Android device; permission grant/deny/limited; FG-service notification;
  app kill → share expires; battery sanity over a 1 h share.

---

## Open questions

1. **Vector-tile style/source for MapLibre.** Renderer is decided: **MapLibre GL
   (BSD-3, vector).** MapLibre needs a style + vector-tile source. Options: a
   free/demo public style for dev, self-hosted tiles, or a paid host (e.g.
   MapTiler) via config/secret. Free public endpoints typically have rate/usage
   limits, so production scale likely needs self-hosted or paid tiles. All are
   swappable behind our module. — confirm the v1 source (default: a free public
   style for dev, decide production source before launch).
2. **Service placement.** *Decided:* host inside `Chat.Service` as a `Reactions`-
   style sibling pair (chat-scoped, shards by `ChatId`, author/membership auth
   already present) — no new service trio.
3. **Storage.** *Decided:* Postgres (`DbLiveLocation` in `ChatDbContext`), matching
   presence/reactions. Coordinates are scrubbed on stop/expiry for privacy (see
   Data model). Redis-only remains a later optimization if write volume bites.
4. **iOS authorization level.** *Decided:* request **"When In Use"**. With the
   `location` background mode + `AllowsBackgroundLocationUpdates = true`, iOS keeps
   delivering updates while backgrounded/locked for the duration of an active,
   user-started share (shows the blue status-bar indicator). This does not survive
   app termination — acceptable for a time-boxed, foreground-initiated share.
   "Always" (needed only for relaunch-after-termination / geofencing) is rejected
   for v1 due to the harder permission + App Store cost.
5. **Phase-2 chat entry.** Should an actual (live-updating) chat message be posted
   like Telegram, or is the banner+map sufficient for v1? *Default: banner only.*
