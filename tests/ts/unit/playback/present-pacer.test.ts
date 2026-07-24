import { describe, it, expect } from 'vitest';
import { count, pipe } from 'ix-ext';
import {
    presentPacer,
    PRESENT_LEAD_MS,
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
    codedWidth = 320;
    codedHeight = 180;
    displayWidth = 320;
    displayHeight = 180;
    constructor(public id = 0) {}
    close(): void { this.closed = true; }
}

type SinkMode = 'ok' | 'fail' | 'throw';

class MockSink implements PresentSink {
    public presented: VideoFrame[] = [];
    public disposed = false;
    constructor(public mode: SinkMode = 'ok') {}
    present(frame: VideoFrame): Promise<boolean> {
        if (this.mode === 'throw')
            return Promise.reject(new Error('sink boom'));

        if (this.mode === 'fail')
            return Promise.resolve(false);

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
        serverArrivedAtUnixMs: 0,
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
        for (const item of items)
            yield item;
    })();
}

interface PacerDefaults {
    getBufferSpanMs: () => number;
    targetSpanMs: number;
    nowFn: () => number;
    delayFn: () => Promise<void>;
}

// nowFn auto-advances 1 s per call so natural-delta pacing never sleeps.
function defaults(): PacerDefaults {
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

    it('audio-master gate: a frame past the audio capture-point paces 1x, not catch-up', async () => {
        const stats = createEmptyPlayerStats();
        // Two 30fps frames with a steady 1 s of excess buffer (1333 - 333):
        // catch-up regime, below the 4 s skip budget. Fixed now so the scheduled
        // delay equals the chosen frame duration.
        async function lastDelay(getAudioCaptureOffsetMs?: () => number | null): Promise<number> {
            const delays: number[] = [];
            const items = [0, 33].map((c, i) => makeEnvelope(stats, i, c));
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

        // No audio pairing: 1s of excess drains at the max 2x overspeed —
        // natural 33ms compresses to 16.5ms, evenly spaced (no burst).
        expect(await lastDelay()).toBeCloseTo(33 / 2 - PRESENT_LEAD_MS, 1);
        // Audio at capture-point 0: the second frame (capturedAt 33) is past
        // audio, so it paces at natural 1x instead of catching up.
        expect(await lastDelay(() => 0)).toBeCloseTo(33 - PRESENT_LEAD_MS, 1);
    });

    it('tracks presentState through a successful present', async () => {
        const stats = createEmptyPlayerStats();
        const sink = new MockSink();
        const items = [makeEnvelope(stats, 0, 0)];

        await count(pipe(staticSource(items), presentPacer({ createSink: () => sink, ...defaults() })));

        expect(stats.presentState).toBe('presented');
        expect(stats.lastPresentAtMs).toBeGreaterThan(0);
        expect(stats.lastPresentAttemptAtMs).toBeGreaterThan(0);
    });

    it('catch-up is an evenly spaced bounded overspeed, ramping with backlog', async () => {
        // arrange: 30fps frames; scheduled delay per frame reveals the chosen pace.
        async function delayFor(extraMs: number): Promise<number> {
            const stats = createEmptyPlayerStats();
            const delays: number[] = [];
            const items = [0, 33].map((c, i) => makeEnvelope(stats, i, c));
            await count(pipe(staticSource(items), presentPacer({
                createSink: () => new MockSink(),
                getBufferSpanMs: (): number => 333 + extraMs,
                targetSpanMs: 333,
                nowFn: (): number => 0,
                delayFn: (ms: number): Promise<void> => { delays.push(ms); return Promise.resolve(); },
            })));
            return delays[delays.length - 1] + PRESENT_LEAD_MS;
        }

        // act + assert: below one source frame of excess → natural 1x pace.
        expect(await delayFor(20)).toBeCloseTo(33, 1);
        // 100ms excess → rate 1.2 → ~27.5ms spacing, far from a MAX_FPS burst.
        expect(await delayFor(100)).toBeCloseTo(33 / 1.2, 1);
        // Deep backlog clamps at 2x — never collapses to back-to-back writes.
        expect(await delayFor(3000)).toBeCloseTo(33 / 2, 1);
    });
});
