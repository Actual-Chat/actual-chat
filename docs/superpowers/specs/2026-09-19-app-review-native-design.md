# App review: native review screens

Status: approved design, 2026-09-19. Working document; delete once shipped.
Issue: #4631. Related: #4625 (per-user usage stats, which the
future trigger depends on).

## Goal

Make the existing `AppReviewModal` (624c57daad) do something: when the user agrees to
leave a review, open the platform's native rating flow, and where that is impossible,
the store's write-review page. Add the Settings entry the modal's Farewell step already
promises. Nothing here decides *when* the modal appears; that trigger waits for #4625.

## Scope

In:
- Native in-app review on iOS, Mac Catalyst, Android and Windows.
- Store-link fallback everywhere a native path is missing or fails, including macOS
  AppKit.
- Wiring the modal's "Leave a review" and "Invite friends" buttons.
- A "Rate Voxt" tile in Settings → App settings.
- Test-page buttons for each path.
- Localizing the modal's strings.

Out:
- The trigger (when to show the modal) and prompt-state persistence. Nothing reads
  prompt state until the trigger exists, so nothing writes it yet.
- Web: no store to rate, the feature is invisible there.
- Any change to the modal's copy or art.

## Contract

`src/dotnet/UI.Blazor/Services/AppReview/IAppReviewer.cs`, following the `IFileSaver`
convention: implemented only where a native path exists; absent means fallback.

```csharp
public interface IAppReviewer
{
    Task<AppReviewOutcome> RequestReview(CancellationToken cancellationToken);
}

public enum AppReviewOutcome
{
    Requested,  // the OS was asked; whether it showed anything is unknowable (iOS, Android)
    Completed,  // the user submitted a rating (Windows only reports this)
    Cancelled,  // the user dismissed the native dialog (Windows only reports this)
    Failed,     // the native path is unusable here; the caller falls back to a link
}
```

Implementations never throw. Any exception maps to `Failed` and is logged at warning.

## Policy service

`src/dotnet/UI.Blazor.App/Services/AppReview/AppReviewUI.cs`, a scoped
`UIServiceBase<UIHub>` registered in `BlazorUIAppModule`.

- `bool IsAvailable` — true when `HostInfo.HostKind.IsMauiApp()` and
  `Links.Apps.Review(HostInfo.AppKind)` is non-null. False on web.
- `Task<AppReviewOutcome> LeaveReview(CancellationToken)`:
  1. If an `IAppReviewer` is registered, await it.
  2. If the outcome is `Failed`, or there is no reviewer, open the review link through
     `Hub.ExternalUrlOpener` and return `Requested`.
  3. Otherwise return the reviewer's outcome.

Review links are added next to the store links in `Links.Apps` (`src/dotnet/Api/Links.cs`):

| AppKind | Link |
|---|---|
| Ios | `https://apps.apple.com/app/id6450874551?action=write-review` |
| Android | the https Play page (`Links.Apps.Android`); `market://` ends in a store chooser on phones with RuStore etc. |
| Windows | `ms-windows-store://review/?ProductId=9N6RWRD9FMS2` |
| MacOS (Catalyst and AppKit) | `macappstore://apps.apple.com/app/id6450874551?action=write-review` |
| Wasm / Unknown | null |

`MauiExternalUrlOpener` already routes non-http schemes through `Launcher`, so `market://`,
`ms-windows-store://` and `macappstore://` need no new plumbing.

## Platform implementations

Registered in the per-TFM `ConfigureBlazorWebViewAppPlatformServices` partials.

**iOS and Mac Catalyst** — `src/dotnet/App.Maui/MaciOS/AppleAppReviewer.cs`. On the main
thread, take the key window's `WindowScene` (same lookup as `ApplePasskeyClient`) and call
`SKStoreReviewController.RequestReview(scene)`. Returns `Requested`. The API is deprecated
on iOS 18 in favor of a Swift-only replacement, so it stays the only option for .NET; it is
functional. Apple rate-limits the prompt to three per year per app and may show nothing.

**Android** — `src/dotnet/App.Maui/Platforms/Android/AndroidAppReviewer.cs`. Adds
`Xamarin.Google.Android.Play.Review` 2.0.2.5 to `Directory.Packages.props` and the
Android TFM's package references. `ReviewManagerFactory.Create(context)` →
`RequestReviewFlow()` → `LaunchReviewFlow(MainActivity.Current, info)`, both awaited
through the Play task-to-Task bridge. Returns `Requested`; any exception, including the
"not installed from Play" case, returns `Failed`. Google rate-limits the card and shows
nothing when the quota is spent.

**Windows** — `src/dotnet/App.Maui/Platforms/Windows/WindowsAppReviewer.cs`.
`StoreContext.GetDefault()`, initialized with the main window handle via
`WinRT.Interop.InitializeWithWindow` (handle obtained as in `WindowConfigurator`), then
`RequestRateAndReviewAppAsync()`. Status maps: `Succeeded` → `Completed`,
`CanceledByUser` → `Cancelled`, `NetworkError` and `Error` → `Failed`. Requires a
Store-installed MSIX; an unpackaged or side-loaded build fails and falls back. The WinRT
types and their ABI stubs get `CodeKeeper.Keep` entries in `MauiAppAotSource.cs` next to
the StartupTask block, since the Windows build is Native AOT.

**macOS AppKit** — no reviewer registered. The AppKit app is not on the Mac App Store yet;
the fallback link points at the Catalyst listing under the same id. When the AppKit app
ships on the store, a reviewer calling the parameterless
`SKStoreReviewController.RequestReview()` joins the others.

**Web** — nothing registered; `IsAvailable` is false.

## Modal and Settings wiring

`AppReviewModal.razor`:
- "Leave a review" (both steps): await `AppReviewUI.LeaveReview()`; then `ThankYou`,
  except `Cancelled` → `Farewell`. The button is disabled while the call is in flight.
- "Invite friends": close the modal, then `ShareUIExt.ShareOwnAccount(...)`.
- Strings move to the localization resources (`Localization/Resources`), matching the
  rest of Settings.

`AppSettings.razor`: a `TileTopic` + `ButtonTile` "Rate Voxt", rendered only when
`AppReviewUI.IsAvailable`, calling `LeaveReview()` directly. The modal is a pitch for
users who did not ask; a user who opened Settings to rate does not need the pitch.

`AppReviewTestPage.razor` (admin-only): keeps "Show review modal" and adds "Request
native review" (shows the outcome) and "Open store review link".

## Error handling

- Reviewer exceptions never surface: `Failed` plus a warning log, then the link.
- If the link cannot be opened (no handler), `ExternalUrlOpener` already reports; the
  modal still proceeds to `ThankYou` because the user's intent was fulfilled as far as
  we can act on it.
- No user-visible error state is added to the modal.

## Testing

- `AppReviewUITest` (UI.Blazor.App tests): fake `IAppReviewer` and fake
  `ExternalUrlOpener`; covers reviewer absent → link, reviewer `Failed` → link, reviewer
  `Requested`/`Completed`/`Cancelled` → passthrough without link, and `IsAvailable` per
  `AppKind`.
- Device passes, recorded in the PR:
  - iOS: the native sheet appears on a development build (nothing is submitted).
  - Mac Catalyst: same.
  - Android: an internal-testing build from Play shows the card; a side-loaded APK
    exercises the `market://` fallback.
  - Windows: a Store-installed build shows the native dialog; a local build exercises
    the `ms-windows-store://review/` fallback.
  - macOS AppKit: the `macappstore://` link opens the Catalyst listing.

## Reuse

Existing: `IFileSaver` pattern and its per-TFM registration; `Links.Apps` and
`AppUpdateUI.Update()` for store links; `MauiExternalUrlOpener` custom-scheme routing;
`ApplePasskeyClient` key-window lookup; `WindowConfigurator` window-handle lookup;
`MauiAppAotSource` keep list; `ShareUIExt.ShareOwnAccount`; `AppSettings.razor` tile
pattern with `GetService` null-gating; `ModalUI`.

New, placement: `IAppReviewer` + `AppReviewOutcome` in `UI.Blazor` (shared, no app deps,
same home as `IFileSaver`); `AppReviewUI` in `UI.Blazor.App` (needs `Links` and
`HostInfo`, app-specific policy); platform reviewers in `App.Maui`. Nothing here belongs
in `Core`.
