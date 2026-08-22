import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
    AsyncVideoEncoder,
    type CapturedFrame,
    type EncodedFrame,
} from '../../../src/dotnet/UI.Blazor.App/Services/Video/adapters';
import { isDisposable } from 'disposable';

// ---- Mocks for the WebCodecs surface --------------------------------------
//
// The wrapper only touches:
//   - new VideoEncoder({ output, error })
//   - encoder.state
//   - encoder.encode(frame, opts)
//   - encoder.close()
//
// VideoFrame's only required method here is .close() (called by the wrapper
// on every reject path). EncodedVideoChunk / EncodedVideoChunkMetadata are
// passed through opaquely.

interface MockVideoEncoderInit {
    output: (chunk: EncodedVideoChunk, metadata: EncodedVideoChunkMetadata) => void;
    error: (e: unknown) => void;
}

class MockVideoEncoder {
    static instances: MockVideoEncoder[] = [];
    state: 'unconfigured' | 'configured' | 'closed' = 'configured';
    encodeCalls: { frame: MockVideoFrame; opts: { keyFrame: boolean } }[] = [];
    configureCalls: VideoEncoderConfig[] = [];
    resetCalls = 0;
    encodeShouldThrow: Error | null = null;
    output: MockVideoEncoderInit['output'];
    error: MockVideoEncoderInit['error'];

    constructor(init: MockVideoEncoderInit) {
        this.output = init.output;
        this.error = init.error;
        MockVideoEncoder.instances.push(this);
    }

    encode(frame: MockVideoFrame, opts: { keyFrame: boolean }): void {
        if (this.encodeShouldThrow) {
            const e = this.encodeShouldThrow;
            this.encodeShouldThrow = null;
            throw e;
        }
        this.encodeCalls.push({ frame, opts });
    }

    close(): void {
        this.state = 'closed';
    }

    configure(config: VideoEncoderConfig): void {
        this.configureCalls.push(config);
        this.state = 'configured';
    }

    reset(): void {
        this.resetCalls++;
        this.encodeCalls.length = 0;
        this.state = 'configured';
    }

    /** Test helper: trigger output for the next pending submission, FIFO. */
    emitNext(meta: EncodedVideoChunkMetadata = {}): void {
        const call = this.encodeCalls.shift();
        if (!call) throw new Error('emitNext: no pending encode');
        const chunk = { type: call.opts.keyFrame ? 'key' : 'delta', timestamp: 0 } as unknown as EncodedVideoChunk;
        this.output(chunk, meta);
    }

    /** Test helper: emit raw chunk without consuming a queued submission. */
    emitRaw(chunk: EncodedVideoChunk, meta: EncodedVideoChunkMetadata = {}): void {
        this.output(chunk, meta);
    }
}

class MockVideoFrame {
    closed = false;
    constructor(public id = 0) {}
    close(): void { this.closed = true; }
}

interface GlobalWithVideoEncoder {
    VideoEncoder?: typeof MockVideoEncoder;
}

beforeEach(() => {
    MockVideoEncoder.instances = [];
    (globalThis as unknown as GlobalWithVideoEncoder).VideoEncoder = MockVideoEncoder;
});

afterEach(() => {
    delete (globalThis as unknown as GlobalWithVideoEncoder).VideoEncoder;
    vi.useRealTimers();
});

// ---- Helpers --------------------------------------------------------------

type Captured = CapturedFrame<{ tag: string }>;
type Encoded = EncodedFrame<{ tag: string }>;

function buildOutput(input: Captured, chunk: EncodedVideoChunk, metadata: EncodedVideoChunkMetadata): Encoded {
    return {
        chunk,
        metadata,
        capturedAt: input.capturedAt,
        index: input.index,
        meta: input.meta,
    };
}

function makeCaptured(index: number, tag = `f${index}`, frame?: MockVideoFrame): Captured {
    return {
        frame: (frame ?? new MockVideoFrame(index)) as unknown as VideoFrame,
        capturedAt: { timeMs: 1_700_000_000_000 + index, epoch: 0 },
        index,
        meta: { tag },
    };
}

function makeEncoder(opts: {
    onError?: (e: unknown) => void;
    onResetRequested?: (reason: string) => void;
    maxInflight?: number;
    timeoutMs?: number;
    firstTimeoutMs?: number;
} = {}) {
    const onError = opts.onError ?? (() => { /* swallow */ });
    return new AsyncVideoEncoder<Captured, Encoded>(
        buildOutput,
        onError,
        {
            maxInflight: opts.maxInflight,
            timeoutMs: opts.timeoutMs ?? 100,
            firstTimeoutMs: opts.firstTimeoutMs ?? opts.timeoutMs ?? 100,
            onResetRequested: opts.onResetRequested,
        },
    );
}

// ---- Tests ----------------------------------------------------------------

describe('AsyncVideoEncoder', () => {
    it('resolves with a built output threading metadata through the boundary', async () => {
        const enc = makeEncoder();
        const mock = MockVideoEncoder.instances[0];

        const captured = makeCaptured(1, 'first');
        const promise = enc.encode(captured, { keyFrame: true });
        expect(enc.inflightCount).toBe(1);

        mock.emitNext({ decoderConfig: { codec: 'avc1.640028' } as VideoDecoderConfig });
        const out = await promise;

        expect(out.index).toBe(1);
        expect(out.capturedAt.timeMs).toBe(captured.capturedAt.timeMs);
        expect(out.meta.tag).toBe('first');
        expect(out.chunk.type).toBe('key');
        expect(out.metadata.decoderConfig?.codec).toBe('avc1.640028');
        expect(enc.inflightCount).toBe(0);
        expect(enc.isDegraded).toBe(false);
    });

    it('preserves FIFO order across two concurrent submissions', async () => {
        const enc = makeEncoder();
        const mock = MockVideoEncoder.instances[0];

        const p1 = enc.encode(makeCaptured(1, 'a'), { keyFrame: false });
        const p2 = enc.encode(makeCaptured(2, 'b'), { keyFrame: false });
        expect(enc.inflightCount).toBe(2);

        mock.emitNext();    // matches first submission
        const r1 = await p1;
        mock.emitNext();    // matches second submission
        const r2 = await p2;

        expect(r1.meta.tag).toBe('a');
        expect(r2.meta.tag).toBe('b');
        expect(enc.inflightCount).toBe(0);
    });

    it('rejects with "queue full" when inflight cap is reached', async () => {
        const enc = makeEncoder({ maxInflight: 2 });

        const p1 = enc.encode(makeCaptured(1), { keyFrame: false });
        const p2 = enc.encode(makeCaptured(2), { keyFrame: false });
        const overflow = makeCaptured(3);
        await expect(enc.encode(overflow, { keyFrame: false })).rejects.toThrow(/queue full/);
        // Overflow frame must be closed by the wrapper.
        expect((overflow.frame as unknown as MockVideoFrame).closed).toBe(true);
        // First two are still in flight, untouched.
        expect(enc.inflightCount).toBe(2);
        // Drain to keep promises from leaking.
        const mock = MockVideoEncoder.instances[0];
        mock.emitNext(); mock.emitNext();
        await Promise.all([p1, p2]);
    });

    it('rejects when encoder.encode throws synchronously and closes the frame', async () => {
        const enc = makeEncoder();
        const mock = MockVideoEncoder.instances[0];
        mock.encodeShouldThrow = new Error('encoder explosion');

        const captured = makeCaptured(1);
        await expect(enc.encode(captured, { keyFrame: false })).rejects.toThrow(/encoder explosion/);
        expect((captured.frame as unknown as MockVideoFrame).closed).toBe(true);
        expect(enc.inflightCount).toBe(0);
    });

    it('rejects encode when encoder is not in "configured" state', async () => {
        const enc = makeEncoder();
        const mock = MockVideoEncoder.instances[0];
        mock.state = 'unconfigured';

        const captured = makeCaptured(1);
        await expect(enc.encode(captured, { keyFrame: false })).rejects.toThrow(/state is 'unconfigured'/);
        expect((captured.frame as unknown as MockVideoFrame).closed).toBe(true);
    });

    it('times out a stuck encode, degrades, and signals onResetRequested', async () => {
        vi.useFakeTimers();
        const onResetReasons: string[] = [];
        const enc = makeEncoder({
            timeoutMs: 50,
            onResetRequested: (r) => onResetReasons.push(r),
        });

        const captured = makeCaptured(1, 'stuck');
        const promise = enc.encode(captured, { keyFrame: false });
        // Don't emit anything — let the timeout fire.
        const rejection = expect(promise).rejects.toThrow(/encode timeout/);
        await vi.advanceTimersByTimeAsync(60);
        await rejection;

        expect(enc.isDegraded).toBe(true);
        expect(enc.effectiveMaxInflight).toBe(1);
        expect(onResetReasons.length).toBe(1);
        expect(onResetReasons[0]).toMatch(/timeout/);
    });

    it('doubles the clean-output bar each time a degraded adapter is restored', async () => {
        vi.useFakeTimers();
        const enc = makeEncoder({ timeoutMs: 50 });
        const mock = MockVideoEncoder.instances[0];
        // Outputs are verified FIFO by index, so it has to keep climbing across degrades.
        let nextIndex = 1;
        const degrade = async (): Promise<void> => {
            const stuck = enc.encode(makeCaptured(nextIndex++, 'stuck'), { keyFrame: false });
            const rejection = expect(stuck).rejects.toThrow(/encode timeout/);
            await vi.advanceTimersByTimeAsync(60);
            await rejection;
        };
        const emitClean = async (count: number): Promise<void> => {
            for (let i = 0; i < count; i++) {
                const p = enc.encode(makeCaptured(nextIndex++), { keyFrame: false });
                mock.emitNext();
                await p;
            }
        };

        // arrange: the first degrade lifts after 30 clean outputs
        await degrade();
        await emitClean(29);
        expect(enc.isDegraded).toBe(true);
        await emitClean(1);
        expect(enc.isDegraded).toBe(false);

        // act: degrading again puts the bar at 60
        await degrade();
        await emitClean(59);

        // assert
        expect(enc.isDegraded).toBe(true);
        await emitClean(1);
        expect(enc.isDegraded).toBe(false);
    });

    it('uses a wider first-output timeout, then steady-state timeout', async () => {
        vi.useFakeTimers();
        const enc = makeEncoder({ firstTimeoutMs: 1_500, timeoutMs: 300 });
        const mock = MockVideoEncoder.instances[0];

        const first = enc.encode(makeCaptured(1), { keyFrame: true });
        await vi.advanceTimersByTimeAsync(1_000);
        expect(enc.inflightCount).toBe(1);
        mock.emitNext();
        await first;

        const second = enc.encode(makeCaptured(2), { keyFrame: false });
        const rejection = expect(second).rejects.toThrow(/encode timeout after 300ms/);
        await vi.advanceTimersByTimeAsync(301);
        await rejection;
    });

    it('resets and reconfigures on timeout when configured through the wrapper', async () => {
        vi.useFakeTimers();
        const enc = makeEncoder({ timeoutMs: 50 });
        const mock = MockVideoEncoder.instances[0];
        const config: VideoEncoderConfig = { codec: 'avc1.640028', width: 640, height: 360 };
        enc.configure(config);

        const promise = enc.encode(makeCaptured(1), { keyFrame: false });
        const rejection = expect(promise).rejects.toThrow(/encode timeout/);
        await vi.advanceTimersByTimeAsync(60);
        await rejection;

        expect(mock.resetCalls).toBe(1);
        expect(mock.configureCalls).toEqual([config, config]);
    });

    it('handleEncoderReset clears pending and resets the order tracker', async () => {
        vi.useFakeTimers();
        const enc = makeEncoder({ timeoutMs: 50 });
        const mock = MockVideoEncoder.instances[0];

        // Two in-flight; first will time out (and degradeAndReset will fail both).
        const p1 = enc.encode(makeCaptured(1), { keyFrame: false });
        const p2 = enc.encode(makeCaptured(2), { keyFrame: false });

        const r1 = expect(p1).rejects.toThrow(/timeout/);
        const r2 = expect(p2).rejects.toThrow();
        await vi.advanceTimersByTimeAsync(60);
        await Promise.all([r1, r2]);

        expect(enc.inflightCount).toBe(0);

        // Owner-driven reset; queue clean.
        enc.handleEncoderReset();
        expect(enc.inflightCount).toBe(0);

        // After reset, new submissions land at the head with effectiveMaxInflight=1.
        vi.useRealTimers();
        const p3 = enc.encode(makeCaptured(3), { keyFrame: true });
        expect(enc.inflightCount).toBe(1);
        // Cap is 1 now; another encode should be rejected immediately.
        await expect(enc.encode(makeCaptured(4), { keyFrame: false })).rejects.toThrow(/queue full/);
        mock.emitNext();
        await p3;
    });

    it('silently discards a late chunk when the queue is empty (post-reset)', async () => {
        vi.useFakeTimers();
        const onResetReasons: string[] = [];
        const enc = makeEncoder({
            timeoutMs: 50,
            onResetRequested: (r) => onResetReasons.push(r),
        });

        const p = enc.encode(makeCaptured(1), { keyFrame: false });
        const rejection = expect(p).rejects.toThrow(/timeout/);
        await vi.advanceTimersByTimeAsync(60);
        await rejection;

        // Queue is empty after timeout (degradeAndReset failed all pending).
        expect(enc.inflightCount).toBe(0);
        // Late chunk: silent discard, no further degradation events.
        const mock = MockVideoEncoder.instances[0];
        const lateChunk: { type: string; closed: boolean; close: () => void } = {
            type: 'delta',
            closed: false,
            close(): void { lateChunk.closed = true; },
        };
        mock.emitRaw(lateChunk as unknown as EncodedVideoChunk);
        expect(onResetReasons.length).toBe(1);
        expect(lateChunk.closed).toBe(true);
    });

    it('detects out-of-order index numbers (descending) and degrades', async () => {
        const onResetReasons: string[] = [];
        const enc = makeEncoder({
            onResetRequested: (r) => onResetReasons.push(r),
        });
        const mock = MockVideoEncoder.instances[0];

        const p1 = enc.encode(makeCaptured(5), { keyFrame: false });
        const p2 = enc.encode(makeCaptured(3), { keyFrame: false });   // index 3 < index 5

        mock.emitNext();        // resolves p1 with index=5; lastResolvedIndex=5
        await p1;
        mock.emitNext();        // would resolve p2 with index=3; 3 <= 5 → violation
        await expect(p2).rejects.toThrow(/out-of-order/);

        expect(enc.isDegraded).toBe(true);
        expect(onResetReasons[0]).toMatch(/out-of-order/);
    });

    it('treats equal index numbers as a violation (≤ not <)', async () => {
        const enc = makeEncoder();
        const mock = MockVideoEncoder.instances[0];

        const p1 = enc.encode(makeCaptured(7), { keyFrame: false });
        const p2 = enc.encode(makeCaptured(7), { keyFrame: false });   // duplicate

        mock.emitNext();
        await p1;
        mock.emitNext();
        await expect(p2).rejects.toThrow(/out-of-order/);
        expect(enc.isDegraded).toBe(true);
    });

    it('dispose fails all pending and closes the encoder; idempotent', async () => {
        const enc = makeEncoder();
        const mock = MockVideoEncoder.instances[0];

        const p1 = enc.encode(makeCaptured(1), { keyFrame: false });
        const p2 = enc.encode(makeCaptured(2), { keyFrame: false });

        enc.dispose();
        await expect(p1).rejects.toThrow(/disposed/);
        await expect(p2).rejects.toThrow(/disposed/);
        expect(mock.state).toBe('closed');
        expect(enc.isDisposed).toBe(true);

        // Subsequent encode rejects immediately and closes the supplied frame.
        const c3 = makeCaptured(3);
        await expect(enc.encode(c3, { keyFrame: false })).rejects.toThrow(/disposed/);
        expect((c3.frame as unknown as MockVideoFrame).closed).toBe(true);

        // Idempotent dispose.
        enc.dispose();
        expect(enc.isDisposed).toBe(true);
    });

    it('implements Disposable (isDisposable detects it)', () => {
        const enc = makeEncoder();
        expect(isDisposable(enc)).toBe(true);
        enc.dispose();
    });

    it('exposes underlying encoder via .encoder for owner-driven configure/state', () => {
        const enc = makeEncoder();
        expect(enc.encoder).toBe(MockVideoEncoder.instances[0]);
        expect(enc.state).toBe('configured');
    });

    it('does not regress on a long stable run of in-order encodes', async () => {
        const enc = makeEncoder({ maxInflight: 2 });
        const mock = MockVideoEncoder.instances[0];

        const promises: Promise<Encoded>[] = [];
        for (let index = 1; index <= 20; index++) {
            // Don't exceed maxInflight: alternate submit, drain.
            promises.push(enc.encode(makeCaptured(index), { keyFrame: index === 1 }));
            if (index % 2 === 0) {
                mock.emitNext();
                mock.emitNext();
            }
        }
        const results = await Promise.all(promises);
        expect(results.map(r => r.index)).toEqual(
            Array.from({ length: 20 }, (_, i) => i + 1));
        expect(enc.isDegraded).toBe(false);
    });
});
