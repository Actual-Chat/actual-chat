# App Localization Plan (MVP: one screen, few languages)

**Date:** 2026-07-12
**Branch:** `feat/3721-app-localization`
**Status:** Draft — supersedes the earlier `.resx`-based draft (see §8)

> **Amendment (2026-07-13):** the design below evolved. `UILanguageUI` and `UILanguageState`
> no longer exist — everything lives on the existing `LanguageUI`
> (`Services/LanguageUI/LanguageUI.cs`). The UI language is **device-local** (like theme), so
> the chosen value persists in local settings via a `StoredState<string>` (KVAS key
> `"UILanguage"`), **not** the synced `UserLanguageSettings`. `AppStringLocalizer` reads
> `LanguageUI.UILanguage` directly — a synchronous property returning the chosen language
> when set, else the browser-detected default (`_detectedUILanguage`), mirroring theme's
> `currentTheme = theme ?? defaultTheme`. The default is detected once at startup by reusing
> `LanguageUI.GetClientLanguages()` (gated on `BrowserInfo.WhenReady`); `SetUILanguage` writes
> the local store and awaits its persistence before the picker reloads. Read "`UILanguageUI`" /
> "`UILanguageState`" below as "the UI-language members of `LanguageUI`".

## 0. TL;DR

- **Goal of this MVP:** localize exactly **one screen — the Settings modal** — into a
  small set of popular LTR languages, with a language picker the user can flip.
  No RTL, no attempt to cover the whole app.
- **Architecture:** a **custom JSON-file string localizer** (`IStringLocalizer<Strings>`
  backed by embedded `Strings.<lang>.json`). This is *not* the textbook `.resx`
  approach and that is deliberate — see §1.
- **Reuse the stash, don't trust it verbatim.** `stash@{0}` ("localization") already
  prototypes this exact design. It **no longer applies cleanly** (the branch moved
  on), and its own plan doc contradicts its code, so we lift the *good parts* and
  re-apply them by hand against current `HEAD`.

## 1. The one constraint that drives everything: `InvariantGlobalization`

`Directory.Build.props:101` sets:

```xml
<InvariantGlobalization>true</InvariantGlobalization>
```

This is global for every project (WASM, MAUI, server). With invariant globalization:

- There is effectively one culture (invariant). `CultureInfo`-based APIs collapse to it.
- **Satellite assemblies / `.resx` resource fallback by culture do not resolve** the
  way the standard `Microsoft.Extensions.Localization` `ResourceManagerStringLocalizer`
  expects. The canonical "add `.resx`, inject `IStringLocalizer<T>`, set the thread
  culture" recipe silently fails to switch languages.

**Consequence:** we cannot use the standard `.resx` stack. We provide our **own**
`IStringLocalizer<Strings>` implementation that:

1. loads translations from **embedded JSON** (`Strings.en.json`, `Strings.es.json`, …), and
2. chooses the active language from an **app-owned scoped state** (`UILanguageState`),
   not from `CultureInfo.CurrentUICulture`.

This is exactly what the stash already does, and it is the right call. The earlier
plan draft's headline recommendation ("`.resx` + `IStringLocalizer`") is wrong for
this codebase and should be ignored.

## 2. Scope of this MVP

| Dimension | This MVP | Explicitly out of scope |
|-----------|----------|-------------------------|
| Screens | **Settings modal only** (`SettingsModal.razor` + the tab components it renders) | Every other screen (chat, onboarding, landing, recorder, …) |
| Languages | **en (default), es, fr, it, ru, de** | The other ~8 from the stash (zh, hi, pt-BR, ja, ko, tr, vi, uk) — trivial to add later |
| Direction | LTR only | RTL (Arabic/Hebrew/…) — no bidi/`dir` work at all |
| String types | Plain + parameterized (`"Quit {0}"`) | Pluralization (CLDR), locale date/time/number formatting |
| Server error / MAUI-native strings | none | `StandardError`, `Info.plist`, `strings.xml`, etc. |

Rationale: the Settings modal is a **self-contained screen**, it is the **natural home
for the language picker**, and the stash already contains vetted translations for its
strings — so it is the cheapest possible first slice that proves the whole pipeline
end-to-end.

## 3. Reuse (mandatory section)

### 3.1 Existing abstractions / prior work to reuse

- **`stash@{0}` ("On feat/3721-app-localization: localization")** — the primary source.
  Salvage these files (re-apply against current `HEAD`, do **not** `git stash apply` —
  it conflicts):
  - `src/dotnet/UI.Blazor.App/Resources/AppStringLocalizer.cs` — the custom
    `IStringLocalizer<Strings>` that reads embedded JSON and honors `UILanguageState`.
    Keep as-is.
  - `src/dotnet/UI.Blazor.App/Resources/Strings.cs` — the empty marker type
    `Strings` used as the `IStringLocalizer<Strings>` generic arg. Keep. (Drop its
    stale "resolve .resx" XML comment — see coding-style note in §7.)
  - `src/dotnet/UI.Blazor.App/Services/UILanguageState.cs` — scoped current-language
    holder + cookie parse/format helpers. Keep, but **trim `SupportedLanguages` to the
    6 MVP languages** so we don't ship half-empty JSON files.
  - `UI.Blazor.App.csproj` — the `<EmbeddedResource Include="Resources\Strings.*.json"
    LogicalName="…" WithCulture="false" />` item. `WithCulture="false"` is essential:
    it stops MSBuild from treating `Strings.es.json` as a Spanish *satellite* resource.
    Keep.
  - `BlazorUIAppModule.cs` — the two DI registrations (`AddScoped<UILanguageState>()`,
    `AddScoped<IStringLocalizer<Strings>, AppStringLocalizer>()`). Keep.
  - The `Strings.en.json` … translations for **Settings** keys — reuse the values for
    en/es/fr/it/ru/de; drop the other languages and drop keys for screens we are not
    localizing in this MVP.
  - `Directory.Packages.props` — add `Microsoft.Extensions.Localization` version pin
    (we still use its `IStringLocalizer`/`LocalizedString` *types*, just not its
    resource manager).
  - `docs/tests/localization-e2e.ts` — Playwright e2e that flips language via
    cookie/query and asserts translated strings. Reuse, trimmed to the MVP languages.
- **`LanguageUI` / `UserLanguageSettings`** (`src/dotnet/UI.Blazor.App/Services/LanguageUI/`)
  — the *spoken/transcription* language system. **Orthogonal** to UI language, so we do
  **not** overload its stored value. We *do* reuse its browser-language detection: the
  `navigator.languages` JS method (`blazorApp.LanguageUI.getLanguages`) that
  `GetClientLanguages()` calls is the same source `UILanguageUI` uses to pick the default.
- **`Hub.LocalSettings`** (KVAS / localStorage, via `KvasExt.Get<T>/Set<T>`) — the standard
  local-preference store (used by `OnboardingUI`, `ChatEditorUI`, …). The UI-language
  preference lives here, exactly like theme and font size.
- **`AppScopedServiceStarter.PrepareFirstRender`** — the existing pre-first-render startup
  hook; `UILanguageUI` seeding plugs in next to `ThemeUI.Start()`.
- **`History.ForceReload`** — reused by the picker to reload the current page after a
  change so strings re-resolve. No new plumbing.

### 3.2 New components and where they live

- **`AppStringLocalizer`, `Strings`, `UILanguageState`** are UI-app-specific (they key
  off app resources and the app's Settings screen). Correct home is
  `UI.Blazor.App` — **not** `ActualChat.Core`. If a *second* UI project later needs
  localization, promote `UILanguageState` + the localizer contract to `UI.Blazor` (the
  shared Blazor project) and keep the `Strings*.json` in each leaf app. Recommendation:
  **keep local for the MVP**, revisit promotion when a second consumer appears.
- **`LanguageSettings.razor`** (the picker) is Settings-specific → stays under
  `Components/Settings/`.

## 4. Target architecture

```
Razor component
  @inject IStringLocalizer<Strings> L
        │  L["Settings_Title"]  /  L["Settings_Quit_Format", appName]
        ▼
AppStringLocalizer : IStringLocalizer<Strings>
        │  reads UILanguageState.Language, falls back to "en"
        ▼
embedded Strings.<lang>.json  (Dictionary<string,string>, loaded once, cached static)
```

Language selection (how `UILanguageState.Language` gets its value) — **no cookie, no URL,
no server involvement.** The Settings modal is opened client-side, well after startup, so
the language is purely a client-persisted preference, stored like every other local UI
setting (theme, font size, onboarding):

- A small **`UILanguageUI`** service (parallel to `FontSizeUI` / `LanguageUI`) owns the
  preference. It persists to **`Hub.LocalSettings`** (the KVAS / localStorage store) under
  key `"UILanguage"`.
- **Startup seed:** `AppScopedServiceStarter.PrepareFirstRender` (which already runs before
  the app is fully rendered, on every interactive host — Server circuit, WASM, MAUI) calls
  `UILanguageUI.Initialize()`. That reads the stored value; if none, it **detects the
  browser/device language** via the same `navigator.languages` source transcription uses
  (`blazorApp.LanguageUI.getLanguages`), maps the first supported two-letter code, and
  falls back to `en`. It writes the result into the scoped `UILanguageState` the localizer
  reads. Since this completes before first full render, the whole app (and any Settings
  modal opened later) sees the right language.
- The picker (`LanguageSettings.razor`, a 6-language `<select>`): on change calls
  `UILanguageUI.Set(lang)` (persists + updates state), then `History.ForceReload(...)` of
  the **current URL** so every already-rendered component re-resolves its strings.

> **Why not the stash's cookie/`?culture=`/`eval` approach:** it puts the language in the
> URL and a cookie and sets that cookie via `JS eval` (CSP-blocked in prod). A UI-language
> preference is client state — it belongs in local settings, same as theme/font, not in
> the address bar or a round-trip to the server. `<html lang>` stays `en`; only the
> client-rendered Settings modal is localized in this MVP, so server-side `lang` isn't
> needed (revisit if/when server-prerendered screens get localized).

## 5. String key convention

`Screen_Context_Description`, matching the stash: `Settings_Title`, `Settings_YourAccount`,
`AppSettings_AllowTelemetry`, `Settings_Quit_Format` (the `_Format` suffix marks a
`string.Format` template with `{0}` placeholders). One flat JSON object per language,
keys identical across languages, English value is the source of truth. Missing key →
localizer returns the key back (visible, greppable) and falls back to English.

## 6. Implementation steps (MVP)

1. **Plumbing (no visible change).**
   - `Directory.Packages.props`: add `Microsoft.Extensions.Localization`.
   - `UI.Blazor.App.csproj`: `PackageReference` + the `Strings.*.json` `EmbeddedResource`
     item (`WithCulture="false"`).
   - Add `Resources/AppStringLocalizer.cs`, `Resources/Strings.cs`,
     `Services/UILanguageState.cs` (6 languages).
   - Register `UILanguageState`, `UILanguageUI`, and `IStringLocalizer<Strings>` in
     `BlazorUIAppModule.InjectServices`.
   - `_Imports.razor`: add `@using Microsoft.Extensions.Localization` and
     `@using ActualChat.UI.Blazor.App.Resources`.
2. **Persistence + startup seed (client-only, like theme/font).**
   - Add `Services/UILanguageUI.cs`: persists to `Hub.LocalSettings` under `"UILanguage"`,
     detects the default from `navigator.languages` when unset.
   - Seed it from `AppScopedServiceStarter.PrepareFirstRender` (`await UILanguageUI.Initialize()`)
     so the language is set before first full render on every interactive host.
   - No server/cookie/URL changes.
3. **Resources.** Add `Strings.en/es/fr/it/ru/de.json` containing **only the Settings
   screen keys** (lift values from the stash's Settings entries).
4. **Convert the Settings screen.** For each component the Settings modal renders
   (`SettingsModal`, `AppSettings`, `UserInterface`, `YourAccount`, `SessionSettings`,
   `ApiKeySettings`, `DeveloperTools`, `ThemeSettings`, `TranscriptionSettings`,
   `DocumentsPage`, `EmailSettings`, `RenderModeSelector`, `NativeAppSettingsView`,
   `TimeZoneEditorModal`, `ApiKeyCreateFormPage`, `ApiKeyRevealPage`,
   `TranscriptionEngineSettings`): add `@inject IStringLocalizer<Strings> L` and replace
   hardcoded UI strings with `L["Key"]` / `L["Key_Format", arg]`. Re-apply the stash's
   edits by hand (it conflicts) — these files have drifted (e.g. icon classes,
   `@using …Module`), so port the *string* changes, not the whole hunk.
   - *Optional narrower slice* if even this is too much: convert only `SettingsModal`
     (tab titles + quit) + `UserInterface` (where the picker lives). Everything else can
     stay English and still demonstrates the feature. Recommend the fuller Settings
     conversion since the translations already exist.
5. **Add the picker.** `LanguageSettings.razor` (6-language `<select>`), wired into the
   **User Interface** tab (`UserInterface.razor`) next to Theme/Font. On change: persist +
   `History.ForceReload`. (The stash created `LanguageSettings.razor` but never wired it
   into any tab — close that gap.)
6. **Verify.** Build (`dotnet build ActualChat.CI.slnf`), then drive the Settings modal
   in the browser: default English, switch to es/ru, confirm strings change and persist
   across reload, `?culture=fr` overrides. Reuse/trim `docs/tests/localization-e2e.ts`.

## 7. Coding-style / correctness notes

- Read `docs/CODING_STYLE.md` before touching C#/Razor. In particular: **no XML-doc /
  `//` comments** unless justified — the stash's files carry several restating comments
  (e.g. `Strings.cs` "resolve .resx", `AppStringLocalizer` summary); drop the ones that
  just restate the code.
- `AppStringLocalizer.this[name, args]` calls `string.Format` even when the key is
  missing (value == name); a key that happens to contain `{` would throw. Low risk for
  our keys, but guard it (skip formatting when `resourceNotFound`).
- Keep JSON keys sorted/grouped by screen so diffs stay readable; keys must match 1:1
  across all 6 files (a missing key silently falls back to English — acceptable, but a
  CI/grep check is a cheap Phase-2 add).

## 8. What changed vs. the earlier draft

The previous `app-localization.md` (in the stash) recommended **`.resx` + standard
`IStringLocalizer`** and a **14-language, whole-app, phased** rollout. That headline
architecture is **incompatible with `InvariantGlobalization=true`** and the scope was far
larger than "one screen." This draft: (a) commits to the **custom JSON localizer** the
stash's *code* actually implements, and (b) narrows scope to **one screen, six LTR
languages**. Pluralization, locale date/time formatting, server-error and MAUI-native
strings, and the remaining screens/languages are all deferred.

## 9. Phase 2+ (deferred, not part of MVP)

- Roll the same pattern out screen-by-screen (chat, onboarding, recorder, landing).
- Restore the other 8 languages (JSON files already exist in the stash history).
- Move UI-language persistence onto `UserLanguageSettings`/KVAS and auto-detect from
  `LanguageUI.GetClientLanguages()` on first run.
- CLDR pluralization service (replace English-only `.Pluralize()`), locale-aware
  date/time/number formatting.
- CI check for missing/extra keys; pseudo-localization pass to catch unlocalized strings.
- RTL support (separate effort).
```
