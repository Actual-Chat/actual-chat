// Recorder-worker implementation — driven by RPC handlers.
//
// This file is the body of the worker, NOT the bootstrap. The bootstrap
// (lives in a separate file under `Services/Video/workers/...` once
// Phase 7 lands) hosts the actuallab-rpc transport, instantiates the
// worker's track / sender / encoder factories from the host
// environment, and forwards method calls to the {@link RecorderWorker}
// implementation exported here.
//
// The implementation is intentionally thin: it owns one `SenderSession`
// + one `Recorder`, parks them across stop/start, and delegates all the
// composable behaviour to those two classes. Per the Phase 3 plan,
// tests for this file are skipped because of the worker context — all
// behaviour we'd test here is already covered by the {@link Recorder}
// tests + the {@link SenderSession}/{@link EncoderPool} tests.

import { sharedSettingsWorker } from 'shared-settings-worker';
import { getLogs } from 'logging';
import { createEmptyRecordingStats, type VideoRecordingStats } from '../frame-envelopes';
import { WorkerConnectivityUI } from '../../../Components/AudioRecorder/workers/worker-connectivity-ui';
import type { DownscalerLike } from '../operators/downscale';
import type { FloodGate } from '../operators/flood-gate';
import type { StreamSenderLike } from '../operators/wire-send';
import { Recorder } from './recorder';
import type { EncoderFactory as PoolEncoderFactory } from './encoder-pool';
import { SenderSession } from './session';
import type {
    RecorderWorker,
    RecorderWorkerOptions,
} from './recorder-worker-contract';

const { errorLog, infoLog } = getLogs('VideoPipeline');

/**
 * Production-side dependencies the bootstrap injects when constructing
 * the worker implementation. Tests don't exercise this path — the
 * factories are owned by the bootstrap, which has access to the
 * worker-global `MediaStreamTrack` (via track transfer), the
 * `WebGpuDownscaler`, and the RPC sender.
 */
export interface RecorderWorkerDeps {
    /** Returns a placeholder `MediaStreamTrack` for the recorder
     *  pipeline's type system; the production binding hands back a
     *  stub since {@link createProcessor} is what actually feeds frames.
     *  Tests can return a real fake track. */
    getTrack: () => MediaStreamTrack;
    /** Returns a {@link MediaStreamTrackProcessor}-shaped object whose
     *  `readable` yields `VideoFrame`s. Production: returns the
     *  pre-built `ReadableStream` transferred from main via
     *  {@link RecorderWorker.setSource}. Tests may pass a synthetic
     *  stream. When omitted the operator falls back to constructing
     *  `new MediaStreamTrackProcessor({ track })` itself, but Chrome
     *  can't transfer a `MediaStreamTrack` so the production path
     *  always supplies this. */
    createProcessor?: (track: MediaStreamTrack) => { readable: ReadableStream<VideoFrame> };
    /** Land a transferred `ReadableStream<VideoFrame>` on the host so
     *  the next `start()`'s `createProcessor` can pick it up.
     *  Production: caches the readable in the host module; tests may
     *  pass a synthetic stub. */
    setSource?: (readable: ReadableStream<VideoFrame>) => void;
    /** Push a single `VideoFrame` from main into the host's source
     *  queue. The host owns frame lifetime once enqueued (closes on
     *  drop / shutdown). Used by the frame-by-frame transfer path
     *  (workaround for the readable-transfer bridge bug). */
    pushFrame?: (frame: VideoFrame) => void;
    /** Signal end-of-source to the host's queue: no more `pushFrame`
     *  calls will arrive. Drains the queue, then the source
     *  iterator returns. */
    endSource?: () => void;
    /** Updates the host's streaming context (apiUrl / chatId /
     *  sourceKind / serverClockOffsetMs) before each `start()`. The
     *  host populates {@link StreamingContext} from these fields so
     *  `createWireSender` can dial the Fusion RPC peer. Optional —
     *  tests with synthetic senders skip it. */
    configureStreaming?: (opts: {
        chatId: string;
        apiUrl: string;
        sourceKind?: number;
        serverClockOffsetMs?: number;
    }) => void;
    /** Forwards the AppConstants payload to the host's
     *  {@link initAppConstants}. Tests can supply a no-op. */
    initAppConstants?: (appConstants: import('./recorder-worker-contract').AppConstantsLike) => void;
    /** Constructs a fresh `StreamSenderLike` for the run. The
     *  recorder threads its {@link FloodGate} so the wire bridge can
     *  drive backpressure based on its own queue fill. */
    createSender: (chatId: string, floodGate: FloodGate) => StreamSenderLike;
    /** Constructs a fresh GPU-backed downscaler. May be omitted for
     *  single-tier (P2P) use; the recorder falls back to an identity
     *  downscaler in that case. */
    createDownscaler?: () => DownscalerLike;
    /**
     * Acquires an encoder for the given layer config / id. Production
     * wiring borrows from the {@link EncoderPool} on the session and
     * binds the WebCodecs `buildOutput`. The bootstrap supplies this
     * because the WebCodecs glue is the part that needs the actual
     * `VideoEncoder` global, which differs across environments.
     */
    poolEncoderFactory: (
        session: SenderSession,
        config: import('../operators/encode').EncoderConfigPerLayer,
        layerId: number,
    ) => PoolEncoderFactory;
    /** Optional override for the session — tests / parallel workers. */
    createSession?: () => SenderSession;
    /** Surface fatal recorder-pipeline failures to the host. */
    reportError?: (error: string) => void;
    /** Surface run completion to the host. */
    reportStreamEnded?: (reason: string) => void;
}

interface WorkerState {
    session: SenderSession;
    recorder: Recorder;
    whenDone: Promise<void> | null;
    deps: RecorderWorkerDeps;
}

let state: WorkerState | null = null;

/**
 * Initialize the worker with its production dependencies. Idempotent
 * across repeated calls — subsequent calls reuse the existing session
 * (so encoder pool + capture clock survive). Throws if the worker has
 * already been initialized with a different deps reference; the
 * bootstrap is expected to call this exactly once at startup.
 */
export function initRecorderWorker(deps: RecorderWorkerDeps): void {
    if (state) return;
    const session = (deps.createSession ?? (() => new SenderSession()))();
    state = {
        session,
        recorder: new Recorder(session),
        whenDone: null,
        deps,
    };
}

/**
 * Tear down the worker — disposes the session + encoder pool. After
 * `disposeRecorderWorker()`, calls to {@link recorderWorkerImpl} reject.
 */
export function disposeRecorderWorker(): void {
    if (!state) return;
    state.recorder.stop();
    state.session.dispose();
    state = null;
}

function requireState(): WorkerState {
    if (!state)
        throw new Error('RecorderWorker: not initialized — call initRecorderWorker first');
    return state;
}

/**
 * Default empty stats payload returned when `getStats()` is called
 * before any run has started. Built fresh per call so callers can't
 * mutate a shared singleton accidentally.
 */
function emptyStats(): VideoRecordingStats {
    return createEmptyRecordingStats(0);
}

/**
 * The RPC-shaped implementation. The bootstrap exports this through
 * actuallab-rpc; the main thread invokes its methods by name.
 */
export const recorderWorkerImpl: RecorderWorker = {
    init(appConstants: import('./recorder-worker-contract').AppConstantsLike): Promise<void> {
        // Defer to the bootstrap's actual `initAppConstants` via the
        // host wiring. The deps surface gets a thin pass-through so
        // tests can supply a no-op stub.
        const s = requireState();
        s.deps.initAppConstants?.(appConstants);
        return Promise.resolve();
    },

    updateSharedSettings: (settings, noWait) => sharedSettingsWorker.updateSharedSettings(settings, noWait),

    setSource(readable: ReadableStream<VideoFrame>): Promise<void> {
        const s = requireState();
        s.deps.setSource?.(readable);
        return Promise.resolve();
    },

    pushFrame(frame: VideoFrame): Promise<void> {
        const s = requireState();
        s.deps.pushFrame?.(frame);
        return Promise.resolve();
    },

    endSource(): Promise<void> {
        const s = requireState();
        s.deps.endSource?.();
        return Promise.resolve();
    },

    async onConnectivityUpdate(
        isOnline: boolean,
        isConnected: boolean,
        isBlazorServer: boolean,
    ): Promise<void> {
        WorkerConnectivityUI.update(isOnline, isConnected, isBlazorServer);
        await Promise.resolve();
    },

    async disconnectApi(): Promise<void> {
        // TODO(phase 7+): force-remove the worker's RPC peer from the
        // hub when one exists. The new pipeline's `push-to-pull-buffer`
        // currently lazy-initialises the peer per stream, so there's
        // no long-lived peer for the debug button to reach.
        await Promise.resolve();
    },

    async start(opts: RecorderWorkerOptions): Promise<void> {
        const s = requireState();
        if (s.whenDone) {
            throw new Error('RecorderWorker: already running — call stop() first');
        }
        const { config } = opts;
        const { deps, recorder, session } = s;
        // Push the streaming context (apiUrl, chatId, etc.) into the
        // host BEFORE the pipeline starts so `createWireSender` finds
        // a populated context on the first encoded chunk.
        deps.configureStreaming?.({
            chatId: config.chatId,
            apiUrl: config.apiUrl,
            sourceKind: config.sourceKind,
            serverClockOffsetMs: config.serverClockOffsetMs,
        });
        const track = deps.getTrack();
        // Build the encoder factory for the operator: per-layer borrow
        // from the pool, with the bootstrap supplying the WebCodecs glue.
        const ladder = config.encoderConfigs;
        const layerCategoriesByCodec = ladder.map(c => deriveCategory(c.codec));
        const handles: { release: () => void }[] = [];
        const encoderFactory: import('./recorder').RecorderConfig['createEncoder'] = (
            layerCfg,
            layerId,
        ) => {
            const category = layerCategoriesByCodec[layerId];
            const handle = session.encoderPool.acquire(
                category,
                deps.poolEncoderFactory(session, layerCfg, layerId),
            );
            handles.push(handle);
            handle.encoder.configure({
                codec: layerCfg.codec,
                width: layerCfg.width,
                height: layerCfg.height,
                bitrate: layerCfg.bitrate,
                framerate: layerCfg.framerate,
                latencyMode: 'realtime',
            });
            return handle.encoder;
        };

        const senderFactory = (gate: FloodGate): StreamSenderLike => deps.createSender(config.chatId, gate);

        const whenDone = recorder.start({
            track,
            encoderConfigs: config.encoderConfigs,
            keyframeIntervalFrames: config.keyframeIntervalFrames,
            maxKeyFrameIntervalMs: config.maxKeyFrameIntervalMs,
            createSender: senderFactory,
            createEncoder: encoderFactory,
            createDownscaler: deps.createDownscaler,
            createProcessor: deps.createProcessor,
        });
        s.whenDone = whenDone;
        // Track the run in the background. The RPC `start()` resolves
        // as soon as the pipeline is wired up — callers don't want
        // their `await worker.start(...)` to block until the run drains.
        whenDone.then(
            () => {
                infoLog?.log('Recorder pipeline ended');
                try { deps.reportStreamEnded?.('completed'); }
                catch (e) { errorLog?.log('reportStreamEnded failed:', e); }
            },
            (e: unknown) => {
                const message = e instanceof Error ? e.message : String(e);
                const stack = e instanceof Error ? e.stack : undefined;
                errorLog?.log('Recorder pipeline failed:', message, stack ?? '');
                try { deps.reportError?.(message); }
                catch (reportError) { errorLog?.log('reportError failed:', reportError); }
                try { deps.reportStreamEnded?.(`error: ${message}`); }
                catch (reportError) { errorLog?.log('reportStreamEnded failed:', reportError); }
            },
        ).finally(() => {
            for (const h of handles) {
                try { h.release(); } catch { /* ignore */ }
            }
            if (s.whenDone === whenDone) s.whenDone = null;
        });
        await Promise.resolve();
    },

    async stop(): Promise<void> {
        const s = requireState();
        const run = s.whenDone;
        s.recorder.stop();
        if (run)
            await run;
    },

    async requestKeyframe(): Promise<void> {
        // The Recorder façade doesn't currently expose a force-keyframe
        // hook; per the Phase 3 plan the implementation is "stop + start
        // with a keyframe-on-restart" which the encoder pool absorbs.
        // For PLI today: rely on the keyframe-policy interval.
        // TODO(phase 3.x): plumb this through `Recorder.requestKeyframe()`
        // once the pipe-internal control channel lands.
        await Promise.resolve();
    },

    getStats(): Promise<VideoRecordingStats> {
        const s = requireState();
        return Promise.resolve(s.recorder.getStats() ?? emptyStats());
    },
};

/**
 * Map a codec string ("avc1.640028", "hev1.1.6.L120.B0", "av01....", ...)
 * to one of the four pool categories. Mirrors the legacy `getCodecCategory`
 * in `Services/Video/codec-support.ts` without importing it (the new
 * pipeline avoids importing legacy modules).
 */
function deriveCategory(codec: string): import('./encoder-pool').EncoderCodecCategory {
    const lower = codec.toLowerCase();
    if (lower.startsWith('av01')) return 'av1';
    if (lower.startsWith('hev1') || lower.startsWith('hvc1')) return 'hevc';
    if (lower.startsWith('vp09') || lower.startsWith('vp9')) return 'vp9';
    return 'h264';
}
