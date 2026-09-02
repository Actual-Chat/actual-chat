import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
    encode,
    isEncoderInitFailedError,
    parseEncoderInitFailedCodec,
    type EncodeOptions,
    type EncodeInput,
    type EncoderConfigPerLayer,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/encode';
import { LayerLadderController } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/sender/layer-ladder-controller';
import {
    type CapturedBundle,
    type CapturedFrame,
    type EncodedBundle,
    type EncodedFrame,
    type RecorderStats,
    createEmptyRecorderStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
import { AsyncVideoEncoder, AsyncVideoEncoderResetError } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/adapters';

// ---- Mock WebCodecs surface ----------------------------------------------
//
// AsyncVideoEncoder constructs `new VideoEncoder({ output, error })`. Tests
// install a MockVideoEncoder as `globalThis.VideoEncoder` for the duration
// of each test (same pattern as async-video-encoder.test.ts), and inject
// the real `AsyncVideoEncoder` via the operator's `createEncoder` option.

interface MockVideoEncoderInit {
    output: (chunk: EncodedVideoChunk, metadata: EncodedVideoChunkMetadata) => void;
    error: (e: unknown) => void;
}

class MockVideoEncoder {
    static instances: MockVideoEncoder[] = [];
    static configureFailureForCodec: string | null = null;
    /** Settle each submission on a microtask, i.e. an encoder that keeps pace.
     *  The operator drops once MAX_PIPELINE submissions are outstanding. */
    static autoEmit = false;
    state: 'unconfigured' | 'configured' | 'closed' = 'configured';
    encodeCalls: { frame: MockVideoFrame; opts: { keyFrame: boolean } }[] = [];
    configureCalls: VideoEncoderConfig[] = [];
    resetCalls = 0;
    output: MockVideoEncoderInit['output'];
    error: MockVideoEncoderInit['error'];

    get encodeQueueSize(): number {
        return this.encodeCalls.length;
    }

    constructor(init: MockVideoEncoderInit) {
        this.output = init.output;
        this.error = init.error;
        MockVideoEncoder.instances.push(this);
    }

    encode(frame: MockVideoFrame, opts: { keyFrame: boolean }): void {
        this.encodeCalls.push({ frame, opts });
        if (MockVideoEncoder.autoEmit)
            queueMicrotask(() => { if (this.encodeCalls.length > 0) this.emitNext(50); });
    }

    close(): void {
        this.state = 'closed';
    }

    configure(config: VideoEncoderConfig): void {
        this.configureCalls.push(config);
        if (MockVideoEncoder.configureFailureForCodec === config.codec)
            throw new Error(`configure rejected ${config.codec}`);
        this.state = 'configured';
    }

    reset(): void {
        this.resetCalls++;
        this.encodeCalls.length = 0;
        this.state = 'configured';
    }

    /** Test helper: complete the next pending submission FIFO with a fake
     *  encoded chunk. `byteLength` and `type` propagate; everything else
     *  is empty. */
    emitNext(byteLength = 100, type: 'key' | 'delta' = 'delta'): void {
        const call = this.encodeCalls.shift();
        if (!call) throw new Error('emitNext: no pending encode');
        const finalType: 'key' | 'delta' = call.opts.keyFrame ? 'key' : type;
        const chunk: { type: string; timestamp: number; byteLength: number; closed: boolean; close: () => void } = {
            type: finalType,
            timestamp: 0,
            byteLength,
            closed: false,
            close(): void { chunk.closed = true; },
        };
        this.output(chunk as unknown as EncodedVideoChunk, {});
    }
}

class MockVideoFrame {
    closed = false;
    constructor(
        public id: number,
        public codedWidth: number,
        public codedHeight: number,
    ) {}
    close(): void { this.closed = true; }
}

interface GlobalWithVideoEncoder {
    VideoEncoder?: typeof MockVideoEncoder;
}

beforeEach(() => {
    MockVideoEncoder.instances = [];
    MockVideoEncoder.configureFailureForCodec = null;
    MockVideoEncoder.autoEmit = false;
    (globalThis as unknown as GlobalWithVideoEncoder).VideoEncoder = MockVideoEncoder;
});

afterEach(() => {
    delete (globalThis as unknown as GlobalWithVideoEncoder).VideoEncoder;
    vi.useRealTimers();
});

// ---- Helpers --------------------------------------------------------------

function makeStats(): RecorderStats {
    return createEmptyRecorderStats();
}

function mkFrame(id: number, w: number, h: number): VideoFrame {
    return new MockVideoFrame(id, w, h) as unknown as VideoFrame;
}

function makeCaptured(
    index: number,
    stats: RecorderStats,
    width: number,
    height: number,
    forceKeyframe = false,
): CapturedFrame {
    return {
        frame: mkFrame(index, width, height),
        capturedAt: { timeMs: 1_700_000_000_000 + index, epoch: 0 },
        durationUs: 1_000_000 / 30,
        index,
        dropTrace: [],
        sourceWidth: 1920,
        sourceHeight: 1080,
        forceKeyframe,
        rotation: 0,
        stats,
    };
}

function makeBundle(
    index: number,
    stats: RecorderStats,
    layers: { width: number; height: number }[],
    forceKeyframe = false,
    ladderVersion = 0,
): CapturedBundle {
    if (layers.length === 0) throw new Error('makeBundle: at least one layer');
    // Bottom-first: layers[0] = base layer, layers[last] = top layer.
    const captured: CapturedFrame[] = layers.map(l =>
        makeCaptured(index, stats, l.width, l.height, forceKeyframe));
    return { layers: captured, ladderVersion, index, dropTrace: [], rotation: 0, stats };
}

function fromArray<T>(items: T[]): AsyncIterable<T> {
    return fromArrayAsync(items);
}
async function* fromArrayAsync<T>(items: readonly T[]): AsyncIterable<T> {
    await Promise.resolve();
    for (const item of items) yield item;
}

const cfg = (width: number, height: number): EncoderConfigPerLayer => ({
    width,
    height,
    bitrate: 1_000_000,
    framerate: 30,
    codec: 'avc1.640028',
});

/** `createEncoder` factory that returns a real `AsyncVideoEncoder`.
 *  Requires `globalThis.VideoEncoder = MockVideoEncoder` to be set. */
function makeFactory(opts: { onResetRequested?: (reason: string) => void; timeoutMs?: number } = {}) {
    return (config: EncoderConfigPerLayer, layerId: number): AsyncVideoEncoder<EncodeInput, EncodedFrame> => {
        const enc = new AsyncVideoEncoder<EncodeInput, EncodedFrame>(
            (input, chunk, metadata) => ({
                chunk,
                metadata,
                capturedAt: input.capturedAt,
                durationUs: 1_000_000 / 30,
                index: input.index,
                dropTrace: [],
                layerId: layerId,
                sourceWidth: 0,
                sourceHeight: 0,
                encodedWidth: config.width,
                encodedHeight: config.height,
                rotation: 0,
                stats: undefined as unknown as RecorderStats,
            }),
            () => { /* swallow encoder error */ },
            {
                // Matches production recorder-worker-host.ts so the
                // pipelined encode operator (MAX_PIPELINE=5) doesn't
                // overrun the adapter queue.
                maxInflight: 5,
                timeoutMs: opts.timeoutMs ?? 100,
                firstTimeoutMs: opts.timeoutMs ?? 100,
                onResetRequested: opts.onResetRequested,
            },
        );
        try {
            enc.configure({
                codec: config.codec,
                width: config.width,
                height: config.height,
                bitrate: config.bitrate,
                framerate: config.framerate,
                latencyMode: 'realtime',
            });
        } catch (e) {
            enc.dispose();
            throw e;
        }
        return enc;
    };
}

async function drain<T>(seg: AsyncIterable<T>): Promise<T[]> {
    const out: T[] = [];
    for await (const item of seg) out.push(item);
    return out;
}

/**
 * Yield microtasks until the encoder at `instanceIdx` exists and has at
 * least `expectedCalls` pending encode submissions. Bounded so a wedged
 * test fails fast.
 */
async function waitForCalls(instanceIdx: number, expectedCalls: number): Promise<void> {
    for (let i = 0; i < 100; i++) {
        if (MockVideoEncoder.instances.length > instanceIdx
            && MockVideoEncoder.instances[instanceIdx].encodeCalls.length >= expectedCalls)
            return;
        await Promise.resolve();
    }
    throw new Error(`waitForCalls: instance ${instanceIdx} did not reach ${expectedCalls} calls`);
}

async function waitForInstances(count: number): Promise<void> {
    for (let i = 0; i < 100; i++) {
        if (MockVideoEncoder.instances.length >= count) {
            // Also wait until each has a pending call.
            let allReady = true;
            for (let j = 0; j < count; j++) {
                if (MockVideoEncoder.instances[j].encodeCalls.length === 0) {
                    allReady = false; break;
                }
            }
            if (allReady) return;
        }
        await Promise.resolve();
    }
    throw new Error(`waitForInstances: never reached ${count} ready instances`);
}

// ---- Tests ----------------------------------------------------------------

describe('encode operator', () => {
    it('single layer: 5 bundles → 5 EncodedFrames out, in order', async () => {
        MockVideoEncoder.autoEmit = true;
        const stats = makeStats();
        const opts: EncodeOptions = {
            controller: new LayerLadderController([cfg(640, 360)]),
            createEncoder: makeFactory(),
        };

        const bundles: CapturedBundle[] = [];
        for (let i = 1; i <= 5; i++)
            bundles.push(makeBundle(i, stats, [{ width: 640, height: 360 }]));

        const seg = encode(opts)(fromArray(bundles));
        const iter: AsyncIterator<EncodedBundle> = seg[Symbol.asyncIterator]();

        const results: EncodedFrame[] = [];
        for (const _b of bundles) {
            const r = await iter.next();
            expect(r.done).toBe(false);
            if (r.done === false) results.push(...r.value.layers);
        }
        const final = await iter.next();
        expect(final.done).toBe(true);

        expect(results).toHaveLength(5);
        expect(results.map(r => r.index)).toEqual([1, 2, 3, 4, 5]);
        for (const result of results) {
            expect(result.layerId).toBe(0);
            expect(result.encodedWidth).toBe(640);
            expect(result.encodedHeight).toBe(360);
            expect(result.sourceWidth).toBe(1920);
            expect(result.sourceHeight).toBe(1080);
            expect(result.stats).toBe(stats);
        }
    });

    it('forceKeyframe → keyFrame flag (verified by inspecting encoder calls)', async () => {
        const stats = makeStats();
        const seg = encode({
            controller: new LayerLadderController([cfg(640, 360)]),
            createEncoder: makeFactory(),
        })(fromArray([
            makeBundle(1, stats, [{ width: 640, height: 360 }], false),
            makeBundle(2, stats, [{ width: 640, height: 360 }], false),
            makeBundle(3, stats, [{ width: 640, height: 360 }], true),
        ]));
        const iter: AsyncIterator<EncodedBundle> = seg[Symbol.asyncIterator]();

        // First bundle: keyFrame=true regardless of policy — pool-reused
        // encoders may not emit a natural keyframe as their first chunk.
        const next1 = iter.next();
        await waitForCalls(0, 1);
        const mock = MockVideoEncoder.instances[0];
        expect(mock.encodeCalls).toHaveLength(1);
        expect(mock.encodeCalls[0].opts.keyFrame).toBe(true);
        mock.emitNext();
        await next1;

        // Bundles 2 and 3 pipeline together now that a routine keyframe no longer
        // drains; the per-bundle flags are what matters here.
        const next2 = iter.next();
        await waitForCalls(0, 2);
        expect(mock.encodeCalls[0].opts.keyFrame).toBe(false);
        expect(mock.encodeCalls[1].opts.keyFrame).toBe(true);
        mock.emitNext();
        await next2;

        const next3 = iter.next();
        mock.emitNext();
        await next3;
        await iter.next();
    });

    // Delay is bounded, not throughput: a stalled encoder must never bank more
    // than MAX_PIPELINE frames. The excess is dropped, and the resulting index gap
    // is what surfaces downstream as senderDropRatioEma.
    it('drops rather than queues once the encoder is MAX_PIPELINE deep', async () => {
        const stats = makeStats();
        const bundles: CapturedBundle[] = [];
        for (let i = 1; i <= 12; i++)
            bundles.push(makeBundle(i, stats, [{ width: 640, height: 360 }]));

        const seg = encode({
            controller: new LayerLadderController([cfg(640, 360)]),
            createEncoder: makeFactory(),
        })(fromArray(bundles));
        const iter: AsyncIterator<EncodedBundle> = seg[Symbol.asyncIterator]();

        // The encoder never settles, so nothing is released and the source is
        // consumed to exhaustion — submissions must still stop at the cap.
        const next = iter.next();
        await waitForCalls(0, 1);
        const mock = MockVideoEncoder.instances[0];
        for (let i = 0; i < 50; i++) await Promise.resolve();
        expect(mock.encodeCalls.length).toBeLessThanOrEqual(3);

        // The dropped bundles' frames are released rather than leaked.
        const dropped = bundles.slice(mock.encodeCalls.length);
        expect(dropped.every(b => b.layers.every(l =>
            (l.frame as unknown as MockVideoFrame).closed))).toBe(true);

        mock.emitNext(50);
        await next;
    });

    it('multi-layer (3 tiers): 5 source bundles → 5 EncodedBundles (1 per layer inside each)', async () => {
        MockVideoEncoder.autoEmit = true;
        const stats = makeStats();
        const layers = [
            { width: 320, height: 180 },
            { width: 640, height: 360 },
            { width: 1280, height: 720 },
        ];
        const opts: EncodeOptions = {
            controller: new LayerLadderController(layers.map(l => cfg(l.width, l.height))),
            createEncoder: makeFactory(),
        };
        const bundles: CapturedBundle[] = [];
        for (let i = 1; i <= 5; i++) bundles.push(makeBundle(i, stats, layers));

        const seg = encode(opts)(fromArray(bundles));
        const iter: AsyncIterator<EncodedBundle> = seg[Symbol.asyncIterator]();

        const collectedBundles: EncodedBundle[] = [];
        for (const _b of bundles) {
            const r = await iter.next();
            if (r.done === false) collectedBundles.push(r.value);
        }
        const tail = await iter.next();
        expect(tail.done).toBe(true);

        expect(collectedBundles).toHaveLength(5);

        for (const eb of collectedBundles) {
            expect(eb.layers).toHaveLength(3);
            expect(eb.layers.map(g => g.layerId)).toEqual([0, 1, 2]);
            expect(eb.layers[0].encodedWidth).toBe(320);
            expect(eb.layers[1].encodedWidth).toBe(640);
            expect(eb.layers[2].encodedWidth).toBe(1280);
        }
    });

    it('updates stats counters: chunksEncoded, bytesEncoded, keyframesEncoded', async () => {
        const stats = makeStats();
        const layers = [
            { width: 320, height: 180 },
            { width: 1280, height: 720 },
        ];
        const opts: EncodeOptions = {
            controller: new LayerLadderController(layers.map(l => cfg(l.width, l.height))),
            createEncoder: makeFactory(),
        };
        const bundles: CapturedBundle[] = [
            makeBundle(1, stats, layers, true),    // keyframe
            makeBundle(2, stats, layers, false),
            makeBundle(3, stats, layers, false),
        ];
        const seg = encode(opts)(fromArray(bundles));
        const iter: AsyncIterator<EncodedBundle> = seg[Symbol.asyncIterator]();

        let totalBytes = 0;
        let bundleIdx = 0;
        for (const _b of bundles) {
            const next = iter.next();
            await waitForInstances(layers.length);
            const byteSizes = [70 + bundleIdx * 10, 130 + bundleIdx * 10];
            const instances: MockVideoEncoder[] = MockVideoEncoder.instances.slice(0, layers.length);
            instances.forEach((inst, layerIdx) => {
                inst.emitNext(byteSizes[layerIdx]);
                totalBytes += byteSizes[layerIdx];
            });
            await next;
            bundleIdx++;
        }
        await iter.next();

        expect(stats.bytesEncoded).toBe(totalBytes);
    });

    it('encoder timeout drops the bundle and forces the next bundle to keyframe', async () => {
        const stats = makeStats();
        const first = makeBundle(1, stats, [{ width: 640, height: 360 }]);
        const second = makeBundle(2, stats, [{ width: 640, height: 360 }]);
        const encodeCalls: { frame: VideoFrame; opts: { keyFrame: boolean } }[] = [];
        let encodeCount = 0;
        const fakeEncoder = {
            encode(input: EncodeInput, opts: { keyFrame: boolean }): Promise<EncodedFrame> {
                encodeCalls.push({ frame: input.frame, opts });
                encodeCount++;
                if (encodeCount === 1) {
                    try { input.frame.close(); } catch { /* ignore */ }
                    return Promise.reject(new AsyncVideoEncoderResetError('timeout'));
                }
                return Promise.resolve({
                    chunk: {
                        type: opts.keyFrame ? 'key' : 'delta',
                        timestamp: 0,
                        byteLength: 123,
                        close(): void { /* ignore */ },
                    } as unknown as EncodedVideoChunk,
                    metadata: {},
                    dropTrace: [],
                    capturedAt: input.capturedAt,
                    durationUs: 1_000_000 / 30,
                    index: input.index,
                    layerId: 0,
                    sourceWidth: 0,
                    sourceHeight: 0,
                    encodedWidth: 640,
                    encodedHeight: 360,
                    rotation: 0,
                    stats: undefined as unknown as RecorderStats,
                });
            },
            dispose(): void { /* ignore */ },
            flush: (): Promise<void> => Promise.resolve(),
            // The encode operator samples encoder.encodeQueueSize per bundle
            // (queue-depth EMA). Provide a static stub so the sampling path
            // doesn't fault on this fake.
            encoder: { encodeQueueSize: 0 },
        } as unknown as AsyncVideoEncoder<EncodeInput, EncodedFrame>;
        const seg = encode({
            controller: new LayerLadderController([cfg(640, 360)]),
            createEncoder: () => fakeEncoder,
        })(fromArray([first, second]));

        const iter: AsyncIterator<EncodedBundle> = seg[Symbol.asyncIterator]();
        const result = await iter.next();
        // B1 drops with reset; B2 is the first bundle yielded.
        expect(result.done).toBe(false);
        expect(result.done === false ? result.value.layers[0].index : 0).toBe(2);
        expect(encodeCalls).toHaveLength(2);
        // First encode forced to keyFrame=true (per-encoder first-call guard).
        expect(encodeCalls[0].opts.keyFrame).toBe(true);
        // B1's frame was closed by the fake encoder before rejecting.
        expect((first.layers[0].frame as unknown as MockVideoFrame).closed).toBe(true);
        // Pipelined operator: B2 was submitted BEFORE B1's reset was observed,
        // so B2 is a delta. forceKeyframeNext is set after B1's reset and
        // applies only to bundles pulled from source AFTER the drain — none
        // here because source only had 2 bundles.
        expect(encodeCalls[1].opts.keyFrame).toBe(false);
        await iter.next();
    });

    it('onResetRequested fires when the wrapper degrades (timeout case)', async () => {
        vi.useFakeTimers();
        const stats = makeStats();
        const reasons: string[] = [];
        const seg = encode({
            controller: new LayerLadderController([cfg(640, 360)]),
            createEncoder: makeFactory({
                timeoutMs: 50,
                onResetRequested: r => reasons.push(r),
            }),
        })(fromArray([makeBundle(1, stats, [{ width: 640, height: 360 }])]));

        const iter: AsyncIterator<EncodedBundle> = seg[Symbol.asyncIterator]();
        const promise = iter.next();
        await vi.advanceTimersByTimeAsync(60);
        const result = await promise;

        expect(reasons.length).toBe(1);
        expect(reasons[0]).toMatch(/timeout/);
        expect(result.done).toBe(true);
    });

    it('disposes per-layer encoders on completion (closes underlying VideoEncoder)', async () => {
        const stats = makeStats();
        const layers = [
            { width: 320, height: 180 },
            { width: 640, height: 360 },
        ];
        const seg = encode({
            controller: new LayerLadderController(layers.map(l => cfg(l.width, l.height))),
            createEncoder: makeFactory(),
        })(fromArray([makeBundle(1, stats, layers)]));

        const iter: AsyncIterator<EncodedBundle> = seg[Symbol.asyncIterator]();
        const firstNext = iter.next();
        await waitForInstances(layers.length);
        for (const m of MockVideoEncoder.instances) m.emitNext(64);
        await firstNext;
        await iter.next();

        // After source completes & finally runs, every underlying mock encoder
        // should be in 'closed' state.
        expect(MockVideoEncoder.instances).toHaveLength(2);
        for (const m of MockVideoEncoder.instances) expect(m.state).toBe('closed');
    });

    it('wraps synchronous encoder configure failure as init failure and cleans up', async () => {
        const stats = makeStats();
        const configs: EncoderConfigPerLayer[] = [
            { ...cfg(320, 180), codec: 'avc1.640028' },
            { ...cfg(640, 360), codec: 'hev1.1.6.L93.B0' },
        ];
        const bundle = makeBundle(1, stats, [
            { width: 320, height: 180 },
            { width: 640, height: 360 },
        ]);
        MockVideoEncoder.configureFailureForCodec = 'hev1.1.6.L93.B0';

        const seg = encode({
            controller: new LayerLadderController(configs),
            createEncoder: makeFactory(),
        })(fromArray([bundle]));

        let thrown: unknown;
        try {
            await drain(seg);
        } catch (e) {
            thrown = e;
        }

        expect(thrown).toBeInstanceOf(Error);
        expect(isEncoderInitFailedError(thrown)).toBe(true);
        expect(parseEncoderInitFailedCodec(thrown)).toBe('hev1.1.6.L93.B0');
        expect((thrown as Error).message).toContain('configure rejected hev1.1.6.L93.B0');
        expect((bundle.layers[0].frame as unknown as MockVideoFrame).closed).toBe(true);
        expect((bundle.layers[1].frame as unknown as MockVideoFrame).closed).toBe(true);
        expect(MockVideoEncoder.instances).toHaveLength(2);
        expect(MockVideoEncoder.instances[0].state).toBe('closed');
        expect(MockVideoEncoder.instances[1].state).toBe('closed');
    });

    it('stats: encodeQueueDepthEma samples encoder.encodeQueueSize per bundle', async () => {
        const stats = makeStats();
        const seg = encode({
            controller: new LayerLadderController([cfg(640, 360)]),
            createEncoder: makeFactory(),
        })(fromArray([
            makeBundle(1, stats, [{ width: 640, height: 360 }]),
            makeBundle(2, stats, [{ width: 640, height: 360 }]),
        ]));
        const iter: AsyncIterator<EncodedBundle> = seg[Symbol.asyncIterator]();

        // Bundle 1: encoder has 1 inflight encode at sample time → ratio = 1.
        const next1 = iter.next();
        await waitForCalls(0, 1);
        MockVideoEncoder.instances[0].emitNext();
        await next1;
        // EMA seed-on-first: equal to the first sample (1).
        expect(stats.encodeQueueDepthEma).toBe(1);

        // Bundle 2: same shape, samples 1 again. EMA holds at 1.
        const next2 = iter.next();
        await waitForCalls(0, 1);
        MockVideoEncoder.instances[0].emitNext();
        await next2;
        expect(stats.encodeQueueDepthEma).toBeCloseTo(1, 6);

        await iter.next();
    });

    it('controller grow: appended layer\'s first chunk is keyframe', async () => {
        const stats = makeStats();
        const controller = new LayerLadderController([cfg(640, 360)]);
        const seg = encode({
            controller,
            createEncoder: makeFactory(),
        })(fromArray([
            makeBundle(1, stats, [{ width: 640, height: 360 }]),
            makeBundle(2, stats, [{ width: 640, height: 360 }, { width: 1280, height: 720 }], false, 1),
            makeBundle(3, stats, [{ width: 640, height: 360 }, { width: 1280, height: 720 }], false, 1),
        ]));
        const iter: AsyncIterator<EncodedBundle> = seg[Symbol.asyncIterator]();

        // Bundle 1: single layer, KF on first encode (warm-up).
        const next1 = iter.next();
        await waitForCalls(0, 1);
        expect(MockVideoEncoder.instances[0].encodeCalls[0].opts.keyFrame).toBe(true);
        MockVideoEncoder.instances[0].emitNext(80, 'key');
        const r1 = await next1;
        if (!r1.done) expect(r1.value.layers).toHaveLength(1);

        // Reconfigure before bundle 2 lands.
        controller.setConfigs([cfg(640, 360), cfg(1280, 720)]);

        // Bundle 2: grow triggers forceKeyframeNext=true → both encoders see keyFrame.
        const next2 = iter.next();
        await waitForInstances(2);
        expect(MockVideoEncoder.instances[0].encodeCalls[0].opts.keyFrame).toBe(true);
        expect(MockVideoEncoder.instances[1].encodeCalls[0].opts.keyFrame).toBe(true);
        MockVideoEncoder.instances[0].emitNext(80, 'key');
        MockVideoEncoder.instances[1].emitNext(160, 'key');
        const r2 = await next2;
        expect(r2.done).toBe(false);
        if (!r2.done) {
            expect(r2.value.layers).toHaveLength(2);
            expect(r2.value.layers[1].chunk.type).toBe('key');
        }

        // Bundle 3: normal delta on both.
        const next3 = iter.next();
        await waitForCalls(0, 1);
        await waitForCalls(1, 1);
        MockVideoEncoder.instances[0].emitNext(80, 'delta');
        MockVideoEncoder.instances[1].emitNext(160, 'delta');
        await next3;
        await iter.next();
    });

    it('controller shrink: disposed encoder count tracks ladder shrink', async () => {
        const stats = makeStats();
        const controller = new LayerLadderController([
            cfg(320, 180), cfg(640, 360), cfg(1280, 720),
        ]);
        const seg = encode({
            controller,
            createEncoder: makeFactory(),
        })(fromArray([
            makeBundle(1, stats, [
                { width: 320, height: 180 },
                { width: 640, height: 360 },
                { width: 1280, height: 720 },
            ]),
            makeBundle(2, stats, [
                { width: 320, height: 180 },
                { width: 640, height: 360 },
            ], false, 1),
        ]));
        const iter: AsyncIterator<EncodedBundle> = seg[Symbol.asyncIterator]();

        const next1 = iter.next();
        await waitForInstances(3);
        for (const m of MockVideoEncoder.instances) m.emitNext(80, 'key');
        await next1;

        // Capture the top encoder before shrinking so we can assert it closed.
        const topEncoder = MockVideoEncoder.instances[2];
        controller.setConfigs([cfg(320, 180), cfg(640, 360)]);

        const next2 = iter.next();
        await waitForCalls(0, 1);
        await waitForCalls(1, 1);
        MockVideoEncoder.instances[0].emitNext(80, 'delta');
        MockVideoEncoder.instances[1].emitNext(80, 'delta');
        const r2 = await next2;
        expect(r2.done).toBe(false);
        if (!r2.done) expect(r2.value.layers).toHaveLength(2);
        // Top encoder dispose() flushed and closed it.
        expect(topEncoder.state).toBe('closed');

        await iter.next();
    });

    it('drops a bundle whose ladder version the controller has moved past', async () => {
        const stats = makeStats();
        const controller = new LayerLadderController([cfg(640, 360)]);
        const seg = encode({
            controller,
            createEncoder: makeFactory(),
        })(fromArray([
            makeBundle(1, stats, [{ width: 640, height: 360 }]),
            // Still stamped v0: downscale emitted it before the reconfigure below.
            makeBundle(2, stats, [{ width: 640, height: 360 }, { width: 1280, height: 720 }]),
        ]));
        const iter: AsyncIterator<EncodedBundle> = seg[Symbol.asyncIterator]();

        const next1 = iter.next();
        await waitForCalls(0, 1);
        MockVideoEncoder.instances[0].emitNext(80, 'key');
        expect((await next1).done).toBe(false);

        // act: the ladder moves on while bundle 2 is mid-reshape
        controller.setConfigs([cfg(640, 360), cfg(1280, 720)]);
        const r2 = await iter.next();

        // assert: no encoder was built for the stale generation, and it leaked nothing
        expect(r2.done).toBe(true);
        expect(MockVideoEncoder.instances).toHaveLength(1);
    });
});
