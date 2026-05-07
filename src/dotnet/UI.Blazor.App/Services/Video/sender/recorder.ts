// Recorder façade — composes the sender operators into one pipeline.
// `start` builds a fresh pipe and drains it; `stop` completes the source
// and lets the pipeline drain, with abort only as a safety timeout.
// `restart` chains stop + start while the session's encoder pool retains
// parked encoders across the gap.

import { drain, pipe } from 'ix-ext';
import { getLogs } from 'logging';
import {
    createEmptyRecordingStats,
    type VideoRecordingStats,
} from '../frame-envelopes';
import { mstpSource } from '../operators/capture';
import { stampCaptureTime } from '../operators/stamp-capture-time';
import { attachSourceDims } from '../operators/attach-source-dims';
import { downscale, type DownscalerLike, type SpatialLayerSpec } from '../operators/downscale';
import { applyKeyframePolicy } from '../operators/apply-keyframe-policy';
import { encode, type EncoderConfigPerLayer, type EncoderFactory } from '../operators/encode';
import { wireSend, type StreamSenderLike } from '../operators/wire-send';
import type { SenderSession } from './session';

const { warnLog } = getLogs('VideoPipeline');
const STOP_DRAIN_GRACE_MS = 1_000;

// Re-export collaborator-facing types so callers don't have to reach
// into operators/.
export type { EncoderConfigPerLayer, EncoderFactory } from '../operators/encode';
export type { StreamSenderLike } from '../operators/wire-send';
export type { DownscalerLike, SpatialLayerSpec } from '../operators/downscale';
export type { EncodeInput } from '../operators/encode';
export type { VideoRecordingStats };

export interface RecorderConfig {
    track: MediaStreamTrack;
    /** Bottom-first simulcast ladder. Single-tier P2P passes one entry. */
    encoderConfigs: readonly EncoderConfigPerLayer[];
    /** Frame-count keyframe interval (must be > 0). */
    keyframeIntervalFrames: number;
    /** Wallclock floor (ms) for keyframe forcing. */
    maxKeyFrameIntervalMs?: number;
    /** Production: `RpcStreamSender` via `InternalVideoStream`. */
    createSender: () => StreamSenderLike;
    /** Production: pulls from session encoder pool + WebCodecs glue. */
    createEncoder: EncoderFactory;
    /** Required for simulcast (length > 1); defaults to clone-only
     *  identity for single-tier. */
    createDownscaler?: () => DownscalerLike;
    /** Test override for the {@link mstpSource} processor factory. */
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
        const ladder: SpatialLayerSpec[] = config.encoderConfigs.map(c => ({
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
        const captureSource = mstpSource({
            track: config.track,
            stats,
            abortSignal,
            stopSignal: sourceStopController.signal,
            createProcessor: config.createProcessor,
        });
        const recordingPipe = pipe(
            captureSource,
            stampCaptureTime({ clock: this.session.captureClock }),
            attachSourceDims(),
            // logItems<CapturedFrame>('captured', { firstN: 5, everyN: 300, format: f => `idx=${f.index}` }),
            downscale({ ladder, createDownscaler }),
            applyKeyframePolicy({
                keyframeIntervalFrames: config.keyframeIntervalFrames,
                maxKeyframeIntervalMs: config.maxKeyFrameIntervalMs,
            }),
            encode({
                configs: config.encoderConfigs,
                createEncoder: config.createEncoder,
            }),
            // logItems<EncodedFrame>('encoded', { firstN: 5, everyN: 300, format: f => `layer=${f.spatialLayerId} key=${f.chunk.type === 'key'} sz=${f.chunk.byteLength}` }),
            wireSend({
                createSender: config.createSender,
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
            // Stats persist post-run for one-shot diagnostic reads —
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

// Single-tier default: clone-then-close. Simulcast ladders MUST supply
// a real downscaler — feeding multi-layer through a clone-only would
// silently skip the resize and lie about layer dims.
function identityDownscaler(): DownscalerLike {
    return {
        process(input: VideoFrame, layers: readonly SpatialLayerSpec[]): Promise<VideoFrame[]> {
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
