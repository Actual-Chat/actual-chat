# Passkeys + device link — design

Status: approved in brainstorming, 2026-09-11. Branch `feat/passkeys`.
Working document: delete once the feature ships (living docs + git history are the record).

## Goals, in priority order

1. Faster, phishing-resistant re-login for existing users (passkey as an added sign-in method).
2. Passkey-first signup — no SMS/email code to create an account.
3. Getting a signed-in phone user onto web/desktop: "Link a device" via QR.

All three ship, as three PRs in that order, on one server core, behind one flag.

## Decisions taken

| Question | Decision |
|---|---|
| Server model | Passkey = `UserIdentity(AuthSchema.Passkey, credentialId)` in `AccountIdentities` + a `Passkeys` table for key material. Sign-in goes through `AccountsBackend_SignIn` unchanged. |
| Native shells | Android: Credential Manager. iOS / Mac Catalyst / AppKit: `ASAuthorizationController`. Web, WASM, Blazor Server, Windows (WebView2): `navigator.credentials`. All at once, not sequentially. |
| Settings placement | Passkeys row in the **Account** tab (they are sign-in methods, siblings of phone/email) → sub-page. **Sessions** tab gets "Link a device" (it creates a session). |
| Discovery | Post-sign-in nudge (onboarding step, snoozable per device, max 3 times / not within 7 days) **and** a badge on Account until the first passkey exists. |
| Recovery policy for passkey-only accounts | Soft: never block; nag to add phone/email. Escalate (re-prompt on every sign-in, stronger copy) when all passkeys are device-bound (not backup-eligible). |
| Device link fallback code | None — QR only. |
| Conditional UI (autofill passkeys in the phone/email box) | Out of scope, follow-up. |

## 1. Server core (phases 1 & 2)

### Storage

`Users.Service/Db/DbPasskey.cs`, table `Passkeys`, plus migration:

| Column | Notes |
|---|---|
| `Id` | base64url credential id (PK) |
| `AccountId` | account id, indexed |
| `UserHandle` | 32 random bytes, base64url; identical for all passkeys of one account (see §5) |
| `PublicKey` | COSE key bytes |
| `SignCount` | uint, monotonic check on assertion |
| `Aaguid` | Guid |
| `Transports` | comma-joined |
| `IsBackupEligible`, `IsBackedUp` | BE / BS flags, refreshed on every assertion |
| `Name` | user-editable; default derived from AAGUID ("iCloud Keychain", "Google Password Manager", "Windows Hello", "Security key", else "Passkey") |
| `CreatedAt`, `LastUsedAt` | |

Every passkey also has an `AccountIdentities` row: `UserIdentity(AuthSchema.Passkey, credentialId)`. That makes `AccountsBackend_SignIn`, `GetIdByUserIdentity`, conflict checks, `Account.Identities` and the session list work without changes. `Core/AuthSchema.cs` gains `Passkey = "passkey"` and its display name.

### Crypto and settings

`Fido2NetLib` (new central package version). `UsersSettings`:

- `IsPasskeyAuthEnabled` (dev on, prod off until device passes are done).
- `PasskeyRpId` = public host (`voxt.ai` / `dev.voxt.ai`).
- `PasskeyOrigins` = allowed web origins + `android:apk-key-hash:<base64url(sha256(cert))>` for dev and prod signing certs.

### `IPasskeyAuth` (`Users.Contracts`; impl `Users.Service/Passkey/PasskeyAuth.cs`)

Same shape as `IPhoneAuth`. All commands rate-limited via `RateLimitPolicy` (`RateLimitClass.Auth`, IP + session). Challenges live in Redis keyed by session hash, one pending per session, 2-minute TTL, consumed atomically (Lua, modelled on `TotpCodes`).

- `IsEnabled(ct)` — compute method, settings-gated.
- `GetRpId(session, ct)` — compute method; native clients read it rather than hardcode.
- `ListOwn(session, ct)` — compute method → `ApiArray<Passkey>` (id, name, createdAt, lastUsedAt, isSynced).
- `GetRecoveryRisk(session, ct)` — compute method → `None | NoContact | DeviceBoundOnly` (phase 2).
- `PasskeyAuth_BeginRegistration(session, purpose, name?)` → creation options JSON. `purpose = AddToAccount` requires a signed-in session and uses the account's existing user handle (or mints one); `purpose = SignUp` is anonymous, requires a captcha proof, mints a user handle and takes the display name.
- `PasskeyAuth_CompleteRegistration(session, attestationJson)` → verifies; inserts `DbPasskey` + identity (conflict → `Unauthorized("already registered")`); in `SignUp` mode first issues `AccountsBackend_SignIn(session, passkeyIdentity, {passkeyIdentity}, {GivenName: name}, AutoCreate: true)` then inserts the passkey for the new account, in one operation scope; invalidates `ListOwn` / `GetRecoveryRisk`.
- `PasskeyAuth_BeginSignIn(session)` → assertion options, empty `allowCredentials` (discoverable), `userVerification: required`.
- `PasskeyAuth_CompleteSignIn(session, assertionJson)` → lookup by credential id, verify signature + sign counter, update `LastUsedAt` / `SignCount` / BE / BS, then `AccountsBackend_SignIn(session, passkeyIdentity, {passkeyIdentity}, claims: {})`. Unknown credential → `NotFound` surfaced as "This passkey isn't linked to an account".
- `PasskeyAuth_Rename(session, id, name)`.
- `PasskeyAuth_Delete(session, id)` — refused (`Constraint`) when it is the last passkey and the account has no other verified identity.

## 2. Client abstraction and web path

`IPasskeyClient` (`UI.Blazor`): `IsAvailable(ct)`, `Create(optionsJson, ct) → attestationJson`, `Get(optionsJson, ct) → assertionJson`. Payloads are standard WebAuthn JSON (`parseCreationOptionsFromJSON` / `toJSON()` shapes) — the same thing Android Credential Manager and Apple speak, so the server contract is identical everywhere. User cancellation → `PasskeyCancelledException`.

`WebPasskeyClient` over `UI.Blazor/Services/AccountUI/passkeys.ts` (next to `web-auth.ts`): feature detection (`PublicKeyCredential` + `isUserVerifyingPlatformAuthenticatorAvailable()`), built-in JSON conversion when present with a base64url shim otherwise, `AbortController` tied to the cancellation token. Windows uses this unchanged.

`PasskeyUI` (`UI.Blazor/Services`, registered like `TotpUI`): `SignIn()`, `Register(name?)`, `SignUp(name)`, `CanUse` (= `IsEnabled ∧ IsAvailable`, cached per session). `AccountUI.SignIn(schema)` gets an `AuthSchema.Passkey` branch delegating here; server-side failures land in the existing `SignInErrorKey` temporal the modal already reads.

`ProviderSelectStep`: "Sign in with passkey" button at the top of the provider list when `CanUse`; resolves in place (no popup/redirect).

## 3. Native bridges

Registered per platform in `MauiProgram.*.cs` over the web default, where `NativeGoogleAuth` / `NativeAppleAuth` are wired.

**Android** — `Platforms/Android/AndroidPasskeyClient.cs` on `Xamarin.AndroidX.Credentials` + `.PlayServicesAuth`. `Create` = `CreatePublicKeyCredentialRequest(json)`, `Get` = `GetCredentialRequest([GetPublicKeyCredentialOption(json)])`, results passed through verbatim. `IsAvailable` = Play Services present ∧ `!MauiSettings.IsHostOverridden`. `GetCredentialCancellationException` / `NoCredentialException` → cancelled. `assetlinks.json` gains `delegate_permission/common.get_login_creds` for both packages.

**Apple** — `MaciOS/Services/ApplePasskeyClient.cs` (iOS 16+, macOS 13+; shared by iOS / Catalyst / AppKit) on `ASAuthorizationPlatformPublicKeyCredentialProvider(rpId)` + `ASAuthorizationController` with a presentation-context delegate. Assembles WebAuthn JSON from the raw blobs (one unit-tested helper). `ASAuthorizationError.Canceled` → cancelled. Entitlements add `webcredentials:voxt.ai` (prod) / `webcredentials:dev.voxt.ai` (dev); AASA gains a `webcredentials.apps` block with both app ids.

RP id comes from `IPasskeyAuth.GetRpId`. Native passkeys are testable only against dev/prod hosts (they need the served association files); `localhost` covers the web path only.

## 4. Settings UI, nudge, badge

- `YourAccount.razor`: "Passkeys" `TileItem` under the phone row; caption "N passkeys" / "Not set up"; chevron → `PasskeySettings.razor` (`SettingsTabId.Passkeys`, hidden from the tab list like the API-key sub-pages). Hidden when `CanUse` is false and the account has no passkeys.
- `PasskeySettings.razor`: `ComputedStateComponent` over `ListOwn`; per passkey: name, "Synced" / "This device only" pill (from `IsBackupEligible`), created, last used, inline rename, delete with confirm. "Add a passkey" primary button (disabled with a reason when `CanUse` is false). Server `Constraint` errors shown as-is.
- Nudge = `PasskeyStep` appended to the existing `OnboardingUI` flow. Shown when `CanUse ∧ ListOwn empty ∧ snooze allows`. Snooze in `LocalOnboardingSettings` (`PasskeyNudgeCount`, `PasskeyNudgeLastAt`): skipped after 3 "Not now" or within 7 days of the last one. No completion flag — having a passkey is completion; `UserOnboardingSettings` stays untouched.
- Badge: dot on the Account tab entry and the Passkeys row while `CanUse ∧ ListOwn.Count == 0`.

## 5. Passkey-first signup

- `ProviderSelectStep`: "New here? Create an account with a passkey" link under the phone/email form (when `CanUse`) → one-field step **Your name** → `PasskeyUI.SignUp(name)`.
- Server: as in §1 (`SignUp` purpose; captcha + IP/session rate limit before `BeginRegistration`; `AutoCreate: true`, no pending-registration prompt).
- User handle: all passkeys of an account share one `user.id` (Apple and Google dedupe by RP + user.id; Apple replaces on collision). `BeginRegistration(AddToAccount)` reuses the handle of the account's existing passkeys.
- Recovery: existing verify-phone / verify-email onboarding steps remain the soft nag. `GetRecoveryRisk` = `NoContact` drives the Account badge and the row caption "Add a phone or email to recover your account"; `DeviceBoundOnly` makes the verify-contact step ignore its completed/skipped flag and re-show on every sign-in with the stronger copy. Never blocks.

## 6. Device link via QR

Reuse: `SessionsBackend_Upsert(session) { UserId }` (the API-key path), `qr-code.lit.ts` (`ShareQrModal`), `qr-scan-view.ts`, existing universal / app links.

- New device: `ProviderSelectStep` "Sign in with your phone" (web, Windows, Mac; hidden on Android/iOS) → `Accounts_StartDeviceLink(session)` → Redis record `{targetSessionHash, description, createdAt}` under a random 128-bit token, 2-min TTL, single use, IP rate-limited; returns token + expiry. Description from user agent ("Chrome · Windows"), plus coarse geo only if a resolver already exists. Renders `https://<host>/link/<token>` as QR with a countdown; re-issues silently on expiry. No polling: `AccountUI.OwnAccount` flips guest → signed in through normal invalidation and `StartOnSignedInWorkflow` closes the modal.
- Phone: system camera via universal link (`/link/*` added to AASA `applinks` paths, `assetlinks`, and the app's link router) **or** Settings → Sessions → **Link a device** (in-app `qr-scan-view`). Both land on `DeviceLinkConfirmModal`: "Sign in on **Chrome · Windows**?" + requested-at, **Sign in** / **Cancel**. Confirm → `Accounts_ApproveDeviceLink(session, token)`: non-guest active account required; consumes token atomically; `SessionsBackend_Upsert(targetSession) { UserId = approver.Id, Description }`. Expired / consumed → `Constraint("This code has expired — show a new one on the other device")`. The link opened outside the app shows "Open this in the Voxt app on your phone".
- Out: typed fallback code, new-sign-in push to other devices, any coupling to passkeys.
- Verify at implementation time: a regular session bound only via `SessionsBackend_Upsert` behaves like one signed in through `AccountsBackend_SignIn` (device registration, `AccountChangedEvent`); adjust if a side effect matters.

## 7. Testing, rollout, phasing

**Server** (`tests/Users.IntegrationTests`), driven by a software authenticator helper in `tests/Testing` (P-256 key; real `clientDataJSON` / `attestationObject` (`none`) / assertions; controllable sign count and BE/BS):

- `PasskeyAuthTest`: register → list → sign in on a fresh session; wrong RP id / origin / stale / reused challenge / non-monotonic counter rejected; rename; delete; delete-last guard both ways; second passkey reuses the user handle.
- `PasskeySignUpTest`: anonymous SignUp creates the named account, no pending-registration temporal; `GetRecoveryRisk` transitions.
- `DeviceLinkTest`: start → approve → target signed in with description; expired / consumed / guest approver / self-approve refused; single use under concurrency.
- Rate limits as in `PhonesTest`.

**Client**: unit test for the Apple blob → JSON helper; jest test for the `passkeys.ts` shim; Playwright with Chromium's virtual authenticator (`WebAuthn.addVirtualAuthenticator`) for register → sign out → sign in → nudge once + snooze. Native bridges and QR flow: manual device checklist (Android, iOS, Windows, web) in the plan. `npm run build:Verify` on TS changes.

**Rollout**: everything behind `IsPasskeyAuthEnabled`. Association files and entitlements ship with phase 1. Migration adds only a new table (forward-safe under rolling deploy).

**PRs**: (1) server core + web + native bridges + settings/nudge; (2) passkey-first signup + recovery risk; (3) device link.

## Reuse

Existing: `AccountsBackend_SignIn`, `AccountIdentities` / `UserIdentity`, `AuthSchema`, `RateLimitPolicy`, `CaptchaProofValidator`, `TotpCodes` (pattern for Redis challenge storage), `SessionsBackend_Upsert`, `OnboardingUI` + `LocalOnboardingSettings`, `SettingsModal` sub-page pattern (`ApiKeySettings`), `qr-code.lit.ts`, `qr-scan-view.ts`, `NativeGoogleAuth` / `NativeAppleAuth` wiring, `SignInErrorKey` temporal.

New shared pieces and placement: `IPasskeyClient` + `PasskeyCancelledException` → `UI.Blazor/Services` (platform seam, must be visible to MAUI); `IPasskeyAuth` → `Users.Contracts`; software authenticator test helper → `tests/Testing`; `AuthSchema.Passkey` → `Core`.
