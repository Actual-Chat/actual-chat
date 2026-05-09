import { describe, it, expect } from 'vitest';
import {
    downscale,
    type DownscaleOptions,
    type DownscalerLike,
    type LayerSpec,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/downscale';
import {
    type CapturedFrame,
    type VideoRecordingStats,
    createEmptyRecordingStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

// ---- Mocks ----------------------------------------------------------------

class MockVideoFrame {
    closed = false;
    constructor(
        public id: number,
        public codedWidth: number,
        public codedHeight: number,
    ) {}
    close(): void { this.closed = true; }
}

const mkFrame = (id: number, w: number, h: number): VideoFrame =>
    new MockVideoFrame(id, w, h) as unknown as VideoFrame;

/**
 * Fake downscaler: returns one freshly-allocated MockVideoFrame per
 * requested layer, sized to the layer's width/height. Closes the
 * input frame on the way out (matching the production contract).
 *
 * Records every `process()` call for assertions.
 */
class FakeDownscaler implements DownscalerLike {
    public calls: { input: VideoFrame; layers: readonly LayerSpec[] }[] = [];
    public disposeCount = 0;
    private nextId = 1000;

    process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        this.calls.push({ input, layers });
        const frames = layers.map(l => mkFrame(this.nextId++, l.width, l.height));
        // Close the input frame, as the contract specifies.
        (input as unknown as MockVideoFrame).close();
        return Promise.resolve(frames);
    }

    dispose(): void {
        this.disposeCount++;
    }
}

// ---- Helpers --------------------------------------------------------------

function makeStats(): VideoRecordingStats {
    return createEmptyRecordingStats(1_700_000_000_000);
}

function makeCaptured(
    index: number,
    stats: VideoRecordingStats,
    opts: { width?: number; height?: number; forceKeyframe?: boolean } = {},
): CapturedFrame {
    const width = opts.width ?? 1280;
    const height = opts.height ?? 720;
    return {
        frame: mkFrame(index, width, height),
        capturedAt: { timeMs: 1_700_000_000_000 + index, epoch: 0 },
        index,
        sourceWidth: width,
        sourceHeight: height,
        forceKeyframe: opts.forceKeyframe ?? false,
        stats,
    };
}

function fromArray<T>(items: T[]): AsyncIterable<T> {
    return fromArrayAsync(items);
}
async function* fromArrayAsync<T>(items: readonly T[]): AsyncIterable<T> {
    await Promise.resolve();
    for (const item of items) yield item;
}

async function drain<T>(seg: AsyncIterable<T>): Promise<T[]> {
    const out: T[] = [];
    for await (const item of seg) out.push(item);
    return out;
}

function makeOpts(
    ladder: LayerSpec[],
    factory: () => DownscalerLike,
): DownscaleOptions {
    return { ladder, createDownscaler: factory };
}

// ---- Tests ----------------------------------------------------------------

describe('downscale operator', () => {
    it('throws when constructed with an empty ladder', () => {
        expect(() =>
            downscale({ ladder: [], createDownscaler: () => new FakeDownscaler() }),
        ).toThrow(/at least one layer/);
    });

    it('1-tier ladder yields length-1 bundles', async () => {
        const stats = makeStats();
        const fake = new FakeDownscaler();
        const ladder: LayerSpec[] = [{ width: 640, height: 360 }];

        const seg = downscale(makeOpts(ladder, () => fake))(
            fromArray([makeCaptured(1, stats), makeCaptured(2, stats)]),
        );
        const out = await drain(seg);

        expect(out).toHaveLength(2);
        for (const bundle of out) {
            expect(bundle.frames).toHaveLength(1);
            expect(bundle.frames[0].frame.codedWidth).toBe(640);
            expect(bundle.frames[0].frame.codedHeight).toBe(360);
            expect(bundle.stats).toBe(stats);
        }
        expect(fake.calls).toHaveLength(2);
        expect(fake.disposeCount).toBe(1);   // disposed in finally
    });

    it('3-tier ladder: bottom-first (frames[0]=base, frames[topIdx]=top)', async () => {
        const stats = makeStats();
        const fake = new FakeDownscaler();
        const ladder: LayerSpec[] = [
            { width: 320, height: 180 },     // base (extra[0])
            { width: 640, height: 360 },     // mid  (extra[1])
            { width: 1280, height: 720 },    // top  (primary)
        ];

        const seg = downscale(makeOpts(ladder, () => fake))(
            fromArray([makeCaptured(7, stats)]),
        );
        const out = await drain(seg);

        expect(out).toHaveLength(1);
        const bundle = out[0];
        expect(bundle.frames).toHaveLength(3);
        // Bottom-first.
        expect(bundle.frames[0].frame.codedWidth).toBe(320);
        expect(bundle.frames[0].frame.codedHeight).toBe(180);
        expect(bundle.frames[1].frame.codedWidth).toBe(640);
        expect(bundle.frames[1].frame.codedHeight).toBe(360);
        expect(bundle.frames[2].frame.codedWidth).toBe(1280);
        expect(bundle.frames[2].frame.codedHeight).toBe(720);
    });

    it('all output envelopes share capturedAt / index / forceKeyframe / stats / sourceWidth/Height', async () => {
        const stats = makeStats();
        const fake = new FakeDownscaler();
        const ladder: LayerSpec[] = [
            { width: 320, height: 180 },
            { width: 640, height: 360 },
            { width: 1280, height: 720 },
        ];

        const seg = downscale(makeOpts(ladder, () => fake))(
            fromArray([makeCaptured(42, stats, { forceKeyframe: true, width: 1920, height: 1080 })]),
        );
        const [bundle] = await drain(seg);

        const all = bundle.frames;
        for (const e of all) {
            expect(e.capturedAt).toEqual({ timeMs: 1_700_000_000_042, epoch: 0 });
            expect(e.index).toBe(42);
            expect(e.forceKeyframe).toBe(true);
            expect(e.sourceWidth).toBe(1920);
            expect(e.sourceHeight).toBe(1080);
            expect(e.stats).toBe(stats);
        }
    });

    it('lazy-creates the downscaler on first iteration only', async () => {
        const stats = makeStats();
        let createCount = 0;
        const factory = (): DownscalerLike => {
            createCount++;
            return new FakeDownscaler();
        };
        const seg = downscale(makeOpts([{ width: 640, height: 360 }], factory))(
            fromArray([makeCaptured(1, stats), makeCaptured(2, stats), makeCaptured(3, stats)]),
        );
        await drain(seg);
        expect(createCount).toBe(1);
    });

    it('does not create the downscaler when the source is empty', async () => {
        let createCount = 0;
        const factory = (): DownscalerLike => {
            createCount++;
            return new FakeDownscaler();
        };
        const seg = downscale(makeOpts([{ width: 640, height: 360 }], factory))(
            fromArray<CapturedFrame>([]),
        );
        const out = await drain(seg);
        expect(out).toEqual([]);
        expect(createCount).toBe(0);
    });

    it('frame ownership: input is consumed (closed by downscaler), outputs are fresh', async () => {
        const stats = makeStats();
        const fake = new FakeDownscaler();
        const ladder: LayerSpec[] = [
            { width: 320, height: 180 },
            { width: 640, height: 360 },
        ];
        const captured = makeCaptured(1, stats);
        const inputFrame = captured.frame as unknown as MockVideoFrame;

        const seg = downscale(makeOpts(ladder, () => fake))(fromArray([captured]));
        const [bundle] = await drain(seg);

        // Input frame closed by the fake (matching production contract).
        expect(inputFrame.closed).toBe(true);
        // Outputs are fresh (different instances) and still open.
        for (const f of bundle.frames) {
            expect(f.frame).not.toBe(captured.frame);
            expect((f.frame as unknown as MockVideoFrame).closed).toBe(false);
        }
    });

    it('disposes the downscaler on normal completion', async () => {
        const stats = makeStats();
        const fake = new FakeDownscaler();
        const seg = downscale(makeOpts([{ width: 320, height: 180 }], () => fake))(
            fromArray([makeCaptured(1, stats)]),
        );
        await drain(seg);
        expect(fake.disposeCount).toBe(1);
    });

    it('disposes the downscaler when the source throws', async () => {
        const stats = makeStats();
        const fake = new FakeDownscaler();

        const failingAsync = async function* (): AsyncIterable<CapturedFrame> {
            await Promise.resolve();
            yield makeCaptured(1, stats);
            throw new Error('upstream blew up');
        };
        const failing: AsyncIterable<CapturedFrame> = failingAsync();
        const seg = downscale(makeOpts([{ width: 320, height: 180 }], () => fake))(failing);
        await expect(drain(seg)).rejects.toThrow('upstream blew up');
        expect(fake.disposeCount).toBe(1);
    });

    it('throws and closes returned frames if downscaler returns a wrong-length array', async () => {
        const stats = makeStats();
        // Faulty: returns one frame regardless of how many layers were asked.
        const orphan = new MockVideoFrame(9000, 10, 10);
        const faulty: DownscalerLike = {
            process(input, _layers) {
                (input as unknown as MockVideoFrame).close();
                return Promise.resolve([orphan as unknown as VideoFrame]);
            },
        };
        const seg = downscale(makeOpts(
            [{ width: 320, height: 180 }, { width: 640, height: 360 }],
            () => faulty,
        ))(fromArray([makeCaptured(1, stats)]));

        await expect(drain(seg)).rejects.toThrow(/returned 1 frames, expected 2/);
        // Orphaned output frame must be closed by the operator's defensive cleanup.
        expect(orphan.closed).toBe(true);
    });

    it('handles a missing dispose() on the downscaler without throwing', async () => {
        const stats = makeStats();
        const noDispose: DownscalerLike = {
            process(input, layers) {
                (input as unknown as MockVideoFrame).close();
                return Promise.resolve(layers.map((l, i) => mkFrame(2000 + i, l.width, l.height)));
            },
        };
        const seg = downscale(makeOpts([{ width: 640, height: 360 }], () => noDispose))(
            fromArray([makeCaptured(1, stats)]),
        );
        const out = await drain(seg);
        expect(out).toHaveLength(1);
    });

    it('hang watchdog: timed-out process discards downscaler, next bundle is force-keyframed', async () => {
        const stats = makeStats();
        let callCount = 0;
        let nextId = 5000;
        const disposed: number[] = [];
        const factories: number[] = [];
        const factory = (): DownscalerLike => {
            const id = factories.length;
            factories.push(id);
            return {
                process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
                    callCount++;
                    if (callCount === 1) {
                        // Hang forever — never resolves.
                        return new Promise<VideoFrame[]>(() => { /* never */ });
                    }
                    (input as unknown as MockVideoFrame).close();
                    const out = layers.map(l => mkFrame(nextId++, l.width, l.height));
                    return Promise.resolve(out);
                },
                dispose(): void { disposed.push(id); },
            };
        };

        const seg = downscale({
            ladder: [{ width: 640, height: 360 }],
            createDownscaler: factory,
            hangTimeoutMs: 10,  // real timer; fires quickly
        })(fromArray([
            makeCaptured(1, stats),
            makeCaptured(2, stats),
        ]));

        const out = await drain(seg);
        // First frame is consumed silently after watchdog timeout.
        // Second frame yields a bundle marked forceKeyframe (post-hang
        // re-anchor) so encoders restart cleanly.
        expect(out).toHaveLength(1);
        expect(out[0].frames.every(f => f.forceKeyframe)).toBe(true);
        // Two factory calls: original + post-hang replacement.
        expect(factories.length).toBe(2);
        // Both downscalers disposed (the hung one immediately, the
        // replacement in the outer finally on completion).
        expect(disposed).toContain(0);
        expect(disposed).toContain(1);
    });

    it('passes the configured ladder verbatim to the downscaler each frame', async () => {
        const stats = makeStats();
        const fake = new FakeDownscaler();
        const ladder: LayerSpec[] = [
            { width: 320, height: 180 },
            { width: 1280, height: 720 },
        ];
        const seg = downscale(makeOpts(ladder, () => fake))(
            fromArray([makeCaptured(1, stats), makeCaptured(2, stats)]),
        );
        await drain(seg);
        expect(fake.calls).toHaveLength(2);
        for (const c of fake.calls) {
            expect(c.layers).toHaveLength(2);
            expect(c.layers[0]).toMatchObject({ width: 320, height: 180 });
            expect(c.layers[1]).toMatchObject({ width: 1280, height: 720 });
        }
    });
});
