// Player worker host. Wires the Services/Video pipeline to the
// actuallab-rpc transport and to production hook factories (real
// streamingApi.liveVideoStreams.GetStream, real VideoDecoder, real
// MSTG/canvas backend selection). Imported lazily from
// player-worker-bootstrap.ts so worker logging is initialized first.

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
import { createWorkerVideoTrackGenerator, pickRenderBackend, type RenderBackendConfig } from './render-backends';

const { infoLog, warnLog } = getLogs('VideoPipeline');

// '~' resolves to Session.Default on the server (legacy worker trick).
const RPC_SESSION_DEFAULT = '~';

let pullApiUrl: string | null = null;
let pullApiInitialized = false;

// Mirrors the recorder side's ensureRpcPush: first call runs Api.init
// + Api.requireConnection; subsequent calls re-arm requireConnection
// defensively because Api.init silently drops options when the hub
// already exists.
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
    Api.requireConnection('VideoPlayer');
    pullApiInitialized = true;
    infoLog?.log(`ensurePullApi: Api initialized for ${pullApiUrl}`);
}

async function getStream(streamId: string): Promise<AsyncIterable<VideoFrameDto>> {
    ensurePullApi();
    if (!Api.peer.isConnected) {
        infoLog?.log(`getStream: waiting for RPC connection before pulling stream ${streamId}`);
        await Api.peer.whenConnected();
    }

    return streamingApi.liveVideoStreams.GetStream(RPC_SESSION_DEFAULT, streamId);
}

function createDecoder(
    _codec: string,
    handlers: { onFrame: (frame: VideoFrame) => void; onError: (e: Error) => void },
): DecoderLike {
    return new VideoDecoder({
        output: handlers.onFrame,
        error: handlers.onError,
    }) as DecoderLike;
}

function buildBackend(
    opts: PlayerWorkerOptions,
): { config: RenderBackendConfig; track: MediaStreamTrack | null } {
    if (opts.backend === 'mstg') {
        // Tier 2: main supplied a WritableStream from a main-side MSTG.
        // Main already attached the track to <video srcObject>, so we
        // skip onTrackReady. Live path on Chromium (worker globals
        // don't expose MSTG; main globals do).
        if (opts.mstgWritable) {
            const writer = opts.mstgWritable.getWriter();
            return {
                config: pickRenderBackend({ preferMstg: true, mstgWriter: writer }),
                track: null,
            };
        }
        // Tier 1: worker-side generator. Chromium exposes
        // MediaStreamTrackGenerator on the worker; Safari exposes
        // VideoTrackGenerator (different ctor, track lives on `.track`).
        const generator = createWorkerVideoTrackGenerator();
        if (generator) {
            const writer = generator.writable.getWriter();
            return {
                config: pickRenderBackend({ preferMstg: true, mstgWriter: writer }),
                track: generator.track,
            };
        }
        throw new Error(
            'PlayerWorkerHost: backend=\'mstg\' requested but neither '
            + '`opts.mstgWritable` (Tier 2) nor a worker-side '
            + 'MediaStreamTrackGenerator/VideoTrackGenerator (Tier 1) is '
            + 'available. Caller should retry with backend=\'canvas\'.');
    }
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
        // WebKit can't drawImage(VideoFrame, ...); promote each frame
        // to ImageBitmap first.
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

function makeReportLatency(
    callbacks: PlayerWorkerCallbacks,
    streamId: string,
): NonNullable<PlayerConfig['reportLatency']> {
    return sample => { callbacks.onLatencyReport(streamId, sample); };
}

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
    reportCodecProven: (streamId, codec) => callbacks.onCodecProven(streamId, codec),
    reportTraceKillInjected: () => callbacks.onTraceKillInjected(),
    prewarmRpc(apiUrl) {
        // Main fires this with rpcNoWait right after constructing the
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
