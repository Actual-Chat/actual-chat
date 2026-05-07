import { getLogs } from 'logging';
import type { MonotonicTime } from 'clocks';
import { type Disposable, ObjectDisposedError } from 'disposable';
import { PromiseSourceWithTimeout } from 'promises';

const { warnLog } = getLogs('AsyncVideoEncoder');

// Test fault injection for encoder recovery drills. Uncomment locally when
// needed; production should use the configured timeout unchanged.
// const RANDOM_INSTANT_TIMEOUT_PROBABILITY = 0.02;

// ---- Recommended input / output types -------------------------------------
//
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

// ---- Wrapper --------------------------------------------------------------

export interface AsyncVideoEncoderOptions {
    /**
     * Initial concurrency cap. Default 2 — lets one encode submit while the
     * previous chunk is still emerging, supporting transient slow-encode
     * spikes (e.g. 40 ms on a 33 ms cadence) without dropping frames. Drops
     * to 1 permanently on the first detected ordering or correlation
     * violation; the wrapper also emits {@link onResetRequested} so the
     * owner can recreate the encoder and force a keyframe.
     */
    maxInflight?: number;
    /**
     * Steady-state per-encode timeout (ms). On timeout the pending
     * promise rejects with {@link AsyncVideoEncoderResetError}, the
     * wrapper drops to maxInflight=1, resets/reconfigures the underlying
     * encoder if it knows the latest config, and invokes
     * {@link onResetRequested}. Default 300. Set to 0 to disable.
     */
    timeoutMs?: number;
    /**
     * Timeout for the first output from an encoder. Hardware encoders can
     * take substantially longer to produce their first keyframe than their
     * steady-state cadence. Default 1500. Set to 0 to disable.
     */
    firstTimeoutMs?: number;
    /**
     * Invoked when the wrapper has decided that the underlying encoder is no
     * longer usable as-is — timeout, stale output, or out-of-order output.
     * Wrapper internal state has already been reset; the owner is expected
     * to recreate / reconfigure the encoder, force a keyframe on the next
     * submission, and call {@link AsyncVideoEncoder.handleEncoderReset}.
     */
    onResetRequested?: (reason: string) => void;
}

interface PendingEncode<TIn, TOut> {
    input: TIn;
    index: number;
    source: PromiseSourceWithTimeout<TOut>;
}

export class AsyncVideoEncoderResetError extends Error {
    readonly isRecoverable = true;

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
 * Async wrapper around a WebCodecs `VideoEncoder` that turns each `encode()`
 * call into a `Promise<TOut>` resolving when the matching `EncodedVideoChunk`
 * emerges.
 *
 * Acts as a bounded FIFO across the encoder boundary: at most `maxInflight`
 * submissions outstanding (default 2). For real-time profiles where encode
 * time < frame interval, steady-state depth is 1 — the queue exists to
 * absorb transient spikes and to thread metadata across the encoder boundary
 * (which only carries `chunk.timestamp`, no other channel).
 *
 * Output ordering: WebCodecs guarantees output is emitted in submission order
 * for non-B-frame configs, which is all we use. The wrapper enforces this
 * defensively — on any detected violation it shrinks `maxInflight` to 1 and
 * asks the owner to reset the encoder.
 *
 * Lifecycle:
 *   const enc = new AsyncVideoEncoder(buildOutput, onError, opts);
 *   enc.encoder.configure(config);
 *   const result = await enc.encode(captured, { keyFrame: true });
 *   ...
 *   enc.dispose();
 */
export class AsyncVideoEncoder<
    TIn extends { frame: VideoFrame; index: number },
    TOut,
> implements Disposable {
    /** The underlying encoder, exposed for `configure()` / `flush()` /
     *  `reset()` / state inspection. The wrapper handles `encode()` and
     *  the `output` callback wiring; everything else is owner-managed. */
    public readonly encoder: VideoEncoder;

    private readonly inflight: PendingEncode<TIn, TOut>[] = [];
    private readonly buildOutput: (
        input: TIn,
        chunk: EncodedVideoChunk,
        metadata: EncodedVideoChunkMetadata,
    ) => TOut;
    private readonly onResetRequested?: (reason: string) => void;
    private readonly timeoutMs: number;
    private readonly firstTimeoutMs: number;
    private maxInflight: number;
    private degraded = false;
    private disposed = false;
    private lastResolvedIndex = -1;
    private hasResolvedOutput = false;
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
        this.buildOutput = buildOutput;
        this.maxInflight = options.maxInflight ?? 2;
        this.timeoutMs = options.timeoutMs ?? 300;
        this.firstTimeoutMs = options.firstTimeoutMs ?? 1_500;
        this.onResetRequested = options.onResetRequested;
        this.encoder = new VideoEncoder({
            output: (chunk: EncodedVideoChunk, metadata: EncodedVideoChunkMetadata) =>
                this.onEncoderOutput(chunk, metadata),
            error: (e: unknown) => onError(e),
        });
    }

    get inflightCount(): number { return this.inflight.length; }
    get isDegraded(): boolean { return this.degraded; }
    get effectiveMaxInflight(): number { return this.maxInflight; }
    get isDisposed(): boolean { return this.disposed; }
    get state(): CodecState { return this.encoder.state; }

    configure(config: VideoEncoderConfig): void {
        this.lastConfig = { ...config };
        this.encoder.configure(config);
    }

    /**
     * Submit a frame for encoding. The returned promise resolves with the
     * encoded output (built by the caller-supplied mapper) or rejects on
     * timeout / queue full / encoder error / disposal.
     *
     * On every reject path the wrapper closes `input.frame` itself so the
     * caller doesn't have to special-case error cleanup. On the success path
     * the encoder owns the frame.
     */
    encode(input: TIn, opts: { keyFrame: boolean }): Promise<TOut> {
        if (this.disposed) {
            input.frame.close();
            return Promise.reject(new ObjectDisposedError('AsyncVideoEncoder is disposed'));
        }
        if (this.encoder.state !== 'configured') {
            input.frame.close();
            return Promise.reject(new Error(
                `AsyncVideoEncoder: encoder state is '${this.encoder.state}'`));
        }
        if (this.inflight.length >= this.maxInflight) {
            input.frame.close();
            return Promise.reject(new Error(
                `AsyncVideoEncoder: queue full (${this.inflight.length}/${this.maxInflight})`));
        }

        const pending = this.makePending(input);
        this.inflight.push(pending);

        try {
            this.encoder.encode(input.frame, { keyFrame: opts.keyFrame });
        } catch (e) {
            this.removeFromQueue(pending);
            input.frame.close();
            pending.source.reject(e);
            return pending.source;
        }
        // Frame close is DEFERRED until the encoder's output callback
        // fires for this submission (see `onEncoderOutput`). Closing
        // synchronously after `encode()` works for source-cloned frames
        // (the underlying GPU buffer survives the clone close), but a
        // canvas-backed frame's JS handle is the SOLE reference to its
        // GPU texture — closing it before the encoder has read the
        // texture leaves it with a released buffer, and it silently
        // never emits output. With 3-tier simulcast, layers 0 and 1
        // come from the WebGPU downscaler's per-layer canvas; closing
        // them post-submit triggered the 1.5s timeout consistently
        // around frame 50.
        return pending.source;
    }

    /**
     * Notify the wrapper that the underlying encoder has been reset by the
     * owner (after `encoder.reset()` / `configure()` / etc., or in response
     * to {@link AsyncVideoEncoderOptions.onResetRequested}). Clears any
     * still-queued pending entries and resets the order tracker.
     */
    handleEncoderReset(): void {
        this.failAllPending('encoder reset', true);
        this.lastResolvedIndex = -1;
        this.hasResolvedOutput = false;
    }

    /**
     * Stop accepting new submissions, fail all pending promises, and close
     * the underlying encoder if it isn't already closed. Idempotent.
     */
    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.failAllPending('disposed', false);
        if (this.encoder.state !== 'closed') {
            try { this.encoder.close(); } catch { /* ignore */ }
        }
    }

    // ---- internals --------------------------------------------------------

    private onEncoderOutput(chunk: EncodedVideoChunk, metadata: EncodedVideoChunkMetadata): void {
        if (this.disposed) {
            closeEncodedChunk(chunk);
            return;
        }
        const front = this.inflight.shift();
        if (!front) {
            closeEncodedChunk(chunk);
            return;             // post-reset / post-flush stray; ignore
        }
        // Close the input frame now — its GPU buffer has been read by
        // the encoder. See the deferred-close note in `encode()`.
        try { front.input.frame.close(); } catch { /* already closed */ }
        if (front.source.isCompleted()) {
            // Most likely the entry timed out. Order/correlation is now broken
            // — anything else in flight no longer matches its tag. Fail the
            // rest and ask owner to reset.
            this.degradeAndReset(`stale-output (index=${front.index})`);
            closeEncodedChunk(chunk);
            return;
        }
        if (this.lastResolvedIndex >= 0 && front.index <= this.lastResolvedIndex) {
            closeEncodedChunk(chunk);
            front.source.reject(new AsyncVideoEncoderResetError(
                `AsyncVideoEncoder: out-of-order output (index=${front.index} <= last=${this.lastResolvedIndex})`));
            this.degradeAndReset(
                `out-of-order: index=${front.index} <= last=${this.lastResolvedIndex}`);
            return;
        }
        this.lastResolvedIndex = front.index;
        try {
            const output = this.buildOutput(front.input, chunk, metadata);
            this.hasResolvedOutput = true;
            front.source.resolve(output);
        } catch (e) {
            closeEncodedChunk(chunk);
            front.source.reject(e);
            this.degradeAndReset(`build-output failed (index=${front.index})`);
        }
    }

    private makePending(input: TIn): PendingEncode<TIn, TOut> {
        const source = new PromiseSourceWithTimeout<TOut>();
        // Swallow unhandled-rejection — caller may not attach .catch before
        // the timeout/queue-full path rejects synchronously. Real consumers
        // get the rejection through the returned promise.
        source.catch(() => { /* ignore */ });
        const pending: PendingEncode<TIn, TOut> = { input, index: input.index, source };

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
                source.reject(new AsyncVideoEncoderResetError(
                    `AsyncVideoEncoder: encode timeout after ${timeoutMs}ms (index=${pending.index})`));
                this.degradeAndReset(`timeout (index=${pending.index})`);
            });
        }
        return pending;
    }

    private degradeAndReset(reason: string): void {
        if (this.disposed) return;
        if (!this.degraded) {
            this.degraded = true;
            this.maxInflight = 1;
            warnLog?.log(`shrinking maxInflight to 1 — ${reason}`);
        }
        this.failAllPending(reason, true);
        this.resetUnderlyingEncoder();
        this.onResetRequested?.(reason);
    }

    private failAllPending(reason: string, recoverable: boolean): void {
        while (this.inflight.length > 0) {
            const p = this.inflight.shift()!;
            try { p.input.frame.close(); } catch { /* already closed */ }
            p.source.reject(recoverable
                ? new AsyncVideoEncoderResetError(reason)
                : new Error(reason));
        }
    }

    private removeFromQueue(pending: PendingEncode<TIn, TOut>): void {
        const idx = this.inflight.indexOf(pending);
        if (idx >= 0) this.inflight.splice(idx, 1);
    }

    private resetUnderlyingEncoder(): void {
        if (this.disposed || this.encoder.state === 'closed') return;
        try {
            this.encoder.reset();
        } catch { /* ignore */ }
        this.lastResolvedIndex = -1;
        this.hasResolvedOutput = false;
        if (!this.lastConfig) return;
        try {
            this.encoder.configure(this.lastConfig);
        } catch (e) {
            warnLog?.log('reconfigure after reset failed:', e);
        }
    }
}

function closeEncodedChunk(chunk: EncodedVideoChunk): void {
    const close = (chunk as unknown as { close?: () => void }).close;
    if (typeof close !== 'function') return;
    try { close.call(chunk); } catch { /* ignore */ }
}
