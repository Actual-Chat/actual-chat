import type { RpcNoWait } from 'rpc';
import type { PlayerStats } from '../frame-envelopes';
import type { LatencySample } from '../operators/latency-tap';
import type { RenderBackendKind } from './render-backends';
import type { VideoTraceKillPeriod } from '../frame-drop-trace';
import type { BgBlurMode } from './bg-blur-tap';

export type { LatencySample } from '../operators/latency-tap';

// Per-stream startup payload. Wire-compatible: every field must
// serialize across the worker boundary (no class instances, no
// functions). Platform resources (MSTG track, canvas) are constructed
// worker-side based on `backend`.
//
// Members ordered by receiver pipeline flow:
//   pull → buffer → decode → present (config + transferables).
export interface PlayerWorkerOptions {
    // -- pull --
    streamId: string;

    // -- buffer --
    targetBufferSpanMs: number;

    // -- decode --
    // First keyframe's description (if present) is merged in by the
    // `decode` operator before configure(). Codec-only is OK for
    // AVC/AV1; HEVC needs the wire to deliver one.
    initialDecoderConfig: {
        codec: string;
        codedWidth?: number;
        codedHeight?: number;
    };

    // -- present --
    backend: RenderBackendKind;

    // Initial QC pause state. This must be known before Player.start() arms
    // any no-chunk watchdog, because the first explicit pause update may have
    // arrived before the worker-side Player exists.
    expectedPaused?: boolean;

    // Tests-only. Production callers pass canvas as a trailing arg of
    // start() — Transferables can't ride inside the structured-cloned
    // options envelope. The worker assigns the trailing arg here
    // server-side before buildBackend runs.
    canvas?: OffscreenCanvas;

    // Tests-only. Production: trailing arg of start() (see canvas).
    mstgWritable?: WritableStream<VideoFrame>;
}

// Subset of AppConstants the worker needs.
export interface AppConstantsLike { readonly appName: string; readonly prodHost: string; readonly video: unknown; readonly audio: unknown }

// Worker-side RPC surface called from the main thread. The worker
// hosts many concurrent playback pipelines under one shared
// PlaybackSession; stop() and requestKeyframe() target a specific
// stream (omitting the id signals "all streams").
//
// Methods ordered by lifecycle:
//   init → prewarm → connectivity → run → query → stop.
export interface PlayerWorker {
    // First call wins. MUST be called before any start().
    init(appConstants: AppConstantsLike): Promise<void>;

    // Fire-and-forget: callers should NOT await before calling start().
    prewarmRpc(apiUrl: string, noWait?: RpcNoWait): Promise<void>;

    onConnectivityUpdate(
        isOnline: boolean,
        isConnected: boolean,
        isBlazorServer: boolean,
        noWait?: RpcNoWait,
    ): Promise<void>;

    // Trailing transferable args (rpc.ts auto-transfer scan):
    //  - mstgWritable — Tier 2 path: main constructs MSTG (Chromium
    //    exposes it on main but not in dedicated workers), attaches
    //    the track to <video srcObject>, ships only the writable.
    //  - canvas — main creates an OffscreenCanvas via
    //    transferControlToOffscreen() and transfers it for the
    //    canvas-fallback backend.
    // These MUST be passed as separate parameters (not nested in
    // opts), because rpc.ts only auto-transfers a trailing run of
    // transferables.
    start(
        opts: PlayerWorkerOptions,
        mstgWritable?: WritableStream<VideoFrame>,
        canvas?: OffscreenCanvas,
    ): Promise<void>;

    setTraceKill(avgPeriod: VideoTraceKillPeriod, stage: number): Promise<boolean>;

    requestKeyframe(streamId?: string): Promise<void>;

    setExpectedPaused(streamId: string, paused: boolean, noWait?: RpcNoWait): Promise<void>;

    // Audio presentation capture-point (ms, in the stream's offset domain) for
    // A/V-sync skip-to-audio. null ⇒ no audio signal ⇒ buffer skips to live.
    setAudioCaptureOffsetMs(streamId: string, caOffsetMs: number | null, noWait?: RpcNoWait): Promise<void>;

    // Bind a bg-blur OffscreenCanvas to `streamId`. `canvas` is transferred
    // (trailing transferable). Replaces any previously-installed canvas
    // for the same stream. `mode` hints which renderer the worker should
    // try first; 'auto' lets the worker probe WebGPU itself. Worker may
    // still fall back to Canvas2D if the chosen renderer fails to init.
    installBgCanvas(streamId: string, mode: BgBlurMode, canvas: OffscreenCanvas): Promise<void>;

    // Toggle whether the worker pumps frames into the bg-blur renderer
    // installed for `streamId`. No-op if no canvas is bound.
    setBgActive(streamId: string, active: boolean, noWait?: RpcNoWait): Promise<void>;

    getStats(streamId: string): Promise<PlayerStats>;

    stop(streamId?: string): Promise<void>;
}

// Main-thread-side RPC surface. The worker calls these to notify the
// main thread of lifecycle events. String error/kind/reason values
// keep the contract serializer-agnostic.
//
// Members ordered by lifecycle:
//   setup → start → runtime → end → error (error is anytime).
export interface PlayerWorkerCallbacks {
    getSessionToken(minLifespanMs?: number): Promise<string>;

    // For MSTG the worker passes the resulting MediaStreamTrack
    // (transferable) so the main thread can attach via <video
    // srcObject>. For canvas the track is null.
    onTrackReady(streamId: string, kind: RenderBackendKind, track: MediaStreamTrack | null): void;

    onLatencyReport(streamId: string, sample: LatencySample): void;

    // reason is a free-form short string for diagnostics
    // ('eof', 'sender-stop', 'session-disposed', etc.).
    onStreamEnded(streamId: string, reason: string): void;

    onError(streamId: string, error: string): void;

    onTraceKillInjected(): void;

    // Fired once per stream when the worker has decoded enough frames of a
    // codec to be confident the codec actually works on this device. Main
    // thread uses this to suppress future codec-exclusion requests for the
    // same codec category.
    onCodecProven(streamId: string, codec: string): void;
}
