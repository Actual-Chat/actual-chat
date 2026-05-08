# 02 — Recorder pipeline

The recorder turns one user's microphone into a non-realtime
`RpcStream<AudioFrame>` headed at the API pod. It uses three execution
contexts — main thread, AudioWorklet (audio thread), Web Worker — and two
parallel chains, one for VAD and one for encoding.

## Process model

```
┌──────────────────── Main thread (Blazor + JS) ───────────────────┐
│ ChatAudioPanel.razor                                              │
│   └─ ChatAudioUI.SetRecordingChatId(chatId)                       │
│        └─ AudioRecorder.cs.StartRecording(chatId, replyId)        │
│             └─ WebRecorderEngine.cs                               │
│                  └─ JS: AudioRecorder.startRecording(...)         │
│                       └─ OpusMediaRecorder                        │
│                            ├─ getUserMedia() → MediaStream        │
│                            ├─ AudioContext + AudioWorklets        │
│                            ├─ spawn / get encoder worker          │
│                            └─ spawn / get VAD worker              │
└───────────────────────────────────────────────────────────────────┘
        │                                       │
        ▼                                       ▼
┌──── Audio thread (AudioWorklet) ───┐   ┌──── Audio thread ──┐
│ opus-encoder-worklet-processor.ts  │   │ audio-vad-worklet- │
│  process() @ 128 samples           │   │  processor.ts      │
│  ring buffer 8192                  │   │  ring buffer 8192  │
│  every 20 ms → push samples to     │   │  every 30/32 ms →  │
│  encoder worker                    │   │  push to VAD worker│
└────────┬───────────────────────────┘   └────────┬───────────┘
         │ rpcSendNoWait(samples)                 │ rpcSendNoWait(samples)
         ▼                                        ▼
┌──── Web Worker ────────────────────┐   ┌──── Web Worker ────┐
│ opus-encoder-worker.ts             │   │ audio-vad-worker.ts│
│  AudioEncoder (system) or          │   │  WebRTC VAD always │
│   libopus WASM                     │   │  Silero ONNX on    │
│  receives 'voice start' →          │   │   delay 2 s        │
│   create AudioStream               │◀──┤  emits             │
│  receives 'voice end' →            │   │  VoiceActivityChange│
│   close AudioStream                │   │  + audio power     │
│  audio-streamer.ts queues          │   └────────────────────┘
│  RpcStream<AudioFrame>             │
│   └─▶ ILiveAudioStreams.PushStream │
└────────────────────────────────────┘
```

The encoder worker and VAD worker each have their own RPC channel back to
the encoder worklet / VAD worklet via `MessageChannel`. Heartbeats from main
to the encoder worker keep it alive.

## Trigger and ramp-up

1. **`ChatAudioUI.SetRecordingChatId(chatId)`** — the user toggles
   `RecorderToggle.razor`. State propagates through `ActiveChatsUI`.
2. **`AudioRecorder.StartRecording(chatId, repliedChatEntryId, ct)`**
   (`AudioRecorder.cs:61`) — acquires audio focus via `AudioFocusUI`, calls
   `_engine.Start(...)` with a 30 s timeout.
3. **`WebRecorderEngine.Start`** wraps a Blazor `IJSObjectReference` — first
   call also runs `EnsureInitialized` which calls
   `BlazorUIAppModule.AudioRecorder.create` to create the JS singleton.
4. **JS `OpusMediaRecorder.start(chatId, repliedChatEntryId)`**
   (`opus-media-recorder.ts`):
   - `getMicrophoneStream()` calls `getUserMedia()` with constraints:
     ```js
     audio: {
       channelCount: 1,
       sampleRate: 16000,
       sampleSize: 32,
       echoCancellation: true,
       autoGainControl: !(isAndroid),  // AGC off on Android (causes silence)
       noiseSuppression: true,
       latency: 0.02,
     }
     ```
     Some platforms ignore the requested rate; the worklet handles the
     real `context.sampleRate` and the VAD path resamples to 16 kHz when
     needed.
   - Connects two `AudioWorkletNode`s to the source — one feeds the encoder
     worklet, one feeds the VAD worklet.
   - Initialises both workers via the actuallab-rpc client.
5. **Heartbeat**: main thread calls `encoderWorker.heartbeat()` every
   `AUDIO.rec.heartbeat.intervalMs`. If the worker doesn't see one for
   ~2 s it self-terminates — protects against a hung main thread leaving
   the recorder running forever.

## Audio-thread stage: AudioWorklet processors

Both worklets share the same shape: a small ring buffer
(`audio-ring-buffer.ts`, 8192 samples), `process()` called at
`128 / sampleRate ≈ 2.67 ms` per call (the AudioWorklet quantum), and an
RPC channel out to a worker.

### Encoder worklet — `opus-encoder-worklet-processor.ts`

- `processorOptions = { timeSlice: 20, sampleRate: context.sampleRate }`.
- `samplesPerWindow = ceil(timeSlice * sampleRate / 1000)`. At 48 kHz this
  is 960 samples per 20 ms; at 16 kHz it's 320.
- Each call:
  1. Push input samples to ring buffer.
  2. Pull `samplesPerWindow` if available.
  3. Compute `capturedAtMs = Date.now() − samplesAvailable / sampleRate * 1000`.
  4. `worker.onEncoderWorkletSamples(buffer, capturedAtMs, RpcNoWait)`.
- Periodic "I'm alive" beat to the main-thread state server every
  `AUDIO.rec.recordingInProgressReportSamples` samples — drives the UI mic
  indicator.

### VAD worklet — `audio-vad-worklet-processor.ts`

- Window size negotiated by the VAD worker on `start(windowSizeMs)`:
  - **30 ms** for WebRTC VAD.
  - **32 ms** for Silero (Neural) VAD.
- Same ring-buffer pattern; pushes the configured window to
  `audio-vad-worker.ts:onFrame()`.

## Web-Worker stage: VAD

File: `Components/AudioRecorder/workers/audio-vad-worker.ts`.

`VadLoader` always loads WebRTC VAD up front (Emscripten WASM module from
`webRtcVadModule`). After 2 s, it asynchronously loads Silero
(`vad_batched.ort` via `onnxruntime-web`). Both run side-by-side; Silero
takes over once ready (Silero is more accurate but heavier; the warm-up
delay keeps page-load latency down).

Each frame:

1. Optional resample to 16 kHz via `ResamplerLoader` (libsamplerate
   compiled to WASM, from `@actual-chat/resampler`). Mic might be 44.1 kHz
   on iOS even when 16 kHz is requested.
2. `vad.appendChunk(samples)` returns either:
   - `number` — current speech probability / gain (0..1) → routed to UI
     animation (recording SVG meter).
   - `VoiceActivityChange { kind: 'start'|'end', offsetSamples,
     durationSamples?, speechProb }` → routed to encoder worker.

The VAD worker calls `encoderWorker.onVoiceActivityChange(change)` directly
(both workers share `appConstants` and `apiUrl` via shared settings).

## Web-Worker stage: encoder

File: `Components/AudioRecorder/workers/opus-encoder-worker.ts`.

### Codec selection

On `create()`:

- If `globalThis.AudioEncoder` exists, use the native WebCodecs encoder
  configured for `{ codec: 'opus', numberOfChannels: 1, sampleRate: 16000,
  bitrate: 32000 }`.
- Otherwise, fall back to the libopus WASM build from `@actual-chat/codec`
  (`codecModule.Encoder(16000, 32000)`). The WASM build is ~150 KB and
  bundled with the app.

Either way the output is plain Opus packets in `Uint8Array`. The encoder
worker pairs them with the source `capturedAtMs` from the worklet to build
the `AudioFrame.Offset`.

### Stream lifecycle

- `onVoiceActivityChange({kind:'start'})` → `startRecording()`:
  - System path: `systemEncoder.configure(systemCodecConfig)`.
  - Creates an `AudioStream` via `AudioStreamer.addStream(preSkip, chatId,
    repliedChatEntryId)`.
- For every encoded chunk: `audioStream.addFrame(opusBytes, true,
  sourceCapturedAtMs)`.
- `onVoiceActivityChange({kind:'end'})` → `stopRecording()`:
  - `systemEncoder.flush()`.
  - `audioStream.complete()` — closes the iterator that `audio-streamer`
    is publishing.

If VAD is disabled (or running in pure-VAD-off debug mode), the encoder
worker streams continuously while recording is active.

### Pre-roll

VAD has a startup latency: speech that triggered the "voice start" is
already a few hundred ms in the past. To avoid clipping the first
syllables, the encoder worker keeps a small rolling buffer of recent
encoded frames (configured by `Constants.Audio.VoicePreRollFrameLimit ≈
15 frames = 300 ms`) and prepends them when a new `AudioStream` opens.
The `preSkip` field on `PushStream` tells the decoder how many samples
to discard at the very start (codec warm-up samples).

## Web-Worker stage: audio-streamer (RPC pump)

File: `Components/AudioRecorder/workers/audio-streamer.ts`.

`AudioStream` represents one `start..end` recording session. It owns:

- `Denque<AudioStreamFrame>` queue (capacity
  `AUDIO.stream.maxBufferedFrames`).
- A pooled buffer to reuse `ArrayBuffer`s across frames (`bufferPool`).
- `EventHandlerSet` to wake the RPC generator when frames arrive.

`addFrame(frame, isEncodedAudioChunk, sourceCapturedAtMs)` enqueues a DTO:

```ts
{
  Data: Uint8Array,                  // raw Opus packet
  Offset: TimeSpan ticks,            // frameIndex × 20 ms (since stream start)
  Duration: TimeSpan ticks,          // 20 ms
  IsKeyFrame: true,                  // always
}
```

The wire offset is **per-stream**, anchored at the moment of `voice start`,
not at the recording start.

`stream()` is an async generator that wraps everything in a retry loop for
peer-change recovery:

- Builds an `RpcStream<AudioFrameDto>` with
  `MediaRpcStreamOptions.audioRecording<AudioFrameDto>()` (sets
  `AckPeriod = 5` and `AllowReconnect = true`).
- Calls `liveAudioStreams.PushStream(RPC_SESSION_DEFAULT, chatId,
  repliedChatEntryId, clientStartAt, preSkip, RpcStreamRef, ct)`.
- If the peer changes mid-stream, the iterator's `return()` fires, the
  outer loop creates a fresh `PushStream` call, and resumes from the
  oldest still-buffered frame. **No frames are dropped** — the server-side
  contract is non-realtime.

`clientStartAt` is the **stream** start time in Unix epoch seconds (double),
biased by `AUDIO.rec.heartbeat.offsetMs` for clock-skew correction. The
server compares this against its own clock and, if drift > 5 s, overrides
with server time.

## State the recorder publishes back

`OpusMediaRecorder` calls `RecorderStateServer.OnRecordingStateChange(...)`
with a tuple `{isRecording, isSignalDetected, isConnected, isVoiceActive}`.
On the C# side `AudioRecorder.cs:148-197` translates that into
`AudioRecorderState`:

- `IsRecording` — encoder worker has an active stream.
- `IsSignalDetected` — mic is producing samples (debounced).
- `IsConnected` — RPC peer is healthy.
- `IsVoiceActive` — VAD says we're inside a speech segment.

Plus per-frame audio power (gain 0..1) → `RecordingActivity` Lit component →
the animated mic SVG.

## Stop and cleanup

`AudioRecorder.StopRecording()` (`AudioRecorder.cs`):

- Calls `_engine.Stop()` → JS `AudioRecorder.stopRecording`.
- `OpusMediaRecorder.stop()` ends the in-flight `AudioStream` (if any),
  stops the worklets, releases the `MediaStream` tracks, releases audio
  focus.

`AudioStream.complete()` lets the RPC generator drain any remaining queued
frames, then resolves. If the user disabled the mic mid-stream, the server
still sees a clean end and finalises the segment / transcript.

## Common error paths

| Symptom | Where it's caught | Recovery |
|---|---|---|
| `getUserMedia` denied | `WebMicrophonePermissionHandler.requestPermission` | UI prompt; `AudioRecorderException` |
| Mic returns wrong sample rate | `audio-vad-worker.processQueue` resamples to 16 kHz | Transparent |
| Encoder worker hangs | Main heartbeat watchdog | Worker self-terminates, recorder fails fast |
| RPC peer disconnects | `AudioStream.stream()` retry loop | New `PushStream` call resumes from queue |
| `getUserMedia` returns silence | UI shows "no signal", VAD never fires | User intervention |
| AGC silences mic on Android | constraint excludes AGC on Android | Already handled |
| AudioContext suspended | `AudioContextSource` traits, user-interaction prompt | Resume on next gesture |
