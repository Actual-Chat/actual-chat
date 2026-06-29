# One-shot `ILocationTracker.Get()` — #3952

Companion to [`location-sharing.md`](location-sharing.md). Resolves the
`// TODO: Tracker.Get() without Start/Stop???` in `LocationUI.SendCurrentLocation`.

## Context

"Send current location" needs **one** position fix. Today `LocationUI.SendCurrentLocation`
fakes a one-shot out of the *continuous* tracking lifecycle:

```csharp
await Tracker.Start(ct);                       // begins continuous updates
try {
    var cPoint = await Tracker.LastKnown.Computed.When(x => x is not null, ct);
    // create + post using cPoint
}
finally {
    if (_shares.Value.Length == 0)             // don't stop an active live share
        await Tracker.Stop(ct);
}
```

Problems:
- **Abuses continuous tracking for a single fix** — mutates the shared `LastKnown`
  state and the tracking lifecycle.
- **Couples the one-shot to live-share state** (`_shares`) just to decide whether to `Stop`.
- **Heavy on Android**: `Start` spins up a foreground service + ongoing notification
  (`AndroidLocationForegroundService`) — absurd for grabbing one fix.
- **No timeout**: `When(x => x is not null)` waits forever if no fix ever arrives
  (only bounded by the caller's CT).

## Goal

Add a first-class one-shot to the tracker:

```csharp
// ILocationTracker
Task<GeoPoint?> Get(CancellationToken cancellationToken);
```

`Get()` returns the current position once, **independent of `Start`/`Stop`**, and never
disturbs an in-progress live share. `null` = no fix available (timeout / unavailable).

## Reuse (mandatory)

### Existing abstractions to reuse

| Concern | Reuse | Path |
|---|---|---|
| Tracker contract + 4 impls | `ILocationTracker` (Web/Apple/Android/Maui) | `UI.Blazor.App/Services/Location/`, `App.Maui/**/Location/`, `App.Maui/Services/` |
| Per-tracker "am I already tracking?" guard | the existing **private** `IsTracking` field in each impl | each tracker |
| Cached latest fix | `LastKnown` (`IState<GeoPoint?>`) | each tracker |
| Native one-shot (Windows/MAUI) | `Geolocation.GetLocationAsync(GeolocationRequest, ct)` | MAUI Essentials |
| Native one-shot (Web) | `navigator.geolocation.getCurrentPosition` | `LocationTracker` TS module |
| Native one-shot (iOS) | `CLLocationManager.RequestLocation()` (single delivery via delegate) | CoreLocation |
| Native one-shot (Android) | `FusedLocationProviderClient.GetCurrentLocation(...)` | Google Play Services Location |
| Accuracy setting | `settings.LocationAccuracyOrDefault` + `CLLocationManagerExt.SetAccuracy` | already used by `Start` |
| Timeout | `Constants.Location` (add `GetTimeout`, e.g. 15s) | `Api/Constants.cs` |

### Reusability of new components
- **`LocationTrackerBase`** (new abstract base, `UI.Blazor.App/Services/Location/`) — holds
  the boilerplate duplicated across all four trackers (`_lastKnown`/`LastKnown`,
  `IsTracking`, a `SetLocation` helper) and implements `Get()` **once** (the piggyback
  rule below). All four trackers inherit it. **Not** a subclass of the concrete
  `MauiGeolocationTracker` — that's a sibling tied to MAUI Essentials' `IGeolocation`,
  which Apple/Android don't use; extending it would force them to override everything and
  carry an unused field. A thin shared base avoids that.
- The only new shared constant (`Constants.Location.GetTimeout`) belongs in
  `Api/Constants.cs` next to the existing location constants.

## Design

### Shared rule: don't fight an active live share
If continuous tracking is already running (a live share is active), `Get()` should return
the **latest `LastKnown`** rather than issue a conflicting one-shot request. This matters
most on **iOS**, where `RequestLocation()` and `StartUpdatingLocation()` on the same
`CLLocationManager` don't mix. Each impl already has the private `IsTracking` flag to
branch on:

```
Get(ct):
    if (IsTracking && LastKnown.Value is { } p) return p;   // piggyback on live share
    return <platform one-shot, bounded by GetTimeout>;
```

### Per-platform one-shot

| Platform | Impl | Notes | Verifiable here |
|---|---|---|---|
| **Web** (`WebLocationTracker`) | new JS `getCurrentLocation()` → `navigator.geolocation.getCurrentPosition` wrapped in a Promise; C# `Get` invokes it | independent of the `watchPosition` used by `start` | **Yes — e2e** |
| **Windows** (`MauiGeolocationTracker`) | `await _geolocation.GetLocationAsync(new GeolocationRequest(Best, GetTimeout), ct)` | native one-shot; no listener churn | partial (build only) |
| **iOS** (`AppleLocationTracker`) | `RequestLocation()` + bridge the next `LocationsUpdated`/`Failed` to a `TaskCompletionSource`; on the main thread; `RequestWhenInUseAuthorization` first | use a **separate** `CLLocationManager` for one-shot, or short-circuit via `IsTracking`/`LastKnown` when a share is live | **No — device** |
| **Android** (`AndroidLocationTracker`) | `FusedLocationProviderClient.GetCurrentLocation(Priority, ct)` (or `LastLocation` fallback) — **no foreground service / notification** | independent of `AndroidLocationForegroundService` | **No — device** |

### `LocationUI.SendCurrentLocation` after

```csharp
public async Task SendCurrentLocation(ChatId chatId, CancellationToken cancellationToken)
{
    if (await Tracker.Get(cancellationToken).ConfigureAwait(false) is not { } point)
        return; // no fix (timeout / unavailable)

    var locationId = SharedLocationId.New();
    await Commander.Call(
            new SharedLocations_Create(Session, chatId, locationId, point, TimeSpan.Zero), cancellationToken)
        .ConfigureAwait(false);
    await Commander.Call(
            new Chats_UpsertEntry(Session, chatId, null) { LocationId = locationId }, cancellationToken)
        .ConfigureAwait(false);
}
```

Drops `Start`/`try`/`finally`-`Stop` and the `_shares` coupling. Permission is unchanged —
`ChatMessageEditor` still calls `LocationPermission.CheckOrRequest()` before this.

## Testing
- **Web**: extend the one-shot e2e (`location-sharing.test.ts > "sends current location once"`)
  — it already mocks geolocation and asserts the inline-map message; verify it passes
  against `getCurrentPosition`.
- **iOS/Android**: **device builds** — the CI slnf excludes the MAUI workload, so these
  can't be compiled or run in the server-loop/Docker env. Must be verified on device
  (one-shot send while no share active, and while a live share is active → should not
  disturb the share).

## Open questions
1. **iOS during an active share** — confirm short-circuiting via `IsTracking`/`LastKnown`
   is acceptable (returns the share's latest fix instead of a fresh `RequestLocation`), or
   use a dedicated one-shot `CLLocationManager`.
2. **Staleness** — when piggybacking on `LastKnown`, is any cached fix OK, or should we
   require it to be recent (e.g. within `GetTimeout`)? Probably fine as-is for "send my
   location now" since a live share updates frequently.
3. **`GetTimeout` value** — 15s? Align with platform defaults.
4. **Failure UX** — `SendCurrentLocation` currently silently returns on no-fix. Surface a
   toast ("Couldn't get your location")? Out of scope here; track separately.

## Rollout
1. Add `Get` to `ILocationTracker` + `Constants.Location.GetTimeout`.
2. Implement Web (+ JS) and refactor `LocationUI.SendCurrentLocation`; verify via e2e.
3. Implement Maui/iOS/Android; verify on device.
4. Remove the `// TODO: Tracker.Get() without Start/Stop???`.
