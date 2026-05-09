// Data envelopes carried between pipe operators.
//
// Sender side flow:
//     CapturedFrame  →  SimulcastBundle  →  EncodedFrame  →  wire DTO
//
// Receiver side flow:
//     wire DTO  →  ArrivedChunk  →  DecodedFrame  →  presented
//
// Every envelope carries enough metadata that downstream operators can
// pace, gate, or recover without having to reach back to module-level
// state.
//
// Stats threading:
// - Sender envelopes carry a `stats: VideoRecordingStats` reference.
//   The reference is the SAME object across every envelope produced by
//   one pipe run; operators mutate counters on it as frames pass
//   through. The owning recorder reads / publishes / resets between
//   runs. Per-stream by construction (one pipeline run = one stream).
// - Receiver envelopes carry a `stats: VideoPlaybackStats` reference
//   that points at the SESSION-level aggregator (one object across all
//   concurrent playback pipelines). Operators contribute increments;
//   the session reports periodically. Per-pipeline counters can be
//   derived later by adding a per-pipeline mirror if needed.

import type { MonotonicTime } from 'clocks';

// ---- Stats ---------------------------------------------------------------

/**
 * Per-recording counters mutated by sender-side operators as frames
 * flow through the pipeline. The same instance is referenced by every
 * envelope produced by a single pipe run.
 *
 * Each field is monotonic for the lifetime of the run and reset when a
 * new pipe is constructed. Time totals are accumulated; readers compute
 * averages as `totalMs / count`.
 */
export interface VideoRecordingStats {
    /** Frames produced by the capture source (raw, pre-process). */
    framesCaptured: number;
    /** Frames that survived all pre-encode operators (blur, dim guard,
     *  YUV preconvert, downscale) and reached the encoder. */
    framesProcessed: number;
    /** Frames dropped before reaching the encoder, by reason bucket. */
    framesDroppedDimMismatch: number;
    framesDroppedBackpressure: number;
    framesDroppedOther: number;
    /** Encoded chunks produced (sum across all layers). */
    chunksEncoded: number;
    keyframesEncoded: number;
    /** Aggregate encoded byte volume (sum across layers). */
    bytesEncoded: number;
    /** Encode-time accumulators for medians / averages. */
    encodeTimeMsSum: number;
    encodeTimeMsCount: number;
    /** Most recent capture-clock epoch observed (for diagnostics). */
    lastCapturedEpoch: number;
    /** Wallclock when the recording started (Unix ms via MonotonicClock). */
    startedAtMs: number;
    /** Sender-side bridge / RpcStream counters. */
    wireFramesAdded: number;
    wireQueueDepth: number;
    wireMaxQueueDepth: number;
    /** Frames the RpcStreamSender skipped inside its local ring via
     *  canSkipTo=isKeyFrame. Real-time compaction inside the sender
     *  ring; NOT a queue-overflow drop counter. */
    rpcStreamFramesSkipped: number;
    /** Frames closed-and-skipped at the capture-side flood gate
     *  because `push-to-pull-buffer` couldn't keep up with the wire. */
    floodGateSkipCount: number;
    wireLastAckAgeMs: number;
    isPeerConnected: boolean;
    /** Local self-view tap failures (clone or write rejections). Counted
     *  for diagnostics; the sender pipeline is unaffected — only the
     *  preview lags. */
    previewClonesFailed: number;
}

/**
 * Receiver-side counters. One instance lives at the session level
 * (`PlaybackSession`); every concurrent playback pipeline contributes
 * to the same object. Per-stream breakdowns can be added as a mirror
 * if needed by diagnostics consumers.
 */
export interface VideoPlaybackStats {
    chunksArrived: number;
    chunksDroppedAtBuffer: number;
    chunksDroppedDecoderError: number;
    framesDecoded: number;
    framesPresented: number;
    framesDroppedAtPresenter: number;
    bytesReceived: number;
    decodeTimeMsSum: number;
    decodeTimeMsCount: number;
    /** Distinct active streams contributing right now. */
    activeStreams: number;
    sessionStartedAtMs: number;
}

export function createEmptyRecordingStats(startedAtMs: number): VideoRecordingStats {
    return {
        framesCaptured: 0,
        framesProcessed: 0,
        framesDroppedDimMismatch: 0,
        framesDroppedBackpressure: 0,
        framesDroppedOther: 0,
        chunksEncoded: 0,
        keyframesEncoded: 0,
        bytesEncoded: 0,
        encodeTimeMsSum: 0,
        encodeTimeMsCount: 0,
        lastCapturedEpoch: 0,
        startedAtMs,
        wireFramesAdded: 0,
        wireQueueDepth: 0,
        wireMaxQueueDepth: 0,
        rpcStreamFramesSkipped: 0,
        floodGateSkipCount: 0,
        wireLastAckAgeMs: -1,
        isPeerConnected: false,
        previewClonesFailed: 0,
    };
}

export function createEmptyPlaybackStats(sessionStartedAtMs: number): VideoPlaybackStats {
    return {
        chunksArrived: 0,
        chunksDroppedAtBuffer: 0,
        chunksDroppedDecoderError: 0,
        framesDecoded: 0,
        framesPresented: 0,
        framesDroppedAtPresenter: 0,
        bytesReceived: 0,
        decodeTimeMsSum: 0,
        decodeTimeMsCount: 0,
        activeStreams: 0,
        sessionStartedAtMs,
    };
}

// ---- Sender ---------------------------------------------------------------

/**
 * One captured source frame, stamped with the sender's MonotonicClock at
 * the moment it entered our pipeline. The `frame` is owned by the
 * envelope until handed to the encoder; downstream operators that
 * replace it (downscaler, blur) close the previous one.
 */
export interface CapturedFrame {
    /** The underlying source `VideoFrame`. Owned by this envelope until
     *  consumed by an operator that replaces or destroys it. */
    frame: VideoFrame;

    /** Wallclock-domain capture timestamp (Unix-epoch ms via
     *  `MonotonicClock`). The receiver compares this against its own
     *  `MonotonicClock` for end-to-end latency / pacing. */
    capturedAt: MonotonicTime;

    /** Monotonic submission counter assigned by the capture-stamper
     *  operator. Used by the encoder lane FIFO to correlate input ↔
     *  output across the WebCodecs encoder boundary, and as a stable
     *  ordering key for diagnostics. */
    index: number;

    /** Source (capture-time, pre-downscale) dimensions. Threaded
     *  end-to-end so wire-side keyframe DTOs can carry them — the
     *  server uses them to track source-resolution growth (window
     *  resize) and unlock higher quality presets mid-stream. */
    sourceWidth: number;
    sourceHeight: number;

    /** True iff the encoder MUST emit a keyframe for this frame.
     *  Set by:
     *    - `stampCaptureTime` when the clock's epoch bumps (sleep / NTP
     *      step → receiver must re-bootstrap its decode anchors);
     *    - `forceKeyframeOnDimChange` when coded dims flip;
     *    - `applyKeyframePolicy` for normal interval / wallclock-floor
     *      triggers;
     *    - `recorder.requestKeyframe()` when the server sends a PLI.
     *  The encode operator just reads the flag. */
    forceKeyframe: boolean;

    /** Shared per-run stats reference. Same object across every
     *  envelope produced by one pipe run; operators contribute by
     *  mutating counters as frames pass through. */
    stats: VideoRecordingStats;
}

/**
 * Multi-tier output of the downscaler. The downscaler emits one bundle
 * per input source `CapturedFrame`. `layers` is bottom-first
 * (`layers[0]` = lowest layer, `layers[length-1]` = top). All entries
 * share `capturedAt` / `index` / `forceKeyframe` / `sourceWidth/Height`
 * — only the underlying `frame` differs per layer.
 *
 * Single-tier (P2P / non-simulcast) sources produce a length-1 bundle.
 */
export interface CapturedBundle {
    /** Bottom-first per-layer entries (length 1..3). */
    layers: CapturedFrame[];
    /** Same reference as `layers[*].stats` — exposed at bundle level so
     *  callers don't have to reach into a layer entry. */
    stats: VideoRecordingStats;
}

export function disposeCapturedBundle(bundle: CapturedBundle): void {
    for (const f of bundle.layers) {
        try { f.frame.close(); } catch { /* already closed */ }
    }
}

/**
 * One encoded chunk emerging from a per-layer encoder, paired with the
 * source-side metadata of the captured frame that produced it. Held
 * inside an `EncodedBundle`.
 */
export interface EncodedFrame {
    chunk: EncodedVideoChunk;
    metadata: EncodedVideoChunkMetadata;

    /** Capture-time metadata, threaded through the encoder boundary via
     *  the per-lane FIFO inside `AsyncVideoEncoder`. Identical across
     *  layers for the same source frame. */
    capturedAt: MonotonicTime;
    index: number;

    /** 0 = base (lowest-res) layer; 1+ = higher-res layers. */
    layerId: number;

    /** Source dims at capture (carried through for keyframe wire DTO). */
    sourceWidth: number;
    sourceHeight: number;

    /** Encoded dims of THIS layer's output (= the layer's encoder
     *  config). Differs from `sourceWidth/Height` after downscale. */
    encodedWidth: number;
    encodedHeight: number;

    stats: VideoRecordingStats;
}

/**
 * Multi-tier output of the encode operator. Bottom-first
 * (`layers[0]` = base layer; `layers[length-1]` = top). All frames
 * share `capturedAt` / `index` and were produced from the same source
 * `CapturedBundle`.
 */
export interface EncodedBundle {
    /** Bottom-first per-layer entries (length 1..3). */
    layers: EncodedFrame[];
    stats: VideoRecordingStats;
}

export function disposeEncodedBundle(bundle: EncodedBundle): void {
    for (const f of bundle.layers) {
        closeEncodedChunk(f.chunk);
    }
}

// ---- Receiver -------------------------------------------------------------

/**
 * One encoded chunk arriving from the wire, lifted into our domain
 * with the sender's `capturedAt` extracted (`Offset` + `OffsetEpoch`)
 * and our own `arrivedAt` recorded.
 *
 * Lives until either:
 *   - the encoded buffer accepts it (becomes the buffer's responsibility), or
 *   - it gets dropped at intake (description-cache miss, epoch reset).
 */
export interface ArrivedChunk {
    chunk: EncodedVideoChunk;

    /** Receiver-side wallclock at the moment the chunk landed in the
     *  pull operator. Drives buffer pacing math (`arrivedAt + bufferSpanMs`). */
    arrivedAt: MonotonicTime;

    /** Sender's MonotonicClock capture time (lifted from the wire's
     *  `Offset` ticks + `OffsetEpoch`). Epoch is part of the timing
     *  contract; receiver-side operators reset pacing/decode anchors when
     *  it changes. */
    capturedAt: { timeMs: number; epoch: number };

    isKeyFrame: boolean;

    /** Codec description bytes (SPS/PPS for H.264, HVCC for HEVC).
     *  Present on (some) keyframes; the decode operator caches per
     *  layer for resolution-change recovery. */
    description?: ArrayBuffer;

    layerId: number;
    width: number;
    height: number;

    /** Raw byte length — used for bitrate metrics without having to
     *  re-introspect the chunk. */
    rawByteLength: number;

    /** Session-level playback stats, shared across all concurrent
     *  playback pipelines. Operators contribute increments. */
    stats: VideoPlaybackStats;
}

/**
 * One decoded frame ready for presentation. The frame is owned by the
 * envelope; the present-* sink closes it after submitting to the
 * underlying writable / canvas / MSTG track.
 */
export interface DecodedFrame {
    frame: VideoFrame;

    /** Threaded through from the matching `ArrivedChunk` so latency
     *  reporting can compute (now − capturedAt). */
    capturedAt: { timeMs: number; epoch: number };
    arrivedAt: MonotonicTime;

    /** When did the decoder hand this frame to us. Used as the canonical
     *  "frame age" reference for the latency-tap operator. */
    decodedAt: MonotonicTime;

    /** Layer that produced this frame. Sticky across decode
     *  outputs — the present sink can use it to render layer indicators
     *  in diagnostics. */
    layerId: number;

    stats: VideoPlaybackStats;
}

// ---- Helpers -----------------------------------------------------------

// Defensive close — `EncodedVideoChunk.close()` exists on Chrome 127+
// but isn't yet in stable Safari/Firefox; treat it as best-effort.
export function closeEncodedChunk(chunk: EncodedVideoChunk): void {
    const close = (chunk as unknown as { close?: () => void }).close;
    if (typeof close !== 'function')
        return;

    try { close.call(chunk); } catch { /* ignore */ }
}
