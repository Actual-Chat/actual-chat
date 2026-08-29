# 03 — Opus codec, VAD, resampling

The audio pipeline has only one codec — Opus — and one mic shape (mono,
16 kHz). This doc covers the encoding configuration, the two VAD
implementations, and the resampler that bridges hardware-imposed sample
rates back to 16 kHz.

## Opus configuration

The recorder uses Opus exclusively. The decoder side accepts Opus from any
of three containers (raw frames in `AudioFrame.Data`, OggOpus, or WebM)
but on the wire from sender → server it's always raw Opus packets in
`AudioFrame.Data`.

### Recording side

| Setting | Value | Source |
|---|---|---|
| Codec | Opus | `Constants.Audio.cs` |
| Sample rate | **16 000 Hz** | `Constants.Audio.RecordingSampleRate` |
| Channels | **1** (mono) | `Constants.Audio.Channels` |
| Bitrate | **32 000 bps** (32 kbps) | `Constants.Audio.Bitrate` |
| Frame duration | **20 ms** | `Constants.Audio.OpusFrameDuration` |
| Frame rate | **50 fps** | `Constants.Audio.FrameRate` |
| Samples per frame (input) | 320 | 16 kHz × 20 ms |
| Samples per frame (decoded) | 960 | 48 kHz × 20 ms |

Implementation choices (`opus-encoder-worker.ts`):

- **Native first**: if `globalThis.AudioEncoder` exists, configure it for
  `{ codec: 'opus', sampleRate: 16000, numberOfChannels: 1, bitrate: 32000 }`.
- **WASM fallback**: `@actual-chat/codec` ships a libopus build —
  `new codecModule.Encoder(16000, 32000)`. Used in browsers without
  WebCodecs.

Other Opus parameters (complexity, application mode, signal type) are left
at libopus defaults (complexity 10, OPUS_APPLICATION_AUDIO). Voice-tuned
mode is not used — the same encoder handles speech, music, and ambient
sound and the bitrate is low enough that the audio-vs-voip distinction is
secondary.

### Playback side

| Setting | Value |
|---|---|
| Sample rate | **48 000 Hz** |
| Channels | **1** (mono) |
| Frame size out | 960 samples |
| Pre-skip | **312 samples** at 48 kHz (codec warm-up) |

The 16/48 kHz asymmetry is just Opus working as designed: it always decodes
to 48 kHz internally, and 16 kHz capture is enough for the voice content
this app cares about. The 3× oversampling on output gives smoother resample
into the AudioContext's native rate (typically 48 kHz; sometimes 44.1 kHz
on macOS).

### `preSkip`

When a stream starts, the first ~312 samples are codec ramp-up garbage.
The recorder sends `preSkip` (in samples) along with `PushStream`, and the
receiver's `EncodedFrameBuffer` uses it to trim the equivalent number of
samples from the first decoded frame. Without this you get a faint click
at the start of every voice segment.

## Voice Activity Detection (VAD)

The recorder runs **two** VADs side-by-side and switches the active one
asynchronously after page load. The result is a stream of
`VoiceActivityChange { kind: 'start'|'end', offsetSamples, durationSamples?,
speechProb }` events plus per-frame audio power values for the UI.

VAD frames are independent of Opus frames — VAD operates on its own window
size, and the encoder worker treats VAD events as control signals to open
and close `AudioStream`s.

### WebRTC VAD (always loaded)

- Provided by an Emscripten compile of WebRTC's voice activity detector
  (file: `webRtcVadModule`, imported at the top of
  `audio-vad-worker.ts`).
- **30 ms window**, 16 kHz mono.
- Runs in the VAD worker.
- Lightweight: ~50 KB module, near-zero per-frame CPU.
- Used immediately at page load — gives the recorder voice-activity
  detection from the first second.

### Silero (Neural) VAD (delayed load)

- ONNX model file: `Components/AudioRecorder/workers/vad_batched.ort`.
- Loaded via `onnxruntime-web` (`ort-wasm-simd.wasm`).
- **32 ms window**, 16 kHz mono.
- Loads asynchronously **2 s after page interactive** to avoid blocking
  cold-start.
- Once ready, it takes over from WebRTC VAD inside the worker — same
  output shape so the encoder worker doesn't notice the swap.
- `audio-vad-worklet-processor.ts` is told the new window size via a
  `start(windowSizeMs: 30 | 32)` RPC and reconfigures itself.

### What the VAD signal does

The encoder worker uses the VAD signal as the trigger for `AudioStream`
lifetimes:

- `kind: 'start'` → open a new `AudioStream`, prepend any pre-roll
  frames, configure the codec, begin encoding/queueing.
- `kind: 'end'` → flush the encoder, complete the stream. The next "start"
  begins a fresh `PushStream`.

This means **a single recording session can produce multiple
`PushStream`s** — one per detected speech segment. Each becomes its own
`StreamId` on the server, its own `OpenAudioSegment`, and (after
transcription) its own chat entry.

The audio-power value (gain 0..1) from VAD also feeds the UI — it drives
the `active-recording-svg.lit.ts` mic-meter animation.

### Disabling VAD

There is no production-facing switch to disable VAD. Test pages
(`AudioRecorderTestPage.razor`) bypass VAD; production always gates on it.
The only related debug flag is "always record own audio for self-listen",
which is unrelated to gating.

## Resampling

The mic constraint asks for 16 kHz, but several platforms (iOS Safari,
some Android devices) ignore that and deliver 44.1 / 48 kHz instead. The
AudioContext's `context.sampleRate` is also platform-dependent. So:

- **Encoder worklet** runs at the AudioContext rate (whatever that is).
  It uses `samplesPerWindow = ceil(timeSlice * sampleRate / 1000)` so a
  20 ms window is correct in samples regardless of rate.
- **Encoder worker** is told the native rate via the `AudioEncoder` config
  (or the WASM encoder constructor). For the system encoder that means
  Opus is asked to encode at 16 kHz, with the WebCodecs implementation
  handling input → 16 kHz internally.
- **VAD worker** wants 16 kHz exactly. If incoming frames are at a
  different rate, it pulls them through a libsamplerate WASM resampler
  (`@actual-chat/resampler/resampler.wasm`, loaded via
  `ResamplerLoader.ts`).

Resampling never happens on the audio thread; it's always inside a worker.
The libsamplerate WASM is shared between recorder and player paths
(player uses it less commonly; the decoder always outputs 48 kHz, but a
playback `<audio>` element sometimes needs a rate conversion on
exotic devices).

## What about Audio Processing Module (APM)?

`Core.Audio/APM/` wraps the native WebRTC Audio Processing Module
(`webrtc-apm` — see `APM/runtimes/<rid>/native/`). Whether it's in the
recording path depends on the platform:

- **Web, Android, iOS/macOS** — not used. Echo cancellation, noise
  suppression and gain control are whatever the platform provides:
  `getUserMedia({audio: { echoCancellation: true, noiseSuppression: true }})`
  in the browser, and the OS voice-processing modes natively.
- **Windows** — used. `WindowsAudioCapture` runs every 10 ms capture frame
  through the APM with the echo canceller, noise suppression, AGC and
  high-pass filter enabled. The far-end reference the echo canceller needs
  is a **WASAPI loopback capture of the default render endpoint**, fed to
  `AnalyzeReverseStream` one frame per capture frame (zeros when the render
  endpoint is idle and the loopback delivers nothing). The capture graph
  deliberately uses `AudioRenderCategory.Other` / `MediaCategory.Other` and
  clears `EffectDefinitions`, so Windows' own voice processing is bypassed
  and the APM is the only echo canceller in the path.

## Constants summary

| Constant | Value | Source | Used by |
|---|---|---|---|
| `RecordingSampleRate` | 16 000 Hz | `Constants.Audio.cs` | encoder worker, VAD |
| `PlaybackSampleRate` | 48 000 Hz | same | decoder, feeder worklet |
| `Bitrate` | 32 000 bps | same | encoder worker |
| `Channels` | 1 | same | both |
| `OpusFrameDuration` | 20 ms | same | every stage |
| `FrameRate` | 50 fps | same | RPC ack period math |
| `OpusFrameLength` | 320 samples | same | encoder worklet |
| `PcmFrameLength` | 960 samples | same | feeder worklet |
| `VadFrameDurationMs` | 32 ms | same | Silero VAD |
| `WebRtcVadWindowMs` | 30 ms | `audio-vad-worker.ts` | WebRTC VAD |
| `VoicePreRollFrameLimit` | 15 frames (~300 ms) | `Constants.Audio.cs` | encoder pre-roll |
| `PreSkip` (default) | 312 samples @ 48 kHz | `AudioFormat.cs` | decoder warm-up trim |
