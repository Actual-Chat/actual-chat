# Speaking bots — design

**Status:** approved design, not yet planned. Working document — delete once shipped.

## Goal

An integration should be able to participate in a chat the way a person does: text that
arrives progressively, and **real speech**. A bot that speaks must finalize into the same
artifact a human recording does — a playable `ChatEntryAudio` with a `LinearMap` — so live
listening, replay, scrubbing and search work without knowing a bot produced it.

Audio a bot brings is the real thing. Server-side synthesis is a **fallback**, off by
default, for listeners who would rather hear a bot than read it.

## What already exists

The text half shipped on `feat/text-entry-streaming`: `IChats.StreamEntry` for stream-capable
transports, and `start/append/finish_message_stream` over MCP for those that aren't. A streamed
text entry already carries a `ContentStreamId` publishing `TranscriptDiff`s.

The speech half exists for voice-over. `ISpeechSynthesizer.Synthesize(streamId,
ChannelReader<string> text, options, ChannelWriter<byte[]> pcm, ct)` is streaming text in,
streaming PCM out, and `AudioStreamingBackend.Dubbing.cs` already turns a live entry's transcript
into a published audio stream that listeners play.

What is missing is the ingestion path for producer-supplied audio, and the wiring that lets the
existing synthesis speak a bot's text.

## Decisions

| Question | Decision |
|---|---|
| Who produces audio | Bot brings its own; server TTS is a fallback |
| When the fallback synthesizes | On demand at listen time, as a dub |
| Text↔audio alignment | Offsets optional; server derives from ingested audio position |
| Transports for bring-your-own-audio | RPC stream **and** an MCP chunked path |
| MCP audio format | Ogg Opus, base64 |
| Does real bot audio play by default | Yes. Only synthesized speech is gated |
| Voice for the fallback | `SpeakerVoices` — per author, already exists |
| What counts as "a bot" | `ChatEntryFlags.IsViaApi` |
| Text-only-with-timings (`PushTranscriptStream`) | Cut from v1 |

### Why the fallback is a dub

A dub stream is addressed as `StreamId.New(baseStreamId, language)`. A listener asking to hear a
bot requests `contentStreamId~<the entry's own language>`, so no contract changes and
`dubStreamId.Language == source language` is the discriminator that selects **speak mode**.

Speak mode bypasses three things, each of which exists only to serve translation:

- `DubStabilizer.Decide` / `DecideOnSource` — they answer "is this worth translating". In speak
  mode the answer is always yes.
- `WaitForTranslation` — nothing to translate; the source transcript feeds TTS directly.
- `WaitForOriginal` returns null, so `VoiceOverMix` runs dub-only. This is already a supported,
  already-logged mode (`"mixing dub-only"`), because `VoiceOverMix` takes a nullable original.

`PublishMix` registers the result in `StreamStore`, so ten opted-in listeners cost one synthesis.
That property is what makes on-demand affordable and is the main argument for this shape.

## Architecture

Two producers converge on one pipeline.

**Path 1 — RPC.** `ILiveAudioStreams.PushAudioWithTranscript(session, chatId, repliedEntryId,
clientStartAt, preSkip, RpcStream<AudioFrame>, RpcStream<ExternalTranscriptChunk>, ct)`. A new
`ExternalTranscriptStreamer` coordinates the two streams. `preSkip` stays here, as on `PushStream`,
because this path carries already-decoded frames with no container to carry it.

**Path 2 — MCP.** `start_voice_stream` / `append_voice_stream` / `finish_voice_stream`, backed by
the same node-pinned lease the text path uses. An append may carry a text delta, a base64 Ogg Opus
chunk, or both. This is an **adapter**: it feeds the same `ExternalTranscriptStreamer` through
channels. Transport logic does not live in MCP tools.

This path is **live**, not post-hoc: the lease holds the stream open, so listeners hear the bot as
its chunks arrive, exactly as on the RPC path. Arrival is chunkier — one call per utterance rather
than a continuous frame stream — so a long silence between appends reads as a pause, which is
acceptable for speech and is what the idle timeout already bounds. `clientStartAt` is taken at
`start_voice_stream` rather than supplied by the producer, since the lease is the only thing that
knows when the stream actually opened.

**Convergence.** Both emit (audio frames, text deltas) → `ExternalTranscriptBuilder` →
`TranscriptDiff` + `LinearMap` → `PushTranscript` plus Opus persistence → finalized
`ChatEntryAudio`. Server transcription is bypassed only in this explicit mode.

**Path 3 — the fallback** reuses the second half only: a text-only entry already has a content
stream, and speak mode synthesizes from it on demand.

### Contracts

```csharp
[DataContract, MessagePackObject]
public sealed partial record ExternalTranscriptChunk(
    [property: DataMember(Order = 0), Key(0)] string Text,
    [property: DataMember(Order = 1), Key(1)] bool IsAppend,
    [property: DataMember(Order = 2), Key(2)] double? AudioOffset,
    [property: DataMember(Order = 3), Key(3)] bool IsStable);
```

`AudioOffset` is seconds into the **producer's own audio** at the end of the resulting text — a
media position, never a timestamp. `null` means the server derives it. `IsAppend: false` replaces
the whole text, which a recognizer correcting itself needs and an LLM never uses.

MCP audio is Ogg Opus because it is ~30× lighter than PCM over base64 (a 30 s utterance is ~160 KB,
not ~3.8 MB), self-describing (sample rate, channels and pre-skip come from `OpusHead`, so
`preSkip` never appears in the MCP contract), and already parsed by `OggOpusReader`, whose
`Append`/`TryRead` shape suits chunks arriving across separate calls. Mono is enforced by the
existing `RequireMono`.

### Deriving an offset

When `AudioOffset` is null the builder pins the chunk to the audio already ingested. Opus frames
are fixed 20 ms and `OggOpusReader` exposes `FrameCount`, so the derived offset is
`frameCount × 20ms` — a real media position, which keeps the "never use arrival time" rule intact.

Accuracy follows the producer's interleaving, and direction matters. A bot sending *audio then its
text* gets an offset at the end of that audio: correct. A bot writing a clause then speaking it
pins the text to where audio ended *before* the clause, skewing the map one clause early. Both
produce a usable map; only the second is visibly off when scrubbing. Documented as: send audio
before its text, or supply offsets.

A producer that sends all text first and all audio last derives every offset to zero. Rather than
fail — the point of optional offsets is not failing — the builder detects a degenerate map at
finalize and distributes text evenly across the final duration, logging it.

### The gate

A per-user setting beside the existing dub and voice settings, default off, enforced in the
frontend `LiveAudioStreams` before it reaches the backend, so an un-opted-in listener never
triggers a synthesis.

"Bot" means `IsViaApi`, which also marks a human using an API key. The setting is named for what
it actually does — read API-written messages aloud — rather than inventing a bot account type
inside this feature.

## Failure modes

| Situation | Behaviour |
|---|---|
| One stream ends before the other | Drain the survivor, finalize on what arrived |
| Producer disconnects mid-utterance | Finalize; a crashed bot leaves a playable message |
| Supplied offset past final audio duration | Error — the producer is wrong about its own media |
| Degenerate map | Even distribution across final duration, logged |
| Corrupt base64, bad Ogg, non-mono | Reject that append, keep the lease open to resend |
| TTS down in speak mode | Reuse `DubSynthesizerDownDelay`; no audio rather than failed playback |
| Maintenance mid-stream | `MaintenanceStreamExt.RequireAvailable` on both streams, per chunk |
| Node holding the lease dies | Nothing finalizes — inherited from the text lease, not solved here |

## Reuse

### Existing abstractions to reuse

- `ISpeechSynthesizer`, `SonioxSpeechSynthesizer`, `FakeSpeechSynthesizer` — streaming TTS.
- `AudioStreamingBackend.Dubbing.cs`: `EnsureDub`, `RunDub`, `PublishMix`, `WaitForOriginal`,
  `DubSynthesizerDownDelay`; `VoiceOverMix` (nullable original = dub-only); `ReplayDubs`.
- `SpeakerVoices.Get(chatId, authorId)` — per-author voice with its full fallback chain.
- `OggOpusReader` (`Append`/`TryRead`/`ReadFrames`, `PreSkip`, `FrameCount`),
  `OggOpusStreamConverter`, `RequireMono`, `ActualOpusStreamHeader`, `AudioSource`.
- `AudioStreamingBackend.ProcessAudio.cs` — audio validation, registration, Opus persistence,
  `OpenAudioSegment`, media finalization, stream fixup. Split so only the transcript producer differs.
- `IAudioStreamingBackend.PushTranscript`, `Transcript`, `TranscriptDiff`, `LinearMap`,
  `ChatEntryAudio`, `StreamStore`.
- `ChatEntryStreams` lease shape — node-pinned `StreamId`, `ExpiringEntry`, offset-checked appends.
- `MaintenanceStreamExt.RequireAvailable`, `MeshRefResolvers` node routing.
- Imported-audio final validation from `consented-chat-import`.
- `TestWait`, `FakeTranscriber`, `McpTestBase`.

Nothing existing builds a time map from producer-supplied or derived offsets, and nothing runs a
same-language dub. Those two are genuinely new.

### Placement of new components

- `ExternalTranscriptChunk`, `ExternalTranscriptBuilder` → `ActualChat.Api/Transcription/`.
  Shared rather than service-local: imports and future transcription providers need the same
  offset→map construction. Recommended over `Streaming.Service`.
- `ExternalTranscriptStreamer` → `Streaming.Service/Services/`, beside native audio processing.
  Local: it coordinates this ingestion path and has no second consumer.
- Speak mode → inside `AudioStreamingBackend.Dubbing.cs`. Local: it is a mode of dubbing, not a
  new responsibility.
- MCP voice tools → `Mcp/Tools/`. Adapter only.

## Sequencing

1. Contracts + `ExternalTranscriptBuilder` — pure, unit-tested, no infrastructure.
2. Split `ProcessAudio`. Riskiest refactor; the native path's existing tests are the proof.
3. RPC `PushAudioWithTranscript` + `ExternalTranscriptStreamer` — first demonstrable speaking bot.
4. MCP voice lease — thin adapter over 3.
5. Speak mode + gate + replay.
6. Docs (`docs/integrations/streaming-writes.md` gains the audio modes) and the marketing row.

**Step 5 depends on nothing in 1–4** — it needs only the shipped text path, so it can run in
parallel or ship first if bots should be audible sooner.

## Testing

The builder carries the weight because it is pure and holds the risk: append vs replace, missing
and present offsets, derived values, degenerate maps, non-monotonic input, past-duration offsets,
Unicode boundaries, and two shapes taken from life — LLM token fragments and recognizer
self-corrections.

Integration covers audio and text at different rates, either side ending first, disconnects, map
validity, the finalized playable entry, a live listener, and replay. Native `PushStream` behaviour
must be proven unchanged. MCP gets a lifecycle test mirroring the text one plus bad-audio cases.
Speak mode gets a test that a text-only bot entry yields a dub-only stream in the author's voice,
and that an un-opted-in listener triggers no synthesis.

`FakeSpeechSynthesizer` and `FakeTranscriber` mean none of this needs a vendor. Waits go through
`TestWait`.

## Relationship to the existing plan

`docs/plans/external-transcript-streaming.md` Tasks 2–5 cover the ingestion half. This design keeps
their structure and departs on three points:

- **Offsets are optional**, with server derivation, where the plan requires them and fails visibly
  without. Rationale: producers without alignment data are common, and graceful degradation beats
  shutting them out.
- **`PushTranscriptStream` is cut** from v1. Text-with-no-audio is already served by the shipped
  text path, and timings with nothing to play against buy nothing today.
- **MCP carries audio**, where the plan says not to. Ogg Opus makes the byte cost acceptable, and
  MCP is the main integration surface — bring-your-own-voice should not require an RPC client.
