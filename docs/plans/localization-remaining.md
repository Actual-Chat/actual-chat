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

## 0. The picker doesn't ship — nothing below matters until it does

`Components/Settings/UserInterface.razor:6` wraps the App-language tile in
`@if (m.EnableIncompleteUI)`, and `LanguageUI.DetectUILanguage`
(`Services/LanguageUI/LanguageUI.cs:131-134`) returns `DefaultUILanguage`
unless the same feature flag is on:

```csharp
if (!await Hub.Features.IsIncompleteUIEnabled(cancellationToken).ConfigureAwait(false))
    return DefaultUILanguage;
```

So in production every user gets English regardless of device locale, and there
is no UI to change it. All 1206 keys are dormant. Enabling
`Features_EnableIncompleteUI` for the language path — or ungating it
specifically — is a product decision, not a code change, but it gates the value
of everything else here.

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

## 2. Account-level UI language — prerequisite for §3

`LanguageUI.UILanguage` is a `SyncedState` over `LocalSettings`
(`LanguageUI.cs:34`) — device-local KVAS. The server never learns it.

Push notifications and emails are composed server-side for a recipient who
isn't holding the device, so **they cannot be localized at all until the
language reaches the account**. Needs: a language field on the account,
sync from the device that changes it, a policy for multiple devices
disagreeing, and a fallback when the account has never set one.

---

## 3. Push notifications and email

Blocked on §2. Text is small and centralised:

- `Notifications.Service/NotificationHelper.cs:42` — `"Voice chat started"` /
  `"{names} started a voice chat"`
- `Notifications.Service/NotificationsBackend.cs:539` — `"Incoming call"` /
  `"Incoming video call"`
- `Users.Templates/*.razor` — 5 templates (`EmailVerification`, `Digest`,
  `DigestChat`, `EmailBody`, `DigestButton`)

Note these duplicate strings the client also has (`"Incoming call"` appears in
`IncomingCallModal.razor:21` too) — worth a shared key rather than two.

---

## 4. Native shells — separate mechanisms, no catalog access

Not blocked on §2: these render on the device, which knows its own locale.

| Surface | Size | Mechanism |
|---|---|---|
| iOS share extension (`App.Maui.IosShareExt/Components/*`) | ~18 strings | `.strings` — pure UIKit, no Blazor DI |
| Android dialogs | 9 strings, 3 files | `strings.xml` per locale |
| `Info.plist` usage descriptions | 6 iOS + 5 MacCatalyst | `InfoPlist.strings` per language |
| Local notifications / Live Activity | ~6 strings | native, composed on-device |

Android dialog sites: `AndroidWebChromeClient.cs:267-270`,
`AndroidNotificationsPermission.cs:44-52`, `WebViewMissingActivity.cs:28-37`.
Local-notification sites: `ChatAttentionService.cs:225-226`
(`"Chat attention required"`, `"Please check chats: …"`),
`NotificationHelper.cs:211` (`"Attention required"`),
`WalkieTalkieWakeHandler.cs:109`, `IosActivitiesBackend.cs:87`
(`"Sharing live location"`), plus the `"Uploads"` channel name.

---

## 5. Landing pages and legal docs — 44 files, zero `L.` usages

`Pages/Landing/**` is completely untouched. It splits in two:

- **Marketing pages** — ordinary translation work, just large.
- **`Pages/Landing/Docs/*`** — Terms, Privacy, Cookies, FAQ. Long-form legal
  text where machine or AI translation carries liability. This is a policy
  decision for whoever owns legal, not an engineering one; the usual answer is
  to publish only human-reviewed translations, or keep one authoritative
  English version and link to it.

---

## 6. Smaller items

- **TypeScript with no localization path**: `web-auth.ts:45` raises a raw
  `alert()`; `video-recorder.ts:120,2893` produces camera-failure text that
  reaches the user as toast text. Neither can read the catalog.
- **`App.Server` pre-language pages**: `ErrorServerPage`, `RootServerPage`
  render before the UI language is resolved. Leave English.

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
1. §0 — decide whether the picker ships; everything else is speculative until then.
2. §2 + §3 together — one story: the server can't localize what it can't address.
3. §4's local-notification subset — cheap, no server change.
4. §5 — needs a scope and liability decision first.
