---
title: Plans
description: Project task tracker — active plans, standing plans by area, and the backlog.
---

# Plans

This is the project's task tracker. **Active** (recently added, larger) efforts
come first, then **standing plans** grouped by area, then the **backlog** of
candidate tasks. A plan is removed from here once its work ships.

## Active

Recently added, larger efforts — in progress or next up.

### H.264 codec selection fixes

[H.264 codec selection fixes](./h264-codec-selection-fixes.md) — a Firefox/Linux
user report exposed six defects in H.264 profile selection: detection probes
Main 3.1 and declares High 4.0, `excludeEncoderCodec('h264')` is a hard no-op
so a failed encoder re-picks itself forever, and the receiver builds a single
decoder candidate with no software-decode retry. Two of them reproduce the same
frozen-tile symptom on browsers that behave correctly.

### Android ANRs — FCM cold-start stalls

[Android ANRs](./android-anr-issues.md) — two thirds of Voxt's Android ANRs are
an FCM push waking a dead process, with `Application.onCreate` blocked on a
runtime mutex past the 10s foreground-broadcast timeout. Priority triage on the
server, less work on push-started processes, and a lazy idle-primed `MauiApp`.

### Notification lifecycle

[Notification lifecycle](./notification-lifecycle.md) — give every notification a
terminal state (per-kind `ExpiresAt` + `DismissMode`), make dismissal as reliable
as delivery via a persisted `PendingDismissals` set drained by a flow, and clear
reactions when their entry is actually on screen. Traced from a production
`CallNotification` stranded for 5½ days with the app-icon badge stuck at 1.

### AI search & indexing — MLSearch → PostgreSQL

[MLSearch: OpenSearch → PostgreSQL](./mlsearch-postgres-fts.md) — replace
OpenSearch with PostgreSQL full-text search (`tsvector` + GIN) behind
`ISearch` / `ISearchBackend`. Text analysis (tokenization, stemming, CJK
segmentation) moves to the application level for uniform coverage across the
top ~50 languages, on a pgvector-ready schema.

### Better translation

[Better translation](./better-translation.md) — throttle both translation
streams. The whole-message stream publishes one wire item per LLM chunk with no
rate limit; the realtime stream is throttled at 500ms, tuned for latency rather
than for the up-to-4-calls/sec of LLM spend it implies.

### Distributed services

[Distributed services](./distributed-services.md) — migrate the ~30 `*Backend`
services off Fusion's Operations-Framework cluster-wide invalidation (the
`Invalidation.IsActive` blocks) to the single-writer-per-shard model already
used by presence, Flows, and the live audio/video pipelines.

### Database sharding

[Database sharding](./db-sharding.md) — shard `ac_chat`, `ac_users`, and the
other backend DBs by their natural key (`ChatId`, `UserId`, `OwnerId`, …) using
Fusion's app-level sharding subsystem, which ActualChat runs in single-shard
mode today.

### Multi-provider transcription

[Multi-provider transcription](./transcription-providers.md) — make
transcription providers pluggable: a registry with per-language preference
ranking, a chat-context prefix (recent entries + conversation summary) fed to
the provider, and health-based auto-ejection built on a new general
`IExternalServiceHealth` API in `Core.Server`. Adds Soniox (streaming +
offline) and Gemini 3 (offline), upgrades Google to Chirp 3 and OpenAI to
`gpt-transcribe` / `gpt-live-transcribe`.

### Localization

[Localization: what's left](./localization-remaining.md) — the app UI, server
errors, validation and dates shipped in #3721, and the App-language picker is
now live, with the language on the account (`UserLanguageSettings.UILanguage`,
`null` = follow the device). What is still English is everything that renders
outside Blazor: push notification text composed on the server (which wants an
iOS Notification Service Extension), the digest emails, and the native shells
(iOS share extension, Android dialogs, `Info.plist`, local notifications). The
44 untouched landing/legal pages are a separate track: the marketing half needs
per-language routes before translation pays off, and the legal half is a
liability decision.

[Max-locale layout findings](./max-locale-findings.md) — 21 layout defects in
12 root causes, found by walking the whole UI under `?ui-language=max` at
390x844, 820x720, 1280x800 and 1440x900, each verified against English on the
same screen. Four blockers: right-panel tabs that cannot be reached, composed
sentences that drop the name out of the middle, radio options that truncate
into duplicates, and the left-panel title painted under the search box. **All
fixed** on `feat/fix-ui-size-issues`, then re-checked by a parallel read-only
sweep whose findings are recorded in the same document — including one fix that
silently disabled another. The route map it came from is the
[UI walk-through](../ui/walk-through.md); the manual pass is the
[fix checklist](./max-locale-fix-checklist.md).

[Localization size budgets](./l10n-size-budgets.md) — the follow-up the layout
work argues for: declare how much room each string has (in em, so it survives a
font-size change), measure every translation against it with the glyph advances
`derive-max.py` already computes, and shorten the ones that do not fit. A
phrase needing two lines in a one-line box is usually a phrase that could have
been shorter, and shortening the widest translation narrows Max itself.

### On-premises instances

[On-premises instances](./on-prem-instances.md) — let customers run their own
Voxt server, DBs, Redis, NATS and (optionally) their own transcription/LLM
providers, while our official apps talk to their instance alongside the cloud
account.

## Standing plans

### Audio and video

- [Audio pipeline redesign](./audio-pipeline-redesign.md) — replace the four
  per-platform audio-lifetime implementations with one platform-independent
  session model: refcounted leases as the single source of truth for "audio
  focus", and lazy per-resource release so short activities stop re-allocating
  expensive native resources. Includes the full iOS `audiomxd` investigation
  (idle 0.267 cores → 0).
- [NAudio replacement](./naudio-replacement.md) — Windows audio capture fails
  under NativeAOT (`InvalidProgramException` in NAudio's WASAPI COM path);
  replace it with an AOT-safe capture path.

### Chat and UI

- [Streaming markup messages](./streaming-markup-messages.md) — let regular
  text messages stream their content in (so an LLM reply appears
  progressively), which needs a partial mode in `MarkupParser` so unterminated
  markup renders styled instead of literal.
- [Speech render performance](./speech-render-perf.md) — next-tier CPU/GPU
  reductions during recording (R1–R3 shipped, R4–R7 open; desktop holds 60 fps).

### Accounts and security

- [E2E encryption](./e2ee.md) — per-chat AES-256-GCM group key, ECDH P-256
  identity keys, client-side encryption; text-only when enabled.
- [User and account merge](./user-account-merge.md) — unify `User` and
  `Account` into one type; incremental (the `AccountFull` step landed; DB
  schema and backend renames remain).

### Platform: macOS / Mac Catalyst

- [Full macOS support](./macos-support.md) — umbrella doc for gaps where Mac
  Catalyst silently inherits an iOS implementation that doesn't fit macOS.
  First entry: downloaded files go to Photos or a share sheet instead of
  `~/Downloads`.
- [Voice processing](./maccatalyst-voice-processing.md) — restore hardware
  AEC / NS / AGC on Mac Catalyst; currently disabled (the `AVAudioEngine` VP
  downlink has no reference graph), so Mac records without echo cancellation.
- [Notification permission](./macos-notification-permission.md) — wire the
  `NotificationsPermissionBanner` "Configure" button on Mac Catalyst; today
  `MacNotificationsPermission` is an unwired stub.

### Build, testing, CI

- [TypeScript `moduleResolution: bundler`](./ts-module-resolution-bundler.md) —
  switch from the legacy `node` algorithm so TS honors `package.json` `exports`
  subpaths.

## Backlog

Candidate tasks without a dedicated plan yet, carried over from the former Big
and Small task lists. Grouped by theme.

### Chat & messaging

- Rename any of your contacts — the custom name is used everywhere (image & bio
  still come from the avatar); auto-rename to the phone-contact name when
  contact import finds a match.
- New chat modes / settings:
  - Only owners can post (+ allow/disallow others' reactions; later:
    allow/disallow others to comment in threads).
  - Max voice-fragment duration: `0` (voice off) / 10s / 30s / 1m / 3m / 5m /
    no limit.
  - Post cooldown: same options + 10m / 30m / 1h.
  - Public chats: require join to view more than the last N messages.
- Emoji `:` picker syntax — `:` opens an inline emoji picker (consider
  [emoji-mart](https://github.com/missive/emoji-mart)); AI emojis (`:my-…`
  generation; admin-published custom emojis available to everyone).
- Tenor / GIF picker — extend the picker with a `.gif` search.
- Show bios in the Members list.
- Chat & place background image (shown at the top of the Chat Settings tab).
- "New message [in another chat]" notification banner.
- Max message length = 64K characters.

### Security & privacy

- Auto-wipe messages:
  - Group chats: chat-level option managed by the owner.
  - Private chats: per-user option applied to that user's messages; also "wipe
    all of my messages from here once they're read".
  - Needs a way to display wipe timers (or a fade-out).
- "Disable file-system cache" option in Settings / Application (store almost
  nothing on-device).
- See also [E2E encryption](./e2ee.md).

### Extensibility / API

- Generate your own API keys; accept API keys instead of a `Session`.
- Web hook for posts.
- Google / crawler support for public chats & places — Open Graph tags for
  `/chat/xxx` and `/u/xxx`; pre-render recent content (~1K messages).
- Custom chat & account IDs — `voxt.ai/u/xxx` vanity URLs as no-redirect aliases.
- Offline action queue — enqueue + list queued actions per scope (chat);
  implement for Post (with uploads) and for recorded audio.

### Recording, playback, transcription

- 1.25× / 1.5× / 1.66× / 2× speed-up for historical playback.
- Dynamic split / pause detection — measure inter-word pauses (discarding long
  inter-phrase ones), track the average, split when a pause exceeds it by 2–3×.

### Mobile

- Android: get rid of share-intent state persistence.
- MAUI: portrait/landscape switch should work (mainly for images & videos) —
  verify whether still broken.

### Accounts & settings

- Replace "Full name" with "Real name" (or hide it — it's confusing next to
  avatars).
- Default-avatar affordance: replace the "Star" with "Default"; use "Make
  default" on other avatars.
- Get rid of `IAuth` — extract Session management (shard it or use Redis); fold
  `User` / `IAuth` into `IAccounts` (relates to
  [User and account merge](./user-account-merge.md)).

### Infrastructure / codebase

- Use "Auto" render mode (.NET 8) for Blazor components.
- `SettingsPanel` / `SettingsTab` should inherit/use `TabPanel` / `Tab`.
- Remove the Kubernetes project?
