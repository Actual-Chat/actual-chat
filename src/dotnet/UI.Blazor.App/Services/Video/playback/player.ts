import { VIDEO } from 'app-constants';
import { drain, pipe, tap, type PipeOperator } from 'ix-ext';
import type { DecodedFrame } from '../frame-envelopes';
import { decode, type DecoderLike } from '../operators/decode';
import { latencyTap, type LatencySample } from '../operators/latency-tap';
import { canvasPresent, type CanvasImageInterface } from '../operators/present-canvas';
import { mstgPresent } from '../operators/present-mstg';
import { pullSource, type VideoFrameDto } from '../operators/pull';
import { pacedEncodedBuffer } from '../operators/encoded-buffer';
import { resetOnEpochChange } from '../operators/epoch-reset';
import { EncodedFrameBuffer } from './encoded-frame-buffer';
import type { PlaybackSession } from './session';
import type { RenderBackendConfig } from './render-backends';

const STOP_DRAIN_GRACE_MS = 3_000;

export type { DecoderLike } from '../operators/decode';
export type { LatencySample } from '../operators/latency-tap';
export type { VideoFrameDto } from '../operators/pull';

export interface PlayerConfig {
    streamId: string;
    initialDecoderConfig: { codec: string; codedWidth?: number; codedHeight?: number };
    targetBufferSpanMs: number;
    backend: RenderBackendConfig;
    getStream: (streamId: string) => Promise<AsyncIterable<VideoFrameDto>> | AsyncIterable<VideoFrameDto>;
    createDecoder: (handlers: {
        onFrame: (frame: VideoFrame) => void;
        onError: (e: Error) => void;
    }) => DecoderLike;
    reportLatency?: (sample: LatencySample) => void;
}

// One running pipeline (one stream). Multiple Players can share a
// PlaybackSession to amortize decoder pool / clock / stats. start()
// rejects if a run is in flight — call stop() and whenDone() first.
// stop() is non-blocking; whenDone() resolves when the pipe drains.
export class Player {
    private readonly session: PlaybackSession;
    private abortController: AbortController | null = null;
    private sourceStopController: AbortController | null = null;
    private abortTimeoutId: ReturnType<typeof setTimeout> | null = null;
    private abortTimeoutReason: unknown = null;
    private buffer: EncodedFrameBuffer | null = null;
    private whenDoneInternal: Promise<void> = Promise.resolve();

    constructor(session: PlaybackSession) {
        this.session = session;
    }

    start(config: PlayerConfig): Promise<void> {
        if (this.abortController !== null) {
            return Promise.reject(new Error(
                `Player.start: already running (streamId=${config.streamId})`));
        }

        const buffer = new EncodedFrameBuffer({
            targetSpanMs: config.targetBufferSpanMs,
            // Optional chain: tests may run before initAppConstants. The
            // buffer falls back to a sensible default when undefined.
            frameDurationMs: VIDEO?.frameDurationMs,
        });
        this.buffer = buffer;
        const session = this.session;
        const arrivalClock = session.arrivalClock;
        const stats = session.stats;
        const abortController = new AbortController();
        const abortSignal = abortController.signal;
        const sourceStopController = new AbortController();
        const source = pullSource({
            streamId: config.streamId,
            getStream: config.getStream,
            arrivalClock,
            stats,
            abortSignal,
            stopSignal: sourceStopController.signal,
        });
        let present: PipeOperator<DecodedFrame, void>;
        const getBufferSpanMs = (): number => buffer.spanMs();
        const targetSpanMs = config.targetBufferSpanMs;
        if (config.backend.kind === 'mstg') {
            const writer = config.backend.writer;
            present = mstgPresent({
                getWriter: () => writer,
                getBufferSpanMs,
                targetSpanMs,
            });
        } else {
            const canvasCtx: CanvasImageInterface = config.backend.canvasCtx;
            const convertToBitmap = config.backend.convertToBitmap;
            present = canvasPresent({
                getCanvasCtx: () => canvasCtx,
                convertToBitmap,
                getBufferSpanMs,
                targetSpanMs,
            });
        }
        const reportLatency = config.reportLatency;
        const wrappedReportLatency = reportLatency
            ? (sample: LatencySample): void => {
                sample.bufferSpanMs = buffer.spanMs();
                reportLatency(sample);
            }
            : undefined;
        const decodedTap = wrappedReportLatency
            ? latencyTap({ report: wrappedReportLatency })
            : tap<DecodedFrame>(() => { /* identity */ });
        const pipeline = pipe(
            source,
            resetOnEpochChange({ buffer }),
            pacedEncodedBuffer({ buffer, abortSignal }),
            decode({
                initialConfig: config.initialDecoderConfig,
                createDecoder: config.createDecoder,
                abortSignal,
            }),
            decodedTap,
            present,
        );
        this.abortController = abortController;
        this.sourceStopController = sourceStopController;
        this.whenDoneInternal = drain(pipeline, e => e === this.abortTimeoutReason).finally(() => {
            if (this.abortController === abortController) {
                if (this.abortTimeoutId !== null)
                    clearTimeout(this.abortTimeoutId);
                this.abortController = null;
                this.sourceStopController = null;
                this.abortTimeoutId = null;
                this.abortTimeoutReason = null;
                this.buffer = null;
            }
        });
        return Promise.resolve();
    }

    stop(): void {
        const sourceStopController = this.sourceStopController;
        const abortController = this.abortController;
        if (!sourceStopController || !abortController)
            return;

        if (!sourceStopController.signal.aborted)
            sourceStopController.abort(new Error('Player.stop: source completed'));
        if (this.abortTimeoutId !== null)
            return;

        const abortReason = new Error('Player.stop: graceful drain timed out');
        this.abortTimeoutReason = abortReason;
        this.abortTimeoutId = setTimeout(() => {
            if (!abortController.signal.aborted)
                abortController.abort(abortReason);
        }, STOP_DRAIN_GRACE_MS);
    }

    isRunning(): boolean {
        return this.abortController !== null;
    }

    whenDone(): Promise<void> {
        return this.whenDoneInternal;
    }

    // Recovery hook for out-of-band sender resync — rarely used.
    resetBuffer(): void {
        this.buffer?.reset();
    }
}
