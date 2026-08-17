# Localization: what's left

## Goal
Finish app localization past the point reached in `feat/3721-app-localization`
(#3721), which shipped the catalog mechanism plus the app UI, server errors,
validation messages and dates. This plan is the remaining work, ordered so that
each item's prerequisite comes before it.

## Where things stand
`Strings.<lang>.json` holds 1206 keys × 14 languages, `Messages.<lang>.json`
holds 100; both are guarded by `AppLocalizationTest` (key/member correspondence,
per-language completeness, placeholder preservation) and
`ServerErrorLocalizationTest`. Consuming code goes through the typed members in
`LocalizedStringsLocalizerExt.cs` — see `docs/CODING_STYLE.md` →
"Localization (UI Strings)".

---

## 0. Deliberately dormant — enable the picker LAST

`Components/Settings/UserInterface.razor:6` wraps the App-language tile in
`@if (m.EnableIncompleteUI)`, and `LanguageUI.DetectUILanguage`
(`Services/LanguageUI/LanguageUI.cs:131-134`) returns `DefaultUILanguage`
unless the same feature flag is on:

```csharp
if (!await Hub.Features.IsIncompleteUIEnabled(cancellationToken).ConfigureAwait(false))
    return DefaultUILanguage;
```

So today every user gets English regardless of device locale, and all 1206 keys
are dormant. **This is intentional and stays that way until §3 and §4 are
done.** (§5 does not gate it — see there.)

The reason is that localization is only partly a per-surface job. A user who
switches the app to Spanish today would get a translated in-app UI while their
push notifications, the iOS share sheet, every Android permission dialog and
the OS microphone prompt all stayed English — a worse, more confusing result
than a consistently English app. The flag is what keeps the half-finished state
invisible.

**Do not flip it as part of an intermediate PR.** Enabling
`Features_EnableIncompleteUI` for the language path — or ungating it
specifically — is the *final* step, taken once push, email and the native
shells are covered.

---

## 1. In-app UI — done

The #3721 content passes localized attributes (`Title=`, `Text=`, `Label=`, …)
and markup text nodes, but missed strings living inside C# expressions in the
markup — ternaries, `??` fallbacks and locals declared in an `@{ }` block:

```razor
@{
    var joinButtonText = "Join muted";                              // ChatActivityPanel.razor:26
    var placeholder = ScreenSize.IsNarrow() ? "Message..." : "Write a message - or simply record one!";
}
<MenuEntry Text="@(IsPinned ? "Unpin message" : "Pin message")"/>   // MessageMenuContent.razor:125
```

66 such strings across 33 files were localized (37 new keys, 29 mapped onto
existing ones) — the message editor placeholder, every audio-panel toggle
tooltip, pin/unpin, `Owner`/`Moderator` in both member menus, read receipts on
mentions, the incoming-call modal, the streaming badge, the video-panel menu
and the search-result group headers.

**Keep the scan below in the loop for future passes** — an attribute/text-node
scan will not find these:

```bash
python3 - <<'PY'
import re, os
skip = re.compile(r'/Discover/|TestPage|Diagnostic|/Landing/|/Emails/|/Testing/|Admin|/Guides/')
for root in ['src/dotnet/UI.Blazor', 'src/dotnet/UI.Blazor.App']:
    for dp, _, fns in os.walk(root):
        for fn in fns:
            if not fn.endswith('.razor'): continue
            p = os.path.join(dp, fn)
            if skip.search(p): continue
            src = open(p, encoding='utf-8').read()
            i = src.find('\n@code')
            body = re.sub(r'@\*.*?\*@', '', src[:i] if i > 0 else src, flags=re.S)
            for n, l in enumerate(body.split('\n'), 1):
                if re.search(r'Log\w*\.|Justification|@using|@inherits|viewBox|\bd=', l): continue
                for m in re.finditer(r'(?:\?|:|=|\?\?)\s*"([A-Z][^"{}]{2,})"', l):
                    v = m.group(1).strip()
                    if '/' in v: continue                                    # asset ids
                    if re.match(r'^[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)+$', v): continue  # enum refs
                    if not re.search(r'[a-z]{2}', v): continue
                    if re.match(r'^[A-Z][a-z]*([A-Z][a-z]*)+$', v): continue  # PascalCase identifiers
                    print(f'{p}:{n}: {v!r}')
PY
```

Deliberately excluded and expected to stay English: brand names (`GIF`,
`Google Play`, `App Store`, `Microsoft Store`, `KLIPY`), guide-screenshot and
tutorial-slide `alt`s (`Web/Chrome/01`, `Tutorial slide #1` — asset
identifiers), diagnostics entries, `CaptchaView`'s fake-reCAPTCHA branch,
`DeveloperTools`, `PlaceInfo`'s `EnableIncompleteUI` mockup text, and the three
`ChatPropertiesMenu` entries that sit inside a `@* … *@` comment block.

---

## 2. UI language is device-local — settled, no work

`LanguageUI.UILanguage` is a `SyncedState` over `LocalSettings`
(`LanguageUI.cs:34`), and **it stays there**. There is no account-level UI
language and none is planned: a display language belongs to the device the user
is looking at, not to the account.

This was previously written up here as a prerequisite for §3 — an account field
plus device sync, a multi-device conflict policy and a fallback. That whole item
is dropped. §3 is not blocked on anything.

The consequence for the server is that anything it composes must get the
language from the surface it is addressing: from the device registration for
push (§3), and from the discussion for digests (§3).

---

## 3. Push notifications and email

Six strings, and none of them are the bulk of a push — author names, message
text and chat titles are user content and pass through untranslated:

- `Notifications.Service/NotificationHelper.cs:32,62` — `"Voice chat started"`,
  `"{names} started a voice chat"`, `"and {n} more"`,
  `"+{n} earlier message(s)"`
- `Notifications.Service/NotificationsBackend.cs:539` — `"Incoming call"` /
  `"Incoming video call"`

`"Incoming call"` also exists in `IncomingCallModal.razor:21` and again as an
English fallback in `service-worker.ts:169,172` — one shared key, not three.

### Push — render client-side wherever the platform allows

**Decision: the device renders the notification text, not the server.** The
device already knows its own language (§2), so the payload should carry
structured fields and let each platform compose. Where a platform can't, the
device's language rides along with its registration — `DbDevice` gains a
`Language` column, `Notifications_RegisterDevice` a matching field, and the app
re-registers when `LanguageUI.UILanguage` changes.

| Platform | Today | Work |
|---|---|---|
| Android | **already client-side** — `Notification = default` is deliberate (`FirebaseMessagingClient.cs:111-117`); `FirebaseMessagingService` composes via `NotificationHelper.ShowChatNotification` | replace the pre-composed `Body` key with structured ones |
| Web | SW is ours and already calls `showNotification` itself (`service-worker.ts:211`, `:169` for calls), passing the server's title/body through | compose locally; needs the catalog in the worker and the UI language reachable from it (IndexedDB/Cache, written by the app) |
| iOS | system renders `Aps.Alert{Title,Body}` while the app isn't running | **needs a Notification Service Extension** — the server half is ready (`MutableContent = true` is already set), the target doesn't exist |

**The iOS NSE comes first, in its own PR.** It is the only genuinely new piece
of machinery here, and it decides the shape of the rest: a .NET appex is
constrained (separate process, ~30s budget, ~24 MB cap — compare what
`App.Maui.IosShareExt` cost to get startup under a second), while a native
Swift NSE with `.strings` is far lighter but duplicates the catalog outside the
.NET pipeline and therefore outside `AppLocalizationTest`'s guarantees. Settle
that trade-off against a real target before touching the payload format.

Until the NSE exists, iOS falls back to server-rendered text keyed on the
device's registered `Language` — which is why the `DbDevice` column is worth
having regardless of how far client-side rendering gets.

### Email — no device, so language comes from the content

Already implemented for the part that matters: `EmailsBackend.cs:215` uses
`GetDominantLanguage(chatId, …) ?? userLanguage`, so each chat's AI summary is
generated in that chat's dominant language, falling back to
`UserLanguageSettings.Primary`.

What is not localized is the template chrome — `Users.Templates/*.razor`
(`EmailVerification`, `Digest`, `DigestChat`, `EmailBody`, `DigestButton`):
"Your email verification code", "Privacy Policy", "Terms and Conditions", the
button labels. **Open:** a digest spans chats with different dominant languages,
so the chrome needs one value the per-chat rule can't supply. Candidates are
`UserLanguageSettings.Primary` (already computed as `userLanguage` at
`EmailsBackend.cs:27,79`, and the only option that also covers
`EmailVerification`, which has no discussion to derive from), the dominant
language across the whole digest, or leaving the chrome English.

---

## 4. Native shells — mostly in-process, and only partly a "separate mechanism"

Same principle as §3: these render on the device, which knows its own locale.

The old framing here — "separate mechanisms, no catalog access" — holds for
only about a third of the list. The catalog is a **static**
`Dictionary<Language, Dictionary<string, string>>` built from resources embedded
in `UI.Blazor` (`AppStringLocalizer.Translations`, and
`StringCatalogs.Assembly => typeof(Strings).Assembly`), so any code in a process
that loads that assembly can already read it:

| Surface | Size | Process | Catalog reachable | Actual mechanism |
|---|---|---|---|---|
| Android dialogs | 9 strings, 3 files | main app | **yes** | plain C#; no `strings.xml` needed |
| Local notifications / Live Activity | ~6 strings | main app | **yes** | plain C# in `App.Maui` |
| iOS share extension (`App.Maui.IosShareExt/Components/*`) | ~18 strings | separate appex | **no** — refs `Api.Contracts` + `Maui` only | undecided, see below |
| `Info.plist` usage descriptions | 6 iOS + 5 MacCatalyst | OS reads them, app not running | **no** | `InfoPlist.strings` per language — genuinely native |

Android dialog sites: `AndroidWebChromeClient.cs:267-270`,
`AndroidNotificationsPermission.cs:44-52`, `WebViewMissingActivity.cs:28-37`.
Local-notification sites: `ChatAttentionService.cs:225-226`
(`"Chat attention required"`, `"Please check chats: …"`),
`NotificationHelper.cs:211` (`"Attention required"`) and `:43` (the `"Uploads"`
channel name), `WalkieTalkieWakeHandler.cs:109`, `IosActivitiesBackend.cs:87`
and `AndroidActivitiesForegroundService.cs:424,509` (`"Sharing live location"`).

So for the in-process two-thirds the strings are not the blocker — the
*language* is. `LanguageUI.UILanguage` is a `SyncedState` scoped to the Blazor
circuit, while this code runs on the native side (foreground service, FCM
receiver, permission dialogs, Live Activity), where no such scope exists.

### Deferred to the NSE PR — do not decide here

Two questions are open, and they are the same question §3 already defers,
because the answer to the first is what makes a .NET NSE viable or not:

1. **Where the catalog lives.** Extensions can't reference `UI.Blazor` — pulling
   the whole Blazor UI assembly into an appex is the wrong dependency for a
   target fighting startup time and bundle size (see what #4132 spent its effort
   on). Options: extract `Strings`, `StringCatalogs` and the 28 JSON resources
   into a small dependency-free assembly (`ActualChat.Core`, or a new
   `ActualChat.Localization`) that both `UI.Blazor` and the extensions
   reference; give the extensions their own `.strings` and accept a second
   translation source outside `AppLocalizationTest`'s key and placeholder
   guarantees; or leave the extensions English.
2. **How native-side code reads the current UI language.** Options: have
   `LanguageUI` publish it to a process-wide accessor on change (one owner,
   synchronous reads); read `LocalSettings` at each call site; or pass it down
   from the Blazor side, which doesn't help the sites the OS invokes directly.

Crossing the process boundary is already solved here: the share extension reads
the session id from `AppleSharedSecureStorage` (`SessionInitializer.cs:35`), so
the same shared keychain / app group can carry a language.

`Info.plist` is the one part that is settled — `InfoPlist.strings` per language,
independent of everything above, and doable at any time.

---

## 5. Landing pages and legal docs — independent of §0, and mostly legal text

`Pages/Landing/**` is completely untouched — 44 files, zero `L.` usages. The
file count hides how lopsided it is:

| Part | Files | Prose | Nature |
|---|---|---|---|
| Marketing pages | 26 | ~2,400 words | ordinary translation |
| Docs chrome (nav, headers, panels) | 14 | small | ordinary translation |
| Legal content — `DocsTermsContent`, `DocsPrivacyContent`, `DocsCookiesContent` | 3 | **~10,400 words** | liability |

The legal text is over four times the marketing prose, and it is the part that
cannot be machine-translated. Across 14 languages that is roughly 145,000 words
of professional legal translation — the single largest cost in this whole
effort, for the surface users read least. It stays a policy decision for
whoever owns legal, not an engineering one; the usual answer is to publish only
human-reviewed translations, or to keep one authoritative English version and
link to it.

### Translating marketing without per-language URLs returns nothing

The value of a translated marketing page is organic search in that language,
and nothing here is set up for that: `RootServerPage.razor:33` hardcodes
`<html lang="en">`, there is no `hreflang` anywhere in the tree, and the routes
are single-language (`/docs/privacy`, `/docs/terms`, …). A page that only
translates at runtime in the visitor's browser is invisible to crawlers in every
language but English.

So the marketing half is not "ordinary translation work, just large" — it needs
per-language routes, `hreflang` and a localized `<html lang>` before the
translation is worth commissioning. Budget that first or skip the section.

### This section does not gate §0

§0's rule is that a user who switches to Spanish must not get a half-translated
experience. Landing and legal pages sit *before* sign-in: a signed-in user
switching the app language essentially never returns to the marketing page, and
English-only legal documents are unremarkable. Gating the picker on this section
would make it wait on ~145,000 words of legal translation that has nothing to do
with the in-app experience.

**§0's gate is §3 + §4.** §5 is an independent marketing/legal track that can
run on its own schedule, or not at all.

---

## 6. Smaller items

- **Video recorder errors — done.** `video-recorder.ts` now hands C# an error
  code plus its argument instead of English prose, and `ChatVideoUI.Localize`
  maps the code onto `Video_CameraUnavailable`,
  `Video_CameraUnavailableNamed_Format` and `Video_RestartRequired`. The old
  framing ("TypeScript with no localization path") missed that this text already
  crossed into C# via `OnRecordingError` — and that the same sink was being fed
  a localized string from `ChatVideoUI.StateSync.cs:113` and an English one from
  JS. Errors we don't originate — browser `DOMException`s, the server's
  screencast message — carry an empty code and still reach the user as raw text;
  that is unavoidable without the browser translating its own messages.
- **`web-auth.ts:45` raises a raw `alert()`** when the sign-in popup is blocked.
  Unlike the video errors this has no C#-ward channel — `AccountUI.cs:144` calls
  `signIn` one-way — so it needs either a return value carrying a code or the
  localized text passed in. Small, but it touches sign-in, so it was left out of
  the pass above.
- **`service-worker.ts:169,172`** falls back to an English `'Incoming call'`.
  Genuinely catalog-less: no app, no DOM. §3's web push track puts the catalog
  in the worker, which fixes this as a side effect.
- **`App.Server` pre-language pages**: `ErrorServerPage`, `RootServerPage`
  render before the UI language is resolved. Leave English — but note
  `RootServerPage.razor:33` is where the hardcoded `<html lang="en">` lives that
  §5 flags for SEO.

---

## 7. Structural defect: errors raised from `.razor` can't be catalogued

`ServerErrorLocalizationTest.SourceText()` enumerates `*.cs` only, and
`EveryCatalogedErrorShouldExistInSource` fails any `Error_*` key whose English
value it can't find there. So an error thrown from markup is unrepresentable:
`PicCropModal.razor:120` (`"Failed to export cropped image."`) and
`FileUpload.razor:25` (`"File is too big. Max file size: {size}."`) both had
keys written for them during #3721 and both had to be dropped.

Today those messages fall through to the AI translation fallback at runtime —
they work, but cost a call and aren't deterministic. Fix by either widening the
scan to `*.razor`, or moving such literals into `.cs` helpers (as
`AttachmentList.cs:6` already does for its own file-size error).

---

## Suggested order
1. The iOS Notification Service Extension, on its own — it is the one new piece
   of machinery, and whether it is a .NET appex or a native Swift one decides
   the payload format §3 can adopt. Do it before changing any payload.
2. §3's push track — structured payload, Android and web composing locally,
   `DbDevice.Language` as the fallback for anything that still renders
   server-side.
3. §3's email track — settle the chrome language, then the 5 templates.
4. §4's `Info.plist` strings — settled and independent, doable any time.
5. §4's in-process subset (Android dialogs, local notifications, Live Activity)
   — needs only a native-side language accessor, no new mechanism.
6. §4's iOS share extension — together with, or after, the NSE: both need the
   catalog question answered the same way.
7. §7 — small, and it removes a silent AI-translation cost.
8. §0 — flip the flag once §3 and §4 are done, i.e. once a user switching
   language gets a consistently localized app, notifications and OS prompts
   included.
9. §5, independently and on its own schedule — the marketing half needs SEO
   routing before translation pays off, and the legal half needs a liability
   decision. Neither holds up §0.
