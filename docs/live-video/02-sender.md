# 02 — Sender pipeline

The sender turns a `MediaStreamTrack` (camera or `getDisplayMedia()`) into a
realtime `RpcStream<VideoFrameBundle>` headed at the API pod. Almost all of it
runs in a dedicated Web Worker — one per recorder, so a camera and a
screencast session can run side-by-side without contending for the same
encoder pool.

## Process model

```
┌─────────────── Main thread (Blazor) ──────────────────────┐
│ VideoTrackPlayer.razor (preview), VideoRecorder.cs        │
│   getUserMedia / getDisplayMedia → MediaStreamTrack       │
│   <video srcObject=track>                                 │
└──────────────────────┬────────────────────────────────────┘
                       │ track transfer (Tier 2)
                       │ or per-frame postMessage (Tier 1)
┌──────────────────────▼─── Worker (recorderWorker.js) ─────┐
│ MediaStreamTrackProcessor.readable                        │
│   ▼                                                       │
│ Pipeline operators (described below) — built with         │
│ `pipe(...)` from `ix-ext`, drained by `drain(...)`.       │
│   ▼                                                       │
│ push-to-pull-buffer (Denque, ~1 s capacity)               │
│   ▼                                                       │
│ ILiveVideoStreams.PushStream  (Fusion RPC over WebSocket) │
└───────────────────────────────────────────────────────────┘
```

Newest-frame-wins semantics for live capture come from the **flood gate**: a
hysteresis valve placed right after capture that drops `VideoFrame`s outright
when the downstream Denque (in `streaming/push-to-pull-buffer.ts`) has filled
past `pushPullBufferSize/2` (default 30 frames ≈ 1 s); it reopens once the
queue drains below `pushPullBufferSize/4`. Skipping at capture is the cheapest
place to absorb wire stalls — the GPU pool, downscaler, and encoder never see
the dropped frame.

> Why not transfer the `MediaStreamTrack` directly? `MediaStreamTrackProcessor`
> across realms starves after ~20 frames in current Chrome; Safari only honors
> track transfer in 18+. Tier 2 (Chromium with stable cross-realm transfer)
> hands the track itself; Tier 1 (Safari) generates `VideoFrame`s on main and
> postMessages them. Both modes feed the same in-worker pipeline.

## The operator chain

`Recorder.start()` builds the pipeline in `sender/recorder.ts`. It is a chain
of async-iterable operators using `ix-ext`'s `pipe(...)` helper:

```
mstpSource                        (capture: MSTP.readable → CapturedFrame)
  ▼
floodGate(gate)                   (drop on backpressure; hysteresis driven by Denque)
  ▼
stampCaptureTime                  (MonotonicClock; epoch-flip ⇒ forceKeyframe)
  ▼
attachSourceDims                  (record pre-downscale resolution)
  ▼
downscale                         (CapturedFrame → CapturedBundle; parallelMap)
  ▼
applyKeyframePolicy               (frame counter + wallclock floor)
  ▼
encode                            (per-layer WebCodecs encoders, parallel)
  ▼  EncodedBundle
wireSend                          (build VideoStreamFrameBundle; race against abort)
  ▼  StreamSenderLike.send(bundle)
push-to-pull-buffer (Denque)       (canSkipTo=isKeyFrame compaction inside RpcStream)
  ▼
RpcStream<VideoFrameBundleDto>
```

Sources: `…/Services/Video/operators/*.ts`,
`…/Services/Video/streaming/push-to-pull-buffer.ts`.

### `mstpSource` — capture
File: `operators/capture.ts`.
Wraps the worker's `MediaStreamTrackProcessor.readable` (or a test injection).
Yields `CapturedFrame` envelopes carrying the raw `VideoFrame`, an empty
`capturedAt`, a (yet-zero) `index`, and the shared `VideoRecordingStats`
reference.

### `floodGate(gate)`
File: `operators/flood-gate.ts`.
Skips frames while the gate is `closed` (capture-side resource is released
immediately via `frame.close()`). The gate is driven from the Denque in
`push-to-pull-buffer.ts`: closes when the Denque hits half-capacity, reopens
below quarter-capacity. `gate.skipCount` feeds
`stats.floodGateSkipCount` via the wire-send sink.

### `stampCaptureTime`
File: `operators/stamp-capture-time.ts`.
Stamps `capturedAt = { timeMs, epoch }` from `MonotonicClock.now()`. When the
clock's epoch increments (sleep/wake, system clock step, Bluetooth audio
takeover), it flips `forceKeyframe = true` so the receiver's
`resetOnEpochChange` ([07](./07-receiver.md)) can re-anchor.

### `attachSourceDims`
File: `operators/attach-source-dims.ts`.
Stamps `sourceWidth/sourceHeight` from the raw frame *before* downscale. The
server uses these to track when the publisher's source resolution changes
(window resize, screen rotation) and to make resolution-aware quality choices.

### `downscale` — produces the simulcast bundle
File: `operators/downscale.ts`.

- Output type: `CapturedBundle { layers: CapturedFrame[] }` — bottom-first
  (`layers[0]` is base, last entry is top tier). All entries share the input
  frame's `capturedAt`/`index`/`forceKeyframe`/source dims; only `frame`
  differs.
- Concurrency: dispatch through `parallelMap` (`operators/parallel-map.ts`)
  with `concurrency = 2` slots by default. Each slot owns its own
  `DownscalerLike` instance, lazy-initialised on first use; ordering preserved.
- Hang watchdog: 1500 ms timeout per `process()` call. On hang it closes the
  slot's downscaler, increments `consecutiveHangs`, sets
  `forceKeyframeAfterHang`, and recreates on the next frame. After 4
  consecutive hangs the operator gives up.
- Production downscaler: **`CanvasDownscaler`** (`canvas/downscaler.ts`), a 2D
  canvas implementation that processes top-down and reuses a higher tier's
  canvas as the source for a lower tier when `longEdge(higher) ≥ 2 × longEdge(target)`
  — saves a redundant draw per source frame for typical 3-tier ladders.
- `WebGpuDownscaler` (`webgpu/downscaler.ts`) is still in the tree (and used
  in some tests/labs) but the production codepath wires `CanvasDownscaler`
  via `VideoRecorder.cs` because WebGPU pacing on iOS forced too many
  per-frame drains.
- Single-tier P2P streams default to `identityDownscaler()` in
  `Recorder.start()` — clones the input once, no resize.

### `applyKeyframePolicy`
File: `operators/apply-keyframe-policy.ts`.
Forces a keyframe on **any** of:
1. Frame counter % `keyframeIntervalFrames == 0`.
2. Wallclock — `now - lastKeyframeAtMs ≥ maxKeyframeIntervalMs`.
3. Upstream already set `forceKeyframe = true` (e.g. epoch flip, dim change,
   downscale hang recovery, recorder PLI).

Sets the flag on every layer in the bundle so all simulcast keyframes line up
on the wire.

### `encode` — per-layer encoders, parallel
File: `operators/encode.ts`. For each `CapturedBundle`:

- Lazily acquires one `PooledEncoder` per layer (see `EncoderPool` below).
- Submits all layers in parallel (`Promise.allSettled`); each call passes
  `{ keyFrame: forceKeyframe }` so all simulcast keyframes line up.
- Yields a single `EncodedBundle { layers: EncodedFrame[] }` with `layers`
  bottom-first (L0, L1, …, LN-1). Reason: the bundle wire format and the
  RPC layer's compaction step both treat L0 keyframes as restart points.
- On `CodecToAsyncAdapterResetError` (encoder timed out, see `adapters.ts`):
  marks the next bundle as a keyframe and retries.

Each `EncodedFrame` carries its `EncodedVideoChunk`,
`EncodedVideoChunkMetadata` (SPS/PPS for keyframes), the source `capturedAt`,
the `index`, the layer id, and source/encoded dimensions.

### `wireSend` — build bundle DTO, hand to push-to-pull buffer
File: `operators/wire-send.ts`.

- Output type: `VideoStreamFrameBundle { layers: VideoStreamFrame[] }` —
  one bundle per source moment, bottom-first; the wire DTO maps 1:1 to
  .NET's `VideoFrameBundle` ([04](./04-rpc-and-framing.md)).
- On the **first top-layer keyframe**, calls `sender.init(StreamFormat)` to
  send the codec, top-layer dims, source dims, and base64 description; this
  is what kicks off the RPC `PushStream` call (the format becomes the
  `VideoFormat` argument).
- For every bundle, computes a single wall-clock offset from the top layer:
  `offset = (top.capturedAt.timeMs - sourceStartMs) × 10000` (100-ns ticks),
  carries `offsetEpoch` so the receiver can detect clock discontinuities.
- Caches `description` (HVCC/SPS) per layer because HEVC keyframes after the
  first may omit it; on each subsequent dim-change keyframe the cache is
  refreshed from `metadata.decoderConfig.description`.
- Races each `send()` against the recorder's abort signal so a stalled RPC
  peer can't block `Recorder.stop()` for more than the drain grace
  (`STOP_DRAIN_GRACE_MS = 3 s`).
- Observes `sender.whenDisposed`; if the wire pump fails or completes ahead
  of the source, replaces the sender on the next bundle and resends `init`
  on the next keyframe.

### `push-to-pull-buffer` — capture↔RPC rendezvous
File: `streaming/push-to-pull-buffer.ts`.

Bridges the synchronous `wireSend.send()` push into Fusion's pull-shaped
`RpcStream`. A `Denque<VideoStreamFrameBundle>` is the queue:

- Capacity: `VIDEO.pushPullBufferSize = frameRate = 30` slots ≈ 1 s.
- Hysteresis: closes the flood gate when `length ≥ closeGateAt = capacity/2`,
  reopens when the consumer drained below `openGateAt = capacity/4 - 1`.
- `RpcStream` is constructed with `MediaRpcStreamOptions.videoRealtime`
  (`isRealTime: true`, `allowReconnect: true`,
  `bufferSize: senderBufferSize ≈ keyframePeriodSize × 4/3`,
  `canSkipTo: bundle ⇒ bundle.Layers[0].IsKeyFrame`) — see
  [04-rpc-and-framing.md](./04-rpc-and-framing.md) for the full set.
- The pump kicks off a background task that calls
  `liveVideoStreams.PushStream(RPC_SESSION_DEFAULT, chatId, sourceStartOffsetSec, formatDto, sourceKind, streamRef)`,
  then awaits `stream.whenSent`. On any failure it sets `lastError`, marks
  the sender disposed, and rejects `whenDisposed` so `wireSend` recreates the
  sender on the next bundle.
- Stats surfaced via `getStats()`: `addedFrameCount` (per-layer count summed
  across bundles), `queueDepth`, `maxQueueDepth`, `rpcStreamSkipped` (from
  `RpcStreamSender.skipCount` — frames the RPC ring compacted via
  `canSkipTo`), `floodGateSkipCount`, `lastAckAgeMs`, `isPeerConnected`.

The Denque is **not** "drop oldest"; the RpcStream's own
`canSkipTo=isKeyFrame` compaction is what reduces backlog under wire stalls
([11-buffering-and-av-sync.md](./11-buffering-and-av-sync.md) covers the two
tiers in detail).

## EncoderPool — keep the NVENC slot warm

File: `sender/encoder-pool.ts`, `sender/session.ts`.

The pool parks released encoders (instead of disposing them) for
`parkTtlMs = 5 s`. The trick is to override the encoder's `dispose` on
checkout so the operator's `finally` parks instead of kills it.

Two non-obvious rules pay off here:

1. **Codec category stays constant within a session.** `getCodecForCategory()`
   picks the highest level in the category (e.g. H.264 High 5.2) and the
   matching pool slot is keyed on category, not the exact codec string.
   Chrome re-initialises NVENC when the codec string changes; reusing a slot
   would otherwise be impossible.
2. **Bitrate-only reconfigures are in-place.** Dimension changes replace the
   encoder instance, but the pool holds the slot during the gap.

`SenderSession` owns the pool, the `MonotonicClock` used for capture timing,
and (optionally) a `MediaStreamTrackGenerator` writer for local preview. It
survives `stop()` → `start()` cycles so warm-state is reused.
`session.reset()` is called from `Recorder.restart()` to clear per-run
state without losing parked encoders.

## Local preview tap

If the main thread provides an MSTG writer via the session, the worker clones
each `CapturedFrame.frame` and writes the clone to the writer; the original
stays in the pipeline. The Blazor side has a `<video srcObject=…>` bound to
that MSTG track, so users see what they're sending. Local preview is
**pre-encode** (post-downscale would be possible but costs an extra GPU read;
the current design keeps preview on the unencoded frame). Failures are
counted in `stats.previewClonesFailed`.

## Stop and cleanup

`Recorder.stop()`:

- Aborts the source's `sourceStopController`, which lets the iterator complete.
- Schedules `STOP_DRAIN_GRACE_MS = 3 s` abort timer. If the pipeline hasn't
  drained by then, the operators are aborted hard so the worker doesn't hang
  on a dead RPC pump.
- Encoder pool keeps parked entries for 5 s in case `start()` is called again
  shortly after (e.g. switching the camera).

`Recorder.restart(config)` is the standard layer-count change path: stops,
awaits drain, calls `session.reset()`, then starts with the new config.

## Stats carried through the pipeline

`VideoRecordingStats` (`frame-envelopes.ts`) is one mutable object per run,
referenced by every envelope. Operators increment counters as frames pass:
`framesCaptured`, `framesProcessed`, `framesDroppedDimMismatch`,
`framesDroppedBackpressure`, `framesDroppedOther`, `chunksEncoded`,
`keyframesEncoded`, `bytesEncoded`, `encodeTimeMsSum/Count`,
`lastCapturedEpoch`, `wireFramesAdded`, `wireQueueDepth`, `wireMaxQueueDepth`,
`rpcStreamFramesSkipped`, `floodGateSkipCount`, `wireLastAckAgeMs`,
`isPeerConnected`, `previewClonesFailed`.

`Recorder.getStats()` (and the 1 Hz health reporter) reads this object; it is
the source for `RecorderHealthSnapshot` sent to the server (see
[08](./08-quality-control.md)).

## Worker contract

File: `sender/recorder-worker-contract.ts`. The main-thread façade
(`video-recorder.ts`) sees the worker through this RPC interface
(`actuallab-rpc`):

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

`RecorderWorkerOptions` is the wire-safe configuration: only
`structuredClone`-able fields — `chatId`, `apiUrl`, `sourceKind`,
`encoderConfigs[]`, `keyframeIntervalFrames`, `maxKeyFrameIntervalMs`. No
closures, no track references.

## Common error paths

| Symptom | Where it's caught | Recovery |
|---|---|---|
| Encoder hangs on `output()` | `AsyncVideoEncoder` timeout (3 s first frame, 1 s steady) | Reset, force keyframe, retry bundle |
| Frame dims ≠ encoder config | `dim-mismatch-guard.ts` | Drop frame; counter tick |
| Downscaler hangs (`process()`) | `downscale.ts` 1.5 s watchdog | Close + recreate, force keyframe; bail after 4 consecutive |
| HW NVENC slot lost | next `acquire()` on pool | Pool recreates encoder, runs through `handleEncoderReset` |
| RPC pump fails mid-stream | `wireSend` observes `whenDisposed` | Replace sender; fresh `init` on next keyframe |
| `Recorder.stop()` drain hangs | 3 s abort timer | Hard abort the operators |
| Wire stall ramps queue | flood gate closes at `pushPullBufferSize/2` | Capture-side skips until queue drains below `/4` |
