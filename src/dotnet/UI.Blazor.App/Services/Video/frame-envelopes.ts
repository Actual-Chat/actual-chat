// Data envelopes carried between pipe operators.
//
// Sender:   CapturedFrame → SimulcastBundle → EncodedFrame → wire DTO
// Receiver: wire DTO → ArrivedChunk → DecodedFrame → presented
//
// Stats threading: sender envelopes share one RecorderStats per pipe run;
// receiver envelopes share one PlayerStats at the SESSION level (across
// all concurrent playback pipelines). Operators mutate counters in place.

import type { MonotonicTime } from 'clocks';
import type { FrameDropStage } from './frame-drop-trace';
import type { RotationQuarter } from './orientation/quantize';

// ---- Stats ---------------------------------------------------------------

// Per-recording-run, mutable counters. Operators bump in place; the
// main-thread reporter polls once per second, diffs, and ships the
// derived snapshot to Blazor.
//
// Only fields fed into the QC classifier or video diagnostics survive.
// Cumulative per-stage drop counts live in `dropTrace`; everything else
// that used to track individual drop sites was redundant.
export interface RecorderStats {
    // Cumulative VideoFrames the capture source yielded — bumped in
    // `mstpSource`, ahead of flood-gate / downscaler / encoder. The
    // ground-truth rate of what the camera or screen track is delivering;
    // `track.getSettings().frameRate` is the *negotiated* / requested
    // value, which Chrome's screencast pipeline routinely overstates.
    framesCaptured: number;
    // Cumulative bundles successfully shipped to the wire. One per source
    // moment (NOT per per-layer chunk).
    bundlesShipped: number;
    // Cumulative bundles successfully encoded by all per-layer encoders
    // (one per source moment, increments at the encode operator's yield
    // boundary). Bundles can be encoded but not yet shipped if wire-send
    // back-pressures, so this isolates encoder throughput from wire
    // throughput. Drives the encoder-throughput QC health signal.
    bundlesEncoded: number;
    // Cumulative encoded bytes summed across all layers — the real on-wire
    // payload total. Drives the outbound bitrate display.
    bytesEncoded: number;
    // Wall-clock encode cost per bundle, split two ways:
    //  * encodeTimeMsSum   — running sum of (sum-across-layers-per-bundle)
    //  * encodeTimeMsMaxSum — running sum of (max-across-layers-per-bundle)
    // Both share `encodeTimeMsCount` (bundles sampled). Display shows
    // mean-per-bundle for each; QC uses encodeTimeMsSum/Count vs frameDuration.
    encodeTimeMsSum: number;
    encodeTimeMsMaxSum: number;
    encodeTimeMsCount: number;
    // Wire-sender side-channels copied from the active sender's stats.
    wireLastAckAgeMs: number;
    isPeerConnected: boolean;
    // Cumulative bytes drained out of the wire send buffer (handed to the
    // RpcStream as it pulls, gated by the ack-window). While the buffer is
    // backlogged the delta over time is the wire delivery rate = uplink
    // capacity. Maintained by the wireSend operator.
    wireAckedBytes: number;
    // EMA of VideoEncoder.encodeQueueSize sampled per bundle, taking the
    // max across layers. -1 == not yet sampled. Maintained by the encode
    // operator.
    encodeQueueDepthEma: number;
    // EMA of WireSenderStats.queueDepth sampled per bundle shipped.
    // -1 == not yet sampled. Maintained by the wireSend operator.
    wireQueueDepthEma: number;
    // FloodGate skips per second (count of skip events in the last 1 s).
    // Maintained by the floodGate operator.
    floodGateSkipPerSec: number;
    // Count of consecutive Api.peer disconnects since the last successful
    // connection. Resets to 0 when isPeerConnected transitions to true.
    peerReconnectStreak: number;
    // Count of in-place encoder hang resets (handleEncoderHang) in the last
    // 60 s window. Maintained by the encode operator.
    encoderRestartStreakIn60s: number;
    // True when document.visibilityState === 'hidden' on the main thread at
    // the moment stats were last collected. Stamped by the main-thread stats
    // poller; the worker has no document access.
    isTabBackgrounded: boolean;
    // Cumulative per-FrameDropStage drop counts since the run started.
    dropTrace: Map<FrameDropStage, number>;
}

// Per-stream, mutable. Created by Player.start; operators bump in place;
// the latency-tap operator's main-thread callback polls and ships the
// derived snapshot to Blazor. End-to-end: `dropTrace` is aggregated at
// the present operator from the byte trail carried with every frame, so
// it covers recorder + server + receiver stages.
export interface PlayerStats {
    // Cumulative frames successfully written to the present sink.
    presented: number;
    // Cumulative bytes received on the wire for this stream.
    bytesReceived: number;
    // Cumulative per-FrameDropStage drop counts since the stream started.
    dropTrace: Map<FrameDropStage, number>;
    // Present-stage carry-forward — drops accumulated since the last
    // successful present, attributed to ReceiverPresent on the next
    // success. Internal to the present operator; surfaced only via
    // dropTrace.
    pendingPresenterDrops: number;
    // Source-clock ms drained per wall-clock ms at the buffer exit, clamped
    // to [0, 1] (catching up doesn't earn credit). Updated in updatePlaybackRateEma.
    playbackRateEma: number;
    driftAnchorEpoch: number;
    driftLastSampleOffsetMs: number;
    driftLastSampleWallMs: number;
    // Above-baseline downlink delay EMA: max(0, rawLatency - minBaselineSkew)
    // where rawLatency = Date.now() - serverArrivedAtUnixMs and the baseline is
    // the sliding-60s minimum of rawLatency. -1 == not yet sampled.
    // Maintained by the downlink-tap operator; consumed by the Downlink health
    // classifier (Step 5+).
    downlinkLatencyEma: number;
    // Sliding-60s minimum of rawLatency for this stream. Represents the
    // constant cross-clock skew floor; jumps absorb NTP steps within the
    // window. -1 == not yet sampled.
    downlinkMinBaselineMs: number;
    // EMA of wallclock ms between consecutive ArrivedChunks. Bursty arrival
    // (server fan-out hiccup) raises this independently of latency. -1 == not
    // yet sampled.
    arrivalIntervalEma: number;
    // Cumulative ArrivedChunks the decode operator pulled from its source.
    // Pairs with `framesDecoded` to derive the decoder throughput deficit.
    chunksReceived: number;
    // Cumulative VideoFrames the decoder emitted (onFrame callback). Strictly
    // ≤ chunksReceived; the gap is decoder slowness + post-recovery losses.
    framesDecoded: number;
    // EMA of (decodedAt - submitMs) / frameDurationMs. Diagnostic only — for
    // QC use see `decodeDeficitEma` (computed at the main-thread reporter from
    // chunksReceived / framesDecoded deltas).
    decodeRatioEma: number;
    // Count of decoder hang-watchdog fires in the last 60 s. Each event marks
    // a stretch where the decoder accepted chunks but produced nothing within
    // the watchdog window.
    hangRateIn60s: number;
    // Number of consecutive decoder recoveries (rebuild + reconfigure) since
    // the last successful frame. Resets to 0 on the first frame emitted by
    // the rebuilt decoder.
    recoveryStreak: number;
    // EMA of 0/1 sampled per decoded frame at the present stage: 1 when the
    // catch-up skip branch was taken, 0 when the frame proceeded to write.
    // -1 == not yet sampled. Maintained by the mstgPresent operator.
    presentSkipRatio: number;
    // EMA of 0/1 sampled per push to the encoded-frame buffer (when armed):
    // 1 when spanMs() < targetSpanMs/3, 0 otherwise. -1 == not yet sampled.
    // Maintained by EncodedFrameBuffer.
    bufferUnderrunRatio: number;
    // Instantaneous queue depths for catch-up decisions / diagnostics.
    // encodedQueueCount: received-but-not-yet-decoded chunks in EncodedFrameBuffer.
    // decoderQueueSize: chunks submitted to the decode operator awaiting output.
    encodedQueueCount: number;
    decoderQueueSize: number;
}

export function createEmptyRecorderStats(): RecorderStats {
    return {
        framesCaptured: 0,
        bundlesShipped: 0,
        bundlesEncoded: 0,
        bytesEncoded: 0,
        encodeTimeMsSum: 0,
        encodeTimeMsMaxSum: 0,
        encodeTimeMsCount: 0,
        wireLastAckAgeMs: -1,
        isPeerConnected: false,
        wireAckedBytes: 0,
        encodeQueueDepthEma: -1,
        wireQueueDepthEma: -1,
        floodGateSkipPerSec: 0,
        peerReconnectStreak: 0,
        encoderRestartStreakIn60s: 0,
        isTabBackgrounded: false,
        dropTrace: new Map(),
    };
}

export function createEmptyPlayerStats(): PlayerStats {
    return {
        presented: 0,
        bytesReceived: 0,
        dropTrace: new Map(),
        pendingPresenterDrops: 0,
        playbackRateEma: 1,
        driftAnchorEpoch: -1,
        driftLastSampleOffsetMs: 0,
        driftLastSampleWallMs: 0,
        downlinkLatencyEma: -1,
        downlinkMinBaselineMs: -1,
        arrivalIntervalEma: -1,
        chunksReceived: 0,
        framesDecoded: 0,
        decodeRatioEma: -1,
        hangRateIn60s: 0,
        recoveryStreak: 0,
        presentSkipRatio: -1,
        bufferUnderrunRatio: -1,
        encodedQueueCount: 0,
        decoderQueueSize: 0,
    };
}

// Call from the present operator after each successful present.
// Samples once per DRIFT_SAMPLE_INTERVAL_MS; intermediate calls just update anchors.
const DRIFT_SAMPLE_INTERVAL_MS = 1000;
const DRIFT_EMA_ALPHA = 0.3;

export function updatePlaybackRateEma(
    stats: PlayerStats,
    presentedOffset: { timeMs: number; epoch: number },
    wallNowMs: number,
): void {
    if (presentedOffset.epoch !== stats.driftAnchorEpoch) {
        stats.driftAnchorEpoch = presentedOffset.epoch;
        stats.driftLastSampleOffsetMs = presentedOffset.timeMs;
        stats.driftLastSampleWallMs = wallNowMs;
        stats.playbackRateEma = 1;
        return;
    }

    const wallDelta = wallNowMs - stats.driftLastSampleWallMs;
    if (wallDelta < DRIFT_SAMPLE_INTERVAL_MS) return;

    const offsetDelta = Math.max(0, presentedOffset.timeMs - stats.driftLastSampleOffsetMs);
    const playbackRate = Math.min(1, offsetDelta / wallDelta);
    stats.playbackRateEma = (1 - DRIFT_EMA_ALPHA) * stats.playbackRateEma + DRIFT_EMA_ALPHA * playbackRate;
    stats.driftLastSampleOffsetMs = presentedOffset.timeMs;
    stats.driftLastSampleWallMs = wallNowMs;
}

// Walk `frame.dropTrace` (the byte array on every envelope) into the
// per-stream cumulative histogram. Called by the terminal stage on every
// frame it observes (present operator on receiver, wireSend on sender).
export function aggregateDropTrace(
    stats: { dropTrace: Map<FrameDropStage, number> },
    frameDropTrace: readonly FrameDropStage[],
): void {
    if (frameDropTrace.length === 0)
        return;
    const histogram = stats.dropTrace;
    for (const stage of frameDropTrace) {
        histogram.set(stage, (histogram.get(stage) ?? 0) + 1);
    }
}

// ---- Sender ---------------------------------------------------------------

export interface CapturedFrame {
    frame: VideoFrame;

    // Sender's MonotonicClock at capture; receiver compares against its own
    // MonotonicClock for E2E latency / pacing.
    capturedAt: MonotonicTime;

    // Monotonic per-capture counter assigned in `mstpSource`. Drops at any
    // later sender stage become visible as gaps in this index. Correlates
    // input↔output across the WebCodecs encoder boundary via the encoder lane FIFO.
    index: number;

    // End-to-end drop attribution. See frame-drop-trace.ts.
    dropTrace: FrameDropStage[];

    // Source (pre-downscale) dimensions. Threaded end-to-end so wire keyframe
    // DTOs carry them — server tracks source-res growth to unlock quality presets.
    sourceWidth: number;
    sourceHeight: number;

    // Set by stampCaptureTime (epoch bump), forceKeyframeOnDimChange,
    // applyKeyframePolicy, and recorder.requestKeyframe (PLI).
    forceKeyframe: boolean;

    // Quarter-turn CW the receiver should apply to display upright.
    // All layers in one source moment share the same value.
    rotation: RotationQuarter;

    stats: RecorderStats;
}

// Sender surface after normalization: one frame at the top spatial layer's
// locked dimensions, with crop/orientation compensation already baked into
// pixels and `rotation` set for remote presentation.
export type NormalizedFrame = CapturedFrame;

// Downscaler output. `layers` is bottom-first (length 1..3). All entries share
// capturedAt/index/forceKeyframe/sourceWidth/Height; only `frame` differs.
// Single-tier (P2P) sources produce a length-1 bundle.
export interface CapturedBundle {
    layers: CapturedFrame[];
    // Bundle-level index (== layers[*].index) for drop-trace gap detection.
    index: number;
    dropTrace: FrameDropStage[];
    rotation: RotationQuarter;
    stats: RecorderStats;
}

export function disposeCapturedBundle(bundle: CapturedBundle): void {
    for (const f of bundle.layers) {
        try { f.frame.close(); } catch { /* already closed */ }
    }
}

export interface EncodedFrame {
    chunk: EncodedVideoChunk;
    metadata: EncodedVideoChunkMetadata;

    capturedAt: MonotonicTime;
    index: number;
    dropTrace: FrameDropStage[];

    // 0 = base (lowest-res); 1+ = higher-res layers.
    layerId: number;

    // Source (pre-downscale) dims; carried for keyframe wire DTO.
    sourceWidth: number;
    sourceHeight: number;

    // This layer's encoder output dims (post-downscale).
    encodedWidth: number;
    encodedHeight: number;

    rotation: RotationQuarter;

    // avcC/HVCC/AV1C bytes for receiver-side decoder.configure().
    // WebCodecs UAs only attach metadata.decoderConfig.description on the
    // first chunk after configure(); the stampEncoderDescription operator
    // caches it per layer and stamps every subsequent frame so the value
    // survives wireGate drops during warmup.
    description?: Uint8Array;

    stats: RecorderStats;
}

// Encode operator output. Bottom-first (length 1..3); all frames share
// capturedAt/index from the same source CapturedBundle.
export interface EncodedBundle {
    layers: EncodedFrame[];
    index: number;
    dropTrace: FrameDropStage[];
    rotation: RotationQuarter;
    stats: RecorderStats;
}

export function disposeEncodedBundle(bundle: EncodedBundle): void {
    for (const f of bundle.layers) {
        closeEncodedChunk(f.chunk);
    }
}

// ---- Receiver -------------------------------------------------------------

export interface ArrivedChunk {
    chunk: EncodedVideoChunk;

    // Receiver wallclock at intake; drives buffer pacing (arrivedAt + bufferSpanMs).
    arrivedAt: MonotonicTime;

    // Sender's MonotonicClock (from wire Offset+OffsetEpoch). Epoch change
    // forces pacing/decode anchor reset.
    capturedAt: { timeMs: number; epoch: number };

    // Sender-assigned source-moment counter; gap == frames dropped somewhere
    // upstream of this point. 0 if the sender peer didn't populate it.
    index: number;
    dropTrace: FrameDropStage[];

    // Server's Date.now()-equivalent at ProcessFrames ingest, converted from
    // DateTime.UtcNow.Ticks on the wire. 0 == legacy frame with no stamp.
    // Used by downlink-tap to compute server->receiver latency via a
    // sliding-min skew baseline (downlinkMinBaselineMs on PlayerStats).
    serverArrivedAtUnixMs: number;

    isKeyFrame: boolean;

    // SPS/PPS (H.264) or HVCC (HEVC). Present on (some) keyframes; cached
    // per layer for resolution-change recovery.
    description?: ArrayBuffer;

    layerId: number;
    width: number;
    height: number;

    rawByteLength: number;

    // Quarter-turn CW the receiver should apply to display upright. 0 when
    // the wire DTO omits the field (legacy sender).
    rotation: RotationQuarter;

    stats: PlayerStats;
}

export interface DecodedFrame {
    frame: VideoFrame;

    capturedAt: { timeMs: number; epoch: number };
    arrivedAt: MonotonicTime;

    // Canonical "frame age" reference for the latency-tap operator.
    decodedAt: MonotonicTime;

    index: number;
    dropTrace: FrameDropStage[];

    layerId: number;

    rotation: RotationQuarter;

    stats: PlayerStats;
}

// ---- Helpers -----------------------------------------------------------

// EncodedVideoChunk.close() exists on Chrome 127+ but not stable Safari/Firefox.
export function closeEncodedChunk(chunk: EncodedVideoChunk): void {
    const close = (chunk as unknown as { close?: () => void }).close;
    if (typeof close !== 'function')
        return;

    try { close.call(chunk); } catch { /* ignore */ }
}
