import { getLogs } from 'logging';
import type { MonotonicTime } from 'clocks';
import { type Disposable, ObjectDisposedError } from 'disposable';
import { PromiseSourceWithTimeout } from 'promises';
import { closeEncodedChunk } from './frame-envelopes';

const { warnLog } = getLogs('AsyncVideoEncoder');

// ---- Recommended input / output types -------------------------------------

// Frame is owned by the wrapper once submitted; closed on output or failure.
export interface CapturedFrame<TMeta = void> {
    frame: VideoFrame;
    capturedAt: MonotonicTime;
    // Monotonic submission counter; used for FIFO ordering verification.
    index: number;
    meta: TMeta;
}

export interface EncodedFrame<TMeta = void> {
    chunk: EncodedVideoChunk;
    metadata: EncodedVideoChunkMetadata;
    capturedAt: MonotonicTime;
    index: number;
    meta: TMeta;
}

export interface CodecToAsyncAdapterOptions {
    // Default 2 — allows one submission to overlap with previous item still emerging.
    maxInflight?: number;
    // Steady-state per-item timeout (ms). On timeout: reject with reset error,
    // drop to maxInflight=1, reset codec, invoke onResetRequested. 0 disables.
    timeoutMs?: number;
    // First-output timeout — HW codecs take longer for the first frame than
    // steady-state cadence. Default 1500. 0 disables.
    firstTimeoutMs?: number;
    onResetRequested?: (reason: string) => void;
}

export type AsyncVideoEncoderOptions = CodecToAsyncAdapterOptions;

interface PendingCodecItem<TIn, TOut> {
    input: TIn;
    index: number;
    source: PromiseSourceWithTimeout<TOut>;
}

export class CodecToAsyncAdapterResetError extends Error {
    readonly isRecoverable = true;

    constructor(message: string) {
        super(message);
        this.name = 'CodecToAsyncAdapterResetError';
    }
}

export function isCodecToAsyncAdapterResetError(e: unknown): e is CodecToAsyncAdapterResetError {
    return e instanceof CodecToAsyncAdapterResetError
        || (e instanceof Error
            && (e as { isRecoverable?: unknown }).isRecoverable === true
            && (
                e.name === 'CodecToAsyncAdapterResetError'
                || e.name === 'AsyncVideoEncoderResetError'
            ));
}

export class AsyncVideoEncoderResetError extends CodecToAsyncAdapterResetError {
    constructor(message: string) {
        super(message);
        this.name = 'AsyncVideoEncoderResetError';
    }
}

export function isAsyncVideoEncoderResetError(e: unknown): e is AsyncVideoEncoderResetError {
    return e instanceof AsyncVideoEncoderResetError
        || (e instanceof Error
            && e.name === 'AsyncVideoEncoderResetError'
            && (e as { isRecoverable?: unknown }).isRecoverable === true);
}

// Bounded FIFO adapter for event-callback codecs. Subclasses submit via `submit`
// and call `resolveOutput` from the codec's output callback; `process()` resolves
// with the output paired to the oldest in-flight input.
export abstract class CodecToAsyncAdapter<TIn, TOut, TCodecOutput> implements Disposable {
    private readonly inflight: PendingCodecItem<TIn, TOut>[] = [];
    private readonly onResetRequested?: (reason: string) => void;
    private readonly timeoutMs: number;
    private readonly firstTimeoutMs: number;
    private maxInflight: number;
    private degraded = false;
    private disposed = false;
    private lastResolvedIndex = -1;
    private hasResolvedOutput = false;

    protected constructor(
        private readonly adapterName: string,
        options: CodecToAsyncAdapterOptions = {},
    ) {
        this.maxInflight = options.maxInflight ?? 2;
        this.timeoutMs = options.timeoutMs ?? 300;
        this.firstTimeoutMs = options.firstTimeoutMs ?? 1_500;
        this.onResetRequested = options.onResetRequested;
    }

    get inflightCount(): number { return this.inflight.length; }
    get isDegraded(): boolean { return this.degraded; }
    get effectiveMaxInflight(): number { return this.maxInflight; }
    get isDisposed(): boolean { return this.disposed; }

    process(input: TIn): Promise<TOut> {
        if (this.disposed) {
            this.closeInput(input);
            return Promise.reject(new ObjectDisposedError(`${this.adapterName} is disposed`));
        }
        const readyError = this.getNotReadyError();
        if (readyError) {
            this.closeInput(input);
            return Promise.reject(readyError);
        }
        if (this.inflight.length >= this.maxInflight) {
            this.closeInput(input);
            return Promise.reject(new Error(
                `${this.adapterName}: queue full (${this.inflight.length}/${this.maxInflight})`));
        }

        const pending = this.makePending(input);
        this.inflight.push(pending);

        try {
            this.submit(input);
        } catch (e) {
            this.removeFromQueue(pending);
            this.closeInput(input);
            pending.source.reject(e);
            return pending.source;
        }
        return pending.source;
    }

    handleCodecReset(): void {
        this.failAllPending('codec reset', true);
        this.lastResolvedIndex = -1;
        this.hasResolvedOutput = false;
    }

    // Same as handleCodecReset, but also issues the codec-level reset+reconfigure
    // hook. Used by upstream operators when a watchdog detects that the codec
    // accepted input but produced no output — the WebCodecs spec recovery step
    // for a silently-wedged HW codec is `reset()` + `configure(lastConfig)`.
    handleCodecHang(): void {
        this.failAllPending('codec hang', true);
        this.lastResolvedIndex = -1;
        this.hasResolvedOutput = false;
        this.resetCodec();
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.failAllPending('disposed', false);
        this.closeCodec();
    }

    protected resolveOutput(output: TCodecOutput): void {
        if (this.disposed) {
            this.closeOutput(output);
            return;
        }
        const front = this.inflight.shift();
        if (!front) {
            this.closeOutput(output);
            return;
        }
        this.closeInputAfterOutput(front.input);
        if (front.source.isCompleted()) {
            this.degradeAndReset(`stale-output (index=${front.index})`);
            this.closeOutput(output);
            return;
        }
        if (this.lastResolvedIndex >= 0 && front.index <= this.lastResolvedIndex) {
            this.closeOutput(output);
            front.source.reject(this.createResetError(
                `${this.adapterName}: out-of-order output (index=${front.index} <= last=${this.lastResolvedIndex})`));
            this.degradeAndReset(
                `out-of-order: index=${front.index} <= last=${this.lastResolvedIndex}`);
            return;
        }
        this.lastResolvedIndex = front.index;
        try {
            const result = this.buildOutput(front.input, output);
            this.hasResolvedOutput = true;
            front.source.resolve(result);
        } catch (e) {
            this.closeOutput(output);
            front.source.reject(e);
            this.degradeAndReset(`build-output failed (index=${front.index})`);
        }
    }

    protected abstract getInputIndex(input: TIn): number;
    protected abstract submit(input: TIn): void;
    protected abstract buildOutput(input: TIn, output: TCodecOutput): TOut;
    protected abstract closeInput(input: TIn): void;
    protected abstract closeOutput(output: TCodecOutput): void;
    protected abstract createResetError(message: string): CodecToAsyncAdapterResetError;

    protected getNotReadyError(): Error | null {
        return null;
    }

    protected closeInputAfterOutput(input: TIn): void {
        this.closeInput(input);
    }

    protected resetCodec(): void {
        // Optional subclass hook.
    }

    protected closeCodec(): void {
        // Optional subclass hook.
    }

    private makePending(input: TIn): PendingCodecItem<TIn, TOut> {
        const source = new PromiseSourceWithTimeout<TOut>();
        // Caller may not attach .catch before sync timeout/queue-full rejection;
        // real consumers still see the rejection via the returned promise.
        source.catch(() => { /* ignore */ });
        const pending: PendingCodecItem<TIn, TOut> = {
            input,
            index: this.getInputIndex(input),
            source,
        };

        const timeoutMs = this.hasResolvedOutput ? this.timeoutMs : this.firstTimeoutMs;
        if (timeoutMs > 0) {
            source.setTimeout(timeoutMs, () => {
                source.reject(this.createResetError(this.getTimeoutMessage(timeoutMs, pending.index)));
                this.degradeAndReset(`timeout (index=${pending.index})`);
            });
        }
        return pending;
    }

    protected getTimeoutMessage(timeoutMs: number, index: number): string {
        return `${this.adapterName}: item timeout after ${timeoutMs}ms (index=${index})`;
    }

    private degradeAndReset(reason: string): void {
        if (this.disposed) return;
        if (!this.degraded) {
            this.degraded = true;
            this.maxInflight = 1;
            warnLog?.log(`shrinking maxInflight to 1 — ${reason}`);
        }
        this.failAllPending(reason, true);
        this.resetCodec();
        this.onResetRequested?.(reason);
    }

    private failAllPending(reason: string, recoverable: boolean): void {
        while (this.inflight.length > 0) {
            const p = this.inflight.shift()!;
            this.closeInput(p.input);
            p.source.reject(recoverable
                ? this.createResetError(reason)
                : new Error(reason));
        }
    }

    private removeFromQueue(pending: PendingCodecItem<TIn, TOut>): void {
        const idx = this.inflight.indexOf(pending);
        if (idx >= 0) this.inflight.splice(idx, 1);
    }
}

interface EncoderOutput {
    chunk: EncodedVideoChunk;
    metadata: EncodedVideoChunkMetadata;
}

// Async wrapper around a WebCodecs VideoEncoder — each encode() returns a
// Promise<TOut> resolving when the matching EncodedVideoChunk emerges.
export class AsyncVideoEncoder<
    TIn extends { frame: VideoFrame; index: number },
    TOut,
> extends CodecToAsyncAdapter<TIn, TOut, EncoderOutput> {
    // Owner manages configure/flush/reset/state; wrapper owns only encode + output wiring.
    public readonly encoder: VideoEncoder;
    // Owner-mutable label for diagnostic logs. Encoders may be reused across
    // layers via `EncoderPool` (category-only matching) and the original
    // construction-time closure values become stale — the owner updates this
    // tag on acquire/configure so error logs reflect the encoder's CURRENT
    // layer/config rather than its first-ever one.
    public tag = '';

    private readonly buildEncodedOutput: (
        input: TIn,
        chunk: EncodedVideoChunk,
        metadata: EncodedVideoChunkMetadata,
    ) => TOut;
    private lastConfig: VideoEncoderConfig | null = null;

    constructor(
        buildOutput: (
            input: TIn,
            chunk: EncodedVideoChunk,
            metadata: EncodedVideoChunkMetadata,
        ) => TOut,
        onError: (e: unknown) => void,
        options: AsyncVideoEncoderOptions = {},
    ) {
        super('AsyncVideoEncoder', options);
        this.buildEncodedOutput = buildOutput;
        this.encoder = new VideoEncoder({
            output: (chunk: EncodedVideoChunk, metadata: EncodedVideoChunkMetadata) =>
                this.resolveOutput({ chunk, metadata }),
            error: (e: unknown) => onError(e),
        });
    }

    get state(): CodecState { return this.encoder.state; }

    configure(config: VideoEncoderConfig): void {
        this.lastConfig = { ...config };
        this.encoder.configure(config);
    }

    encode(input: TIn, opts: { keyFrame: boolean }): Promise<TOut> {
        return this.processWithOptions(input, opts);
    }

    handleEncoderReset(): void {
        this.handleCodecReset();
    }

    handleEncoderHang(): void {
        this.handleCodecHang();
    }

    protected getInputIndex(input: TIn): number {
        return input.index;
    }

    protected getNotReadyError(): Error | null {
        if (this.encoder.state === 'configured')
            return null;
        return new Error(`AsyncVideoEncoder: encoder state is '${this.encoder.state}'`);
    }

    protected buildOutput(input: TIn, output: EncoderOutput): TOut {
        return this.buildEncodedOutput(input, output.chunk, output.metadata);
    }

    protected closeInput(input: TIn): void {
        try { input.frame.close(); } catch { /* already closed */ }
    }

    protected closeOutput(output: EncoderOutput): void {
        closeEncodedChunk(output.chunk);
    }

    protected createResetError(message: string): AsyncVideoEncoderResetError {
        return new AsyncVideoEncoderResetError(message);
    }

    protected getTimeoutMessage(timeoutMs: number, index: number): string {
        return `AsyncVideoEncoder: encode timeout after ${timeoutMs}ms (index=${index})`;
    }

    protected resetCodec(): void {
        if (this.isDisposed || this.encoder.state === 'closed') return;
        try {
            this.encoder.reset();
        } catch { /* ignore */ }
        if (!this.lastConfig) return;
        try {
            this.encoder.configure(this.lastConfig);
        } catch (e) {
            warnLog?.log('reconfigure after reset failed:', e);
        }
    }

    protected closeCodec(): void {
        if (this.encoder.state !== 'closed') {
            try { this.encoder.close(); } catch { /* ignore */ }
        }
    }

    private processWithOptions(input: TIn, opts: { keyFrame: boolean }): Promise<TOut> {
        this.nextEncodeOptions = opts;
        try {
            return this.process(input);
        } finally {
            this.nextEncodeOptions = null;
        }
    }

    private nextEncodeOptions: { keyFrame: boolean } | null = null;

    protected submit(input: TIn): void {
        this.encoder.encode(input.frame, { keyFrame: this.nextEncodeOptions?.keyFrame ?? false });
    }

    // Frame close is DEFERRED until the encoder's output callback fires:
    // canvas-backed frames may be the sole reference to their GPU texture, and
    // closing before the encoder reads it makes the encoder silently never emit.
}
