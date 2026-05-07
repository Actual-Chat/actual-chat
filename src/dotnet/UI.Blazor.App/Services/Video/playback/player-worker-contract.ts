import type { RpcNoWait } from 'rpc';
import type { VideoPlaybackStats } from '../frame-envelopes';
import type { LatencySample } from '../operators/latency-tap';
import type { RenderBackendKind } from './render-backends';

export type { LatencySample } from '../operators/latency-tap';

/**
 * Per-stream startup payload sent from the main thread (caller of the
 * playback worker) to the worker's `start()` handler. Identifies the
 * stream and pins the rendering backend the worker should set up.
 *
 * Wire-compatible: every field must serialize cleanly across the
 * worker boundary (no class instances, no functions). The actual
 * platform resources (MSTG track, canvas) are owned by the worker side
 * and constructed there based on the `backend` tag.
 */
export interface PlayerWorkerOptions {
    /** Stream identity used to bind the `getStream` pull source on the
     *  worker side (production: `streamingApi.liveVideoStreams.GetStream`). */
    streamId: string;

    /**
     * Initial decoder configuration. The first keyframe's description
     * (if present) is merged in by the `decode` operator before it
     * calls `configure()`. Codec-only is acceptable for AVC / AV1 (no
     * description required); HEVC needs the wire to deliver one.
     */
    initialDecoderConfig: {
        codec: string;
        codedWidth?: number;
        codedHeight?: number;
    };

    /** Receiver-side jitter buffer span in ms. Drives both the bootstrap
     *  threshold and per-chunk pacing. Typical: 100-300 ms. */
    targetBufferSpanMs: number;

    /** Which rendering surface the worker should set up for this stream. */
    backend: RenderBackendKind;

    /**
     * Tests-only: a synthetic `OffscreenCanvas` for the canvas backend.
     * Production callers MUST pass canvas as a trailing argument of
     * {@link PlayerWorker.start} — `Transferable` values can't ride
     * inside the structured-cloned options envelope. The worker assigns
     * the trailing arg onto this field server-side before
     * `buildBackend` runs.
     */
    canvas?: OffscreenCanvas;

    /**
     * Tests-only: synthetic `WritableStream<VideoFrame>` for the Tier 2
     * MSTG path. Production: pass as a trailing arg of
     * {@link PlayerWorker.start}; worker sets this field internally.
     */
    mstgWritable?: WritableStream<VideoFrame>;
}

/**
 * Worker-side RPC surface called from the main thread. Each method
 * returns a Promise so the main-thread caller can await completion /
 * propagation of errors. Implementations live in `player-worker.ts`.
 *
 * Concurrency: `start()` may be called multiple times with distinct
 * `streamId`s — the worker hosts many concurrent playback pipelines
 * under one shared `PlaybackSession`. `stop()` and `requestKeyframe()`
 * target a specific stream; the worker locates the player by
 * `streamId` (omitting the id signals "all streams").
 */
/** Subset of `AppConstants` the worker needs. Same shape as
 *  `RecorderWorker.AppConstantsLike`. */
export interface AppConstantsLike { readonly appName: string; readonly prodHost: string; readonly video: unknown; readonly audio: unknown }

export interface PlayerWorker {
    /** Initialise worker-local app constants. First call wins. MUST
     *  be called before any {@link start}. */
    init(appConstants: AppConstantsLike): Promise<void>;
    /**
     * Start a playback pipeline for `opts.streamId`. Resolves once the
     * pipeline has been constructed and registered with the session;
     * the actual frame flow begins on the next event-loop tick.
     * Calling `start()` twice with the same streamId rejects (the
     * worker doesn't multiplex pipes per stream).
     *
     * Trailing transferable args (rpc.ts auto-transfer scan):
     *  - `mstgWritable` — Tier 2 path: main constructs
     *    `MediaStreamTrackGenerator` (Chromium exposes it on main but
     *    not in dedicated workers), attaches the resulting track to its
     *    own `<video srcObject>`, and ships only the writable across.
     *    The worker writes decoded frames into this stream.
     *  - `canvas` — main creates an `OffscreenCanvas` via
     *    `transferControlToOffscreen()` and transfers it in for the
     *    canvas-fallback backend (Safari, or Chromium when MSTG isn't
     *    on either side).
     *
     * Trailing transferable args MUST be passed as separate parameters
     * (not nested in `opts`), because `rpc.ts` only auto-transfers a
     * trailing run of transferables — anything inside the structured-
     * cloned `opts` envelope is rejected by the platform.
     */
    start(
        opts: PlayerWorkerOptions,
        mstgWritable?: WritableStream<VideoFrame>,
        canvas?: OffscreenCanvas,
    ): Promise<void>;

    /**
     * Stop a running pipeline. If `streamId` is omitted, all running
     * pipelines are stopped. Resolves once the pipeline's `whenDone`
     * has resolved (i.e. the pipe finished unwinding).
     */
    stop(streamId?: string): Promise<void>;

    /**
     * Hint to the upstream sender (relayed via the streaming API) that
     * we've lost a keyframe and need a fresh one. Best-effort — the
     * server may delay or coalesce. Targets the supplied stream;
     * omitting the id requests for all running streams.
     */
    requestKeyframe(streamId?: string): Promise<void>;

    /** Snapshot of the session-level aggregated stats. Cheap. */
    getStats(): Promise<VideoPlaybackStats>;

    /**
     * Pre-warm the worker's Fusion RPC peer so the WebSocket handshake
     * happens before the first `start()` call. Idempotent — subsequent
     * calls no-op. Currently a stub on the worker side; production
     * wiring lands in a follow-up.
     * Fire-and-forget: callers should NOT await before calling start().
     * @param apiUrl Fusion RPC websocket URL (e.g. wss://host/rpc/ws)
     */
    prewarmRpc(apiUrl: string, noWait?: RpcNoWait): Promise<void>;

    /**
     * Forward main-thread connectivity state into the worker so its
     * Api peer honours `isDotNetRpcConnected`. Currently a stub on the
     * worker side; production wiring lands in a follow-up.
     */
    onConnectivityUpdate(
        isOnline: boolean,
        isConnected: boolean,
        isBlazorServer: boolean,
        noWait?: RpcNoWait,
    ): Promise<void>;
}

/**
 * Main-thread-side RPC surface. The worker calls these to notify the
 * main thread of lifecycle events — track readiness (so the `<video>`
 * element can be wired up), per-stream errors (so the UI can show a
 * "playback failed" badge), and stream end (clean termination by the
 * sender or a network drop).
 *
 * String error / kind / reason values keep the contract serializer-
 * agnostic. The main thread is responsible for any UX layering on top.
 */
export interface PlayerWorkerCallbacks {
    /**
     * Worker-initiated session token request. The main thread answers
     * via `Api.getSessionToken(minLifespanMs)`. The worker's own
     * `Api.init` calls this at peer-handshake time so the WebSocket
     * authenticates against the server.
     */
    getSessionToken(minLifespanMs?: number): Promise<string>;

    /**
     * Called on terminal pipeline failure. The pipe has unwound; the
     * worker has dropped its `Player` for this stream; the main thread
     * should decide whether to retry (e.g. with a different codec).
     */
    onError(streamId: string, error: string): void;

    /**
     * Called once per `start()` after the worker has set up the
     * rendering surface. For the MSTG backend the worker passes the
     * resulting `MediaStreamTrack` (transferable) so the main thread
     * can attach it via `<video srcObject = …>`. For canvas the track
     * is null — the main thread already holds the `<canvas>` whose
     * `OffscreenCanvas` was transferred to the worker on `start()`.
     */
    onTrackReady(streamId: string, kind: RenderBackendKind, track: MediaStreamTrack | null): void;

    /**
     * Called on clean upstream termination (sender stopped streaming).
     * `reason` is a free-form short string for diagnostics
     * (`'eof'`, `'sender-stop'`, `'session-disposed'`, etc.).
     */
    onStreamEnded(streamId: string, reason: string): void;

    /**
     * Periodic latency report from the worker's `latencyTap` operator
     * (default cadence: 1 Hz while frames are flowing). The main thread
     * forwards this to the server via `ReportVideoLatency` so server-side
     * playback-quality machinery (`PlaybackHealthSnapshot`) can react.
     */
    onLatencyReport(streamId: string, sample: LatencySample): void;
}
