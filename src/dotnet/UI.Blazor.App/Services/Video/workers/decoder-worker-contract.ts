/**
 * Decoder Worker Contract (Universal - Chrome & Safari)
 * Defines the API for the decoder worker using RPC pattern
 */

import { RpcNoWait, RpcTimeout } from 'rpc';
import type { DecoderConfig, DecoderStats } from '../webcodecs-decoder';
import type { RawChunkMessage } from './stream-channel';
import type { AppConstants } from 'app-constants';

// Re-export for convenience
export type { RawChunkMessage };

export interface DecoderWorkerLatencyReport {
    streamOffsetMs: number;
    medianDecodeTimeMs: number;
    bufferDepth: number;
    bufferSpanMs: number;
    // Last keyframe's transmitted dims (VideoFrameDto.Width/Height tracked
    // by the worker on each keyframe). Surfaced to main so output
    // verification can compare decoded dims against this fresh reference
    // — never against the stream-creation-time VideoFormat metadata,
    // which goes stale on resolution change (rotation, simulcast layer
    // switch, screencast resize). 0 / undefined when no keyframe with
    // dims has been seen yet.
    lastKeyframeWidth?: number;
    lastKeyframeHeight?: number;
}

/**
 * Decoder Worker API
 * Represents the RECEIVER side of the video pipeline
 * This interface is implemented by the worker and called from the main thread
 */
export interface DecoderWorker {
    // Propagates app-wide constants from the main thread (sets CONSTANTS / VIDEO).
    // Called once per worker, before any other RPC. First call wins.
    init(appConstants: AppConstants): Promise<void>;

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
     * Decode raw encoded bytes (used by video-player.ts).
     * The worker creates EncodedVideoChunk internally from the raw bytes.
     * ArrayBuffer args are placed last (before noWait) so RPC's getTransferables()
     * scanning from the end can zero-copy transfer them. Numeric `width`/`height`
     * sit before the buffers — they're not transferable, so they don't disturb
     * the trailing-transferable scan.
     * @param timestamp Timestamp in microseconds
     * @param duration Duration in microseconds
     * @param isKeyFrame Whether this is a keyframe
     * @param sequenceNumber Chunk sequence number for ordering
     * @param width Source frame width (keyframe only, undefined if unknown)
     * @param height Source frame height (keyframe only, undefined if unknown)
     * @param data Raw encoded bytes (transferred, zero-copy)
     * @param description Optional codec description bytes (transferred, zero-copy)
     * @param noWait Fire-and-forget flag (don't wait for response)
     */
    decodeRawChunk(
        timestamp: number,
        duration: number,
        isKeyFrame: boolean,
        sequenceNumber: number,
        width: number | undefined,
        height: number | undefined,
        data: ArrayBuffer,
        description?: ArrayBuffer,
        noWait?: RpcNoWait
    ): Promise<void>;

    /**
     * Reset the decoder (flush internal queue).
     * Used as last-resort recovery on real decoder errors.
     */
    resetDecoder(): Promise<void>;

    /**
     * Tell the worker to drop incoming deltas until the next keyframe.
     * Used on tab-visibility restore and SKIP_TO_LIVE — server keeps the
     * existing pull running, client just waits for the PLI keyframe to
     * appear in-band. Does NOT close, recreate, or reconfigure the decoder.
     */
    flagWaitingForKeyframe(): Promise<void>;

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
     * Pre-warm the worker's Fusion RPC peer: triggers Api.init + requireConnection
     * so the WebSocket handshake happens BEFORE startPullInWorker is invoked.
     * Without pre-warm the WS connect (Started → Connecting → WS open →
     * Handshake) is observed sequentially between `Off-thread pull started`
     * and the first chunk — adding ~hundreds of ms to the rotating-indicator
     * window on iOS. Idempotent: subsequent calls no-op.
     * Fire-and-forget — caller does NOT need to await before starting pull.
     * @param apiUrl Fusion RPC websocket URL (e.g. wss://host/rpc/ws)
     */
    prewarmRpc(apiUrl: string, noWait?: RpcNoWait): Promise<void>;

    /**
     * Switch the worker into off-thread render mode AND start the Fusion RPC
     * pull entirely inside the worker. The worker constructs a
     * MediaStreamTrackGenerator (Chromium) or VideoTrackGenerator (Safari)
     * locally, owns the writable, runs audio-clock-driven selection, ships
     * the resulting MediaStreamTrack back to main via onOffThreadTrackReady,
     * AND iterates `streamingApi.liveVideoStreams.GetStream(session, streamId, skipToTicks)`
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
     * @param serverClockOffsetMs `ServerClock.now() - Date.now()` snapshotted on
     *                 main thread. Worker can derive server-aligned now via
     *                 `Date.now() + serverClockOffsetMs`. Drives the
     *                 wallclock target in MstgSelector.tick().
     * @param jitterBufferMs Initial jitter buffer in ms
     * @param writable Optional WritableStream<VideoFrame> from main-side MSTG
     *                 (transferred — when present, tier 2).
     * @param bgCanvas Optional OffscreenCanvas for the blurred letterbox-fill
     *                 backdrop (§13). Worker draws the latest VideoFrame at
     *                 64×N with `ctx.filter='blur(...)'` baked into the bitmap,
     *                 throttled to ~10 fps. Transferred — main loses control.
     */
    startPullInWorker(
        streamId: string,
        skipToMs: number,
        apiUrl: string,
        startedAtMs: number,
        serverClockOffsetMs: number,
        jitterBufferMs: number,
        writable?: WritableStream<VideoFrame>,
        bgCanvas?: OffscreenCanvas,
    ): Promise<void>;

    /**
     * Abort any in-flight pull loop started by startPullInWorker. Idempotent.
     */
    stopPullInWorker(noWait?: RpcNoWait): Promise<void>;

    /**
     * Toggle the blur-backdrop paint inside the worker's MstgSelector. Off for
     * sidebar / unfocused tiles (CSS hides the bg canvas anyway, no point spending
     * CPU on box-blur every 100 ms); back on when the tile becomes the focused
     * one. No-op when no off-thread pull is active. Idempotent.
     */
    setBgPaintEnabled(enabled: boolean, noWait?: RpcNoWait): Promise<void>;

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
    getSessionToken(minLifespanMs?: number): Promise<string>;

    /**
     * Called when a frame has been decoded (asynchronous event).
     *
     * Ownership: the VideoFrame is **transferred** to the main thread via the
     * RPC framework's auto-transfer (VideoFrame is detected as Transferable in
     * `getTransferables`). The main-thread implementer **MUST `frame.close()`
     * exactly once** — after rendering, after a downstream transfer, or in an
     * error path. Without close the platform GCs the frame and HW decoder
     * pool slots leak; dev builds emit "VideoFrame was garbage-collected
     * without being closed".
     *
     * @param frame Decoded VideoFrame (transferred — caller must close)
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
     * recent stream offset and receiver-side pressure metrics so the main side
     * can call ReportVideoLatency on the server. Replaces the main-thread
     * `reportLatencyTick`.
     */
    onLatencyReport(report: DecoderWorkerLatencyReport, noWait?: RpcNoWait): Promise<void>;

    /**
     * Fired when the worker's pull loop terminates (server ended, fatal error,
     * or stop). Main forwards to Blazor via existing `OnEnded`.
     */
    onPullEnded(errorMessage: string | null, noWait?: RpcNoWait): Promise<void>;
}
