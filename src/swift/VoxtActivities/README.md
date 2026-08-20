# VoxtActivities — iOS Live Activities

Two Xcode targets backing the iOS half of `ActivitiesBackend`:

- **`VoxtActivityKitShim`** — static library. Exports three C symbols that
  `IosActivitiesBackend` P/Invokes. Renaming one breaks the managed side at runtime, not at
  build time:

  ```c
  int  voxt_activities_enabled(void);
  int  voxt_activity_start_or_update(int kind, const char* title, const char* subtitle, double progress);
  void voxt_activity_end(void);
  ```

- **`VoxtActivitiesWidget`** — SwiftUI widget extension (`.appex`) rendering the lock screen
  and Dynamic Island presentations.

`Shared/VoxtActivityAttributes.swift` is compiled into **both** targets. ActivityKit matches
the requesting app and the rendering widget by attributes *type name*, so this must stay a
single shared file — never copy it.

## Prerequisites

- macOS with Xcode (iOS 16.1+ SDK).
- `brew install xcodegen`.

`VoxtActivities.xcodeproj` is **generated from `project.yml` and gitignored**. `build.sh`
regenerates it whenever it is missing or older than `project.yml`, so xcodegen is a build
machine prerequisite, not just a one-off — the ipa job installs it too, since a GitHub macOS
runner has no xcodegen.

## Building

The MAUI iOS build does this for you: `App.Maui.csproj` runs the `BuildVoxtActivities` target
(hooked via `MaciOSPrepareForBuildDependsOn`, conditioned on macOS + an `-ios` TFM), which
invokes `build.sh`. The products are then consumed as a `NativeReference` (the `.a`) and an
`AdditionalAppExtensions` item (the `.appex`).

`SHORT_VERSION` / `BUILD_VERSION` must be the app's own `CFBundleShortVersionString` /
`CFBundleVersion`: App Store Connect rejects an upload whose extension disagrees with its
host app (ITMS-90473). The target therefore depends on `NBGV_SetVersionForMauiIOS` — the
NBGV target that fills `ApplicationDisplayVersion` / `ApplicationVersion` — because it
otherwise runs before it and would pass empty strings.

Manually:

```sh
./build.sh [CONFIG] [SDK] [BUNDLE_ID] [SHORT_VERSION] [BUILD_VERSION] [DEVELOPMENT_TEAM] \
           [IDENTITY] [PROFILE] [ENTITLEMENTS]
./build.sh                              # Release / iphoneos / dev bundle id, unsigned
./build.sh Debug iphonesimulator
```

Output lands in `build/$CONFIG-$SDK/`:

```
build/Release-iphoneos/libVoxtActivityKitShim.a
build/Release-iphoneos/VoxtActivitiesWidget.appex
```

Check the exported symbols with:

```sh
nm -gU build/Release-iphoneos/libVoxtActivityKitShim.a | grep voxt_
```

## Bundle id and version

Both are passed to `xcodebuild` as build-setting overrides rather than baked into `project.yml`,
because the app is flavor-conditional. `App.Maui.csproj` derives the widget id as
`$(ApplicationId).widget` and the versions from `$(ApplicationDisplayVersion)` /
`$(ApplicationVersion)`, so the widget tracks the app automatically:

| Flavor | App id | Widget id |
|---|---|---|
| `IsDevMaui=true` (default) | `chat.actual.dev.app` | `chat.actual.dev.app.widget` |
| production | `chat.actual.app` | `chat.actual.app.widget` |

`Widget/Info.plist` reads `$(PRODUCT_BUNDLE_IDENTIFIER)`, `$(MARKETING_VERSION)` and
`$(CURRENT_PROJECT_VERSION)`, so it needs no edit when these change. The Xcode *product* name
stays `VoxtActivitiesWidget` regardless — it must match the `<Name>` metadata on the
`AdditionalAppExtensions` item, which is how the SDK locates the `.appex`.

## Signing

**Unsigned by default**, which is all the simulator and CI need.

For **device and TestFlight builds the appex must be signed here**: the .NET iOS SDK re-signs
the `.appex` it embeds, but it never gives it an `embedded.mobileprovision` — only the app
bundle gets one — so an unsigned appex fails at install time, not build time. Supply a team id
to have Xcode sign it and embed a profile:

```sh
dotnet build ... -p:VoxtActivitiesTeamId=M287G8G83F   # from MSBuild
VOXT_ACTIVITIES_TEAM=M287G8G83F ./build.sh            # env var
./build.sh Release iphoneos chat.actual.dev.app.widget 1.0 1 M287G8G83F   # arg 6
```

`M287G8G83F` is this account's team id (visible in the `CodesignKey`,
`Apple Distribution: Actual Chat Inc. (M287G8G83F)`).

The signing identity defaults to **`Apple Development`**, signed automatically — no profile name
needed, Xcode fetches one via `-allowProvisioningUpdates`.

TestFlight and App Store builds need **`Apple Distribution`**, and that **also requires naming a
provisioning profile**: automatic signing only ever selects a development identity, so pairing it
with a distribution one fails with *"conflicting provisioning settings"*. Supplying a profile
(arg 8 / `VOXT_ACTIVITIES_PROFILE` / `-p:VoxtActivitiesProfile`) switches the widget to manual
signing, which is what makes the distribution identity usable:

```sh
dotnet build ... -p:VoxtActivitiesTeamId=M287G8G83F \
                 -p:VoxtActivitiesIdentity="Apple Distribution" \
                 -p:VoxtActivitiesProfile="Voxt Widget App Store"

./build.sh Release iphoneos chat.actual.app.widget 1.0 1 M287G8G83F \
    "Apple Distribution" "Voxt Widget App Store"
```

The profile must be one created for the widget's own App ID — the app's profile will not do.

`Entitlements.dev.plist` / `Entitlements.prod.plist` hold the one entitlement the widget has,
its `application-identifier`. Xcode would derive the same set from the profile on its own, but
the ipa job re-signs the embedded appex after the .NET SDK has, and it signs it with this file —
so the file, the App ID and the profile all have to agree.

Passing the identity is not optional in the signed path: `project.yml` pins
`CODE_SIGN_IDENTITY` to `""` for the unsigned default, an empty identity means "skip signing",
and a project-level setting outranks automatic signing — so a signed build that omits it
**succeeds while silently producing an unsigned appex**. `build.sh` overrides it on the
xcodebuild command line, which takes precedence over the project.

**Live Activities need no entitlement and no portal capability** — no "Live Activities"
capability exists in the developer portal, and the `com.apple.developer.live-activities` /
`…activitykit…` entitlement strings circulating online are bogus (confirmed by Apple DTS,
forum threads 791243 and 808712). The only requirement is `NSSupportsLiveActivities` in the
host app's `src/dotnet/App.Maui/Platforms/iOS/Info.plist` (already present).

Like every embedded app extension, the appex still needs an App ID + provisioning profile
for device signing — with **no** capabilities ticked:

- `chat.actual.dev.app.widget`
- `chat.actual.app.widget`

The signed build path passes `-allowProvisioningUpdates`, so Xcode automatic signing
creates both on first use; manual portal registration is only needed for manually-managed
distribution profiles.

Capabilities become necessary only for future features: **App Groups** (shared container)
the moment the Live Activity should show chat avatars — images can't ride the size-limited
`ContentState` payload — and **Push Notifications** on the host app if push-started/updated
activities are ever added (out of scope by design; the shim is local-only). Interactive
buttons (iOS 17+ App Intents, e.g. Stop/Pause parity with the Android notification) need
no capability at all, just intent code shared into the extension target.
