// Player worker host. Wires the new Services/Video pipeline to:
//  - the actuallab-rpc transport (so the main thread can call our
//    RecorderWorker / PlayerWorker contracts);
//  - production hook factories (real `streamingApi.liveVideoStreams.GetStream`,
//    real `VideoDecoder`, real MSTG / canvas backend selection).
//
// Imported lazily from `player-worker-bootstrap.ts` so that worker
// logging is initialized before any of these modules run.

import { rpcClientServer } from 'rpc';
import { Api, streamingApi } from 'api';
import { initAppConstants, type AppConstants } from 'app-constants';
import { getLogs } from 'logging';
import { WorkerConnectivityUI } from '../../../Components/AudioRecorder/workers/worker-connectivity-ui';
import {
    __setPlayerWorkerHooks,
    playerWorkerImpl,
} from './player-worker';
import type {
    PlayerWorkerCallbacks,
    PlayerWorkerOptions,
} from './player-worker-contract';
import type { DecoderLike } from '../operators/decode';
import type { VideoFrameDto } from '../operators/pull';
import type { PlayerConfig } from './player';
import { pickRenderBackend, type RenderBackendConfig } from './render-backends';

const { infoLog, warnLog } = getLogs('VideoPipeline');

/** Session token used by `GetStream` — `'~'` resolves to `Session.Default`
 *  on the server (the same trick the legacy worker uses). */
const RPC_SESSION_DEFAULT = '~';

// ---- Worker-local Fusion RPC bootstrap ---------------------------------

/** WS URL for the worker's Fusion RPC peer. Populated by `prewarmRpc`
 *  (called from main thread immediately after the worker is constructed)
 *  or, as a fallback, by the first `start()` if prewarm was missed. */
let pullApiUrl: string | null = null;
let pullApiInitialized = false;

/**
 * Lazily initialise `Api` for the playback worker so
 * `streamingApi.liveVideoStreams.GetStream` has a peer to dial.
 *
 * Mirrors the recorder side's `ensureRpcPush` (in push-to-pull-buffer.ts):
 *  - First call: `Api.init('VideoPlayer', {...}) + Api.requireConnection`.
 *  - Subsequent calls: no-op (Api.init silently drops options when the
 *    hub already exists, so we re-arm `requireConnection` defensively).
 */
function ensurePullApi(): void {
    if (pullApiInitialized) return;
    if (!pullApiUrl)
        throw new Error(
            'PlayerWorkerHost: pullApiUrl is not set — '
            + 'prewarmRpc must be called before start()');
    Api.init('VideoPlayer', {
        url: pullApiUrl,
        modules: [streamingApi],
        connectivityUI: WorkerConnectivityUI,
        sessionTokenProvider: minLifespanMs => callbacks.getSessionToken(minLifespanMs),
        requireConnection: true,
    });
    // `Api.init` silently drops `requireConnection: true` when the hub
    // already exists (e.g. a prior init from another module on the same
    // worker realm). Re-add the scope explicitly so `GetStream`'s
    // `AwaitForConnection` resolves.
    Api.requireConnection('VideoPlayer');
    pullApiInitialized = true;
    infoLog?.log(`ensurePullApi: Api initialized for ${pullApiUrl}`);
}

// ---- Production hook factories ------------------------------------------

/**
 * Production binding for the receiver's pull source. Defers to
 * `streamingApi.liveVideoStreams.GetStream`, which returns an
 * `AsyncIterable<VideoFrameDto>` directly — no adapter needed. The
 * `pullSource` operator wraps it in `exclusive` + `finalize`, so a
 * fresh `getStream(...)` call per pipeline run is what's expected here.
 */
function getStream(streamId: string): Promise<AsyncIterable<VideoFrameDto>> | AsyncIterable<VideoFrameDto> {
    ensurePullApi();
    return streamingApi.liveVideoStreams.GetStream(RPC_SESSION_DEFAULT, streamId);
}

/**
 * Production binding for the decoder factory. The `decode` operator
 * calls this once per pipeline run; we hand back a real `VideoDecoder`
 * with the operator's `onFrame` / `onError` callbacks plumbed into the
 * native `output` / `error` hooks.
 *
 * The operator owns the configure / decode / close lifecycle — we only
 * have to plug the constructor's callbacks here.
 */
function createDecoder(
    _codec: string,
    handlers: { onFrame: (frame: VideoFrame) => void; onError: (e: Error) => void },
): DecoderLike {
    return new VideoDecoder({
        output: handlers.onFrame,
        error: handlers.onError,
    }) as DecoderLike;
}

/**
 * Production binding for backend selection. The worker prefers MSTG
 * when the platform exposes `MediaStreamTrackGenerator` in its global
 * scope; otherwise the caller is expected to ship a canvas context via
 * a side channel (TODO once the main-thread cut-over lands).
 *
 * The Phase 7 cut-over wires the `OffscreenCanvas` and / or MSTG
 * writer per-stream via the worker bootstrap message; for now this
 * function constructs an MSTG locally and returns the corresponding
 * config. Canvas fallback is not yet supported — callers running on
 * Safari / non-MSTG environments must fall back to the legacy worker
 * until follow-up work lands.
 */
interface MstgGlobal {
    MediaStreamTrackGenerator?: new (init: { kind: 'video' }) => MediaStreamTrack & {
        readonly writable: WritableStream<VideoFrame>;
    };
}

/**
 * Build a {@link RenderBackendConfig} per `start()` request:
 *
 *  - `backend === 'mstg'` constructs a `MediaStreamTrackGenerator`
 *    locally; `onTrackReady` (TODO callback) ships the resulting track
 *    back to the main thread for `<video srcObject>`.
 *
 *  - `backend === 'canvas'` requires the caller to transfer an
 *    `OffscreenCanvas` via `opts.canvas`. We grab a 2D context here
 *    (Safari needs `convertToBitmap` because Webkit can't `drawImage`
 *    a `VideoFrame` directly — only `<video>` works there).
 *
 *  - Anything else fails fast with a clear error.
 */
function buildBackend(
    opts: PlayerWorkerOptions,
): { config: RenderBackendConfig; track: MediaStreamTrack | null } {
    if (opts.backend === 'mstg') {
        // Tier 2: main supplied a WritableStream from a main-side
        // MSTG. Use it directly — main already attached the track to
        // <video srcObject>, so we don't need to fire onTrackReady.
        // This is the live path on Chromium (worker globals don't
        // expose MSTG; main globals do).
        if (opts.mstgWritable) {
            const writer = opts.mstgWritable.getWriter();
            return {
                config: pickRenderBackend({ preferMstg: true, mstgWriter: writer }),
                track: null,
            };
        }
        // Tier 1: try worker-side MSTG (Safari workers, future Chromium
        // builds that expose MSTG to dedicated workers).
        const g = globalThis as unknown as MstgGlobal;
        if (typeof g.MediaStreamTrackGenerator !== 'function') {
            throw new Error(
                'PlayerWorkerHost: backend=\'mstg\' requested but neither '
                + '`opts.mstgWritable` (Tier 2) nor a worker-side '
                + 'MediaStreamTrackGenerator (Tier 1) is available. '
                + 'Caller should retry with backend=\'canvas\'.');
        }
        const generator = new g.MediaStreamTrackGenerator({ kind: 'video' });
        const writer = generator.writable.getWriter();
        return {
            config: pickRenderBackend({ preferMstg: true, mstgWriter: writer }),
            track: generator,
        };
    }
    // Canvas branch — guard via type assertion since the union has only
    // two variants and TS narrows after the 'mstg' return.
    {
        if (!opts.canvas) {
            throw new Error(
                'PlayerWorkerHost: backend=\'canvas\' requires '
                + 'opts.canvas to be a transferred OffscreenCanvas.');
        }
        const ctx = opts.canvas.getContext('2d');
        if (!ctx) {
            throw new Error(
                'PlayerWorkerHost: canvas getContext(\'2d\') returned null '
                + '(the canvas is already attached to a different context, '
                + 'or 2D contexts are unavailable in this worker).');
        }
        // Safari path: `ctx.drawImage(VideoFrame, ...)` is unsupported in
        // WebKit; promote each frame to ImageBitmap first. Feature-detect
        // via the global `createImageBitmap` — present everywhere modern.
        const isSafari = /^((?!chrome|android).)*safari/i.test(
            (globalThis as { navigator?: { userAgent?: string } }).navigator?.userAgent ?? '');
        const convertToBitmap = isSafari
            ? (frame: VideoFrame): Promise<ImageBitmap> => createImageBitmap(frame)
            : undefined;
        return {
            config: pickRenderBackend({
                preferMstg: false,
                canvasCtx: ctx,
                convertToBitmap,
            }),
            track: null,
        };
    }
}

/**
 * Stream-scoped latency reporter — forwarded through the RPC callback
 * surface to the main thread. The worker creates one per `start()` so
 * the streamId travels with each sample without needing per-call
 * parameters in the operator.
 */
function makeReportLatency(
    callbacks: PlayerWorkerCallbacks,
    streamId: string,
): NonNullable<PlayerConfig['reportLatency']> {
    return sample => { callbacks.onLatencyReport(streamId, sample); };
}

// ---- Wire-up ------------------------------------------------------------

const callbacks = rpcClientServer<PlayerWorkerCallbacks>(
    'PlayerWorker',
    self as unknown as Worker,
    playerWorkerImpl,
);

__setPlayerWorkerHooks({
    getStream,
    createDecoder,
    buildBackend,
    onTrackReady: (streamId, kind, track) => callbacks.onTrackReady(streamId, kind, track),
    makeReportLatency: streamId => makeReportLatency(callbacks, streamId),
    initAppConstants(appConstants) {
        initAppConstants(appConstants as AppConstants);
    },
    reportError: (streamId, error) => callbacks.onError(streamId, error),
    reportStreamEnded: (streamId, reason) => callbacks.onStreamEnded(streamId, reason),
    prewarmRpc(apiUrl) {
        // Cache the URL and trigger the Api init eagerly so the
        // WebSocket handshake overlaps the rest of player setup. Main
        // thread fires this with rpcNoWait right after constructing the
        // worker, so any error here must NOT escape — log + bail.
        pullApiUrl = apiUrl;
        try {
            ensurePullApi();
        } catch (e) {
            warnLog?.log('prewarmRpc: ensurePullApi failed:', e);
        }
    },
});

infoLog?.log('Player worker host initialized');
