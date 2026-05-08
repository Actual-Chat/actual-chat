# 07 — Receiver pipeline

The receiver mirrors the sender: a Web Worker pulls a server-filtered
`RpcStream<VideoFrame>`, decodes it with WebCodecs, and presents either via
`MediaStreamTrackGenerator` (MSTG → `<video>`) or by drawing onto an
`OffscreenCanvas`. The Razor entry is `VideoTrackPlayer.razor` (used by
`RemoteStreamPlayer.razor`).

## Process model

```
┌── Main thread (Blazor) ─────────────────────────────────┐
│ VideoTrackPlayer.razor  ──[create()]──▶ VideoPlayer (TS) │
│                                              │            │
│   <canvas class="remote-video">              │            │
│   <video  class="live-stream-video">         │ start()    │
│   <canvas class="remote-video-bg">           ▼            │
│                                         playerWorker     │
└──────────────────────────────────────────────┬───────────┘
                                               │ postMessage
┌──────────────────────────────── Worker ─────▼───────────┐
│ playback/session.ts (one PlaybackSession per worker)    │
│  ├ DecoderPool   (codec-keyed, parkTtlMs=30s)          │
│  ├ MonotonicClock arrivalClock                         │
│  └ stats (VideoPlaybackStats)                          │
│                                                         │
│ playback/player.ts (one Player per stream)              │
│  pull → resetOnEpochChange → pacedEncodedBuffer →       │
│  decode → latencyTap → presentMstg | presentCanvas      │
└─────────────────────────────────────────────────────────┘
```

The session is a singleton inside the worker, persistent across stream
start/stop cycles. The decoder pool's parked decoders survive too — switching
between two streams with the same codec is essentially free.

## How playback starts

`VideoTrackPlayer.razor` (`OnAfterRenderAsync`):

1. `RegisterMember(session, chatId, supportedDecoderCodecs)` — declares this
   client's decoder support to the server's chat state. Heartbeat every 30 s.
2. `serverTimeSync.EnsureSynced()` — so the receiver's `MonotonicClock` lines
   up with the sender's offset domain.
3. `JS.InvokeAsync("VideoPlayer.create", ...)` — main-thread façade.
4. `player.startPull(streamId, skipToMs)` — kicks off subscription.
5. Background `PlayAsync()` waits for end and surfaces stats.

`VideoPlayer.startPull`:

- Builds the render backend (MSTG vs. canvas) — see below.
- Calls `playerWorker.start({ streamId, initialDecoderConfig, targetBufferSpanMs, backend }, mstgWritable?, offscreenCanvas?)`.
- The worker creates a `Player` and runs the pipeline in the background.

## Worker pipeline

File chain (operators in `Services/Video/operators/`):

### `pullSource` — `VideoFrameDto` → `ArrivedChunk`

File: `operators/pull.ts`. Calls
`streamingApi.liveVideoStreams.GetStream(RPC_SESSION_DEFAULT, streamId)` and
iterates the resulting `AsyncIterable<VideoFrameDto>`. For each DTO:

- Stamps `arrivedAt = arrivalClock.now()` (receiver's `MonotonicClock`).
- Converts `Offset` (TimeSpan ticks) → microseconds for
  `EncodedVideoChunk.timestamp`.
- Builds `EncodedVideoChunk` with type `key`/`delta` from `IsKeyFrame`.
- Wraps in `ArrivedChunk { chunk, capturedAt, arrivedAt, isKeyFrame, layerId, stats }`.
- Increments `stats.chunksArrived` and `stats.bytesReceived`.

`exclusive` + `finalize` wrappers ensure exactly one iteration and that
`return()` is propagated upstream on stop.

### `resetOnEpochChange`

File: `operators/epoch-reset.ts`. Watches `chunk.capturedAt.epoch`. When it
changes (sender clock discontinuity, sender restart), calls `buffer.reset()`
on the encoded-frame buffer below — so the buffer goes back to its `reset`
state and waits for the next keyframe before producing anything.

### `pacedEncodedBuffer` — jitter buffer + pacing

File: `operators/encoded-buffer.ts`, with the actual buffer in
`playback/encoded-frame-buffer.ts`. The buffer is a small two-state machine:

```
state = reset          state = armed
        │                       │
keyframe arrives ─────▶ enqueue, switch to armed
delta in reset    ─── drop (chunksDroppedAtBuffer++)
any chunk in armed── enqueue
```

`tryPull` releases a chunk only if all three hold:

1. At least 2 chunks queued.
2. Span between front and back ≥ `targetSpanMs` (default
   `TARGET_BUFFER_SPAN_MS = 333 ms` — matches server retention).
3. The front chunk's wall-clock "due time" arrived: `now ≥ arrivedAt.timeMs +
   targetSpanMs`.

The pacing operator races `iterator.next()` against `setTimeout(5 ms)` so it
checks for due chunks frequently without busy-waiting. Net behaviour: the
buffer absorbs ~333 ms of network jitter; under steady arrivals chunks are
released at roughly the same rate they came in, just delayed by the buffer
span.

### `decode` — WebCodecs

File: `operators/decode.ts`. Uses `decoderPool.acquire(codec)` from the
session.

- Initial config from `PlayerWorkerOptions.initialDecoderConfig` (codec, dims).
- On every keyframe with a `description`, caches it per-`layerId`. (HEVC may
  omit description on later keyframes; H.264 carries SPS/PPS in-band.)
- Reconfigures on dim changes detected from the produced frames.
- On decode error: buffers the error, recovers on next keyframe by closing
  and re-creating the decoder via the pool. After
  `maxRecoveries = 4` consecutive errors it fires `onCodecExhausted` and the
  player surfaces an error to main-thread, which can `excludeDecoderCodec(category)`
  and re-register.
- Tracks pending decodes in a small FIFO so `onFrame` callbacks pair up with
  the right `arrivedAt`/`capturedAt` for downstream latency reporting.

### `latencyTap`

File: `operators/latency-tap.ts`. Sampled every 1 s (driven by frame arrival,
not `setInterval` — a stalled stream produces no spurious "latency = ∞"
reports). Builds a `LatencySample`:

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
into the `VideoQualityUI` controller (08) as part of `PlaybackHealthSnapshot`.

### Present — MSTG or canvas

Two sinks, picked by `pickRenderBackend` based on platform/preferences (query
param `?renderBackend=mstg|canvas` overrides):

- **`mstgPresent`** (`operators/present-mstg.ts`): writes decoded
  `VideoFrame`s into a `MediaStreamTrackGenerator` writable. Single-slot
  replace — a fresher decoded frame replaces a still-pending older one.
  Counts only enqueued frames as `framesPresented`.
  Track is attached to a `<video>` element on main thread → platform
  hardware-decoded display. Preferred path on Chrome/Safari.
- **`canvasPresent`** (`operators/present-canvas.ts`): `drawImage(frame, …)`
  onto an `OffscreenCanvas` transferred from main. Resizes the canvas to the
  current frame dims to avoid upscaling. Safari needs a `convertToBitmap`
  intermediate (WebKit can't `drawImage(VideoFrame)` directly).

There's a third path — **worker-side MSTG** (`OffThreadRenderBackend`). On
platforms where the main-thread MSTG path doesn't work (some Safari versions),
the worker creates the MSTG, fires `onTrackReady(streamId, 'mstg', track)`,
and the main thread attaches the received track to the `<video>`.

The MSTG render backend on the main thread also runs a watchdog that retries
`<video>.play()` on stalls and falls back to canvas if `<video>` is stuck for
a configurable period.

## Player lifecycle

File: `playback/player.ts`.

- `start(config)` constructs the pipeline and runs it in the background.
- `whenDone()` resolves when the pipeline finishes (sender ended, error, or
  `stop()`).
- `stop()` aborts the pull source; gives the pipeline 1 s
  (`STOP_DRAIN_GRACE_MS`) to drain, then hard-aborts.
- The worker's `PlayerWorker` impl tracks `locallyStopped` to suppress an
  `onStreamEnded` callback when a fallback (e.g. MSTG → canvas) restarts the
  pipeline, so the UI doesn't think the stream actually ended.

After completion the worker decrements `session.activeStreams` and removes the
player from its registry, but the `PlaybackSession` keeps the decoder pool warm.

## DecoderPool

File: `playback/decoder-pool.ts`.

Codec-keyed: one parked slot per distinct codec string. `acquire(codec)`:

- Returns the parked slot if codecs match.
- Otherwise evicts mismatched parked slots, calls the factory.

`release()` parks the decoder under its codec key with a wallclock stamp.
`sweep()` runs periodically and closes anything idle longer than
`parkTtlMs = 30 s`. `dispose()` closes all parked decoders and
neutralises outstanding leases.

This lets a viewer who frequently pauses/resumes a stream avoid repeated
codec init (which on hardware is many milliseconds and on Chrome can cause
visible glitches).

## Codec selection on the receiver

`VideoPlayer.initPlayerWorker` uses
`getCodecCandidates(codec, description)` (file: `hevc-codec-selection.ts`) to
turn the publisher's codec string into a list of fallback strings to try in
order. `selectDecoderCodec(candidates, description, dims)` then probes them
via `VideoDecoder.isConfigSupported()` and picks the first match. If the
worker reports a hard decode error, the main thread maps
`getCodecCategory(codecString)`, calls `excludeDecoderCodec(category)`
(localStorage-backed), and the next `RegisterMember` reports the smaller list.

## Quality feedback collected here

Two reports are produced from receiver state and sent to the server (08 has
the full picture):

1. **`PlaybackQualityInfo` (every ~2 s + 5 s heartbeat)** — bandwidth
   capacity estimate, aggregate health, decoder queue depth EMA, keyframe
   skips in window, etc. Computed from worker stats.
2. **Per-stream `ReceiveQuality`** — the desired `MaxLayerId` and
   `MaxTemporalLayerId` for each stream the viewer is watching. Sent in the
   same `ChangePlaybackQuality` call.

Render-size hint (from `ResizeObserver` on the canvas/`<video>`) is also part
of `PlaybackQualityInfo`. `cssLongSide` and `devicePixelRatio` let the server
pick a layer near the viewport size — there's no point sending a 720p layer to
a 200-px tile.

## Multiple concurrent streams

A worker can play many streams at once. They share:

- The `PlaybackSession` (clock, decoder pool, stats counters).
- One `RpcPeer` to the API pod (Fusion RPC over a single WebSocket).

Per-stream they each have their own `Player`, `EncodedFrameBuffer`, render
surface, and (typically) decoder lease.

`Constants.Video.TargetBufferSize = 10` and the per-stream RPC `BufferSize`
mean each stream is bounded ≤ ~333 ms of frames in flight. With N streams
that scales linearly; the AIMD playback controller (08) is what keeps the
total under the receiver's measured capacity.
