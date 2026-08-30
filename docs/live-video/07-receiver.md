# 07 — Receiver pipeline

The receiver mirrors the sender: a Web Worker pulls a server-filtered
`RpcStream<VideoFrame>`, decodes it with WebCodecs, and presents either via
`MediaStreamTrackGenerator` (MSTG → `<video>`) or by drawing onto an
`OffscreenCanvas`. The Razor entry is `VideoTrackPlayer.razor` (used by the
remote-stream player components).

## Process model

```
┌── Main thread (Blazor) ─────────────────────────────────┐
│ VideoTrackPlayer.razor  ──[create()]──▶ VideoPlayer (TS) │
│   <canvas class="remote-video">                          │
│   <video  class="live-stream-video">  (MSTG sink)        │
│                              │ start()                   │
│                              ▼                           │
│                         playerWorker                     │
└──────────────────────────────┬──────────────────────────┘
                                │ postMessage
┌──────────────────────────────▼─── Worker ───────────────┐
│ playback/session.ts (one PlaybackSession per worker)    │
│  ├ MonotonicClock arrivalClock                          │
│  ├ DecoderPool   (codec-keyed, parkTtlMs ≈ 30 s)        │
│  └ stats (VideoPlaybackStats, session-level)            │
│                                                         │
│ playback/player.ts (one Player per stream)              │
│  pull → resetOnEpochChange → pacedEncodedBuffer →       │
│  decode → latencyTap → mstgPresent | canvasPresent      │
└─────────────────────────────────────────────────────────┘
```

The session is a singleton inside the worker, persistent across stream
start/stop cycles. The decoder pool's parked decoders survive too — switching
between two streams with the same codec is essentially free.

## How playback starts

`VideoTrackPlayer.razor`:

1. `RegisterMember(session, chatId, supportedDecoderCodecs)` — declares this
   client's decoder support to the server's chat state. Heartbeat every 30 s.
2. `serverTimeSync.EnsureSynced()` — so the receiver's `MonotonicClock` lines
   up with the sender's offset domain.
3. `JS.InvokeAsync("VideoPlayer.create", ...)` — main-thread façade.
4. `player.startPull(streamId, skipToMs)` — kicks off subscription.

`VideoPlayer.startPull`:

- Builds the render backend (MSTG vs. canvas) — see below.
- Calls
  `playerWorker.start({ streamId, initialDecoderConfig, targetBufferSpanMs, backend }, mstgWritable?, offscreenCanvas?)`.
- The worker creates a `Player` and runs the pipeline in the background.
- A render-size hint (`OnPlaybackViewportChanged`) flows back into
  `VideoQualityUI` whenever the player tile is resized, driving the
  allocator's per-stream `RenderVideoSize` ([08](./08-quality-control.md)).

## Worker pipeline

File chain (operators in `Services/Video/operators/`):

### `pullSource` — `VideoFrameDto` → `ArrivedChunk`

File: `operators/pull.ts`. Calls
`streamingApi.liveVideoStreams.GetStream(RPC_SESSION_DEFAULT, streamId)` and
iterates the resulting `AsyncIterable<VideoFrameDto>`. For each DTO:

- Stamps `arrivedAt = arrivalClock.now()` (receiver's `MonotonicClock`).
- Parses `Offset` (TimeSpan ticks, possibly bigint) → `capturedAt.timeMs`,
  with `OffsetEpoch` carried through.
- Builds an `EncodedVideoChunk` with type `key`/`delta` from `IsKeyFrame`,
  copying `Description` into a standalone `ArrayBuffer` (MessagePack may
  hand back a shared-buffer view).
- Wraps in `ArrivedChunk { chunk, arrivedAt, capturedAt, isKeyFrame,
  description?, layerId, width, height, rawByteLength, stats }`.
- Increments `stats.chunksArrived` and `stats.bytesReceived`.

`exclusive` + `finalize` wrappers ensure exactly one iteration and that
`return()` is propagated upstream on stop, so the in-flight RPC
subscription unwinds on dispose.

### `resetOnEpochChange`

File: `operators/epoch-reset.ts`. Watches `chunk.capturedAt.epoch`. When it
changes (sender clock discontinuity, sender restart), calls `buffer.reset()`
on the encoded-frame buffer below — so the buffer goes back to its `reset`
state and waits for the next keyframe before producing anything.

### `pacedEncodedBuffer` — span-gated jitter buffer

File: `operators/encoded-buffer.ts`, with the actual buffer in
`playback/encoded-frame-buffer.ts`. The buffer is a small two-state machine:

```
state = reset          state = armed
        │                       │
keyframe arrives ─────▶ enqueue, switch to armed
delta in reset    ─── drop (chunksDroppedAtBuffer++)
any chunk in armed── enqueue
```

`tryPull()` releases a chunk only if **both** hold:

1. The buffer is `armed` and non-empty.
2. `spanMs() ≥ targetSpanMs` (default `Constants.Video.TargetBufferSpanMs ≈ 333 ms`).

`spanMs()` returns `(last.capturedAt.timeMs - first.capturedAt.timeMs) +
frameDurationMs` — capture-time-anchored, so it tracks source pacing rather
than wallclock arrivals. The trailing chunk's real duration isn't known until
the next chunk arrives, so it's approximated as one nominal frame
(33.333 ms by default).

Span-gating self-corrects on every push/pull: if jitter momentarily fills
the buffer, the next arrivals advance `last` and `tryPull` keeps releasing;
if the source pauses, `spanMs` shrinks below target and pulls stop. The
`pacedEncodedBuffer` operator races `iterator.next()` against the abort
signal; on shutdown it disposes the buffer and any in-flight upstream
iteration.

`detectRegression(chunk, toleranceMs)` is a stateless helper for callers
that want to detect out-of-order arrivals across the same epoch. The buffer
itself does not reject regressing chunks.

### `decode` — WebCodecs

File: `operators/decode.ts`. Uses `decoderPool.acquire(codec)` from the
session.

- Initial config from `PlayerWorkerOptions.initialDecoderConfig` (codec,
  dims).
- On every keyframe with a `description`, caches it per-`layerId`. (HEVC may
  omit description on later keyframes; H.264 carries SPS/PPS in-band.)
- Reconfigures on dim changes detected from the produced frames.
- **Hang watchdog**: `decoderHangTimeoutMs = 2000 ms`. Arms a race when the
  decoder has pending submitted chunks but hasn't produced a frame or
  error. On timeout, synthesizes an error which drives the standard
  recovery path (close + recreate + reconfigure on next keyframe).
- **Error recovery**: closes and rebuilds the decoder via the pool, drops
  pre-keyframe deltas during recovery. After `maxRecoveries = 4`
  consecutive errors it fires `onCodecExhausted` and the player surfaces
  the error to main-thread, which can `excludeDecoderCodec(codec)` and
  re-register.
- Tracks pending decodes in a small FIFO so `onFrame` callbacks pair up
  with the right `arrivedAt`/`capturedAt` for downstream latency reporting.

### `latencyTap`

File: `operators/latency-tap.ts`. Sampled at `LatencyReportInterval = 500 ms`
(driven by frame arrival, not `setInterval` — a stalled stream produces no
spurious "latency = ∞" reports). Builds a `LatencySample`:

```ts
{
  frameAgeMs: now - decodedAt.timeMs,            // receiver-domain
  e2eLatencyMs: now - capturedAt.timeMs,         // cross-clock approx
  capturedEpoch, layerId,
  bytesReceived,                                 // running total
  bufferSpanMs                                   // filled in by Player
}
```

Pushed to main via `onLatencyReport(streamId, sample)`. Main thread feeds it
into `VideoQualityUI` ([08](./08-quality-control.md)) as part of
`PlaybackHealthSnapshot`.

### Present — MSTG or canvas

Two sinks, picked by `pickRenderBackend(...)` (in
`playback/render-backends.ts`) using `preferMstg`, the supplied writer/canvas,
and a `convertToBitmap` shim where Safari needs it:

- **`mstgPresent`** (`operators/present-mstg.ts`): writes decoded
  `VideoFrame`s into a `MediaStreamTrackGenerator` writable. Pacing is
  capture-time-delta driven:
  - `MAX_FPS = 120`, `MIN_FPS = 10` ⇒ `MIN_DURATION_MS ≈ 8.3 ms`,
    `MAX_DURATION_MS = 100 ms`.
  - `extraMs = max(0, bufferSpan - targetSpan)`.
  - **Skip mode**: if `extraMs > CATCHUP_BUDGET_MS = 4000` AND the next
    write would land within `MIN_DURATION_MS` of the previous one, drop
    the frame. `framesDroppedAtPresenter++`. Used when the buffer is so
    far over target that the MAX_FPS catch-up alone can't drain it.
  - **Catch-up**: if `extraMs > 0`, force `durationMs = MIN_DURATION_MS`
    (present at MAX_FPS).
  - **Steady**: else, `durationMs = clamp(natural source delta, MIN, MAX)`
    — schedule advances by exactly the source delta so it tracks capture
    time without anchor drift.
  - On write success: `framesPresented++`. On write failure:
    `framesDroppedAtPresenter++` (the writer raised; pipeline rethrows).
- **`canvasPresent`** (`operators/present-canvas.ts`): `drawImage(frame, …)`
  onto an `OffscreenCanvas` transferred from main. Resizes the canvas to
  the current frame dims to avoid upscaling. Safari needs a
  `convertToBitmap` intermediate (WebKit can't `drawImage(VideoFrame)`
  directly).

The MSTG render backend on the main thread runs a watchdog that retries
`<video>.play()` on stalls and falls back to canvas if `<video>` is stuck
for a configurable period (`render-backend-mstg.ts`).

### Rotation-aware presentation

The sender stamps a quantized device-orientation rotation on each wire frame
(the encoded pixels stay sensor-oriented). The receiver applies it on decode —
`VideoFrame.rotation` on Chromium, a worker VTG wrap where that isn't
available — so the picture is always upright. `video-player.ts` then makes a
**cover-vs-contain** fit decision against the tile: it uses cover until it
would crop more than `COVER_LOSS_MAX` (20%) of source pixels, then switches to
contain and paints a **blurred backdrop** on a second (background) canvas to
fill the letterbox (`applyFitDecision`; backdrop can be disabled). Fit is
recomputed on tile resize from the last post-rotation frame dims.

## Player lifecycle

File: `playback/player.ts`.

- `start(config)` constructs the pipeline and runs it in the background;
  rejects if a run is already in flight.
- `whenDone()` resolves when the pipeline finishes (sender ended, error, or
  `stop()`).
- `stop()` aborts the pull source; gives the pipeline `STOP_DRAIN_GRACE_MS = 3 s`
  to drain, then hard-aborts.
- `resetBuffer()` calls `buffer.reset()` — out-of-band sender resync hook,
  rarely used.
- The worker's `PlayerWorker` impl tracks `locallyStopped` to suppress an
  `onStreamEnded` callback when a fallback (e.g. MSTG → canvas) restarts
  the pipeline, so the UI doesn't think the stream actually ended.

After completion the worker decrements `session.activeStreams` and removes
the player from its registry, but the `PlaybackSession` keeps the decoder
pool warm.

## DecoderPool

File: `playback/decoder-pool.ts`.

Codec-keyed: one parked slot per distinct codec string. `acquire(codec)`:

- Returns the parked slot if codecs match.
- Otherwise evicts mismatched parked slots, calls the factory.

`release()` parks the decoder under its codec key with a wallclock stamp.
A periodic sweep closes anything idle longer than `parkTtlMs ≈ 30 s`.
`dispose()` closes all parked decoders and neutralises outstanding leases.

This lets a viewer who frequently pauses/resumes a stream avoid repeated
codec init (which on hardware is many milliseconds and on Chrome can cause
visible glitches).

## Codec selection on the receiver

`VideoPlayer.initPlayerWorker` uses
`getCodecCandidates(codec, description)` (file: `hevc-codec-selection.ts`)
to turn the publisher's codec string into a list of fallback strings to
try in order. `selectDecoderCodec(candidates, description, dims)` then
probes them via `VideoDecoder.isConfigSupported()` and picks the first
match — every candidate against `prefer-hardware` first, then the whole
list again against `no-preference`, since Firefox routinely rejects the
former for a codec it decodes in software. The chosen acceleration is
threaded into `initialDecoderConfig`, not re-assumed.

H.264 candidates only ever **widen**: the declared string, the same profile
at L5.2, then High at L5.2. A decoder configured above the bitstream decodes
it; configured below, `configure()` succeeds and `decode()` drops chunks
silently, so no lower-profile candidate is offered.

If the worker reports a hard decode error, the main thread maps
`getCodecCategory(codecString)`, calls `excludeDecoderCodec(codecString)`
(localStorage-backed), and the next `RegisterMember` reports the smaller
list.

## Quality feedback collected here

Two reports are produced from receiver state and sent to the server (see
[08](./08-quality-control.md) for full picture):

1. **`PlaybackQualityInfo` (every ~2 s + 1 min keep-alive)** — bandwidth
   capacity estimate, aggregate health, decoder queue depth EMA, keyframe
   skips in window, render-size hints, etc. Computed from worker stats and
   per-stream `LatencySample`s.
2. **Per-stream `ReceiveQuality`** — the desired `MaxLayerId` and
   `MaxTemporalLayerId` for each stream the viewer is watching. Sent in
   the same `ChangePlaybackQuality` call.

`RenderVideoSize` (per-stream) and `cssLongSide` × `devicePixelRatio` let
the server-side allocator pick a layer near the viewport size — no point
sending a 720p layer to a 200-px tile.

## Multiple concurrent streams

A worker can play many streams at once. They share:

- The `PlaybackSession` (clock, decoder pool, stats counters).
- One `RpcPeer` to the API pod (Fusion RPC over a single WebSocket).

Per-stream they each have their own `Player`, `EncodedFrameBuffer`, render
surface, and (typically) decoder lease.

`Constants.Video.TargetBufferSize = 10` and the per-stream consumer-leg
`AckAdvance = 16` bound each stream to ~333 ms of frames in flight. With N
streams that scales linearly; the AIMD playback controller
([08](./08-quality-control.md)) is what keeps the total under the receiver's
measured capacity.
