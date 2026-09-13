import { beforeAll, describe, expect, it, vi } from 'vitest';
import type {
    ImageOutputSpec,
    ImageProcessRequest,
    ImageProcessResult,
} from 'image-processing/image-processing-contracts';
import type { ImageSize } from 'image-processing/image-geometry';

const BASE_URL = new URL('../../../src/nodejs/jpegli', import.meta.url).href;

// image-processor-worker runs as a module worker in production, where `self` is the worker's
// own global scope; stand one in so its top-level rpcServer(...) call can bind to it. This must
// happen before the worker module is imported, so the import below is dynamic (in beforeAll).
(globalThis as unknown as { self?: unknown }).self ??= globalThis;

type ImageProcessorWorkerModule = typeof import('image-processing/image-processor-worker');

// vitest's node environment has neither OffscreenCanvas, ImageBitmap nor createImageBitmap, and
// jpegli needs a real RGBA buffer to encode, so these doubles do an actual area-average resize
// rather than no-op stubs, matching placeholder-encoder.test.ts's approach.
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

function makeGradientRgba(width: number, height: number): Uint8ClampedArray {
    const rgba = new Uint8ClampedArray(width * height * 4);
    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) {
            const i = (y * width + x) * 4;
            rgba[i] = Math.round(255 * (x / width));
            rgba[i + 1] = Math.round(255 * (y / height));
            rgba[i + 2] = 128;
            rgba[i + 3] = 255;
        }
    }
    return rgba;
}

// createImageBitmap decodes real image bytes in production; here it looks up the size the test
// registered for that exact Blob and synthesizes a bitmap of it (or of the requested resize).
const sourceSizeByBlob = new WeakMap<Blob, ImageSize>();

const createImageBitmapMock = vi.fn(
    (source: Blob, options?: { resizeWidth?: number; resizeHeight?: number }): Promise<ImageBitmap> => {
        const size = sourceSizeByBlob.get(source);
        if (!size)
            throw new Error('createImageBitmapMock: unregistered source blob');

        const width = options?.resizeWidth ?? size.width;
        const height = options?.resizeHeight ?? Math.round(width * size.height / size.width);
        const bitmap = new TestBitmap(width, height, makeGradientRgba(width, height));
        return Promise.resolve(bitmap as unknown as ImageBitmap);
    },
);
vi.stubGlobal('createImageBitmap', createImageBitmapMock);

function makeJpegHeader(width: number, height: number): Uint8Array {
    // SOI, then a minimal SOF0 segment carrying width/height (the only markers
    // readImageDimensions/stripImageMetadata need), then EOI
    return new Uint8Array([
        0xFF, 0xD8,
        0xFF, 0xC0, 0x00, 0x11, 0x08,
        (height >> 8) & 0xFF, height & 0xFF,
        (width >> 8) & 0xFF, width & 0xFF,
        0x03,
        0x01, 0x22, 0x00,
        0x02, 0x11, 0x01,
        0x03, 0x11, 0x01,
        0xFF, 0xD9,
    ]);
}

function makeJpegBlob(width: number, height: number): Blob {
    const blob = new Blob([makeJpegHeader(width, height) as BlobPart], { type: 'image/jpeg' });
    sourceSizeByBlob.set(blob, { width, height });
    return blob;
}

function mainSpec(maxPixels: number, maxLongSide: number): ImageOutputSpec {
    return { kind: 'main', maxPixels, maxLongSide, codec: 'auto', stripMetadata: true, maxPassthroughPixels: null };
}

function passthroughMainSpec(): ImageOutputSpec {
    return {
        kind: 'main', maxPixels: null, maxLongSide: null, codec: 'passthrough',
        stripMetadata: true, maxPassthroughPixels: null,
    };
}

function placeholderSpec(): ImageOutputSpec {
    return {
        kind: 'placeholder', maxPixels: null, maxLongSide: null, codec: 'placeholder',
        stripMetadata: true, maxPassthroughPixels: null,
    };
}

let worker: ImageProcessorWorkerModule;

beforeAll(async () => {
    worker = await import('image-processing/image-processor-worker');
    await worker.serverImpl.init(BASE_URL);
});

function processInWorker(source: Blob, request: ImageProcessRequest): Promise<ImageProcessResult> {
    return worker.serverImpl.process(source, request);
}

describe('placeholder output', () => {
    it('should return a placeholder alongside the main output from one decode', async () => {
        // arrange
        const source = makeJpegBlob(800, 600);
        createImageBitmapMock.mockClear();

        // act
        const result = await processInWorker(source, {
            outputs: [mainSpec(12582912, 6144), placeholderSpec()],
        });

        // assert
        const placeholder = result.outputs.find(o => o.kind === 'placeholder');
        expect(placeholder?.placeholder).toBeTruthy();
        expect(atob(placeholder!.placeholder!).length).toBeLessThan(1024);
        // one decode, two outputs: the placeholder must not trigger a second createImageBitmap call
        expect(createImageBitmapMock).toHaveBeenCalledTimes(1);
        expect(createImageBitmapMock).toHaveBeenCalledWith(source);
    });

    it('should decode only a small bitmap for the placeholder when the main output is a passthrough', async () => {
        // arrange: a passthrough main output never calls createImageBitmap, so a placeholder needs
        // its own decode - it must be requested downscaled, never at full resolution
        const source = makeJpegBlob(4000, 3000);
        createImageBitmapMock.mockClear();

        // act
        const result = await processInWorker(source, {
            outputs: [passthroughMainSpec(), placeholderSpec()],
        });

        // assert
        const placeholder = result.outputs.find(o => o.kind === 'placeholder');
        expect(placeholder?.placeholder).toBeTruthy();
        expect(createImageBitmapMock).toHaveBeenCalledTimes(1);
        expect(createImageBitmapMock).toHaveBeenCalledWith(source, { resizeWidth: 64, resizeQuality: 'high' });
    });

    it('should return an empty placeholder without failing the main output when decoding fails', async () => {
        // arrange: an unregistered blob makes the fake decoder throw, standing in for a source
        // format the browser can't decode
        const source = new Blob([new Uint8Array([1, 2, 3])], { type: 'image/jpeg' });

        // act
        const result = await processInWorker(source, { outputs: [passthroughMainSpec(), placeholderSpec()] });

        // assert
        const main = result.outputs.find(o => o.kind === 'main');
        const placeholder = result.outputs.find(o => o.kind === 'placeholder');
        expect(main).toBeTruthy();
        expect(placeholder?.placeholder).toBe('');
    });
});
