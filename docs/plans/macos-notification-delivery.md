# macOS (MacCatalyst): Deliver push notifications

## Goal
Make FCM/APNs push notifications actually arrive on the MacCatalyst app: the Mac
registers an FCM token with the server, and incoming pushes are delivered, shown,
and badge/dismissal-synced — reaching parity with iOS.

This is the larger follow-up to
[`macos-notification-permission.md`](macos-notification-permission.md), which only
wired the **permission prompt**. That plan's "Notification delivery on Mac —
feasibility" section is the origin of this one; read it first for background. Some
of its premises have since changed (see "What changed since the feasibility note").

## What changed since the feasibility note
The feasibility analysis (2026-05-29) assumed the Firebase packages were pinned at
iOS-only versions. They have since been bumped, which removes the biggest blocker:

- `AdamE.Firebase.iOS.{Core,Analytics,Crashlytics,CloudMessaging}` are now
  **12.5.0.4** (`Directory.Packages.props:77-79`) — the Catalyst-capable line. The
  native `FirebaseMessaging.xcframework` in these packages ships an
  `ios-arm64_x86_64-maccatalyst` slice.
- `AppKind.MacOS` already exists (`Core/Hosting/AppKind.cs`) and
  `AppKindExt.IsApple()` already returns `true` for it.

What is **still** true (and still blocks delivery):
1. `Plugin.Firebase.CloudMessaging` (4.0.1, `Directory.Packages.props:44`) has **no
   MacCatalyst target** — only `net9.0`, `-android`, `-ios`. iOS leans on it for
   init (`FirebaseCloudMessagingImplementation.Initialize()`) and token retrieval
   (`IFirebaseCloudMessaging.GetTokenAsync()`). **Mac must call the AdamE native
   `Firebase.*` API directly instead.**
2. The AdamE 12.x packages are referenced **only under `-ios`**
   (`App.Maui.csproj:179-188`); the `-maccatalyst` ItemGroup (`:189-191`) has none.
3. No Firebase init, no APNs registration, and no token forwarding on Mac:
   - `MacDeviceTokenRetriever.GetDeviceToken()` returns `null`.
   - `Platforms/MacCatalyst/AppDelegate.cs` is a bare `CreateMauiApp()` stub (no
     `IMessagingDelegate`, no `DidReceiveRegistrationToken`, no
     `RegisterForRemoteNotifications`).
   - `MauiProgram.MacCatalyst.cs` has no `ConfigurePlatformLifecycleEvents` body.
4. No push capability on Mac: neither `Platforms/MacCatalyst/Entitlements.{dev,prod}.plist`
   has `aps-environment`, and `Info.plist` lacks `UIBackgroundModes`
   (`remote-notification`). Both Mac entitlements set
   `com.apple.security.app-sandbox = true`, which needs the push exception.
5. No `GoogleService-Info.plist` is bundled for `-maccatalyst` (only `-ios`,
   `App.Maui.csproj`'s iOS-conditioned `BundleResource` includes).
6. `DeviceType` (`Api/Notifications/DeviceType.cs`) has no Mac value;
   `NotificationUI.GetDeviceType()` (`UI.Blazor.App/NotificationUI.cs:213-226`)
   falls through to `WebBrowser` for Mac.

## The backend needs no changes (re-confirmed)
`NotificationsBackend.OnRegisterDevice` (`Notifications.Service/NotificationsBackend.cs:249`)
only *upgrades* a `WebBrowser` row to the reported app type; it never rejects a
type. `FirebaseMessagingClient.SendMessage` fans a single `MulticastMessage`
(with both `Android` and `Apns` blocks) to a list of tokens via
`SendEachForMulticastAsync` and does **not** branch on `DeviceType`. FCM routes
Apple tokens (incl. Mac Catalyst) through the `Apns` block. The Mac shares the iOS
bundle ID and team, so the existing APNs auth key already covers it. ⇒ Once a Mac
registers an FCM token, pushes flow through the exact iOS path with **zero
server / Firebase-config changes**.

## Key architectural decision: AdamE-direct, not Plugin.Firebase
Because `Plugin.Firebase` has no Catalyst slice, the Mac cannot reuse
`IosPushNotifications.GetDeviceToken()` (which calls `IFirebaseCloudMessaging`) or
the `CrossFirebase.Initialize()` / `FirebaseCloudMessagingImplementation.Initialize()`
init path. Instead, Mac uses the **AdamE native binding** (`Firebase.Core`,
`Firebase.CloudMessaging`, namespace `Firebase.CloudMessaging`) directly.

This is not new ground: the **iOS `AppDelegate` already uses the AdamE native API
directly** — it implements `IMessagingDelegate` and
`[Export("messaging:didReceiveRegistrationToken:")] DidReceiveRegistrationToken(
Firebase.CloudMessaging.Messaging, string)` (`Platforms/iOS/AppDelegate.cs:77-89`).
Only iOS *init* and *pull-token* go through Plugin.Firebase. So the Mac
AppDelegate-side code is a near-clone of iOS's; the only genuinely Mac-specific
additions are explicit Firebase init + explicit APNs registration (which
Plugin.Firebase does implicitly on iOS).

> ⚠️ **API-surface caveat:** the exact AdamE 12.5.0.4 member names
> (`Firebase.Core.App.Configure()`, `Messaging.SharedInstance`, `.ApnsToken`,
> `.FcmToken` / `FetchToken*Async`, `.Delegate`, `IMessagingDelegate`) must be
> verified against the actual binding during implementation — bind-generated names
> sometimes differ from the Swift/ObjC originals. Step 0 below pins them down before
> any other code is written.

## Reuse (mandatory section per CLAUDE.md)

### 1. Existing abstractions to reuse
- `MauiNotifications.RefreshNotificationToken(token, DeviceType, ct)`
  (`App.Maui/Services/MauiNotifications.cs:19-29`) — the exact server-registration
  call iOS uses from `DidReceiveRegistrationToken`. Reused verbatim, only the
  `DeviceType` argument differs (`MacOSApp`).
- `IDeviceTokenRetriever` (`UI.Blazor.App/IDeviceTokenRetriever.cs`) — the existing
  contract; we replace the Mac stub's body, not the interface. `NotificationUI.RegisterDevice`
  (`UI.Blazor.App/NotificationUI.cs:157-227`) already pulls the token through it.
- `NotificationUI.GetDeviceType()` (`:213-226`) — extended with one `AppKind.MacOS`
  case; the `IsMauiApp()` switch is already there.
- `AppKind.MacOS` + `AppKindExt.IsApple()` (`Core/Hosting/AppKind*.cs`) — already
  present; no change.
- The iOS `AppDelegate` push members (`Platforms/iOS/AppDelegate.cs:55-103`):
  `DidReceiveRegistrationToken`, the silent-dismissal `DidReceiveRemoteNotification`
  handler, and `RemoveDeliveredNotifications` — all AdamE-based and platform-shareable.
- `IDeviceNotifications` / `IosDeviceNotifications` prune logic
  (`Platforms/iOS/IosDeviceNotifications.cs`) — see Step 6 (optional parity).
- The `MaciOS/` compile convention (`App.Maui.csproj:611-615`): files under
  `MaciOS/**` compile for **both** `-ios` and `-maccatalyst`. This is where any code
  shared between the two AppDelegates / token retrievers belongs (same home as the
  already-shared `AppleNotificationsPermission`).
- The iOS `GoogleService-Info.plist.{dev,prod}` bundle resources — the Mac shares
  the bundle ID, so the **same** files are bundled for `-maccatalyst` (no new asset).

### 2. Reusability of new components
- **Shared Apple push core** (the `IMessagingDelegate` token-forwarding + silent-push
  + delivered-notification pruning). This is genuinely shared by iOS and Mac, so it
  belongs in `App.Maui/MaciOS/` (e.g. an `AppleAppDelegateBase : MauiUIApplicationDelegate,
  IMessagingDelegate` or a shared static helper), **not** under either platform
  folder. It is MAUI/Apple-specific (depends on `Firebase.CloudMessaging`, `UIKit`,
  `UserNotifications`), so it does **not** go in `ActualChat.Core`. Recommendation:
  factor the shared members into `App.Maui/MaciOS/` and have both
  `Platforms/{iOS,MacCatalyst}/AppDelegate.cs` derive from / delegate to it.
- **`MacDeviceTokenRetriever`** stays Mac-only (it's the AdamE-direct counterpart of
  `IosPushNotifications`'s Plugin.Firebase token pull). No other project can use it.
- **`DeviceType.MacOSApp`** is added to the shared `Api` enum — inherently shared.

## Implementation Plan

### Step 0 — Pin the AdamE 12.5.0.4 API surface (spike, no commit)
Before writing wiring, confirm the real binding member names by referencing
`AdamE.Firebase.iOS.CloudMessaging` 12.5.0.4 from a `-maccatalyst` scratch build and
inspecting the generated API (object browser / `ApiDiff`, or the package's
`*.xml`). Specifically confirm: `Firebase.Core.App.Configure()`, the messaging
singleton accessor (`Messaging.SharedInstance`), the delegate property and
`IMessagingDelegate` shape, `ApnsToken` setter type (`NSData`), and the
token-fetch API (`FcmToken` property vs `FetchToken(Async)`). Adjust later steps to
the actual names.

**Confirmed (against 12.5.0.4 `net10.0-maccatalyst26.0` via `monodis`):**
- `Firebase.Core.App.Configure()` — static, parameterless. ✓
- `Firebase.CloudMessaging.Messaging.SharedInstance` — static accessor. ✓
- `Messaging.ApnsToken` — `NSData` get/set property. ✓
- `Messaging.Delegate` — `IMessagingDelegate` get/set property. ✓
- `Messaging.FcmToken` — `string` get property; `Messaging.FetchTokenAsync()` → `Task<string>`. ✓
- `Messaging.AutoInitEnabled` — `bool` get/set. ✓
- `IMessagingDelegate.DidReceiveRegistrationToken(Messaging, string)` (Export
  `messaging:didReceiveRegistrationToken:`) — matches the iOS AppDelegate already. ✓

The package ships `net9.0-maccatalyst18.0` and `net10.0-maccatalyst26.0` slices, so
Catalyst is fully supported.

### Step 1 — Reference AdamE Firebase for MacCatalyst
In `App.Maui.csproj`, add to the `-maccatalyst` ItemGroup (`:189-191`):
```xml
<PackageReference Include="AdamE.Firebase.iOS.Core" />
<PackageReference Include="AdamE.Firebase.iOS.CloudMessaging" />
<!-- Analytics/Crashlytics only if we want them on Mac; not required for delivery -->
```
Do **not** add `Plugin.Firebase.*` here — no Catalyst target. Versions come from
`Directory.Packages.props` (already 12.5.0.4).

### Step 2 — Bundle GoogleService-Info.plist for MacCatalyst
Mirror the iOS-conditioned `BundleResource` includes for `-maccatalyst`, reusing the
**same** `Platforms/iOS/GoogleService-Info.plist.{dev,prod}` files (shared bundle ID):
```xml
<ItemGroup Condition="'$(IsDevMaui)' == 'true' AND $(TargetFramework.Contains('-maccatalyst'))">
  <BundleResource Include="Platforms\iOS\GoogleService-Info.plist.dev" Link="GoogleService-Info.plist" />
</ItemGroup>
<ItemGroup Condition="'$(IsDevMaui)' != 'true' AND $(TargetFramework.Contains('-maccatalyst'))">
  <BundleResource Include="Platforms\iOS\GoogleService-Info.plist.prod" Link="GoogleService-Info.plist" />
</ItemGroup>
```

### Step 3 — Entitlements + Info.plist (Apple capability)
- Add `aps-environment` (`development` / `production`) to both
  `Platforms/MacCatalyst/Entitlements.{dev,prod}.plist`.
- Because the Mac app is sandboxed, add the sandbox push exception
  (`com.apple.security.application-groups` is unrelated; the relevant key is
  `aps-environment` itself — sandbox allows APNs once the entitlement is present;
  verify on-device).
- Add `UIBackgroundModes` → `remote-notification` to
  `Platforms/MacCatalyst/Info.plist` (mirrors iOS `Info.plist`), so
  content-available (silent dismissal) pushes are delivered in the background.
- **Apple portal:** ensure Push Notifications is enabled on the shared App ID
  (`chat.actual[.dev].app`, team `M287G8G83F`). Likely already enabled for iOS —
  confirm, don't assume.

### Step 4 — Initialize Firebase + register for APNs on Mac
In `MauiProgram.MacCatalyst.cs`, implement `ConfigurePlatformLifecycleEvents` to run
on launch (MacCatalyst lifecycle), calling the AdamE init directly (the
Plugin.Firebase `CrossFirebase.Initialize()` iOS uses is unavailable):
```csharp
private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
    => events.AddiOS(ios => ios.FinishedLaunching((app, options) => {
        Firebase.Core.App.Configure();
        UIApplication.SharedApplication.RegisterForRemoteNotifications();
        return false;
    }));
```
(`AddiOS` is the MacCatalyst lifecycle hook in MAUI too. Confirm in Step 0/4 that
`Configure()` picks up the bundled `GoogleService-Info.plist`.)

### Step 5 — Mac AppDelegate: messaging delegate, APNs token, FCM token forwarding
Extend `Platforms/MacCatalyst/AppDelegate.cs` to mirror the iOS one
(`Platforms/iOS/AppDelegate.cs:55-103`). Prefer factoring the shared members into a
`App.Maui/MaciOS/` base (see Reuse §2) and deriving both platforms from it; if that
refactor is deferred, clone:
- implement `IMessagingDelegate`;
- `RegisteredForRemoteNotifications(UIApplication, NSData deviceToken)` →
  `Messaging.SharedInstance.ApnsToken = deviceToken;`
- `FailedToRegisterForRemoteNotifications(...)` → log;
- `[Export("messaging:didReceiveRegistrationToken:")] DidReceiveRegistrationToken(...)`
  → `MauiNotifications.RefreshNotificationToken(token, DeviceType.MacOSApp, ...)`
  (identical to iOS except the `DeviceType`);
- the silent-dismissal `DidReceiveRemoteNotification` + `RemoveDeliveredNotifications`
  handler (verbatim from iOS).

### Step 6 — Mac token retriever (pull path)
Replace `MacDeviceTokenRetriever.GetDeviceToken()`'s `null` with the AdamE token
fetch (e.g. `await Messaging.SharedInstance.FetchToken*Async()` or read `FcmToken`),
so `NotificationUI.RegisterDevice` registers the device even when the push callback
hasn't fired yet. Keep `DeleteDeviceToken` as-is (same "no native delete API" caveat
as iOS, `IosPushNotifications.cs:28-30`). Then register it (it already is) in
`MauiProgram.MacCatalyst.cs`.
Optionally also register an `IDeviceNotifications` for Mac (an `AppleDeviceNotifications`
shared with iOS's prune-only impl) so the reconciler prunes delivered banners; iOS's
`IosDeviceNotifications` is prune-only and `UNUserNotificationCenter`-based, so it's
Mac-compatible. Low priority — the reconciler no-ops without it.

### Step 7 — DeviceType + device-type mapping
- Add `MacOSApp = 4` to `DeviceType` (`Api/Notifications/DeviceType.cs`). Appending a
  new int value is serialization/DB-safe (existing rows unaffected; server doesn't
  branch on type for sending).
- Add `case AppKind.MacOS: return DeviceType.MacOSApp;` to
  `NotificationUI.GetDeviceType()` (`UI.Blazor.App/NotificationUI.cs:213-226`).
This is for correctness/analytics — delivery does not depend on it.

## Open questions (decide during implementation)
1. **Shared AppDelegate refactor now, or clone-then-refactor?** Factoring the iOS
   push members into `MaciOS/` is the CLAUDE.md-preferred path (no duplication), but
   it touches the working iOS AppDelegate. Recommendation: do the refactor (Step 5),
   since the code is already AdamE-based and identical; if it destabilizes iOS,
   fall back to a clone and file a follow-up.
2. **Drop Plugin.Firebase on iOS too?** Out of scope. iOS works today; converging
   iOS onto AdamE-direct (to delete the Plugin.Firebase dependency entirely) is a
   separate cleanup.
3. **`dev` vs `prod` `aps-environment`** must track the `IsDevMaui` split exactly as
   iOS does; getting this wrong yields silent no-delivery (tokens register against
   the wrong APNs environment).

## Verification
- Build `-maccatalyst` locally with the full `.sln` (MAUI workload; not in the CI
  solution filter).
- Run the Mac app, sign in, grant permission (from the permission-plan work), and
  confirm in logs: APNs `RegisteredForRemoteNotifications` → `DidReceiveRegistrationToken`
  with a non-empty FCM token → `MauiNotifications.RefreshNotificationToken` →
  `Notifications_RegisterDevice` succeeds (device row gets `MacOSApp`).
- Send a test push (message in a chat from another account) → banner appears on Mac;
  tap navigates via `OnNotificationTapped`/app-link path.
- Badge + silent dismissal: read on another device → the Mac banner is pruned
  (validates the silent `DidReceiveRemoteNotification` path).
- Regression-check iOS end-to-end if the Step 5 shared-AppDelegate refactor lands.

## Files touched
- `Directory.Packages.props` — (already 12.5.0.4; no change unless a newer pin is wanted).
- `App.Maui/App.Maui.csproj` — add AdamE refs + GoogleService-Info bundling for `-maccatalyst`.
- `App.Maui/Platforms/MacCatalyst/Entitlements.{dev,prod}.plist` — add `aps-environment`.
- `App.Maui/Platforms/MacCatalyst/Info.plist` — add `UIBackgroundModes`/`remote-notification`.
- `App.Maui/MauiProgram.MacCatalyst.cs` — Firebase init + APNs registration in lifecycle.
- `App.Maui/Platforms/MacCatalyst/AppDelegate.cs` — messaging delegate + token forwarding.
- `App.Maui/Platforms/MacCatalyst/MacDeviceTokenRetriever.cs` — real AdamE token fetch.
- `App.Maui/MaciOS/` — **new** shared Apple push core (if Step 5 refactor lands);
  possibly `AppleDeviceNotifications.cs` (Step 6, optional).
- `App.Maui/Platforms/iOS/AppDelegate.cs` — only if the shared refactor lands.
- `Api/Notifications/DeviceType.cs` — add `MacOSApp`.
- `UI.Blazor.App/NotificationUI.cs` — map `AppKind.MacOS` → `DeviceType.MacOSApp`.
- **Apple Developer portal** — one-time Push capability confirm on the App ID.
