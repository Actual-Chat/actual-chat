/**
 * Video Processing Worker Contract
 * Unified worker that combines segmentation, encoding, and SignalR streaming.
 * All video frame processing happens in a single worker context — no cross-worker
 * VideoFrame transfer needed. Encoded chunks go directly to SignalR from the worker.
 */

import { RpcNoWait, RpcTimeout } from 'rpc';
import type { EncoderConfig, EncoderStats } from '../webcodecs-encoder';
import type { SegmentationConfig, SegmentationStats } from './segmentation-worker-contract';

/**
 * Configuration for the unified video processing worker.
 */
export interface VideoProcessingConfig {
    /** Encoder settings (codec, bitrate, resolution, etc.) */
    encoder: EncoderConfig;
    /** Segmentation settings — omit for no blur */
    segmentation?: SegmentationConfig;
    /** Adaptive framerate settings */
    adaptiveFramerate?: { reducedFps: number };
    /** SignalR streaming configuration */
    streaming: {
        hubUrl: string;
        sessionToken: string;
        chatId: string;
        serverClockOffsetMs: number;
    };
}

/**
 * Combined stats from encoder and segmentation.
 */
export interface VideoProcessingStats {
    encoder: EncoderStats;
    segmentation: SegmentationStats | null;
}

/**
 * Video Processing Worker API
 * Main thread calls these methods via RPC.
 */
export interface VideoProcessingWorker {
    // --- Initialization ---

    /**
     * Start stream-based processing.
     * Transfers MSTP ReadableStream to worker. Worker handles segmentation, encoding,
     * and SignalR streaming internally. No data flows back to main thread except
     * preview frames and control callbacks.
     */
    startWithStream(
        config: VideoProcessingConfig,
        frameInputStream: ReadableStream<VideoFrame>,
        timeout?: RpcTimeout,
    ): Promise<void>;

    /**
     * Initialize in RPC fallback mode (no transferable streams).
     * Use encodeFrame() to send frames one by one.
     */
    initialize(config: VideoProcessingConfig, timeout?: RpcTimeout): Promise<void>;

    /**
     * Process a single frame (RPC fallback path).
     * Frame is transferred via postMessage.
     */
    encodeFrame(frame: VideoFrame, noWait?: RpcNoWait): Promise<void>;

    // --- Control ---

    /** Update VAD state for adaptive framerate */
    setVadState(speaking: boolean, remoteStreamCount: number): Promise<void>;

    /** Reconfigure encoder bitrate/resolution */
    reconfigure(params: { bitrate: number; width: number; height: number }): Promise<void>;

    /** Switch codec mid-stream (flushes encoder, resets SignalR stream) */
    switchCodec(config: EncoderConfig): Promise<void>;

    /** Toggle background blur on/off. Loads ONNX model lazily on first enable. */
    toggleBlur(enabled: boolean, segConfig?: SegmentationConfig): Promise<void>;

    /** Force next encoded frame to be a keyframe */
    forceKeyFrame(): Promise<void>;

    /** Flush pending frames in encoder */
    flush(): Promise<void>;

    /** Stop all processing and clean up resources */
    stop(): Promise<void>;

    /** Get combined encoder + segmentation stats */
    getStats(): Promise<VideoProcessingStats>;

    // --- Dynamic updates from main thread ---

    /** Update session token (for SignalR reconnect) */
    updateSessionToken(token: string): Promise<void>;

    /** Update server clock offset (for timestamp synchronization) */
    updateServerClockOffset(offsetMs: number): Promise<void>;
}

/**
 * Callbacks from worker to main thread.
 */
export interface VideoProcessingWorkerCallbacks {
    /**
     * Encoded chunk data (RPC fallback only — when streaming is disabled
     * or transferable streams not supported).
     */
    onSerializedChunk(
        chunkBytes: ArrayBuffer,
        timestamp: number,
        duration: number,
        isKeyFrame: boolean,
        codec: string,
        sequenceNumber: number,
        descriptionBytes?: ArrayBuffer,
        noWait?: RpcNoWait,
    ): Promise<void>;

    /** Sustained encoder backpressure — main thread should step down quality */
    onBackpressure(dropRate: number, noWait?: RpcNoWait): Promise<void>;

    /** Worker detected dimension mismatch and auto-reconfigured */
    onDimensionReconciled(width: number, height: number, noWait?: RpcNoWait): Promise<void>;

    /** Processed (blurred) preview frame for local camera display */
    onPreviewFrame(frame: VideoFrame, noWait?: RpcNoWait): Promise<void>;

    /** SignalR video stream created — provides codec settings for decoder init */
    onStreamCreated(codecSettings: string, noWait?: RpcNoWait): Promise<void>;
}
