import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
    pullSource,
    type VideoFrameDto,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/pull';
import {
    createEmptyPlayerStats,
    type ArrivedChunk,
    type PlayerStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
import { MonotonicClock } from 'clocks';

// ---- Mock WebCodecs surface ----------------------------------------------

class MockEncodedVideoChunk {
    type: 'key' | 'delta';
    timestamp: number;
    byteLength: number;
    duration: number | null;
    private readonly bytes: Uint8Array;
    constructor(init: { type: 'key' | 'delta'; timestamp: number; duration?: number; data: Uint8Array }) {
        this.type = init.type;
        this.timestamp = init.timestamp;
        const bytes = init.data;
        this.bytes = bytes;
        this.byteLength = bytes.byteLength;
        this.duration = init.duration ?? null;
    }
    copyTo(buffer: ArrayBuffer): void {
        new Uint8Array(buffer).set(this.bytes);
    }
}

interface GlobalWithChunk {
    EncodedVideoChunk?: typeof MockEncodedVideoChunk;
}

beforeEach(() => {
    (globalThis as unknown as GlobalWithChunk).EncodedVideoChunk = MockEncodedVideoChunk;
});

afterEach(() => {
    delete (globalThis as unknown as GlobalWithChunk).EncodedVideoChunk;
});

// ---- Helpers --------------------------------------------------------------

function makeDto(opts: Partial<VideoFrameDto> & { offsetTicks?: number; IsKeyFrame?: boolean }): VideoFrameDto {
    const data = opts.Data ?? new Uint8Array([1, 2, 3, 4]);
    // IsKeyFrame is not on the wire — it's derived as KeyFrameIndex === Index.
    // For test convenience, accept an IsKeyFrame boolean and synthesise
    // consistent KeyFrameIndex/Index values.
    const isKey = opts.IsKeyFrame ?? false;
    const index = opts.Index ?? 0;
    const keyFrameIndex = opts.KeyFrameIndex ?? (isKey ? index : index - 1);
    return {
        Data: data,
        // Default to 10 ticks per ms (TimeSpan ticks = 100ns; 10000 = 1ms)
        Offset: opts.Offset ?? (opts.offsetTicks ?? 10000),
        OffsetEpoch: opts.OffsetEpoch,
        Duration: opts.Duration ?? 0,
        KeyFrameIndex: keyFrameIndex,
        Index: index,
        Width: opts.Width,
        Height: opts.Height,
        Description: opts.Description,
        Codec: opts.Codec,
        LayerId: opts.LayerId,
    };
}

async function* fromArray(items: VideoFrameDto[]): AsyncIterable<VideoFrameDto> {
    for (const item of items) {
        await Promise.resolve();
        yield item;
    }
}

interface ProbedIterable {
    iterable: AsyncIterable<VideoFrameDto>;
    returned: boolean;
}

/** Wraps an async generator so we can observe whether `return()` was called. */
function probedIterable(items: VideoFrameDto[], infinite = false): ProbedIterable {
    const probe: ProbedIterable = {
        returned: false,
        iterable: undefined as unknown as AsyncIterable<VideoFrameDto>,
    };
    let i = 0;
    probe.iterable = {
        [Symbol.asyncIterator](): AsyncIterator<VideoFrameDto> {
            return {
                async next(): Promise<IteratorResult<VideoFrameDto>> {
                    await Promise.resolve();
                    if (infinite) {
                        const item = items[i % items.length];
                        i++;
                        return { value: item, done: false };
                    }
                    if (i < items.length) {
                        return { value: items[i++], done: false };
                    }
                    return { value: undefined as unknown as VideoFrameDto, done: true };
                },
                return(): Promise<IteratorResult<VideoFrameDto>> {
                    probe.returned = true;
                    return Promise.resolve({ value: undefined as unknown as VideoFrameDto, done: true });
                },
            };
        },
    };
    return probe;
}

async function drain<T>(seg: AsyncIterable<T>): Promise<T[]> {
    const out: T[] = [];
    for await (const item of seg) out.push(item);
    return out;
}

function makeStats(): PlayerStats {
    return createEmptyPlayerStats();
}

// ---- Tests ----------------------------------------------------------------

describe('pullSource', () => {
    it('5 dtos in → 5 ArrivedChunks out, with capturedAt/arrivedAt/layerId fields', async () => {
        const stats = makeStats();
        const dtos = [
            makeDto({ IsKeyFrame: true,  Offset: 0,         LayerId: 0 }),
            makeDto({ IsKeyFrame: false, Offset: 330_000,   LayerId: 1 }), // 33 ms
            makeDto({ IsKeyFrame: false, Offset: 660_000,   LayerId: 0 }), // 66 ms
            makeDto({ IsKeyFrame: false, Offset: 990_000,   LayerId: 0 }), // 99 ms
            makeDto({ IsKeyFrame: false, Offset: 1_320_000, LayerId: 1 }), // 132 ms
        ];

        // Mock arrival clock with predictable times.
        let tick = 0;
        const clock = {
            now: () => ({ timeMs: 1_700_000_000_000 + (tick++ * 33), epoch: 5 }),
            epoch: 5,
        } as unknown as MonotonicClock;

        const seg = pullSource({
            streamId: 's1',
            getStream: () => fromArray(dtos),
            arrivalClock: clock,
            stats,
        });
        const out: ArrivedChunk[] = await drain(seg);

        expect(out).toHaveLength(5);

        expect(out[0].capturedAt).toEqual({ timeMs: 0, epoch: 0 });
        expect(out[1].capturedAt).toEqual({ timeMs: 33, epoch: 0 });
        expect(out[2].capturedAt).toEqual({ timeMs: 66, epoch: 0 });
        expect(out[3].capturedAt).toEqual({ timeMs: 99, epoch: 0 });
        expect(out[4].capturedAt).toEqual({ timeMs: 132, epoch: 0 });

        expect(out[0].layerId).toBe(0);
        expect(out[1].layerId).toBe(1);
        expect(out[2].layerId).toBe(0);

        // arrivedAt timeMs is monotone (because of mock clock).
        const arrivalTimes = out.map(o => o.arrivedAt.timeMs);
        for (let i = 1; i < arrivalTimes.length; i++)
            expect(arrivalTimes[i]).toBeGreaterThan(arrivalTimes[i - 1]);
        expect(out[0].arrivedAt.epoch).toBe(5);

        expect(out[0].isKeyFrame).toBe(true);
        expect(out[1].isKeyFrame).toBe(false);

    });

    it('OffsetEpoch missing → 0', async () => {
        const stats = makeStats();
        const dtos = [
            makeDto({ IsKeyFrame: true, Offset: 0 }),
            // OffsetEpoch undefined ↓
            makeDto({ IsKeyFrame: false, Offset: 100_000 /* 10 ms */ }),
            // explicit OffsetEpoch=2
            makeDto({ IsKeyFrame: false, Offset: 200_000, OffsetEpoch: 2 }),
        ];
        const seg = pullSource({
            streamId: 's1',
            getStream: () => fromArray(dtos),
            stats,
        });
        const out = await drain(seg);

        expect(out[0].capturedAt.epoch).toBe(0);
        expect(out[1].capturedAt.epoch).toBe(0);
        expect(out[2].capturedAt.epoch).toBe(2);
    });

    it('Keyframe sets isKeyFrame and chunk.type correctly', async () => {
        const stats = makeStats();
        const dtos = [
            makeDto({ IsKeyFrame: true, Offset: 0, Width: 1280, Height: 720 }),
            makeDto({ IsKeyFrame: false, Offset: 330_000, Width: 1280, Height: 720 }),
        ];
        const seg = pullSource({
            streamId: 's1',
            getStream: () => fromArray(dtos),
            stats,
        });
        const out = await drain(seg);

        expect(out[0].isKeyFrame).toBe(true);
        expect((out[0].chunk as unknown as MockEncodedVideoChunk).type).toBe('key');
        expect((out[0].chunk as unknown as MockEncodedVideoChunk).timestamp).toBe(0);
        expect(out[1].isKeyFrame).toBe(false);
        expect((out[1].chunk as unknown as MockEncodedVideoChunk).type).toBe('delta');
        expect((out[1].chunk as unknown as MockEncodedVideoChunk).timestamp).toBe(33_000);

        // Width/height/raw byte length lifted.
        expect(out[0].width).toBe(1280);
        expect(out[0].height).toBe(720);
        expect(out[0].rawByteLength).toBe(dtos[0].Data.byteLength);
    });

    it('maps TimeSpan offsets and durations to WebCodecs microseconds', async () => {
        const stats = makeStats();
        const dtos = [
            makeDto({ IsKeyFrame: true, Offset: 1_230, Duration: 330_000 }),
        ];
        const seg = pullSource({
            streamId: 's1',
            getStream: () => fromArray(dtos),
            stats,
        });
        const out = await drain(seg);
        const chunk = out[0].chunk as unknown as MockEncodedVideoChunk;
        expect(chunk.timestamp).toBe(123);
        expect(chunk.duration).toBe(33_000);
    });

    it('exclusive: a second concurrent iteration throws', async () => {
        const stats = makeStats();
        const probe = probedIterable([makeDto({ IsKeyFrame: true, Offset: 0 })], /* infinite */ true);
        const seg = pullSource({
            streamId: 's1',
            getStream: () => probe.iterable,
            stats,
        });
        const iter1 = seg[Symbol.asyncIterator]();
        await iter1.next();
        const iter2 = seg[Symbol.asyncIterator]();
        await expect(iter2.next()).rejects.toThrow(/already running/);
        await iter1.return?.();
    });

    it('finalize: source iterator returned() when consumer aborts the signal', async () => {
        const stats = makeStats();
        const ac = new AbortController();
        const probe = probedIterable(
            [makeDto({ IsKeyFrame: true, Offset: 0 })],
            /* infinite */ true,
        );
        const seg = pullSource({
            streamId: 's1',
            getStream: () => probe.iterable,
            stats,
            abortSignal: ac.signal,
        });
        const seen: ArrivedChunk[] = [];
        const driver = (async (): Promise<void> => {
            for await (const item of seg) {
                seen.push(item);
                if (ac.signal.aborted) break;
            }
        })();
        await Promise.resolve(); await Promise.resolve();
        ac.abort();
        await driver;
        expect(probe.returned).toBe(true);
    });

    it('getStream returning a Promise<AsyncIterable> is awaited', async () => {
        const stats = makeStats();
        const dtos = [makeDto({ IsKeyFrame: true, Offset: 0 })];
        const seg = pullSource({
            streamId: 's1',
            getStream: () => Promise.resolve(fromArray(dtos)),
            stats,
        });
        const out = await drain(seg);
        expect(out).toHaveLength(1);
    });

    it('description bytes are lifted into ArrayBuffer', async () => {
        const stats = makeStats();
        const desc = new Uint8Array([0x01, 0x42, 0xE0, 0x1E]);
        const dtos = [
            makeDto({ IsKeyFrame: true, Offset: 0, Description: desc }),
        ];
        const seg = pullSource({
            streamId: 's1',
            getStream: () => fromArray(dtos),
            stats,
        });
        const out = await drain(seg);
        expect(out[0].description).toBeInstanceOf(ArrayBuffer);
        expect(new Uint8Array(out[0].description!)).toEqual(desc);
    });

    it('Offset given as bigint (large values) is converted correctly', async () => {
        const stats = makeStats();
        // 1 hour = 3_600_000 ms = 36_000_000_000 ticks.
        const oneHourTicks = BigInt(36_000_000_000);
        const dtos = [
            makeDto({ IsKeyFrame: true, Offset: oneHourTicks }),
        ];
        const seg = pullSource({
            streamId: 's1',
            getStream: () => fromArray(dtos),
            stats,
        });
        const out = await drain(seg);
        expect(out[0].capturedAt.timeMs).toBe(3_600_000);
    });
});

// Silence unused-var warnings if any
void vi;
