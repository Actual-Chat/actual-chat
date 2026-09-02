// Recorder-worker RPC implementation. Owns one SenderSession + one Recorder
// across stop/start; behaviour lives in those two classes.

import { sharedSettingsWorker } from 'shared-settings-worker';
import { WebCodecsCompat, type FrameSource } from 'web-codecs-compat/init';
import { getLogs } from 'logging';
import { createEmptyRecorderStats, type RecorderStats } from '../frame-envelopes';
import { WorkerConnectivityUI } from '../../../Components/AudioRecorder/workers/worker-connectivity-ui';
import type { FloodGate } from '../operators/flood-gate';
import type { StreamSenderLike } from '../operators/wire-send';
import { Recorder } from './recorder';
import type { EncodedFrame } from '../frame-envelopes';
import type { EncodeInput } from '../operators/encode';
import type { AsyncVideoEncoder } from '../adapters';
import { setVideoTraceKill, type VideoTraceKillPeriod } from '../frame-drop-trace';
import { ScreenOrientation, DeviceOrientation } from 'orientation';
import { SenderSession } from './session';
import { getCodecCategory } from '../codec-support';
import { MediaCapture } from '../services/media-capture';
import {
    createWorkerVideoTrackGenerator,
    type WorkerVideoTrackGenerator,
} from '../playback/render-backends';
import type {
    PreviewFramePresentation,
    RecorderWorker,
    RecorderWorkerOptions,
} from './recorder-worker-contract';

// Hydrate orientation in this worker realm via SharedSettings.changed.
// Main thread is the source: it pushes screenOrientation/deviceOrientation
// updates which the existing SharedSettingsWorkerSync relays here.
ScreenOrientation.init();
DeviceOrientation.init();

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
    reportPreviewFrame?: (frame: FrameSource) => void | Promise<void>;
    reportPreviewFramePresentation?: (presentation: PreviewFramePresentation) => void;
    // Ships a worker-created preview track to main (Safari). Null ⇒ no
    // generator available, main falls back to canvas.
    reportPreviewTrack?: (track: MediaStreamTrack | null) => void;
}

interface WorkerState {
    session: SenderSession;
    recorder: Recorder;
    whenDone: Promise<void> | null;
    deps: RecorderWorkerDeps;
    // Set only when the preview generator is created in this worker realm
    // (Safari). Its track is transferred to main; its writable stays here, so
    // the worker owns the lifetime and stops it on run end.
    workerPreviewGenerator: WorkerVideoTrackGenerator | null;
    // Set when capture runs through a worker-side MediaStreamTrackProcessor
    // (Safari): main transfers a clone of the camera track in, the worker owns
    // it and stops it on run end.
    workerSourceTrack: MediaStreamTrack | null;
}

let state: WorkerState | null = null;

// Idempotent: subsequent calls reuse the existing session so the
// capture clock survives across runs.
export function initRecorderWorker(deps: RecorderWorkerDeps): void {
    if (state)
        return;

    const session = (deps.createSession ?? (() => new SenderSession()))();
    session.setPreviewFrameReporter(deps.reportPreviewFrame);
    session.setPreviewFramePresentationReporter(deps.reportPreviewFramePresentation);
    state = {
        session,
        recorder: new Recorder(session),
        whenDone: null,
        deps,
        workerPreviewGenerator: null,
        workerSourceTrack: null,
    };
}

// Stops the worker-created preview generator's track (if any) and clears the
// ref. The track was transferred to main, but stopping it here closes the
// underlying source so the generator's writable can be released.
function disposeWorkerPreviewGenerator(s: WorkerState): void {
    const generator = s.workerPreviewGenerator;
    if (!generator)
        return;

    s.workerPreviewGenerator = null;
    try { generator.track.stop(); } catch { /* ignore */ }
}

// Builds the worker-side generator, hands its writable to the session and ships
// the track to main. `reportPreviewTrack(null)` puts the preview on the canvas
// fallback when the platform has no generator at all.
function installWorkerPreviewGenerator(s: WorkerState): void {
    const generator = createWorkerVideoTrackGenerator();
    s.workerPreviewGenerator = generator;
    s.session.setPreviewGenerator(generator ? { writable: generator.writable } : undefined);
    s.deps.reportPreviewTrack?.(generator?.track ?? null);
}

// Stops the worker-owned capture track (transferred clone) feeding a worker-side
// MSTP, and clears the ref.
function disposeWorkerSourceTrack(s: WorkerState): void {
    const track = s.workerSourceTrack;
    if (!track)
        return;

    s.workerSourceTrack = null;
    try { track.stop(); } catch { /* ignore */ }
}

export function disposeRecorderWorker(): void {
    if (!state)
        return;

    state.recorder.stop();
    disposeWorkerPreviewGenerator(state);
    disposeWorkerSourceTrack(state);
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

    setSourceTrack(track: MediaStreamTrack): Promise<boolean> {
        const s = requireState();
        disposeWorkerSourceTrack(s);
        const Ctor = (globalThis as unknown as {
            MediaStreamTrackProcessor?: new (init: { track: MediaStreamTrack })
                => { readable: ReadableStream<VideoFrame> };
        }).MediaStreamTrackProcessor;
        if (typeof Ctor !== 'function') {
            try { track.stop(); } catch { /* ignore */ }
            return Promise.resolve(false);
        }

        try {
            const processor = new Ctor({ track });
            s.deps.setSource?.(processor.readable);
            s.workerSourceTrack = track;
            infoLog?.log('setSourceTrack: worker-side MSTP built from transferred track');
            return Promise.resolve(true);
        } catch (e) {
            errorLog?.log('setSourceTrack: MSTP construction failed:', e);
            try { track.stop(); } catch { /* ignore */ }
            return Promise.resolve(false);
        }
    },

    async setCaptureFrameRate(fps: number): Promise<boolean> {
        const s = requireState();
        const track = s.workerSourceTrack;
        if (track?.readyState !== 'live')
            return false;

        return MediaCapture.applyFrameRate(track, fps);
    },

    // The frame arrives transferred, so this realm owns it: an absent handler or a throw
    // out of requireState() has to release it rather than drop it on the floor.
    pushFrame(frame: VideoFrame): Promise<void> {
        let isOwned = true;
        try {
            const push = requireState().deps.pushFrame;
            if (push) {
                push(frame);
                isOwned = false;
            }
        } finally {
            if (isOwned)
                try { frame.close(); } catch { /* ignore */ }
        }

        return Promise.resolve();
    },

    endSource(): Promise<void> {
        const s = requireState();
        s.deps.endSource?.();
        return Promise.resolve();
    },

    async start(
        opts: RecorderWorkerOptions,
        previewWritable?: WritableStream<VideoFrame>,
    ): Promise<void> {
        const s = requireState();
        if (s.whenDone)
            throw new Error('RecorderWorker: already running — call stop() first');

        const { config } = opts;
        const { deps, recorder, session } = s;
        disposeWorkerPreviewGenerator(s);
        // A generator takes only native VideoFrames, and at `full` the downscaler
        // builds polyfilled ones — every write fails with "Null video frame". Only
        // `full` swaps the frame class; `vp9` swaps the encoder and leaves frames
        // native, so the generator stays usable there.
        const canUseGenerator = !WebCodecsCompat.isPolyfilledRealm;
        if (previewWritable && canUseGenerator) {
            // Tier 2: main built the generator (Chromium) and transferred only
            // the writable.
            session.setPreviewGenerator({ writable: previewWritable });
        } else if (opts.createPreviewInWorker && canUseGenerator) {
            // Tier 1: main couldn't build a generator (Safari) — create it here
            // and ship the track back. Writable stays in this realm.
            installWorkerPreviewGenerator(s);
        } else {
            session.setPreviewGenerator(undefined);
            // Main leaves the preview pending until a track (or an explicit null)
            // arrives, so refusing the generator has to say so or it waits forever.
            if (opts.createPreviewInWorker)
                deps.reportPreviewTrack?.(null);
        }
        // Streaming context must land before the pipeline starts so
        // `createWireSender` finds it on the first encoded chunk.
        deps.configureStreaming?.({
            chatId: config.chatId,
            apiUrl: config.apiUrl,
            sourceKind: config.sourceKind,
            serverClockOffsetMs: config.serverClockOffsetMs,
        });
        // Before any encoder is constructed: at level `vp9` the class it gets
        // depends on libav.js being loaded, and awaiting here is what loads it.
        await WebCodecsCompat.whenReadyFor('video-encode');
        const track = deps.getTrack();
        const encoderFactory: import('./recorder').RecorderConfig['createEncoder'] = (
            layerCfg,
            layerId,
        ) => {
            // Acquire from session's encoder pool — park-after-release across
            // recording restarts avoids burning HW encoder slots (NVENC has a
            // bounded concurrent-session limit on consumer GPUs). On a pool
            // hit, the encoder is reset (queue cleared) but the codec config
            // is set by configure() below.
            const category = getCodecCategory(layerCfg.codec);
            const handle = session.encoderPool.acquire(
                category,
                () => deps.createEncoder(session, layerCfg, layerId),
            );
            try {
                // hardwareAcceleration comes from the WireSafeRecorderConfig;
                // defaults to 'prefer-hardware' for the normal multi-tier
                // path. The 1-tier last-resort fallback flips this to
                // 'no-preference' so the browser can pick SW when HW
                // activation is failing (AMD iGPU + Windows MFT pressure
                // produces "Not enough memory resources" on HW encoder
                // activation).
                const encoderConfig: VideoEncoderConfig = {
                    codec: layerCfg.codec,
                    width: layerCfg.width,
                    height: layerCfg.height,
                    bitrate: layerCfg.bitrate,
                    framerate: layerCfg.framerate,
                    latencyMode: 'realtime',
                    hardwareAcceleration: config.hardwareAcceleration ?? 'prefer-hardware',
                };
                if (category === 'h264')
                    encoderConfig.avc = { format: 'annexb' };
                handle.encoder.configure(encoderConfig);
                // Stamp the encoder's diagnostic tag with current layer + dims
                // so the onError log reports the encoder's CURRENT use, not
                // the layer it was first constructed for (pool reuse mismatch).
                handle.encoder.tag = `layer=${layerId}, codec=${layerCfg.codec}, ${layerCfg.width}x${layerCfg.height}`;
            } catch (e) {
                // Release back to pool so it can be parked or discarded by canReuse check.
                try { handle.release(); } catch { /* ignore */ }
                throw e;
            }
            return handle.encoder;
        };

        const senderFactory = (gate: FloodGate): StreamSenderLike => deps.createSender(config.chatId, gate);

        const whenDone = recorder.start({
            track,
            createProcessor: deps.createProcessor,
            encoderConfigs: config.encoderConfigs,
            normalizeSize: config.normalizeSize,
            downscalerMode: config.downscalerMode,
            createEncoder: encoderFactory,
            keyframeIntervalFrames: config.keyframeIntervalFrames,
            maxKeyFrameIntervalMs: config.maxKeyFrameIntervalMs,
            keepAlivePeriodMs: config.keepAlivePeriodMs,
            createSender: senderFactory,
            sourceKind: config.sourceKind ?? 0,
            isFrontCamera: config.isFrontCamera ?? false,
            isIos: config.isIos ?? false,
            initialGateOpen: config.initialGateOpen ?? true,
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
            session.setPreviewGenerator(undefined);
            disposeWorkerPreviewGenerator(s);
            disposeWorkerSourceTrack(s);
            if (s.whenDone === whenDone)
                s.whenDone = null;
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

    async setGateOpen(open: boolean): Promise<void> {
        const s = requireState();
        s.recorder.setGateOpen(open);
        await Promise.resolve();
    },

    async reconfigureLayers(
        configs: readonly import('../operators/encode').EncoderConfigPerLayer[],
    ): Promise<void> {
        const s = requireState();
        s.recorder.reconfigureLayers(configs);
        await Promise.resolve();
    },

    async setTargetFps(fps: number): Promise<void> {
        const s = requireState();
        s.recorder.setTargetFps(fps);
        await Promise.resolve();
    },

    async setPreviewSize(width: number, height: number): Promise<void> {
        const s = requireState();
        s.recorder.setPreviewSize(width, height);
        await Promise.resolve();
    },

    getPreviewTrace(): Promise<import('./recorder-worker-contract').PreviewTrace> {
        const s = requireState();
        return Promise.resolve({ ...s.session.previewTrace });
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
