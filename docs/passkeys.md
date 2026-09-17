# Passkeys

A passkey is a WebAuthn credential bound to the account. Sign-in with one is a
challenge/response ceremony run by the platform (Face ID, Touch ID, fingerprint,
Windows Hello) instead of a code sent by SMS or email. This page is the
operational reference: where the pieces live, the values that must agree across
server, app and association files, and how to verify a change by hand.

[[toc]]

## What a passkey is in this codebase

- **Identity row.** Every passkey is an `AccountIdentities` row with schema
  `AuthSchema.Passkey` and the base64url credential id as the key
  ([UserIdentityExt.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Api/Users/UserIdentityExt.cs)),
  so sign-in resolves the account the same way phone and email do.
- **`Passkeys` table.** [DbPasskey.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Users.Service/Db/DbPasskey.cs)
  holds the public key, sign counter, AAGUID, transports, backup flags, name and
  timestamps. Both rows are written together by
  [PasskeysBackend.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Users.Service/Passkey/PasskeysBackend.cs).
- **One user handle per account.** The WebAuthn `user.id` is a random 32-byte
  handle generated for the first passkey and reused for every later one — Apple
  and Google dedupe passkeys by RP id + `user.id`, so a second handle would show
  the same account twice in the picker.
- **API.** [IPasskeyAuth.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Api.Contracts/Users/IPasskeyAuth.cs):
  `BeginRegistration`/`CompleteRegistration`, `BeginSignIn`/`CompleteSignIn`,
  `Rename` (1..64 characters), `Delete` (refused for the last passkey when the
  account has no other identity). Implemented by
  [PasskeyAuth.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Users.Service/Passkey/PasskeyAuth.cs)
  over [Fido2NetLib](https://github.com/passwordless-lib/fido2-net-lib).

## Feature flag

`UsersSettings:IsPasskeyAuthEnabled` — `null` (the default) means **on
everywhere except production**; set it explicitly to override either way.
`IPasskeyAuth.IsEnabled` is what every client asks; `PasskeyUI.CanUse` is that
AND a working platform authenticator, and the settings tab, the Account row and
the sign-in button are all gated on `CanUse` (or on an existing passkey).

## RP id and origins

| Setting | Default | Notes |
|---|---|---|
| `UsersSettings:PasskeyRpId` | host of `HostInfo.BaseUrl` | `voxt.ai` in prod, `dev.voxt.ai` on dev, `local.voxt.ai` locally |
| `UsersSettings:PasskeyOrigins` | origin of `HostInfo.BaseUrl` | `;`-separated allow-list of `clientDataJSON.origin` values |

::: warning
Setting `PasskeyOrigins` **replaces** the implicit base-URL origin — it doesn't
add to it. Always list the web origin (`https://voxt.ai`) alongside the app
origins, or web sign-in stops verifying.
:::

### Android origin

Android apps present `android:apk-key-hash:<base64url(sha256(signing cert))>`,
derived from the same SHA-256 fingerprint `assetlinks.json` lists:

```bash
# fingerprint from `keytool -list -v -keystore <keystore>` or assetlinks.json
echo "7F:34:78:A4:..." | tr -d ':' | xxd -r -p | base64 | tr '+/' '-_' | tr -d '='
```

| Environment | Package | Signed by | apk-key-hash |
|---|---|---|---|
| dev, Play build | `chat.actual.dev.app` | Play app signing | `teyyQLAlytNdVpouFUXplnRaKYEChk-Dvbjlu91FZkk` |
| dev, side-loaded build | `chat.actual.dev.app` | `myapp.keystore` (upload key) | `fzR4pOolsfmBS_aofT7PcsXMAc_Iihkd6-O6tWbDfQs` |
| prod | `chat.actual.app` | Play app signing | `oUXW_CW4O6BYDXt9nq9CHqTTpNk_rLYPXjDiyIkAXnc` |

Play App Signing re-signs every build it distributes, so a Play-installed dev app
presents Play's key, not the upload key — both must be listed, in
[assetlinks.json](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/App.Wasm/wwwroot/.well-known/assetlinks.json)
and in `PasskeyOrigins`, or Credential Manager fails with "Unable to verify RP ID"
(`Не удалось проверить идентификатор RP`) before our server is ever called. The
authoritative fingerprint is the installed app's:
`adb shell pm dump <package> | grep Signatures:`. Re-derive rather than copy if a
signing key ever rotates. The local dev config
([appsettings.Development.json](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/App.Server/appsettings.Development.json))
is the worked example; the dev and prod clusters take the value from
`UsersSettings__PasskeyOrigins` in `flux-team-core`.

## Association files

| Platform | File | What it grants |
|---|---|---|
| Android | `.well-known/assetlinks.json` | `delegate_permission/common.get_login_creds` per package + cert fingerprint |
| Apple | `.well-known/apple-app-site-association.json` | `webcredentials.apps` lists `M287G8G83F.chat.actual.app` and `.dev.app` |
| Apple app | `Platforms/{iOS,MacCatalyst,MacOS}/Entitlements.{dev,prod}.plist` | `webcredentials:voxt.ai` (prod) / `webcredentials:dev.voxt.ai` + `webcredentials:local.voxt.ai?mode=developer` (dev) |

The RP id the server hands out must be one of the `webcredentials:` domains, or
Apple refuses the ceremony; `ApplePasskeyClient.IsAvailable` returns `false`
when the host is overridden for that reason.

### Apple apps against a local server

Apple normally fetches the association file through its own CDN, which can never
reach `local.voxt.ai` - the name has no public DNS record. `?mode=developer` makes
the device fetch it straight from the domain instead. It only applies to
development-signed builds, so a `MauiSettings.UseLocalhost` build needs:

- **iPhone**: Settings › Developer › Associated Domains Development on, *before*
  the app is installed - the association is checked at install time. The phone
  must resolve `local.voxt.ai` and trust the mkcert root CA at that moment.
- **Mac**: `sudo swcutil developer-mode -e true`, once. The app must also carry
  `com.apple.security.get-task-allow`; `App.Maui.csproj` adds it to Debug Mac
  builds only, because the App Store rejects it.
- **Mac, stale copies**: macOS keeps one association record per app id and binds
  it to a copy in `/Applications` when there is one. An installed
  `/Applications/Voxt (Dev).app` without the entitlement shadows every fresh
  build - remove it while testing. Unregistering it with `lsregister -u` doesn't
  hold; it is back within seconds.

`sudo swcutil show` lists what macOS has registered per app id and domain;
`log show --predicate 'process == "swcd"'` shows the download attempts
(`route: .wk` is the direct developer-mode fetch, `route: cdn` the normal one).

## Client seam

[IPasskeyClient](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/UI.Blazor/Services/PasskeyUI/IPasskeyClient.cs)
runs a ceremony over standard WebAuthn JSON, so the server produces and verifies
one shape for every platform. Implementations:

| Client | Hosts | Backed by |
|---|---|---|
| `WebPasskeyClient` | web, Blazor Server/WASM | `passkeys.ts` → `navigator.credentials` |
| `AndroidPasskeyClient` | MAUI Android | Credential Manager |
| `ApplePasskeyClient` | MAUI iOS, Mac Catalyst, macOS | `ASAuthorizationController` |
| `UnavailablePasskeyClient` | MAUI Windows | nothing — `IsAvailable` is `false` |

Windows gets the stub because the WebView2 page origin is `https://0.0.0.1`
(`MauiSettings.LocalHost`): `isAvailable()` would say yes, but every
`credentials.create/get` targeting `rp.id = voxt.ai` fails with `SecurityError`.
A native Windows Hello path is a follow-up.

`PasskeyUI.CanUse` caches the client's answer per scope; a probe that throws
(circuit not interactive yet) isn't cached and re-runs after 5 s.

## Challenges

Challenges live in Redis, keyed by a hash of the session id and the ceremony
kind (`.PasskeyChallenge:create:` / `:get:`), single-use (read-and-delete on
completion) and expire after `UsersSettings:PasskeyChallengeLifetime` (2 min).
The registration challenge also pins the account id it was issued for, so a
session that signs in as someone else mid-ceremony is refused.

## Manual verification

1. Run the server; open the app in Chrome.
2. DevTools → More tools → **WebAuthn** → Enable → Add virtual authenticator
   (protocol ctap2, transport internal, resident key + user verification on).
3. Sign in with a code → Settings → Account shows the Passkeys row ("Not set up"
   + badge) → Passkeys tab → **Add a passkey** → the authenticator prompt →
   one passkey named "Passkey", marked synced. Click the row to rename it.
4. Sign out → **Sign in with a passkey** → signed in on the same account.
5. Delete the passkey → the badge returns; the sign-in button now shows
   "isn't linked to an account" after the prompt.

Server side, `PasskeyAuthTest` covers the same ceremonies with a software
authenticator (`tests/Users.IntegrationTests`).

## Known limitations / follow-ups

- **Windows**: no native path; the WebView2 origin blocks WebAuthn (see above).
- **Apple `excludedCredentials`**: not passed, so a device can register twice
  for one account; needs iOS 17.4+ API.
- **Web `AbortController`**: `passkeys.ts` doesn't abort the browser prompt when
  the .NET call is cancelled.
- **Desktop without a platform authenticator**: `CanUse` is `false`, so the
  hybrid (QR-to-phone) sign-in isn't offered even though the browser supports it.
- **Delete-last guard is API-side only**: `PasskeyAuth.OnDelete` refuses to
  remove the only identity; `PasskeysBackend` doesn't, so backend callers can
  strip it.
- **Passkey-first sign-up** and conditional UI (autofill) are out of scope for
  now; `PasskeyPurpose.SignUp` is reserved and refused.
