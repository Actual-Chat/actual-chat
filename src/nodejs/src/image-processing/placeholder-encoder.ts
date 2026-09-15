import type { JpegliEncoder } from './jpegli-encoder';
import type { ImageSize } from './image-geometry';

export const PLACEHOLDER_LONG_SIDE = 64;
export const PLACEHOLDER_DISTANCE = 6;
export const PLACEHOLDER_FORMAT_STRIPPED = 1;
export const PLACEHOLDER_FORMAT_FULL = 2;

// SOI+DQT+SOF0, baseline (progressive: 0) 4:2:0 at PLACEHOLDER_DISTANCE, jpegli commit
// 031a0077f5799a6041004267fc12b956c1f52a20 (src/nodejs/jpegli/README.md) — content-independent
// except SOF0's 4 dimension bytes, which byte 1 rebuilds. placeholder-encoder.test.ts asserts this
// array against the encoder's own output and prints a replacement when it stops matching.
// PLACEHOLDER_PREFIX and PLACEHOLDER_DISTANCE change together; changing either needs a new format
// mark, never an edit — stored rows were packed against the old one.
export const PLACEHOLDER_PREFIX = new Uint8Array([
    255, 216, 255, 219, 0, 197, 0, 16, 11, 11, 24, 17, 24, 25, 24, 24,
    25, 43, 29, 30, 29, 43, 44, 44, 34, 34, 44, 44, 49, 40, 44, 43,
    44, 40, 49, 49, 51, 50, 53, 53, 50, 51, 49, 51, 48, 54, 58, 54,
    48, 51, 54, 54, 63, 63, 54, 54, 59, 71, 69, 71, 59, 70, 65, 65,
    70, 78, 69, 78, 69, 69, 56, 1, 15, 13, 13, 26, 22, 26, 32, 26,
    26, 32, 44, 41, 41, 41, 44, 62, 64, 60, 60, 64, 62, 76, 66, 65,
    72, 65, 66, 76, 124, 141, 115, 72, 72, 115, 141, 124, 94, 255, 96, 77,
    96, 255, 94, 108, 112, 63, 63, 112, 108, 175, 69, 128, 69, 175, 250, 189,
    189, 250, 62, 62, 62, 62, 62, 255, 2, 15, 9, 9, 21, 16, 21, 19,
    20, 20, 19, 34, 21, 25, 21, 34, 32, 31, 23, 23, 31, 32, 38, 28,
    29, 27, 29, 28, 38, 41, 35, 33, 35, 35, 33, 35, 41, 37, 34, 30,
    27, 30, 34, 37, 39, 33, 41, 41, 33, 39, 41, 31, 35, 31, 41, 39,
    40, 40, 39, 59, 64, 59, 59, 59, 255, 255, 192, 0, 17, 8, 0, 48,
    0, 64, 3, 1, 34, 0, 2, 17, 1, 3, 17, 2,
]);

/** The 2-byte EOI marker jpegli always appends; stripped in format 1 and rebuilt by the decoder. */
const JPEG_EOI_LENGTH = 2;

export function placeholderSize(width: number, height: number): ImageSize {
    const longSide = PLACEHOLDER_LONG_SIDE;
    // The 2:1 clamp keeps a panorama from degenerating into a line
    const shortSide = Math.max(
        Math.round(longSide / 2),
        Math.min(longSide, Math.round(longSide * Math.min(width, height) / Math.max(width, height))));
    return width >= height
        ? { width: longSide, height: shortSide }
        : { width: shortSide, height: longSide };
}

export function encodePlaceholder(bitmap: ImageBitmap, encoder: JpegliEncoder): Uint8Array {
    const size = placeholderSize(bitmap.width, bitmap.height);
    const canvas = new OffscreenCanvas(size.width, size.height);
    const context = canvas.getContext('2d')!;
    context.imageSmoothingQuality = 'high';
    context.drawImage(bitmap, 0, 0, size.width, size.height);
    const image = context.getImageData(0, 0, size.width, size.height);
    const jpeg = encoder.encode(image.data, size.width, size.height, {
        distance: PLACEHOLDER_DISTANCE,
        subsampling: 420,
        progressive: 0,
    });
    const isStripped = matchesPrefix(jpeg);
    const payload = isStripped ? jpeg.subarray(PLACEHOLDER_PREFIX.length, jpeg.length - JPEG_EOI_LENGTH) : jpeg;
    const packed = new Uint8Array(2 + payload.length);
    packed[0] = isStripped ? PLACEHOLDER_FORMAT_STRIPPED : PLACEHOLDER_FORMAT_FULL;
    new Int8Array(packed.buffer, 1, 1)[0] = size.width <= size.height ? size.width : -size.height;
    packed.set(payload, 2);
    return packed;
}

// Private methods

function matchesPrefix(jpeg: Uint8Array): boolean {
    if (jpeg.length < PLACEHOLDER_PREFIX.length + JPEG_EOI_LENGTH)
        return false;
    if (jpeg[jpeg.length - 2] !== 0xFF || jpeg[jpeg.length - 1] !== 0xD9)
        return false;

    let dimensionOffset: number;
    try {
        dimensionOffset = findSof0DimensionOffset(jpeg);
    }
    catch {
        return false;
    }

    for (let i = 0; i < PLACEHOLDER_PREFIX.length; i++) {
        if (i >= dimensionOffset && i < dimensionOffset + 4)
            continue;
        if (jpeg[i] !== PLACEHOLDER_PREFIX[i])
            return false;
    }
    return true;
}

function findSof0DimensionOffset(jpeg: Uint8Array): number {
    let offset = 2;
    while (offset + 4 <= jpeg.length) {
        const marker = jpeg[offset + 1];
        if (marker === 0xC0)
            return offset + 5;

        const segmentLength = (jpeg[offset + 2] << 8) | jpeg[offset + 3];
        offset += 2 + segmentLength;
    }
    throw new Error('placeholder-encoder: JPEG has no SOF0 marker');
}
