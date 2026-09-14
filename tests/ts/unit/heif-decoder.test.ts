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

// 240x160 gradients differing in exactly one byte: the EXIF Orientation value, 1 vs 6
function readFixture(name: string): Uint8Array {
    return new Uint8Array(fs.readFileSync(new URL(`fixtures/${name}`, import.meta.url)));
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

let decoder: HeifDecoder;

beforeAll(async () => {
    decoder = await HeifDecoder.load(BASE_URL);
}, 30_000);

describe('HeifDecoder', () => {
    it('should decode a HEIC into real pixels', async () => {
        // arrange
        const bytes = readFixture('orientation1.heic');

        // act
        const image = await decoder.decode(bytes);

        // assert
        expect(sniffImageFormat(bytes)).toBe('heif');
        expect(image).not.toBeNull();
        expect([image!.width, image!.height]).toEqual([240, 160]);
        expect(image!.data.length).toBe(240 * 160 * 4);
        expect(countDistinctColors(image!)).toBeGreaterThan(1000);
    }, 30_000);

    it('should swap the dimensions of an orientation-6 HEIC', async () => {
        // arrange: libheif ignores EXIF Orientation and WebKit applies it, so the decoder has to
        // apply it - otherwise the same photo is upright from Safari and sideways from Chrome
        const upright = readFixture('orientation1.heic');
        const rotated = readFixture('orientation6.heic');
        expect(readHeifOrientation(upright)).toBe(1);
        expect(readHeifOrientation(rotated)).toBe(6);

        // act
        const uprightImage = (await decoder.decode(upright))!;
        const rotatedImage = (await decoder.decode(rotated))!;

        // assert
        expect([uprightImage.width, uprightImage.height]).toEqual([240, 160]);
        expect([rotatedImage.width, rotatedImage.height]).toEqual([160, 240]);
        // orientation 6 is a 90-degree clockwise rotation: the left edge becomes the top edge
        expect(getPixel(rotatedImage, rotatedImage.width - 1, 0)).toEqual(getPixel(uprightImage, 0, 0));
        expect(getPixel(rotatedImage, 0, 0)).toEqual(getPixel(uprightImage, 0, uprightImage.height - 1));
        expect(getPixel(rotatedImage, rotatedImage.width - 1, rotatedImage.height - 1))
            .toEqual(getPixel(uprightImage, uprightImage.width - 1, 0));
    }, 30_000);

    it('should report the stored, pre-orientation dimensions from the header', () => {
        // act: the mobile-encode guard reads these before any decode; ispe never reflects EXIF
        // Orientation, and neither maxPixels nor maxLongSide changes when width and height swap
        const dimensions = readImageDimensions(readFixture('orientation6.heic'), 'heif');

        // assert
        expect(dimensions).toEqual({ width: 240, height: 160 });
    });
});
