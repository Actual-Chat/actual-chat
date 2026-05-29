# macOS (MacCatalyst): Wire up the notification-permission "Configure" button

## Goal
Make the "Configure" button in the `NotificationsPermissionBanner` actually do
something on the MacCatalyst app: trigger the OS permission prompt, reflect the
real permission state, and hide the banner once granted — reusing the iOS
implementation instead of duplicating it.

## Problem (root cause)
Clicking **Configure** calls `INotificationsPermission.Request()`
(`UI.Blazor.App/Components/Banners/NotificationsPermissionBanner.razor:83`).
On MacCatalyst this resolves to a stub:

```csharp
// App.Maui/Platforms/MacCatalyst/MacNotificationsPermission.cs:10-11
public Task Request(CancellationToken cancellationToken = default)
    => Task.CompletedTask;   // no-op → nothing happens
```

`MacNotificationsPermission.IsGranted()` also hardcodes `false`, so the banner
is *always* visible (it shows whenever `IsGranted is not true`, see
`NotificationsPermissionBanner.razor:10`).

The stub was intentional — `MauiProgram.MacCatalyst.cs:17` notes *"Push
notifications are not wired up on MacCatalyst yet (no Firebase native
bindings)."*

## Key insight
The permission logic does **not** depend on Firebase. The iOS implementation
(`App.Maui/Platforms/iOS/IosPushNotifications.cs:38-76`) implements
`IsGranted`/`Request` using only the `UserNotifications` framework
(`UNUserNotificationCenter`), which **is available on MacCatalyst**. Only the
*device-token / FCM* parts of `IosPushNotifications` (`IDeviceTokenRetriever`,
`Messaging.NotificationTapped/Received`) are Firebase-coupled.

`SystemSettingsUI.Open()` (used by the iOS flow when permission is denied) is
already cross-platform: `MauiSystemSettingsUI` is registered for all MAUI
platforms (`App.Maui/Module/MauiAppModule.cs:45`) and calls
`AppInfo.Current.ShowSettingsUI()`, which works on Mac.

## Caveat — read before implementing
Even with a working `Request()`, **push notifications will not be delivered on
Mac** yet: `MacDeviceTokenRetriever.GetDeviceToken()` returns `null` (no FCM),
so `NotificationUI.RegisterDevice` (`UI.Blazor.App/NotificationUI.cs:157-211`)
logs *"Failed to get notification device token"* and never registers the device
with the server.

What this plan *does* deliver:
- The macOS permission prompt appears when the user clicks Configure.
- `IsGranted` reflects the real OS state, so the banner correctly hides once
  granted (and System Settings opens if previously denied).

What it does **not** deliver: actual push delivery to the Mac (needs FCM/APNs
wiring — a separate, larger task). Because of this, the banner's copy ("Voxt
can notify you about new messages") is arguably premature on Mac. See
"Open question" below, and "Notification delivery on Mac" for the full
feasibility analysis.

## Notification delivery on Mac — feasibility (investigated 2026-05-29)

This is the harder, separate problem (out of scope for the permission fix above,
documented here so it isn't re-investigated). Three blockers exist today:

1. **Firebase bindings are iOS-only at the pinned versions.**
   `Plugin.Firebase.CloudMessaging` / `AdamE.Firebase.iOS.*` are referenced only
   under `-ios` and excluded from `-maccatalyst` (`App.Maui.csproj:178-193`).
   That's why `MacDeviceTokenRetriever.GetDeviceToken()` returns `null`, so
   `NotificationUI.RegisterDevice` never registers the device with the server.
2. **No push capability in Mac entitlements.** Neither
   `Platforms/MacCatalyst/Entitlements.*.plist` has `aps-environment`, and both
   set `com.apple.security.app-sandbox` = true. A sandboxed Mac app needs the
   Push Notifications capability to register with APNs.
3. **The Mac `AppDelegate` is a bare stub.** `Platforms/MacCatalyst/AppDelegate.cs`
   only calls `CreateMauiApp()` — no `IMessagingDelegate`, no
   `DidReceiveRegistrationToken`, no APNs/badge registration. The iOS one
   (`Platforms/iOS/AppDelegate.cs:55-83`) does all of that.

### Package-support findings (verified against the local NuGet cache + nuget.org)

The native Firebase SDK **does** support Mac Catalyst now, but the cross-platform
wrapper the code actually calls (`Plugin.Firebase`) does **not** — that's the
real gate.

| Package | Pinned (`Directory.Packages.props`) | Mac Catalyst slice? | Catalyst-capable version |
|---|---|---|---|
| `AdamE.Firebase.iOS.Core` | 11.10.0 | **No** — xcframework has only `ios-arm64` + simulator | **12.5.0.4+** (`net9.0-maccatalyst18.0`; native `FirebaseCore.xcframework/ios-arm64_x86_64-maccatalyst`) |
| `AdamE.Firebase.iOS.CloudMessaging` | 11.10.0 | **No** | **12.5.0.4+** (native `FirebaseMessaging.xcframework/ios-arm64_x86_64-maccatalyst` confirmed; latest published 12.10.0) |
| `Plugin.Firebase.CloudMessaging` | 3.1.2 | **No** | **None** — latest published (4.0.1) ships only `net9.0`, `-android35.0`, `-ios18.0` |
| `Plugin.Firebase.Core` | 3.1.1 | **No** | **None** — latest (4.1.0) same |

Verification method: unzipped the native `*.resources.zip` inside each package's
`lib/net9.0-maccatalyst18.0/` folder and listed the `*.xcframework` slices;
cross-checked latest published versions via the nuget.org flat-container API.

Key point: `IosPushNotifications` retrieves the token via **`Plugin.Firebase`'s**
`IFirebaseCloudMessaging`, not AdamE directly. Since `Plugin.Firebase` has no
Catalyst target in any release, the iOS *delivery* code cannot be reused
verbatim.

### The backend needs no changes

`FirebaseMessagingClient.SendMessage` (`Notification.Service/FirebaseMessagingClient.cs:62-118`)
sends a single FCM `MulticastMessage` to a list of device **tokens**, carrying
both an `Android` and an `Apns` config block, via `SendEachForMulticastAsync`.
FCM routes each token to the right transport — Apple tokens (incl. Mac Catalyst)
go out through the `Apns` block. Sending does **not** branch on `DeviceType`
(the only `DeviceType` check is a WebBrowser guard at registration,
`NotificationsBackend.cs:294`). The Mac shares the iOS bundle ID and team
(`application-identifier` `chat.actual[.dev].app`, team `M287G8G83F`), so the
Firebase project's existing APNs auth key already covers it.

⇒ A Mac that registers an **FCM token** receives pushes through the exact path
already used for iOS, with **zero server / Firebase-config changes**. All
remaining work is client-side + a one-time Apple capability toggle.

### What must be done for Mac push (all client-side)

1. **Packages** — bump `AdamE.Firebase.iOS.*` to 12.x (Catalyst-capable) in
   `Directory.Packages.props`, and reference `AdamE.Firebase.iOS.CloudMessaging`
   (+ Core/Analytics as needed) for `-maccatalyst` in `App.Maui.csproj` (today
   excluded, `:188-193`). Do **not** rely on `Plugin.Firebase` here — it has no
   Catalyst target, so the Mac token retriever must call the AdamE
   `Firebase.CloudMessaging.Messaging` API directly.
2. **Token retriever** — replace the `MacDeviceTokenRetriever` null-stub with a
   real implementation that returns the FCM token via the AdamE `Messaging` API,
   so `NotificationUI.RegisterDevice` registers the device with the server.
3. **Entitlements** — add `aps-environment` and the sandbox push exception to
   both `Platforms/MacCatalyst/Entitlements.{dev,prod}.plist` (both currently set
   `com.apple.security.app-sandbox` = true with no push entitlement).
4. **AppDelegate wiring** — extend the Mac `Platforms/MacCatalyst/AppDelegate.cs`
   (today a bare `CreateMauiApp()` stub) to mirror the iOS one
   (`Platforms/iOS/AppDelegate.cs:55-83`): `IMessagingDelegate`,
   `DidReceiveRegistrationToken`, APNs/remote-notification registration, badge
   registration.
5. **Device type** — add a Mac value to the `DeviceType` enum
   (today `iOSApp/AndroidApp/WindowsApp/WebBrowser`) and a corresponding
   `AppKind.MacOS` case in `NotificationUI.GetDeviceType()` (currently falls back
   to `WebBrowser` for Mac). Sending doesn't branch on type, so this is for
   correctness/analytics rather than delivery.
6. **Apple portal** — enable the Push Notifications capability on the (shared)
   App ID; likely already enabled because iOS uses it.

Steps 1–5 are code; step 6 is a one-time portal toggle. None touch the server.
This is a distinct, larger effort from the permission fix in this plan.

## Reuse (mandatory section per CLAUDE.md)

### 1. Existing abstractions to reuse
- `INotificationsPermission` (`UI.Blazor.App/INotificationPermissions.cs`) — the
  interface already consumed by the banner; no change.
- `UNUserNotificationCenter` (Apple `UserNotifications` framework) — the same
  API iOS already uses; available on MacCatalyst, no Firebase dependency.
- `SystemSettingsUI` / `MauiSystemSettingsUI`
  (`Core/UI/SystemSettingsUI.cs`, `App.Maui/Services/MauiSystemSettingsUI.cs`) —
  reused as-is for the "denied → open settings" path; already works on Mac.
- `NotificationUI.SetIsGranted` (`UI.Blazor.App/NotificationUI.cs:87`) — reused
  to push the resolved state back into Fusion so the banner reacts.
- `UIServiceBase<AppUIHub>` / `ForegroundTask.Run` — base class + helper the iOS
  and Android implementations already use; reused for the new shared class.
- The existing `MaciOS/` compile convention
  (`App.Maui/App.Maui.csproj:631-632`): files under `MaciOS/**` compile for
  **both** `-ios` and `-maccatalyst` TFMs. This is the established home for
  shared Apple code (see commit "move shared iOS code to MaciOS for
  iOS+MacCatalyst").

### 2. Reusability of new components
The only new component is `AppleNotificationsPermission` — the shared
`INotificationsPermission` implementation. It is genuinely shared across iOS +
MacCatalyst, so it belongs in `App.Maui/MaciOS/`, **not** under a single
platform's folder. It is MAUI/Apple-specific (depends on `UserNotifications` and
the MAUI hub), so it does not belong in `ActualChat.Core` — `App.Maui/MaciOS/`
is the correct shared home. No other project can use it.

## Implementation Plan

### Step 1 — Add the shared implementation
New file `App.Maui/MaciOS/AppleNotificationsPermission.cs`, implementing
`INotificationsPermission` with the `IsGranted`/`Request` bodies lifted verbatim
from `IosPushNotifications` (lines 38-76). It depends only on
`UNUserNotificationCenter`, `NotificationUI`, `SystemSettingsUI`, and
`UIDevice` — all available on both TFMs.

```csharp
using ActualChat.UI;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using UIKit;
using UserNotifications;

namespace ActualChat.App.Maui;

public class AppleNotificationsPermission(AppUIHub hub)
    : UIServiceBase<AppUIHub>(hub), INotificationsPermission
{
    private NotificationUI NotificationUI => Hub.NotificationUI;
    private SystemSettingsUI SystemSettingsUI
        => field ??= Hub.Services.GetRequiredService<SystemSettingsUI>();
    private static UNUserNotificationCenter NotificationCenter
        => UNUserNotificationCenter.Current;

    public async Task<bool?> IsGranted(CancellationToken cancellationToken = default)
    {
        var settings = await NotificationCenter.GetNotificationSettingsAsync().ConfigureAwait(false);
        return settings.AuthorizationStatus switch {
            UNAuthorizationStatus.NotDetermined => null,
            UNAuthorizationStatus.Authorized => true,
            UNAuthorizationStatus.Provisional => true,
            UNAuthorizationStatus.Ephemeral => true,
            _ => false,
        };
    }

    public Task Request(CancellationToken cancellationToken = default)
        => ForegroundTask.Run(async () => {
            var isGranted = await IsGranted(cancellationToken).ConfigureAwait(true);
            if (isGranted == true) {
                NotificationUI.SetIsGranted(isGranted);
                return;
            }
            var options = UNAuthorizationOptions.Alert
                | UNAuthorizationOptions.Badge
                | UNAuthorizationOptions.Sound;
            var (result, error) = await NotificationCenter.RequestAuthorizationAsync(options).ConfigureAwait(true);
            if (result)
                Log.LogInformation("NotificationCenter.RequestAuthorizationAsync: granted");
            else
                Log.LogWarning("NotificationCenter.RequestAuthorizationAsync: denied, {Error}", error);
            isGranted = await IsGranted(cancellationToken).ConfigureAwait(true);
            if (isGranted == false)
                await SystemSettingsUI.Open().ConfigureAwait(true);
            NotificationUI.SetIsGranted(isGranted);
        }, Log, "Notifications permission request failed", cancellationToken);
}
```

(The iOS-10 `UIDevice.CheckSystemVersion(10, 0)` guard from the iOS version is
dropped — it is always true on every supported iOS and MacCatalyst target.)

### Step 2 — Slim down `IosPushNotifications`
`App.Maui/Platforms/iOS/IosPushNotifications.cs` keeps `IDeviceTokenRetriever`
+ the FCM event wiring (`NotificationTapped`/`NotificationReceived`) but **drops
`INotificationsPermission`** and the `IsGranted`/`Request`/`SystemSettingsUI`
members now living in the shared class. Remove the now-unused
`using UserNotifications;` only if nothing else needs it (note
`OnNotificationReceived` still uses `UNUserNotificationCenter.SetBadgeCount`, so
the using likely stays).

### Step 3 — Update DI registrations
- iOS (`App.Maui/MauiProgram.iOS.cs:26-28`): keep
  `IosPushNotifications` registered for `IDeviceTokenRetriever`; register
  `INotificationsPermission` → `AppleNotificationsPermission` instead of
  `IosPushNotifications`.
  ```csharp
  services.AddScoped<IosPushNotifications>(c => new IosPushNotifications(c.AppUIHub()));
  services.AddTransient<IDeviceTokenRetriever>(c => c.GetRequiredService<IosPushNotifications>());
  services.AddScoped<INotificationsPermission>(c => new AppleNotificationsPermission(c.AppUIHub()));
  ```
- MacCatalyst (`App.Maui/MauiProgram.MacCatalyst.cs:19`): register
  `INotificationsPermission` → `AppleNotificationsPermission`.
  ```csharp
  services.AddScoped<INotificationsPermission>(c => new AppleNotificationsPermission(c.AppUIHub()));
  ```

### Step 4 — Delete the stub
Remove `App.Maui/Platforms/MacCatalyst/MacNotificationsPermission.cs`.
(`MacDeviceTokenRetriever` stays — FCM is still absent on Mac.)

### Step 5 — Entitlements / Info.plist check
Confirm `Platforms/MacCatalyst/Entitlements.{dev,prod}.plist` and `Info.plist`
allow the notification prompt. `UNUserNotificationCenter.RequestAuthorization`
itself does not require the `aps-environment` (push) entitlement — that's only
needed for *remote* APNs registration — so local prompting should work without
it. Verify on-device; if the prompt is suppressed, this step expands.

## Open question (decide before/while implementing)
Since push delivery doesn't work on Mac yet, do we want the banner to appear at
all? Options:
- **(a)** Ship as-is — the prompt works, the banner hides once granted, delivery
  comes later. Simplest; mild risk of user confusion (granted but no pushes).
- **(b)** Suppress the banner on `AppKind.MacOS` until push is wired up (via a
  special-case in `NotificationsPermissionBanner.razor` `ComputeState`), and
  defer the permission work entirely.

Recommendation: **(a)** if we want the permission groundwork in now; **(b)** if
we'd rather not surface a half-working feature. This plan implements (a).

## Verification
- Build the MacCatalyst target (requires MAUI workload — not in the CI solution
  filter; build locally with the full `.sln`).
- Run the Mac app, sign in, observe the banner, click **Configure** → macOS
  permission prompt should appear.
- Grant → banner disappears (state flows through `NotificationUI.SetIsGranted`).
- Re-open after denying once → clicking Configure opens System Settings.
- Regression-check iOS: prompt + banner behavior unchanged after the split.

## Files touched
- `App.Maui/MaciOS/AppleNotificationsPermission.cs` — **new** (shared).
- `App.Maui/Platforms/iOS/IosPushNotifications.cs` — drop permission members.
- `App.Maui/MauiProgram.iOS.cs` — re-point `INotificationsPermission` reg.
- `App.Maui/MauiProgram.MacCatalyst.cs` — re-point `INotificationsPermission` reg.
- `App.Maui/Platforms/MacCatalyst/MacNotificationsPermission.cs` — **delete**.
- (maybe) `App.Maui/Platforms/MacCatalyst/Entitlements.*.plist` — only if Step 5
  finds the prompt is blocked.
