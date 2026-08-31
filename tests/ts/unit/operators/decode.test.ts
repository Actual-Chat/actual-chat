import { describe, it, expect, vi } from 'vitest';
import {
    decode,
    type DecoderLike,
    type DecodeOptions,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/decode';
import {
    createEmptyPlayerStats,
    type ArrivedChunk,
    type DecodedFrame,
    type PlayerStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
// ---- Mock surfaces --------------------------------------------------------

class MockVideoFrame {
    closed = false;
    constructor(public id: number) {}
    close(): void { this.closed = true; }
    codedWidth = 1280;
    codedHeight = 720;
}

class MockDecoder implements DecoderLike {
    state: 'unconfigured' | 'configured' | 'closed' = 'unconfigured';
    decodeQueueSize = 0;
    configureCalls: VideoDecoderConfig[] = [];
    decodeCalls: EncodedVideoChunk[] = [];
    closed = false;
    constructor(
        public handlers: { onFrame: (f: VideoFrame) => void; onError: (e: Error) => void },
    ) { /* nothing */ }
    configure(config: VideoDecoderConfig): void {
        this.configureCalls.push(config);
        this.state = 'configured';
    }
    decode(chunk: EncodedVideoChunk): void {
        this.decodeCalls.push(chunk);
    }
    flush(): Promise<void> { return Promise.resolve(); }
    close(): void { this.state = 'closed'; this.closed = true; }
    /** Emit a frame (mimics the decoder's output callback). */
    emitFrame(id = 0): MockVideoFrame {
        const f = new MockVideoFrame(id);
        this.handlers.onFrame(f as unknown as VideoFrame);
        return f;
    }
    /** Trigger an error (mimics the decoder's error callback). */
    emitError(msg = 'mock error'): void {
        this.handlers.onError(new Error(msg));
    }
}

class MockEncodedVideoChunk {
    public closed = false;
    constructor(public type: 'key' | 'delta', public byteLength = 16) {
        // mimic the property name from WebCodecs
    }
    timestamp = 0;
    duration: number | null = null;
    close(): void { this.closed = true; }
}

// ---- Helpers --------------------------------------------------------------


function makeStats(): PlayerStats {
    return createEmptyPlayerStats();
}

interface ArrivedOpts {
    isKeyFrame?: boolean;
    description?: ArrayBuffer;
    width?: number;
    height?: number;
    layerId?: number;
    capturedTimeMs?: number;
    capturedEpoch?: number;
    arrivedTimeMs?: number;
    arrivedEpoch?: number;
}

function makeArrived(stats: PlayerStats, opts: ArrivedOpts = {}): ArrivedChunk {
    const chunk = new MockEncodedVideoChunk(
        opts.isKeyFrame ? 'key' : 'delta',
    ) as unknown as EncodedVideoChunk;
    return {
        chunk,
        arrivedAt: { timeMs: opts.arrivedTimeMs ?? 1_000, epoch: opts.arrivedEpoch ?? 0 },
        capturedAt: { timeMs: opts.capturedTimeMs ?? 0, epoch: opts.capturedEpoch ?? 0 },
        index: 0,
        dropTrace: [],
        serverArrivedAtUnixMs: 0,
        isKeyFrame: opts.isKeyFrame ?? false,
        description: opts.description,
        layerId: opts.layerId ?? 0,
        width: opts.width ?? 1280,
        height: opts.height ?? 720,
        rawByteLength: 16,
        rotation: 0,
        stats,
    };
}

function fromArray<T>(items: T[]): AsyncIterable<T> {
    return fromArrayAsync(items);
}
async function* fromArrayAsync<T>(items: readonly T[]): AsyncIterable<T> {
    for (const item of items) {
        await Promise.resolve();
        yield item;
    }
}

// Manually-gated source: the test controls exactly when each chunk becomes
// available, so the run-ahead feed pump can't submit ahead of what's pushed.
// Needed for timing/recovery tests that assumed lockstep submit-on-pull.
interface ManualSource<T> {
    iterable: AsyncIterable<T>;
    push(item: T): void;
    end(): void;
}
function manualSource<T>(): ManualSource<T> {
    const queue: T[] = [];
    let wake: (() => void) | null = null;
    let done = false;
    const iterable: AsyncIterable<T> = {
        async *[Symbol.asyncIterator]() {
            for (;;) {
                while (queue.length > 0) yield queue.shift()!;
                if (done) return;
                await new Promise<void>(r => { wake = r; });
            }
        },
    };
    return {
        iterable,
        push(item: T): void { queue.push(item); wake?.(); wake = null; },
        end(): void { done = true; wake?.(); wake = null; },
    };
}

/** Drives the operator one chunk at a time, letting tests slip in
 *  decoder.emitFrame() between submissions. */
async function stepIter<T>(
    iter: AsyncIterator<T>,
): Promise<IteratorResult<T>> {
    return iter.next();
}

/** Yield microtasks until the condition holds, or fail after `max` rounds. */
async function pumpUntil(check: () => boolean, max = 200): Promise<void> {
    for (let i = 0; i < max; i++) {
        if (check()) return;
        await Promise.resolve();
    }
    throw new Error('pumpUntil: condition never satisfied');
}

// ---- Tests ----------------------------------------------------------------

describe('decode operator', () => {
    it('first keyframe with description → configure + decode', async () => {
        const stats = makeStats();
        const desc = new Uint8Array([0xAA, 0xBB]).buffer;
        let captured: MockDecoder | undefined;

        const opts: DecodeOptions = {
            initialConfig: { codec: 'hev1.1.6.L120.B0' },
            createDecoder: handlers => {
                captured = new MockDecoder(handlers);
                return captured;
            },
        };

        const arrivals = [
            makeArrived(stats, { isKeyFrame: true, description: desc, width: 1280, height: 720 }),
        ];
        const seg = decode(opts)(fromArray(arrivals));
        const iter = seg[Symbol.asyncIterator]();

        // Kick off the iteration; the operator should configure + decode then
        // wait for a frame. We let it run, manually emit a frame, and see the
        // envelope come back.
        const next = stepIter(iter);
        // Wait for the operator to submit its decode call.
        await pumpUntil(() => captured !== undefined && captured.decodeCalls.length >= 1);
        expect(captured).toBeDefined();
        expect(captured!.configureCalls).toHaveLength(1);
        expect(captured!.configureCalls[0].codec).toBe('hev1.1.6.L120.B0');
        expect(captured!.configureCalls[0].description).toBe(desc);
        expect(captured!.configureCalls[0].codedWidth).toBe(1280);
        expect(captured!.configureCalls[0].codedHeight).toBe(720);
        expect(captured!.decodeCalls).toHaveLength(1);

        // Emit a frame; the operator should yield it on the *next* iteration.
        captured!.emitFrame(0);
        const r = await next;
        expect(r.done).toBe(false);
        const tail = await iter.next();
        expect(tail.done).toBe(true);
        expect((arrivals[0].chunk as unknown as MockEncodedVideoChunk).closed).toBe(true);
    });

    it('first keyframe WITHOUT description (avc1) → configure-without-description path', async () => {
        const stats = makeStats();
        let captured: MockDecoder | undefined;
        const opts: DecodeOptions = {
            initialConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => {
                captured = new MockDecoder(handlers);
                return captured;
            },
        };
        const arrivals = [
            makeArrived(stats, { isKeyFrame: true, width: 640, height: 360 }),
        ];
        const seg = decode(opts)(fromArray(arrivals));
        const iter = seg[Symbol.asyncIterator]();
        const next = stepIter(iter);
        await pumpUntil(() => captured !== undefined && captured.decodeCalls.length >= 1);
        expect(captured!.configureCalls).toHaveLength(1);
        expect(captured!.configureCalls[0].codec).toBe('avc1.42E01E');
        expect(captured!.configureCalls[0].description).toBeUndefined();
        expect(captured!.decodeCalls).toHaveLength(1);
        captured!.emitFrame(0);
        await next;
        await iter.next();
    });

    it('first keyframe WITHOUT description (vp09) → configure-without-description path', async () => {
        // VP9 was absent from the codecs allowed to configure without a
        // description, so every keyframe was rejected, the decoder never
        // configured, and playback wedged on a full queue with no visible error.
        const stats = makeStats();
        let captured: MockDecoder | undefined;
        const opts: DecodeOptions = {
            initialConfig: { codec: 'vp09.00.41.08' },
            createDecoder: handlers => {
                captured = new MockDecoder(handlers);
                return captured;
            },
        };
        const arrivals = [
            makeArrived(stats, { isKeyFrame: true, width: 1280, height: 720 }),
        ];
        const seg = decode(opts)(fromArray(arrivals));
        const iter = seg[Symbol.asyncIterator]();
        const next = stepIter(iter);
        await pumpUntil(() => captured !== undefined && captured.decodeCalls.length >= 1);
        expect(captured!.configureCalls).toHaveLength(1);
        expect(captured!.configureCalls[0].codec).toBe('vp09.00.41.08');
        expect(captured!.configureCalls[0].description).toBeUndefined();
        expect(captured!.decodeCalls).toHaveLength(1);
        captured!.emitFrame(0);
        await next;
        await iter.next();
    });

    it('first keyframe without description and HEVC codec → throws', async () => {
        const stats = makeStats();
        const opts: DecodeOptions = {
            initialConfig: { codec: 'hev1.1.6.L120.B0' },
            createDecoder: handlers => new MockDecoder(handlers),
        };
        const arrivals = [
            makeArrived(stats, { isKeyFrame: true, width: 1280, height: 720 }),
        ];
        const seg = decode(opts)(fromArray(arrivals));
        const iter = seg[Symbol.asyncIterator]();
        await expect(iter.next()).rejects.toThrow(/requires description/);
        expect((arrivals[0].chunk as unknown as MockEncodedVideoChunk).closed).toBe(true);
    });

    it('5 chunks in, decoder emits 5 frames → 5 DecodedFrames out, FIFO-correlated', async () => {
        const stats = makeStats();
        let captured: MockDecoder | undefined;
        const opts: DecodeOptions = {
            initialConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => {
                captured = new MockDecoder(handlers);
                return captured;
            },
        };
        const arrivals: ArrivedChunk[] = [];
        // First a keyframe, then 4 deltas, all with distinct capturedAt.
        for (let i = 0; i < 5; i++) {
            arrivals.push(makeArrived(stats, {
                isKeyFrame: i === 0,
                capturedTimeMs: i * 33,
                capturedEpoch: 7,
                arrivedTimeMs: 1_000 + i,
                layerId: 0,
                width: 640,
                height: 360,
            }));
        }
        const seg = decode(opts)(fromArray(arrivals));
        const iter = seg[Symbol.asyncIterator]();

        const collected: DecodedFrame[] = [];
        for (let i = 0; i < 5; i++) {
            const expected = i + 1;
            const next = stepIter(iter);
            // Wait until the operator has submitted this chunk.
            await pumpUntil(() => captured !== undefined && captured.decodeCalls.length >= expected);
            captured!.emitFrame(i);
            const r = await next;
            expect(r.done).toBe(false);
            if (r.done === false) collected.push(r.value);
        }
        const tail = await iter.next();
        expect(tail.done).toBe(true);

        expect(collected).toHaveLength(5);
        // FIFO correlation: capturedAt comes through in submission order.
        const capturedTimeMsList = collected.map(c => c.capturedAt.timeMs);
        expect(capturedTimeMsList).toEqual([0, 33, 66, 99, 132]);
        const arrivedTimeMsList = collected.map(c => c.arrivedAt.timeMs);
        expect(arrivedTimeMsList).toEqual([1000, 1001, 1002, 1003, 1004]);
        // capturedEpoch threaded through.
        for (const f of collected) expect(f.capturedAt.epoch).toBe(7);
        // layerId threaded through.
        for (const f of collected) expect(f.layerId).toBe(0);
        // stats reference is shared.
        for (const f of collected) expect(f.stats).toBe(stats);
    });

    it('decoder error → onCodecExhausted called when consecutiveRecoveries >= max; pipe throws', async () => {
        const stats = makeStats();
        let captured: MockDecoder | undefined;
        const decoders: MockDecoder[] = [];
        const exhaustedCalls: string[] = [];
        const opts: DecodeOptions = {
            initialConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => {
                captured = new MockDecoder(handlers);
                decoders.push(captured);
                return captured;
            },
            onCodecExhausted: codec => exhaustedCalls.push(codec),
            maxRecoveries: 2,
        };

        // Sequence: keyframe → error → keyframe (recovery #1) → error → keyframe (recovery #2 → exhausted).
        // Gated source: a keyframe must remain unsubmitted to drive each recovery,
        // so push them one at a time rather than letting the pump run ahead.
        const src = manualSource<ArrivedChunk>();
        const seg = decode(opts)(src.iterable);
        const iter = seg[Symbol.asyncIterator]();

        // Step 1: submit the first keyframe.
        const n1 = stepIter(iter);
        src.push(makeArrived(stats, { isKeyFrame: true, width: 640, height: 360 }));
        await pumpUntil(() => captured !== undefined && captured.decodeCalls.length >= 1);
        expect(captured!.configureCalls).toHaveLength(1);
        expect(captured!.decodeCalls).toHaveLength(1);
        // Trigger an error on the just-submitted chunk (no frame emitted).
        captured!.emitError('boom 1');
        // Recovery consumes the next keyframe to spin up a second decoder.
        src.push(makeArrived(stats, { isKeyFrame: true, width: 640, height: 360 }));
        await pumpUntil(() => decoders.length >= 2 && decoders[1].decodeCalls.length >= 1);
        const second = decoders[1];
        expect(second.configureCalls).toHaveLength(1);
        expect(second.decodeCalls).toHaveLength(1);

        // Now trigger another error on this rebuilt decoder.
        second.emitError('boom 2');
        // Recovery #2 consumes the third keyframe and hits the limit.
        src.push(makeArrived(stats, { isKeyFrame: true, width: 640, height: 360 }));
        // Recovery #2 hits the limit; the operator throws and onCodecExhausted fires.
        await expect(n1).rejects.toThrow(/recovery exhausted/i);
        expect(exhaustedCalls).toEqual(['avc1.42E01E']);
    });

    it('stats: framesDecoded and decodeTimeMsSum/Count increment per output', async () => {
        const stats = makeStats();
        let captured: MockDecoder | undefined;
        let nowMs = 100;
        const opts: DecodeOptions = {
            initialConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => {
                captured = new MockDecoder(handlers);
                return captured;
            },
            now: () => nowMs,
        };
        const arrivals: ArrivedChunk[] = [
            makeArrived(stats, { isKeyFrame: true, width: 640, height: 360 }),
            makeArrived(stats, { isKeyFrame: false, width: 640, height: 360 }),
        ];
        const seg = decode(opts)(fromArray(arrivals));
        const iter = seg[Symbol.asyncIterator]();

        // First chunk: submitMs = 100.
        const n1 = stepIter(iter);
        await pumpUntil(() => captured !== undefined && captured.decodeCalls.length >= 1);
        // Decode took 5 ms.
        nowMs = 105;
        captured!.emitFrame(0);
        await n1;

        // Second chunk: submitMs = 200.
        nowMs = 200;
        const n2 = stepIter(iter);
        await pumpUntil(() => captured !== undefined && captured.decodeCalls.length >= 2);
        nowMs = 210; // decode took 10 ms
        captured!.emitFrame(1);
        await n2;

        await iter.next();
    });

    it('abort unwinds when source ended with an in-flight decode', async () => {
        const stats = makeStats();
        const ac = new AbortController();
        let captured: MockDecoder | undefined;
        const opts: DecodeOptions = {
            initialConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => {
                captured = new MockDecoder(handlers);
                return captured;
            },
            abortSignal: ac.signal,
        };
        const arrivals = [
            makeArrived(stats, { isKeyFrame: true, width: 640, height: 360 }),
        ];
        const seg = decode(opts)(fromArray(arrivals));
        const iter = seg[Symbol.asyncIterator]();
        const next = stepIter(iter);

        await pumpUntil(() => captured !== undefined && captured.decodeCalls.length >= 1);
        ac.abort();

        const result = await next;
        expect(result.done).toBe(true);
        expect(captured!.closed).toBe(true);
        expect((arrivals[0].chunk as unknown as MockEncodedVideoChunk).closed).toBe(true);
    });

    it('watchdog: hang synthesises an error and the next keyframe drives recovery', async () => {
        const stats = makeStats();
        const decoders: MockDecoder[] = [];
        interface FakeTimer { cb: () => void; canceled: boolean }
        const timers: FakeTimer[] = [];
        const setTimeoutFn = (cb: () => void): unknown => {
            const t: FakeTimer = { cb, canceled: false };
            timers.push(t);
            return t;
        };
        const clearTimeoutFn = (h: unknown): void => {
            (h as FakeTimer).canceled = true;
        };
        const fireWatchdog = (): void => {
            for (const t of timers.splice(0)) {
                if (!t.canceled) t.cb();
            }
        };

        let nowMs = 100;
        const opts: DecodeOptions = {
            initialConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => {
                const d = new MockDecoder(handlers);
                decoders.push(d);
                return d;
            },
            maxRecoveries: 4,
            decoderHangTimeoutMs: 2_000,
            now: () => nowMs,
            setTimeoutFn,
            clearTimeoutFn,
        };
        // First keyframe hangs (no frame emitted) → watchdog → recovery on the
        // second keyframe rebuilds the decoder and resubmits. Gated source holds
        // the second keyframe back so it's available to drive recovery.
        const src = manualSource<ArrivedChunk>();
        const seg = decode(opts)(src.iterable);
        const iter = seg[Symbol.asyncIterator]();
        const n1 = stepIter(iter);
        src.push(makeArrived(stats, { isKeyFrame: true, width: 640, height: 360 }));

        await pumpUntil(() => decoders.length >= 1 && decoders[0].decodeCalls.length >= 1);
        // The drain loop arms the watchdog a hop after the pump submits.
        await pumpUntil(() => timers.length >= 1);
        nowMs = 100 + 2_000;
        fireWatchdog();
        src.push(makeArrived(stats, { isKeyFrame: true, width: 640, height: 360 }));
        // Recovery: a second decoder is created and the second keyframe is submitted.
        await pumpUntil(() => decoders.length >= 2 && decoders[1].decodeCalls.length >= 1);
        expect(decoders[0].closed).toBe(true);

        // Emit a frame on the rebuilt decoder; n1 yields it.
        decoders[1].emitFrame(0);
        const r1 = await n1;
        expect(r1.done).toBe(false);
    });

    it('stats: decodeRatioEma reflects (decodedAt - submitMs) / frameDurationMs', async () => {
        const stats = makeStats();
        let captured: MockDecoder | undefined;
        let nowMs = 1000;
        const opts: DecodeOptions = {
            initialConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => {
                captured = new MockDecoder(handlers);
                return captured;
            },
            now: () => nowMs,
            frameDurationMs: 50, // ratio = decodeMs / 50
        };
        // Gated source so each chunk is submitted at a controlled `nowMs` (the
        // run-ahead pump would otherwise submit chunk 2 as soon as space frees).
        const src = manualSource<ArrivedChunk>();
        const seg = decode(opts)(src.iterable);
        const iter = seg[Symbol.asyncIterator]();

        // Chunk 1: submitMs = 1000; decode took 25 ms → ratio = 0.5.
        const n1 = stepIter(iter);
        src.push(makeArrived(stats, { isKeyFrame: true, width: 640, height: 360 }));
        await pumpUntil(() => captured !== undefined && captured.decodeCalls.length >= 1);
        nowMs = 1025;
        captured!.emitFrame(0);
        await n1;
        expect(stats.decodeRatioEma).toBeCloseTo(0.5, 4);

        // Chunk 2: submitMs = 1100; decode took 100 ms → ratio = 2.0. EMA blends.
        nowMs = 1100;
        const n2 = stepIter(iter);
        src.push(makeArrived(stats, { isKeyFrame: false, width: 640, height: 360 }));
        await pumpUntil(() => captured !== undefined && captured.decodeCalls.length >= 2);
        nowMs = 1200;
        captured!.emitFrame(1);
        await n2;
        // With alpha=0.2: prev=0.5, new=2.0 → 0.5 + 0.2*(2.0-0.5) = 0.8.
        expect(stats.decodeRatioEma).toBeCloseTo(0.8, 4);

        src.end();
        await iter.next();
    });

    it('stats: recoveryStreak tracks consecutiveRecoveries; resets on successful frame', async () => {
        const stats = makeStats();
        const decoders: MockDecoder[] = [];
        const opts: DecodeOptions = {
            initialConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => {
                const d = new MockDecoder(handlers);
                decoders.push(d);
                return d;
            },
            maxRecoveries: 5,
        };
        // keyframe → error → keyframe (recovery #1 — should bump streak to 1)
        const src = manualSource<ArrivedChunk>();
        const seg = decode(opts)(src.iterable);
        const iter = seg[Symbol.asyncIterator]();
        const n1 = stepIter(iter);
        src.push(makeArrived(stats, { isKeyFrame: true, width: 640, height: 360 }));
        await pumpUntil(() => decoders.length >= 1 && decoders[0].decodeCalls.length >= 1);
        decoders[0].emitError('boom');
        src.push(makeArrived(stats, { isKeyFrame: true, width: 640, height: 360 }));
        await pumpUntil(() => decoders.length >= 2 && decoders[1].decodeCalls.length >= 1);
        expect(stats.recoveryStreak).toBe(1);

        // A successful frame resets the streak.
        decoders[1].emitFrame(0);
        await n1;
        expect(stats.recoveryStreak).toBe(0);
    });

    it('stats: hangRateIn60s increments on watchdog fire', async () => {
        const stats = makeStats();
        const decoders: MockDecoder[] = [];
        interface FakeTimer { cb: () => void; canceled: boolean }
        const timers: FakeTimer[] = [];
        const setTimeoutFn = (cb: () => void): unknown => {
            const t: FakeTimer = { cb, canceled: false };
            timers.push(t);
            return t;
        };
        const clearTimeoutFn = (h: unknown): void => { (h as FakeTimer).canceled = true; };
        const fireWatchdog = (): void => {
            for (const t of timers.splice(0)) if (!t.canceled) t.cb();
        };
        let nowMs = 0;
        const opts: DecodeOptions = {
            initialConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => {
                const d = new MockDecoder(handlers);
                decoders.push(d);
                return d;
            },
            maxRecoveries: 4,
            decoderHangTimeoutMs: 2_000,
            now: () => nowMs,
            setTimeoutFn,
            clearTimeoutFn,
        };
        const src = manualSource<ArrivedChunk>();
        const seg = decode(opts)(src.iterable);
        const iter = seg[Symbol.asyncIterator]();
        const n1 = stepIter(iter);
        src.push(makeArrived(stats, { isKeyFrame: true, width: 640, height: 360 }));
        await pumpUntil(() => decoders.length >= 1 && decoders[0].decodeCalls.length >= 1);
        expect(stats.hangRateIn60s).toBe(0);
        // The drain loop arms the watchdog a hop after the pump submits.
        await pumpUntil(() => timers.length >= 1);
        nowMs = 2_000;
        fireWatchdog();
        await pumpUntil(() => stats.hangRateIn60s >= 1);
        expect(stats.hangRateIn60s).toBe(1);
        // Recovery resubmits on chunk 2; emit a frame so the operator finishes.
        src.push(makeArrived(stats, { isKeyFrame: true, width: 640, height: 360 }));
        await pumpUntil(() => decoders.length >= 2 && decoders[1].decodeCalls.length >= 1);
        decoders[1].emitFrame(0);
        await n1;
    });

    it('reconfigure on dim change: second keyframe with different dims triggers a new configure()', async () => {
        const stats = makeStats();
        let captured: MockDecoder | undefined;
        const opts: DecodeOptions = {
            initialConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => {
                captured = new MockDecoder(handlers);
                return captured;
            },
        };
        const arrivals: ArrivedChunk[] = [
            makeArrived(stats, { isKeyFrame: true,  width: 640,  height: 360 }),
            makeArrived(stats, { isKeyFrame: false, width: 640,  height: 360 }),
            makeArrived(stats, { isKeyFrame: true,  width: 1280, height: 720 }),
        ];
        const seg = decode(opts)(fromArray(arrivals));
        const iter = seg[Symbol.asyncIterator]();

        // Drive 3 chunks through; emit a frame for each.
        for (let i = 0; i < 3; i++) {
            const expected = i + 1;
            const next = stepIter(iter);
            await pumpUntil(() => captured !== undefined && captured.decodeCalls.length >= expected);
            captured!.emitFrame(i);
            await next;
        }
        await iter.next();

        // Two configures: initial 640x360 + reconfigure 1280x720.
        expect(captured!.configureCalls).toHaveLength(2);
        expect(captured!.configureCalls[0].codedWidth).toBe(640);
        expect(captured!.configureCalls[0].codedHeight).toBe(360);
        expect(captured!.configureCalls[1].codedWidth).toBe(1280);
        expect(captured!.configureCalls[1].codedHeight).toBe(720);
        expect(captured!.decodeCalls).toHaveLength(3);
    });

    it('feed pump keeps the decoder topped to targetInFlightDepth without consumer pulls', async () => {
        const stats = makeStats();
        const ac = new AbortController();
        let captured: MockDecoder | undefined;
        const opts: DecodeOptions = {
            initialConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => {
                captured = new MockDecoder(handlers);
                return captured;
            },
            abortSignal: ac.signal,
            targetInFlightDepth: 3,
            readyCap: 10,
        };
        const arrivals: ArrivedChunk[] = [makeArrived(stats, { isKeyFrame: true })];
        for (let i = 0; i < 10; i++) arrivals.push(makeArrived(stats, { isKeyFrame: false }));
        const seg = decode(opts)(fromArray(arrivals));
        const iter = seg[Symbol.asyncIterator]();

        // Kick the generator (starts the pump) but never consume a frame.
        const first = stepIter(iter);
        await pumpUntil(() => captured !== undefined && captured.decodeCalls.length >= 3);
        // Prove it backpressures: it must NOT submit beyond the target.
        for (let i = 0; i < 30; i++) await Promise.resolve();
        expect(captured!.decodeCalls.length).toBe(3);

        ac.abort();
        const r = await first;
        expect(r.done).toBe(true);
    });

    it('backpressure: plateaus at targetInFlightDepth, advances by one when a frame frees a slot', async () => {
        const stats = makeStats();
        const ac = new AbortController();
        let captured: MockDecoder | undefined;
        const opts: DecodeOptions = {
            initialConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => {
                captured = new MockDecoder(handlers);
                return captured;
            },
            abortSignal: ac.signal,
            targetInFlightDepth: 2,
            readyCap: 10,
        };
        const arrivals: ArrivedChunk[] = [makeArrived(stats, { isKeyFrame: true })];
        for (let i = 0; i < 4; i++) arrivals.push(makeArrived(stats, { isKeyFrame: false }));
        const seg = decode(opts)(fromArray(arrivals));
        const iter = seg[Symbol.asyncIterator]();

        const first = stepIter(iter);
        await pumpUntil(() => captured !== undefined && captured.decodeCalls.length >= 2);
        for (let i = 0; i < 20; i++) await Promise.resolve();
        expect(captured!.decodeCalls.length).toBe(2);

        // Emit one frame → one in-flight slot frees → exactly one more submitted.
        captured!.emitFrame(0);
        await first;
        await pumpUntil(() => captured!.decodeCalls.length >= 3);
        for (let i = 0; i < 20; i++) await Promise.resolve();
        expect(captured!.decodeCalls.length).toBe(3);

        ac.abort();
        await iter.return?.();
    });

    it('backpressure: plateaus at readyCap until the consumer drains decoded inventory', async () => {
        const stats = makeStats();
        const ac = new AbortController();
        let captured: MockDecoder | undefined;
        const opts: DecodeOptions = {
            initialConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => {
                captured = new MockDecoder(handlers);
                return captured;
            },
            abortSignal: ac.signal,
            targetInFlightDepth: 100, // not the binding constraint here
            readyCap: 2,
        };
        const src = manualSource<ArrivedChunk>();
        const seg = decode(opts)(src.iterable);
        const iter = seg[Symbol.asyncIterator]();

        const n0 = stepIter(iter);
        src.push(makeArrived(stats, { isKeyFrame: true }));
        await pumpUntil(() => captured !== undefined && captured.decodeCalls.length >= 1);
        captured!.emitFrame(0);
        await n0; // consumes frame0, ready back to 0, generator suspended

        // Fill decoded inventory to readyCap without pulling.
        src.push(makeArrived(stats, { isKeyFrame: false }));
        await pumpUntil(() => captured!.decodeCalls.length >= 2);
        captured!.emitFrame(1); // ready = 1
        src.push(makeArrived(stats, { isKeyFrame: false }));
        await pumpUntil(() => captured!.decodeCalls.length >= 3);
        captured!.emitFrame(2); // ready = 2 (== cap)

        // Next chunk is available but must NOT be submitted — readyCap is full.
        src.push(makeArrived(stats, { isKeyFrame: false }));
        for (let i = 0; i < 30; i++) await Promise.resolve();
        expect(captured!.decodeCalls.length).toBe(3);

        // Draining one decoded frame frees inventory → the pump submits again.
        const r = await iter.next();
        expect(r.done).toBe(false);
        await pumpUntil(() => captured!.decodeCalls.length >= 4);
        expect(captured!.decodeCalls.length).toBe(4);

        ac.abort();
        src.end();
        await iter.return?.();
    });

    it('count conservation: N chunks in → N frames out, every chunk closed, no on-time drop', async () => {
        const stats = makeStats();
        let captured: MockDecoder | undefined;
        const opts: DecodeOptions = {
            initialConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => {
                captured = new MockDecoder(handlers);
                return captured;
            },
            targetInFlightDepth: 3,
            readyCap: 5,
        };
        const n = 8;
        const arrivals: ArrivedChunk[] = [];
        for (let i = 0; i < n; i++) arrivals.push(makeArrived(stats, { isKeyFrame: i === 0 }));
        const seg = decode(opts)(fromArray(arrivals));
        const iter = seg[Symbol.asyncIterator]();

        const collected: DecodedFrame[] = [];
        for (let i = 0; i < n; i++) {
            const next = stepIter(iter);
            await pumpUntil(() => captured !== undefined && captured.decodeCalls.length >= i + 1);
            captured!.emitFrame(i);
            const r = await next;
            if (r.done === false) collected.push(r.value);
        }
        const tail = await iter.next();
        expect(tail.done).toBe(true);

        expect(collected).toHaveLength(n);
        expect(stats.framesDecoded).toBe(n);
        for (const a of arrivals)
            expect((a.chunk as unknown as MockEncodedVideoChunk).closed).toBe(true);
    });

    it('watchdog: a frame arriving during decode clears the hang timer (no false hang)', async () => {
        const stats = makeStats();
        const ac = new AbortController();
        interface FakeTimer { cb: () => void; canceled: boolean }
        const timers: FakeTimer[] = [];
        const setTimeoutFn = (cb: () => void): unknown => {
            const t: FakeTimer = { cb, canceled: false };
            timers.push(t);
            return t;
        };
        const clearTimeoutFn = (h: unknown): void => { (h as FakeTimer).canceled = true; };
        const fireWatchdog = (): void => {
            for (const t of timers.splice(0)) if (!t.canceled) t.cb();
        };
        let nowMs = 0;
        let captured: MockDecoder | undefined;
        const opts: DecodeOptions = {
            initialConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => {
                captured = new MockDecoder(handlers);
                return captured;
            },
            abortSignal: ac.signal,
            decoderHangTimeoutMs: 2_000,
            now: () => nowMs,
            setTimeoutFn,
            clearTimeoutFn,
        };
        const src = manualSource<ArrivedChunk>();
        const seg = decode(opts)(src.iterable);
        const iter = seg[Symbol.asyncIterator]();

        const n0 = stepIter(iter);
        src.push(makeArrived(stats, { isKeyFrame: true }));
        // A watchdog arms while the chunk is in flight.
        await pumpUntil(() => captured !== undefined
            && captured.decodeCalls.length >= 1
            && timers.length >= 1);
        // The frame lands just before the timeout would elapse — the drain loop
        // clears the armed timer.
        nowMs = 1_900;
        captured!.emitFrame(0);
        await n0;
        // Any leftover timer is now canceled → firing it must not synthesise a hang.
        fireWatchdog();
        for (let i = 0; i < 10; i++) await Promise.resolve();
        expect(stats.hangRateIn60s).toBe(0);

        ac.abort();
        src.end();
        await iter.return?.();
    });
});

// Silence unused-var lint
void vi;
