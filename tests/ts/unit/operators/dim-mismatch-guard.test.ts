import { describe, it, expect, vi, beforeEach, afterEach, type MockInstance } from 'vitest';
import { dropDimMismatch } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/dim-mismatch-guard';
import {
    createEmptyRecorderStats,
    type CapturedFrame,
    type RecorderStats,
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

const mkFrame = (id: number, w: number, h: number): MockVideoFrame => new MockVideoFrame(id, w, h);

// ---- Helpers --------------------------------------------------------------

function envelopeFor(stats: RecorderStats, frame: MockVideoFrame, index = 0): CapturedFrame {
    return {
        frame: frame as unknown as VideoFrame,
        capturedAt: { timeMs: 100 + index, epoch: 0 },
        durationUs: 1_000_000 / 30,
        index,
        dropTrace: [],
        sourceWidth: frame.codedWidth,
        sourceHeight: frame.codedHeight,
        forceKeyframe: false,
        rotation: 0,
        stats,
    };
}

function source(items: CapturedFrame[]): AsyncIterable<CapturedFrame> {
    return (async function* () {
        await Promise.resolve();
        for (const item of items) yield item;
    })();
}

async function drain<T>(seg: AsyncIterable<T>): Promise<T[]> {
    const out: T[] = [];
    for await (const item of seg) out.push(item);
    return out;
}

// ---- Tests ----------------------------------------------------------------

describe('dropDimMismatch', () => {
    let consoleWarnSpy: MockInstance<(...args: unknown[]) => void>;

    beforeEach(() => {
        // The operator's warn goes through `getLogs('VideoPipeline').warnLog`
        // which ultimately writes to console. Spy on console.warn so the
        // tests can both silence noise AND assert call counts.
        consoleWarnSpy = vi.spyOn(console, 'warn').mockImplementation(() => { /* noop */ }) as unknown as MockInstance<(...args: unknown[]) => void>;
    });

    afterEach(() => {
        consoleWarnSpy.mockRestore();
    });

    it('matching dims: passes through unchanged; stats not incremented', async () => {
        const stats = createEmptyRecorderStats();
        const op = dropDimMismatch({ getExpectedDims: () => ({ width: 1920, height: 1080 }) });
        const frames = [mkFrame(1, 1920, 1080), mkFrame(2, 1920, 1080)];
        const envelopes = frames.map((f, i) => envelopeFor(stats, f, i));

        const out = await drain(op(source(envelopes)));

        expect(out).toHaveLength(2);
        expect(out[0].frame).toBe(frames[0] as unknown as VideoFrame);
        for (const f of frames) expect(f.closed).toBe(false);
    });

    it('burst coalescing: multiple consecutive mismatches → exactly one warn', async () => {
        const stats = createEmptyRecorderStats();
        const op = dropDimMismatch({ getExpectedDims: () => ({ width: 1920, height: 1080 }) });
        const bad = [
            mkFrame(1, 1280, 720),
            mkFrame(2, 1280, 720),
            mkFrame(3, 1280, 720),
        ];
        const envelopes = bad.map((f, i) => envelopeFor(stats, f, i));

        const out = await drain(op(source(envelopes)));

        expect(out).toHaveLength(0);
        for (const f of bad) expect(f.closed).toBe(true);
        expect(consoleWarnSpy).toHaveBeenCalledTimes(1);
    });

    it('burst flag clears on a passing frame; a fresh mismatch warns again', async () => {
        const stats = createEmptyRecorderStats();
        const op = dropDimMismatch({ getExpectedDims: () => ({ width: 1920, height: 1080 }) });
        const sequence = [
            mkFrame(1, 1280, 720),  // mismatch — warn 1
            mkFrame(2, 1280, 720),  // mismatch — coalesced (no new warn)
            mkFrame(3, 1920, 1080), // OK — burst flag clears
            mkFrame(4, 1280, 720),  // mismatch — warn 2 (new burst)
        ];
        const envelopes = sequence.map((f, i) => envelopeFor(stats, f, i));

        const out = await drain(op(source(envelopes)));

        expect(out).toHaveLength(1);
        expect(out[0].frame).toBe(sequence[2] as unknown as VideoFrame);
        expect(consoleWarnSpy).toHaveBeenCalledTimes(2);
    });

    it('getExpectedDims is called per frame (allows reconfigure to take effect mid-stream)', async () => {
        const stats = createEmptyRecorderStats();
        let expected: { width: number; height: number } = { width: 1920, height: 1080 };
        const op = dropDimMismatch({ getExpectedDims: () => expected });

        const envelopes: CapturedFrame[] = [];
        const frames = [
            mkFrame(1, 1920, 1080), // matches initial expected
            mkFrame(2, 1280, 720),  // matches reconfigured expected
            mkFrame(3, 1280, 720),  // matches reconfigured expected
        ];
        envelopes.push(envelopeFor(stats, frames[0], 0));
        envelopes.push(envelopeFor(stats, frames[1], 1));
        envelopes.push(envelopeFor(stats, frames[2], 2));

        // Reconfigure happens between frame 0 and frame 1 in this in-memory test
        // by yielding control to the operator stepwise via a generator.
        const gen = (async function* () {
            await Promise.resolve();
            for (const env of envelopes) {
                if (env.index === 1) expected = { width: 1280, height: 720 };
                yield env;
            }
        })();
        const seg: AsyncIterable<CapturedFrame> = gen;

        const out = await drain(op(seg));

        expect(out).toHaveLength(3);
    });
});
