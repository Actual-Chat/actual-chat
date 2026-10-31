# macOS distribution

How to build and ship the macOS (AppKit, `net11.0-macos`) app for the Mac App Store
(App Store + TestFlight), and how to run a local build against dev/prod. The
distribution path mirrors the iOS pipeline (`build-ios-pkg` / `deploy-ios-to-appstore`).
The backend itself is described in [macOS AppKit backend](./macos-appkit.md).

## One-time prerequisites

### Apple Developer Console

1. **Identifiers** — for both `chat.actual.dev.app` and `chat.actual.app`:
   the Mac app reuses the iOS App ID (universal purchase).
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

   > **Do not use the "Mac Catalyst" profile type.** It makes dyld refuse `@rpath`
   > expansion for embedded `.framework`s under the App Store/TestFlight security policy,
   > so the app gets `EXC_CRASH (SIGABRT)` at launch with *"Library not loaded: @rpath/…
   > security policy does not allow @ path expansion"*. It only bites the distribution
   > build (`get-task-allow=false`); local Debug masks it.
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

```bash
./b.cmd app run mac          # dev backend (dev.voxt.ai) -> Voxt (Dev).app
./b.cmd app run mac --prod   # prod backend (voxt.ai)    -> Voxt.app
```

Signed with your Apple Development cert + the matching `mac.chat.actual…` profile;
see `/mac-run` for the details.

## Local build (App Store signed `.pkg`)

The `pack-mac` target builds, signs the `.app` with the Apple Distribution cert, and —
via `CreatePackage` + `EnablePackageSigning` + `PackageSigningKey` (the installer cert) —
emits the installer-signed `.pkg` directly. No separate packaging step.

```bash
./b.cmd app pack mac                 # dev, host CPU only
./b.cmd app pack mac --prod          # prod
./b.cmd app pack mac --universal     # arm64 + x64, as CI builds it
# -> artifacts/publish/App.Maui/release_net11.0-macos*/*.pkg
```

Upload via Transporter, or:

```bash
xcrun altool --upload-app -f <pkg> -t macos \
  --apiKey <APPSTORE_API_KEY_ID> --apiIssuer <APPSTORE_API_ISSUER_ID>
```

## CI

Two jobs in `.github/workflows/build-test-deploy-dev.yml` —
`build-macos-appkit-pkg` and `deploy-macos-appkit-to-appstore` — mirror the iOS
ones. They run whenever `MUST_BUILD_PACKAGE == true` (dev/release branch pushes,
or a manual run with `buildAppFor`), build a universal package via the `pack-mac`
target, then validate + upload with `altool -t macos`. A manual run can skip the
build with `buildMacOS: false`, or the upload with `publishMacPackage: false`.

### Required GitHub secrets

Reused from the iOS pipeline:
- `APPLE_DISTRIBUTION_CERT_BASE64`, `APPLE_DISTRIBUTION_CERT_PASSWORD`
- `APPSTORE_API_KEY_BASE64`, `APPSTORE_API_KEY_ID`, `APPSTORE_API_ISSUER_ID`
- `NPM_READ_TOKEN`

Mac-only:
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
