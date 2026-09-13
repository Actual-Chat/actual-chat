import { describe, it, expect, beforeAll } from 'vitest';
import { JpegliEncoder } from 'image-processing/jpegli-encoder';
import type { tryEncodeWithRebuild as TryEncodeWithRebuild } from 'image-processing/image-processor-worker';

const BASE_URL = new URL('../../../src/nodejs/jpegli', import.meta.url).href;

function createGradient(width: number, height: number): Uint8ClampedArray {
    const rgba = new Uint8ClampedArray(width * height * 4);
    for (let i = 0; i < rgba.length; i += 4) {
        rgba[i] = (i / 4) % 256;
        rgba[i + 1] = 128;
        rgba[i + 2] = 255 - ((i / 4) % 256);
        rgba[i + 3] = 255;
    }
    return rgba;
}

function readJpegSize(jpeg: Uint8Array): { width: number, height: number } {
    let offset = 2;
    while (offset + 9 < jpeg.length) {
        const marker = jpeg[offset + 1];
        if (marker === 0xC0 || marker === 0xC2)
            return {
                height: (jpeg[offset + 5] << 8) | jpeg[offset + 6],
                width: (jpeg[offset + 7] << 8) | jpeg[offset + 8],
            };

        offset += 2 + ((jpeg[offset + 2] << 8) | jpeg[offset + 3]);
    }
    throw new Error('No SOF marker');
}

describe('JpegliEncoder', () => {
    for (const variant of ['simd', 'scalar'] as const) {
        it(`should encode RGBA pixels into a JPEG (${variant})`, async () => {
            const encoder = await JpegliEncoder.load(BASE_URL, variant);

            const jpeg = encoder.encode(
                createGradient(64, 48), 64, 48, { distance: 1.9, subsampling: 420, progressive: 2 });

            expect(encoder.variant).toBe(variant);
            expect(Array.from(jpeg.subarray(0, 2))).toEqual([0xFF, 0xD8]);
            expect(Array.from(jpeg.subarray(jpeg.length - 2))).toEqual([0xFF, 0xD9]);
            expect(readJpegSize(jpeg)).toEqual({ width: 64, height: 48 });
        });
    }

    it('should reuse its input buffer across images of different sizes', async () => {
        const encoder = await JpegliEncoder.load(BASE_URL, 'scalar');

        const large = encoder.encode(
            createGradient(128, 96), 128, 96, { distance: 1.9, subsampling: 420, progressive: 2 });
        const small = encoder.encode(
            createGradient(16, 16), 16, 16, { distance: 1.9, subsampling: 420, progressive: 0 });

        expect(readJpegSize(large)).toEqual({ width: 128, height: 96 });
        expect(readJpegSize(small)).toEqual({ width: 16, height: 16 });
    });

    it('should reject pixel data shorter than width * height * 4', async () => {
        const encoder = await JpegliEncoder.load(BASE_URL, 'scalar');

        expect(() => encoder.encode(
            new Uint8ClampedArray(10), 64, 48, { distance: 1.9, subsampling: 420, progressive: 2 }))
            .toThrow(/shorter/);
    });
});

describe('tryEncodeWithRebuild', () => {
    let tryEncodeWithRebuild: typeof TryEncodeWithRebuild;

    beforeAll(async () => {
        // image-processor-worker runs as a module worker in production, where `self` is the
        // worker's own global scope; stand one in so its top-level rpcServer(...) call can bind to it.
        (globalThis as unknown as { self?: unknown }).self ??= globalThis;
        ({ tryEncodeWithRebuild } = await import('image-processing/image-processor-worker'));
    });

    it('should drop a poisoned encoder so the next call rebuilds it', async () => {
        // arrange
        const loads: number[] = [];
        const encoder = {
            encode: () => { throw new WebAssembly.RuntimeError('memory access out of bounds'); },
        };
        const factory = () => { loads.push(1); return Promise.resolve(encoder as never); };

        // act
        const first = await tryEncodeWithRebuild(factory, () => encoder.encode());
        const second = await tryEncodeWithRebuild(factory, () => encoder.encode());

        // assert: each attempt loaded a fresh encoder rather than reusing the trapped one
        expect(first).toBeNull();
        expect(second).toBeNull();
        expect(loads.length).toBe(2);
    });
});
