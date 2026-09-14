# Replay dubbing — design

Date: 2026-09-14. Branch: `feat/voice-dubbing` (PR #4514, issue #4496). Extends phase 1 (live
dubbing, see `docs/live-audio/12-dubbing.md`) with dubbing of historical playback.

## Goal

A listener with "Translated voice" on (and the chat toggle not off) hears replayed voice entries
spoken in their language, with the stock voice, instead of the original. Live behaviour is
unchanged. Voice cloning is out of scope (Soniox caps custom voices at 20 per organization).

## Decisions

- **Provider:** Soniox REST TTS (`POST https://tts-rt.soniox.com/tts`, `audio_format: pcm_s16le`,
  `sample_rate: 48000`, stock voice `TranscriptionSettings.SonioxTtsVoice`). One request per
  entry translation; no WebSocket. Text over 5000 characters is split at sentence boundaries into
  several requests whose audio is concatenated.
- **Timing:** natural pace, timeline stretches. Each dub plays at its own length; the next entry
  never starts before the previous dub has ended. Nothing overlaps; total replay time drifts.
- **Generation:** lazy, on the first replay that needs it; stored forever after (until the
  translation changes). No eager generation on translation completion.
- **Skip rule** (same as live): no dub when the entry's detected languages
  (`IChatEntryLanguagesBackend`) contain the listener's language by ISO code, or when the
  translation `MatchesOriginal(entry.Content)`. Any failure serves the original.

## Data model

`Translation` (`src/dotnet/Api/Chat/Translation.cs`, array-form MessagePack — append only):

| Key | Field | Type | Meaning |
|---|---|---|---|
| 7 | `DubMediaId` | `MediaId?` | media of the synthesized audio; `null` = not generated |
| 8 | `DubContentHash` | `HashString` | hash of `Content` the audio was made from |

`TranslationDiff` gets matching `Option<MediaId?> DubMediaId` and `HashString? DubContentHash`.
`DbTranslation` gets `DubMediaId` (`string?`) and `DubContentHash` (`string`, default `""`), plus a
migration on the chat DB. A dub is valid iff `DubMediaId != null && DubContentHash == Content.Hash`;
`TranslationsBackend` clears both fields whenever it writes a new `Content` (so a re-translation
invalidates the dub without a separate cleanup), and the media is deleted by the same command.

The dub media is a regular `MediaFull` (`ContentType = "audio/webm"`, `BeginsAt/EndsAt` = 0 and
the dub duration — replay derives timing from the entry, not from the media) with a blob under
`BlobScope.AudioRecord` named `<entry stream id>~<lang>.webm`.

## Server

### `IDubsBackend` (Streaming.Service, `Backend/DubsBackend.cs`)

```csharp
Task<Media?> GetOrCreateDub(ChatEntryId entryId, Language language, CancellationToken ct);
```

1. Load the entry; return `null` without audio, or when the entry's languages contain `language`.
2. Get the translation with `ITranslationsBackend.Get(id, translateIfMissing: true)` (bounded by
   `Constants.Audio.ReplayDubTimeout`, 20 s); return `null` if it has no content or
   `MatchesOriginal`.
3. If the translation carries a valid dub, return its media.
4. Otherwise synthesize: `SonioxTtsClient.Generate(text, language, voice, ct)` returns PCM;
   `OpusFramePump` in unpaced mode (new `OpusFramePump.Options { IsPaced = false }`) encodes it to
   `AudioFrame`s; an `AudioSource` over those frames goes through `AudioSegmentSaver.SaveAndCreateMedia(AudioSource, blobId, ct)`
   (new overload); `TranslationsBackend_Change` stamps `DubMediaId` + `DubContentHash`
   (with `ExpectedVersion` — a concurrent re-translation wins and the media is deleted).
5. Concurrent callers for the same `(entryId, language)` share one in-flight task
   (`ConcurrentDictionary<key, Task<Media?>>`, entry removed on completion).

`ISpeechSynthesizer` gains `Task<AudioSource> Synthesize(string text, SpeechSynthesisOptions, ct)`
(one-shot) next to the streaming overload, so `FakeSpeechSynthesizer` covers tests and the backend
does not depend on Soniox directly.

### Replay muxer

- `ILiveAudioStreams.GetReplayStream` gains a 7-arg overload with `Language? dubLanguage` (the old
  one stays for old clients); `LiveAudioStreams` validates it with `IsDubLanguageAllowed` (same
  rules as live) and passes it to `ReplayStreamMuxer`.
- `ReplayStreamMuxer.ProcessEntry`: when `dubLanguage != null`, ask `IDubsBackend.GetOrCreateDub`;
  on a media, download its blob instead of the entry's, and emit `StreamInfo` with
  `StreamId = "<entry stream id>~<lang>"`, `Languages = [lang]` — the client shows the same chip as
  live. On `null` or any error: the original, logged at Information.
- Timing: the muxer tracks `nextEntryNotBefore` (the end of the last dub emitted); every entry's
  `PlaysAt = max(timelinePlaysAt, nextEntryNotBefore)`. For a dub, its end is
  `PlaysAt + dubDuration / Speed`; for an original, `PlaysAt + entryDuration / Speed`.
- Seeking into an entry: `skipTo` is scaled by `dubDuration / entryDuration` for a dub.
- Lookahead: the muxer starts `GetOrCreateDub` for the two entries following the current one
  while the current one streams; results are awaited when their turn comes.
- Speed > 1 frame dropping applies to dub frames unchanged.

## Client

- `ReplayStreamProcessor` gets `DubLanguageProvider` (as `ListeningStreamProcessor`) and calls the
  7-arg overload when it yields a language, the 6-arg one otherwise.
- `ChatReplayPlayer` wires `Hub.TranslationUI.GetDubLanguage(ChatId)` and re-subscribes when it
  changes (same watcher shape as `ChatListeningPlayer.ResubscribeOnDubLanguageChange`; extract a
  shared helper if it is line-for-line the same).
- No new settings or strings.

## Error handling

- Soniox errors, timeouts, missing translation, empty audio → original audio, one Information log
  line per entry. No cool-down: replay is not latency-critical and each entry is independent.
- A dub media whose blob is missing (deleted) → original for that entry, and the translation's dub
  fields are cleared so the next replay regenerates it.

## Tests

- `ReplayStreamMuxerTest` (unit, static helpers): `PlaysAt` stretching across dub/original
  entries; `skipTo` scaling.
- `DubsBackendTest` (Streaming.IntegrationTests, `FakeSpeechSynthesizer`): first call creates media
  and stamps the translation; second call reuses it (synthesizer called once); a changed
  translation regenerates; the listener's own language / `MatchesOriginal` → `null`;
  synthesizer failure → `null` and no media.
- `ReplayDubbingTest` (Chat.IntegrationTests): a replayed chat with one Russian entry and an en-US
  listener yields a stream whose `StreamId` ends with `~en-US` and whose frames are the fake dub.
- Existing live tests unchanged.

## Docs

`docs/live-audio/12-dubbing.md` gets a "Replay" section; PR #4514 body updated.

## Out of scope

Voice cloning; eager generation; dubbing of non-Soniox synthesizers; per-request `speed`.
