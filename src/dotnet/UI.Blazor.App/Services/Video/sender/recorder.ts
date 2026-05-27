// Recorder façade — composes the sender operators into one pipeline.
// `stop` completes the source and lets the pipe drain; abort is a safety
// timeout. `restart` keeps the session's parked encoders across the gap.

import { drain, pipe } from 'ix-ext';
import { getLogs } from 'logging';
import {
    createEmptyRecorderStats,
    type CapturedBundle,
    type CapturedFrame,
    type EncodedBundle,
    type RecorderStats,
} from '../frame-envelopes';
import { FrameDropStage, traceDrops } from '../frame-drop-trace';
import { mstpSource } from '../operators/capture';
import { stampCaptureTime } from '../operators/stamp-capture-time';
import { attachSourceDims } from '../operators/attach-source-dims';
import { normalizeFrame, spatialize } from '../operators/downscale';
import { previewForwarder } from '../operators/preview-forwarder';
import { applyKeyframePolicy } from '../operators/apply-keyframe-policy';
import { encode, type EncoderConfigPerLayer, type EncoderFactory } from '../operators/encode';
import { FloodGate, floodGate } from '../operators/flood-gate';
import { stampEncoderDescription } from '../operators/stamp-encoder-description';
import { MutableWireGate, wireGate } from '../operators/wire-gate';
import { wireSend, type StreamSenderLike } from '../operators/wire-send';
import { LayerLadderController } from './layer-ladder-controller';
import type { SenderSession } from './session';

const { warnLog } = getLogs('VideoPipeline');
const STOP_DRAIN_GRACE_MS = 3_000;

export type { EncoderConfigPerLayer, EncoderFactory } from '../operators/encode';
export type { StreamSenderLike } from '../operators/wire-send';
export type { LayerSpec } from '../operators/downscale';
export type { EncodeInput } from '../operators/encode';
export type { RecorderStats };

// Members ordered by sender pipeline flow:
//   source (track) → normalize → preview/effects → spatial layers → encode → wireSend.
export interface RecorderConfig {
    // -- source --
    track: MediaStreamTrack;
    createProcessor?: (track: MediaStreamTrack) => { readable: ReadableStream<VideoFrame> };

    // -- orientation / downscale --
    // 0 = Camera, 1 = ScreenCast. Maps to .NET VideoSourceKind.
    sourceKind: number;
    isFrontCamera: boolean;
    isIos: boolean;

    // -- encode --
    // Bottom-first simulcast ladder; single-tier P2P passes one entry.
    encoderConfigs: readonly EncoderConfigPerLayer[];
    createEncoder: EncoderFactory;

    // -- keyframe policy --
    keyframeIntervalFrames: number;
    maxKeyFrameIntervalMs?: number;

    // -- wire send --
    createSender: (gate: FloodGate) => StreamSenderLike;
    // When false, the pipeline runs end-to-end but encoded bundles are
    // discarded before they reach `wireSend` — the server sees nothing.
    // Used for JoinVideoCallModal warmup so the encoder/HW slot can be
    // proven on real camera frames before the user clicks Join. Default
    // `true` preserves the legacy single-shot `startRecording` behavior.
    initialGateOpen?: boolean;
}

export class Recorder {
    private readonly session: SenderSession;
    private abortController: AbortController | null = null;
    private sourceStopController: AbortController | null = null;
    private abortTimeoutId: ReturnType<typeof setTimeout> | null = null;
    private abortTimeoutReason: unknown = null;
    private currentWhenDone: Promise<void> | null = null;
    private currentStats: RecorderStats | null = null;
    private forceKeyframeRequested = false;
    // Lives for the duration of one run; null when stopped.
    private ladderController: LayerLadderController | null = null;
    private wireGateState: MutableWireGate | null = null;

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

        const stats = createEmptyRecorderStats();
        this.currentStats = stats;
        const ladderController = new LayerLadderController(config.encoderConfigs);
        this.ladderController = ladderController;
        const wireGateState = new MutableWireGate(config.initialGateOpen ?? true);
        this.wireGateState = wireGateState;
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

        // Two pipes only because pipe()'s typed overload tops out at 9 ops;
        // runtime composition is identical.
        const captureToNormalized = pipe(
            captureSource,
            traceDrops<CapturedFrame>(FrameDropStage.SenderSource),
            floodGate(gate),
            traceDrops<CapturedFrame>(FrameDropStage.SenderFloodGate),
            stampCaptureTime({ clock: this.session.captureClock }),
            attachSourceDims(),
            normalizeFrame({
                ladder: ladderController,
                isCamera: config.sourceKind === 0,
                isFrontCamera: config.isFrontCamera,
                isIos: config.isIos,
            }),
            // simpleBlur({ radiusPx: 6 }), // Temporary effect probe; keep disabled by default.
            previewForwarder({
                getWriter: () => this.session.getPreviewWriter(),
                reportFrame: frame => this.session.reportPreviewFrame(frame),
                reportPresentation: p => this.session.reportPreviewFramePresentation(p),
            }),
        );
        const normalizedToBundle = pipe(
            captureToNormalized,
            spatialize({ controller: ladderController }),
            traceDrops<CapturedBundle>(FrameDropStage.SenderDownscale),
        );
        const recordingPipe = pipe(
            normalizedToBundle,
            applyKeyframePolicy({
                keyframeIntervalFrames: config.keyframeIntervalFrames,
                maxKeyframeIntervalMs: config.maxKeyFrameIntervalMs,
                consumeForceKeyframe: () => {
                    const requested = this.forceKeyframeRequested;
                    this.forceKeyframeRequested = false;
                    return requested;
                },
            }),
            encode({
                controller: ladderController,
                createEncoder: config.createEncoder,
            }),
            traceDrops<EncodedBundle>(FrameDropStage.SenderEncode),
            stampEncoderDescription(),
            wireGate(wireGateState),
            wireSend({
                createSender: () => config.createSender(gate),
                controller: ladderController,
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
                this.ladderController = null;
                this.wireGateState = null;
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

    requestKeyframe(): void {
        if (!this.abortController)
            return;
        this.forceKeyframeRequested = true;
    }

    // Flip the wireGate. Closed = encoded bundles are dropped before
    // wireSend; open = bundles flow to the server. Idempotent; safe to
    // call on a stopped recorder (no-op).
    setGateOpen(open: boolean): void {
        this.wireGateState?.setOpen(open);
    }

    // Hot-apply: mutate the running pipeline's layer ladder without stopping
    // the wire RpcStream. Caller is responsible for ensuring `next` is
    // codec-compatible with the running encoders (codec swap still requires a
    // full restart). No-op when not running.
    reconfigureLayers(next: readonly EncoderConfigPerLayer[]): void {
        if (!this.abortController || !this.ladderController)
            return;
        if (next.length === 0)
            throw new Error('Recorder.reconfigureLayers: configs must not be empty');
        this.ladderController.setConfigs(next);
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
        await this.start(config);
    }

    isRunning(): boolean {
        return this.abortController !== null;
    }

    getStats(): RecorderStats | null {
        return this.currentStats;
    }
}
