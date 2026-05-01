# Video Pipeline Refactoring Plan

This document tracks the incremental refactoring of the live video pipeline
toward the target design.

## Reference docs

- [video-pipeline.md](video-pipeline.md) — target high-level design.
  Conceptual stages, buffering and skipping policies, control plane
  structure, and the canonical `Constants.Video` block.
- [video-pipeline-now.md](video-pipeline-now.md) — current-state map.
  For each conceptual stage, names the matching files and classes today,
  describes how they work, and calls out the major differences from the
  target. Section 13.7 ranks the hardest control-plane refactorings.

## Completed steps

### Step 1 — Unified video pipeline constants

Established a single source of truth for video pipeline constants in .NET
and propagated them to every consumer.

- `Constants.Video` (`src/dotnet/Api/Constants.Video.cs`) is the canonical
  source. Added the doc's `FrameRate`, `FrameDuration`, `TargetBufferSize`,
  `TargetBufferDuration`, `KeyFramePeriod`, `KeyFramePeriodSize`,
  `BufferHysteresisSize`, `MinBufferSize`, `MaxBufferSize`,
  `ServerReplayTailDuration` fields. Renamed `StreamAckPeriod` →
  `RpcStreamAckPeriod`, `StreamBufferSize` → `RpcStreamBufferSize`,
  `ReplayBufferSize` → `ServerReplayTailSize` (values unchanged).
- The serializable DTOs were later split into `AppConstants.Video`
  (`src/dotnet/Api/AppConstants.Video.cs`) and `AppConstants.Audio`
  (`src/dotnet/Api/AppConstants.Audio.cs`); the legacy
  `src/dotnet/Api/Video/VideoConstants.cs` and the TS
  `src/nodejs/src/_constants.ts` are gone. `AppConstants` is registered
  as a singleton in `ApiModule`.
- `BrowserInit` carries `AppConstants` to the main thread; each video /
  audio worker receives them via an `init(appConstants)` RPC and
  populates a module-local `APP_CONSTANTS` / `VIDEO` / `AUDIO` field on
  first call (first-call wins). `whenAppConstantsReady` lets callers
  await initialization, with explicit validation on init.
- The shared module is `src/nodejs/src/app-constants.ts`. TS consumers
  read `VIDEO.frameRate`, `VIDEO.targetBufferSize`, etc. Reading before
  init throws — intended fail-loud behavior.
- Hardcoded TS literals (RpcStream `ackPeriod`/`bufferSize`,
  `SKIP_TO_LIVE_THRESHOLD_MS`, `LATENCY_REPORT_INTERVAL_MS`,
  `SLOW_DECODE_TIME_THRESHOLD_MS`, etc.) now read from `VIDEO.*`.

Outcome: changing one .NET constant updates every consumer everywhere.

### Step 2 — Anchor replay at the first keyframe

Ensured no consumer of the server stream store ever receives a delta
frame before its anchor keyframe.

- `VideoStreamingBackend.GetVideo` and `GetVideoRaw`
  (`src/dotnet/Streaming.Service/Backend/VideoStreamingBackend.cs`)
  apply `stream.SkipWhile(f => !f.IsKeyFrame)` between
  `_videoStreams.Get(...)` and the rest of the read pipeline.
- `AsyncMemoizer<T>` and `StreamStore<T>` are unchanged — they remain
  generic infrastructure shared across audio, video, and transcripts.
- `VideoStreamFilter`'s mid-stream `KeyFrameNumber` gap detection is
  unchanged and still serves as the safety net + observability signal.

Outcome: a single consumer-side filter guarantees every video read starts
at a keyframe, regardless of where in retention the consumer attaches
(initial join, late join, reconnect, p2p, simulcast).

### Step 3 — Replaceable slot ahead of the encoder

Brought the `video encoder` stage in line with the doc's `replaceable
slot` contract.

- `src/dotnet/UI.Blazor.App/Services/Video/workers/video-processing.ts`
  replaces the eager "drop frame when `encoder.encodeQueueSize > N`"
  path in `processOneFrame` with a single replaceable `VideoFrame` slot
  (`pendingEncoderFrame`). When the base encoder is busy, a newer raw
  frame closes and replaces the older pending one; the encoder's
  `onChunk` path drains the slot as soon as the encoder frees up.
- Slot ownership rule: every transition out of the slot reads the field
  into a local AND nulls the field BEFORE calling `encode()`, because
  `WebCodecsEncoder.encode` closes the input frame in `finally`. A
  defensive `try/catch` around `.close()` guards the race window
  introduced by `encodeProcessedFrame`'s async rotation/downscale.
- Centralised the busy threshold (`ENCODER_BUSY_THRESHOLD = isIos ? 1
  : 3`). Renamed observability counters
  (`backpressureDrops/TotalFrames` → `slotReplacements/Arrivals`,
  `backpressureWindowMs` → `slotWindowMs`,
  `backpressureNotified` → `slotPressureNotified`); the 5 s window /
  20 % threshold and the main-thread `onBackpressure(rate)` reaction
  are unchanged.
- Simulcast extra encoders keep their own internal queues; slot
  decisions remain gated on the base encoder's queue (matches prior
  behavior).

Outcome: the encoder stage no longer hides sustained overload by
building a queue — newest raw frame replaces a pending one.

### Step 4 — Video sender = `RpcStream` alone

Made the client `video sender` a thin `RpcStream` wrapper, matching the
doc's "real-time stream, no second buffer on top" model.

- Removed the producer-side 60-frame Denque (`MAX_QUEUE_LENGTH`) and
  `dropForOverflow()` logic from `InternalVideoStream`
  (`src/dotnet/UI.Blazor.App/Services/Video/workers/video-streaming.ts`),
  along with `InternalVideoStreamCallbacks`, `droppedFrameCount`, and
  the `queueDrops` field on `VideoProcessingStreamingStats`. The
  remaining `frames` Denque is just the rendezvous between the encoder
  callback (sync) and the `RpcStream` source iterator (async pull); it
  stays near-empty under healthy operation.
- Aligned `Constants.Video.RpcStreamAckPeriod` (64 → 5 =
  `TargetBufferSize / 2`) and `RpcStreamBufferSize` (192 → 10 =
  `TargetBufferSize`) with the doc's "Constants" block. ACK cadence is
  now ~165 ms and the outstanding window ~333 ms @ 30 fps; on a
  sustained stall the sender's `mustReset` ACK path drains the source
  to the next keyframe and resumes — one keyframe-anchored catch-up
  instead of a multi-second replay burst.
- The same constants drive the server→client fan-out in
  `VideoStreamingBackend.GetVideo` / `GetVideoRaw`, `StreamServer.GetVideo`,
  and `LiveVideoStreams.GetVideo` (one shared policy per the doc's
  "Server video sender" section).

Outcome: `video sender` and `server video sender` both rely on
`RpcStream`'s real-time `canSkipTo = isKeyFrame` semantics for
backpressure, no application-layer caps on top.

### Step 5 — Server stream store: duration-tracked, keyframe-span retention

Replaced the count-based memoizer eviction with the doc-target "drop
oldest keyframe span, sized in source-time" policy.

- `AsyncMemoizer<T>` (`src/dotnet/Core/Async/AsyncMemoizer.cs`) factored
  the bounded-eviction loop out of `AppendItem` into a protected virtual
  `EvictIfNeeded(Node)`. The default implementation is the existing
  count-based FIFO drop, so audio, transcripts, and `MediaSource` keep
  working unchanged. `Node` is now `protected internal sealed`, with
  `CurrentHead` / `CurrentTail` getters and a `TryAdvanceHead` helper —
  the minimum API a subclass needs to walk and trim the chain.
  `AsyncMemoizer` itself is no longer `sealed`.
- `VideoStreamMemoizer`
  (`src/dotnet/Streaming.Service/Backend/VideoStreamMemoizer.cs`)
  subclasses `AsyncMemoizer<VideoFrame>` with `capacity = int.MaxValue`
  so the base class doesn't compete with the duration policy. It tracks
  per-spatial-layer keyframe queues in a single chain and evicts whole
  keyframe spans whenever any layer's `bufferedDuration` exceeds the
  target; single-span layers are preserved so the retention always
  holds at least one decodable anchor per layer.
- `VideoStreamingBackend.PushVideoInternal` swaps the legacy
  `.Memoize(RetentionBufferSize, ct)` call for
  `new VideoStreamMemoizer(ProcessFrames(...), ServerReplayTailDuration, ct)`.
  Step 2's per-consumer `SkipWhile(!IsKeyFrame)` filter stays as
  defense-in-depth.
- `Constants.Video.RetentionBufferSize` is gone — the source of truth
  is now `ServerReplayTailDuration`. Step 5's earlier sizing rule
  (`ServerReplayTailSize × MaxSimulcastTiers`, dropping the magic
  180-frame retention to 60) is subsumed by the duration policy;
  `MaxSimulcastTiers` stays as an architecture cap.

Outcome: the server replay tail is bounded by source-time and evicts
along keyframe-span boundaries, so a delta whose anchor keyframe has
fallen out of retention is no longer reachable in the store.

### Step 6 — Receiver-side: encoded `video buffer`, single-slot decoded paths

Migrated playback latency from decoded-frame queues onto a single
encoded pre-decode buffer in the decoder worker — the doc's `video
buffer` stage.

- **Step A — Drop dead `reorderBuffer` + legacy `decodeChunk` path**
  (`decoder-worker.ts`). The `decodeChunk` RPC entry point had no
  callers (only `decodeRawChunk` is used by both the in-worker pull
  loop and the main-thread fallback). Removed: `reorderBuffer` Map,
  `processBufferedChunks`, `nextExpectedSequence`, `MAX_REORDER_GAP`,
  `lastKeyframeSequence`, the local `decodeChunk` function,
  `handleCodecChange`, `codecFamily`, `pendingChunks`, and the
  `extractHVCC` import. `waitingForKeyframe` is kept (still set by
  `decodeRawChunk` recovery). Net: −313 lines in `decoder-worker.ts`,
  −7 in `decoder-worker-contract.ts`. Subsumes the planned Step B.
- **Step E.1 — Encoded pre-decode buffer in the decoder worker.** A
  `RingBuffer<EncodedChunkArgs>` sits between the `RpcStream` input
  and the `WebCodecs` decoder; the drain loop pulls one chunk at a
  time, gated by `VideoDecoder.decodeQueueSize` so the platform
  decoder's internal queue stays ≤ `DRAIN_DECODE_QUEUE_LIMIT` (4).
  Buffer sizes come from `VIDEO.maxBufferSize` (~15 frames soft trim
  to newest keyframe) with a defensive 2× hard cap. The
  `decodeRawChunk` RPC entry now pushes into the buffer and returns
  immediately, so `RpcStream` ACKs flow at network rate. Buffer is
  cleared on stop / `resetDecoder` / `configureDecoder` so a stale
  GOP can't leak into a fresh decoder. `WebCodecsDecoder.getDecodeQueueSize()`
  is exposed so the drain loop can read decoder backpressure without
  going through `getStats()`.
- **Step E.2 — Collapse MSTG selector queue to a single decoded slot.**
  `WorkerMstgSelector.queue` (decoded `VideoFrame[]`, soft 40 / hard 50
  cap) → 1-frame slot (`pending: VideoFrame | null`). With encoded
  pacing now in front of the decoder, only the most-recent decoded
  frame is meaningful for presentation; older queued frames were
  always stale.
- **Step E.3 — Collapse canvas `pendingFrames` Denque to a single slot.**
  `VideoPlayer.pendingFrames` is now a `SingleSlot<PendingFrame>` shim
  exposing the same Denque-shaped API but capped at 1; push closes any
  prior pending frame before storing the new one. Decoded `VideoFrame`s
  on the canvas path now ≤ 2 (one in slot, one being drawn) instead of
  up to 20.
- **Step E.4 — Drop dead canvas-path machinery + relabel diagnostics.**
  Stripped now-dead multi-frame logic from `VideoPlayer`: jitter
  measurement (`jitterEstimateMs`, adaptive 20–120 ms band), soft
  catch-up (length>15 + bufferSpan>600 ms), hard cap loop
  (length>20), audio-sync buffer flush (length≥2 + bufferSpan>2 s),
  wallclock `playbackRate` chase (1.0/1.05/1.15), hard-seek
  (bufferSpan>5 s), and the `bufferSize`/`maxBufferSize`/
  `playbackRate` state. Kept: `LATE_JOIN_GAP_MS` catch-up (uses
  external offsets, works on sparse-heartbeat streams), audio-sync
  target derivation, and the latency-tick recovery path. New fixed
  `JITTER_BUFFER_MS = 40 ms` presentation-side margin. `DecoderStats`
  grew `encodedBufferDepth` + `encodedBufferSpanMs`; `buildLatencyReport`
  now reports encoded-buffer depth/span instead of selector stats.
  `PlayerStats` keeps its shape for the Blazor diagnostics consumer.
  Net: −181 lines in `video-player.ts`.
- **Step F — Refresh stale receiver-buffer docstrings.** Two docblocks
  refreshed: the `SingleSlot` docblock no longer references "follow-up
  commit removes that dead machinery" (E.4 did it), and the
  `worker-mstg-selector.ts` header describes the single-slot model
  instead of "owns the decoded VideoFrame queue". No residual buffers
  found on the receive path.

Outcome: the receiver path has one intentional buffer — encoded chunks
in the decoder worker — and two single-frame slots ahead of the
canvas/MSTG presentation paths. Encoded chunks are ~200× cheaper than
decoded frames, so the same memory budget holds many more frames for
better jitter absorption and a longer late-join window.

#### Follow-ups on Step 6

- **Never drop isolated deltas in encoded-buffer eviction.**
  `pushEncodedChunk`'s hard-cap fallback used to call `buf.pullHead()`,
  which would strand a delta-without-anchor or orphan every delta after
  a single keyframe. Now eviction respects decoder-safe boundaries: at
  hard cap a keyframe clears stale GOP and is accepted; a delta is
  refused and `bufferWaitingForKeyframe` is set, with a PLI requested
  to accelerate recovery. `clearEncodedBuffer` resets the flag.
- **Audio-target pacing of the decoder drain.** With the decoded slot
  collapsed to 1, the freshest decoded frame ran ~300 ms ahead of the
  audio target so the MSTG selector's `pending.ts ≤ target` check
  never passed and the playout track starved. Drain was paced against
  `WorkerMstgSelector.getAudioTargetUs()` with a 33 ms lookahead, with
  a fall-through (no pacing) before audio is observed. **Currently
  disabled** (commit `c0470ed2f`): audio-target pacing and the MSTG
  selector's Rule-4 force-write / starvation watchdog are commented
  out for simplified decoding while the A/V-sync model is being
  rethought (see Step 7).

### Step 7 — Doc-only and adjacent clean-ups

Smaller updates that didn't change wire/data structures but kept the
"current state" doc honest as the refactor moved.

- **`joinPendingKF` marked as doc-compliant `replaceable slot`** in
  `docs/video-pipeline-now.md` §7. The 1-frame capacity / "newest
  higher-layer keyframe replaces pending" shape exactly matches the
  doc's `replaceable slot` for the server-fan-out stage. The same
  section was refreshed to drop the stale "20× larger" RpcStream
  sizing complaint (Step 4 fixed that).
- **A/V sync disabled, gated on a diagnostics-admin toggle.**
  `AudioVideoSync` and the `playbackRate`-chasing video → audio
  follower path are off by default; admins can re-enable from
  diagnostics settings while the doc-target "video establishes the
  presentation delay" model is being designed.
