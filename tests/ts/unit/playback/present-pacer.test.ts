import { describe, it, expect } from 'vitest';
import { count, pipe } from 'ix-ext';
import {
    presentPacer,
    type PresentSink,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/present-pacer';
import {
    createEmptyPlayerStats,
    type DecodedFrame,
    type PlayerStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

// Direct coverage of the sink-agnostic pacer core, isolated from the mstg/canvas
// IO. The operator tests exercise the pacing math through real sinks; this file
// pins the PresentSink contract — including the present()-returns-false branch
// that neither real sink hits.

class MockVideoFrame {
    closed = false;
    constructor(public id = 0) {}
    close(): void { this.closed = true; }
    codedWidth = 320;
    codedHeight = 180;
    displayWidth = 320;
    displayHeight = 180;
}

type SinkMode = 'ok' | 'fail' | 'throw';

class MockSink implements PresentSink {
    public presented: VideoFrame[] = [];
    public disposed = false;
    constructor(public mode: SinkMode = 'ok') {}
    present(frame: VideoFrame): Promise<boolean> {
        if (this.mode === 'throw') return Promise.reject(new Error('sink boom'));
        if (this.mode === 'fail') return Promise.resolve(false);
        this.presented.push(frame);
        return Promise.resolve(true);
    }
    dispose(): void { this.disposed = true; }
}

function makeEnvelope(stats: PlayerStats, id: number, capturedAtMs: number, frame?: MockVideoFrame): DecodedFrame {
    const f = frame ?? new MockVideoFrame(id);
    return {
        frame: f as unknown as VideoFrame,
        capturedAt: { timeMs: capturedAtMs, epoch: 0 },
        arrivedAt: { timeMs: capturedAtMs + 100, epoch: 0 },
        decodedAt: { timeMs: capturedAtMs + 200, epoch: 0 },
        index: id,
        dropTrace: [],
        layerId: 0,
        rotation: 0,
        stats,
    };
}

function staticSource(items: DecodedFrame[]): AsyncIterable<DecodedFrame> {
    return (async function* () {
        await Promise.resolve();
        for (const item of items) yield item;
    })();
}

// nowFn auto-advances 1 s per call so natural-delta pacing never sleeps.
function defaults(): { getBufferSpanMs: () => number; targetSpanMs: number; nowFn: () => number; delayFn: () => Promise<void> } {
    let t = 0;
    return {
        getBufferSpanMs: (): number => 0,
        targetSpanMs: 333,
        nowFn: (): number => { t += 1000; return t; },
        delayFn: (): Promise<void> => Promise.resolve(),
    };
}

describe('presentPacer', () => {
    it('presents every frame through the sink, increments presented, disposes once', async () => {
        const stats = createEmptyPlayerStats();
        const sink = new MockSink();
        const frames = Array.from({ length: 4 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, i * 33, f));

        await count(pipe(staticSource(items), presentPacer({ createSink: () => sink, ...defaults() })));

        expect(sink.presented).toHaveLength(4);
        expect(stats.presented).toBe(4);
        expect(frames.map(f => f.closed)).toEqual([true, true, true, true]);
        expect(sink.disposed).toBe(true);
    });

    it('createSink is called exactly once and reused across frames', async () => {
        const stats = createEmptyPlayerStats();
        const sink = new MockSink();
        let createCalls = 0;
        const items = Array.from({ length: 5 }, (_, i) => makeEnvelope(stats, i, i * 33));

        await count(pipe(staticSource(items), presentPacer({
            createSink: () => { createCalls++; return sink; },
            ...defaults(),
        })));

        expect(createCalls).toBe(1);
        expect(sink.presented).toHaveLength(5);
    });

    it('present() returning false counts a drop, does not increment presented, and continues', async () => {
        const stats = createEmptyPlayerStats();
        const sink = new MockSink('fail');
        const frames = Array.from({ length: 3 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, i * 33, f));

        await count(pipe(staticSource(items), presentPacer({ createSink: () => sink, ...defaults() })));

        // No throw: the whole stream drains. Nothing presented, all frames closed,
        // and every frame is attributed as a presenter drop.
        expect(stats.presented).toBe(0);
        expect(frames.every(f => f.closed)).toBe(true);
        expect(stats.dropTrace.get(64 /* ReceiverPresent */) ?? 0).toBe(0);
        // pendingPresenterDrops accrues but is only flushed to the histogram on a
        // successful present; with no success it stays pending.
        expect(stats.pendingPresenterDrops).toBe(3);
    });

    it('present() throwing propagates, closes the frame, and still counts the drop', async () => {
        const stats = createEmptyPlayerStats();
        const sink = new MockSink('throw');
        const frame = new MockVideoFrame(0);
        const items = [makeEnvelope(stats, 0, 0, frame)];

        await expect(count(pipe(staticSource(items), presentPacer({ createSink: () => sink, ...defaults() }))))
            .rejects.toThrow('sink boom');
        expect(stats.presented).toBe(0);
        expect(frame.closed).toBe(true);
        expect(stats.pendingPresenterDrops).toBe(1);
    });

    it('trackSkipRatio:false leaves presentSkipRatio untouched', async () => {
        const stats = createEmptyPlayerStats();
        const initialSkipRatio = stats.presentSkipRatio;
        const sink = new MockSink();
        const items = Array.from({ length: 3 }, (_, i) => makeEnvelope(stats, i, i * 33));

        await count(pipe(staticSource(items), presentPacer({
            createSink: () => sink,
            trackSkipRatio: false,
            ...defaults(),
        })));

        expect(stats.presented).toBe(3);
        // Untouched: stays at the sentinel createEmptyPlayerStats seeds (not 0,
        // which is what the EMA would settle to once tracking is on).
        expect(stats.presentSkipRatio).toBe(initialSkipRatio);
    });

    it('audio-master gate: a frame past the audio capture-point paces 1x, not MAX_FPS', async () => {
        const stats = createEmptyPlayerStats();
        // Two frames 1 s apart, with a steady 1 s of excess buffer (1333 - 333):
        // catch-up regime, below the 4 s skip budget. Fixed now so the scheduled
        // delay equals the chosen frame duration.
        async function lastDelay(getAudioCaptureOffsetMs?: () => number | null): Promise<number> {
            const delays: number[] = [];
            const items = [0, 1000].map((c, i) => makeEnvelope(stats, i, c));
            await count(pipe(staticSource(items), presentPacer({
                createSink: () => new MockSink(),
                getBufferSpanMs: (): number => 1333,
                targetSpanMs: 333,
                nowFn: (): number => 0,
                delayFn: (ms: number): Promise<void> => { delays.push(ms); return Promise.resolve(); },
                getAudioCaptureOffsetMs,
            })));
            return delays[delays.length - 1];
        }

        // No audio pairing: video sprints the backlog at MAX_FPS.
        expect(await lastDelay()).toBeCloseTo(1000 / 120, 1);
        // Audio at 500 ms: the second frame (capturedAt 1000) is past audio, so it
        // paces at natural 1x (clamped to MAX_DURATION) instead of sprinting.
        expect(await lastDelay(() => 500)).toBe(1000 / 10);
    });
});
