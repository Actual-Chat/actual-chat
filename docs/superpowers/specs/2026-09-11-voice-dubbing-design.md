---
title: "Voice dubbing: translated speech in the speaker's voice"
description: Design for live (and later replay) dubbing of voice messages into the listener's language using Soniox TTS v2 with per-speaker cloned voices.
---

# Voice dubbing — design

Branch: `feat/voice-dubbing`. Status: design approved 2026-09-11, not implemented.

## Goal

A listener whose language differs from the speaker's hears the speaker's
messages **spoken in the listener's language**, in a synthetic copy of the
speaker's own voice when the speaker has opted in, otherwise in a stock voice.
Live sessions first; replay of past messages second.

We already have every input: Soniox real-time STT (`SonioxTranscriber`),
real-time LLM translation of the transcript stream
(`TranslationsBackend.StartTranscriptStreamTranslation`), persisted audio for
every voice entry, and a fan-out/replay pipeline for audio streams. Soniox
adds the two missing pieces:

- [Voice cloning](https://soniox.com/voice-cloning) (July 2026): a clean
  single-speaker clip of a few seconds up to 20 s → `voice_id`, usable in all
  60+ languages, REST and WebSocket. Consent is required by their terms.
- [TTS v2 real-time WebSocket](https://soniox.com/docs/tts/rt/real-time-generation):
  one config per stream (`model: tts-rt-v2`, `voice`, `language`,
  `audio_format` incl. `opus`/`pcm_s16le`, `sample_rate` up to 48000), text
  appended incrementally as `{text, text_end, stream_id}`, base64 audio
  chunks streamed back, many streams per connection. ~$0.70 per generated
  hour. **Text cannot be revised once sent.**

## Decisions (brainstorm 2026-09-11)

| # | Question | Decision |
|---|---|---|
| 1 | Live vs replay | Live first, replay second |
| 2 | What the listener hears | The dub **replaces** the original for that listener (no ducked mix) |
| 3 | Consent / enrollment | Opt-in speaker setting; reference clip auto-picked from the speaker's own entries, with an optional "record my sample" fix-up; speakers who did not opt in are dubbed with a **stock voice** |
| 4 | Listener activation | User-level setting "Translated voice" (default off) piggybacking on the existing translation-language choice, plus a per-chat toggle back to the original voice |
| 5 | Lag handling | Natural pace, never cut, no catch-up speed-up (client-side 1.25× catch-up is a later lever) |
| — | Architecture | Server-side dub published as a derived audio stream and substituted in the listening muxer (approach A below) |

## Approaches considered

- **A. Server-side derived audio stream, substituted in the muxer** — chosen.
  One TTS stream per (source stream, language) regardless of listener count;
  the dub is a normal `AudioFrame` stream so `StreamStore`, the muxer,
  `AudioSegmentSaver` and the client decoder need no new format; the saved
  blob is what replay serves later; no Soniox key reaches clients.
- **B. Server-side TTS delivered as a separate "dub track", client switches** —
  no muxer surgery, but two subscriptions per foreign speaker per listener and
  new mixing/track logic on Web and MAUI, the most platform-sensitive code we
  have; replay still needs its own path.
- **C. Client-side TTS via the Soniox Web SDK** — lowest latency, but cost per
  listener, key leasing, per-client stabilization, no persistence, MAUI
  WebView/audio-focus/battery pain. Rejected.

## Components and data flow

```
mic ─► AudioStreamingBackend.ProcessAudio ─► StreamStore<AudioFrame>[S] ─► muxer ─► listeners      (today)
                     │
                     └─► SonioxTranscriber ─► TranscriptDiff[S] ─► TranslationsBackend ─► TranscriptDiff[S~ru]   (today)
                                                                                              │
                                                             NEW  DubbingBackend.OnDubStream(S, ru) ◄── first listener in `ru`
                                                                      │  stable text deltas only
                                                                      ▼
                                                             SonioxTtsClient (tts-rt-v2, voice = clone | stock, opus 48 kHz)
                                                                      │  Opus packets → 20 ms AudioFrame
                                                                      ▼
                                                             StreamStore<AudioFrame>[S~ru] + AudioSegmentSaver → blob
                                                                      │
                                              LiveStreamMuxer(chat, dubLanguage = ru) serves S~ru instead of S for that author
```

### New pieces

All server-side unless stated.

- **`SonioxTtsClient`** (`Transcription.Service/Transcribers/`) — WebSocket
  client mirroring `SonioxClient`'s shape: config frame, `{text, text_end}`
  messages, base64 `audio` frames, `terminated`. Shares key, endpoint, retry
  and cleaner conventions with the STT client. The project is not renamed.
- **`SonioxVoices`** (same project) — REST wrapper: create / get / delete
  voice.
- **`IDubbingBackend` / `DubbingBackend`** (`Streaming.Contracts` /
  `Streaming.Service`), sharded by `StreamId` like
  `TranslationsBackend_TranslateStream`. Command
  `DubbingBackend_DubStream(StreamId Source, Language Target) → StreamId?`,
  idempotent per `(source, target)` via the `_activePublishers` / `FuncWorker`
  pattern of `StartTranscriptStreamTranslation`. The worker: resolves the
  author's voice, subscribes to the translated transcript memoizer, runs the
  stabilizer, pushes text to TTS, publishes returned frames under
  `StreamId.New(source, target)`, registers the stream in `LiveAudioBackend`
  with `DubOf = source` and `Language = target`. `AudioSegmentSaver` persists it
  as an additional media of the same chat entry (`ChatEntry.Dubs[language] →
  MediaId`), which is what replay reads later.
- **Trigger** — `AudioStreamingBackend.GetTranscript(session, streamId,
  language)` already fires `TranslateStream` on the first request in a
  language; `DubStream` hangs off the same place, gated on the listener's
  `TranslatedVoice` setting. Nothing runs for a language nobody listens in.
- **Muxer substitution** — `GetListeningStream` gains `Language? dubLanguage`.
  Rules below.

### Reuse

`SonioxClient` conventions, `StreamStore`, `AsyncMemoizer`, `FuncWorker`,
`AudioSegmentSaver`, `LiveAudioBackend` registry, `StreamId.New(streamId,
language)`, the stable/unstable transcript handling in `TranslationsBackend`,
`UserLanguageSettings` (new flags), `Constants.Transcription.Soniox` for TTS
constants, `SonioxSweeper` for artifact purging, Flows.Service for the
enrollment flow, the existing per-chat KVAS settings pattern for the per-chat
toggle, the existing recorder + `Media` upload for the recorded sample.
Nothing new belongs in Core: the TTS client is provider-specific.

Deliberate scope cuts: no audio codec on the server (Soniox emits `opus`; we
only repacketize), no client-side mixing (substitution is server-side).

## Voice lifecycle

**Settings** (`UserLanguageSettings`, keys appended at the end per the
schema-evolution rules): `VoiceCloningEnabled` (speaker, default false),
`TranslatedVoice` (listener, default false).

**`UserVoice`** entity in `Users.Service`, own table (not KVAS — it must be
sweepable): `UserId`, `SonioxVoiceId`, `Source` (`Auto` | `Recorded`),
`ReferenceMediaId`, `CreatedAt`, `Version`. One per user; recompute replaces
the Soniox voice and deletes the old one.

**Auto enrollment** — enabling `VoiceCloningEnabled` starts a `UserVoiceFlow`
(Flows.Service) that scans the user's recent voice entries newest-first and
picks a span: single entry, 6–20 s of speech (trimmed by transcript
timestamps, no long silences), high transcript confidence, no other author's
stream overlapping in time. It uploads that clip to Soniox voices. If nothing
qualifies, the setting stays on and the flow retries after the user's next
voice entry finalizes. Until a voice exists, the speaker is dubbed with the
stock voice.

**Recorded enrollment** — a settings panel records ~15 s through the existing
recorder into a `Media` upload (not a chat entry), previews the clone with a
one-shot TTS REST call in the user's Secondary language (or English), and on
confirm sets `Source = Recorded`. A recorded sample is never replaced by
auto-pick.

**Disable** — flips the flag, deletes the Soniox voice and the `UserVoice`
row; running dubs finish on the current voice, new ones use stock. Account
deletion does the same via the existing user-removal path.

**Stock voice** — one built-in Soniox voice, `TranscriptionSettings.Soniox.
DefaultTtsVoice`; no attempt to match the speaker. Opted-in speakers whose
voice failed to compute fall back to it with a one-line log.

**Sweeping** — `SonioxSweeper` also lists Soniox voices and deletes any whose
id is not in `UserVoice`.

**Consent record** — the flag's `Version` + `Origin` on `UserLanguageSettings`
plus `UserVoice.CreatedAt`. The toggle copy states that others will hear
translations in a synthetic version of the user's voice and that the sample can
be deleted at any time.

Out of scope: per-chat opt-in/out, sharing a voice across accounts, any
listener-triggered cloning.

## Stabilization, TTS feeding, timing

**What is spoken.** `DubWorker` replays the `S~lang` transcript memoizer,
folds diffs into a running `Transcript`, and tracks `stableText` = text of the
latest `IsStable` transcript (the stable translated prefix only grows). Each
growth sends the new suffix as one `{text}` chunk; the unstable tail is never
sent. Source completion → `text_end: true` → drain audio → complete the dub
stream. If the first stable chunk is `NoTranslationNeeded` (speaker already in
the listener's language) the worker exits without publishing and records a
`NoDub` marker.

**Lag budget** (speaker pause → dub audio): STT finalization 0.3–1 s +
translator call 0.5–1.5 s + `TranslateThrottleDelay` 0.5 s + TTS first byte
~0.2 s ≈ **1.5–3 s**. The throttle is the first lever if it is too slow.

**Audio frames.** Soniox `opus` output is repacketized into 20 ms `AudioFrame`s
(`Constants.Audio.OpusFrameDuration`), 48 kHz mono — the same shape
`ProcessAudio` publishes. Gaps while waiting for text are filled with a
precomputed Opus silence packet so `Offset`s stay contiguous and the saved
`.webm` is a real timeline. If Soniox's `opus` is Ogg-only, we parse the Ogg
pages; no encoder is added. This is spike #1.

**Timeline.** Dub `BeginsAt` = its first frame; `LiveAudioStreamInfo` carries
`DubOf` and `Language`. Live-edge trim applies to dubs unchanged.

**Muxer rules.**
1. *Selection* — per author, if `dubLanguage` is set and `S~dubLanguage` is
   registered, serve it; else serve `S`.
2. *Expected-language gate* — `AudioSegmentLanguage` knows the speaker's
   candidate languages before transcription. If none is the listener's
   language, the muxer holds `S` for up to `DubWaitTimeout` ≈ 5 s waiting for
   `S~lang` or `NoDub`, then serves `S` (fail-open). If the candidates include
   the listener's language, `S` is served at once and the muxer switches to
   the dub only if one registers: `MuxedAudioStreamEnd(S)` +
   `MuxedAudioStreamStart(S~lang)`.
3. *Merge key* becomes `(AuthorId, DubOf is null)`: originals evict originals;
   dubs never evict originals or vice versa.
4. *Same-author sequencing* — a dub can outlive its source by lag + speech
   length. `DubbingBackend` gates `S2~lang`'s first frame on `S1~lang`
   completion per `(author, lang)`, so one voice never overlaps itself.
5. *Eviction delay* (4 s) unchanged.

**Failure handling.** TTS socket error → log, complete the dub stream with
error, `NoDub` marker so the muxer falls back to the original for the rest of
that utterance; no mid-utterance retry. Voice id rejected by Soniox → mark
`UserVoice` stale (flow recomputes), continue with the stock voice. Translated
transcript ends with error → dub ends with error.

## API surface and client

**Wire (additive):**
- `ILiveAudioStreams.GetListeningStream(session, chatId, catchUpFrom,
  Language? dubLanguage, ct)` — new overload via `[LegacyName]`.
- `LiveAudioStreamInfo` gains `DubOf` (key 8) and `Language` (key 9).
  `AuthorId` and `EntryId` on a dub are the source's.
- `UserLanguageSettings`: `TranslatedVoice`, `VoiceCloningEnabled`.
- `IUserVoices` (Users.Contracts): `Get(session)`, `OnEnroll(session, MediaId
  sample)`, `OnRemove(session)`; `IUserVoicesBackend` twin for the server.

**Client (UI.Blazor.App, shared by Web and MAUI):**
- `ListeningStreamProcessor` passes `dubLanguage =
  TranslationUI.GetTranslationLanguage(chatId)` when `TranslatedVoice` is on
  and the per-chat override is not "original voice"; changing either
  re-subscribes (same path as sleep/resume).
- `ChatListeningPlayer`: no mixing change; a "translated" marker on the
  speaking indicator when `StreamInfo.DubOf != null`.
- "Translating…" cue while the muxer holds the original: derived client-side
  from *author is live-recording ∧ no audible track for that author ∧ dub mode
  on*; no new wire message.
- Per-chat toggle: a chip in the live block header, visible only when dub mode
  is on and at least one speaker's languages exclude the listener's; stored as
  a per-chat local setting the processor observes.
- Settings (`TranscriptionSettings.razor`): the two switches with the consent
  copy, plus a "Voice sample" panel (record, preview, delete).
- Transcript captions: untouched.

## Phasing

Each phase is shippable and flag-gated.

0. Spikes: Soniox `opus` framing → `AudioFrame` repacketizer; TTS WebSocket
   round-trip against the live API (`SonioxTtsClientTest`, self-skipping
   without the key like `SonioxTranscriberTest`).
1. Server dub with the **stock voice** + listener setting + muxer substitution
   + client marker. The full live experience minus the clone.
2. Auto-clone: `UserVoice`, speaker setting, `UserVoiceFlow`, sweeper.
3. Recorded sample UI + preview.
4. Replay: serve `ChatEntry.Dubs[lang]` from blob in `ReplayStreamMuxer`,
   generate on demand for entries recorded before the feature.

## Testing

- Unit: stabilizer (stable-suffix extraction from a scripted diff sequence,
  retractions, `NoTranslationNeeded`); repacketizer (frame count, offsets,
  silence fill); muxer rules (selection, hold + timeout, merge key,
  same-author sequencing) on the existing muxer test harness.
- Integration: one test per Soniox call (TTS stream, voice create/delete),
  behind the key.
- Manual: two-device pass on dev (Russian speaker → English listener) before
  phase 1 merges.
