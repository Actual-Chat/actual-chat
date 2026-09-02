# Localization: what's left

## Goal
Finish app localization past the point reached in `feat/3721-app-localization`
(#3721), which shipped the catalog mechanism plus the app UI, server errors,
validation messages and dates. This plan is the remaining work, ordered so that
each item's prerequisite comes before it.

## Where things stand
`Strings.<lang>.json` holds 1304 keys × 22 languages (plus the derived `Max`),
`Messages.<lang>.json` holds 103; both are guarded by `AppLocalizationTest` (key/member correspondence,
per-language completeness, placeholder preservation) and
`ServerErrorLocalizationTest`. Consuming code goes through the typed members in
`LocalizedStringsLocalizerExt.cs` — see `docs/CODING_STYLE.md` →
"Localization (UI Strings)".

---

## 0. The picker is live — done

The App-language picker used to sit behind `EnableIncompleteUI`, and
`DetectUILanguage` returned English outright unless that flag was on, so every
user got English regardless of device locale. Both are gone: the tile in
Settings → User Interface is unconditional, and the language now lives on the
account as `UserLanguageSettings.UILanguage` (`null` = follow the device), cached
per device so the first frame never waits on the network. See
[i18n.md → Where the UI language comes from](../i18n.md#where-the-ui-language-comes-from)
for how it resolves and why a change takes effect after a manual reload.

**The gate was removed before §3 and §4 were finished, deliberately.** What it
was protecting against is still true and still open: a user who switches to
Spanish gets a translated in-app UI while push notification text, the iOS share
sheet, Android permission dialogs and every OS prompt stay English. Those are §3
and §4 below, and they are now the visible gap rather than a hidden one. §5 does
not gate anything — see there.

Two things the switch left open, both cheap and both worth doing before the
inconsistency is widely noticed:

- **Guest → sign-in.** `ServerKvas.MigrateGuestKeys` moves guest settings onto
  the user prefix, so a language chosen while signed out now migrates together
  with the spoken languages. That is probably right, but nobody has decided it.
- **First start on a fresh device, already signed in.** The cache is empty, so
  the app renders auto-detected. When the account value arrives it is cached, but
  the running app keeps its startup language; the selection takes effect on the
  next manual reload or app start.

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

### The second pass (#4233), and why one was needed

That scan read only the markup half of a `.razor` file, so anything below
`@code` — a `switch` helper, a `[Parameter]` default, a `ToastUI.Show` argument,
a `StringBuilder` composing share text — stayed English. ~60 strings across 30
files did, including the search filter badges (`Chat`, `Place`, `People`,
`Groups`, `Messages`), the sidebar's `Chats`/`Unread` headers, the whole phone
verifier, the join-video modal's camera states, `Owner`/`Moderator`/`Your
profile` in the *place* member lists (the chat ones were fixed in #3721), the
mention picker's filter chips, and six date patterns spelled out at the call
site instead of taken from `Date_*`.

34 new keys; the rest mapped onto keys that already existed, which is itself the
tell — half of these were duplicated English next to a key that already said the
same thing.

Deliberately excluded and expected to stay English: brand names (`GIF`,
`Google Play`, `App Store`, `Microsoft Store`, `KLIPY`), guide-screenshot and
tutorial-slide `alt`s (`Web/Chrome/01`, `Tutorial slide #1` — asset
identifiers), diagnostics entries, `CaptchaView`'s fake-reCAPTCHA branch,
`DeveloperTools`, `PlaceInfo`'s `EnableIncompleteUI` mockup text, the three
`ChatPropertiesMenu` entries that sit inside a `@* … *@` comment block, and chat
titles that become persisted data (`Notes`, a Place's `Welcome` chat,
`Anonymous chat (<date>)`).

### The runtime check — `ui-localization-smoke.test.ts`

`tests/ts/e2e/ui-localization-smoke.test.ts` is the standing check, and it works from the
rendered page rather than from source: for each of the 13 non-English UI languages it
switches the app over, walks the tour (chat list, three menus, chat view, the chat side
panel and its tabs, nine settings tabs, the search panel with its filter menu and badges,
the mention list, the unavailable-chat page) and compares every visible text node and
`title`/`aria-label`/`placeholder`/`data-tooltip` against the English catalog. Text
matching an English value whose translation differs is an unlocalized string, reported
with the key that produced it. The reverse check runs too — each screen must show some
*translated* text — so a language that silently failed to apply fails the test instead
of passing vacuously.

```bash
AC_E2E_SERVER=external npx vitest run tests/ts/e2e/ui-localization-smoke.test.ts --config vitest.config.e2e.ts
# one or more languages: AC_E2E_LANGUAGES=ru,ja
```

Its first run found three misses no source-reading scan could have caught, all fixed
here: `StatusBadge` composed "Public chat"/"Private chat"/"Place chat" (plus a
concatenated " thread") and "Online"/"Away"/"Offline" in `ComputeState`;
`LeftChatSearchInput` and `SearchPanel` defaulted `Placeholder` to `"Search"`; and
`PresenceFragments.PresenceText` held "Speaking"/"Last seen"/"Offline".

That last one is the interesting case. Both presence fragments **were** components until
`2e085ab24b` ("optimize author presence rendering", #2450) collapsed them into static
`RenderFragment`s — they render per chat entry, and a fragment costs no instance, no parameter
diff and no lifecycle. A fragment also has no way to resolve a service, which is why the text
was still English. It stays a fragment; the localizer is passed in as a fourth tuple element
(`RenderFragment<(Presence, Moment?, bool, IStringLocalizer)>`) rather than re-componentizing
it. **Don't "fix" that by making it a component again** — read #2450 first.

Two things it cannot see, and #4233 measured both. **Text hardcoded *and* absent
from the catalog** has nothing to match against: of that pass's findings, roughly
half (`Unread`, `Search...`, `In call`, `Notifications panel`, `Verify by SMS`,
`Camera is off`, the four mention chips, …) were invisible to it no matter which
screen it visited — reading the source is the only way to find that half.
`DownloadAppBanner`'s `Get @AppName App`, called out here as one such string, is
now `Download_GetAppBanner_Format`.

**Screens off the tour** were the other half, and that is where the reported bug
lived: the search panel was never visited, so `Place` sat in the filter badge in
every language. The tour now also walks the search panel, its filter menu, the
filter badges, the mention list and the unavailable-chat page, plus the chat side
panel's own tabs; steps that depend on account state this test doesn't create (a
chat with members, a Place) are marked `isOptional` and skipped rather than
failed. Adding a screen is still one entry in `TOUR`.

The `right-panel` step had been failing silently against a stale `.right-panel`
selector — the component is `.chat-side-panel` — so nothing it covered was
actually being checked. Fixed in the same pass.

The Notes chat title is the one deliberate exception (`KnownEnglish` in the spec):
`ChatsBackend` creates that chat with the literal title "Notes" and the user can rename
it, so it is data, not a string.

### System entries — localized at the render site

"X has joined the chat." and "X asked for attention." were the last in-app strings
composed in C# rather than read from the catalog, and they were composed by the
data contract itself: `SystemEntry.ToMarkup()`, implemented on `MembersChangedEntry`
and `NotifyMembersEntry`. Nothing is persisted — `DbChatEntry` stores the structured
payload (author id, name, `HasLeft`) and leaves `ChatEntry.Content` empty for system
entries — so this was purely a render-site fix, and old entries re-render in whatever
language is current.

`ToMarkup()` is gone from the records. `SystemEntryMarkupBuilder`
(`Api/Chat/Markup/`) dispatches on the entry kind — the shape the markup visitors
in that folder already use — to one private method per kind, each building its
markup directly, exactly as the old `ToMarkup()` did. The words are abstract
members; `LocalizedSystemEntryMarkupBuilder` (`ActualChat.Localization`) supplies
them from the catalog, and there is no English copy in C# (#4339). There is
deliberately **no shared "build an author sentence" helper**: the two kinds
resemble each other only by coincidence today, and a kind carrying two names, a
count, or no author at all should not be forced through one shape.

The catalog values are **suffixes only** — what follows the author name — not the
`_Prefix`/`_Suffix` pair used elsewhere. In all 14 languages the name is the
sentence's subject, so every prefix was empty, and an empty `PlainTextMarkup` is not
free: `PlainTextMarkupView` is a `ComputedStateComponent` that runs a search-match
lookup, so each one costs a component and a computed state on every system entry.
The cost is that a translation cannot put words before the name ("Willkommen, X!");
add the prefix back for that language's sake if it ever comes up.

`IChatMarkupHub` carries the builder, non-nullable: `ChatMarkupHub` resolves the
localized one from the circuit's services, `BackendChatMarkupHub` returns the one
for `Languages.Main`. That leaves notifications, digests and content
links rendering English on purpose — that language belongs to the recipient, so it
is §3's work, not this section's — and `TranscriptionContextSource` keeps English
deliberately, being LLM prompt text. **The builder is a hub property rather than an
optional service** because `BackendChatMarkupHub` is a *singleton over the root
provider*: a `Services.GetService<…>()` lookup there would either trip scope
validation or hand back a circuit-less UI service.

Four `SystemEntry_*` keys × 14 languages, carrying a `//` note for translators.
`SystemEntryLocalizationTest` renders every kind in every shipped language and
enumerates `SystemEntry`'s `[Union]` subtypes, so a new kind cannot reach the
builder's `_ => Markup.EmptyText` arm unnoticed.

---

## 2. UI language is account-level — done

This section used to argue the opposite: that a display language belongs to the
device, not the account, and that no account field was planned. That was
reversed and shipped — `UserLanguageSettings.UILanguage` (`null` = follow the
device), cached per device in `localStorage` so the first frame never waits on
the network. [i18n.md → Where the UI language comes from](../i18n.md#where-the-ui-language-comes-from)
is the reference; the reversal was about the user, who reads one language on
every device they own, not about which surfaces are localized.

**What it changed for §3:** the server can read the recipient's language
directly. The one gap — recipients left on auto, where only the device knows the
locale — was closed by having the client write that resolution into
`UserLanguageSettings.DetectedUILanguage` rather than by adding
`DbDevice.Language`, so the whole chain is account-level. It also settles §3's
open question about digest chrome: `UILanguage` is a better answer than
`UserLanguageSettings.Primary`, which stays what it should be, a *spoken*
language.

**What it leaves for §4:** native surfaces still can't read the selection —
`localStorage` is unreachable from the FCM receiver, the Android foreground
service and the Live Activity. Mirroring the resolved language into
`LocalSettings` (or `MauiPreferences`) from `LanguageUI.SetStoredLanguage` gives
native code a process-wide accessor, which is §4's second open question with an
obvious answer now.

---

## 3. Push notifications and email

### Push — composed on the server, in the recipient's language — done (#4125)

**This reverses the decision this section used to carry** ("the device renders
the notification text, not the server"), and the reversal rests on a factual
correction. That decision's table said Android already composed client-side. It
does not: `FirebaseMessagingClient.cs` puts the server-composed `Title`/`Body`
into `renderData`, and `NotificationData.cs` renders exactly those. Web
(`service-worker.ts`) and iOS (`Aps.Alert`) do the same. All three consume server
text today, so composing per recipient on the server is **one** change instead of
three — and it also fixes the in-app notification list, which renders
`Notification.Text` verbatim and which client-side push rendering would never
have reached.

What shipped:

- `ActualChat.Localization` — the catalog extracted out of `UI.Blazor` into a
  dependency-free assembly, plus `LanguageStringLocalizer`, an `IStringLocalizer`
  bound to an explicit language rather than to the Blazor circuit.
- `UserLanguageSettings.DetectedUILanguage` — what "follow the device" currently
  resolves to, written by the client. `UILanguage` is `null` for everyone who
  never opened the picker, and nothing server-side can resolve that on its own.
- `NotificationsBackend` composes `Notification.Text` per recipient, resolving
  `UILanguage ?? DetectedUILanguage ?? English` through `ServerKvasBackend`,
  beside the notification mode it already reads per recipient.

Consequences worth recording:

- **The iOS Notification Service Extension is no longer a prerequisite.** It
  remains worth having for other reasons — rich attachments, on-device
  decoration — but it is now an independent item, not a gate on localized push.
- **`DbDevice.Language` was not added and is not needed.** `DetectedUILanguage`
  covers the auto case for the whole account, and a per-device language would
  fight the single `Notification.Text` that `SendMessage` multicasts to all of a
  user's devices — and disagree with the in-app text besides.
- **Text is composed at fan-out and persisted per user**, so a notification
  composed before a language change stays in the old language. Re-rendering
  history would mean storing structured payloads for every notification kind.
- `Call_Incoming` / `Call_IncomingVideo` are shared with the UI rather than
  duplicated, and the name-joining reuses `Conversation_TwoNames_Format`.

**The follow-up #4125 missed: text the markup layer substitutes.** An entry with
no text of its own never reached a catalog key - it reached
`ChatMarkupHubExt.GetEmptyMarkupReplacement`, which composed an English sentence
inside a `PlainTextMarkup`, and that string was user content as far as
`EnqueueMessageRelatedNotifications` could tell. It is now
`EmptyEntryMarkupBuilder`, carried on `IChatMarkupHub` exactly as
`SystemEntryMarkupBuilder` is, with the same split: the render site takes the
viewer's language, the fan-out takes the recipient's.

- **Render sites** - `ChatMarkupHub` resolves `LocalizedEmptyEntryMarkupBuilder`
  from the circuit, so quotes, the pinned bar and chat-list previews are
  localized. `BackendChatMarkupHub` returns the one for `Languages.Main`, which
  is what leaves `ContentLinksBackend` (a preview cached per content id, with no
  reader to have a language) and `TranscriptionContextSource` (LLM prompt text)
  in English on purpose.
- **The fan-out** - `EmptyEntryNotificationContent.Render` gets a
  `LocalizedEmptyEntryMarkupBuilder` per language, so the wording comes from the
  same place rather than being restated in the notification layer.
- `EmptyEntryLocalizationTest` renders every case in every shipped language.
  The English literals the base class used to carry, and the test that kept them
  equal to the catalog, are gone (#4339): the builder in `Api` owns the cases and
  reads every word through an abstract member.

**Detection is a property of the entry, not of the parse.** The parser yields its
empty result only for empty content, so whether an entry has text of its own is
known before any markup exists: a location, or empty content. `NotificationTextComposer`
tests exactly that and returns either the author's words as shared content or an
`EmptyEntryNotificationContent` that re-words per reader; `NotificationsBackend`
never looks at the entry kind itself. That single gate is also what dropped the
`!entry.HasLocation` special case that kept the reaction path from quoting a
maps-link fallback as if it were the author's words.

A location push also distinguishes a live share from a one-shot pin now, which the
chat list had always done and the push never did: `NotificationTextComposer` reads
`SharedLocation.Duration` once per entry and passes the fact to `Build`, so
`EmptyEntry_SentLiveLocation` reaches the recipient instead of a pin's wording. The
chat list now goes through the same `Build` call, so its own `ChatList_SentLocation`
and `ChatList_SharedLiveLocation` keys were deleted - in seven catalogs they had been
translated as a gendered past tense ("Poslao lokaciju") where the `EmptyEntry_*` forms
are neutral participles ("Poslana lokacija"), which is the wording a sender of unknown
gender needs. Quotes and the pinned bar still say "Sent a location" for a live share -
they'd need the same async lookup inside the markup hub, which is more than that path
should do.

The English was restructured before being translated. `Sent 2 images and 2 files`
conjoined clauses whose parts must agree in case, which no `" and "` concatenation
survives; a mixed set is now named by its total (`Sent 4 attachments`), and a
homogeneous one by its kind (`Sent 2 images`). The reaction line drops the count
entirely - `your images`, not `your 2 images` - because it names a target rather
than reporting a quantity.

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
`EmailsBackend.cs:27,79`), the dominant language across the whole digest, or
leaving the chrome English.

**#4125 answered this.** The chrome language is
`UILanguage ?? DetectedUILanguage ?? English`, the same resolution the
notification path uses, and `LanguageStringLocalizer` is the mechanism — no new
machinery, and it covers `EmailVerification`, which has no discussion to derive
a language from. `UserLanguageSettings.Primary` is not a candidate: it is a
*spoken* language. What is left here is the template work itself.

---

## 4. Native shells — mostly in-process, and only partly a "separate mechanism"

Same principle as §3: these render on the device, which knows its own locale.

The old framing here — "separate mechanisms, no catalog access" — holds for
only about a third of the list. The catalog is a **static**
`Dictionary<Language, Dictionary<string, string>>` built from resources embedded
in `ActualChat.Localization` (`StringCatalog.Translations`, and
`StringCatalogs.Assembly => typeof(Strings).Assembly`), so any code in a process
that loads that assembly can already read it:

| Surface | Size | Process | Catalog reachable | Actual mechanism |
|---|---|---|---|---|
| Android dialogs | 9 strings, 3 files | main app | **yes** | plain C#; no `strings.xml` needed |
| Local notifications / Live Activity | ~6 strings | main app | **yes** | plain C# in `App.Maui` |
| iOS share extension (`App.Maui.IosShareExt/Components/*`) | ~18 strings | separate appex | **yes, since #4125** — it references `ActualChat.Localization` | **done (#4261)** — `AppStrings.L`, language from the App Group |
| `Info.plist` usage descriptions | 6 iOS + 5 MacCatalyst | OS reads them, app not running | **no** | `InfoPlist.strings` per language — genuinely native |

Android dialog sites: `AndroidWebChromeClient.cs:267-270`,
`AndroidNotificationsPermission.cs:44-52`, `WebViewMissingActivity.cs:28-37`.
Local-notification sites: `ChatAttentionService.cs:225-226`
(`"Chat attention required"`, `"Please check chats: …"`),
`NotificationHelper.cs:211` (`"Attention required"`) and `:43` (the `"Uploads"`
channel name), `WalkieTalkieWakeHandler.cs:109`, `IosActivitiesBackend.cs:87`
and `AndroidActivitiesForegroundService.cs:424,509` (`"Sharing live location"`).

So for the in-process two-thirds the strings are not the blocker — the
*language* is. `LocalizationUI.Language` is scoped to the Blazor
circuit, while this code runs on the native side (foreground service, FCM
receiver, permission dialogs, Live Activity), where no such scope exists.

### Both questions answered

The catalog question that used to gate this section — and §3 with it — was
answered by #4125; the language question, by #4261:

1. **Where the catalog lives — answered by #4125.** `ActualChat.Localization`
   exists: dependency-free (`Api` + `Microsoft.Extensions.Localization`), it owns
   `Strings`, `StringCatalogs` and the embedded JSON, and the 2.2 MB of catalogs
   left `UI.Blazor` rather than being duplicated. An extension can reference it
   without pulling in the Blazor UI assembly — the dependency #4132 and #4214
   spent their effort avoiding. The namespace was `ActualChat.UI.Blazor.Resources`
   until it was renamed to `ActualChat.Localization` to match the
   assembly.
2. **How native-side code reads the current UI language — answered for iOS by
   #4261.** `MauiBrowserInfo.OnInitialized` mirrors `BrowserInfo.UILanguage` into
   `MauiPreferences.UILanguage`, which on iOS is the App Group container the app
   and its extensions share — the same crossing the session id already makes
   through `AppleSharedSecureStorage` (`SessionInitializer.cs:35`). The share
   extension reads it back through `AppStrings.L` and falls back to
   `NSLocale.PreferredLanguages` until the app has run once. The in-process sites
   (Android dialogs, local notifications, Live Activity) can read the same
   accessor rather than reaching for the Blazor circuit.

`Info.plist` is the one part that is settled — `InfoPlist.strings` per language,
independent of everything above, and doable at any time.

---

## 5. Landing pages and legal docs — mostly legal text

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

### This section never gated the picker

§0's rule was that a user who switches to Spanish must not get a half-translated
experience. Landing and legal pages sit *before* sign-in: a signed-in user
switching the app language essentially never returns to the marketing page, and
English-only legal documents are unremarkable. Gating the picker on this section
would make it wait on ~145,000 words of legal translation that has nothing to do
with the in-app experience.

**§0's gap is §3 + §4.** §5 is an independent marketing/legal track that can
run on its own schedule, or not at all.

---

## 6. Smaller items — deferred, but the approach is settled

### The catalog loads every language, on every host

`StringCatalog` builds its `Language -> keys` map eagerly, so the first string
lookup parses **all 46 embedded resources — 2.14 MB, 32,338 entries across 23
languages**. A client reads exactly two of them: the selected language and the
English fallback, 1,406 entries. It is on the startup path, since the field is a
`static readonly` initializer and the first lookup happens during the first
render.

This predates the catalog's move out of `UI.Blazor` (`AppStringLocalizer` was
eager too), so it is a standing cost on every WASM and MAUI launch rather than a
regression. The server wants laziness for the same reason, just less visibly: it
needs the languages its users actually have, discovered at runtime.

**Agreed approach:** a `ConcurrentDictionary<Language, Dictionary<string, string>?>`
filled per language on first use, caching `null` for a language that ships no
catalog. Per-lookup cost is unchanged — one hash lookup either way — and the
English fallback is just a second `Get`. Its own PR: it is a runtime change with
nothing to do with any one feature.

The old framing, "TypeScript with no localization path", is wrong for the video
recorder: its text already crosses into C#. `describeStartError`'s result travels
through `blazorRef.invokeMethodAsync('OnRecordingError', …)` into
`ChatVideoUI.OnRecordingError`, and that same method is already handed a
*localized* string by `ChatVideoUI.StateSync.cs:113`
(`L.Video_FailedToStartRecording`) and an English one by JS. One sink, two
languages — so this needs no TS-side catalog, only a protocol.

**Agreed approach: JS sends an error code plus its argument, C# localizes.**
Prototyped and verified (`tsc`, `eslint`, `npm run build:Verify`,
`dotnet build`, `AppLocalizationTest` 15/15), then pulled back out to keep this
branch docs-only. The implementation is preserved on the local branch
`wip/l10n-video-error-codes` (commit `1e14cc0ce2`) — reuse it rather than
redoing the work:

- `video-recorder.ts` gains `RecordingErrorCodes`, a `CodedError` carrying a
  code through a `throw`, and a `RecordingError { code, arg, message }` returned
  by `describeStartError`; the four `OnRecordingError` call sites pass the
  triple.
- `ChatVideoUI.Localize(code, arg, message)` maps `cameraUnavailable` /
  `restartRequired` onto three new keys — `Video_CameraUnavailable`,
  `Video_CameraUnavailableNamed_Format`, `Video_RestartRequired`. The camera
  label rides as an argument instead of being interpolated into an English
  sentence no translation could follow.
- The raw message still travels beside the code, because
  `IsScreenCastAlreadyActiveError` string-matches the *untranslated* wording of
  the server's "Another screencast is already active"
  (`LiveVideoBackend.cs:92`) to decide whether to show the modal.
- Errors we don't originate — browser `DOMException`s — carry an empty code and
  reach the user as raw browser text. Not fixable from our side.

**Trap worth knowing:** a file using the typed localizer members needs
`using ActualChat.Localization;`. Without it every member is invisible and
the compiler reports CS1061 naming the *key* ("`IStringLocalizer` does not
contain a definition for `Video_CameraUnavailable`"), which reads like a bad
catalog entry or a stale build. `ChatVideoUI.Recording.cs` had this exact
failure.

Still open, and not prototyped:

- **`web-auth.ts:45` raises a raw `alert()`** when the sign-in popup is blocked.
  Unlike the video errors this has no C#-ward channel — `AccountUI.cs:144` calls
  `signIn` one-way — so it needs either a return value carrying a code or the
  localized text passed in. Small, but it touches sign-in.
- **`service-worker.ts:169,172`** falls back to an English `'Incoming call'`.
  Genuinely catalog-less: no app, no DOM. §3's web push track puts the catalog
  in the worker, which fixes this as a side effect.
- **`App.Server` pre-language pages**: `ErrorServerPage`, `RootServerPage`
  render before the UI language is resolved. Leave English — but note
  `RootServerPage.razor:33` is where the hardcoded `<html lang="en">` lives that
  §5 flags for SEO.

---

## 7. Errors raised from `.razor` — resolved, and the rule is now written down

`ServerErrorLocalizationTest.SourceText()` enumerates `*.cs` only, and
`EveryCatalogedErrorShouldExistInSource` fails any `Error_*` key whose English
value it can't find there. During #3721 that read as a defect: keys for
`PicCropModal.razor:120` and `FileUpload.razor:25` were written and then had to
be dropped, and this section proposed widening the scan to `*.razor`.

**The scan stays `*.cs`. The narrowness is load-bearing.** Both messages moved
into `StandardError.Upload` instead — `FileTooBig(maxSizeMb)`,
`TooManyFiles(maxCount)` and `CropExportFailed()` — and are catalogued in all 14
languages. That closes the backlog: no user-facing error is thrown from markup
any more. What remains in `.razor` is developer invariants (`"<Tab> component
must be nested into <TabPanel>"`, `"This component should never be rendered."`),
admin pages and test pages — none of it catalog material.

Two pieces of evidence settled it against widening:

- Running the test's own matching logic over every `.razor` file against all 97
  `Error_*` values matches **nothing** — 0 keys would gain an anchor, and 0
  would gain a *false* one. So widening buys nothing today, while permanently
  admitting the failure mode it protects against: a key byte-matching display
  text in markup instead of a real throw would stay "anchored" forever after the
  throw was deleted, silently defeating the drift check.
- The one time the rule was obeyed rather than worked around, it produced better
  code. The file-size message had four spellings precisely because it was
  written inline at each markup site; sharing it removed the duplication and a
  hardcoded `10` sitting next to it.

The real defect was that none of this was written down — the test's failure
message said only "must byte-match a message the code still throws", which is
why #3721 dropped the keys instead of relocating them. Now:

- `ServerErrorLocalizationTest`'s header explains why the scan is `*.cs`, and its
  failure message names the fix and points at `StandardError.Upload.FileTooBig`.
- `docs/CODING_STYLE.md` → "Localization (UI Strings)" carries the rule, together
  with the reason error messages keep their **English** literal at the throw site
  rather than calling `L`: `LocalizationUI.Get` matches the English
  `MessageIndex`, so a pre-localized message misses it and buys a redundant AI
  translation.

---

## Suggested order
1. §3's email track — the chrome language is settled (#4125); what is left is
   the 5 templates, using the same `LanguageStringLocalizer`.
2. §4's `Info.plist` strings — settled and independent, doable any time.
3. §4's in-process subset (Android dialogs, local notifications, Live Activity)
   — the native-side accessor now exists (`MauiPreferences.UILanguage`, written on
   every platform), so this is catalog lookups at the call sites and nothing else.
4. §6's video error codes — the branch `wip/l10n-video-error-codes` is
   ready to cherry-pick; then web-auth's popup alert.
5. §5, independently and on its own schedule — the marketing half needs SEO
   routing before translation pays off, and the legal half needs a liability
   decision. Neither holds up anything else.
