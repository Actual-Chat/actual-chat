# Approximate Address in Location Messages

## Goal

Show a human-readable approximate address (e.g. "Marktstraße, Prenzlauer Berg, Berlin")
instead of raw coordinates in the location message footer
(`LocationMessageView.razor` → `c-caption`, currently `GeoPoint.ToDisplayText()`
= `"52.5417, 13.4106"`). Coordinates remain the fallback while the address is
unknown or geocoding fails, and stay in the copy/share actions (map URLs).

## Current state (investigated)

- **No geocoding exists anywhere in the codebase** — forward or reverse. The only
  "geocode" mentions are CoreLocation error-enum mappings in
  `AppleLocationTracker.cs`.
- Coordinates are displayed in exactly one place: `LocationMessageView` footer
  caption. `LocationMapModal` / `LocationParticipantView` show name, distance,
  and time left — no coordinates. `LocationParticipantMenu` uses coordinates only
  to build Google Maps / OSM URLs, which should stay coordinate-based.
- The map stack is MapLibre GL + `maps.<host>` nginx reverse proxy to
  **OpenFreeMap** (`tiles.openfreemap.org`) — tiles/styles only, OpenFreeMap has
  **no geocoding API**. So an address requires a new external provider.
- `SharedLocation.Point` lives in `Chat.Service` (`DbSharedLocation`); the UI
  reads it via `ISharedLocations.Get` (Fusion compute method), so reactive
  update of a derived address is free once it's a compute method.
- `Media.Service` link previews are the established pattern for "derive data
  from an external HTTP call, persist it, invalidate": compute method reads the
  DB row via `IDbEntityResolver`, a `ThrottledUpdateFlow` (`LinkPreviewFlow`)
  does the fetch out-of-band and stores the result via a backend `Change`
  command, whose invalidation block re-invalidates the compute method. Flows are
  already wired in `Chat.Service` (`ChatServiceModule.cs:177`).

## Design

### Where to geocode: server-side

Client-side geocoding (browser/MAUI calling Nominatim etc.) is rejected:
- every viewer of the same message would repeat the call (rate-limit abuse,
  no shared cache);
- leaks viewer IP + shared coordinates to a third party per client;
- MAUI `Geocoding.GetPlacemarksAsync` (platform geocoders) covers only native
  apps, not web, and yields inconsistent address strings across platforms.

Server-side: one geocode per location cell, shared cache, provider key stays
secret, results identical for all viewers. The server already stores the exact
coordinates, so nothing new is exposed except to the geocoding provider (which
receives *rounded* coordinates only — see below).

### "Approximate" = rounded cell + street-level zoom

- Round lat/lon to **3 decimals (~110 m)** before geocoding. This is the cache
  key and the precision cap: house numbers are dropped from the result (they'd
  be wrong at ~100 m anyway; GPS `Accuracy` is often worse).
- Request street/suburb-level detail (Nominatim `zoom=16`, or equivalent per
  provider) and format as `road, suburb/locality, city` — skipping empty parts.
- A live share updating every 30 s re-geocodes only when the point crosses a
  ~110 m cell boundary; parked users cost one request total.

### Provider

Pluggable client behind a small interface; provider + key + base URL in
settings. Comparison:

| Provider | Cost | Notes |
|---|---|---|
| Nominatim public API | free | ≤1 req/s policy, mandatory caching + UA header; fine for dev/low volume, gray area for commercial scale |
| Photon (komoot.io) | free | fair-use, fewer languages, no SLA |
| LocationIQ / OpenCage | free tier → paid | hosted Nominatim, policy-clean, SLA, ~5k/day free |
| Google Geocoding API | paid (free monthly quota) | best quality; GCloud billing/keys already used for transcription |
| Self-hosted Nominatim/Photon | infra cost | planet import is 100s of GB + ops burden — overkill now |

**Recommendation**: ship with Nominatim public API as the default (volumes after
cell-caching are tiny; enforce a global 1 req/s outbound throttle + proper
`User-Agent`), keep the provider swappable by config so production can move to
LocationIQ or Google if volume or ToS pressure demands it. Decision on the
production provider/key is an ops call — flag at review.

### Data flow (mirrors link previews)

1. **Cache table** `GeoAddresses` in `ChatDbContext`:
   `Id` (cache key: `"{lat:F3},{lon:F3}:{lang}"`), `Version`, `Address`
   (empty = negative result), `ModifiedAt`. New migration in
   `Chat.Service.Migration`. Addresses are effectively immutable; no TTL/refresh
   in v1 (negative entries retried via flow rescheduling if `ModifiedAt` is old).
2. **Backend** `IGeoAddressesBackend` (Chat.Contracts + Chat.Service):
   - `[ComputeMethod] Get(GeoAddressId id)` — reads via `IDbEntityResolver`,
     returns null on miss and schedules `GeocodeFlow` via
     `FlowHub.TryScheduleUpdate` (same shape as `LinkPreviewsBackend.Get`);
   - `[CommandHandler] OnChange(...)` — upserts the row, invalidates `Get`.
3. **`GeocodeFlow : ThrottledUpdateFlow`** — parses the key, calls the
   provider client with timeout (~5 s), stores the result (empty string on
   not-found/failure) via `OnChange`.
4. **Session-facing surface**: new compute method on `ISharedLocations`:
   `GetAddress(Session, ChatId, SharedLocationId, CancellationToken)` — checks
   `ChatPermissions.Read` (same as `Get`), loads the location, rounds its point,
   delegates to `IGeoAddressesBackend.Get`. Tying it to a `SharedLocationId`
   instead of accepting a raw `GeoPoint` keeps it access-controlled and avoids
   shipping an open geocoding proxy.
   Additive API change — old clients simply never call it (no wire-compat risk).
5. **UI** (`LocationMessageView.razor`): `ComputeState` also awaits
   `SharedLocations.GetAddress(...)`; `Model` gains `Address`; caption becomes
   `address ?? m.Center.ToDisplayText()`. When a live point crosses a cell, the
   compute chain invalidates and the caption updates automatically.

Language: v1 requests English (or provider default); the cache key already
carries `lang` so per-user language (issue #3721 l10n work) can plug in later
without a schema change.

### Outbound HTTP

`services.AddHttpClient(ReverseGeocoder.HttpClientName)` with a fixed
`User-Agent` (same registration shape as `Crawler` in `MediaServiceModule`).
The target host is a fixed configured provider, not user-supplied, so
`EgressGuard`-style SSRF checks are not needed. A `SemaphoreSlim`+min-interval
throttle in the client enforces the 1 req/s Nominatim policy.

## Reuse

**Existing abstractions to reuse:**
- `ThrottledUpdateFlow` + `FlowHub.TryScheduleUpdate` (`ActualChat.Flows`) —
  out-of-band fetch, exactly as `LinkPreviewFlow`.
- `DbServiceBase<ChatDbContext>`, `IDbEntityResolver<string, TDbEntity>`,
  `Change`/`RecordDiff` command shape, `VersionGenerator` — as in
  `LinkPreviewsBackend` / `SharedLocationsBackend`.
- `GeoPoint` (`Api/GeoPoint.cs`) — add nothing to it except (optionally) a
  `RoundToCell()` helper next to `ToDisplayText()`.
- `Chats.GetRules(...).Require(ChatPermissions.Read)` — existing access check in
  `SharedLocations`.
- `IHttpClientFactory` via `AddHttpClient` named client (as `Crawler`, `Gifs`).
- Settings-class pattern (`ChatSettings`) for provider/base-URL/key/throttle.
- No existing geocoder/address/place abstraction was found in `docs/api-index*.md`
  or the codebase — confirmed absent, hence the new component below.

**Reusability of new components:**
- `ReverseGeocoder` (provider HTTP client + response→address formatting): not
  chat-specific; plausible future users (attach-location, check-ins, search).
  Options: (a) `Chat.Service` local; (b) **`ActualChat.Core.Server`** (shared,
  no UI deps) — recommended per default-to-shared rule; only the cache table,
  flow, and session surface stay in `Chat.Service` (they're bound to
  `ChatDbContext`/`ISharedLocations`).
- `GeoAddress`/cache-key type: keep in `Chat.Contracts` next to
  `SharedLocation`; promote later only if a second consumer appears.

## Steps

1. `ReverseGeocoder` client + settings + fake implementation for tests/dev
   (pattern: `UseFakeLanguageDetection`) — in `Core.Server`.
2. `DbGeoAddress` + migration; `IGeoAddressesBackend` (`Get`/`OnChange`) +
   `GeocodeFlow` in `Chat.Service`; register flow + named HTTP client in
   `ChatServiceModule`.
3. `ISharedLocations.GetAddress` (contract + impl).
4. `LocationMessageView` caption swap + model field.
5. Tests: rounding/key + address formatting unit tests; backend test with fake
   geocoder (seed via command pipeline, not direct store writes); adjust
   location e2e if it asserts on the coordinate caption.

## Out of scope / later

- Address in `LocationMapModal` participant rows (design doesn't show it today).
- Per-user language for addresses (cache key is ready for it).
- Cache refresh/TTL policy beyond negative-entry retry.
- Forward geocoding / place search ("send a pin at an address").

## Open questions

1. Production provider + key (ops/cost decision) — default is public Nominatim
   behind the cell cache and 1 req/s throttle.
2. Exact caption format when parts are missing (bare `suburb, city` vs falling
   back to coordinates) — propose: show whatever non-empty parts exist, fall
   back to coordinates only when the provider returns nothing.
