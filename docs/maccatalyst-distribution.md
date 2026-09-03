# Mac Catalyst distribution

How to build and ship the MAUI app for the Mac App Store (App Store + TestFlight),
and how to run a local build against dev/prod. The distribution path mirrors the
existing iOS pipeline (`build-ios-pkg` / `deploy-ios-to-appstore`).

## One-time prerequisites

### Apple Developer Console

1. **Identifiers** — for both `chat.actual.dev.app` and `chat.actual.app`:
   the Mac Catalyst app reuses the iOS App ID (universal purchase). You do **not**
   need to enable the legacy "Mac Catalyst" capability / derived `maccatalyst.*` ID.
2. **Register your Mac as a device** — Devices → +  → Platform **macOS** → Device ID =
   the Mac's **Provisioning UDID** (`system_profiler SPHardwareDataType | grep "Provisioning UDID"`).
   Needed so development profiles can authorize local runs.
3. **Provisioning Profiles** — four total. On the profile-generation page select the
   **"Mac"** profile type, **not "Mac Catalyst"**:

   | Name | Type | Profile type | App ID | Cert |
   |---|---|---|---|---|
   | `mac.chat.actual.dev.app` | macOS App Development | **Mac** | `chat.actual.dev.app` | Apple Development (+ your Mac) |
   | `mac.chat.actual.app` | macOS App Development | **Mac** | `chat.actual.app` | Apple Development (+ your Mac) |
   | `Mac App Store Dev` | Mac App Store | **Mac** | `chat.actual.dev.app` | Apple Distribution |
   | `Mac App Store` | Mac App Store | **Mac** | `chat.actual.app` | Apple Distribution |

   Names must match `CodesignProvision` in `src/dotnet/App.Maui/App.Maui.csproj`
   (Debug uses the Development pair, Release the App Store pair).

   > **Do not use the "Mac Catalyst" profile type.** A Mac Catalyst profile makes dyld
   > refuse `@rpath` expansion for embedded `.framework`s under the App Store/TestFlight
   > security policy, so the app gets `EXC_CRASH (SIGABRT)` at launch with
   > *"Library not loaded: @rpath/…OpusLib … security policy does not allow @ path expansion"*.
   > It only bites the distribution build (`get-task-allow=false`); local Debug masks it.
   > See [dotnet/macios#14686](https://github.com/dotnet/macios/issues/14686).
4. **Certificates** — in addition to the existing Apple Distribution + Apple Development
   certs, create a **Mac Installer Distribution** cert (Production). Installs as
   `3rd Party Mac Developer Installer: Actual Chat Inc. (M287G8G83F)`.
   Required to wrap the `.app` into an App-Store-uploadable `.pkg`.

### Locally

Double-click all four `.provisionprofile` files to install. Verify certs with:

```bash
security find-identity -v -p basic | grep -E "Apple Development|Apple Distribution|3rd Party Mac Developer Installer"
```

## Run locally (Debug)

For everyday local testing against dev or prod backends. Signed with the Apple
Development cert + the matching `Mac Development …` profile, using the same
entitlements as the App Store build (so keychain / Apple Sign-In / universal
links behave as in production). `IsDevMaui` picks the backend.

```bash
cd src/dotnet/App.Maui
# Against prod backend (voxt.ai):
dotnet build -f net11.0-maccatalyst -c Debug -p:IsDevMaui=false -p:RuntimeIdentifier=maccatalyst-arm64
open ../../../artifacts/bin/App.Maui/debug_net11.0-maccatalyst_maccatalyst-arm64/Voxt.app
# Against dev backend (dev.voxt.ai): -p:IsDevMaui=true  -> Voxt (Dev).app
```

## Local build (App Store signed `.pkg`)

`pack-maccatalyst` builds, signs the `.app` with the Apple Distribution cert, and —
via `CreatePackage` + `EnablePackageSigning` + `PackageSigningKey` (the installer cert) —
emits the installer-signed `.pkg` directly. No separate packaging step.

```bash
# dev:
./run-build.cmd pack-maccatalyst --configuration Release --is-dev-maui true
# prod:
./run-build.cmd pack-maccatalyst --configuration Release --is-dev-maui false
# -> artifacts/publish/App.Maui/release_net11.0-maccatalyst_maccatalyst-arm64/ActualChat-<version>.pkg
```

Upload via Transporter, or:

```bash
xcrun altool --upload-app -f <pkg> -t macos \
  --apiKey <APPSTORE_API_KEY_ID> --apiIssuer <APPSTORE_API_ISSUER_ID>
```

## CI

Two jobs in `.github/workflows/build-test-deploy-dev.yml` —
`build-maccatalyst-pkg` and `deploy-maccatalyst-to-appstore` — mirror the iOS
ones. They run whenever `MUST_BUILD_PACKAGE == true` (dev/release branch pushes,
or a manual run with `buildAppFor`), build via the `pack-maccatalyst` target
(which emits the installer-signed `.pkg` directly), then validate + upload with
`altool -t macos`.

The Release config signs the `.app` (and nested code) with the Apple Distribution cert
and the SDK's `CreatePackage`/`EnablePackageSigning` produces the `.pkg` — no manual
re-sign or `productbuild` step.

### Required GitHub secrets

Reused from the iOS pipeline:
- `APPLE_DISTRIBUTION_CERT_BASE64`, `APPLE_DISTRIBUTION_CERT_PASSWORD`
- `APPSTORE_API_KEY_BASE64`, `APPSTORE_API_KEY_ID`, `APPSTORE_API_ISSUER_ID`
- `GOOGLE_SERVICES_PLIST_DEV`, `GOOGLE_SERVICES_PLIST_PROD`, `NPM_READ_TOKEN`

New for Mac Catalyst:
- `PROVISIONING_PROFILE_MAC_DEV_BASE64` — `base64 -i <dev>.provisionprofile`
- `PROVISIONING_PROFILE_MAC_PROD_BASE64` — `base64 -i <prod>.provisionprofile`
- `MAC_INSTALLER_CERT_BASE64` — `base64 -i installer.p12`
- `MAC_INSTALLER_CERT_PASSWORD` — the installer `.p12` password

## Export the `.p12` files for CI

In Keychain Access:
1. Find **Apple Distribution: Actual Chat Inc. (M287G8G83F)** → right-click → Export → `.p12`, set a password. (Already in GH secrets as `APPLE_DISTRIBUTION_CERT_BASE64`.)
2. Find **3rd Party Mac Developer Installer: Actual Chat Inc. (M287G8G83F)** → same.
3. `base64 -i installer.p12 | pbcopy` → paste into `MAC_INSTALLER_CERT_BASE64`.

Provisioning profiles: download the two `.provisionprofile` files from the Apple
Developer Console and `base64 -i <file>.provisionprofile | pbcopy` into the
matching secret.
