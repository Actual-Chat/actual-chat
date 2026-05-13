// Recorder-worker RPC implementation. Owns one SenderSession + one Recorder
// across stop/start; behaviour lives in those two classes.

import { sharedSettingsWorker } from 'shared-settings-worker';
import { getLogs } from 'logging';
import { createEmptyRecorderStats, type RecorderStats } from '../frame-envelopes';
import { WorkerConnectivityUI } from '../../../Components/AudioRecorder/workers/worker-connectivity-ui';
import type { DownscalerLike } from '../operators/downscale';
import type { FloodGate } from '../operators/flood-gate';
import type { StreamSenderLike } from '../operators/wire-send';
import { Recorder } from './recorder';
import type { EncodedFrame } from '../frame-envelopes';
import type { EncodeInput } from '../operators/encode';
import type { AsyncVideoEncoder } from '../adapters';
import { setVideoTraceKill, type VideoTraceKillPeriod } from '../frame-drop-trace';
import { SenderSession } from './session';
import type {
    RecorderWorker,
    RecorderWorkerOptions,
} from './recorder-worker-contract';

const { errorLog, infoLog } = getLogs('VideoPipeline');

// Production-side dependencies injected by the bootstrap.
// Members ordered by pipeline flow:
//   init  → session  → source  → pipeline-stage factories (downscale → encode → wireSend)
//   → lifecycle callbacks.
export interface RecorderWorkerDeps {
    // -- init / configuration --
    initAppConstants?: (appConstants: import('./recorder-worker-contract').AppConstantsLike) => void;
    configureStreaming?: (opts: {
        chatId: string;
        apiUrl: string;
        sourceKind?: number;
        serverClockOffsetMs?: number;
    }) => void;
    createSession?: () => SenderSession;

    // -- source (capture → mstpSource) --
    getTrack: () => MediaStreamTrack;
    createProcessor?: (track: MediaStreamTrack) => { readable: ReadableStream<VideoFrame> };
    setSource?: (readable: ReadableStream<VideoFrame>) => void;
    pushFrame?: (frame: VideoFrame) => void;
    endSource?: () => void;

    // -- pipeline-stage factories, in sender pipeline order --
    createDownscaler?: () => DownscalerLike;
    // Always returns a FRESH AsyncVideoEncoder. We deliberately don't pool
    // encoders: a pool-reused encoder can emit a delta as its first chunk
    // after reset, which the wire/server then mis-classify; a brand-new
    // encoder's first chunk is guaranteed to be a keyframe.
    createEncoder: (
        session: SenderSession,
        config: import('../operators/encode').EncoderConfigPerLayer,
        layerId: number,
    ) => AsyncVideoEncoder<EncodeInput, EncodedFrame>;
    createSender: (chatId: string, floodGate: FloodGate) => StreamSenderLike;

    // -- lifecycle callbacks --
    reportError?: (error: string) => void;
    reportStreamEnded?: (reason: string) => void;
    reportTraceKillInjected?: () => void;
}

interface WorkerState {
    session: SenderSession;
    recorder: Recorder;
    whenDone: Promise<void> | null;
    deps: RecorderWorkerDeps;
}

let state: WorkerState | null = null;

// Idempotent: subsequent calls reuse the existing session so the
// capture clock survives across runs.
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

// Built fresh per call so callers can't mutate a shared singleton.
function emptyStats(): RecorderStats {
    return createEmptyRecorderStats();
}

// Method order matches the RecorderWorker interface contract:
//   init → connectivity → source → run → query → stop → dispose.
export const recorderWorkerImpl: RecorderWorker = {
    init(appConstants: import('./recorder-worker-contract').AppConstantsLike): Promise<void> {
        const s = requireState();
        s.deps.initAppConstants?.(appConstants);
        return Promise.resolve();
    },

    updateSharedSettings: (settings, noWait) => sharedSettingsWorker.updateSharedSettings(settings, noWait),

    async onConnectivityUpdate(
        isOnline: boolean,
        isConnected: boolean,
        isBlazorServer: boolean,
    ): Promise<void> {
        WorkerConnectivityUI.update(isOnline, isConnected, isBlazorServer);
        await Promise.resolve();
    },

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

    async start(opts: RecorderWorkerOptions): Promise<void> {
        const s = requireState();
        if (s.whenDone)
            throw new Error('RecorderWorker: already running — call stop() first');

        const { config } = opts;
        const { deps, recorder, session } = s;
        // Streaming context must land before the pipeline starts so
        // `createWireSender` finds it on the first encoded chunk.
        deps.configureStreaming?.({
            chatId: config.chatId,
            apiUrl: config.apiUrl,
            sourceKind: config.sourceKind,
            serverClockOffsetMs: config.serverClockOffsetMs,
        });
        const track = deps.getTrack();
        const encoderFactory: import('./recorder').RecorderConfig['createEncoder'] = (
            layerCfg,
            layerId,
        ) => {
            // Fresh encoder per run: see `createEncoder` in RecorderWorkerDeps.
            // encode() disposes it in its own `finally`.
            const encoder = deps.createEncoder(session, layerCfg, layerId);
            try {
                encoder.configure({
                    codec: layerCfg.codec,
                    width: layerCfg.width,
                    height: layerCfg.height,
                    bitrate: layerCfg.bitrate,
                    framerate: layerCfg.framerate,
                    latencyMode: 'realtime',
                });
            } catch (e) {
                try { encoder.dispose(); } catch { /* ignore */ }
                throw e;
            }
            return encoder;
        };

        const senderFactory = (gate: FloodGate): StreamSenderLike => deps.createSender(config.chatId, gate);

        const whenDone = recorder.start({
            track,
            createProcessor: deps.createProcessor,
            createDownscaler: deps.createDownscaler,
            encoderConfigs: config.encoderConfigs,
            createEncoder: encoderFactory,
            keyframeIntervalFrames: config.keyframeIntervalFrames,
            maxKeyFrameIntervalMs: config.maxKeyFrameIntervalMs,
            createSender: senderFactory,
        });
        s.whenDone = whenDone;
        // RPC `start()` resolves once the pipeline is wired up; the run
        // drains in the background.
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
            if (s.whenDone === whenDone) s.whenDone = null;
        });
        await Promise.resolve();
    },

    setTraceKill(avgPeriod: VideoTraceKillPeriod, stage: number): Promise<boolean> {
        const s = requireState();
        return Promise.resolve(setVideoTraceKill(
            'recording',
            avgPeriod,
            stage,
            () => s.deps.reportTraceKillInjected?.()));
    },

    async requestKeyframe(): Promise<void> {
        const s = requireState();
        s.recorder.requestKeyframe();
        await Promise.resolve();
    },

    getStats(): Promise<RecorderStats> {
        const s = requireState();
        return Promise.resolve(s.recorder.getStats() ?? emptyStats());
    },

    async stop(): Promise<void> {
        const s = requireState();
        const run = s.whenDone;
        s.recorder.stop();
        if (run)
            await run;
    },

    async disconnectApi(): Promise<void> {
        // No-op today: the new pipeline lazy-creates the peer per stream,
        // so there's no long-lived peer to disconnect.
        await Promise.resolve();
    },
};
