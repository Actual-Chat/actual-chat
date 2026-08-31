# VoxtNotificationService — iOS chat icons in push notifications

A `UNNotificationServiceExtension` (`.appex`) that rewrites every chat push into an iOS
**communication notification**: the chat or author avatar replaces the app icon on the banner,
and the chat's own name is the headline — the same shape Android's `MessagingStyle` renders,
and the one Telegram and WhatsApp use. The author of each message is named by its body line,
not by the title.

## Why the extension has to exist

The server has always sent everything needed for this: `FirebaseMessagingClient` sets
`aps.mutable-content = 1`, puts the absolute icon URL in the `icon` data key, and also passes
it as `fcm_options.image`. But `mutable-content` only means *"an extension is allowed to
rewrite this notification"* — with no service extension in the bundle, nothing downloads the
image and iOS renders the plain app-icon banner. That was issue #4184.

## Why Swift and not .NET

Unlike the share extension (`src/dotnet/App.Maui.IosShareExt`), a notification service
extension runs under a hard **24 MB** memory limit — well below what the managed runtime
needs — and the whole job is ~150 lines of `URLSession` plus `Intents`. So it follows
`../VoxtActivities` instead: an xcodegen-generated Xcode project, built by `build.sh` from
`App.Maui.csproj` and embedded via `AdditionalAppExtensions`.

## What it does, per push

1. Reads the `icon` data key. No icon → deliver the push untouched.
2. Downloads it (5s timeout, 512 KB cap, HTTP cache honoured — the extension process is
   reused, so a chatty conversation's avatar is normally already cached).
3. Reads the `groupTitle` data key, falling back to `senderName` — a peer chat carries no
   group title. Both are empty for a notification composed before the server sent them, and
   then the banner is delivered as the server titled it. The title is never split back apart:
   a real name or chat title can contain `" @ "` (`Design @ Voxt` would split into sender
   "Design", group "Voxt").
4. Builds an `INSendMessageIntent` whose sender is the *chat* — `groupTitle` when there is
   one, the other party otherwise — carrying the avatar as an `INImage`, donates the
   interaction (so Focus can allow-list the chat), and returns `content.updating(from: intent)`.

**Nothing sets `speakableGroupName`, deliberately.** iOS renders it as a subtitle *under* the
sender's name, giving a two-line header (sender, then chat) — and it only renders it at all for
a conversation iOS considers a group, which nothing but `recipients.count > 1` makes it. So
with a sender-named title it silently vanished, which was issue #4305. Naming the banner after
the chat drops the second line and the group-classification rule along with it: the avatar is
the chat's picture, so the chat's name is what belongs beside it.

**`conversationIdentifier` must equal `content.threadIdentifier`.** `updating(from:)` rewrites
the thread id from the conversation id, and `AppDelegate.RemoveDeliveredNotifications` matches
delivered banners on their thread id when a silent dismissal push arrives — so a different
value would silently break dismissal and per-chat grouping.

If `updating(from:)` fails — which is what a missing entitlement looks like — the banner is
delivered titled with the chat but without the avatar, not a crash.

## Prerequisites

- macOS with Xcode (iOS 16.4+ SDK).
- `brew install xcodegen`.

`VoxtNotificationService.xcodeproj` is generated from `project.yml` and gitignored; `build.sh`
regenerates it whenever it's missing or stale.

## Building

The MAUI iOS build does it: `App.Maui.csproj` runs `BuildVoxtNotificationService` (hooked via
`MaciOSPrepareForBuildDependsOn`, macOS + `-ios` only), which invokes `build.sh`. The product
is consumed as an `AdditionalAppExtensions` item.

```sh
./build.sh [CONFIG] [SDK] [BUNDLE_ID] [SHORT_VERSION] [BUILD_VERSION] [DEVELOPMENT_TEAM] \
           [IDENTITY] [PROFILE] [ENTITLEMENTS]
./build.sh                        # Release / iphoneos / dev bundle id, unsigned
./build.sh Debug iphonesimulator
```

Output: `build/$CONFIG-$SDK/VoxtNotificationService.appex`.

Signing works as for the widget — see `../appex-build.sh`, which both projects share, and
`../VoxtActivities/README.md#signing` for the reasoning. Short version: unsigned by default; a
device or TestFlight build needs a team id here, because the .NET SDK re-signs the embedded
appex but never gives it an `embedded.mobileprovision`.

Two things differ from the widget, both because this appex actually carries an entitlement:

- **The SDK's re-sign drops entitlements unless you ask it not to.** An
  `AdditionalAppExtensions` item is re-signed with whatever `CodesignEntitlements` metadata it
  has — with none, the appex ships with an empty entitlement set, the build stays green, and
  `updating(from:)` fails at runtime. `App.Maui.csproj` sets that metadata to the
  flavor-appropriate plist. The symptom if it regresses is the middle row of the table below.
- **A device build needs the appex's own profile embedded**, since its `application-identifier`
  is `…app.notification`, which the app's profile doesn't cover. `App.Maui.csproj` defaults the
  team/identity/profile for Debug `iphoneos` builds so `/ios-run` works with no extra
  arguments; CI passes its own via the `VoxtNotificationService*` properties.

## Apple Developer portal setup

Unlike the widget, this extension carries a real capability —
`com.apple.developer.usernotifications.communication` — and it has to be enabled on the App ID
behind every profile that signs the appex *and* on the host app's App ID. Set up on
2026-08-27:

| What | Dev | Prod |
|---|---|---|
| Extension App ID | `chat.actual.dev.app.notification` | `chat.actual.app.notification` |
| App Store profile | `App Store Notification Dev` | `App Store Notification` |
| Development profile | `chat.actual.dev.app.notification` | — (nobody builds prod-flavor Debug) |

Nothing on the app side needed changing: `chat.actual.dev.app` and `chat.actual.app` already
had Communication Notifications enabled, and `App Store Dev`, `App Store 2` and the
`chat.actual.dev.app` development profile already carried the entitlement.

The two App Store profiles are what the repo secrets
`PROVISIONING_PROFILE_NOTIFICATION_DEV_BASE64` / `PROVISIONING_PROFILE_NOTIFICATION_PROD_BASE64`
hold, base64 of the `.mobileprovision`.

Registering an App ID takes an **Admin**-role App Store Connect API key — an App Manager key
can create profiles but gets a 403 on `POST /v1/bundleIds`, so new App IDs are a web-UI job.

If the entitlement ever goes missing, banners keep arriving without an avatar rather than
failing.

## Testing it

**This can only be tested on a device.** Two independent reasons:

- `xcrun simctl push` never runs a service extension. It hands the payload straight to
  SpringBoard, which adds the notification request as-is — the log shows the banner delivered
  with its original `thread-id` and `Notification serviced by the communication context
  service: 0`, and no extension process is ever spawned.
- A simulator build is ad-hoc signed with an empty entitlement set, so
  `updating(from:)` would fail there even if the extension did run.

So: build to a device (`/ios-run`), background the app, and send yourself a message from
another account — a second browser session on dev.voxt.ai is the quickest. Verified working on
device 2026-08-27.

**If a simulator build came first, wipe `artifacts/out` before building to a device.** Every
iOS TFM shares that one flat obj dir, so the simulator run leaves a simulator-arch
`r2r_modules.o` behind and the device link dies with `ld: building for 'iOS', but linking in
object file ... built for 'iOS-simulator'`. `rm -rf artifacts/out
artifacts/bin/App.Maui/debug_net11.0-ios_ios-arm64` and rebuild.

What to look for:

| Result | Meaning |
|---|---|
| Circular chat avatar, chat name as the title, `Author: text` body lines | working |
| App icon, chat name as the title | the extension ran; `updating(from:)` failed — check the entitlement on **both** the app and the appex |
| App icon and a `"<sender> @ <chat>"` title, in **some** chats | expected, not a fault: those notifications were already in the store before the server sent `senderName`/`groupTitle`, so the extension leaves them alone. Clears as each is read or receives another message |
| App icon and a `"<sender> @ <chat>"` title, in **every** chat | the extension didn't run at all — check `PlugIns/VoxtNotificationService.appex` exists and is signed |

The extension is a separate process, so the app's debugger session won't stop in it — attach
to `VoxtNotificationService` explicitly, or read its `os_log` output with
`xcrun devicectl` / Console.app filtered on the process name.
