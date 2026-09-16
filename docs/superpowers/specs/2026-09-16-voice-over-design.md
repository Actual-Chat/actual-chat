# Voice-over dubbing — design

**Decided with Alexey on 2026-09-16.** Branch `feat/voice-dubbing` (PR #4514). Living doc to
update on delivery: `docs/live-audio/12-dubbing.md`.

## Problem

A live dub replaces the speaker's original audio. Its first word lands 3.5–8 s behind the speech
(measured 2026-09-16 on `c0f352200f`: short utterances 3.6–3.9 s, an 11 s one 7.8 s), so a
dubbing listener hears silence for that long, then the translation. Two consequences:

- The listening muxer holds the original for up to `DubWaitTimeout` (10 s) until the dub decides,
  then either substitutes the dub or serves the delayed original.
- An utterance that decides "no dub" after the author's next utterance has started is swallowed:
  the fallback re-registers as a plain stream and the muxer's "latest stream per author wins" rule
  cancels it (observed: a 0.6 s utterance served as 1 frame).

## Decisions

1. **One live behaviour, voice-over.** Every dubbing listener hears the original immediately and
   the translation on top of it. No replace mode, no per-listener choice.
2. **TV-style ducking.** The original plays at full volume until the dub's first frame, then at
   `VoiceOverDuckGain` (−12 dB, 0.25) for as long as the dub is speaking, with a
   `VoiceOverDuckHold` (1 s) over gaps between TTS chunks and a `VoiceOverDuckRamp` (50 ms) on
   each transition. It returns to full only if the dub ends while the original is still going.
3. **Cross-utterance ducking.** When the author's next utterance starts while the previous dub is
   still speaking, both play: the previous dub finishes, the new original starts ducked for as long
   as any dub of that author in that language is speaking.
4. **The mix is the `S~lang` stream**, produced once per (utterance, language) on the owner node
   and shared by every listener of that language. Clients need no change.
5. **Replay is unchanged** (substitution). A replay voice-over is a follow-up.

## Architecture

```
original S (Opus) ──► OpusToPcmDecoder ──┐
                                         ├─► VoiceOverMixer (PCM, ducking) ─► OpusFramePump ─► S~lang
Soniox TTS (PCM) ─────────────────────────┘
```

### `VoiceOverMixer` — `ActualChat.Core.Server/Audio/VoiceOverMixer.cs`

Pure PCM, no I/O, no clocks; reusable and testable with arrays. 48 kHz mono 16-bit, 20 ms frames
(`Constants.Audio.OpusFrameLength` samples).

- `void AddDubPcm(ReadOnlySpan<byte> pcm)` — buffers dub audio (any length; the mixer slices it
  into 960-sample frames).
- `bool HasDubAudio` — dub samples buffered.
- `void Mix(ReadOnlySpan<short> original, Span<short> output, bool isDubSpeakingElsewhere)` —
  produces one frame: `output = clamp(original * gain + dub)`, where `dub` is the next 960
  buffered dub samples or silence, and `gain` follows the duck state. `original` may be empty
  (the original has ended): then `output` is the dub frame alone.
- Duck state: `speaking` = dub samples were consumed within the last `VoiceOverDuckHold` **or**
  `isDubSpeakingElsewhere`. `gain` ramps linearly between 1.0 and `VoiceOverDuckGain` over
  `VoiceOverDuckRamp` (2.5 frames at 20 ms — implement per sample so the ramp is smooth).
- `bool IsDubSpeaking` — for the cross-utterance signal.

### `VoiceOverMix` — `Streaming.Service/Audio/VoiceOverMix.cs`

The per-`S~lang` pipeline, owned by the dub worker.

- Inputs: the original's `AsyncMemoizer<AudioFrame>` (replayed from its first frame), a
  `ChannelReader<byte[]>` of dub PCM (null-able: set once synthesis starts), the shared
  `DubActivity` for (author, language), `MomentClockSet`.
- Output: `ChannelWriter<AudioFrame>` of Opus frames, published as `S~lang` exactly as
  `StartSynthesis` publishes the dub today (`ActualOpusStreamHeader` first, then frames).
- Clocking: **while the original runs, each original frame produces one mixed frame with the same
  `Offset`** (decode → mix → encode; the original's timing passes through). **After the original
  ends**, a 20 ms `CpuClock` tick produces dub-only frames while `HasDubAudio` or synthesis is
  still running; offsets continue from the original's last offset plus wall-clock elapsed. Nothing
  is emitted while waiting for dub audio that hasn't arrived (the client schedules by offset).
- End: the original has ended **and** (no synthesis was started, or synthesis completed and the
  dub buffer drained).
- `DubActivity` (per (author, language), a `ConcurrentDictionary` on the backend): `SpeakingUntil`
  moment, refreshed to `now + VoiceOverDuckHold` on every frame where dub samples were consumed;
  read as `isDubSpeakingElsewhere = activity.SpeakingUntil > now`. Entries expire with the
  author's dub chain.
- Encoding: one `OpusFramePump` in unpaced mode (the mix is already paced by the original / the
  tick).

### Synthesizer hands out PCM

`ISpeechSynthesizer.Synthesize(string streamId, ChannelReader<string> text, SpeechSynthesisOptions
options, ChannelWriter<byte[]> pcm, CancellationToken ct)` — the streaming overload writes 48 kHz
s16le PCM. `SonioxSpeechSynthesizer` forwards `SonioxTtsClient.Run`'s PCM channel directly (the
pump inside it goes away); `FakeSpeechSynthesizer` writes PCM (its tone/silence generator); the
`RecordingSpeechSynthesizer` test double records text as today. The one-shot
`Synthesize(text) → AudioSource` (replay) keeps its own pump, untouched.

### Dub worker (`AudioStreamingBackend.Dubbing.cs`)

- `EnsureDub(S~lang)`: starts the worker and returns as soon as `S~lang` is published (the mix
  begins with the original at once). No `WhenDecided`, no `DubWaitTimeout`, no decision-timeout
  cooldown (`_dubCooldowns`, `StartCooldown`, `DubCooldown`, `IsCoolingDown`'s cooldown half). The
  synthesizer-down cooldown (`_synthesizerDownUntilTicks`, `DubSynthesizerDownDelay`) stays: when
  set, `EnsureDub` still publishes the mix (it's the transcoded original) but `RunDub` skips
  synthesis.
- `RunDub`: unchanged decision logic (`WaitForSourceTranscript` → `DecideOnSource` /
  translated-text fallback), but it now only decides whether to synthesize. `Dub` →
  `StartSynthesis` connects the synthesizer's PCM channel to the mix. `NoDub`, too short, no
  transcript, no translation → the mix just never gets dub audio. `ForgetDub` goes away (a miss is
  no longer retried — the mix already serves the original).
- `StartSynthesis`: keeps the per-author chain (`ChainDub`), voice lookup, the listener hooks;
  errors leave the original flowing (no fallback switching; log as today).
- `DubLatencyTrace`: `OnMixed()` (first mixed frame emitted → `mixed +X.Xs` after the request,
  expected ≈ 0), `OnDucked()` / `OnUnducked()` (`ducked at X.Xs of speech`); `first word behind
  speech` unchanged. `DubStabilizer`, `Skip`/`isLate`, `ReadTranslation` unchanged.

### Muxer (`ListeningStreamMuxer`)

- `GetStream` for a dubbing listener requests `S~lang` and serves what `EnsureDub` returns — no
  wait, no "no dub → serve the original", no "dub failed → serve the original"
  (`_undubbedStreamIds`, the dub retry branch in `ProcessStream`'s `finally`). A listener with no
  dub language gets the plain original as today. A dubbing listener always gets the mix — the
  decision isn't known when the stream starts — and when it's `NoDub` (`DubStabilizer.Decide`,
  unchanged rule) the mix is just the transcoded original.
- `TryRegister` already exempts dub entries from the per-author replacement, so two mixes of one
  author overlap as decision 3 requires. The tie/retry replacement for plain streams is untouched.

### Constants (`Constants.Audio`)

`VoiceOverDuckGain = 0.25f`, `VoiceOverDuckHold = 1 s`, `VoiceOverDuckRamp = 50 ms`. Removed:
`DubWaitTimeout`, `DubCooldown`. Kept: `DubSynthesizerDownDelay`,
`DubBacklogThreshold`, `DubTranslationRetryDelay`.

## Costs and trade-offs

- One Opus decode + mix + encode per (utterance, language) with at least one dubbing listener;
  the encode existed already (the dub), the decode is new and cheap. Listeners without a dub
  language are untouched.
- The original is transcoded for dubbing listeners (32 kbps → PCM → 32 kbps): slight quality
  loss, accepted.
- `NoDub` utterances are transcoded needlessly (the decision comes ~1.2 s after the stream starts,
  too late to switch without a discontinuity). Accepted.
- The mix outlives the original by the dub lag plus the dub's spoken length, as the dub does
  today; the muxer already tolerates that.

## Error handling

- Original decode error on a frame → that frame is treated as silence, logged once per stream.
- Synthesis failure → the mix continues with the original alone; `RunDub` logs as today and
  arms the synthesizer-down cooldown for provider failures (not for translation failures).
- The mix worker failing → the `S~lang` stream faults; the muxer's existing stream-error handling
  applies (retry serves the same `S~lang`, which is re-created by `EnsureDub` if gone).
- Cancellation (host stop, listener gone): the worker runs on the host-lifetime token as today;
  the mix keeps going while `S~lang` is published, listeners come and go.

## Testing

- `VoiceOverMixerTest` (Core.Server.UnitTests or Streaming.UnitTests): gain 1.0 with no dub; ramp
  down over `VoiceOverDuckRamp` once dub samples flow; hold over a gap shorter than
  `VoiceOverDuckHold`, ramp up after a longer one; `isDubSpeakingElsewhere` ducks with no dub
  samples; sum and clamp; original empty → dub only; dub buffer slicing across frames.
- `VoiceOverMixTest` (Streaming.UnitTests, `TestClock`): frame-for-frame offsets while the original
  runs; wall-clock ticks after it ends; ends when both inputs are done; no output while waiting
  for a late dub.
- `DubbingTranslationFlowTest`: original audible from t=0 before any decision; `NoDub` → the mix
  is the original only and ends with it; `Dub` → ducked mix, length = original + lag + tail;
  next utterance's mix starts ducked while the previous dub speaks (`DubActivity`); synthesis
  failure → original continues; a transcript-less 0.6 s utterance followed at once by another is
  heard in full.
- `ListeningStreamMuxerTest`: drop the hold/fallback cases; add "two dub entries of one author
  overlap, neither is cancelled".
- Manual: the two-device pass on dev (Russian speaker → English listener), reading the
  `Dub latency #…` lines.

## Follow-ups (not in this design)

- Replay voice-over (mix at replay time; the original is fully available so the dub could be
  time-aligned).
- A listener-side duck level (would need per-listener mixes or client-side ducking in both
  playback engines).
- Client "heard at" measurement (item 8 of the 2026-09-16 list) — the server-side `mixed +X.Xs`
  covers the server; a client timestamp is a separate small task.
- Translator latency grows with utterance length (3.4–4.7 s on an 11 s utterance vs 1.6 s on
  short ones) — the next latency lever after voice-over.
