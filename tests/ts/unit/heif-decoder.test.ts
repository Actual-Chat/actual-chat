import fs from 'node:fs';
import { beforeAll, describe, expect, it, vi } from 'vitest';
import { HeifDecoder } from 'image-processing/heif-decoder';
import type { RgbaImage } from 'image-processing/heif-decoder';
import { readHeifOrientation, readImageDimensions, sniffImageFormat } from 'image-processing/image-format';

const BASE_URL = new URL('../../../src/nodejs/libheif', import.meta.url).href;

// HeifDecoder fetches libheif.wasm; node's fetch refuses file: URLs, so it reads from disk here
vi.stubGlobal('fetch', (url: string) => {
    const bytes = fs.readFileSync(new URL(url));
    return Promise.resolve({
        ok: true,
        arrayBuffer: () => Promise.resolve(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength)),
    });
});

function readFixture(name: string): Uint8Array {
    return new Uint8Array(fs.readFileSync(new URL(`fixtures/${name}`, import.meta.url)));
}

// 240x160 gradients; orientation1 and orientation6 differ in exactly one byte, the EXIF value
const UPRIGHT = readFixture('orientation1.heic');
const ROTATED = readFixture('orientation6.heic');
const ORIENTATION_OFFSET = findSingleDifference(UPRIGHT, ROTATED);

function findSingleDifference(a: Uint8Array, b: Uint8Array): number {
    const offsets = [...a.keys()].filter(i => a[i] !== b[i]);
    if (a.length !== b.length || offsets.length !== 1)
        throw new Error(`The fixtures must differ in exactly one byte, found ${offsets.length}.`);

    return offsets[0];
}

function withOrientation(orientation: number): Uint8Array {
    const bytes = new Uint8Array(UPRIGHT);
    bytes[ORIENTATION_OFFSET] = orientation;
    return bytes;
}

/** Where EXIF says a source pixel belongs, written out per the spec rather than as the decoder's
 *  base/step arithmetic, so a mistake in that arithmetic shows up here. */
function mapExifPoint(orientation: number, x: number, y: number, width: number, height: number): number[] {
    switch (orientation) {
    case 2:
        return [width - 1 - x, y];
    case 3:
        return [width - 1 - x, height - 1 - y];
    case 4:
        return [x, height - 1 - y];
    case 5:
        return [y, x];
    case 6:
        return [height - 1 - y, x];
    case 7:
        return [height - 1 - y, width - 1 - x];
    case 8:
        return [y, width - 1 - x];
    default:
        return [x, y];
    }
}

function getPixel(image: RgbaImage, x: number, y: number): number[] {
    const i = (y * image.width + x) * 4;
    return [image.data[i], image.data[i + 1], image.data[i + 2]];
}

function countDistinctColors(image: RgbaImage): number {
    const colors = new Set<number>();
    for (let i = 0; i < image.data.length; i += 4)
        colors.add((image.data[i] << 16) | (image.data[i + 1] << 8) | image.data[i + 2]);
    return colors.size;
}

/** Pads with a 'free' box, which every ISO base media parser skips - the bytes are still copied
 *  into the wasm heap, which is what makes a retained heif_context measurable. */
function padWithFreeBox(bytes: Uint8Array, padLength: number): Uint8Array {
    const result = new Uint8Array(bytes.length + padLength);
    result.set(bytes);
    new DataView(result.buffer).setUint32(bytes.length, padLength);
    result.set([0x66, 0x72, 0x65, 0x65], bytes.length + 4);
    return result;
}

let decoder: HeifDecoder;

beforeAll(async () => {
    decoder = await HeifDecoder.load(BASE_URL);
}, 30_000);

describe('HeifDecoder', () => {
    it('should decode a HEIC into real pixels', async () => {
        // act
        const image = await decoder.decode(UPRIGHT);

        // assert
        expect(sniffImageFormat(UPRIGHT)).toBe('heif');
        expect(image).not.toBeNull();
        expect([image!.width, image!.height]).toEqual([240, 160]);
        expect(image!.data.length).toBe(240 * 160 * 4);
        expect(countDistinctColors(image!)).toBeGreaterThan(1000);
    }, 30_000);

    it('should swap the dimensions of an orientation-6 HEIC', async () => {
        // arrange: libheif ignores EXIF Orientation and WebKit applies it, so the decoder has to
        // apply it - otherwise the same photo is upright from Safari and sideways from Chrome
        expect(readHeifOrientation(UPRIGHT)).toBe(1);
        expect(readHeifOrientation(ROTATED)).toBe(6);

        // act
        const uprightImage = (await decoder.decode(UPRIGHT))!;
        const rotatedImage = (await decoder.decode(ROTATED))!;

        // assert
        expect([uprightImage.width, uprightImage.height]).toEqual([240, 160]);
        expect([rotatedImage.width, rotatedImage.height]).toEqual([160, 240]);
    }, 30_000);

    it.each([2, 3, 4, 5, 6, 7, 8])('should place every corner right for orientation %i', async orientation => {
        // arrange
        const isTransposed = orientation >= 5;

        // act
        const upright = (await decoder.decode(UPRIGHT))!;
        const image = (await decoder.decode(withOrientation(orientation)))!;

        // assert
        expect([image.width, image.height])
            .toEqual(isTransposed ? [upright.height, upright.width] : [upright.width, upright.height]);
        const corners = [[0, 0], [upright.width - 1, 0], [0, upright.height - 1],
            [upright.width - 1, upright.height - 1]];
        for (const [x, y] of corners) {
            const [dx, dy] = mapExifPoint(orientation, x, y, upright.width, upright.height);
            expect(getPixel(image, dx, dy), `corner ${x},${y} of orientation ${orientation}`)
                .toEqual(getPixel(upright, x, y));
        }
    }, 30_000);

    it('should leave a file carrying irot to libheif', async () => {
        // arrange: every camera HEIC carries irot as well as EXIF, and libheif applies irot - so
        // applying the EXIF tag on top would rotate such a photo twice
        const bytes = readFixture('orientation6-irot.heic');
        expect(readImageDimensions(bytes, 'heif')).toEqual({ width: 240, height: 160 });
        expect(readHeifOrientation(bytes)).toBe(1);

        // act
        const image = (await decoder.decode(bytes))!;

        // assert: 160x240 is libheif's own post-irot result; 240x160 would mean a second rotation
        expect([image.width, image.height]).toEqual([160, 240]);
        expect(countDistinctColors(image)).toBeGreaterThan(1000);
    }, 30_000);

    it('should not retain a libheif context between decodes', async () => {
        // arrange: the glue frees the previous context on each decode but never the last one, and
        // the wasm heap never shrinks - so a retained context is permanent for the worker's life
        const bytes = padWithFreeBox(UPRIGHT, 4 * 1024 * 1024);
        await decoder.decode(bytes);

        // act
        const sizes: number[] = [];
        for (let i = 0; i < 8; i++) {
            await decoder.decode(bytes);
            sizes.push(decoder.heapByteLength);
        }

        // assert: the heap pins the per-decode leak, hasContext the last one, which reuse alone
        // leaves alive for the worker's life without ever growing the heap
        expect(new Set(sizes).size).toBe(1);
        expect(decoder.hasContext).toBe(false);
    }, 60_000);

    it('should report the stored, pre-orientation dimensions from the header', () => {
        // act: the mobile-decode guard reads these before any decode; ispe never reflects EXIF
        // Orientation, and neither maxPixels nor maxLongSide changes when width and height swap
        const dimensions = readImageDimensions(ROTATED, 'heif');

        // assert
        expect(dimensions).toEqual({ width: 240, height: 160 });
    });
});
