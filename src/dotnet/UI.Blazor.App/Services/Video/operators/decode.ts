import { from, type PipeOperator } from 'ix-ext';
import { AsyncSignal, abortPromise } from 'actuallab-core';
import { closeEncodedChunk, type ArrivedChunk, type DecodedFrame } from '../frame-envelopes';
import {
    VideoDecoderBridge,
    CODEC_EXHAUSTED_PREFIX,
    isCodecExhaustedError,
    type DecoderLike,
    type InitialDecoderConfig,
} from '../playback/video-decoder-bridge';

// Public surface kept stable for importers (player, decoder-pool, video-player).
export { CODEC_EXHAUSTED_PREFIX, isCodecExhaustedError };
export type { DecoderLike, InitialDecoderConfig };

const DEFAULT_FRAME_DURATION_MS = 1000 / 30;

// Feed pump keeps the decoder input queue topped to this many submitted-but-not-
// output frames, independent of present pacing, so a latency-y decoder pipelines
// instead of running one-deep at the live edge.
const DEFAULT_TARGET_INFLIGHT_DEPTH = 3;
// Hard cap on decoded-VideoFrame inventory awaiting present. The receiver buffers
// ENCODED frames (cheap) upstream; decoded frames are expensive, so the pump
// pauses once this many sit in `ready`.
const DEFAULT_READY_CAP = 4;

export interface DecodeOptions {
    initialConfig: InitialDecoderConfig;
    createDecoder: (handlers: {
        onFrame: (frame: VideoFrame) => void;
        onError: (e: Error) => void;
    }) => DecoderLike;
    // Informational only — operator throws on the same tick.
    onCodecExhausted?: (codec: string) => void;
    // Fired once per stream when `framesUntilProven` decoded frames have landed
    // without resetting the counter; the bridge then switches to unbounded
    // recovery so a single transient failure never tears down a working codec.
    onCodecProven?: (codec: string) => void;
    // Fired when the decoder hangs on a not-yet-proven codec (see bridge).
    onDecoderHang?: () => void;
    framesUntilProven?: number;
    now?: () => number;
    maxRecoveries?: number;
    // Synthesises an error and drives recovery if the decoder sits on submitted
    // chunks without producing a frame or error. Default 2000 ms.
    decoderHangTimeoutMs?: number;
    setTimeoutFn?: (cb: () => void, ms: number) => unknown;
    clearTimeoutFn?: (handle: unknown) => void;
    abortSignal?: AbortSignal;
    // Source nominal frame duration (ms); decodeRatio = (decodedAt - submitMs)
    // / frameDurationMs. Defaults to 1000/30 for tests.
    frameDurationMs?: number;
    // Feed-pump backpressure knobs (tests shrink these for determinism).
    targetInFlightDepth?: number;
    readyCap?: number;
}

// ArrivedChunk -> DecodedFrame. The VideoDecoderBridge owns the decoder
// lifecycle, in-flight FIFO, recovery, and (re)configure; this operator is just
// the two async loops: a detached feed pump that submits ahead of presentation
// (bounded by targetInFlightDepth / readyCap), and the drain loop that yields
// decoded frames at the present pace and runs the hang watchdog.
export function decode(opts: DecodeOptions): PipeOperator<ArrivedChunk, DecodedFrame> {
    return source => from(decodeAsync(source, opts));
}

type WaitResult =
    | { kind: 'data' }
    | { kind: 'abort' }
    | { kind: 'watchdog' };

async function* decodeAsync(
    source: AsyncIterable<ArrivedChunk>,
    opts: DecodeOptions,
): AsyncIterable<DecodedFrame> {
    const now = opts.now ?? ((): number => performance.now());
    const setTimeoutFn = opts.setTimeoutFn ?? ((cb, ms): unknown => setTimeout(cb, ms));
    const clearTimeoutFn = opts.clearTimeoutFn
        ?? ((h: unknown): void => clearTimeout(h as ReturnType<typeof setTimeout>));
    const abortSignal = opts.abortSignal;
    const targetInFlightDepth = Math.max(1, opts.targetInFlightDepth ?? DEFAULT_TARGET_INFLIGHT_DEPTH);
    const readyCap = Math.max(1, opts.readyCap ?? DEFAULT_READY_CAP);

    const bridge = new VideoDecoderBridge({
        initialConfig: opts.initialConfig,
        createDecoder: opts.createDecoder,
        onCodecExhausted: opts.onCodecExhausted,
        onCodecProven: opts.onCodecProven,
        onDecoderHang: opts.onDecoderHang,
        framesUntilProven: Math.max(1, opts.framesUntilProven ?? 10),
        maxRecoveries: opts.maxRecoveries ?? 4,
        decoderHangTimeoutMs: opts.decoderHangTimeoutMs ?? 2_000,
        frameDurationMs: opts.frameDurationMs ?? DEFAULT_FRAME_DURATION_MS,
        now,
    });

    const abortWait: Promise<WaitResult> = abortSignal
        ? abortPromise(abortSignal).catch((): WaitResult => ({ kind: 'abort' }))
        : new Promise<WaitResult>(() => { /* never resolves */ });

    // One long-lived hang timer instead of an install/clear pair per drain
    // iteration. It's armed once for the current deadline and left running while
    // frames keep landing; each frame only moves bridge.hangDeadline() out, so
    // the timer just fires harmlessly at the old deadline and re-arms. A genuine
    // hang is confirmed at fire time by re-reading the deadline.
    const watchdogSignal = new AsyncSignal();
    let watchdogHandle: unknown = null;
    const disarmWatchdog = (): void => {
        if (watchdogHandle === null)
            return;
        try { clearTimeoutFn(watchdogHandle); } catch { /* ignore */ }
        watchdogHandle = null;
    };
    const armWatchdog = (): void => {
        // Leave a live timer running across transient pending==0 dips — tearing
        // it down every frame just reintroduces the per-frame churn this rework
        // removes. A stale fire (deadline moved out, or nothing pending) is a
        // no-op at the race winner check, which then re-arms for the real deadline.
        if (watchdogHandle !== null)
            return;
        const deadline = bridge.hangDeadline();
        if (deadline === null)
            return;
        watchdogHandle = setTimeoutFn(() => {
            watchdogHandle = null;
            watchdogSignal.notify();
        }, Math.max(0, deadline - now()));
    };

    // Manual iterator so the feed pump can race source.next() against abort.
    const sourceIter = source[Symbol.asyncIterator]();
    let nextSourcePromise: Promise<IteratorResult<ArrivedChunk>> | null = null;
    // Holder so the feed-pump closure's writes are visible to the drain loop (a
    // captured `let` would narrow to its `false` initializer in the drain loop).
    const feed = { done: false };
    const armSource = (): Promise<IteratorResult<ArrivedChunk>> => {
        nextSourcePromise ??= sourceIter.next();
        return nextSourcePromise;
    };

    let stopped = false;
    const isStopped = (): boolean => stopped || (abortSignal?.aborted ?? false);
    const canSubmit = (): boolean =>
        bridge.inFlight < targetInFlightDepth && bridge.readyCount < readyCap;

    // Feed pump: submits chunks to the decoder ahead of presentation, keeping up
    // to targetInFlightDepth in flight and at most readyCap decoded buffered.
    // Resolves (never rejects) on stop/abort; routes a fatal throw to the drain
    // loop via bridge.setError + sourceDone.
    const runFeedPump = async (): Promise<void> => {
        try {
            for (;;) {
                if (isStopped()) return;

                const sourceP = armSource();
                const result = await Promise.race([
                    sourceP.then((r): IteratorResult<ArrivedChunk> | 'abort' => r),
                    abortWait.then((): 'abort' => 'abort'),
                ]);
                if (isStopped() || result === 'abort') return;
                nextSourcePromise = null;
                if (result.done) {
                    feed.done = true;
                    bridge.whenDataReady.notify();
                    return;
                }
                const arrived = result.value;

                // Backpressure: hold the pulled chunk until the decoder has room
                // and inventory is below cap — but never stall while an error is
                // outstanding, since this chunk may be the recovery keyframe.
                for (;;) {
                    if (isStopped()) { closeEncodedChunk(arrived.chunk); return; }
                    if (bridge.error)
                        break;
                    if (canSubmit())
                        break;
                    // Arm the wakeup, then re-check, to avoid a lost signal.
                    const whenSpace = bridge.whenSpaceAvailable.wait();
                    if (canSubmit())
                        continue;
                    await Promise.race([whenSpace, abortWait]);
                }
                if (isStopped()) { closeEncodedChunk(arrived.chunk); return; }

                bridge.submit(arrived);
            }
        } catch (e) {
            // Surface a fatal feed error (e.g. recovery exhausted) to the consumer.
            bridge.setError(e);
            feed.done = true;
            bridge.whenDataReady.notify();
        }
    };

    const feedDone = runFeedPump();

    try {
        for (;;) {
            if (abortSignal?.aborted)
                return;
            // Arm the data/watchdog wakeups before draining/checking so a frame
            // that lands during this iteration can't be lost between the check
            // and the await.
            const whenData = bridge.whenDataReady.wait();
            const whenWatchdog = watchdogSignal.wait();
            let envelope: DecodedFrame | null;
            while ((envelope = bridge.tryPull()) !== null) {
                let mustClose = true;
                try {
                    mustClose = false;
                    yield envelope;
                } finally {
                    if (mustClose)
                        try { envelope.frame.close(); } catch { /* ignore */ }
                }
            }
            if (bridge.error && feed.done)
                throw bridge.error;
            if (feed.done && bridge.inFlight === 0)
                return;

            // Arms only when the decoder owes us a frame, and only if not already
            // armed (a no-op while the timer keeps running).
            armWatchdog();
            const winner = await Promise.race([
                whenData.then((): WaitResult => ({ kind: 'data' })),
                whenWatchdog.then((): WaitResult => ({ kind: 'watchdog' })),
                abortWait,
            ]);

            if (winner.kind === 'abort')
                return;
            if (winner.kind === 'watchdog') {
                // Confirm the hang at fire time: a frame may have landed and moved
                // the deadline out, making this a stale fire to ignore (the next
                // iteration re-arms for the new deadline).
                const deadline = bridge.hangDeadline();
                if (deadline !== null && now() >= deadline)
                    bridge.onWatchdogFired(now());
            }
        }
    } finally {
        stopped = true;
        disarmWatchdog();
        bridge.whenSpaceAvailable.notify();
        try { await feedDone; } catch { /* ignore */ }
        bridge.dispose();
        try { await sourceIter.return?.(); } catch { /* ignore */ }
    }
}
