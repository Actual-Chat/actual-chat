import { AsyncSignal } from 'actuallab-core';
import { RunningEMA } from 'math';
import { closeEncodedChunk, type ArrivedChunk, type DecodedFrame } from '../frame-envelopes';
import { createCodecProofTracker, type CodecProofTracker } from '../codec-proof';
import { HAS_VF_ROTATION_INIT, wrapWithRotation } from '../video-frame-caps';
import type { RotationQuarter } from 'orientation';

const DECODE_RATIO_EMA_ALPHA = 0.2;
const HANG_WINDOW_MS = 60_000;

// Message prefix stamped on a thrown error when the recovery budget is
// exhausted — VideoPlayer reads it to drive codec exclusion. Encoded in the
// message because errors cross the worker boundary as strings.
export const CODEC_EXHAUSTED_PREFIX = '[CODEC_EXHAUSTED]';

export function isCodecExhaustedError(error: unknown): boolean {
    const message = error instanceof Error ? error.message : String(error);
    return message.startsWith(CODEC_EXHAUSTED_PREFIX);
}

// WebCodecs VideoDecoder surface. Tests inject a fake.
export interface DecoderLike {
    state: 'unconfigured' | 'configured' | 'closed';
    decodeQueueSize: number;
    configure(config: VideoDecoderConfig): void;
    decode(chunk: EncodedVideoChunk): void;
    flush(): Promise<void>;
    close(): void;
}

export interface InitialDecoderConfig {
    codec: string;
    codedWidth?: number;
    codedHeight?: number;
    hardwareAcceleration?: 'prefer-hardware' | 'prefer-software' | 'no-preference';
    optimizeForLatency?: boolean;
}

export interface VideoDecoderBridgeOptions {
    initialConfig: InitialDecoderConfig;
    createDecoder: (handlers: {
        onFrame: (frame: VideoFrame) => void;
        onError: (e: Error) => void;
    }) => DecoderLike;
    onCodecExhausted?: (codec: string) => void;
    onCodecProven?: (codec: string) => void;
    // All defaulted by the caller (decode operator).
    framesUntilProven: number;
    maxRecoveries: number;
    decoderHangTimeoutMs: number;
    frameDurationMs: number;
    now: () => number;
}

// FIFO mirroring decode() calls; output frames pair by shift order
// (WebCodecs guarantees in-order output per submitted chunk).
interface PendingDecode {
    capturedAt: { timeMs: number; epoch: number };
    arrivedAt: ArrivedChunk['arrivedAt'];
    serverArrivedAtUnixMs: number;
    layerId: number;
    submitMs: number;
    index: number;
    dropTrace: ArrivedChunk['dropTrace'];
    rotation: RotationQuarter;
}

// HEVC (hev1/hvc1) needs a description; AVC and AV1 inline codec parameters in
// the bytestream and can configure without one.
function canConfigureWithoutDescription(codec: string): boolean {
    return codec.startsWith('avc1') || codec.startsWith('av01');
}

// Owns the WebCodecs decoder lifecycle and the push→pull bridge: holds the
// in-flight FIFO and decoded-frame inventory, the recovery state machine
// (errors funnel through `error`; a keyframe triggers decoder recreate, and the
// codec is declared exhausted after maxRecoveries), and keyframe (re)configure.
// `onFrame`/`onError` are the decoder's handlers; they mutate fields and pulse
// the two signals the operator's loops wait on. Sync — the `decode` operator
// owns the async loops (mirrors EncodedFrameBuffer / pacedEncodedBuffer).
export class VideoDecoderBridge {
    // `whenDataReady` nudges the drain/yield loop (a frame landed, a chunk went
    // in flight, or an error needs handling); `whenSpaceAvailable` nudges the
    // feed pump when backpressure clears (in-flight dropped or inventory drained).
    readonly whenDataReady = new AsyncSignal();
    readonly whenSpaceAvailable = new AsyncSignal();

    private readonly opts: VideoDecoderBridgeOptions;
    private readonly handlers: { onFrame: (f: VideoFrame) => void; onError: (e: Error) => void };
    private decoder: DecoderLike;

    private readonly pending: PendingDecode[] = [];
    private readonly ready: DecodedFrame[] = [];
    private pendingError: Error | null = null;
    private consecutiveRecoveries = 0;
    private readonly codecProofTracker: CodecProofTracker;
    private codecProvenFired = false;

    private readonly currentCodec: string;
    private configured = false;
    private currentWidth: number;
    private currentHeight: number;
    private readonly descriptionByLayer = new Map<number, ArrayBuffer>();

    private readonly decodeRatioEma = new RunningEMA(0, 1, DECODE_RATIO_EMA_ALPHA);
    private readonly hangTimestamps: number[] = [];
    private lastDecoderActivityMs: number;
    private currentStats: ArrivedChunk['stats'] | null = null;

    constructor(opts: VideoDecoderBridgeOptions) {
        this.opts = opts;
        this.currentCodec = opts.initialConfig.codec;
        this.currentWidth = opts.initialConfig.codedWidth ?? 0;
        this.currentHeight = opts.initialConfig.codedHeight ?? 0;
        this.codecProofTracker = createCodecProofTracker(opts.framesUntilProven);
        this.lastDecoderActivityMs = opts.now();
        this.handlers = {
            onFrame: (f: VideoFrame): void => this.onFrame(f),
            onError: (e: Error): void => this.onError(e),
        };
        this.decoder = opts.createDecoder(this.handlers);
    }

    get inFlight(): number { return this.pending.length; }
    get readyCount(): number { return this.ready.length; }
    get error(): Error | null { return this.pendingError; }

    setError(e: unknown): void {
        this.pendingError ??= e instanceof Error ? e : new Error(String(e));
    }

    tryPull(): DecodedFrame | null {
        const frame = this.ready.shift() ?? null;
        if (frame)
            this.whenSpaceAvailable.notify();
        return frame;
    }

    // Watchdog: remaining ms before the decoder is declared hung, or null when
    // nothing is in flight (a quiet stream must not synthesise spurious timeouts).
    msUntilHang(now: number): number | null {
        if (this.pending.length === 0)
            return null;
        return Math.max(0, this.opts.decoderHangTimeoutMs - (now - this.lastDecoderActivityMs));
    }

    onWatchdogFired(now: number): void {
        // Synthesise a decoder error so the recovery branch handles it like a real one.
        this.pendingError ??= new Error(
            `decode: hang watchdog (no frames in ${this.opts.decoderHangTimeoutMs} ms, `
            + `pending=${this.pending.length}, codec=${this.currentCodec})`);
        this.codecProofTracker.noteDecoderError();
        this.recordHang(now);
        if (this.currentStats)
            this.currentStats.hangRateIn60s = this.hangTimestamps.length;
        this.lastDecoderActivityMs = now;
        // pendingError now opens the pump's backpressure gate; wake it so it
        // consumes the recovery keyframe (a hung decoder won't drain pending).
        this.whenSpaceAvailable.notify();
    }

    // Process one arrived chunk: recovery, keyframe (re)configure, submit. Throws
    // CODEC_EXHAUSTED only when recovery is exhausted; recoverable failures funnel
    // through `pendingError`. Always closes the chunk.
    submit(arrived: ArrivedChunk): void {
        const now = this.opts.now;
        try {
            this.currentStats = arrived.stats;
            arrived.stats.chunksReceived++;
            // Time-decay hangs so the count drops without needing a new event.
            const arrivalNowMs = now();
            while (this.hangTimestamps.length > 0
                && arrivalNowMs - this.hangTimestamps[0] > HANG_WINDOW_MS)
                this.hangTimestamps.shift();
            arrived.stats.hangRateIn60s = this.hangTimestamps.length;

            const errSnapshot = this.pendingError;
            if (errSnapshot) {
                if (!arrived.isKeyFrame)
                    return;

                this.consecutiveRecoveries++;
                arrived.stats.recoveryStreak = this.consecutiveRecoveries;
                if (!this.codecProofTracker.isProven() && this.consecutiveRecoveries >= this.opts.maxRecoveries) {
                    const codec = this.currentCodec;
                    this.pendingError = null;
                    this.opts.onCodecExhausted?.(codec);
                    throw new Error(
                        `${CODEC_EXHAUSTED_PREFIX} decode: recovery exhausted after ${this.consecutiveRecoveries} attempts (codec=${codec})`,
                        { cause: errSnapshot },
                    );
                }
                this.pendingError = null;
                try { this.decoder.close(); } catch { /* ignore */ }
                this.decoder = this.opts.createDecoder(this.handlers);
                this.configured = false;
                this.pending.length = 0;
            }

            const dec = this.decoder;
            if (arrived.isKeyFrame) {
                const newWidth = arrived.width || this.currentWidth;
                const newHeight = arrived.height || this.currentHeight;
                if (arrived.description && arrived.description.byteLength > 0)
                    this.descriptionByLayer.set(arrived.layerId, arrived.description);
                const description = this.descriptionByLayer.get(arrived.layerId);

                const dimChanged = this.configured
                    && (newWidth !== this.currentWidth || newHeight !== this.currentHeight);
                if (!this.configured || dimChanged) {
                    if (!description && !canConfigureWithoutDescription(this.currentCodec)) {
                        // Funnel through pendingError so the same N-attempt recovery handles it.
                        this.pendingError ??= new Error(
                            `decode: codec ${this.currentCodec} requires description but none provided`);
                        this.codecProofTracker.noteDecoderError();
                        return;
                    }

                    // Pin displayAspect=coded so the browser doesn't derive display
                    // dims from the bitstream SPS/HVCC (Edge HEVC HW returns swapped
                    // portrait dims; Chrome Android confuses <video srcObject>).
                    const config: VideoDecoderConfig = {
                        codec: this.currentCodec,
                        codedWidth: newWidth || undefined,
                        codedHeight: newHeight || undefined,
                        // Prefer the HW decoder (default 'no-preference' lets the
                        // browser pick a slow SW path even when HW exists) and ask
                        // for low-latency mode (no decode-reorder buffering).
                        hardwareAcceleration: this.opts.initialConfig.hardwareAcceleration ?? 'prefer-hardware',
                        optimizeForLatency: this.opts.initialConfig.optimizeForLatency ?? true,
                    };
                    if (description) config.description = description;
                    if (newWidth > 0 && newHeight > 0) {
                        config.displayAspectWidth = newWidth;
                        config.displayAspectHeight = newHeight;
                    }
                    try {
                        dec.configure(config);
                        this.configured = true;
                        this.currentWidth = newWidth;
                        this.currentHeight = newHeight;
                    } catch (e) {
                        this.pendingError ??= e instanceof Error ? e : new Error(String(e));
                        this.codecProofTracker.noteDecoderError();
                        return;
                    }
                }
            }

            if (!this.configured)
                return;

            const submitMs = now();
            this.pending.push({
                capturedAt: arrived.capturedAt,
                arrivedAt: arrived.arrivedAt,
                serverArrivedAtUnixMs: arrived.serverArrivedAtUnixMs,
                layerId: arrived.layerId,
                submitMs,
                index: arrived.index,
                dropTrace: arrived.dropTrace,
                rotation: arrived.rotation,
            });
            try {
                dec.decode(arrived.chunk);
                // Nudge the drain loop so it (re)arms the hang watchdog now that a
                // chunk is in flight — the pump, not the consumer, drives submission.
                this.whenDataReady.notify();
            } catch (e) {
                this.pending.pop();
                this.pendingError ??= e instanceof Error ? e : new Error(String(e));
                this.codecProofTracker.noteDecoderError();
                this.whenDataReady.notify();
            }
        } finally {
            closeEncodedChunk(arrived.chunk);
        }
    }

    dispose(): void {
        try { this.decoder.close(); } catch { /* ignore */ }
        while (this.ready.length > 0) {
            const envelope = this.ready.shift()!;
            try { envelope.frame.close(); } catch { /* ignore */ }
        }
    }

    // Private methods

    private onFrame(frame: VideoFrame): void {
        this.lastDecoderActivityMs = this.opts.now();
        const meta = this.pending.shift();
        if (!meta) {
            try { frame.close(); } catch { /* ignore */ }
            return;
        }
        const decodedAtMs = this.opts.now();
        const stats = this.currentStats!;
        // Move display rotation from the envelope to the VideoFrame's own metadata
        // when supported (<video> auto-rotates from frame.rotation; downstream
        // CSS/canvas paths read envelope.rotation), so zero it here to avoid
        // double-rotation. Canvas backends / Firefox stay on the legacy path.
        let outFrame = frame;
        let outRotation: RotationQuarter = meta.rotation;
        if (HAS_VF_ROTATION_INIT && meta.rotation !== 0) {
            outFrame = wrapWithRotation(frame, meta.rotation);
            try { frame.close(); } catch { /* ignore */ }
            outRotation = 0;
        }
        const envelope: DecodedFrame = {
            frame: outFrame,
            capturedAt: meta.capturedAt,
            arrivedAt: meta.arrivedAt,
            serverArrivedAtUnixMs: meta.serverArrivedAtUnixMs,
            decodedAt: { timeMs: decodedAtMs, epoch: 0 },
            index: meta.index,
            dropTrace: meta.dropTrace,
            layerId: meta.layerId,
            rotation: outRotation,
            stats,
        };
        this.consecutiveRecoveries = 0;
        stats.recoveryStreak = 0;
        this.decodeRatioEma.appendSample((decodedAtMs - meta.submitMs) / this.opts.frameDurationMs);
        stats.decodeRatioEma = this.decodeRatioEma.value;
        stats.framesDecoded++;
        stats.decoderQueueSize = this.pending.length;
        this.noteFrameDecoded(meta.layerId);
        this.ready.push(envelope);
        this.whenDataReady.notify();
        this.whenSpaceAvailable.notify();
    }

    private onError(e: Error): void {
        this.lastDecoderActivityMs = this.opts.now();
        this.pendingError = e;
        this.codecProofTracker.noteDecoderError();
        this.whenDataReady.notify();
        this.whenSpaceAvailable.notify();
    }

    private noteFrameDecoded(layerId: number): void {
        const wasProven = this.codecProofTracker.isProven();
        this.codecProofTracker.noteFrameDecoded(layerId);
        if (!this.codecProvenFired && !wasProven && this.codecProofTracker.isProven()) {
            this.codecProvenFired = true;
            this.opts.onCodecProven?.(this.currentCodec);
        }
    }

    private recordHang(wallMs: number): void {
        this.hangTimestamps.push(wallMs);
        while (this.hangTimestamps.length > 0 && wallMs - this.hangTimestamps[0] > HANG_WINDOW_MS)
            this.hangTimestamps.shift();
    }
}
