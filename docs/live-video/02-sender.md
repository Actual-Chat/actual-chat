# 02 — Sender pipeline

The sender turns a `MediaStreamTrack` (camera or `getDisplayMedia()`) into a
realtime `RpcStream<VideoFrame>` headed at the API pod. Almost all of it runs
in a dedicated Web Worker — one per recorder, so a camera and a screencast
session can run side-by-side without contending for the same encoder pool.

## Process model

```
┌─────────────── Main thread (Blazor) ──────────────────────┐
│ VideoTrackPlayer.razor (preview), VideoRecorder           │
│   getUserMedia / getDisplayMedia → MediaStreamTrack       │
│   <video srcObject=track>                                 │
│   requestVideoFrameCallback loop                          │
│     └─▶ new VideoFrame(<video>) → worker.pushFrame(frame) │
└──────────────────────┬────────────────────────────────────┘
                       │ postMessage (transfer)
┌──────────────────────▼─── Worker (recorderWorker.js) ─────┐
│ ReplaceableSlot<VideoFrame> (capacity = 1)                │
│   ▼                                                       │
│ Pipeline operators (described below)                      │
│   ▼                                                       │
│ ILiveVideoStreams.PushStream  (Fusion RPC over WebSocket) │
└───────────────────────────────────────────────────────────┘
```

The "single-slot" inbox between main and worker is intentional: if the worker
falls behind, older frames are dropped before they reach the GPU. This avoids
exhausting Chromium's `VideoFrame` GPU pool (12–20 buffers) and gives
"newest frame wins" semantics for a live stream.

> Why not transfer the `MediaStreamTrack` directly? `MediaStreamTrackProcessor`
> across realms starves after ~20 frames in current Chrome; Safari only honors
> track transfer in 18+. So we generate `VideoFrame`s on the main thread from
> `<video>` and push them frame-by-frame.

## The operator chain

`Recorder.start()` builds the pipeline in `sender/recorder.ts`. It is a chain
of async-iterable operators (RxJS-style `ix`):

```
mstpSource
  │  CapturedFrame
  ▼
stampCaptureTime          (MonotonicClock; epoch-flip ⇒ forceKeyframe)
  ▼
attachSourceDims          (record pre-downscale resolution)
  ▼
downscale                 (GPU; CapturedFrame → SimulcastBundle)
  ▼
applyKeyframePolicy       (frame counter + wallclock floor)
  ▼
encode                    (per-layer WebCodecs encoders, parallel)
  │  EncodedFrame
  ▼
wireSend                  (build VideoStreamFrame, hand to RpcStream sender)
```

Sources: `…/Services/Video/operators/*.ts`.

### `mstpSource` — capture
File: `operators/capture.ts`.
Wraps `MediaStreamTrackProcessor.readable` (test mode) or the
`ReplaceableSlot` push from main. Yields `CapturedFrame` envelopes carrying the
raw `VideoFrame`, `capturedAt`, a (yet-zero) `index`, and a shared
`VideoRecordingStats` reference.

### `stampCaptureTime`
File: `operators/stamp-capture-time.ts`.
Stamps `capturedAt = { timeMs, epoch }` from `MonotonicClock.now()`. When the
clock's epoch increments (sleep/wake, system clock step, Bluetooth audio
takeover), it flips `forceKeyframe = true` so the receiver's
`resetOnEpochChange` (07) can re-anchor.

### `attachSourceDims`
File: `operators/attach-source-dims.ts`.
Stamps `sourceWidth/sourceHeight` from the raw frame *before* downscale. The
server uses these to track when the publisher's source resolution changes
(window resize, screen rotation) and to make resolution-aware quality choices.

### `downscale` — produces the simulcast bundle
File: `operators/downscale.ts`. The downscaler is created lazily on the first
frame:

- Production: `WebGpuDownscaler` (`webgpu/downscaler.ts`) — one GPU render pass
  per non-identity tier.
- Single-layer or test: `identityDownscaler()` clones once.

Output is a `SimulcastBundle = { primary, extras[], stats }` where `primary` is
the top tier and `extras[]` is bottom-first (`extras[0]` = base layer, lowest
res). All three carry the same `capturedAt`/`index`/`forceKeyframe`. Owns the
GPU resources; cleans them up in `finally`.

### `applyKeyframePolicy`
File: `operators/apply-keyframe-policy.ts`.
Forces a keyframe on **any** of:
1. Frame counter % `keyframeIntervalFrames == 0`.
2. Wallclock — `now - lastKeyframeAtMs ≥ maxKeyframeIntervalMs`.
3. Upstream already set `forceKeyframe = true` (e.g. epoch flip, dim change).

Sets the flag on every layer in the bundle. The reset of the counter happens
on any trigger.

### `encode` — per-layer encoders, parallel
File: `operators/encode.ts`. For each `SimulcastBundle`:

- Lazily acquires one `PooledEncoder` per layer (see `EncoderPool` below).
- Submits all layers in parallel (`Promise.allSettled`); each call passes
  `{ keyFrame: forceKeyframe }` so all simulcast keyframes line up.
- Yields `EncodedFrame`s **bottom-first** (L0, L1, …, LN-1). Reason: the RPC
  layer's compaction step treats only L0 keyframes as restart points.
- On `CodecToAsyncAdapterResetError` (encoder timed out, see `adapters.ts`):
  marks the next bundle as a keyframe and retries.

Each `EncodedFrame` carries its `EncodedVideoChunk`, `EncodedVideoChunkMetadata`
(SPS/PPS for keyframes), the source `capturedAt`, the `index`, the layer id,
and source/encoded dimensions.

### `wireSend` — sink to RPC
File: `operators/wire-send.ts`.

- On the **first top-layer keyframe**, calls `sender.init(StreamFormat)` to send
  codec, dims, source dims, and base64 description; fires `onStreamCreated`
  back to main.
- For every frame, builds a `VideoStreamFrame` (see [04](./04-rpc-and-framing.md))
  and queues it into a `Denque` that an `RpcStream<VideoFrameDto>` generator
  drains.
- Computes wall-clock offset:
  `offset = (frame.capturedAt.timeMs - sourceStartMs) × 10000` (100-ns ticks).
  Carries an `offsetEpoch` so the receiver can detect clock discontinuities.
- Caches `description` (HVCC/SPS) per layer because HEVC keyframes after the
  first may omit it.
- Races each `send()` against the recorder's abort signal so a stalled RPC peer
  can't block `Recorder.stop()` for more than 1 s.
- Compacts the queue when more than 2 keyframes for the same layer are
  buffered: drops everything before the most recent keyframe (still
  decodable).

## EncoderPool — keep the NVENC slot warm

File: `sender/encoder-pool.ts`, `sender/session.ts`.

The pool parks released encoders (instead of disposing them) for
`parkTtlMs = 5 s`. The trick is to override the encoder's `dispose` on
checkout so the operator's `finally` parks instead of kills it.

Two non-obvious rules pay off here:

1. **Codec string stays constant across sessions.** `getCodecForCategory()`
   picks the highest level in the category (e.g. H.264 High 5.2) and uses it
   for every resolution. Chrome re-initialises NVENC when the codec string
   changes; reusing a slot would otherwise be impossible.
2. **Bitrate-only reconfigures are in-place.** Dimension changes replace the
   encoder instance, but the pool holds the slot during the gap.

`SenderSession` owns the pool, the `MonotonicClock` used for capture timing,
and (optionally) a `MediaStreamTrackGenerator` writer for local preview. It
survives `stop()` → `start()` cycles so warm-state is reused.

## Local preview tap

If the main thread provides an MSTG writer via the session, `previewTap`
(`operators/preview-tap.ts`) clones each `CapturedFrame.frame` and writes the
clone to the writer; the original stays in the pipeline. The Blazor side has a
`<video srcObject=…>` bound to that MSTG track, so users see what they're
sending. Local preview is **pre-encode** (post-downscale would be possible but
costs an extra GPU read; the current design keeps preview on the unencoded
frame).

## RPC initiation (`streaming-glue.ts`)

`ensureRpcPush()` is called the first time a frame is ready to send. It:

1. Initialises the worker's Fusion RPC peer (`Api.init`) with the chat's
   `apiUrl`, the session token provider, and `requireConnection: true`.
2. Looks up the `ILiveVideoStreams` client.
3. Calls `PushStream(RPC_SESSION_DEFAULT, chatId, sourceStartOffsetSeconds,
   formatDto, sourceKind, RpcStreamRef)` (the stream is realtime, with
   `allowReconnect: false` — peer changes restart the stream from scratch).

The `Denque` between `wireSend` and the RPC generator is the only intentional
queue inside the worker. The actual buffer is the `RpcStream` ring + ACK
compaction on the wire.

## Stop and cleanup

`Recorder.stop()`:

- Aborts the source's stop controller, which lets the iterator complete.
- Schedules `STOP_DRAIN_GRACE_MS = 1000` ms abort timeout. If the pipeline
  hasn't drained by then, the operators are aborted hard so the worker doesn't
  hang on a dead RPC pump.
- Encoder pool keeps parked entries for 5 s in case `start()` is called again
  shortly after (e.g. switching the camera).

## Stats carried through the pipeline

`VideoRecordingStats` (`frame-envelopes.ts`) is one mutable object per run,
referenced by every envelope. Operators increment counters as frames pass:
`framesCaptured`, `framesProcessed`, `framesDroppedDimMismatch`,
`framesDroppedBackpressure`, `chunksEncoded`, `keyframesEncoded`,
`bytesEncoded`, `wireQueueDepth`, `wireFramesDropped`, `wireKeyframesDropped`,
`isPeerConnected`, `lastAckAgeMs`, `lastCapturedEpoch`, …

`Recorder.getStats()` (and the 1 Hz health reporter) reads this object; it is
the source for `RecorderHealthSnapshot` sent to the server (08).

## Worker contract

File: `sender/recorder-worker-contract.ts`. The main-thread façade
(`video-recorder.ts`) sees the worker through this RPC interface
(actuallab-rpc):

```ts
interface RecorderWorker extends SharedSettingsWorker {
    init(appConstants): Promise<void>;
    pushFrame(frame: VideoFrame, noWait?): Promise<void>;
    endSource(noWait?): Promise<void>;
    start(opts: RecorderWorkerOptions): Promise<void>;
    stop(): Promise<void>;
    requestKeyframe(): Promise<void>;
    getStats(): Promise<VideoRecordingStats>;
}

interface RecorderWorkerCallbacks {
    onError(error: string): void;
    onStreamCreated(codecSettings: string): void;
    onStreamEnded(reason: string): void;
}
```

`RecorderWorkerOptions` is `WireSafeRecorderConfig`: only `structuredClone`-able
fields — `chatId`, `apiUrl`, `sourceKind`, `encoderConfigs[]`,
`keyframeIntervalFrames`, `maxKeyFrameIntervalMs`. No closures, no track
references.

## Common error paths

| Symptom | Where it's caught | Recovery |
|---|---|---|
| Encoder hangs on `output()` | `AsyncVideoEncoder` timeout (3 s first frame, 1 s steady) | Reset, force keyframe, retry bundle |
| Frame dims ≠ encoder config | `dim-mismatch-guard.ts` | Drop frame; counter tick |
| HW NVENC slot lost | next `acquire()` on pool | Pool recreates encoder, runs through `handleEncoderReset` |
| RPC pump fails mid-stream | `wireSend` observes `whenDisposed` | Replace sender, fresh init on next keyframe |
| `Recorder.stop()` drain hangs | 1 s abort timer | Hard abort the operators |
