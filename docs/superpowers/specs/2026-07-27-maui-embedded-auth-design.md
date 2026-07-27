# MAUI sign-in without the external browser

Date: 2026-07-27
Status: approved, ready for implementation

## Problem

App Store review rejected the iOS app under **Guideline 4 — Design**:

> We noticed that the user is taken to the default web browser to sign in or
> register for an account, which provides a poor user experience. This is
> exclusive to the Google registration flow only.
>
> You may also choose to implement the Safari View Controller API to display web
> content within the app.

The report is accurate and precisely scoped. Current behavior:

| Platform | Google | Apple | Sign-out |
|---|---|---|---|
| iOS | **system browser** | native `ASAuthorization` (`NativeAppleAuth.cs:11`) | system browser |
| Android | native Play Services (`NativeGoogleAuth.cs:54`) | system browser | system browser + native |
| Mac Catalyst | system browser | system browser | system browser |
| Windows | system browser | system browser | system browser |

The web path is `MauiAccountUI.WebSignInOrSignOut` → `MauiBrowser.Open` →
`/maui-auth/start` → `/signIn/{schema}` → provider → `/fusion/close`.

`MauiSettings.WebAuth.UseSystemBrowser` (`MauiSettings.cs:65`) already gates a
dormant alternative that navigates the **main BlazorWebView** to the host URL.
That path is not viable: it hijacks the app's own webview, which is served from
`https://0.0.0.1/`, and its host-side branch in `MauiWebView.Navigation.cs`
is dead code — `IsAllowedHostUri` returns `false` unconditionally today, as its
own call-site comment admits.

### Why not a literal embedded webview

Google blocks OAuth from embedded webviews — `WKWebView`, Android `WebView`,
`WebView2` — returning `disallowed_useragent` / "This browser or app may not be
secure". An embedded webview therefore cannot serve the exact flow Apple
flagged. What Google permits, and what the reviewer asked for, is the
platform's **in-app browser**: `ASWebAuthenticationSession` (built on
`SFSafariViewController`) on iOS and Mac Catalyst, Chrome Custom Tabs on
Android. MAUI wraps both behind `WebAuthenticator`.

## Approach

Keep the server-side OAuth flow exactly as it is; change only *where the browser
lives* and *how control returns to the app*. Native provider paths stay as the
first choice where they already exist.

A consequence worth stating up front: because the OAuth callback still lands on
the server, **nothing changes in the Google or Apple consoles**. Redirect URIs
stay `https://voxt.ai/signin-google` and `https://dev.voxt.ai/signin-google`.
The custom scheme is used only for the final server→app hop. A native
client-side PKCE flow, by contrast, would have required new per-flavor client
IDs and redirect URIs in both consoles for all four platforms.

```
MauiAccountUI.SignInBackend(schema)
├─ Android + Google  → NativeGoogleAuth      (unchanged, native)
├─ iOS + Apple       → NativeAppleAuth       (unchanged, native)
└─ everything else   → MauiWebAuthenticator.Run(url)   ← NEW
                       ├─ iOS/macOS  ASWebAuthenticationSession (ephemeral)
                       ├─ Android    Chrome Custom Tabs
                       └─ Windows    default browser + AppInstance activation

MauiAccountUI.SignOutBackend()
├─ Android → NativeGoogleAuth.SignOut()      (unchanged)
└─ all     → Commander.Call(NativeAuth_SignOut(session))   ← NEW, no browser
```

## Reuse

### Existing abstractions to reuse

- **`Microsoft.Maui.Authentication.WebAuthenticator`** — the in-app browser
  session on iOS, Mac Catalyst and Android. Already a global using
  (`App.Maui/GlobalUsings.cs:11`); no new package reference.
- **`MauiAuthController`** (`Users.Service/Controllers/MauiAuthController.cs`) —
  the `/maui-auth/start` entry point, its session-token cookie, and its
  `redirectUrl` parameter are used as-is.
- **`AuthHelper`** (`Users.Service/AuthHelper.cs`) — token-session path,
  close-flow detection, and `SessionTemporals.SignInErrorKey` error reporting
  all unchanged.
- **`HostInfoExt.GetHosts()`** (`Api/HostInfoExt.cs:16`) — already resolves to
  `Constants.Hosts.AllProd` / `AllDev` / `AllLocal` per deployment. Reused as
  the host allowlist for redirect validation rather than writing a new one.
- **`Constants.Hosts`** (`Api/Constants.cs:8`) — the pattern the new
  `Constants.AppSchemes` mirrors.
- **`NativeAuth` / `INativeAuth`** (`Users.Service/NativeAuth.cs`,
  `Api.Contracts/Users/INativeAuth.cs`) — sign-out
  becomes a third command on the existing service, symmetric with
  `OnSignInGoogle` / `OnSignInApple`, reusing its `SessionTemporals` error
  reporting.
- **`App.AppInstanceActivated`** (`Platforms/Windows/App.Activation.cs:37`) —
  the existing single-instance activation event carries the Windows callback;
  no new activation plumbing.
- **`AccountUI.SignIn` / `SignOut`** (`UI.Blazor/Services/AccountUI/AccountUI.cs`)
  — the base class and its `SignInBackend` / `SignOutBackend` extension points
  are unchanged; only the MAUI override body changes.
- **`ProviderSelectStep.razor:149`** — already reads and displays
  `SignInErrorKey`; the error path needs no UI work.
- **`MauiSettings.AppScheme`** (`Maui/MauiSettings.cs:25`) — an existing
  `const`, already flavor-split, and attribute-legal for the Android
  `[IntentFilter]`.

No gap found: every piece of this design is either an existing abstraction or a
thin platform shim over one.

### Reusability of new components

**`MauiWebAuthenticator`** is the only new component of substance. Placement
options:

- `App.Maui/Services/` — next to `MauiBrowser.cs`, which it partially replaces.
- `Maui/` (the shared MAUI library) — alongside `MauiSettings`.

**Recommendation: `App.Maui/Services/`.** The only other consumer of the shared
`Maui` library is `App.Maui.IosShareExt`, which has no authentication and never
will; and the Windows implementation depends on `App.Maui.WinUI.App`, which
lives in `App.Maui`. Promoting it later, if a second consumer appears, is a file
move.

`Constants.AppSchemes` is genuinely shared — server and client both need it —
and goes in `Api/Constants.cs` next to `Constants.Hosts`.

`NativeAuth_SignOut` goes in `Api.Contracts/Users/INativeAuth.cs` with its
siblings.

## Sign-in flow

```
WebAuthenticator.AuthenticateAsync(
    Url:         https://voxt.ai/maui-auth/start?s=<token>&e=/signIn/Google&flow=Sign-in
                                                &redirectUrl=voxt%3A%2F%2Fauth-complete
    CallbackUrl: voxt://auth-complete
    PrefersEphemeralWebBrowserSession: true)

  → MauiAuthController.Start: sets session-token cookie, 302 →
  → /signIn/Google?returnUrl=…/fusion/close?flow=Sign-in&mustClose=0
                              &redirectUrl=voxt://auth-complete
  → Google (prompt=select_account) → OAuth callback
  → /fusion/close → AuthHelper.UpdateAuthState runs AccountsBackend_SignIn
                    on the token session
  → RootServerPage.razor:431  302 → voxt://auth-complete
  → sheet auto-dismisses, AuthenticateAsync returns
```

The 302 to a custom scheme is what closes the sheet: `ASWebAuthenticationSession`
and Custom Tabs both match on scheme, including through redirects. No data is
read off the callback URL — the session was bound server-side before the
redirect was issued.

`PrefersEphemeralWebBrowserSession = true` gives a private cookie jar per
sign-in: no system consent alert, and nothing left behind in Safari, which
delivers the original "clean the cookies" goal without any sign-out work. The
cost is that the user authenticates with Google from scratch each time rather
than reusing a Safari session. Sign-in is a once-per-install action, so this is
an acceptable trade. The flag is iOS/macOS-only; Custom Tabs has no equivalent,
which is moot on Android because its Google path is native and never opens a
browser.

A behavioral improvement falls out for free: `SignInBackend` now genuinely
*awaits* completion. Today `_ = MauiBrowser.Open(url)` is fire-and-forget, so
`AccountUI.SignIn`'s follow-up `EnsureDeviceRegistered` (`AccountUI.cs:116`)
runs long before the user has signed in.

## Sign-out flow

No browser, no webview. `SignOutBackend` calls the new
`NativeAuth_SignOut(Session)` command, whose handler runs
`AccountsBackend_SignOut` — the same command the close flow runs today
(`AuthHelper.cs:179`). Android additionally keeps its native Play Services
sign-out.

Nothing depends on clearing browser cookies: `prompt=select_account`
(`UsersServiceModule.cs:67`) already forces Google's account chooser on every
sign-in, and the ephemeral session leaves no cookies to clear.

## Error and cancel handling

- **User dismisses the sheet** — `TaskCanceledException` from
  `AuthenticateAsync`. Caught, logged at debug level, no error surfaced.
- **Sign-in fails server-side** — `AuthHelper` writes
  `SessionTemporals.SignInErrorKey` and *still* redirects to the app: the
  redirect at `RootServerPage.razor:430` precedes error rendering. The app
  returns silently and `ProviderSelectStep.razor:149` displays the message.
  This is the same mechanism `NativeAuth` already relies on for the native
  paths.
- **Windows activation never arrives** — the user closed the browser without
  finishing. The awaited `TaskCompletionSource` is bounded by a timeout so the
  UI cannot wedge.

## Environments

Three axes, not two.

**Axis 1 — build flavor.** The `IsDevMaui` MSBuild property
(`App.Maui.csproj:83`, defaults to `true`) sets `IS_DEV_MAUI` and drives a
matched set:

| | dev flavor | prod flavor |
|---|---|---|
| `DefaultHost` | `dev.voxt.ai` | `voxt.ai` |
| `AppScheme` | `voxt-dev` | `voxt` |
| `ApplicationId` | `chat.actual.dev.app` | `chat.actual.app` |

**Axis 2 — `UseLocalhost`** (`MauiSettings.cs:10`, never committed as `true`)
→ `local.voxt.ai`.

**Axis 3 — `MauiPreferences.HostOverride`** — runtime, arbitrary host
(worktree subdomains such as `wt1.local.voxt.ai`) via
`MauiAppServerInstanceSelector`.

Axes 2 and 3 need nothing new. The auth URL is built from
`MauiSettings.BaseUrl`, so it follows the host automatically, and the callback
is a custom scheme that is host-independent. A side benefit:
`NativeGoogleAuth.IsAvailable()` returns `false` under host override
(`NativeGoogleAuth.cs:47`), so Android + Google on a worktree host falls
through to the web path and now lands in Custom Tabs rather than the external
browser.

### Scheme registration, and one bug it exposes

- **iOS** — ships `voxt-dev` in `Info.plist:82`; CI rewrites it to `voxt` for
  the prod flavor (`build-test-deploy-dev.yml:684`). Correct as-is.
- **Mac Catalyst** — `Info.plist:64-68` registers **both** `voxt-dev` *and*
  `voxt`, with no CI rewrite. Today that is inert. Under this design it is a
  live bug: with both apps installed, macOS routes `voxt://` to whichever it
  chooses, so a prod sign-in callback can be delivered to the dev app, leaving
  the prod app's auth sheet hanging until it times out. Fix: strip `voxt` from
  the MacCatalyst plist and add a CI rewrite step mirroring iOS.
- **Android** — no custom scheme registered today. The new `[IntentFilter]`
  derives from the `MauiSettings.AppScheme` const, so the dev APK filters
  `voxt-dev` and the prod APK filters `voxt`. Package IDs already differ, so
  both coexist.
- **Windows** — unpackaged (`App.Maui.csproj:814` sets
  `WindowsPackageType=None`), so registration is `HKCU\Software\Classes`. It
  must use `MauiSettings.AppScheme` rather than a literal, and must rewrite
  when the exe path changes, since dev builds move between output directories.

### Server-side allowlist

Redirect validation accepts `HostInfoExt.GetHosts()` **plus both app schemes**,
not just the running deployment's flavor — `HostOverride` legitimately lets a
prod-flavor app sign in against `dev.voxt.ai` and vice versa.

## Changes by file

### Server

- `Api/Constants.cs` — add `Constants.AppSchemes` (`voxt`, `voxt-dev`),
  mirroring `Constants.Hosts`. `MauiSettings.AppScheme` sources from it.
- `Api.Contracts/Users/INativeAuth.cs` — add the `NativeAuth_SignOut(Session)`
  record and its `[CommandHandler]` declaration. `Api.Contracts/Module/ApiContractsAotSource.g.cs`
  must then be regenerated via `./update-aot-helpers.cmd`, or the command fails
  at runtime under AOT.
- `Users.Service/NativeAuth.cs` — `OnSignOut` runs `AccountsBackend_SignOut`,
  reporting failures through the existing `ReportError` path.
- `Users.Service/AuthHelper.cs` — validate `redirectUrl` in `IsCloseFlow`:
  accept relative URLs, absolute URLs whose host is in `GetHosts()`, and URLs
  whose scheme is in `Constants.AppSchemes`; drop anything else. This also
  closes a pre-existing open redirect — `RootServerPage.razor:431` currently
  passes an unvalidated query parameter straight to `Response.Redirect`, and
  this change widens that parameter to custom schemes.

### MAUI

- `App.Maui/Services/MauiWebAuthenticator.cs` — **new**. `Run(string url,
  CancellationToken)` → `true` on completion, `false` on user cancel.
- `App.Maui/Services/MauiAccountUI.cs` — `WebSignInOrSignOut` becomes a
  sign-in-only `WebSignIn` that awaits `MauiWebAuthenticator`; new
  `SignOutBackend`. Drops the `UseSystemBrowser` branch.
- `Maui/MauiSettings.cs` — add `AuthCallbackUrl` = `$"{AppScheme}://auth-complete"`
  (so `voxt://auth-complete` / `voxt-dev://auth-complete`); **delete** the
  `WebAuth` nested class.
- `App.Maui/Platforms/Android/` — new `WebAuthCallbackActivity :
  WebAuthenticatorCallbackActivity` with an `[IntentFilter]` on
  `MauiSettings.AppScheme`.
- `App.Maui/Platforms/Windows/` — register the scheme under
  `HKCU\Software\Classes` at startup; bridge `App.AppInstanceActivated`
  (`App.Activation.cs:37`) into `MauiWebAuthenticator`.
- `App.Maui/Platforms/MacCatalyst/Info.plist` — remove the `voxt` entry, leaving
  `voxt-dev`.
- `App.Maui/WebView/MauiWebView.Navigation.cs` — delete `IsAllowedHostUri` and
  its dead call site; trim `AllowedExternalHosts` to `{ "www.youtube.com" }`.
- iOS `Info.plist` — no change; the scheme is already registered.

### CI

- `.github/workflows/build-test-deploy-dev.yml` — add a MacCatalyst plist
  rewrite step for the prod flavor, mirroring the existing iOS step at line 684.

## Testing

The platform pieces are device-level and not unit-testable. Automated coverage
is server-side:

- **Redirect allowlist** (`AuthHelper`) — accepts relative URLs, absolute URLs
  on an allowed host, `voxt://` and `voxt-dev://` regardless of which
  deployment is running; rejects foreign hosts, `javascript:`, and
  protocol-relative `//evil.com`.
- **`NativeAuth_SignOut`** — integration test: a signed-in session becomes a
  guest account.

Manual matrix — 4 platforms × {Google, Apple} × {completes, user cancels,
server-side error}, plus sign-out on each platform. Additionally, on macOS and
Windows, with **both dev and prod apps installed**, confirm each app's callback
reaches the app that started the flow.

## Out of scope

- **Account deletion** (the boilerplate line in the review response) — already
  implemented in the account editing form; not the cited issue.
- **Microsoft sign-in** — configured server-side
  (`UsersServiceModule.cs:108`) but not in `AuthSchema.AllExternal`, so it is
  unreachable from the UI and unaffected.
- **Native Google Sign-In SDK on iOS** — would remove the browser entirely on
  iOS, at the cost of a new native dependency and console configuration. The
  in-app browser is what the reviewer explicitly offered as acceptable, so this
  is not needed now.
