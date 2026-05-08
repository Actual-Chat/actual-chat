# 07 — Receiver pipeline

The receiver consumes either `RpcStream<AudioFrame>` (per-stream pull) or
`RpcStream<LiveStreamItem>` (per-chat live multiplex), decodes Opus in a
shared Web Worker, and feeds 48 kHz PCM through an `AudioWorklet` ring
buffer to a `WebAudio` destination.

## Process model

```
┌─── Main thread ──────────────────────────────────────────────┐
│ ChatAudioUI (start/stop, audio focus)                         │
│  ├─ ChatPlayer / ChatListener / ChatReplayer                  │
│  │   └─ AudioTrackPlayer.cs (one per author per chat)        │
│  │       └─ WebAudioPlaybackEngine.cs                         │
│  │           └─ JS: AudioPlayer (one instance per track)     │
│  │               ├─ FeederAudioWorkletNode                    │
│  │               └─ MessageChannel ↔ decoder worker port      │
│  └─ AudioContextSource (singleton context per purpose)        │
└──────────────────────────────────────────────────────────────┘
        │ rpcSendNoWait('frame', [bytes, offset], transferList)
        ▼
┌─── Web Worker (shared, one per app) ──────────┐
│ opus-decoder-worker.ts                         │
│  ├─ EncodedFrameBuffer (per track)             │
│  ├─ Opus decoder (system AudioDecoder or       │
│  │   libopus WASM)                            │
│  └─ MessagePort writes PCM to feeder           │
└──────────────────────────────────────────────────┘
        │ MessagePort (transferable)
        ▼
┌─── Audio thread (AudioWorkletGlobalScope) ────┐
│ feeder-audio-worklet-processor.ts             │
│  ring buffer 8192 samples ≈ 170 ms            │
│  process() @ 128 samples (~2.67 ms)           │
│  → AudioContext destination                   │
└──────────────────────────────────────────────────┘
```

The decoder worker is a **process-wide singleton** (one per browser tab).
Multiple tracks share it, each with its own `EncodedFrameBuffer` keyed by
`internalId`. Encoded frames travel from main → decoder via
`rpcSendNoWait` (transferable `ArrayBuffer`); decoded PCM travels from
decoder → feeder via a per-track `MessagePort` pair.

## Trigger and ramp-up

### Live listening (whole chat)

1. **`ChatAudioUI.SetListeningState(chatId, true)`** — UI toggle. Updates
   `ActiveChatsUI`.
2. **`ChatAudioUI.GetOrCreatePlayer(chatId, ChatPlayerKind.Listening)`**
   (`ChatAudioUI.Players.cs`) — creates a `ChatListener`.
3. **`ChatListener.Play()`** (`Services/Playback/ChatListener.cs`):
   - Acquires audio focus via `AudioFocusUI`.
   - `LiveStreamProcessor` calls
     `ILiveAudioStreams.LegacyGetStream(session, chatId, settings)`.
   - `LiveStreamDemuxer` parses the `RpcStream<LiveStreamItem>` and fires
     `StreamStarted` per author.
4. **Per author**: `OnStreamStarted(streamInfo, audioFrames)` constructs an
   `AudioSource`, computes `playAt` = `max(streamInfo.BeginsAt,
   serverNow - PlaybackTargetBufferSizeWithVideo)`, and calls
   `Playback.Play(ChatAudioTrackInfo, AudioSource)`. That spins up an
   `AudioTrackPlayer`.
5. `AudioTrackPlayer` calls `WebAudioPlaybackEngine.Play()` which calls
   the JS `AudioPlayer.create(...)`.

### Per-message playback

A click on an audio chat entry triggers `ChatPlayer.Play(entryId)`. That
resolves the entry's `Audio.MediaId` (finalised) or `Audio.StreamId`
(in-flight) and calls `ILiveAudioStreams.GetStream(streamId, skipTo)`
directly — no demuxer, just one author's stream. Otherwise the path is
the same.

### Replay

`ChatReplayer.Play(startAt, rewindOffset, speed)` calls
`ILiveAudioStreams.GetReplayStream`. The wire stream is a
`RpcStream<LiveStreamItem>` paced server-side; the client doesn't add
its own playback delay logic. Wall-clock tracking via `CpuTimestamp`
corrects for sleep and pauses.

## C# side: `AudioTrackPlayer` and `WebAudioPlaybackEngine`

File: `src/dotnet/UI.Blazor.App/Components/AudioPlayer/AudioTrackPlayer.cs`.

`AudioTrackPlayer` extends `TrackPlayer` (from the generic
`MediaPlayback` API). It processes a `MessageProcessor<IPlayerCommand>`
queue — `PlayCommand`, `PauseCommand`, `ResumeCommand`, `AbortCommand`,
`EndCommand`. The hot path is `ProcessMediaFrame(AudioFrame)`:

```
PushFrame(frame)                      ← from upstream AudioSource
   │
   ▼ (head-start phase, _playDuration < 30 ms)
   push immediately, no delay        ← burst the first 30 ms
   │
   ▼ (real-time phase, 30 ms ≤ _playDuration < 200 ms)
   compute framePushMoment           = playDuration - frameDur - 30 ms
   delay = framePushMoment - _playStartedAt.Elapsed
   await delay if positive
   │
   ▼ (steady-state, _playDuration ≥ 200 ms)
   push immediately, rely on JS feeder's `isBufferLow` signal
   to gate further pushes
```

**Why pacing**: WASM decode + JS scheduling can't keep up with a
naive "send everything at once" approach when starting a new track.
The first 30 ms gets a burst so the feeder can light up; the next
170 ms is paced to match real-time so JS doesn't burn through the
queue before the worklet is even scheduled. After 200 ms,
flow-control via `isBufferLow` takes over.

### A/V sync hook (currently disabled)

Every 10 frames (~200 ms), `ApplyAudioSync()` runs:

```csharp
if (!ChatAudioUI.IsAudioSyncEnabled) return;       // FALSE in production
var desired = await CatchUpPolicy.GetDesiredCatchUp(authorId, ct);
if (desired >= Constants.Audio.PlaybackHardSkipThreshold)  // ≈ 2 s
    _playbackEngine.SkipUntil(targetMs);
else if (desired > 0)
    _playbackEngine.SpeedUpUntil(targetMs, dropEveryN: 4);
```

`LiveAudioCatchUpPolicy` reads `PlaybackLagTracker` (which fuses video and
audio lag samples from `OnPresentationLag` callbacks) to compute a
desired correction. The flag `IsAudioSyncEnabled` defaults to `false`
(`// NOTE(AY): Needs testing!`); toggle via `DebugUI.EnableAudioSync(true)`
on a dev instance.

For full design, see
[`live-video/11-buffering-and-av-sync.md`](../live-video/11-buffering-and-av-sync.md).

### Latency reporting

`ChatListener.OnStreamStarted` reports end-to-end latency once per
stream:

```csharp
var latency = serverClock.Now - streamInfo.BeginsAt;
_ = LiveAudioStreams.ReportAudioLatency(session, latency, ct);
```

Server records into `AppMeters.AudioLatency`. Receiver-side EMA-smoothed
presentation lag is reported separately at ~2 Hz via
`OnPresentationLag(ms)` from JS — that feeds the `PlaybackLagTracker`
used by the A/V sync policy.

## JS side: `audio-player.ts`

File: `src/dotnet/UI.Blazor.App/Components/AudioPlayer/audio-player.ts`.

`AudioPlayer` instances are pooled (`ObjectPool<AudioPlayer>`), since
attaching/detaching `AudioWorkletNode`s is expensive.

### Static init (once per app)

```typescript
decoderWorkerInstance = new Worker('/dist/opusDecoderWorker.js', { type: 'module' });
decoderWorker = rpcClient<OpusDecoderWorker>(decoderWorkerInstance);
await decoderWorker.create(AC, Versioning.assetMap, SharedSettings.all);
```

Loads the WASM Opus codec inside the worker, sets up RPC.

### Per-track create

`AudioPlayer.create(blazorRef, id, preSkip, title, album, authorId,
recordedAtMs, targetBufferSizeMs)`:

1. Acquire an `AudioContextRef` with `FeederNodeTrait` and
   `DemandInteractiveUI` traits (covered below).
2. In the audio context's ready callback:
   - Create a `MessageChannel` between decoder and feeder.
   - Construct `FeederAudioWorkletNode` (`port2`).
   - `decoderWorker.init(internalId, port1)`.
   - `feederNode.connect(audioContext.destination)`.
3. `decoderWorker.setTargetBufferSize(internalId, targetBufferSizeMs)`.
4. `decoderWorker.resume(internalId, recordedAtMs)`.
5. `feederNode.resume(preSkip)`.
6. Set `MediaSession` metadata for lock-screen controls.

`recordedAtMs` is the sender-side wall-clock at stream start
(`SourceBeginsAt`), used by the feeder worklet to compute
`presentationLagMs`.

### Hot path: `frame(bytes, sourceOffsetMs)`

Called ~50× per second from C#:

```typescript
public frame(bytes: Uint8Array, sourceOffsetMs: number): void {
    if (this.playbackState === 'ended') return;
    if (this.contextRef && !this.contextRef.isReady) return;
    const buf = bytes.buffer as ArrayBuffer;
    rpcSendNoWait(decoderWorkerInstance, 'frame',
        [this.internalId, buf, bytes.byteOffset, bytes.length, sourceOffsetMs],
        [buf]);  // Transfer ownership
}
```

This deliberately **bypasses the RPC client** to avoid per-call object
allocation and to transfer ownership of the `ArrayBuffer` (the worker
returns the buffer through the buffer pool after decoding).

### Feedback path: `OnPlaying`, `OnPresentationLag`

The feeder worklet emits state changes (`'playing'`, `'starving'`,
`'ended'`, `'paused'`) and `presentationLagMs` samples. The decoder
worker forwards them to main, which forwards to Blazor:

- `OnPlaying({ playingAt, isPlaying, isPaused, isEnded })` — `playingAt`
  is quantised to 100 ms buckets and only sent on material change or every
  `ReportPlayingMaxIntervalMs = 1000 ms` heartbeat.
- `OnPresentationLag(ms)` — at most every 500 ms; ms includes the
  audio-context's `outputLatency` so it's "speaker time, not buffer time".

## Decoder worker — `opus-decoder.ts`

### `EncodedFrameBuffer`

One per track. State machine:

| State | Meaning | Pull |
|---|---|---|
| `targetDurationMs > 0` | warming up: need at least N ms buffered before releasing first frame | gated |
| post-warmup | normal jitter buffer | `shiftReady` returns frame if buffered ≥ `targetDurationMs` |

Two control inputs from main:

- `skipUntil(sourceOffsetMs)` — drop all frames with offset < target.
- `speedUpUntil(sourceOffsetMs, dropEveryN)` — counter-based speed-up;
  drop every Nth frame until reaching target.

These are the audio side of A/V sync. They live in the worker (not the
worklet) because dropping encoded frames is much cheaper than dropping
PCM samples.

### Decode loop

For each `EncodedFrame`:

1. If `skipUntilMs` is active and `sourceOffsetMs < skipUntilMs`, drop.
2. If `speedUpUntilMs` is active and the frame counter says "drop", drop.
3. `decoder.decode(EncodedAudioChunk)`.
4. Forward decoded PCM samples to feeder via `MessagePort` along with
   `sourceRecordedAtMs`, `sourceOffsetMs`, and `presentationLagMs`
   metadata.
5. Pool the input `ArrayBuffer` back to main via `transfer`.

The decoder is either the system `AudioDecoder` (WebCodecs) or a libopus
WASM build, picked at `create()` time.

## Feeder worklet — `feeder-audio-worklet-processor.ts`

The terminal stage. Lives in `AudioWorkletGlobalScope` (sample-rate
clock, real-time priority).

### Ring buffer

`AudioRingBuffer`, 8192 float32 samples mono = ~170 ms at 48 kHz.

### `process()` per quantum (128 samples ≈ 2.67 ms)

```
if buffer < 128 samples and chunks empty:
    output zeros
    set playbackState = 'starving'
else:
    pop 128 samples → output channel
    advance playingAt by 128/sampleRate
    update presentationLagMs = ServerClock.now() - (sourceRecordedAtMs + sourceOffsetMs)
    if buffer drops below feederTargetDuration:
        set bufferState = 'low'   (triggers 'isBufferLow' callback to main)
```

State changes are reported only on material change or heartbeat — the
50 fps render rate would otherwise spam the message channel.

### `feederTargetDuration`

A small target (a few render quanta, ≈ 5–10 ms) below which the worklet
asks the decoder for more frames. Above this it stays quiet. The actual
jitter buffer is upstream (decoder's `EncodedFrameBuffer`), and is
configured via `targetBufferSizeMs` from C#.

For audio-only tracks, `targetBufferSizeMs = 0` (no extra delay needed).
For audio paired with video, `targetBufferSizeMs =
PlaybackTargetBufferSizeWithVideo` so the audio buffer matches the video
buffer's natural delay — this is the original "audio adopts video's
target delay" plan, applied at buffer level even before the A/V sync
catch-up policy kicks in.

## Audio context — `AudioContextSource`

File: `src/dotnet/UI.Blazor.App/Services/audio-context-source.ts`.

A small reference-counted manager for `AudioContext`s. One context per
"purpose" (`recording` | `playback`) is enough; everyone who needs an
output node gets a `AudioContextRef` and configures it via traits.

### Traits

- **`FeederNodeTrait`** — adds an `AudioWorkletNode` for the feeder
  worklet and provides the `MessageChannel` plumbing.
- **`DemandInteractiveUI`** — if the context can't resume (Safari's
  "must be in user gesture" rule), surface a UI prompt via
  `InteractiveUI.Demand()`.
- **`DestinationFallbackTrait`** — iOS Safari workaround: routes audio
  through a hidden `<audio>` element so iOS's "lock screen now playing"
  metadata works (regular AudioContext output doesn't show up there).

### Recovery loop

A 3 s health-check loop watches for `state === 'suspended'` and
attempts `context.resume()` on the next user gesture. Repeated failures
escalate to a UI prompt.

## Multi-author mixing

There is **no explicit mixer**. Each author's `AudioTrackPlayer` creates
its own `AudioPlayer` → `FeederAudioWorkletNode`. All feeder nodes
`connect(destination)` to the shared `AudioContext.destination`, and
WebAudio mixes them automatically. There's no cross-author coordination,
no ducking, no spatial positioning.

The result is "everyone speaking at once just sounds like everyone
speaking at once" — which is acceptable for chat where listeners have
visual cues to decide who to focus on, and where the per-author live
audio is short, voice-gated, and rarely overlapping.

## Audio focus (mobile)

`ChatAudioUI.Players.cs:104+` integrates with `AudioFocusUI`:

- `TryAcquireAudioFocus()` — Android `AudioManager.requestAudioFocus`.
- `OnAudioFocusLost(canDuck)` —
  - `canDuck = true` (transient): ignore (no ducking implementation;
    keeps playing).
  - `canDuck = false` (loss): pause or stop.
- On focus regain: resume from paused offset (live → resume listening,
  replay → resume from saved position).

iOS uses MAUI-side audio session APIs in the iOS app; on the web this
collapses to "always have focus".

## Track lifecycle

```
PlayCommand
   ↓
WebAudioPlaybackEngine.Play() → AudioPlayer.create() (from pool)
   ↓
PushFrame loop (with pacing)
   ↓
Source ends OR Abort/End command
   ↓
WebAudioPlaybackEngine.End(mustAbort)
   ↓
AudioPlayer cleanup → return to pool
   ↓ (decoder worker keeps running, parks per-track state)
```

Decoded PCM is **not** buffered after the source ends — `End` flushes
remaining frames, the feeder reports `'ended'`, and the player reports
back to C#.

## Common error paths

| Symptom | Recovery |
|---|---|
| Suspended `AudioContext` (autoplay blocked) | `DemandInteractiveUI` trait surfaces a prompt; resume on user gesture |
| iOS lock-screen no metadata | `DestinationFallbackTrait` routes through hidden `<audio>` |
| Feeder starves | `bufferState='low'` → main thread pauses pacing, lets buffer fill |
| Decoder error | Worker logs, drops frame, continues; per-track `EncodedFrameBuffer` reset on next keyframe equivalent (every Opus frame is a keyframe) |
| Cross-shard fetch fails | `RemoteAudioStreamCache.Store.Get` returns `null`; client retries on next stream-start invalidation |
| Audio focus lost mid-playback | `Playback.Pause()`; resume on regain |
