import { describe, expect, it, vi } from 'vitest';
import { PromiseSource } from 'actuallab-core';
import { filterSupportedVideoFrames, getVideoCodecCategory } from 'api/video-codec';
import type { VideoFrameDto } from 'api/streaming-api';

type Frame = Pick<VideoFrameDto, 'Codec' | 'Index' | 'KeyFrameIndex' | 'LayerId'>;

async function* source(frames: Frame[]): AsyncGenerator<Frame> {
    await Promise.resolve();
    yield* frames;
}

async function collect(frames: AsyncIterable<Frame>): Promise<Frame[]> {
    const result: Frame[] = [];
    for await (const frame of frames)
        result.push(frame);
    return result;
}

describe('video codec admission', () => {
    it.each([
        ['avc1.42E01F', 'h264'], ['AVC3.42E01F', 'h264'], ['H264', 'h264'],
        ['hev1.1.6.L120.B0', 'hevc'], ['hvc1', 'hevc'], ['HEVC', 'hevc'],
        ['vp09.00.41.08', 'vp9'], ['VP9', 'vp9'],
        ['av01.0.08M.08', 'av1'], ['AV1', 'av1'],
        ['unknown', null], ['', null], [undefined, null], [null, null],
    ])('should classify %s as %s', (codec, category) => {
        expect(getVideoCodecCategory(codec)).toBe(category);
    });

    it('should gate each layer until a supported keyframe', async () => {
        const frames: Frame[] = [
            { Index: 0, KeyFrameIndex: 0, Codec: 'hevc' },
            { Index: 1, KeyFrameIndex: 0 },
            { Index: 2, KeyFrameIndex: 2, Codec: 'vp09.00.41.08' },
            { Index: 3, KeyFrameIndex: 2 },
            { Index: 4, KeyFrameIndex: 2, LayerId: 1 },
            { Index: 5, KeyFrameIndex: 5, LayerId: 1, Codec: 'vp9' },
            { Index: 6, KeyFrameIndex: 6, Codec: 'hevc' },
            { Index: 7, KeyFrameIndex: 6 },
        ];

        const received = await collect(filterSupportedVideoFrames(source(frames), ['vp9']));

        expect(received).toEqual([frames[2], frames[3], frames[5]]);
    });

    it('should not treat unknown or missing keyframe codecs as H264', async () => {
        const frames: Frame[] = [
            { Index: 0, KeyFrameIndex: 0, Codec: 'unknown' },
            { Index: 1, KeyFrameIndex: 1 },
            { Index: 2, KeyFrameIndex: 2, Codec: 'avc3.42E01F' },
        ];

        expect(await collect(filterSupportedVideoFrames(source(frames), ['h264']))).toEqual([frames[2]]);
    });

    it('should close the underlying pull while waiting for an admitted frame', async () => {
        const pending = new PromiseSource<IteratorResult<Frame>>();
        const close = vi.fn(() => {
            const done = { done: true as const, value: undefined };
            pending.resolve(done);
            return Promise.resolve(done);
        });
        const frames: AsyncIterable<Frame> = {
            [Symbol.asyncIterator]: () => ({ next: () => pending.promise, return: close }),
        };
        const iterator = filterSupportedVideoFrames(frames, ['vp9'])[Symbol.asyncIterator]();
        const next = iterator.next();

        await iterator.return!();

        expect(close).toHaveBeenCalledOnce();
        expect((await next).done).toBe(true);
    });
});
