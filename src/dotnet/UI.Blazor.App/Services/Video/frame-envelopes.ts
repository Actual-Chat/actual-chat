// Data envelopes carried between pipe operators.
//
// Sender:   CapturedFrame → SimulcastBundle → EncodedFrame → wire DTO
// Receiver: wire DTO → ArrivedChunk → DecodedFrame → presented
//
// Stats threading: sender envelopes share one VideoRecordingStats per pipe run;
// receiver envelopes share one VideoPlaybackStats at the SESSION level (across
// all concurrent playback pipelines). Operators mutate counters in place.

import type { MonotonicTime } from 'clocks';
import type { FrameDropStage } from './frame-drop-trace';

// ---- Stats ---------------------------------------------------------------

export interface VideoRecordingStats {
    framesCaptured: number;
    // Frames that reached the encoder (survived blur, dim guard, YUV preconvert, downscale).
    framesProcessed: number;
    framesDroppedDimMismatch: number;
    framesDroppedBackpressure: number;
    framesDroppedOther: number;
    chunksEncoded: number;
    keyframesEncoded: number;
    bytesEncoded: number;
    encodeTimeMsSum: number;
    encodeTimeMsCount: number;
    lastCapturedEpoch: number;
    startedAtMs: number;
    wireFramesAdded: number;
    wireQueueDepth: number;
    wireMaxQueueDepth: number;
    // RpcStreamSender ring compaction (canSkipTo=isKeyFrame); NOT queue overflow.
    rpcStreamFramesSkipped: number;
    // Flood-gate skips when push-to-pull-buffer can't keep up with the wire.
    floodGateSkipCount: number;
    wireLastAckAgeMs: number;
    isPeerConnected: boolean;
    // Local self-view tap failures; sender pipeline unaffected, only preview lags.
    previewClonesFailed: number;
}

// Session-level (PlaybackSession) — every concurrent playback pipeline contributes.
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

    stats: VideoRecordingStats;
}

// Downscaler output. `layers` is bottom-first (length 1..3). All entries share
// capturedAt/index/forceKeyframe/sourceWidth/Height; only `frame` differs.
// Single-tier (P2P) sources produce a length-1 bundle.
export interface CapturedBundle {
    layers: CapturedFrame[];
    // Bundle-level index (== layers[*].index) for drop-trace gap detection.
    index: number;
    dropTrace: FrameDropStage[];
    stats: VideoRecordingStats;
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

    stats: VideoRecordingStats;
}

// Encode operator output. Bottom-first (length 1..3); all frames share
// capturedAt/index from the same source CapturedBundle.
export interface EncodedBundle {
    layers: EncodedFrame[];
    index: number;
    dropTrace: FrameDropStage[];
    stats: VideoRecordingStats;
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

    isKeyFrame: boolean;

    // SPS/PPS (H.264) or HVCC (HEVC). Present on (some) keyframes; cached
    // per layer for resolution-change recovery.
    description?: ArrayBuffer;

    layerId: number;
    width: number;
    height: number;

    rawByteLength: number;

    stats: VideoPlaybackStats;
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

    stats: VideoPlaybackStats;
}

// ---- Helpers -----------------------------------------------------------

// EncodedVideoChunk.close() exists on Chrome 127+ but not stable Safari/Firefox.
export function closeEncodedChunk(chunk: EncodedVideoChunk): void {
    const close = (chunk as unknown as { close?: () => void }).close;
    if (typeof close !== 'function')
        return;

    try { close.call(chunk); } catch { /* ignore */ }
}
