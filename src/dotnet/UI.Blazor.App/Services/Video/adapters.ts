import { getLogs } from 'logging';
import type { MonotonicTime } from 'clocks';
import { type Disposable, ObjectDisposedError } from 'disposable';
import { PromiseSourceWithTimeout } from 'promises';
import { closeEncodedChunk } from './frame-envelopes';

const { warnLog } = getLogs('AsyncVideoEncoder');

// Test fault injection for encoder recovery drills. Uncomment locally when
// needed; production should use the configured timeout unchanged.
// const RANDOM_INSTANT_TIMEOUT_PROBABILITY = 0.02;

// ---- Recommended input / output types -------------------------------------

// Callers can use these directly, extend them, or substitute their own —
// `AsyncVideoEncoder` is generic over `TIn extends { frame, index }` and an
// arbitrary `TOut`.

/**
 * Input bundle for {@link AsyncVideoEncoder.encode}. The `frame` is owned by
 * the wrapper once submitted and is closed when output emerges or the
 * submission is failed. `capturedAt` and `index` are threaded through to
 * the matching {@link EncodedFrame}.
 */
export interface CapturedFrame<TMeta = void> {
    frame: VideoFrame;
    capturedAt: MonotonicTime;
    /** Monotonic submission counter assigned by the producer. Used by the
     *  wrapper for FIFO ordering verification. */
    index: number;
    meta: TMeta;
}

/**
 * Output bundle: encoded chunk + metadata reattached after passing through
 * the WebCodecs encoder boundary.
 */
export interface EncodedFrame<TMeta = void> {
    chunk: EncodedVideoChunk;
    metadata: EncodedVideoChunkMetadata;
    capturedAt: MonotonicTime;
    index: number;
    meta: TMeta;
}

export interface CodecToAsyncAdapterOptions {
    /**
     * Initial concurrency cap. Default 2 — lets one codec submission happen
     * while the previous item is still emerging, supporting transient slow
     * spikes without building an unbounded queue.
     */
    maxInflight?: number;
    /**
     * Steady-state per-item timeout (ms). On timeout the pending promise
     * rejects with a recoverable reset error, the adapter drops to
     * maxInflight=1, resets/reconfigures the underlying codec if supported,
     * and invokes `onResetRequested`. Default 300. Set to 0 to disable.
     */
    timeoutMs?: number;
    /**
     * Timeout for the first output from a codec. Hardware codecs can take
     * substantially longer to produce their first keyframe / frame than
     * their steady-state cadence. Default 1500. Set to 0 to disable.
     */
    firstTimeoutMs?: number;
    /**
     * Invoked when the adapter has decided that the underlying codec is no
     * longer usable as-is — timeout, stale output, or out-of-order output.
     * Adapter internal state has already been reset; the owner is expected
     * to force the next submission to be independently recoverable when the
     * codec format requires it.
     */
    onResetRequested?: (reason: string) => void;
}

/** Back-compat name for existing encoder call sites. */
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

/**
 * Bounded FIFO adapter for event-callback codecs. Subclasses submit input to
 * an event-based codec in `submit`, then call `resolveOutput` from the codec's
 * output callback. The returned `process()` promise resolves with the output
 * paired to the oldest in-flight input.
 */
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
        // Optional for subclasses.
    }

    protected closeCodec(): void {
        // Optional for subclasses.
    }

    private makePending(input: TIn): PendingCodecItem<TIn, TOut> {
        const source = new PromiseSourceWithTimeout<TOut>();
        // Swallow unhandled-rejection — caller may not attach .catch before
        // the timeout/queue-full path rejects synchronously. Real consumers
        // get the rejection through the returned promise.
        source.catch(() => { /* ignore */ });
        const pending: PendingCodecItem<TIn, TOut> = {
            input,
            index: this.getInputIndex(input),
            source,
        };

        // Test fault injection for encoder recovery drills. Uncomment locally
        // to make random submissions behave as if they had a 0ms timeout.
        // const injectInstantTimeout = Math.random() < RANDOM_INSTANT_TIMEOUT_PROBABILITY;
        // if (injectInstantTimeout)
        //     warnLog?.log(`TEST FAULT: injected random instant timeout (index=${pending.index})`);
        // const timeoutMs = injectInstantTimeout ? 0 : (this.hasResolvedOutput ? this.timeoutMs : this.firstTimeoutMs);

        const timeoutMs = this.hasResolvedOutput ? this.timeoutMs : this.firstTimeoutMs;
        // if (timeoutMs > 0 || injectInstantTimeout) {
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

/**
 * Async wrapper around a WebCodecs `VideoEncoder` that turns each `encode()`
 * call into a `Promise<TOut>` resolving when the matching `EncodedVideoChunk`
 * emerges.
 */
export class AsyncVideoEncoder<
    TIn extends { frame: VideoFrame; index: number },
    TOut,
> extends CodecToAsyncAdapter<TIn, TOut, EncoderOutput> {
    /** The underlying encoder, exposed for `configure()` / `flush()` /
     *  `reset()` / state inspection. The wrapper handles `encode()` and
     *  the `output` callback wiring; everything else is owner-managed. */
    public readonly encoder: VideoEncoder;

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

    // Frame close is DEFERRED until the encoder's output callback fires for
    // this submission. Closing synchronously after `encode()` works for
    // source-cloned frames, but a canvas-backed frame's JS handle can be the
    // sole reference to its GPU texture; closing it before the encoder has
    // read the texture can make the encoder silently never emit output.
}
