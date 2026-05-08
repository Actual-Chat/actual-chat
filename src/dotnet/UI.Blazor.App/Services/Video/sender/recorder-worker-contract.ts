// RPC contract for the recorder worker.
//
// Pure types — no `actuallab-rpc` dependency: the bootstrap that wires
// the worker to the host owns the RPC plumbing, while this file is
// kept minimal so it can be shared between the main thread and the
// worker without dragging in transport machinery.
//
// The shape mirrors what the production main thread (today's
// `recording-service.ts`) calls into the worker: `start` /
// `stop` / `requestKeyframe` / `getStats`, plus a `callbacks`
// surface for events the worker pushes back (errors, stream
// lifecycle).
//
// `WireSafeRecorderConfig` is the structurally-cloneable subset of
// {@link RecorderConfig} — everything that survives a `postMessage`.
// Non-clonable collaborators (encoder factory, sender factory,
// downscaler factory) are constructed inside the worker from this
// config plus the {@link SenderSession} and don't appear here.

import type { RpcNoWait } from 'rpc';
import type { EncoderConfigPerLayer } from '../operators/encode';
import type { VideoRecordingStats } from '../frame-envelopes';
import type { SharedSettingsWorker } from 'shared-settings-worker';

/**
 * Wire-safe subset of recorder configuration. Everything here must
 * round-trip through `structuredClone` — no closures, no `MediaStream`
 * references. The worker fills in non-clonable bits (track, encoder
 * factory, sender factory) from its own context.
 */
export interface WireSafeRecorderConfig {
    /** Chat / stream id this recording is bound to. The worker uses
     *  it to construct the matching RPC sender. */
    chatId: string;
    /** Fusion RPC WebSocket URL (e.g. `wss://local.voxt.ai/rpc/ws`).
     *  Required: the worker's `streamingContext` is populated from
     *  this on `start()` so `PushStream` can dial the right peer. */
    apiUrl: string;
    /** Stream kind: 0 = Camera, 1 = ScreenCast. Maps to .NET
     *  `.NET VideoSourceKind`. Default 0. */
    sourceKind?: number;
    /** Server clock offset in milliseconds, captured on the main
     *  thread for sender-side latency math. Default 0. */
    serverClockOffsetMs?: number;
    /** Bottom-first simulcast ladder (same shape as
     *  {@link RecorderConfig.encoderConfigs}). */
    encoderConfigs: readonly EncoderConfigPerLayer[];
    /** Frame-count keyframe interval (must be > 0). */
    keyframeIntervalFrames: number;
    /** Optional wallclock floor (ms) for the keyframe policy. */
    maxKeyFrameIntervalMs?: number;
}

/**
 * Inputs to {@link RecorderWorker.start}.
 *
 * `sourceStartedAtMs` is the main-thread-side wallclock anchor
 * (`Date.now()` at the moment the user pressed "record"). The worker
 * threads it through to diagnostics so reset events can be expressed
 * in source-relative time.
 */
export interface RecorderWorkerOptions {
    sourceStartedAtMs: number;
    config: WireSafeRecorderConfig;
}

/**
 * RPC surface the main thread calls. Each method returns a Promise so
 * the underlying transport can serialize completion / errors back.
 */
/**
 * Subset of `AppConstants` that the worker actually needs for the new
 * pipeline. Re-declared here as a structural shape so the contract
 * stays decoupled from the full type — anything assignable to
 * `AppConstants` satisfies this. */
export interface AppConstantsLike { readonly appName: string; readonly prodHost: string; readonly video: unknown; readonly audio: unknown }

export interface RecorderWorker extends SharedSettingsWorker {
    /** Initialise worker-local app constants (video/audio limits,
     *  RPC stream tuning). First call wins; subsequent calls no-op.
     *  MUST be called before {@link start}; the streaming-glue layer
     *  reads `VIDEO.rpcStreamAckPeriod` etc. from this. */
    init(appConstants: AppConstantsLike): Promise<void>;
    /** Land a transferred `ReadableStream<VideoFrame>` (constructed
     *  on the main thread from `MediaStreamTrackProcessor.readable`)
     *  in the worker's pending-source slot. Must be called before
     *  each {@link start}. The host's `createProcessor` factory
     *  hands this readable back to the recorder pipeline as the
     *  source iterator.
     *
     *  Why a stream rather than the track itself: Chromium doesn't
     *  honor `postMessage(track, [track])` (only Safari 18+ does); the
     *  readable stream is universally transferable across realms.
     *
     *  KNOWN LIMITATION: Chrome's MSTP-readable cross-realm bridge
     *  starves the consumer after a fixed small number of frames.
     *  Production callers prefer {@link pushFrame} + {@link endSource}
     *  which transfer individual `VideoFrame`s instead — see those
     *  methods. `setSource` is retained for tests that synthesize a
     *  ReadableStream directly. */
    setSource(readable: ReadableStream<VideoFrame>): Promise<void>;
    /**
     * Push a single `VideoFrame` from main into the worker's source
     * queue. Frame ownership is transferred via the trailing-args
     * convention in `rpc.ts` (VideoFrame is a `Transferable`). The
     * worker enqueues the frame for the recorder pipeline's
     * `mstpSource` operator to consume.
     *
     * Workaround for the readable-transfer cross-realm bridge bug:
     * main reads frames from rVFC and ferries them one-by-one. Callers
     * should keep at most one push in flight; otherwise transferred
     * VideoFrames can accumulate in the worker message queue before the
     * worker-side latest-frame slot gets a chance to close them.
     */
    pushFrame(frame: VideoFrame, noWait?: RpcNoWait): Promise<void>;
    /**
     * Signal that no more `pushFrame` calls will arrive (the main
     * reader hit `done` or was cancelled). The worker's source
     * iterator returns after draining its queue.
     */
    endSource(noWait?: RpcNoWait): Promise<void>;
    /** Start a fresh run. Rejects if a run is already in flight or if
     *  the configuration is invalid. */
    start(opts: RecorderWorkerOptions): Promise<void>;
    /** Trigger a clean shutdown of the current run. Resolves once
     *  the pipe has finished draining. No-op when no run is active. */
    stop(): Promise<void>;
    /** Force the next encoded frame to be a keyframe. Useful for
     *  PLI handling (server asks for a refresh). */
    requestKeyframe(): Promise<void>;
    /** Snapshot the current run's stats (or empty stats when no run
     *  has started yet). */
    getStats(): Promise<VideoRecordingStats>;
    /** Mirrors `ConnectivityUI.isOnline` / `isConnected` /
     *  `isBlazorServer` into the worker so its RPC peer mirror can
     *  honor the connectivity gate. */
    onConnectivityUpdate(isOnline: boolean, isConnected: boolean, isBlazorServer: boolean): Promise<void>;
    /** Debug-only: force-remove the worker's RPC peer from the hub.
     *  No-op for now — there is no hub-managed peer in the new
     *  pipeline. The reconnect loop is handled by the underlying
     *  transport (StreamingApi). */
    disconnectApi(): Promise<void>;
}

/**
 * Events the worker pushes back to the main thread. Implementations
 * are wired into the bootstrap that hosts the worker.
 */
export interface RecorderWorkerCallbacks {
    /** Fatal error — the current run has been torn down. */
    onError(error: string): void;
    /** A streaming session has been created on the wire. The
     *  `codecSettings` string carries the codec descriptor the
     *  receiver needs to bootstrap its decoder. */
    onStreamCreated(codecSettings: string): void;
    /** A streaming session has been ended (clean stop, encoder
     *  failure, server-initiated close). */
    onStreamEnded(reason: string): void;
}
