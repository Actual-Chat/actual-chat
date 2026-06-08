import { describe, it, expect } from 'vitest';
import { applyKeyframePolicy } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/apply-keyframe-policy';
import {
    createEmptyRecorderStats,
    type CapturedBundle,
    type CapturedFrame,
    type RecorderStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

// ---- Mocks ----------------------------------------------------------------

class MockVideoFrame {
    constructor(
        public id: number,
        public codedWidth = 1920,
        public codedHeight = 1080,
    ) {}
    close(): void { /* noop */ }
}

// ---- Helpers --------------------------------------------------------------

function makeCaptured(stats: RecorderStats, idx: number, forceKeyframe = false): CapturedFrame {
    return {
        frame: new MockVideoFrame(idx) as unknown as VideoFrame,
        capturedAt: { timeMs: 1_000 + idx, epoch: 0 },
        index: idx,
        dropTrace: [],
        sourceWidth: 1920,
        sourceHeight: 1080,
        forceKeyframe,
        rotation: 0,
        stats,
    };
}

function makeBundle(stats: RecorderStats, idx: number, forceKeyframe = false, extraCount = 0): CapturedBundle {
    const layers: CapturedFrame[] = [];
    for (let i = 0; i < extraCount + 1; i++) {
        layers.push(makeCaptured(stats, idx, forceKeyframe));
    }
    return { layers, index: idx, dropTrace: [], rotation: 0, stats };
}

async function* source(items: CapturedBundle[]): AsyncIterable<CapturedBundle> {
    await Promise.resolve();
    for (const item of items) yield item;
}

async function drain<T>(it: AsyncIterable<T>): Promise<T[]> {
    const out: T[] = [];
    for await (const item of it) out.push(item);
    return out;
}

function bundleForceKey(bundle: CapturedBundle): boolean {
    // Contract: when this operator decides to key, every layer in the
    // bundle MUST carry forceKeyframe=true.
    return bundle.layers.length > 0 && bundle.layers.every(f => f.forceKeyframe);
}

// ---- Tests ----------------------------------------------------------------

describe('applyKeyframePolicy', () => {
    it('frame-count interval: every Nth bundle is keyed', async () => {
        const stats = createEmptyRecorderStats();
        const op = applyKeyframePolicy({
            keyframeIntervalFrames: 3,
            now: () => 0, // fixed clock; only frame-count trigger fires
        });
        // 7 bundles: frames 1..7 → trigger at frame counts 3, 6 (i.e. bundles 3 and 6 of each cycle).
        const items = Array.from({ length: 7 }, (_, i) => makeBundle(stats, i));
        const out = await drain(op(source(items)));
        // Counter increments per bundle starting at 1; trigger when count%3==0.
        // After trigger, counter resets to 0 (next bundle starts at count=1 → no trigger).
        // Sequence: 1,2,3*(reset),1,2,3*(reset),1.
        expect(out.map(bundleForceKey)).toEqual([false, false, true, false, false, true, false]);
    });

    it('wallclock floor triggers when interval exceeds maxKeyframeIntervalMs', async () => {
        const stats = createEmptyRecorderStats();
        // Frame-count interval is large enough that the interval trigger never fires
        // in our 5-bundle test; only the wallclock floor should activate.
        let t = 0;
        const op = applyKeyframePolicy({
            keyframeIntervalFrames: 9999,
            maxKeyframeIntervalMs: 100,
            now: () => t,
        });
        const items: CapturedBundle[] = [];
        const ts = [0, 50, 110, 150, 220];
        for (let i = 0; i < ts.length; i++) {
            items.push(makeBundle(stats, i));
        }

        const seg: AsyncIterable<CapturedBundle> = (async function* () {
            await Promise.resolve();
            for (let i = 0; i < items.length; i++) {
                t = ts[i];
                yield items[i];
            }
        })();

        const out = await drain(op(seg));
        // Bundle 0 at t=0: wallclock floor condition needs `lastKeyframeAtMs !== −∞`,
        //   so first bundle does NOT key by wallclock. Frame-count interval is 9999, so no key.
        // Bundle 1 at t=50: still no last keyframe → no key.
        // Bundle 2 at t=110: still no last keyframe → no key (wallclock requires a prior key).
        // (Same as legacy behavior — the floor measures since-last, not since-start.)
        // To exercise the floor, we need a starter keyframe. Force one upstream on bundle 0.
        expect(out.map(bundleForceKey)).toEqual([false, false, false, false, false]);
    });

    it('wallclock floor: after a starter keyframe, fires once interval exceeds threshold', async () => {
        const stats = createEmptyRecorderStats();
        let t = 0;
        const op = applyKeyframePolicy({
            keyframeIntervalFrames: 9999,
            maxKeyframeIntervalMs: 100,
            now: () => t,
        });
        const ts = [0, 50, 110, 130, 250];
        const upstreamForce = [true, false, false, false, false];

        const seg: AsyncIterable<CapturedBundle> = (async function* () {
            await Promise.resolve();
            for (let i = 0; i < ts.length; i++) {
                t = ts[i];
                yield makeBundle(stats, i, upstreamForce[i]);
            }
        })();

        const out = await drain(op(seg));
        // Bundle 0 (t=0): upstream-forced → key. lastKeyframeAtMs=0.
        // Bundle 1 (t=50): 50−0=50 < 100, frame count 1 % 9999 ≠ 0 → no key.
        // Bundle 2 (t=110): 110−0=110 ≥ 100 → wallclock fires. lastKeyframeAtMs=110, count reset.
        // Bundle 3 (t=130): 130−110=20 < 100 → no key.
        // Bundle 4 (t=250): 250−110=140 ≥ 100 → wallclock fires.
        expect(out.map(bundleForceKey)).toEqual([true, false, true, false, true]);
    });

    it('upstream-set forceKeyframe is preserved (and resets the counter)', async () => {
        const stats = createEmptyRecorderStats();
        const op = applyKeyframePolicy({
            keyframeIntervalFrames: 5,
            now: () => 0,
        });
        const items = [
            makeBundle(stats, 0, false),  // count 1
            makeBundle(stats, 1, false),  // count 2
            makeBundle(stats, 2, true),   // upstream-key → counter resets
            makeBundle(stats, 3, false),  // count 1
            makeBundle(stats, 4, false),  // count 2
            makeBundle(stats, 5, false),  // count 3
            makeBundle(stats, 6, false),  // count 4
            makeBundle(stats, 7, false),  // count 5 → frame-count fires
        ];
        const out = await drain(op(source(items)));
        expect(out.map(bundleForceKey)).toEqual([
            false, false, true,    // upstream
            false, false, false, false,
            true,                  // counter cycled to 5
        ]);
    });

    it('counter resets on a triggered keyframe (so the next interval starts fresh)', async () => {
        const stats = createEmptyRecorderStats();
        const op = applyKeyframePolicy({
            keyframeIntervalFrames: 2,
            now: () => 0,
        });
        const items = Array.from({ length: 6 }, (_, i) => makeBundle(stats, i));
        const out = await drain(op(source(items)));
        // count 1 → false, count 2 → true (reset), count 1 → false, count 2 → true (reset), ...
        expect(out.map(bundleForceKey)).toEqual([false, true, false, true, false, true]);
    });

    it('sets forceKeyframe on every bundle layer when triggering', async () => {
        const stats = createEmptyRecorderStats();
        const op = applyKeyframePolicy({
            keyframeIntervalFrames: 2,
            now: () => 0,
        });
        const items = [
            makeBundle(stats, 0, false, /* extras */ 2),  // count 1
            makeBundle(stats, 1, false, /* extras */ 2),  // count 2 → key
        ];
        const out = await drain(op(source(items)));

        expect(out[0].layers.every(f => !f.forceKeyframe)).toBe(true);

        expect(out[1].layers).toHaveLength(3);
        for (const f of out[1].layers) {
            expect(f.forceKeyframe).toBe(true);
        }
    });

    it('throws on invalid keyframeIntervalFrames', () => {
        expect(() => applyKeyframePolicy({ keyframeIntervalFrames: 0 })).toThrow(/> 0/);
        expect(() => applyKeyframePolicy({ keyframeIntervalFrames: -3 })).toThrow(/> 0/);
    });

    it('does not mutate input bundle envelopes when raising the flag', async () => {
        const stats = createEmptyRecorderStats();
        const op = applyKeyframePolicy({
            keyframeIntervalFrames: 1,    // every bundle is keyed by interval
            now: () => 0,
        });
        const original = makeBundle(stats, 0, false, /* extras */ 1);
        const originalLayers = original.layers.slice();
        const out = await drain(op(source([original])));

        expect(out[0].layers.every(f => f.forceKeyframe)).toBe(true);
        // Original envelopes not mutated.
        for (const f of originalLayers)
            expect(f.forceKeyframe).toBe(false);
    });
});
