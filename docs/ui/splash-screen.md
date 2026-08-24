# Splash screens

Voxt shows up to two splash screens on startup, and they're owned by different
layers:

- **Native splash** — drawn before any of our code paints. On Android, iOS and Mac
  Catalyst it's drawn by the OS from a static image: the same artwork everywhere,
  `Resources/Splash/splashscreen_voxt.png`, declared once as `<MauiSplashScreen>` in
  `App.Maui.csproj` with `<Color>#0C003D</Color>`. Windows has no OS splash for
  unpackaged apps, so we draw our own window instead (see below). Whether we control
  *when* it goes away is platform-specific, and that's the whole story below.
- **Web splash** — `<div id="web-splash">`, a full-screen `z-[1900]` overlay with
  the same `#0C003D` background, living inside the WebView / page. This one we
  fully control on every platform.

They share a background colour on purpose: the handover between them is meant to
be invisible. `MauiSettings.SplashBackgroundColor` (`#0C003D`) is the single
source, used by the native splash, `MainPage.BackgroundColor`,
`BlazorWebView.BackgroundColor`, and the `<body>` inline style of the host page.

Windows is the exception: its splash window paints the **last known theme
background** instead, falling back to the dark theme's `--background-01`
(`#28282E`) — never `#0C003D`. See *The Windows splash window*.

## Per-platform behavior

| | Native splash | Held until app is ready? | Exit animation | Web splash | Transition |
|---|---|---|---|---|---|
| **Android** | `SplashTheme` → `Maui.SplashTheme` (window background; system splash on API 31+) | **Yes** — pre-draw hold in `MainActivity.SplashDelayer`, capped at 3s | **Ours**, 300ms fade via `SplashExitAnimator` (API 31+; hard cut on 28-30) | Removed **instantly**, at `MarkRendered` | **splash → app** |
| **iOS / Mac Catalyst** | `MauiSplash.storyboardc` via `UILaunchStoryboardName` | **No** — the system dismisses it once the app finishes launching | None available | Fades out, 350ms | **splash → bg → app** |
| **Windows** | **Ours** — `WindowsSplashScreen`, a borderless always-on-top window in theme colour | **Yes** — closed on `WhenFirstSplashRemoved`, capped at 3s | **Ours**, 200ms logo fade | Removed **instantly**, behind the splash window | **splash → app** |
| **Web — WASM (`w`) / Auto (`a`)** | None | n/a | n/a | Fades out, 350ms | **bg → app** |
| **Web — Server / SSB (`s`, `ss`)** | None | n/a | n/a | **Not rendered at all** | none |

### The outcome

Everything above reduces to whether we can hold the native splash:

- **No control** (iOS) — the web splash is nothing but the background colour, so
  the native splash fades *to it*, and then that background fades to the app. Two
  transitions: **splash → bg → app**. The middle step is dead time where the logo
  is already gone and the app isn't there yet.
- **Control** (Android, Windows) — the native splash gives way straight to the
  app. On Android the web splash (a bg-coloured cover) is removed instantly the
  moment that starts; on Windows the whole handover happens *behind* our splash
  window, which is only closed once the app has rendered. One transition:
  **splash → app**.

The web splash isn't a second splash screen so much as a *stand-in* for the
native one — it exists to hold the background colour during the window where the
native splash is gone but the app hasn't rendered. On Android and Windows that
window doesn't exist, so it's removed without ceremony.

Two extra gates on the web side, both in `RootServerPage.razor`: the web splash
is skipped during auth flows (`_authState.IsAnyAuthFlow`), and for any render
mode whose key starts with `s`.

## Why Android differs

Android is the only platform that lets us suppress the first frame. An
`OnPreDrawListener` on the content view returns `false` until we're ready, so the
system never draws — and therefore never dismisses the splash. That's the same
mechanism `androidx.core.splashscreen`'s `setKeepOnScreenCondition` uses
internally, which is why we don't take that dependency.

iOS has no equivalent hook, and it isn't for lack of looking: there's no public API
to extend the launch storyboard. The only way to get **splash → app** there would be
*splash continuation* — re-instantiating the same launch storyboard as a native
overlay on top of the window and dismissing it ourselves. Not done.

Windows gets there by a different route: there's no OS splash to hold, so we draw
one ourselves and control it end to end.

Recreating the logo in HTML instead is a dead end: the web splash would need to
match the native artwork's position, scale and colour exactly, and any mismatch
reads worse than the plain background does.

## The removal pipeline

Shared by every platform, and the point where "ready" is decided:

```
MarkRendered()                  (AlwaysVisibleComponents.OnAfterRender)
  └─ RemoveWebSplash(instantly: isCoveredByNativeSplash)
       └─ JS BrowserInit.removeWebSplash(instantly)
```

Every platform removes it at the same point — when the first render completes.
Only the *manner* differs: Android and Windows remove it instantly, the rest fade.

Removing it earlier, at `MarkLoaded`, was tried and backed out. It dropped the
native splash while the app was still blank, so the logo disappeared visibly
before the UI arrived.

Android and Windows skip the fade because a native splash is still covering the
screen at that point, and this removal is precisely what releases it — fading
behind a cover nobody can see through only delays the handover.

The condition is `HostInfo.HostKind.IsMauiApp() && (OSInfo.IsAndroid ||
OSInfo.IsWindows)`, and the `IsMauiApp` half is load-bearing: `OSInfo` reports the
*.NET runtime's* OS, so a Blazor Server or Auto session served from a Windows host
reports `IsWindows` while having no native splash at all — there the fade is the
only thing the user sees.

With `instantly: false` (iOS, web):

| t | What happens |
|---|---|
| 0 | `.removing` added → 350ms opacity fade (`duration-350`) |
| 200ms | `BrowserInfo.onWebSplashRemoved()` → .NET `IBrowserInfoBackend.OnWebSplashRemoved` |
| 350ms | `splash.remove()` |

The 200ms split is deliberate: on MAUI that callback re-applies the theme and
forces a relayout (`MauiBrowserInfo.OnWebSplashRemoved`), and doing it while the
splash is still ~half-opaque hides the change. The element must not be removed
early either — at `z-[1900]` and full-screen it still swallows taps until it's
gone.

With `instantly: true` (Android, Windows) all three collapse into one tick, and the
`OnWebSplashRemoved` callback is what releases the native splash — via
`MauiLoadingUI.MarkFirstSplashRemoved()`.

`MauiLoadingUI.WhenFirstSplashRemoved` is the shared "the app is really on screen"
signal: Android's `SplashDelayer` and the Windows splash window both wait on it.

## The Windows splash window

Windows has no OS splash for an unpackaged app, and MAUI paints a white and then a
gray frame before Blazor content appears ([dotnet/maui#19942][maui19942], still
open). `WindowsSplashScreen` covers that window entirely.

[maui19942]: https://github.com/dotnet/maui/issues/19942

- **Shown before MAUI starts.** `App.OnLaunched` calls it *before*
  `base.OnLaunched`, in the branch where this instance isn't redirecting to an
  already-running one, so no frame of MAUI's window is ever seen uncovered.
- **App startup waits for it to render.** Building MauiApp blocks the UI thread
  for ~500ms, and `CompositionTarget.Rendering` fires as a frame is *composed*,
  not presented — acting on the first tick left the window black for the whole
  build. It waits 3 ticks, with a 1s timer backstop so a tick that never comes
  can't leave the app unstarted.
- **It covers rather than hides.** The main window stays visible underneath:
  WebView2 suspends rendering while hidden and would repaint from scratch on
  reveal, which flashed. A borderless maximized `IsAlwaysOnTop` window covers it
  instead, re-activating itself if the main window steals the foreground.
- **Theme-coloured.** It reads `MauiThemeHandler.TopBarColor` — restored from
  `MauiPreferences.Theme` in the constructor, so it's known before the WebView
  exists. First run falls back to `#28282E`, the dark theme's `--background-01`.
  This is why `Action<ThemeInfo>` is registered on Windows in
  `MauiProgram.Windows.cs`: without it the theme is never persisted.
- **Two logo variants.** `splashlogo_light.png` / `splashlogo_dark.png` under
  `Platforms/Windows/Assets`, picked by the background's luminance. The
  Resizetizer-generated `splashscreen_voxtSplashScreen.*` can't be used: it has
  `#0C003D` baked in as an opaque rectangle, which on any other background reads
  as a coloured box around the logo.
- **Closed on `WhenFirstSplashRemoved`**, capped at 3s so a failed load can't leave
  the app permanently covered.
- **The logo fades out over 200ms** before the window closes. Only the logo is
  animated: the splash background is already the background the app paints, so the
  logo is the one thing that actually changes on handover — fading it is
  indistinguishable from fading the whole window, and needs no layered-window
  interop to see through to the app.

The window title bar is deliberately left to Windows. Painting it from the theme
was tried: `--background-01` doesn't match what's directly beneath it on the
Windows layout, and overriding the caption-button colours cost them their system
contrast. Since the splash now covers all of startup, the theming had no
remaining purpose.

## Timeouts, and why they exist

Every hold is capped, because they all wait on signals that can never arrive
(offline start, failed load, signed-out user):

- `LoadingUI.MarkLoaded()` schedules `MarkRendered()` after **0.5s** regardless,
  so the web splash comes down even if the first render never completes.
- `SplashDelayer.MaxDelay` is **3s**. Holding the pre-draw indefinitely starves
  input dispatch and Android reports a "no focused window" ANR.
- `WindowsSplashScreen.MaxDuration` is **3s**. Its separate 1s `ShowTimeout` guards
  a different risk: that hold sits *before* app startup, so it must never hang.

The Android cap is the one to watch: `MarkLoaded` waits on `PrepareFirstRender`,
which does history init and auto-navigation, so a cold or offline start can hit
3s and reveal an unfinished UI. That's the intended failure mode, but the number
may need revisiting.

## Known rough edges

- **Android warm start** — the activity is recreated while the service provider
  survives, so some services re-initialize and, per the note in
  `MainActivity.OnCreate`, "splash screen is getting hidden early and user sees
  index.html w/o any content yet."
- **No Android exit animation.** API 31+ exposes
  `SplashScreen.SetOnExitAnimationListener`, and a 200ms fade through it was tried
  and removed: on device the splash still vanished instantly, and registering a
  listener suppresses the system's own animation, so it was strictly worse than
  stock. On 28–30 there's no splash *view* at all, only a window background, so a
  hard cut is the only option there regardless.
- **`AndroidThemeHandler.SetBarsAppearance(splashColor, splashColor)`** in
  `MainActivity.OnCreate` predates the pre-draw hold. Its comment claims
  `base.OnCreate` hides the native splash and that the bars are being matched to
  the *web* splash — neither premise holds now.
- **iOS root view** must be painted explicitly. It's white by default and shows
  for a frame between the launch screen and WebKit's first paint; `AppDelegate`
  sets both the `UIWindow` and the root view controller's view to the splash
  colour to prevent that flash.

## Files

| Path | Role |
|---|---|
| `src/dotnet/App.Maui/App.Maui.csproj` | `<MauiSplashScreen>` — artwork + colour for every native splash |
| `src/dotnet/Maui/MauiSettings.cs` | `SplashBackgroundColor` (`#0C003D`) |
| `src/dotnet/App.Maui/MauiThemeHandler.cs` | `TopBarColor` — last known theme background, from preferences |
| `src/dotnet/App.Maui/Platforms/Android/MainActivity.cs` | `SplashDelayer` (pre-draw hold), `SplashExitAnimator` (300ms exit fade) |
| `src/dotnet/App.Maui/Platforms/Android/Resources/values/styles.xml` | `SplashTheme` |
| `src/dotnet/App.Maui/Platforms/iOS/AppDelegate.cs` | Window + root view background |
| `src/dotnet/App.Maui/Platforms/Windows/WindowsSplashScreen.cs` | The Windows splash window |
| `src/dotnet/App.Maui/Platforms/Windows/Assets/splashlogo_{light,dark}.png` | Transparent logo, one per background brightness |
| `src/dotnet/App.Maui/MauiProgram.Windows.cs` | Registers `Action<ThemeInfo>` so the theme is persisted for the splash |
| `src/dotnet/App.Maui/Platforms/Windows/Package.appxmanifest` | `<uap:VisualElements BackgroundColor>`; Resizetizer injects `<uap:SplashScreen>` (packaged builds only) |
| `src/dotnet/App.Maui/wwwroot/index.htm` | Web splash markup, MAUI host page |
| `src/dotnet/App.Server/Components/Pages/RootServerPage.razor` | Web splash markup, web host page |
| `src/dotnet/App.Maui/wwwroot/websplash.js` | Injects skeletons when signed in |
| `src/dotnet/UI.Blazor/Components/Overlays/web-splash.css` | Overlay styling + fade duration |
| `src/dotnet/UI.Blazor/Services/LoadingUI.cs` | Decides when the splash comes down |
| `src/dotnet/UI.Blazor/Services/BrowserInit/browser-init.ts` | `removeWebSplash` |
| `src/dotnet/App.Maui/Services/MauiLoadingUI.cs` | `WhenFirstSplashRemoved` — the signal Android waits on |
