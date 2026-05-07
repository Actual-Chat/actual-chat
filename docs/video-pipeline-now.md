# Video Pipeline — Current State vs. Target

This document maps every conceptual stage from `docs/video-pipeline.md` to the
matching piece (or pieces) of the current implementation, briefly describes how
it works today, and calls out the major differences from the target design. The
control plane gets its own dedicated section at the end because it is the part
that diverges most.

All file paths are relative to the repo root.

---

## 1. `raw video source`

**Now:**
- `src/dotnet/UI.Blazor.App/Services/Video/services/media-capture.ts` — wraps
  `navigator.mediaDevices.getUserMedia()` / `getDisplayMedia()` and exposes a
  `MediaStreamTrack`.
- `src/dotnet/UI.Blazor.App/Services/Video/services/recording-service.ts` —
  manages acquisition lifecycle (`start()`, `stop()`, codec/device selection,
  blur toggle, codec switch, `RecordingConfig` shape).
- `src/dotnet/UI.Blazor.App/Services/Video/services/video-pipeline.ts` —
  orchestrator on the main thread; transfers the camera `ReadableStream` (from
  `MediaStreamTrackProcessor`, with a `<canvas>+rAF` fallback for Safari) into
  the shared `videoProcessingWorker` via `postMessage`/`transfer`.

**vs. doc:**
- The doc treats this stage as "no buffering, immediately pass to the next
  component". Current code matches — the worker reads from the transferred
  `ReadableStream` directly, no intermediate queueing on the main thread.
- One wrinkle: the **shared worker is reference-counted with a 60 s idle
  terminate timer** to keep an HW NVENC session warm across stop/start. That
  is a sender-side optimisation; it has no analogue in the doc but is
  invisible to the rest of the pipeline.

## 2. `raw video processors`

**Now:** all inside `src/dotnet/UI.Blazor.App/Services/Video/workers/video-processing.ts`
(2 054 lines, the big one). Stages that run before encode:
- **Frame queue** — when blur/segmentation is enabled, `Denque<QueuedFrame>`
  holds raw `VideoFrame`s pulled from the `MediaStreamTrackProcessor` reader.
  `processingFrame` flag serialises one at a time; backpressure is measured as
  a 5-second drop ratio (`backpressureDropThreshold = 0.20`). Without blur,
  frames bypass this queue and go straight into the encode/downscale path.
- **Segmentation** — ONNX selfie-segmentation model (`selfie_segmentation_olive_webgpu.onnx`)
  via `onnxruntime-web`. The config type still names `webgpu/webgl/wasm`, but
  the current worker initialization hard-codes WebGPU execution; WebGL/WASM are
  detected/configured by surrounding code but are not actually selected in the
  worker's ONNX session today.
  > **Disabled in the current release.** `onnxruntime-web` and the
  > segmentation function bodies in `video-processing.ts` /
  > `tensor-utils.ts` are commented out because segmentation is not yet
  > wired into any UI flow and the runtime is heavy on low-end mobiles.
  > `initializeSegmentation` is a no-op; `segInitialized` stays `false`,
  > so all gated call sites bypass the queue. Re-enable by uncommenting
  > the imports and function bodies.
- **Background blur** — `src/dotnet/UI.Blazor.App/Services/Video/webgpu-blur.ts`,
  GPU mipmapped Gaussian pyramids + temporal mask EMA.
- **Downscale** — `src/dotnet/UI.Blazor.App/Services/Video/webgpu-downscaler.ts`
  produces base + simulcast layers from a single source frame.
- **YUV conversion** — `src/dotnet/UI.Blazor.App/Services/Video/webgpu-yuv-converter.ts`
  exists and is used by the GPU blur path (`submitBlurI420`) to produce I420,
  but the normal WebGPU downscaler path generally feeds GPU-resident RGBA
  frames to the hardware encoder. `preConvertYuv` is optional and currently
  skipped when the WebGPU downscaler is active to avoid GPU→CPU→GPU round-trips.
- **Rotation reconcile** — first-frame check that adjusts `senderRotationDeg` to
  match what the platform actually delivers.

**vs. doc:**
- The doc prescribes a **single replaceable slot** between capture and the
  next stage. Current code has two behaviours: no-blur frames bypass the
  segmentation queue, while blur frames use a bounded Denque (`maxQueueSize`
  5 by default, 3 on mobile) with internal drop accounting. In practice, when
  GPU is healthy the queue stays near-empty, but the structure is not a 1-slot
  replaceable handoff.
- Backpressure is metric-driven (5 s window, 20 % drop ratio surfaces a
  callback). The doc's contract is structural ("newer frame replaces pending
  frame").

## 3. `video encoder`

**Now:**
- `src/dotnet/UI.Blazor.App/Services/Video/webcodecs-encoder.ts` — `WebCodecsEncoder`
  wrapper around `VideoEncoder`. Supports H.264 / HEVC / AV1 / VP9.
- In `video-processing.ts`: a **base encoder** (`encoder`, `SpatialLayerId=0`)
  plus an array `extraLayerEncoders[]` for simulcast layers when
  `VideoProcessingConfig.spatialLayers` is non-empty. Encoders are pooled
  (`POOL_TTL_MS = 30 s`) so an HW slot survives across short stop/start.
- Per-frame work: rotate → optional blur → downscale to N targets → optionally
  convert to I420 on the blur/`preConvertYuv` paths → feed each
  `WebCodecsEncoder.encode()`. `nextFrameIsKeyFrame` flag forces an IDR when
  the control plane requests one.
- `src/dotnet/UI.Blazor.App/Services/Video/codec-support.ts` — runtime probe
  for HW acceleration (priority: AV1 HW > H.264 HW High > H.264 HW > H.264 SW).
- `src/dotnet/UI.Blazor.App/Services/Video/hevc-parser.ts` — parses HVCC
  descriptor manually (WebCodecs returns SPS/PPS without packaging).

**vs. doc:**
- Doc: **one replaceable slot**, drop or replace if encoder slow. Current code
  relies on WebCodecs `encodeQueueSize` checks (`> 3`, or `> 1` on iOS) and
  simply drops new raw frames when the encoder is backed up. If blur is active,
  the upstream segmentation Denque can also absorb/drop a few frames. There is
  no explicit "replace pending frame on arrival" semantic.
- Simulcast / spatial-layer multiplexing already exists and is more elaborate
  than the doc explicitly calls for (the doc calls out simulcast in passing).

## 4. `video sender`

**Now:**
- `src/dotnet/UI.Blazor.App/Services/Video/workers/video-streaming.ts` →
  `class InternalVideoStream`.
- Encoded chunks are pushed into a `Denque<VideoStreamFrame>` with
  `MAX_QUEUE_LENGTH = 60` (about 2 s @ 30 fps).
- On overflow it **drops the oldest non-keyframe**; if only keyframes remain
  it drops the oldest one and fires `onNeedKeyframe?` so the encoder emits a
  fresh IDR.
- The queue is drained by an async generator handed to
  `new RpcStream<VideoFrameDto>(gen, { isRealTime: true, allowReconnect: true,
  ackPeriod: 5, bufferSize: 31, canSkipTo: f => f.IsKeyFrame })`.
- The stream is sent via Fusion RPC: `streamServer.PushVideo(session, chatId,
  clientStartOffset, format, stream.toRef(peer), streamKind)`. `RPC_SESSION_DEFAULT
  = '~'` resolves to the WS connection's session.

**vs. doc:**
- Direction matches: real-time `RpcStream` with `canSkipTo = isKeyFrame`.
- **Numbers differ.** Doc target: `BufferSize = 10`, `AckPeriod = 5`. Current:
  `bufferSize: 31`, `ackPeriod: 5` for the RpcStream itself, **plus** a
  separate 60-entry producer-side Denque on top — the doc's ownership model
  says the RPC stream alone should be the only thing holding unsent encoded
  frames.
- Drop policy: doc wants ACK-driven compaction to the latest decoder-safe
  frame. Current code does **eager pre-emptive** drops at the producer
  Denque before the RPC layer ever sees them.

## 5. `server video receiver`

**Now:**
- `src/dotnet/Streaming.Service/Services/StreamServer.cs:108` — `PushVideo()`
  parses the `VideoRecord`, mints a `StreamId` on the local mesh node, wraps
  the inbound `RpcStream<VideoFrame>` with `RpcStream.New(frameStream)` and
  forwards to the backend.
- `src/dotnet/Streaming.Service/Backend/VideoStreamingBackend.cs:76` →
  `PushVideo()` → `PushVideoInternal()`. Validates `StreamId` belongs to this
  node, checks `Chats.GetRules(...)` permissions, ensures author membership.
- `ProcessFrames` (inner async iterator inside `PushVideoInternal`):
  - Frame-silence watchdog (10 s webcam, 3 min screencast).
  - Stamps each frame with a per-spatial-layer `KeyFrameNumber` so downstream
    gap-detection on the selected layer is correct under simulcast.
  - On keyframe, calls `LatencyStore.UpdateMaxQuality(streamId, sourceWidth,
    sourceHeight)` to track mid-stream source-resolution growth.
  - `LatencyStore.RecordFrameBytes(streamId, byteLen, spatialLayerId)` for
    throughput budgeting.
  - Heartbeats `LiveVideoBackend.Register(...)` every 2.5 min so the
    cross-shard chat state stays alive.

**vs. doc:**
- Doc: "no buffering, validate & forward". Current code is largely that,
  **plus** it does several control-plane side-effects on the data path
  (UpdateMaxQuality, RecordFrameBytes, heartbeat). The doc would push these
  to the control plane explicitly.

## 6. `server stream store`

**Now:**
- `src/dotnet/Streaming.Service/Services/StreamStore.cs` —
  `StreamStore<VideoFrame>`. Memoizes the inbound stream via
  `AsyncMemoizer.Memoize(RetentionBufferSize=180, ct)` so multiple receivers
  can replay it.
- Configured in `VideoStreamingBackend` ctor:
  `ExpirationDelay = 30 s`, `ReplayTailSize = 30` (= `Constants.Video.ReplayBufferSize`,
  ~1 s @ 30 fps).
- The **memoizer's retention window is 180 frames** (~6 s for single-layer,
  ~2 s effective for 3-layer simulcast). That is the upper bound on how far
  back a late joiner / reconnect can read.
- The per-consumer **replay channel** size is 30 (the `Replay()` call applies
  a `ReplayTailSize`).

**vs. doc:**
- Doc: `ServerReplayTailSize = 30` (~1 s), drop-oldest **keyframe span**.
- Current: replay channel is 30 frames (matches), but the underlying
  retention is 180 frames (much larger), and eviction is **frame-count
  FIFO**, not "drop oldest keyframe span". A delta whose anchor keyframe has
  already been evicted can still be in retention and is not proactively
  cleaned up — `VideoStreamFilter` papers over this by skipping until the
  next keyframe.

## 7. `server video sender`

**Now:**
- `src/dotnet/Streaming.Service/Backend/VideoStreamingBackend.cs:43` →
  `GetVideo(streamId, skipTo, peerId, ct)`.
- Wraps the memoizer in `new VideoStreamFilter(...).Apply(streamId, peerId,
  skipTo, source, ct)` and returns it as `RpcStream<VideoFrame>` with
  `AckPeriod = Constants.Video.RpcStreamAckPeriod` (5) and
  `BufferSize = Constants.Video.RpcStreamBufferSize` (10).
- `VideoStreamFilter` (`src/dotnet/Streaming.Service/Services/VideoStreamFilter.cs`)
  is the per-consumer fan-out filter. It does **all** of:
  1. **Quality preset refresh** — background task subscribes to
     `Computed<VideoQualityPreset>` and tracks current preset (drives `Paused`
     filtering).
  2. **Layer-cap refresh** — re-reads `latencyStore.GetPeerMax{Spatial,Temporal}Layer`
     once per second.
  3. **Egress back-pressure detection** — if consumer hasn't pulled within
     `EgressStallThreshold = 500 ms`, marks `skipping = true` (force a
     keyframe-anchored resume). If hidden tab, suppresses skip.
  4. **Spatial layer selection** — `joinPendingKF` holds at most one
     keyframe for up to 50 ms (or until the first delta arrives) so the
     receiver decoder configures with the highest layer the cap permits,
     not the first one seen. **This is the doc's `replaceable slot` at
     this stage** — capacity 1, newest higher-layer keyframe replaces the
     pending one until the burst stabilises.
  5. **Burst stabilisation + decay** — if observed top spatial layer hasn't
     produced a keyframe within `SpatialStalenessWindow = 6 s`, demotes only
     after a 1.5 s confirmation window so an out-of-order base keyframe
     can't trigger a false demotion.
  6. **Egress fallback** — after `EgressGapFrameThreshold = 150` consecutive
     skipped delta frames on the selected layer, calls
     `latencyStore.DecrementPeerEgressFallback(...)` to drop the cap by one
     layer. Restored after `EgressRecoveryWindow = 10 s`.
  7. **Pause filter** — drops every frame when current preset is `Paused`.
  8. **KeyFrame gap filter** — uses `KeyFrameNumber` equality to detect a
     gap; switches to `skipping = true` until the next keyframe arrives.

  There is also an `ApplySimple()` fallback gated on a hard-coded
  `TempBypassSimulcastLogic = false` flag — minimal pass-through for
  troubleshooting.
- Per-stream RPC stream: `BufferSize = 10`, `AckPeriod = 5` — doc-aligned.

**vs. doc:**
- RPC stream sizing matches the doc (`BufferSize = 10`, `AckPeriod = 5`).
- The `joinPendingKF` slot is the doc's `replaceable slot` at this stage
  (1-frame capacity, newest replaces pending), so it is doc-compliant.
- Doc: `server video sender` simply selects a simulcast layer per receiver.
  Current `VideoStreamFilter` is a **dense rule engine** that owns layer
  selection, gap recovery, egress fallback, pause, and join stabilisation
  all in one place.

## 8. `video receiver`

**Now:**
- `src/dotnet/UI.Blazor.App/Components/VideoPanel/video-player.ts` →
  `VideoPlayer.startPull(streamId, skipToMs)` → `streamingApi.streamServer.GetVideo(...)`
  via Fusion RPC.
- Two paths:
  - **In-worker pull** (preferred when the off-thread MSTG/VTG render backend
    is selected): the decoder worker owns the `RpcStream<VideoFrame>`
    directly; main thread does no per-frame work.
  - **Main-thread fallback**: VideoPlayer reads the RPC stream and forwards
    encoded chunks to `DecoderWorker` over the worker RPC channel.
- Each arriving `VideoFrameDto` is converted into a `RawChunkMessage`
  (codec init payload + encoded bytes) and pushed to the decoder; in-worker
  pull does this conversion inside the decoder worker.
- VideoPlayer also: chooses a render backend, resolves `decoderReady` so
  startPull awaits worker init, manages reconnect/retry on
  `pullAbortController` cancellation.

**vs. doc:**
- Doc: receiver should not contain a second playback buffer. Current code
  matches at the **encoded-frame** level (chunks go straight into the
  decoder), but the *decoded* path has a buffer (see next section) — and the
  doc's "video buffer" is supposed to be **encoded** frames. This is the
  biggest structural mismatch. (Section 9 expands on it.)

## 9. `video buffer`

**Now (this is structurally different from the doc):**
- **Canvas/main-thread path:** `VideoPlayer.pendingFrames` —
  `Denque<PendingFrame>` of **decoded** frames, not encoded.
  `maxBufferSize = 20`.
- `enqueuePendingFrame` is the canvas-path entry point. It:
  - Measures inter-frame arrival jitter (`jitterEstimateMs`, EMA α=0.1);
    sets `jitterBufferMs = clamp(jitterEstimateMs * 2, 20, 120)`.
  - Recomputes `pipelineLatencyMs` (asymmetric EMA: α=0.2 up, α=0.15 down).
  - **Soft catchup**: when `pendingFrames.length > 15` AND the buffer span
    exceeds 600 ms, drops oldest frames until ~300 ms remain.
  - **Hard cap**: drops oldest frames once `length > 20`.
- `onRenderFrame()` (called from the rAF loop) consumes the buffer, computes
  a `targetTimestamp` either from `AudioVideoSync` or via wall-clock pacing,
  and adjusts `playbackRate` (1.0 / 1.05 / 1.15) to converge.
- Hard seek to live edge if `bufferSpanMs > seekThresholdMs = 5000`, with a
  5 s cooldown.
- Late-join catchup: if `liveGapMs = lastArrivedOffsetMs - lastRenderedOffsetMs
  > LATE_JOIN_GAP_MS (1500)`, jumps to the latest buffered frame.
- **MSTG/off-thread path:** `src/dotnet/UI.Blazor.App/Services/Video/workers/worker-mstg-selector.ts`
  owns a second decoded-frame queue inside the decoder worker. It soft-catches
  up when `queue.length > 40` and span exceeds 1500 ms, keeps ~1000 ms, and
  hard-caps at 50 decoded frames. This is the preferred path when off-thread
  rendering is available, so `VideoPlayer.pendingFrames` is not the only
  current playback buffer.

**vs. doc:**
- **Buffer is post-decode, not pre-decode.** The doc expects the playback
  buffer to hold encoded frames so quality skips can be keyframe-aware.
  Both current buffers (`VideoPlayer.pendingFrames` and `WorkerMstgSelector.queue`)
  hold already-decoded frames, where the doc says "it is too late to make
  keyframe-aware encoded-frame skip decisions" — which is exactly the case
  here.
- **Sizes differ.** Doc: `TargetBufferSize = 10` (333 ms), min/max 5/15.
  Current: canvas path `maxBufferSize = 20`, with an adaptive `jitterBufferMs`
  of 20–120 ms layered on top, a soft 600 ms catchup trigger, and a 5 s
  hard-seek threshold. MSTG path is larger still (40/50-frame soft/hard caps)
  because it was sized to absorb audio playout jitter.
- **Playback timing differs.** Doc: timing should not chase short
  fluctuations. Current code adjusts `videoEl.playbackRate` between 1.0,
  1.05, and 1.15 (or 0.95) and rebases timing anchors when it does.
- **A/V sync.** Doc: video establishes target delay; audio matches via shared
  origin timeline. Current code does the inverse — `AudioVideoSync` (in
  `nodejs/src/audio-video-sync.ts`) is fed by the audio player, and
  `onRenderFrame` *reads* it to compute `targetTimestamp` for video. So
  audio drives video timing today.

## 10. `video decoder`

**Now:**
- `src/dotnet/UI.Blazor.App/Services/Video/workers/decoder-worker.ts` (1 415
  lines) — runs in a Web Worker, wraps `WebCodecsDecoder`
  (`webcodecs-decoder.ts`).
- Receives `RawChunkMessage` over a transferable stream or RPC, builds an
  `EncodedVideoChunk`, calls `decoder.decode(chunk)`.
- On output: either posts decoded `VideoFrame` back to main (canvas path) or
  routes it directly into `WorkerMstgSelector` (off-thread MSTG path). Tracks
  `medianDecodeTimeMs` and decoder queue depth — these end up in the latency
  report.
- Description / SPS handling: buffers chunks until a keyframe with
  description arrives (H.264 / HEVC); uses `hevc-parser.ts` to package an
  HVCC descriptor.
- Also has small encoded-chunk buffers for decoder configuration, sequence
  recovery, and out-of-order/lost-packet handling. Those are recovery buffers,
  not the intentional playback buffer described by the target doc.

**vs. doc:**
- Doc says "no buffering, immediately hand to renderer". Current code gets
  close only in the happy path; recovery/configuration buffers exist in the
  decoder worker, and the intentional decoded-frame buffer lives either on
  main (`pendingFrames`) or in `WorkerMstgSelector` (see §9).

## 11. `video renderer`

**Now:**
- `src/dotnet/UI.Blazor.App/Components/VideoPanel/render-backend.ts` —
  abstract `RenderBackend` interface.
- Two implementations:
  - `render-backend-canvas.ts` — `canvas.drawImage(VideoFrame|ImageBitmap)`.
    Safari requires an `ImageBitmap` conversion (`createImageBitmap(frame)`).
  - `render-backend-mstg.ts` — uses `MediaStreamTrackGenerator` /
    `VideoTrackGenerator` to feed a hidden `<video>` element off-thread. Main
    thread renders nothing per-frame; the platform compositor does it.
- Selection in `pickRenderBackend()`: prefer MSTG when `isOffThreadPlausible()`,
  fall back to canvas. URL flag `?renderBackend=mstg|canvas` overrides.

**vs. doc:**
- Canvas rendering is roughly aligned as a thin `drawImage` backend, but its
  caller (`VideoPlayer.renderTick`) owns timing. The MSTG backend is thin on
  main, but presentation timing is not purely delegated to the platform:
  `WorkerMstgSelector` selects decoded frames and writes them to the generator
  based on wall-clock/audio timing.

## 12. `video presentation`

**Now:**
- For the canvas backend: `VideoPlayer.renderTick` (rAF loop) computes a
  target timestamp per refresh and picks the latest pending frame whose
  timestamp ≤ target. Older frames are closed and dropped.
- For the MSTG backend: presentation is delegated to the `<video>` element;
  `WorkerMstgSelector` (in the worker) writes one frame at a time via the
  `WritableStream<VideoFrame>` and adjusts `videoEl.playbackRate` to
  converge with audio drift.

**vs. doc:**
- Doc: a single replaceable slot — newer decoded frame replaces a pending
  one, no queue. Current canvas backend does this in spirit (`while
  (pendingFrames.peekFront().timestamp <= adjustedTarget) … frameToRender =
  shift()`), but the upstream `pendingFrames` Denque is not a 1-slot
  semantic. MSTG is farther from the target here because its worker-side
  selector intentionally keeps a decoded-frame queue.

## 13. `control plane`

This is where the gap is largest. The current implementation does not have a
"control plane" abstraction — control logic is woven into the data path,
spread across at least eight files, and uses three different transports
(per-frame fields, dedicated RPC methods, and `Computed<T>` invalidations)
plus several purely local browser-side loops.
Below is a complete walkthrough of how the existing control plane works.

### 13.1 State containers

- **`StreamLatencyStore`** (`src/dotnet/Streaming.Service/Backend/StreamLatencyStore.cs`)
  — node-local, lives on the `VideoStreamingBackend` shard that owns each
  `StreamId`. Holds three concurrent dictionaries keyed by `StreamId`:
  - `LatencyStates` → `StreamLatencyState` (per stream).
  - `KeyFrameRequests` → bool (PLI pending flag).
  - `LastKeyFrameRequestTime` → CpuTimestamp (PLI rate limit).

- **`StreamLatencyState`** (per stream, inside the same file). Owns:
  - `MutableState<VideoQualityPreset> QualityPreset` — the per-stream
    aggregated directive that publishers subscribe to via Fusion.
  - `_peers : ConcurrentDictionary<peerId, PeerLatencyState>` — per-receiver
    state.
  - Throughput counters: `_totalBytesReceived`, `_bytesAtLastCheck`,
    `_lastThroughputCheckAt`, `_consecutiveHighThroughputChecks`.
  - Hysteresis counters: `_consecutiveNetworkSlowChecks`,
    `_consecutiveReceiverSlowChecks`.
  - `_maxObservedSpatialLayer` (lock-free CAS) — used to disable
    over-delivery detection once simulcast is active.
  - `_maxQuality` — pixel-count-derived ceiling (Ultra/Full/High/Medium/Low),
    monotonically promoted when a keyframe reports larger source dims.

- **`PeerLatencyState`** (per receiver of a stream). Owns:
  - `_samples : Queue<float>` — sliding window (`LatencyHistorySize = 5`,
    ~10 s at the 2 s report cadence).
  - `MedianLatencyMs`, `BaselineLatencyMs` (EMA, α=0.05),
    `MedianDecodeTimeMs`, `BufferDepth`, `BufferSpanMs`.
  - `MaxTemporalLayer`, `MaxSpatialLayer` — derived from
    `IsNetworkSlow / IsNetworkFast` with a 2-consecutive-slow-sample
    hysteresis on the drop.
  - `RenderHintSpatialLayer` — client-declared cap based on render canvas
    size (sidebar tile vs. focused view).
  - `EgressFallbackSpatialLayer` — server-side fast-reaction cap set by
    `VideoStreamFilter` when fan-out stalls; restored after 10 s.
  - `EffectiveMaxSpatial = min(MaxSpatial, RenderHint, EgressFallback)`.
  - `IsVisible` — client-declared `document.visibilityState`. Suppresses
    egress-stall handling for hidden tabs.
  - `IsWarmedUp` — peer is ignored for the first `PeerWarmupDuration = 10 s`.
  - `IsNetworkSlow / IsNetworkFast` — **delta-from-baseline** semantics: a
    permanently high but stable latency is **not** congestion; a rise of
    `> baseline + 200 ms` AND `> baseline × 1.3` is.
  - `IsReceiverBound` — `MedianDecodeTimeMs > 100 ms` OR `BufferDepth > 10`
    classifies the bottleneck as decoder/buffer rather than network.
  - `ForwardedSpatialLayerId / ForwardedWidth / ForwardedHeight /
    ObservedMaxSpatialLayer` — written by `VideoStreamFilter` after each
    yield; surfaced back to the client in the latency-report response.

- **`LiveVideoBackend.ChatState`** (`src/dotnet/Streaming.Service/Backend/LiveVideoBackend.ChatState.cs`)
  — per-chat, on the `LiveBackend` shard owning the `ChatId`. Owns:
  - `_currentSupportedDecoderCodecs` with a 10 s downgrade-only hysteresis
    window before climbing to a higher codec tier.
  - `_pausedStreamIds : FrozenSet<string>` — atomic immutable snapshot,
    swapped out under `_codecLock`. Lock-free reads from `ShouldPause(streamId)`.
  - `_lastAudioActivityAt : Dictionary<AuthorId, Moment>` — rolling window
    of who spoke recently.

### 13.2 Signals: what is reported, when, and how

| Signal | Producer | Transport | Consumer |
|---|---|---|---|
| Per-frame byte counts | `PushVideoInternal.ProcessFrames` (server, on data path) | direct method call to `LatencyStore.RecordFrameBytes` | `EvaluateQuality` for over-delivery detection |
| Source dimensions on keyframes | same | `LatencyStore.UpdateMaxQuality` | sets `_maxQuality` ceiling for step-up |
| Per-peer latency report | `VideoPlayer.reportLatencyTick` every 2 s | `streamingApi.streamServer.ReportVideoLatency(streamId, VideoLatencyReport)` over Fusion RPC | `StreamLatencyStore.ReportPeerLatency` → `PeerLatencyState.RecordLatency` |
| Decode time / buffer depth / buffer span | piggybacked on each `VideoLatencyReport` | same RPC | `PeerLatencyState` (drives `IsReceiverBound`) |
| Render-quality hint | piggybacked (`VideoLatencyReport.RenderQuality`) | same RPC | `PeerLatencyState.RenderHintSpatialLayer` |
| Tab visibility | piggybacked (`VideoLatencyReport.IsVisible`) | same RPC | `PeerLatencyState.IsVisible` (suppresses egress-stall) |
| Forwarded layer feedback | server response of `ReportVideoLatency` | `VideoLatencyReportResponse` (LayerId, Width, Height, ObservedMax) | `VideoPlayer.lastForwarded` → diagnostics modal |
| PLI (keyframe request) | `VideoPlayer` when buffer becomes undecodable | `streamingApi.streamServer.RequestKeyFrame(streamId)` → `VideoStreamingBackend.RequestKeyFrame` | sets `KeyFrameRequests[streamId]` flag, invalidates `GetQualityPreset` |
| Aggregated quality preset | `StreamLatencyState.EvaluateQuality` updates `MutableState<VideoQualityPreset>` | `[ComputeMethod] GetQualityPreset` invalidation; `LiveVideoStreams.GetQualityPreset` proxy | `VideoRecorder.SubscribeToQualityRequests` (publisher side) |
| Member registration + decoder codecs | `ChatVideoUI` on join | `ILiveVideoBackend.RegisterMember(chatId, sessionId, supportedDecoderCodecs)` | `LiveVideoBackend.ChatState.RecomputeCodecs` |
| Active streams list | `LiveVideoBackend.Register/Unregister` (called from `PushVideo` start/end + 2.5 min heartbeat) | Redis `RedisMultiHashMap<VideoStreamInfo>` + `[ComputeMethod] List` | `ChatVideoUI.GetActiveVideoStreams`, frontend stream listings |
| Should-pause for a stream | `ChatState.EvaluatePriority` (kicked on register/unregister) | `[ComputeMethod] ShouldPause` invalidation across shards | `VideoStreamingBackend.GetQualityPreset` cross-service RPC |
| Egress stall (data-plane signal) | `VideoStreamFilter` measures `lastYieldAt.Elapsed > 500 ms` | direct method call to `LatencyStore.DecrementPeerEgressFallback` | `PeerLatencyState.EgressFallbackSpatialLayer` |
| Egress recovery | `VideoStreamFilter` after 10 s of clean delivery | direct method call to `LatencyStore.RestorePeerEgressFallback` | same |
| Sender encode backpressure | `video-processing.ts` drops raw frames when `VideoEncoder.encodeQueueSize` stays high; 5 s drop-rate window | worker callback `onBackpressure(dropRate)` | `VideoPipeline.handleEncoderBackpressure` locally reconfigures or falls back codec |
| Local VAD/silence | `RecorderStateHub` voice activity + remote stream count | local TS subscription; worker `setVadState` | `VideoPipeline` lowers bitrate, reduces frame rate, and may drop top simulcast extra during silence |
| Audience codec set | `LiveVideoBackend.ChatState.RecomputeCodecs` from registered members | `[ComputeMethod] GetSupportedCodecs` invalidation | `VideoRecorder.SubscribeToSupportedDecoderCodecs` → JS codec switch/fallback |

### 13.3 Decision loops

There are **at least six** decision loops, running on different cadences in
both server and browser code:

1. **`PeerLatencyState.RecordLatency`** (called per latency report, every 2 s
   per receiver) computes `IsNetworkSlow/Fast`, updates `BaselineLatencyMs`
   EMA, and decides this peer's `MaxSpatialLayer / MaxTemporalLayer` with
   2-sample hysteresis on the drop.

2. **`StreamLatencyState.EvaluateQuality`** (throttled to
   `QualityDecisionInterval = 2 s`, called from `ReportPeerLatency`):
   - Throughput-based **over-delivery** check: if measured bps > 250% of
     target for the current preset for 2 consecutive checks, step down. (HW
     encoder ignoring bitrate cap, e.g. HEVC VBR.) Disabled when simulcast
     is active.
   - **Receiver-bound** path (precedence over network-slow): if more than
     `PeerOutlierRatio` (50 %, or 34 % in calls of ≤3 peers) of peers are
     `IsReceiverBound` for 2 consecutive checks, step down.
   - **Network-slow** path: same shape, on `IsNetworkSlow`.
   - **Step-up**: only when *all* peers are `IsNetworkFast && !IsReceiverBound`
     AND `_lastQualityChangeAt.Elapsed >= QualityHysteresisWindow = 5 s`,
     AND the candidate is at or above `_maxQuality` (the source-derived
     ceiling).
   - **Aggregate** layer caps: `aggregatedMaxSpatial = peers.Max(p =>
     p.EffectiveMaxSpatial)` and the same for temporal; written into the
     preset alongside `ViewerCount`.

3. **`LiveVideoBackend.ChatState.EvaluatePriority`** (kicked from
   `Register/Unregister`):
   - Below `PriorityActivationThreshold = 6` webcam streams in a chat: no
     pausing.
   - Above the threshold: rank speakers by current speech and recency of
     last speech (from `LiveAudioBackend.List`), pause everything past
     `MaxWebcamStreamsPerChat = 8` plus anything between threshold and cap
     that hasn't spoken in `SilenceGracePeriod = 30 s`.
   - Atomically swap `_pausedStreamIds`. Each transition invalidates the
     `[ComputeMethod] ShouldPause(chatId, streamId)` for the affected stream
     so the per-stream `GetQualityPreset` recomputes.

4. **`VideoPipeline.handleEncoderBackpressure`** (browser-local, driven by
   worker backpressure callbacks) smooths the worker's 5 s drop-rate windows
   with an EMA, ignores hidden-tab / startup / post-switch samples, then steps
   down along a fixed 1080p → 720p → 540p → 360p ladder after sustained
   overload. If already at the bottom tier, it asks the recorder to fall back
   to another codec.

5. **VAD adaptive send control** (browser-local, from `RecorderStateHub`):
   after silence in a group call, `VideoPipeline` reduces bitrate, tells the
   worker to lower effective frame rate, and may remove the top simulcast
   extra. Speech resumes restore bitrate/layers and force a keyframe. This is
   independent of server latency decisions.

6. **Codec compatibility control** (chat-level server state, browser-applied):
   `LiveVideoBackend.ChatState` computes the intersection of viewers'
   supported decoder codecs with a 10 s upgrade hysteresis. The publisher
   subscribes to `LiveVideoStreams.GetSupportedCodecs` and can switch encoder
   codec locally. Encoder hard failures also feed the same JS fallback path.

### 13.4 How a directive reaches the publisher

The publisher subscribes via Fusion `Computed.Capture`:

```
src/dotnet/UI.Blazor.App/Services/VideoRecorder.cs:222
    cState = Computed.Capture(() =>
        LiveVideoStreams.GetQualityPreset(Session, ownStreamId, ct));
    foreach (var (preset, _) in cState.Changes(ct)) {
        if (qualityChanged) await _jsRef.InvokeVoidAsync("reconfigure",
            preset.Level.ToString(), preset.Width, preset.Height);
        if (preset.IsKeyFrameRequested) await _jsRef.InvokeVoidAsync("forceKeyFrame");
        if (preset.ViewerCount != _viewerCount) ApplySimulcastDecision(...);
        if (preset.MaxSpatialLayer != _lastMaxSpatial) SetSimulcastLayers(BuildClampedLadder(...));
    }
```

So one `MutableState<VideoQualityPreset>` carries **five distinct
directives** in one record: target preset, keyframe request, viewer count
(arms simulcast), aggregated spatial cap, aggregated temporal cap. The
publisher demultiplexes them on each change.

There is a second publisher-side subscription for supported decoder codecs:
`VideoRecorder.SubscribeToSupportedDecoderCodecs` watches
`LiveVideoStreams.GetSupportedCodecs(...)` and calls JS
`updateSupportedDecoderCodecs`. That path is separate from
`VideoQualityPreset`, but it can still trigger an encoder codec switch and is
therefore part of the effective send-quality control plane.

### 13.5 How layer-selection happens per receiver

`VideoStreamFilter` re-reads `latencyStore.GetPeerMaxSpatialLayer(streamId,
peerId)` once per second, but it's also driven by:
- `RenderHint` (set instantly when a new `VideoLatencyReport` carries a
  `RenderQuality` field, even before warmup).
- `EgressFallback` (set by the filter itself on stall / 150-frame gap).
- `MaxSpatialLayer` from latency-derived classification.

The minimum of the three is `EffectiveMaxSpatial`. The filter then commits
a layer at the *next* keyframe on the desired layer, with burst
stabilisation and decay logic to avoid switching mid-burst or on
out-of-order delivery.

### 13.6 Major differences from the doc's control plane

The doc's control plane is **deliberately coarse**: per-client receive
budgets in bytes/sec, a 3-state receiver signal (`starving / healthy / can
do more`), simple decision cadence with cooldowns, sender-side response to
its own outgoing constraints. Current code is the opposite shape on every
axis:

1. **Granularity.** Current control plane operates on **continuous numeric
   signals** (`MedianLatencyMs`, `BaselineLatencyMs`, `BufferSpanMs`,
   `MedianDecodeTimeMs`, `BufferDepth`, `RenderQuality`, `IsVisible`,
   per-spatial-layer caps, observed-max-spatial). Doc proposes one tri-state
   per client plus measured byte rates.

2. **No receive budget.** The server does not maintain a per-client byte
   budget. Quality is chosen per-stream from a fixed enum
   (`Ultra/Full/High/Medium/Low/Paused`) and per-peer simulcast layer
   selection happens in the data-path filter. Doc says: server should think
   in measured byte rates; named qualities are just choices that fit a
   budget.

3. **Two parallel adaptation mechanisms.** Today, per-stream preset stepping
   AND per-peer spatial-layer caps both adapt to congestion, with their
   own hysteresis. Doc says: do quality switching at decoder-safe
   boundaries inside one logical stream per remote video — i.e. one
   mechanism, not two.

4. **Decision locality.** Today the `VideoStreamingBackend` shard for a
   `StreamId` owns the latency state, but `ChatState` for pausing lives on
   the `LiveBackend` shard for that `ChatId`, and the publisher runs in the
   client. Cross-shard wiring (`GetQualityPreset` calls
   `ILiveVideoBackend.ShouldPause`, `LiveVideoBackend.Register` calls
   `EvaluateStreamPriority` which calls `LiveAudioBackend.List`) is dense.
   Doc treats the control plane as a single conceptual surface.

5. **Receiver state is implicit.** Current code computes `IsNetworkSlow /
   IsReceiverBound` server-side from raw samples sent by the client. Doc
   wants the client to **compute and report a single coarse state** plus a
   measured byte rate.

6. **Buffer health as primary signal.** Doc: `Buffered media duration per
   stream` is the primary health signal. Current: the client sends
   `BufferDepth` and `BufferSpanMs`, but they only contribute to
   `IsReceiverBound` (alongside `MedianDecodeTimeMs`); they are *not* the
   primary congestion signal — `MedianLatencyMs - BaselineLatencyMs` is.

7. **Multiple-knob directive.** Doc: control plane should not move
   individual frames. Current `VideoQualityPreset` carries
   `IsKeyFrameRequested` (a per-frame action) and is driven by both
   server-aggregated caps AND server priority decisions AND PLI requests
   AND viewer-count.

8. **Time model is mixed.** Doc: every unit carries origin capture time;
   receiver builds local presentation mapping. Current: frames carry
   `Offset` (from `ClientStartAt`), there is a server-side clock-skew
   guard (±5 s overrides client offset with server now), and the receiver
   latency report is `serverNow - (StartedAt + StreamOffsetMs)`. Workable
   but not a clean shared origin timeline — and `AudioVideoSync` then
   flips A/V sync to be audio-driven (audio leads, video follows) instead
   of video-driven (doc's model).

9. **Sender-side control is split between server and client.** Doc calls out
   sender backlog/ACK lag and encode cost as explicit sender signals. Current
   implementation partly covers encode cost locally (`encodeQueueSize` →
   drop-rate EMA → resolution/codec step-down) and partly covers send pressure
   indirectly through latency/egress logic. VAD bitrate/layer drops are another
   local sender policy, not visible to the server except through changed frame
   bytes/layers.

### 13.7 Hardest control-plane refactorings

In rough order of difficulty:

1. **Untangle "preset" from "layer cap" from "PLI" from "viewer count"**
   inside `VideoQualityPreset`. The publisher subscribes once and
   demultiplexes — splitting these requires either many `MutableState`s
   (more `ComputeMethod`s, more invalidations) or one richer record with
   careful change-detection. Either way, the publisher's
   `reconfigure / forceKeyFrame / SetSimulcastLayers` calls all need to
   re-anchor.

2. **Move the playback buffer from decoded to encoded.** This is not
   strictly control-plane, but it unblocks the doc's "skip at decoder-safe
   points" model. Today there are two decoded buffers:
   `VideoPlayer.pendingFrames` for canvas and `WorkerMstgSelector.queue` for
   off-thread MSTG. Their rAF/selector timing and `AudioVideoSync`
   integrations are built on decoded-time semantics. Switching to an
   encoded-frame buffer means rebuilding pacing, jitter measurement, and A/V
   sync all at once.

3. **Replace the rule-engine `VideoStreamFilter` with budget-driven layer
   selection.** Burst stabilisation, decay, egress fallback, gap recovery,
   and pause filtering are all colocated and interact through shared
   state. The doc's much smaller "select a simulcast layer per receiver"
   surface needs the per-client byte budget to even exist first.

4. **Compute a coarse receiver state on the client, send it instead of
   raw samples.** Client today computes `pipelineLatencyMs`,
   `jitterEstimateMs`, `smoothedRttMs`, `playbackRate`, etc. internally,
   and sends raw `StreamOffsetMs / MedianDecodeTimeMs / BufferDepth /
   BufferSpanMs`. Server runs the classifier. Inverting this requires
   moving the `IsNetworkSlow/IsReceiverBound`-style classification to the
   client and shrinking the wire format — but the diagnostics modal,
   metrics, and `ForwardedSpatialLayerId` round-trip all read those raw
   numbers today.

5. **Consolidate the quality decision loops.** Server loops
   (`RecordLatency` per-report, `EvaluateQuality` every 2 s,
   `EvaluatePriority` on register events) and browser-local loops
   (backpressure EMA, VAD send reduction, codec fallback) all affect the
   produced or forwarded quality. Doc has one conceptual cadence (3–5 s
   aggregation, slow upgrades, fast downgrades) plus explicit sender
   constraints. Merging them while preserving priority-pause, codec
   compatibility, and silence reduction needs careful unwinding because pause
   currently rides the same `Paused` enum value through `VideoStreamFilter`.

6. **Drop the producer-side 60-frame Denque on top of the RpcStream and
   shrink the RPC buffer to the doc's 10/5.** Looks small, but the current
   eager pre-emptive drops mask a lot of real backpressure cases. Once
   removed, the underlying real-time RpcStream needs to actually do its
   ACK-driven compaction reliably under simulcast (3× per-source-frame
   item rate) and screencast (sparse heartbeat) workloads.

7. **Stream store retention vs. replay tail mismatch.** Doc: replay tail =
   30 frames, drop oldest **keyframe span**. Current: 180-frame retention,
   30-frame replay channel, no keyframe-span eviction. Tightening
   retention is easy; switching to keyframe-span semantics requires the
   memoizer (or a wrapper) to understand keyframe boundaries — which
   today only the filter does.
