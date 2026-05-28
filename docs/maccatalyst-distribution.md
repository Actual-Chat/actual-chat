# Mac Catalyst distribution

How to build and ship the MAUI app for the Mac App Store (App Store + TestFlight),
and how to run a local build against dev/prod. The distribution path mirrors the
existing iOS pipeline (`build-ios-pkg` / `deploy-ios-to-appstore`).

## One-time prerequisites

### Apple Developer Console

1. **Identifiers** — for both `chat.actual.dev.app` and `chat.actual.app`:
   the Mac Catalyst app reuses the iOS App ID (universal purchase). You do **not**
   need to enable the legacy "Mac Catalyst" capability / derived `maccatalyst.*` ID.
2. **Provisioning Profiles** — create two **Mac App Store** (Mac Catalyst subtype) profiles:
   - `Mac App Store Dev` → `chat.actual.dev.app`
   - `Mac App Store` → `chat.actual.app`

   Both signed by `Apple Distribution: Actual Chat Inc. (M287G8G83F)`.
   The names must match `CodesignProvision` in `src/dotnet/App.Maui/App.Maui.csproj`
   (Release config).
3. **Certificates** — in addition to the existing Apple Distribution cert,
   create a **Mac Installer Distribution** cert (Production). Installs as
   `3rd Party Mac Developer Installer: Actual Chat Inc. (M287G8G83F)`.
   Required to wrap the `.app` into an App-Store-uploadable `.pkg`.

### Locally

Double-click both `.provisionprofile` files to install. Verify with:

```bash
security find-identity -v -p basic | grep -E "Apple Distribution|3rd Party Mac Developer Installer"
```

## Run locally (Debug — no Apple account needed)

For everyday local testing against dev or prod backends. Ad-hoc signed, minimal
entitlements (`Entitlements.debug.plist`), launches directly on your Mac.
`IsDevMaui` picks the backend; signing config is independent.

```bash
cd src/dotnet/App.Maui
# Against prod backend (voxt.ai):
dotnet build -f net10.0-maccatalyst -c Debug -p:IsDevMaui=false -p:RuntimeIdentifier=maccatalyst-arm64
open ../../../artifacts/bin/App.Maui/debug_net10.0-maccatalyst_maccatalyst-arm64/Voxt.app
# Against dev backend (dev.voxt.ai): -p:IsDevMaui=true  -> Voxt (Dev).app
```

**Gotcha:** all Mac Catalyst configs share `IntermediateOutputPath=artifacts/out`
(hardcoded; `fix-codesigning.sh` paths depend on it). Switching between Debug and
Release locally cross-contaminates that dir and yields an unsigned app or undefined
`_callback_*` linker symbols. If you switch configs, run `rm -rf artifacts/out` first.

## Local build (dev — App Store signed)

```bash
./run-build.cmd publish-maccatalyst --configuration Release --is-dev-maui true
./tools/sign-maccatalyst.sh dev
# artifacts/maccatalyst/chat.actual.dev.app-<version>.pkg
```

## Local build (prod — App Store signed)

```bash
./run-build.cmd publish-maccatalyst --configuration Release --is-dev-maui false
./tools/sign-maccatalyst.sh prod
# artifacts/maccatalyst/chat.actual.app-<version>.pkg
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
or a manual run with `buildAppFor`), build via the `publish-maccatalyst` target,
wrap the signed `.app` with `tools/sign-maccatalyst.sh`, and validate + upload
with `altool -t macos`.

Unlike iOS, no manual re-sign step is needed: the Release config + `fix-codesigning.sh`
already sign the `.app` (and nested code) with the Apple Distribution cert.

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
