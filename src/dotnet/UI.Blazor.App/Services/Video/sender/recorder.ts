// Recorder façade — composes the sender operators into one pipeline.
// `stop` completes the source and lets the pipe drain; abort is a safety
// timeout. `restart` keeps the session's parked encoders across the gap.

import { drain, pipe } from 'ix-ext';
import { getLogs } from 'logging';
import {
    createEmptyRecordingStats,
    type VideoRecordingStats,
} from '../frame-envelopes';
import { mstpSource } from '../operators/capture';
import { stampCaptureTime } from '../operators/stamp-capture-time';
import { attachSourceDims } from '../operators/attach-source-dims';
import { downscale, type DownscalerLike, type LayerSpec } from '../operators/downscale';
import { applyKeyframePolicy } from '../operators/apply-keyframe-policy';
import { encode, type EncoderConfigPerLayer, type EncoderFactory } from '../operators/encode';
import { FloodGate, floodGate } from '../operators/flood-gate';
import { wireSend, type StreamSenderLike } from '../operators/wire-send';
import type { SenderSession } from './session';

const { warnLog } = getLogs('VideoPipeline');
const STOP_DRAIN_GRACE_MS = 3_000;

export type { EncoderConfigPerLayer, EncoderFactory } from '../operators/encode';
export type { StreamSenderLike } from '../operators/wire-send';
export type { DownscalerLike, LayerSpec } from '../operators/downscale';
export type { EncodeInput } from '../operators/encode';
export type { VideoRecordingStats };

export interface RecorderConfig {
    track: MediaStreamTrack;
    // Bottom-first simulcast ladder; single-tier P2P passes one entry.
    encoderConfigs: readonly EncoderConfigPerLayer[];
    keyframeIntervalFrames: number;
    maxKeyFrameIntervalMs?: number;
    createSender: (gate: FloodGate) => StreamSenderLike;
    createEncoder: EncoderFactory;
    // Required for simulcast (length > 1); single-tier defaults to clone-only.
    createDownscaler?: () => DownscalerLike;
    createProcessor?: (track: MediaStreamTrack) => { readable: ReadableStream<VideoFrame> };
}

export class Recorder {
    private readonly session: SenderSession;
    private abortController: AbortController | null = null;
    private sourceStopController: AbortController | null = null;
    private abortTimeoutId: ReturnType<typeof setTimeout> | null = null;
    private abortTimeoutReason: unknown = null;
    private currentWhenDone: Promise<void> | null = null;
    private currentStats: VideoRecordingStats | null = null;
    private startedAtMs = 0;

    constructor(session: SenderSession) {
        this.session = session;
    }

    async start(config: RecorderConfig): Promise<void> {
        if (this.abortController)
            throw new Error('Recorder: already running — call stop() first');
        if (config.encoderConfigs.length === 0)
            throw new Error('Recorder: encoderConfigs must contain at least one layer');
        if (config.keyframeIntervalFrames <= 0)
            throw new Error('Recorder: keyframeIntervalFrames must be > 0');

        this.startedAtMs = Date.now();
        const stats = createEmptyRecordingStats(this.startedAtMs);
        this.currentStats = stats;
        const ladder: LayerSpec[] = config.encoderConfigs.map(c => ({
            width: c.width,
            height: c.height,
        }));
        const createDownscaler = config.createDownscaler
            ?? (config.encoderConfigs.length === 1
                ? () => identityDownscaler()
                : (): DownscalerLike => {
                    throw new Error(
                        'Recorder: createDownscaler is required for simulcast '
                        + `(${config.encoderConfigs.length}-layer ladder)`);
                });
        const abortController = new AbortController();
        const abortSignal = abortController.signal;
        const sourceStopController = new AbortController();
        // Closed by `push-to-pull-buffer` when its bundle queue fills past
        // half, reopened below a quarter. Closing right after capture is
        // the cheapest place to absorb a wire stall.
        const gate = new FloodGate();
        const captureSource = mstpSource({
            track: config.track,
            stats,
            abortSignal,
            stopSignal: sourceStopController.signal,
            createProcessor: config.createProcessor,
        });
        const recordingPipe = pipe(
            captureSource,
            floodGate(gate),
            stampCaptureTime({ clock: this.session.captureClock }),
            attachSourceDims(),
            downscale({ ladder, createDownscaler }),
            applyKeyframePolicy({
                keyframeIntervalFrames: config.keyframeIntervalFrames,
                maxKeyframeIntervalMs: config.maxKeyFrameIntervalMs,
            }),
            encode({
                configs: config.encoderConfigs,
                createEncoder: config.createEncoder,
            }),
            wireSend({
                createSender: () => config.createSender(gate),
                layerCount: config.encoderConfigs.length,
                topLayerWidth: config.encoderConfigs[config.encoderConfigs.length - 1].width,
                topLayerHeight: config.encoderConfigs[config.encoderConfigs.length - 1].height,
                abortSignal,
            }),
        );
        this.abortController = abortController;
        this.sourceStopController = sourceStopController;
        const whenDrained = drain(recordingPipe, e => e === this.abortTimeoutReason);
        this.currentWhenDone = whenDrained;
        try {
            await whenDrained;
        } finally {
            if (this.abortController === abortController) {
                if (this.abortTimeoutId !== null)
                    clearTimeout(this.abortTimeoutId);
                this.abortController = null;
                this.sourceStopController = null;
                this.abortTimeoutId = null;
                this.abortTimeoutReason = null;
                this.currentWhenDone = null;
            }
            // Stats persist post-run for one-shot diagnostic reads;
            // cleared on the next `start()`.
        }
    }

    stop(): void {
        const sourceStopController = this.sourceStopController;
        const abortController = this.abortController;
        if (!sourceStopController || !abortController)
            return;

        if (!sourceStopController.signal.aborted)
            sourceStopController.abort(new Error('Recorder.stop: source completed'));
        if (this.abortTimeoutId !== null)
            return;

        const abortReason = new Error('Recorder.stop: graceful drain timed out');
        this.abortTimeoutReason = abortReason;
        this.abortTimeoutId = setTimeout(() => {
            if (!abortController.signal.aborted)
                abortController.abort(abortReason);
        }, STOP_DRAIN_GRACE_MS);
    }

    async restart(config: RecorderConfig): Promise<void> {
        const controller = this.abortController;
        const whenDone = this.currentWhenDone;
        if (controller) {
            this.stop();
            if (whenDone) {
                try { await whenDone; }
                catch (e) { warnLog?.log('Recorder.restart: previous run failed during restart:', e); }
            }
        }
        this.session.reset();
        await this.start(config);
    }

    isRunning(): boolean {
        return this.abortController !== null;
    }

    getStats(): VideoRecordingStats | null {
        return this.currentStats;
    }
}

// Single-tier default: clone-then-close. Multi-layer through a clone-only
// would silently skip the resize and lie about layer dims, so we throw.
function identityDownscaler(): DownscalerLike {
    return {
        process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
            if (layers.length !== 1) {
                try { input.close(); } catch { /* ignore */ }
                return Promise.reject(new Error(
                    `identityDownscaler: expected single-tier ladder, got ${layers.length}`));
            }
            const out: VideoFrame[] = [input.clone()];
            try { input.close(); } catch { /* ignore */ }
            return Promise.resolve(out);
        },
    };
}
