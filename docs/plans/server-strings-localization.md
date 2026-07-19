# Server-Originated Strings Localization Plan

**Date:** 2026-07-16
**Branch:** `feat/3721-app-localization` (follow-up to `docs/plans/app-localization.md`)
**Status:** Draft — investigation + proposal

## 0. TL;DR

The JSON `AppStringLocalizer` covers strings the *client* renders from literals it owns.
This plan covers everything else — text that is **composed on the server** and reaches the
user as data: exception/error messages, push notifications, emails, and system chat entries.

Recommended approach, per category:

| Category | Mechanism | AI at runtime? |
|---|---|---|
| A. Error messages from RPC commands | Client-side `ServerMessageLocalizer`: exact-match table for the ~264 known constant messages, **AI translate-and-cache fallback** for parameterized/unknown text | Fallback only |
| B. Push notifications | Server-side: fixed phrases from a shared string table, per-**device** UI language | No |
| C. Emails (verification, digest) | Server-side: shared string table for subjects/templates, per-**account** UI language; digest body already AI-localized | Digest only (already done) |
| D. System chat entries ("X joined") | Client render-time localization from structured data — plain JSON keys | No |
| E. Chat message content | Already solved (`ITranslations` pipeline) — out of scope | — |

The "UI service which translates and caches" the team envisioned materializes as two pieces:
a client `ServerMessageLocalizer` (sync table hit + async fallback swap) and a server
`TranslateText` endpoint that reuses the existing `Translator` + a `DbTranslation`-style
hash-keyed cache. Details in §4.

## 1. Inventory — what a JSON resource file cannot localize

### A. Exception / error messages crossing RPC (the big one)

- `StandardError.*` factories (`src/dotnet/Core/Errors/StandardError*.cs`) are called with
  **~264 literal English messages** across services, e.g.
  `Chat.Service/Authors.cs:273` — *"You can't leave this chat because you are its only
  owner. Please add another chat owner first."*
- Some are **parameterized at runtime**, e.g. `Chat.Service/ChatsBackend.cs:2445` —
  `$"You can send up to {limit} messages until this user adds you to their contacts or replies."`
- ActualLab RPC re-materializes exceptions client-side from **type + message text only** —
  there is no error-code channel, and the client cannot key a JSON lookup off anything
  but the English string itself.
- Display path: `UIActionFailureTracker` → `ToastHost.razor` (`UI.Blazor/Components/Toast/
  ToastHost.razor:90`) shows `failure.Error?.Message ?? "Unknown error."` **verbatim**.
  `UICommander.ShowError` (`UI.Blazor/UICommanderExt.cs`) feeds the same pipeline.
- Same problem class: the 5 `ErrorMessage =` validation attributes, and server-thrown
  messages embedded in command handlers (`Chats.cs:418` *"Sorry, you can't post empty
  messages."* etc.).

Why JSON alone fails: the client receives an arbitrary English sentence, not a key. Any
solution needs either (a) an error-code refactor of all throw sites, or (b) translation
of the received text. §4.1 recommends a hybrid.

### B. Push notifications

Server composes final display text; when the app process is dead, the OS (APNs/FCM
`notification` payload, `Notifications.Service/FirebaseMessagingClient.cs:109-126`)
renders it — **no client code runs**, so client-side localization is impossible for the
most important delivery case.

English literals baked in `Notifications.Service`:
- `NotificationsBackend.cs:491` — `"Incoming video call"` / `"Incoming call"`
- `NotificationsBackend.cs:597` — `$"{reaction.Emoji} to {text}"` (reaction)
- `NotificationsBackend.cs:627` — `$"Thread '{chat.Title}' has been created"`
- `NotificationsBackend.cs:1034` — `$"{author.Avatar.Name} asks for attention"`
- `NotificationHelper.cs:42` — `"+1 more message"` / `$"+{moreCount} more messages"`
  (plural forms!) and the `·` aggregation composition.
- `MentionReminderFlow.cs` — reminder texts.

Missing prerequisite: the server doesn't know the target's UI language, and since the UI
language is deliberately **device-local** (like theme), the right granularity for push is
**per device** — `DbDevice` (`Notifications.Service/Db/DbDevice.cs`) has no language column.

### C. Emails

- Verification/sign-in: `Users.Service/Email/EmailAuth.cs:84-88` — English subjects
  (`$"{CoreConstants.AppName}: sign-in code"`), body from the Blazor-rendered
  `EmailVerification` MJML template (English).
- Digest: `Users.Service/EmailsBackend.cs:91` — subject `$"{CoreConstants.AppName}: digest"`,
  template chrome in English. Notably the **digest body summaries are already
  AI-localized**: `EmailsBackend.GetUserLanguage` (line 264) reads
  `UserLanguageSettings.Primary` and passes it to `IChatDigestSummarizer`
  (`Chat.ML/ChatDigestSummarizer.cs`), which prompts the LLM in the target language.
  That's the in-house precedent for "AI localizes server-composed content."
- Caveat: emails are per-**account**, but `UserLanguageSettings.Primary` is the *spoken*
  language, not the UI language. Using it is a reasonable default; §4.3 adds a synced
  UI-language setting the server can prefer when present.

### D. System chat entries — localizable *without* AI, but not via `L["key"]` as-is

`MembersChangedEntry.ToMarkup()` (`Api/Chat/MembersChangedEntry.cs:18-28`) builds
*"{name} has joined/left the chat."* from **structured fields** (`TargetAuthorName`,
`HasLeft`). The entry crosses the wire as data; text is produced wherever `ToMarkup()`
runs. So the fix is deterministic: localize at the **UI render site** with ordinary JSON
keys — but `ToMarkup()` lives in the shared `Api` project (no localizer there) and is also
consumed server-side (notification text extraction via `NotificationHelper.GetText`), so
English must remain the server-side default. Same applies to `NotifyMembersEntry`.
`LegacySystemEntry` is wire-frozen and unaffected.

### E. Already solved / out of scope

- **Chat message content** — the existing translation pipeline (`ITranslations`,
  `TranslationsBackend`, `Translator`, `DbTranslation`) handles user content; untouched.
- **MAUI-native strings** (`Info.plist` permission prompts, `App.Maui.IosShareExt`
  hardcoded English views) — platform resource mechanisms (`.lproj`, `strings.xml`),
  a separate effort; listed for completeness.
- **Date/time relative text, `.Pluralize()`** — client-side; belongs to the main
  JSON-localization rollout, not this plan.

## 2. Design principle

**Deterministic first, AI second.** Every string that is (or can be made) *enumerable at
build time* gets a reviewed, committed translation in a JSON table — same quality bar as
the existing `Strings.<lang>.json`. Runtime AI translation is the fallback for text that
cannot be enumerated (parameterized error messages, strings added after the table was
generated, third-party exception text). AI output is cached server-side so each distinct
(text, language) pair is translated **once globally**, and cached client-side so repeat
displays are instant.

## 3. Reuse (mandatory section)

### 3.1 Existing abstractions to reuse

- **`Translator`** (`Chat.Service/Translation/Translator.cs`) — keyed SemanticKernel
  chat-completion wrapper (OpenAI/Gemini) with prompt-file templating
  (`CoreServerSettings.PromptsDir`), already registered in `ChatServiceModule`. Register
  one more keyed instance with a UI-string prompt (preserve placeholders/numbers/names,
  terse register) instead of writing a new LLM client.
- **`RateLimitedChatCompletionService`** (`Chat.ML/ChatCompletionService/`) — existing
  rate limiting for completion calls; the new translator instance rides on it.
- **`ITranslations` / `ITranslationsBackend` + `DbTranslation`**
  (`Chat.Service/Translations.cs`, `TranslationsBackend.cs`, `Db/DbTranslation.cs`) — the
  in-house translate-and-cache pattern, including `SourceContentHash` for staleness. The
  new text-translation endpoint extends this stack (new method + new table) rather than
  creating a parallel service family.
- **`IChatDigestSummarizer`** (`Chat.ML/ChatDigestSummarizer.cs`) — precedent for
  language-parameterized prompts; digest email body needs no new work.
- **`AppStringLocalizer` + `Strings.<lang>.json` + `AppStrings` extension members +
  `AppLocalizationTest`** (`UI.Blazor.App/Resources/`,
  `tests/Chat.UI.Blazor.UnitTests/AppLocalizationTest.cs`) — the JSON-table format,
  loading code, typed-key convention, and key-coverage test to replicate for the new
  tables (`ServerStrings.<lang>.json`).
- **`LanguageUI.UILanguage`** (`UI.Blazor.App/Services/LanguageUI/LanguageUI.cs`) —
  the device-local UI language `SyncedState`; source of the language for all client-side
  lookups and the value pushed to the server in §4.3.
- **`IServerKvasBackend` / `StoredSettings`** (`UserLanguageSettings` pattern,
  `Api/Users/UserLanguageSettings.cs`) — the storage shape for the new synced
  `UserUILanguageSettings`.
- **`LruCache<TKey, TValue>`** (`docs/api-index.md` — ActualChat.Core) — client
  in-memory cache for AI-translated messages.
- **Fusion `[ComputeMethod]`** — the front-facing `TranslateText` read is a compute
  method, so repeated client lookups of the same (text, language) are served from the
  computed graph without re-querying.
- **`UIActionFailureTracker` / `ToastHost` / `ErrorToast`** — the single choke point for
  error display; localization hooks in there, no per-call-site changes.
- **`Notifications_RegisterDevice` / `DbDevice`** — existing device-registration flow to
  carry the per-device language.
- **`BlazorRenderer` + MJML templates** (`Users.Service/Email/EmailAuth.cs`) — email
  templates stay; they take localized strings as parameters.

No existing abstraction was found for: client-side "match message against known string
table" (new, small), and server-side string tables usable outside `UI.Blazor.App` (the
loader exists but is private to `AppStringLocalizer`) — hence the promotions below.

### 3.2 New components — local vs shared placement

| New component | Local option | Shared option | Recommendation |
|---|---|---|---|
| `StringTable` (embedded-JSON dictionary loader, extracted from `AppStringLocalizer.LoadAll`) | keep private in `UI.Blazor.App` | **`ActualChat.Core`** (no server/UI deps; both `AppStringLocalizer` and `Notifications.Service`/`Users.Service` consume it) | **Shared — `ActualChat.Core`.** Two consumers exist on day one. |
| `ServerStrings.<lang>.json` (known server messages + notification/email phrases) | per-project copies | **embedded in `ActualChat.Core`** next to the loader | **Shared — `ActualChat.Core`**; single source, one coverage test. |
| `ServerMessageLocalizer` (client: table match + AI fallback + swap) | **`UI.Blazor.App/Services`** | `UI.Blazor` | **Local — `UI.Blazor.App`**, because it depends on `LanguageUI` and `ITranslations`; promote only if a second UI project appears (same call as `AppStringLocalizer`). |
| `TranslateText` endpoint + `DbTextTranslation` | **extend `Chat.Service` translation stack** | new microservice/module | **Local to `Chat.Service`** — `Translator`, `Kernel`, prompts, rate limiting, and the translation DB already live there; a new module would duplicate all of that. |
| `UserUILanguageSettings` | — | **`Api/Users`** (next to `UserLanguageSettings`) | Shared by definition (API record). |

## 4. Target architecture

### 4.1 Error messages (category A)

```
server throw StandardError.Constraint("You can't …")
        │ RPC (message text only)
        ▼
UIActionFailureTracker ──► ToastHost / ErrorToast
        │ message
        ▼
ServerMessageLocalizer.Localize(message)          [UI.Blazor.App]
  1. UILanguage == en → return as-is
  2. exact match in ServerStrings table → localized instantly (sync)
  3. in-memory LruCache hit (AI-translated earlier) → instant (sync)
  4. else → return English now + fire ITranslations.TranslateText(session, text, lang);
     swap the toast text when the result lands (toast lives 5s; cache makes repeats instant)
        ▼
ITranslations.TranslateText ──► ITranslationsBackend ──► DbTextTranslation cache
                                        │ miss
                                        ▼
                          Translator (keyed "ui-strings" instance, new prompt file)
```

- **The exact-match table** covers the majority of the 264 messages — they are constant
  literals. Generation: a script (`pwsh`, ripgrep-based, like the existing key-coverage
  tooling) scans `StandardError.*("…")` call sites into `ServerStrings.en.json`; the
  14 translations are produced the same way `Strings.<lang>.json` were, reviewed, and
  committed. A unit test (mirroring `AppLocalizationTest`) fails when a new literal
  appears in code but not in the table, keeping drift visible.
- **Parameterized messages** (interpolated `$"…"`) can't exact-match. They fall through
  to the AI path. Cardinality is low (a handful of sites, few distinct arg values), so
  the server cache absorbs them. *Optional later hardening:* template-match (derive a
  regex per format string) to translate the template once and re-substitute args — not
  worth it for the MVP.
- **`DbTextTranslation`**: `Id = hash(text) + language` (reuse the `Hashing` helpers used
  by `DbTranslation.SourceContentHash`), `Content`, timestamps, plus a `PromptVersion`
  discriminator so a prompt change can invalidate. Lives in `ChatDbContext` + migration.
- **Guardrails** on `TranslateText`: max input length (~500 chars — error messages are
  short), target language must be in `SupportedUILanguages`, per-user rate limit, and the
  existing `RateLimitedChatCompletionService` behind it. This endpoint translates
  arbitrary caller-supplied text by design (the client can't prove a string came from the
  server), so treat it like any other LLM-backed endpoint: cheap model, hard caps.
- **Prompt**: new `prompts/translate-ui-string.txt` — translate a short UI/error message,
  preserve numbers, quoted names, emoji, `{0}`-style placeholders, and punctuation; output
  text only. Temperature ≈ 0.1 (same as chat translation).

### 4.2 Client integration points

- `ToastHost.OnFailuresChanged` groups by message — group by the **localized** message.
- `ErrorToast` title `"Error"` / `"Action failed!"` (`ToastHost.razor:14`) and the
  `"Unknown error."` fallback move to ordinary `Strings.<lang>.json` keys (they are
  client literals; they only ride along here because they live in the same components).
- The swap in step 4 is a state update on the toast component — Blazor re-renders; no
  new plumbing.
- Other surfaces that print `Exception.Message` (banners, modals such as
  `CopyChatToPlaceUI`, share flows) call the same `ServerMessageLocalizer` — one service,
  many call sites, added incrementally.

### 4.3 UI language propagation to the server

Two additions, both write-through from the existing `LanguageUI.UILanguage` state:

1. **Per account** — `UserUILanguageSettings` (`Api/Users`, `StoredSettings` +
   `IHasKvasKey`, mirroring `UserLanguageSettings`). `LanguageUI` writes it (fire-and-
   forget) whenever `UILanguage` changes and on startup when signed in. Semantics:
   *"UI language of the most recently active device"* — good enough for emails.
   Fallback chain server-side: `UserUILanguageSettings` → `UserLanguageSettings.Primary`
   → `en`.
2. **Per device** — add `Language` to the device-registration command
   (`Notifications_RegisterDevice`) and a nullable column on `DbDevice` (+ migration).
   Old clients simply leave it null → account fallback chain.

### 4.4 Push notifications (category B)

- `NotificationsBackend` fan-out already iterates target users/devices; resolve the
  language per device (from `DbDevice.Language`, falling back per §4.3) and compose
  title/text via the shared `StringTable` (`ServerStrings.<lang>.json` from
  `ActualChat.Core`): `Notification_IncomingCall`, `Notification_AsksForAttention_Format`,
  `Notification_ThreadCreated_Format`, `Notification_ReactionTo_Format`,
  `Notification_MoreMessages_Format`, mention-reminder texts.
- **Plurals** (`"+{0} more messages"`): sidestep CLDR by choosing phrasing that doesn't
  inflect (e.g. ru: `"ещё сообщений: {0}"`). A real plural-rules service stays deferred,
  as in the main localization plan.
- Message *content* previews remain user content — untranslated (consistent with the
  in-app experience; live entry translation for previews would be a separate product
  decision with real cost).
- Note: `Notification.Title`/`Text` are **persisted**; language changes after the fact
  don't retro-localize old notifications. Acceptable.

### 4.5 Emails (category C)

- Subjects (`EmailAuth.cs:84-88`, `EmailsBackend.cs:91`) and template chrome
  (`EmailVerification`, digest MJML) → keys in the shared `ServerStrings.<lang>.json`,
  language resolved per §4.3 account chain. Templates receive strings as parameters —
  no template-engine changes.
- Digest summaries: already localized via `ChatDigestSummarizer`; switch its language
  argument from `UserLanguageSettings.Primary` to the §4.3 chain so digest chrome and
  body agree.

### 4.6 System chat entries (category D)

- Keep `SystemEntry.ToMarkup()` English (server consumers: search text, notification
  extraction).
- At the UI markup-render layer, special-case `MembersChangedEntry` /
  `NotifyMembersEntry`: build the display markup from the entry's **structured fields**
  using `L` keys (`SystemEntry_Joined_Format`, `SystemEntry_Left_Format`, …) instead of
  calling `ToMarkup()`. Pure JSON localization; no AI, no server change, old entries
  localize retroactively since text is derived at render time.

## 5. Phasing

1. **Phase 1 — client error localization, AI path.** `TranslateText` endpoint +
   `DbTextTranslation` + keyed `Translator` + prompt file; `ServerMessageLocalizer`
   (steps 1, 3, 4 — no table yet); `ToastHost`/`ErrorToast` integration. This alone
   makes every server error localized (eventually-instant), and is the piece the team
   asked for.
2. **Phase 2 — deterministic table.** Extraction script → `ServerStrings.en.json`;
   translate + review; `StringTable` promotion to `ActualChat.Core`; coverage test;
   `ServerMessageLocalizer` step 2. AI path becomes fallback-only.
3. **Phase 3 — language propagation.** `UserUILanguageSettings` + device-registration
   language + migrations.
4. **Phase 4 — push + email.** §4.4 and §4.5, consuming Phase 2's table and Phase 3's
   language resolution.
5. **Phase 5 — system entries.** §4.6 (independent; can run any time after the base
   JSON localizer covers chat view).

Phases 1–2 are intentionally swappable: if reviewed-quality-first matters more than
coverage-first, do the table before the runtime endpoint.

## 6. Alternatives considered

- **Full error-code refactor** (every `StandardError` site throws a typed code + args;
  client maps code → JSON key). Cleanest long-term, but a 264-site breaking sweep across
  all services, needs an RPC-safe carrier for code+args (current exception transport
  keeps message text only), and still doesn't cover third-party/unexpected exception
  text. Rejected as a prerequisite; the string-table + AI design doesn't preclude
  migrating hot paths to codes later.
- **Server-side error localization in RPC middleware** (translate the message before it
  leaves the server, using the session's language). One choke point and no client work,
  but it puts an LLM call (or cache lookup) on the *failure path* of every command,
  breaks message-based grouping/logging consistency, and bakes the language into logged
  errors. Rejected.
- **Generic errors UX** ("Something went wrong" localized + details collapsed). Solves
  localization by removing information; product regression for the many actionable
  constraint messages ("add another owner first"). Rejected as the primary approach,
  but reasonable as the display for *unmatched, untranslatable* text if AI fallback is
  ever deemed too risky.
- **Client-direct LLM calls** (no server endpoint). No shared cache, per-client cost,
  and API keys on clients. Rejected.

## 7. Open questions

1. **Unreviewed AI output in the UI** — Phase 1 shows machine translations of error
   messages without human review (same trust level as chat translation today). OK, or
   should unmatched strings stay English until Phase 2's table covers them?
2. **Model choice** for the UI-string translator — the realtime translation model, or a
   cheaper one (strings are short, latency barely matters since English shows first)?
3. Should notification **message previews** ever be translated (cost/product call)?
