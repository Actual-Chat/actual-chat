import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
    encode,
    type EncodeOptions,
    type EncodeInput,
    type EncoderConfigPerLayer,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/encode';
import {
    type SimulcastBundle,
    type CapturedFrame,
    type EncodedFrame,
    type VideoRecordingStats,
    createEmptyRecordingStats,
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
    state: 'unconfigured' | 'configured' | 'closed' = 'configured';
    encodeCalls: { frame: MockVideoFrame; opts: { keyFrame: boolean } }[] = [];
    configureCalls: VideoEncoderConfig[] = [];
    resetCalls = 0;
    output: MockVideoEncoderInit['output'];
    error: MockVideoEncoderInit['error'];

    constructor(init: MockVideoEncoderInit) {
        this.output = init.output;
        this.error = init.error;
        MockVideoEncoder.instances.push(this);
    }

    encode(frame: MockVideoFrame, opts: { keyFrame: boolean }): void {
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
    (globalThis as unknown as GlobalWithVideoEncoder).VideoEncoder = MockVideoEncoder;
});

afterEach(() => {
    delete (globalThis as unknown as GlobalWithVideoEncoder).VideoEncoder;
    vi.useRealTimers();
});

// ---- Helpers --------------------------------------------------------------

function makeStats(): VideoRecordingStats {
    return createEmptyRecordingStats(1_700_000_000_000);
}

function mkFrame(id: number, w: number, h: number): VideoFrame {
    return new MockVideoFrame(id, w, h) as unknown as VideoFrame;
}

function makeCaptured(
    index: number,
    stats: VideoRecordingStats,
    width: number,
    height: number,
    forceKeyframe = false,
): CapturedFrame {
    return {
        frame: mkFrame(index, width, height),
        capturedAt: { timeMs: 1_700_000_000_000 + index, epoch: 0 },
        index,
        sourceWidth: 1920,
        sourceHeight: 1080,
        forceKeyframe,
        stats,
    };
}

function makeBundle(
    index: number,
    stats: VideoRecordingStats,
    layers: { width: number; height: number }[],
    forceKeyframe = false,
): SimulcastBundle {
    if (layers.length === 0) throw new Error('makeBundle: at least one layer');
    const top = layers[layers.length - 1];
    const primary = makeCaptured(index, stats, top.width, top.height, forceKeyframe);
    const extras: CapturedFrame[] = [];
    for (let i = 0; i < layers.length - 1; i++) {
        const l = layers[i];
        // Extras share index/capturedAt/forceKeyframe/stats with primary.
        const c = makeCaptured(index, stats, l.width, l.height, forceKeyframe);
        extras.push(c);
    }
    return { primary, extras, stats };
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
                index: input.index,
                spatialLayerId: layerId,
                sourceWidth: 0,
                sourceHeight: 0,
                encodedWidth: config.width,
                encodedHeight: config.height,
                stats: undefined as unknown as VideoRecordingStats,
            }),
            () => { /* swallow encoder error */ },
            {
                timeoutMs: opts.timeoutMs ?? 100,
                firstTimeoutMs: opts.timeoutMs ?? 100,
                onResetRequested: opts.onResetRequested,
            },
        );
        enc.configure({
            codec: config.codec,
            width: config.width,
            height: config.height,
            bitrate: config.bitrate,
            framerate: config.framerate,
            latencyMode: 'realtime',
        });
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
    it('throws when constructed with empty configs', () => {
        expect(() => encode({
            configs: [],
            createEncoder: makeFactory(),
        })).toThrow(/at least one layer/);
    });

    it('single layer: 5 bundles → 5 EncodedFrames out, in order', async () => {
        const stats = makeStats();
        const opts: EncodeOptions = {
            configs: [cfg(640, 360)],
            createEncoder: makeFactory(),
        };

        const bundles: SimulcastBundle[] = [];
        for (let i = 1; i <= 5; i++)
            bundles.push(makeBundle(i, stats, [{ width: 640, height: 360 }]));

        const seg = encode(opts)(fromArray(bundles));
        const iter: AsyncIterator<EncodedFrame> = seg[Symbol.asyncIterator]();

        const results: EncodedFrame[] = [];
        for (const _b of bundles) {
            // Kick off the next iteration; it submits to the encoder, then
            // awaits the matching chunk. We let microtasks run, then emit.
            const next = iter.next();
            await waitForCalls(0, 1);
            const mock = MockVideoEncoder.instances[0];
            mock.emitNext(50);
            const r = await next;
            expect(r.done).toBe(false);
            if (r.done === false) results.push(r.value);
        }
        const final = await iter.next();
        expect(final.done).toBe(true);

        expect(results).toHaveLength(5);
        expect(results.map(r => r.index)).toEqual([1, 2, 3, 4, 5]);
        for (const result of results) {
            expect(result.spatialLayerId).toBe(0);
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
            configs: [cfg(640, 360)],
            createEncoder: makeFactory(),
        })(fromArray([
            makeBundle(1, stats, [{ width: 640, height: 360 }], false),
            makeBundle(2, stats, [{ width: 640, height: 360 }], true),
        ]));
        const iter: AsyncIterator<EncodedFrame> = seg[Symbol.asyncIterator]();

        // First bundle submitted: keyFrame=false.
        const next1 = iter.next();
        await waitForCalls(0, 1);
        const mock = MockVideoEncoder.instances[0];
        expect(mock.encodeCalls).toHaveLength(1);
        expect(mock.encodeCalls[0].opts.keyFrame).toBe(false);
        mock.emitNext();
        await next1;

        // Second bundle submitted: keyFrame=true.
        const next2 = iter.next();
        await waitForCalls(0, 1);
        expect(mock.encodeCalls).toHaveLength(1);
        expect(mock.encodeCalls[0].opts.keyFrame).toBe(true);
        mock.emitNext();
        await next2;
        await iter.next();
    });

    it('multi-layer (3 tiers): 5 bundles → 15 EncodedFrames (1 per layer per bundle)', async () => {
        const stats = makeStats();
        const layers = [
            { width: 320, height: 180 },
            { width: 640, height: 360 },
            { width: 1280, height: 720 },
        ];
        const opts: EncodeOptions = {
            configs: layers.map(l => cfg(l.width, l.height)),
            createEncoder: makeFactory(),
        };
        const bundles: SimulcastBundle[] = [];
        for (let i = 1; i <= 5; i++) bundles.push(makeBundle(i, stats, layers));

        const seg = encode(opts)(fromArray(bundles));
        const iter: AsyncIterator<EncodedFrame> = seg[Symbol.asyncIterator]();

        const collected: EncodedFrame[] = [];
        for (const _b of bundles) {
            // Kick off the iteration that will submit to all 3 encoders.
            const firstNext = iter.next();
            await waitForInstances(3);
            // Emit one chunk per encoder.
            for (const m of MockVideoEncoder.instances) m.emitNext(80);
            // Pull all 3 results (the operator yields one per layer per bundle).
            const r1 = await firstNext;
            if (r1.done === false) collected.push(r1.value);
            const r2 = await iter.next();
            if (r2.done === false) collected.push(r2.value);
            const r3 = await iter.next();
            if (r3.done === false) collected.push(r3.value);
        }
        const tail = await iter.next();
        expect(tail.done).toBe(true);

        expect(collected).toHaveLength(15);

        // Group by source bundle (`index`) and verify all 3 layer ids present.
        const byIndex = new Map<number, EncodedFrame[]>();
        for (const f of collected) {
            const arr = byIndex.get(f.index) ?? [];
            arr.push(f);
            byIndex.set(f.index, arr);
        }
        expect(byIndex.size).toBe(5);
        for (const [, group] of byIndex) {
            expect(group).toHaveLength(3);
            expect(group.map(g => g.spatialLayerId)).toEqual([0, 1, 2]);
            const ids = group.map(g => g.spatialLayerId).sort();
            expect(ids).toEqual([0, 1, 2]);
            // Encoded dims line up with config.
            const byLayer = new Map(group.map(g => [g.spatialLayerId, g]));
            expect(byLayer.get(0)?.encodedWidth).toBe(320);
            expect(byLayer.get(1)?.encodedWidth).toBe(640);
            expect(byLayer.get(2)?.encodedWidth).toBe(1280);
        }
    });

    it('updates stats counters: chunksEncoded, bytesEncoded, keyframesEncoded', async () => {
        const stats = makeStats();
        const layers = [
            { width: 320, height: 180 },
            { width: 1280, height: 720 },
        ];
        const opts: EncodeOptions = {
            configs: layers.map(l => cfg(l.width, l.height)),
            createEncoder: makeFactory(),
        };
        const bundles: SimulcastBundle[] = [
            makeBundle(1, stats, layers, true),    // keyframe
            makeBundle(2, stats, layers, false),
            makeBundle(3, stats, layers, false),
        ];
        const seg = encode(opts)(fromArray(bundles));
        const iter: AsyncIterator<EncodedFrame> = seg[Symbol.asyncIterator]();

        let totalBytes = 0;
        let bundleIdx = 0;
        for (const _b of bundles) {
            const firstNext = iter.next();
            await waitForInstances(layers.length);
            const byteSizes = [70 + bundleIdx * 10, 130 + bundleIdx * 10];
            const instances: MockVideoEncoder[] = MockVideoEncoder.instances.slice(0, layers.length);
            instances.forEach((inst, layerIdx) => {
                inst.emitNext(byteSizes[layerIdx]);
                totalBytes += byteSizes[layerIdx];
            });
            await firstNext;
            for (let l = 1; l < layers.length; l++) await iter.next();
            bundleIdx++;
        }
        await iter.next();

        expect(stats.chunksEncoded).toBe(6);          // 3 bundles × 2 layers
        expect(stats.keyframesEncoded).toBe(2);       // both layers of bundle 1
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
                    capturedAt: input.capturedAt,
                    index: input.index,
                    spatialLayerId: 0,
                    sourceWidth: 0,
                    sourceHeight: 0,
                    encodedWidth: 640,
                    encodedHeight: 360,
                    stats: undefined as unknown as VideoRecordingStats,
                });
            },
            dispose(): void { /* ignore */ },
        } as unknown as AsyncVideoEncoder<EncodeInput, EncodedFrame>;
        const seg = encode({
            configs: [cfg(640, 360)],
            createEncoder: () => fakeEncoder,
        })(fromArray([first, second]));

        const iter: AsyncIterator<EncodedFrame> = seg[Symbol.asyncIterator]();
        const result = await iter.next();
        expect(result.done).toBe(false);
        expect(result.done === false ? result.value.index : 0).toBe(2);
        expect(encodeCalls).toHaveLength(2);
        expect(encodeCalls[0].opts.keyFrame).toBe(false);
        expect(encodeCalls[1].opts.keyFrame).toBe(true);
        expect((first.primary.frame as unknown as MockVideoFrame).closed).toBe(true);
        await iter.next();
    });

    it('onResetRequested fires when the wrapper degrades (timeout case)', async () => {
        vi.useFakeTimers();
        const stats = makeStats();
        const reasons: string[] = [];
        const seg = encode({
            configs: [cfg(640, 360)],
            createEncoder: makeFactory({
                timeoutMs: 50,
                onResetRequested: r => reasons.push(r),
            }),
        })(fromArray([makeBundle(1, stats, [{ width: 640, height: 360 }])]));

        const iter: AsyncIterator<EncodedFrame> = seg[Symbol.asyncIterator]();
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
            configs: layers.map(l => cfg(l.width, l.height)),
            createEncoder: makeFactory(),
        })(fromArray([makeBundle(1, stats, layers)]));

        const iter: AsyncIterator<EncodedFrame> = seg[Symbol.asyncIterator]();
        const firstNext = iter.next();
        await waitForInstances(layers.length);
        for (const m of MockVideoEncoder.instances) m.emitNext(64);
        await firstNext;
        for (let l = 1; l < layers.length; l++) {
            await iter.next();
        }
        await iter.next();

        // After source completes & finally runs, every underlying mock encoder
        // should be in 'closed' state.
        expect(MockVideoEncoder.instances).toHaveLength(2);
        for (const m of MockVideoEncoder.instances) expect(m.state).toBe('closed');
    });

    it('rejects bundles whose layer count mismatches configs (defensive)', async () => {
        const stats = makeStats();
        // configs: 2 layers; bundle: 1 layer.
        const seg = encode({
            configs: [cfg(320, 180), cfg(1280, 720)],
            createEncoder: makeFactory(),
        })(fromArray([makeBundle(1, stats, [{ width: 1280, height: 720 }])]));
        await expect(drain(seg)).rejects.toThrow(/expected 2/);
    });
});
