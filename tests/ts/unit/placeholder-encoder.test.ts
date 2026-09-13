import { describe, expect, it, vi } from 'vitest';
import {
    encodePlaceholder,
    PLACEHOLDER_DISTANCE,
    PLACEHOLDER_FORMAT_STRIPPED,
    PLACEHOLDER_LONG_SIDE,
    PLACEHOLDER_PREFIX,
} from 'image-processing/placeholder-encoder';
import { JpegliEncoder } from 'image-processing/jpegli-encoder';

const BASE_URL = new URL('../../../src/nodejs/jpegli', import.meta.url).href;

// vitest's node environment has neither OffscreenCanvas nor ImageBitmap, and jpegli needs a real
// RGBA buffer to encode, so these doubles do an actual area-average resize rather than no-op stubs.
class TestBitmap {
    constructor(
        public readonly width: number,
        public readonly height: number,
        public readonly rgba: Uint8ClampedArray,
    ) {}
    close(): void { /* no-op */ }
}

class TestCanvasContext2D {
    imageSmoothingQuality = 'low';
    private data = new Uint8ClampedArray(0);

    // Area-averages source pixels per destination pixel: a real high-quality canvas resize blurs
    // away fine noise the same way, and createPhotoBitmap's size assertion relies on that.
    drawImage(image: TestBitmap, _dx: number, _dy: number, dw: number, dh: number): void {
        const dst = new Uint8ClampedArray(dw * dh * 4);
        const scaleX = image.width / dw;
        const scaleY = image.height / dh;
        for (let y = 0; y < dh; y++) {
            const srcY0 = Math.floor(y * scaleY);
            const srcY1 = Math.max(srcY0 + 1, Math.floor((y + 1) * scaleY));
            for (let x = 0; x < dw; x++) {
                const srcX0 = Math.floor(x * scaleX);
                const srcX1 = Math.max(srcX0 + 1, Math.floor((x + 1) * scaleX));
                const dstIndex = (y * dw + x) * 4;
                this.averageBlock(image, srcX0, srcX1, srcY0, srcY1, dst, dstIndex);
            }
        }
        this.data = dst;
    }

    getImageData(_x: number, _y: number, w: number, h: number): ImageData {
        return { data: this.data, width: w, height: h, colorSpace: 'srgb' } as ImageData;
    }

    // Private methods

    private averageBlock(
        image: TestBitmap,
        srcX0: number,
        srcX1: number,
        srcY0: number,
        srcY1: number,
        dst: Uint8ClampedArray,
        dstIndex: number,
    ): void {
        let r = 0;
        let g = 0;
        let b = 0;
        let a = 0;
        let count = 0;
        for (let sy = srcY0; sy < srcY1; sy++) {
            for (let sx = srcX0; sx < srcX1; sx++) {
                const srcIndex = (sy * image.width + sx) * 4;
                r += image.rgba[srcIndex];
                g += image.rgba[srcIndex + 1];
                b += image.rgba[srcIndex + 2];
                a += image.rgba[srcIndex + 3];
                count++;
            }
        }
        dst[dstIndex] = Math.round(r / count);
        dst[dstIndex + 1] = Math.round(g / count);
        dst[dstIndex + 2] = Math.round(b / count);
        dst[dstIndex + 3] = Math.round(a / count);
    }
}

class TestOffscreenCanvas {
    private readonly context = new TestCanvasContext2D();
    constructor(public readonly width: number, public readonly height: number) {}
    getContext(kind: string): TestCanvasContext2D | null {
        return kind === '2d' ? this.context : null;
    }
}

vi.stubGlobal('OffscreenCanvas', TestOffscreenCanvas);

function toImageBitmap(bitmap: TestBitmap): Promise<ImageBitmap> {
    return Promise.resolve(bitmap as unknown as ImageBitmap);
}

// Shared with the PLACEHOLDER_PREFIX test below: the prefix was captured from this exact fill,
// so a bitmap built from it must keep matching for PLACEHOLDER_FORMAT_STRIPPED to come out.
const TEST_FILL_COLOR: readonly [number, number, number, number] = [128, 96, 160, 255];

function createTestBitmap(width: number, height: number): Promise<ImageBitmap> {
    const rgba = new Uint8ClampedArray(width * height * 4);
    const [r, g, b, a] = TEST_FILL_COLOR;
    for (let i = 0; i < rgba.length; i += 4) {
        rgba[i] = r;
        rgba[i + 1] = g;
        rgba[i + 2] = b;
        rgba[i + 3] = a;
    }
    return toImageBitmap(new TestBitmap(width, height, rgba));
}

function clampByte(value: number): number {
    return Math.max(0, Math.min(255, Math.round(value)));
}

function createPhotoBitmap(width: number, height: number): Promise<ImageBitmap> {
    const rgba = new Uint8ClampedArray(width * height * 4);
    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) {
            const i = (y * width + x) * 4;
            const gx = x / width;
            const gy = y / height;
            // A slow spatial gradient plus a small high-frequency dither: the dither approximates
            // photo grain, which a real high-quality downscale (like this file's box-filter double)
            // blurs away, while the gradient survives into the placeholder.
            const dither = (x * 37 + y * 59) % 23 - 11;
            rgba[i] = clampByte(40 + 180 * gx + dither);
            rgba[i + 1] = clampByte(60 + 150 * gy + dither);
            rgba[i + 2] = clampByte(90 + 120 * gx * gy + dither);
            rgba[i + 3] = 255;
        }
    }
    return toImageBitmap(new TestBitmap(width, height, rgba));
}

let cachedEncoder: Promise<JpegliEncoder> | null = null;
function loadEncoder(): Promise<JpegliEncoder> {
    return cachedEncoder ??= JpegliEncoder.load(BASE_URL);
}

const SOF0_MARKER = 0xC0;

function findSegmentEnd(jpeg: Uint8Array, marker: number): number {
    let offset = 2;
    while (offset + 4 <= jpeg.length) {
        const segmentLength = (jpeg[offset + 2] << 8) | jpeg[offset + 3];
        if (jpeg[offset + 1] === marker)
            return offset + 2 + segmentLength;

        offset += 2 + segmentLength;
    }
    throw new Error(`findSegmentEnd: no 0x${marker.toString(16)} marker`);
}

describe('placeholder container', () => {
    it('should encode a landscape image with a negative short-side byte', async () => {
        // arrange: a 128x96 source becomes a 64x48 placeholder
        const bitmap = await createTestBitmap(128, 96);
        // act
        const packed = encodePlaceholder(bitmap, await loadEncoder());
        // assert
        expect(packed[0]).toBe(PLACEHOLDER_FORMAT_STRIPPED);
        expect(new Int8Array(packed.buffer, 1, 1)[0]).toBe(-48);
        expect(packed.length).toBeLessThan(1024);
    });

    it('should encode a portrait image with a positive short-side byte', async () => {
        const bitmap = await createTestBitmap(96, 128);
        const packed = encodePlaceholder(bitmap, await loadEncoder());
        expect(new Int8Array(packed.buffer, 1, 1)[0]).toBe(48);
    });

    it('should clamp an extreme aspect ratio to 2:1', async () => {
        const bitmap = await createTestBitmap(1000, 100);
        const packed = encodePlaceholder(bitmap, await loadEncoder());
        expect(Math.abs(new Int8Array(packed.buffer, 1, 1)[0])).toBe(PLACEHOLDER_LONG_SIDE / 2);
    });

    it('should encode a square image with a positive short-side byte', async () => {
        const bitmap = await createTestBitmap(200, 200);
        const packed = encodePlaceholder(bitmap, await loadEncoder());
        expect(new Int8Array(packed.buffer, 1, 1)[0]).toBe(PLACEHOLDER_LONG_SIDE);
    });

    it('should stay well inside the byte budget on a photo', async () => {
        const bitmap = await createPhotoBitmap(1200, 900);
        const packed = encodePlaceholder(bitmap, await loadEncoder());
        // Measured median is ~413 bytes across 15 real photos
        expect(packed.length).toBeLessThan(900);
    });

    it('should match PLACEHOLDER_PREFIX byte for byte (prints the template when it does not)', async () => {
        // arrange: the exact fill, size and settings PLACEHOLDER_PREFIX was captured from
        const rgba = new Uint8ClampedArray(64 * 48 * 4);
        const [r, g, b, a] = TEST_FILL_COLOR;
        for (let i = 0; i < rgba.length; i += 4) {
            rgba[i] = r;
            rgba[i + 1] = g;
            rgba[i + 2] = b;
            rgba[i + 3] = a;
        }

        // act: the prefix ends with SOF0 - the Huffman tables after it stay in the payload
        const jpeg = (await loadEncoder()).encode(rgba, 64, 48, {
            distance: PLACEHOLDER_DISTANCE,
            subsampling: 420,
            progressive: 0,
        });
        const prefix = Array.from(jpeg.subarray(0, findSegmentEnd(jpeg, SOF0_MARKER)));

        // assert: a failure means jpegli or PLACEHOLDER_DISTANCE changed, which needs a NEW format
        // mark plus this printout pasted into PLACEHOLDER_PREFIX - never an edit under mark 1
        console.log('PLACEHOLDER_PREFIX =', prefix);
        expect(prefix).toEqual(Array.from(PLACEHOLDER_PREFIX));
    });
});
