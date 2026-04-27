/**
 * Decoder Worker Contract (Universal - Chrome & Safari)
 * Defines the API for the decoder worker using RPC pattern
 */

import { RpcNoWait, RpcTimeout } from 'rpc';
import type { DecoderConfig, DecoderStats } from '../webcodecs-decoder';
import type { EncodedChunkData } from '../webcodecs-encoder';
import type { RawChunkMessage } from './stream-channel';

// Re-export for convenience
export type { RawChunkMessage };

/**
 * Decoder Worker API
 * Represents the RECEIVER side of the video pipeline
 * This interface is implemented by the worker and called from the main thread
 */
export interface DecoderWorker {
    /**
     * Initialize the decoder with configuration (RPC fallback path).
     * Use decodeRawChunk() to send chunks one by one.
     * @param config Decoder configuration (codec, description, etc.)
     * @param timeout Optional RPC timeout configuration
     */
    initialize(config: DecoderConfig, timeout?: RpcTimeout): Promise<void>;

    /**
     * Initialize and start stream-based decoding (input only).
     * Encoded chunks are read from the transferred ReadableStream.
     * Decoded frames are returned via onDecodedFrame RPC callback (postMessage with transfer).
     * @param config Decoder configuration
     * @param chunkInputStream Transferred ReadableStream of encoded chunk messages
     * @param timeout Optional RPC timeout
     */
    initializeWithStreams(
        config: DecoderConfig,
        chunkInputStream: ReadableStream<RawChunkMessage>,
        timeout?: RpcTimeout,
    ): Promise<void>;

    /**
     * Stop the decoder and clean up resources
     */
    stop(): Promise<void>;

    /**
     * Decode an encoded chunk (used by pipeline loopback path — kept for backwards compat)
     * @param chunkData Encoded chunk data to decode
     */
    decodeChunk(chunkData: EncodedChunkData): Promise<void>;

    /**
     * Decode raw encoded bytes (used by video-player.ts).
     * The worker creates EncodedVideoChunk internally from the raw bytes.
     * ArrayBuffer args are placed last (before noWait) so RPC's getTransferables()
     * scanning from the end can zero-copy transfer them.
     * @param timestamp Timestamp in microseconds
     * @param duration Duration in microseconds
     * @param isKeyFrame Whether this is a keyframe
     * @param sequenceNumber Chunk sequence number for ordering
     * @param data Raw encoded bytes (transferred, zero-copy)
     * @param description Optional codec description bytes (transferred, zero-copy)
     * @param noWait Fire-and-forget flag (don't wait for response)
     */
    decodeRawChunk(
        timestamp: number,
        duration: number,
        isKeyFrame: boolean,
        sequenceNumber: number,
        data: ArrayBuffer,
        description?: ArrayBuffer,
        noWait?: RpcNoWait
    ): Promise<void>;

    /**
     * Reset the decoder (flush internal queue).
     * Used for tab visibility restore handling.
     */
    resetDecoder(): Promise<void>;

    /**
     * Reconfigure the decoder with new config.
     * Used after reset for tab visibility restore.
     * @param config New decoder configuration
     */
    configureDecoder(config: DecoderConfig): Promise<void>;

    /**
     * Flush pending chunks in the decoder
     */
    flush(): Promise<void>;

    /**
     * Get current decoder statistics
     */
    getStats(): Promise<DecoderStats>;

    /**
     * Toggle between WASM and built-in decoders
     * @param useWasm Whether to use WASM (dav1d.js) decoder or built-in decoder
     */
    toggleDecoderType(useWasm: boolean): Promise<void>;

    /**
     * Switch the worker into off-thread render mode AND start the Fusion RPC
     * pull entirely inside the worker. The worker constructs a
     * MediaStreamTrackGenerator (Chromium) or VideoTrackGenerator (Safari)
     * locally, owns the writable, runs audio-clock-driven selection, ships
     * the resulting MediaStreamTrack back to main via onOffThreadTrackReady,
     * AND iterates `streamingApi.streamServer.GetVideo(streamId, skipToTicks)`
     * — feeding chunks directly into its own decoder. Main does no per-frame
     * work on this path.
     *
     * Resolves once the generator is created and pull has started. Rejects if
     * the worker has no usable generator API — caller falls back to the
     * main-thread canvas + main-thread pull path.
     *
     * Two-tier generator construction:
     *   Tier 1 — worker constructs MSTG/VTG itself (Safari workers, Chromium
     *     workers that expose MSTG). No `writable` arg required; worker fires
     *     onOffThreadTrackReady with the track for main to attach.
     *   Tier 2 — main constructs MSTG, attaches track locally, passes the
     *     writable to the worker. Used on Chromium versions where MSTG only
     *     exists in main-thread globals (worker has neither MSTG nor VTG).
     *     `writable` carries the transferred stream; track is already attached.
     *
     * @param streamId Server stream id
     * @param skipToMs Initial skip-to offset in ms (forwarded as TimeSpan ticks)
     * @param apiUrl Fusion RPC websocket URL (e.g. wss://host/rpc/ws)
     * @param startedAtMs Stream start ms-since-epoch (server clock)
     * @param jitterBufferMs Initial jitter buffer in ms
     * @param syncPort MessagePort subscribed to AudioVideoSync (transferred)
     * @param writable Optional WritableStream<VideoFrame> from main-side MSTG
     *                 (transferred — when present, tier 2). MUST trail syncPort.
     */
    startPullInWorker(
        streamId: string,
        skipToMs: number,
        apiUrl: string,
        startedAtMs: number,
        jitterBufferMs: number,
        syncPort: MessagePort,
        writable?: WritableStream<VideoFrame>,
    ): Promise<void>;

    /**
     * Abort any in-flight pull loop started by startPullInWorker. Idempotent.
     */
    stopPullInWorker(noWait?: RpcNoWait): Promise<void>;

    /**
     * Forward connectivity state from main thread's ConnectivityUI to the
     * worker's WorkerConnectivityUI mirror. Same shape as the sender worker.
     */
    onConnectivityUpdate(
        isOnline: boolean,
        isConnected: boolean,
        isBlazorServer: boolean,
        noWait?: RpcNoWait,
    ): Promise<void>;
}

/**
 * Callbacks from Decoder Worker to Main Thread
 * These are for asynchronous events only (not method responses)
 */
export interface DecoderWorkerCallbacks {
    /**
     * Called when a frame has been decoded (asynchronous event)
     * @param frame Decoded VideoFrame (will be transferred)
     * @param noWait Fire-and-forget flag (don't wait for response)
     */
    onDecodedFrame(frame: VideoFrame, noWait?: RpcNoWait): Promise<void>;

    /**
     * Fired by the worker after startPullInWorker creates a generator —
     * delivers the MediaStreamTrack the main thread must attach to a
     * <video srcObject>. Track is transferable.
     */
    onOffThreadTrackReady(track: MediaStreamTrack, noWait?: RpcNoWait): Promise<void>;

    /**
     * Fired periodically (~every 2 s) by the worker pull loop with the most
     * recent `streamOffsetMs` so the main side can call ReportVideoLatency on
     * the server. Replaces the main-thread `reportLatencyTick`.
     */
    onLatencyReport(streamOffsetMs: number, noWait?: RpcNoWait): Promise<void>;

    /**
     * Fired when the worker's pull loop terminates (server ended, fatal error,
     * or stop). Main forwards to Blazor via existing `OnEnded`.
     */
    onPullEnded(errorMessage: string | null, noWait?: RpcNoWait): Promise<void>;
}
