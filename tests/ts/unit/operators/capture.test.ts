import { describe, it, expect, vi } from 'vitest';
import { mstpSource } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/capture';
import {
    createEmptyRecordingStats,
    type CapturedFrame,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

// ---- Mocks ---------------------------------------------------------------

class MockVideoFrame {
    closed = false;
    constructor(public id = 0) {}
    close(): void { this.closed = true; }
    codedWidth = 1280;
    codedHeight = 720;
}

interface ReaderProbe {
    cancelled: boolean;
    released: boolean;
}

/** Wraps a stream so getReader() returns a reader whose `releaseLock` flips
 *  `probe.released = true`. Returns the wrapped stream. */
function instrumentRelease(
    readable: ReadableStream<VideoFrame>,
    probe: ReaderProbe,
): ReadableStream<VideoFrame> {
    const origGetReader = readable.getReader.bind(readable) as () => ReadableStreamDefaultReader<VideoFrame>;
    readable.getReader = ((): ReadableStreamDefaultReader<VideoFrame> => {
        const reader = origGetReader();
        const origRelease = reader.releaseLock.bind(reader) as () => void;
        reader.releaseLock = (): void => { probe.released = true; origRelease(); };
        return reader;
    }) as ReadableStream<VideoFrame>['getReader'];
    return readable;
}

interface ProcessorFactoryProbe {
    factory: (track: MediaStreamTrack) => { readable: ReadableStream<VideoFrame> };
    probe: ReaderProbe;
}

interface ProcessorFactoryProbeWithSpy extends ProcessorFactoryProbe {
    cancelSpy: ReturnType<typeof vi.fn>;
}

/** Build a ReadableStream that emits the given frames then closes. The
 *  returned probe records cancel + releaseLock observations. */
function probedFiniteProcessor(frames: MockVideoFrame[]): ProcessorFactoryProbe {
    const probe: ReaderProbe = { cancelled: false, released: false };
    const factory = (_track: MediaStreamTrack): { readable: ReadableStream<VideoFrame> } => {
        let i = 0;
        const readable = new ReadableStream<VideoFrame>({
            pull(controller): void {
                if (i < frames.length) controller.enqueue(frames[i++] as unknown as VideoFrame);
                else controller.close();
            },
            cancel(): void { probe.cancelled = true; },
        });
        return { readable: instrumentRelease(readable, probe) };
    };
    return { factory, probe };
}

/** Build an infinite-emit ReadableStream + probe. */
function probedInfiniteProcessor(): ProcessorFactoryProbeWithSpy {
    const probe: ReaderProbe = { cancelled: false, released: false };
    const cancelSpy = vi.fn((): void => { probe.cancelled = true; });
    let counter = 0;
    const factory = (_track: MediaStreamTrack): { readable: ReadableStream<VideoFrame> } => {
        const readable = new ReadableStream<VideoFrame>({
            pull(controller): void {
                controller.enqueue(new MockVideoFrame(counter++) as unknown as VideoFrame);
            },
            cancel: cancelSpy,
        });
        return { readable: instrumentRelease(readable, probe) };
    };
    return { factory, probe, cancelSpy };
}

function probedIdleProcessor(): ProcessorFactoryProbeWithSpy {
    const probe: ReaderProbe = { cancelled: false, released: false };
    const cancelSpy = vi.fn((): void => { probe.cancelled = true; });
    const factory = (_track: MediaStreamTrack): { readable: ReadableStream<VideoFrame> } => {
        const readable = new ReadableStream<VideoFrame>({
            pull(): void {
                // Leave the read pending until cancellation.
            },
            cancel: cancelSpy,
        });
        return { readable: instrumentRelease(readable, probe) };
    };
    return { factory, probe, cancelSpy };
}

const dummyTrack = {} as MediaStreamTrack;

// ---- Helpers --------------------------------------------------------------

async function drain<T>(seg: AsyncIterable<T>): Promise<T[]> {
    const out: T[] = [];
    for await (const item of seg) out.push(item);
    return out;
}

// ---- Tests ----------------------------------------------------------------

describe('mstpSource', () => {
    it('yields one envelope per source frame (5 in → 5 out)', async () => {
        const frames = Array.from({ length: 5 }, (_, i) => new MockVideoFrame(i));
        const { factory } = probedFiniteProcessor(frames);
        const stats = createEmptyRecordingStats(0);
        const seg = mstpSource({ track: dummyTrack, stats, createProcessor: factory });
        const out = await drain(seg);
        expect(out).toHaveLength(5);
        expect(out.map(e => (e.frame as unknown as MockVideoFrame).id)).toEqual([0, 1, 2, 3, 4]);
    });

    it('seeds placeholder envelope fields for downstream operators', async () => {
        const stats = createEmptyRecordingStats(0);
        const { factory } = probedFiniteProcessor([new MockVideoFrame(0)]);
        const seg = mstpSource({ track: dummyTrack, stats, createProcessor: factory });
        const out = await drain(seg);
        expect(out).toHaveLength(1);
        const env = out[0];
        expect(env.capturedAt).toEqual({ timeMs: 0, epoch: 0 });
        expect(env.index).toBe(0);
        expect(env.sourceWidth).toBe(0);
        expect(env.sourceHeight).toBe(0);
        expect(env.forceKeyframe).toBe(false);
        expect(env.stats).toBe(stats);
    });

    it('increments stats.framesCaptured by exactly 1 per yielded envelope', async () => {
        const stats = createEmptyRecordingStats(0);
        const frames = Array.from({ length: 4 }, (_, i) => new MockVideoFrame(i));
        const { factory } = probedFiniteProcessor(frames);
        const seg = mstpSource({ track: dummyTrack, stats, createProcessor: factory });
        const seen: number[] = [];
        for await (const _ of seg) seen.push(stats.framesCaptured);
        expect(seen).toEqual([1, 2, 3, 4]);
        expect(stats.framesCaptured).toBe(4);
    });

    it('exclusive: a second concurrent iteration throws', async () => {
        const stats = createEmptyRecordingStats(0);
        const { factory } = probedInfiniteProcessor();
        const seg = mstpSource({ track: dummyTrack, stats, createProcessor: factory });
        const iter1 = seg[Symbol.asyncIterator]();
        await iter1.next();
        const iter2 = seg[Symbol.asyncIterator]();
        await expect(iter2.next()).rejects.toThrow(/already running/);
        await iter1.return?.();
    });

    it('finalize: reader is released when iteration ends naturally', async () => {
        // Natural end (`controller.close()`) doesn't trip the cancel callback —
        // the stream is already in the "closed" state when our finalize calls
        // cancel(), so it's a no-op. The reader IS released though.
        const stats = createEmptyRecordingStats(0);
        const { factory, probe } = probedFiniteProcessor([new MockVideoFrame(0), new MockVideoFrame(1)]);
        const seg = mstpSource({ track: dummyTrack, stats, createProcessor: factory });
        await drain(seg);
        expect(probe.released).toBe(true);
    });

    it('finalize: reader is released when abort() fires', async () => {
        const stats = createEmptyRecordingStats(0);
        const { factory, probe } = probedInfiniteProcessor();
        const ac = new AbortController();
        const seg = mstpSource({ track: dummyTrack, stats, abortSignal: ac.signal, createProcessor: factory });
        const runPromise = drain(seg);
        // abort() is independent — the source's `finally` must run.
        ac.abort();
        await runPromise;
        expect(probe.cancelled).toBe(true);
        expect(probe.released).toBe(true);
    });

    it('finalize: abort() cancels an idle pending read', async () => {
        const stats = createEmptyRecordingStats(0);
        const { factory, probe, cancelSpy } = probedIdleProcessor();
        const ac = new AbortController();
        const seg = mstpSource({ track: dummyTrack, stats, abortSignal: ac.signal, createProcessor: factory });
        const runPromise = drain(seg);
        await Promise.resolve();
        ac.abort();
        await runPromise;
        expect(cancelSpy).toHaveBeenCalledTimes(1);
        expect(probe.cancelled).toBe(true);
        expect(probe.released).toBe(true);
        expect(stats.framesCaptured).toBe(0);
    });

    it('finalize: reader is released when an error propagates downstream', async () => {
        const stats = createEmptyRecordingStats(0);
        const { factory, probe } = probedInfiniteProcessor();
        const seg = mstpSource({ track: dummyTrack, stats, createProcessor: factory });
        const errorPromise = (async (): Promise<void> => {
            for await (const _ of seg) {
                throw new Error('downstream-boom');
            }
        })();
        await expect(errorPromise).rejects.toThrow('downstream-boom');
        expect(probe.cancelled).toBe(true);
        expect(probe.released).toBe(true);
    });

    it('finalize: cancel spy is called after consumer break via iter.return()', async () => {
        const stats = createEmptyRecordingStats(0);
        const { factory, probe, cancelSpy } = probedInfiniteProcessor();
        const seg = mstpSource({ track: dummyTrack, stats, createProcessor: factory });
        const iter = seg[Symbol.asyncIterator]();
        await iter.next();
        await iter.return?.();
        expect(cancelSpy).toHaveBeenCalledTimes(1);
        expect(probe.cancelled).toBe(true);
        expect(probe.released).toBe(true);
    });

    it('uses default MediaStreamTrackProcessor when createProcessor is omitted', async () => {
        const stats = createEmptyRecordingStats(0);
        const g = globalThis as unknown as { MediaStreamTrackProcessor?: unknown };
        const constructed: { track: MediaStreamTrack }[] = [];
        const original = g.MediaStreamTrackProcessor;
        g.MediaStreamTrackProcessor = class {
            readable: ReadableStream<VideoFrame>;
            constructor(init: { track: MediaStreamTrack }) {
                constructed.push(init);
                this.readable = new ReadableStream<VideoFrame>({
                    pull(controller) { controller.close(); },
                });
            }
        };
        try {
            const seg = mstpSource({ track: dummyTrack, stats });
            const out = await drain(seg);
            expect(out).toEqual([]);
            expect(constructed).toHaveLength(1);
            expect(constructed[0].track).toBe(dummyTrack);
        } finally {
            if (original === undefined) delete g.MediaStreamTrackProcessor;
            else g.MediaStreamTrackProcessor = original;
        }
    });

    it('does not yield when signal is already aborted before any frame is read', async () => {
        const stats = createEmptyRecordingStats(0);
        const { factory } = probedFiniteProcessor([new MockVideoFrame(0), new MockVideoFrame(1)]);
        const ac = new AbortController();
        ac.abort();
        const seg = mstpSource({ track: dummyTrack, stats, abortSignal: ac.signal, createProcessor: factory });
        const out: CapturedFrame[] = [];
        for await (const env of seg) out.push(env);
        expect(out).toEqual([]);
        expect(stats.framesCaptured).toBe(0);
    });
});
