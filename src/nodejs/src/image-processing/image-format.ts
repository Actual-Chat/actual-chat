import type { ImageFormat } from './image-processing-contracts';
import type { ImageSize } from './image-geometry';
import { readAscii, readUint16BE, readUint16LE, readUint32BE, readUint32LE, startsWith } from './image-bytes';

const PNG_SIGNATURE = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
const HEIF_BRANDS = new Set(['heic', 'heix', 'hevc', 'hevx', 'heif', 'heim', 'heis', 'mif1', 'msf1']);
export const EXIF_HEADER = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00];
const EXIF_ORIENTATION_TAG = 0x0112;
const TIFF_HEADER_LENGTH = 8;
const WEBP_ANIMATION_FLAG = 0x02;
const MIME_TYPES: Record<ImageFormat, string> = {
    jpeg: 'image/jpeg',
    png: 'image/png',
    webp: 'image/webp',
    gif: 'image/gif',
    bmp: 'image/bmp',
    heif: 'image/heif',
    avif: 'image/avif',
    svg: 'image/svg+xml',
    unknown: 'application/octet-stream',
};

export function sniffImageFormat(bytes: Uint8Array): ImageFormat {
    if (startsWith(bytes, 0, [0xFF, 0xD8, 0xFF]))
        return 'jpeg';
    if (startsWith(bytes, 0, PNG_SIGNATURE))
        return 'png';
    if (readAscii(bytes, 0, 4) === 'GIF8')
        return 'gif';
    if (readAscii(bytes, 0, 4) === 'RIFF' && readAscii(bytes, 8, 4) === 'WEBP')
        return 'webp';
    if (readAscii(bytes, 0, 2) === 'BM')
        return 'bmp';
    if (readAscii(bytes, 4, 4) === 'ftyp')
        return getIsoBaseMediaFormat(bytes);
    if (looksLikeSvg(bytes))
        return 'svg';

    return 'unknown';
}

export function isAnimatedImage(bytes: Uint8Array, format: ImageFormat): boolean {
    if (format === 'png')
        return hasPngChunkBeforeImageData(bytes, 'acTL');
    if (format === 'webp')
        return readAscii(bytes, 12, 4) === 'VP8X' && bytes.length > 20 && (bytes[20] & WEBP_ANIMATION_FLAG) !== 0;

    return false;
}

/** Reads the stored (pre-orientation) dimensions from the header without decoding the image. */
export function readImageDimensions(bytes: Uint8Array, format: ImageFormat): ImageSize | null {
    switch (format) {
    case 'jpeg':
        return readJpegDimensions(bytes);
    case 'png':
        return bytes.length >= 24 ? { width: readUint32BE(bytes, 16), height: readUint32BE(bytes, 20) } : null;
    case 'webp':
        return readWebpDimensions(bytes);
    case 'heif':
    case 'avif':
        return readLargestIspeDimensions(bytes);
    default:
        return null;
    }
}

/** EXIF Orientation of a HEIF/AVIF image, 1 when it has none. libheif ignores the tag and WebKit
 *  applies it, so a wasm decode has to apply it itself to land on the same pixels. */
export function readHeifOrientation(bytes: Uint8Array): number {
    // The Exif item's payload is a TIFF block behind an 'Exif\0\0' marker; found by scanning
    // rather than by walking iinf/iloc, the same shortcut readLargestIspeDimensions takes
    const end = bytes.length - EXIF_HEADER.length - TIFF_HEADER_LENGTH;
    for (let offset = 0; offset <= end; offset++) {
        if (bytes[offset] !== EXIF_HEADER[0] || !startsWith(bytes, offset, EXIF_HEADER))
            continue;

        const orientation = readExifOrientation(bytes.subarray(offset + EXIF_HEADER.length));
        if (orientation > 1)
            return orientation;
    }
    return 1;
}

/** Reads Orientation out of a TIFF block - the payload of a JPEG APP1 segment or a HEIF Exif item,
 *  in both cases past the 'Exif\0\0' marker. Returns 1 for anything it can't parse. */
export function readExifOrientation(tiff: Uint8Array): number {
    if (tiff.length < TIFF_HEADER_LENGTH)
        return 1;

    const isLittleEndian = tiff[0] === 0x49 && tiff[1] === 0x49;
    if (!isLittleEndian && !(tiff[0] === 0x4D && tiff[1] === 0x4D))
        return 1;

    const readUint16 = (offset: number): number =>
        isLittleEndian ? readUint16LE(tiff, offset) : readUint16BE(tiff, offset);
    const ifdOffset = isLittleEndian ? readUint32LE(tiff, 4) : readUint32BE(tiff, 4);
    if (ifdOffset + 2 > tiff.length)
        return 1;

    const entryCount = readUint16(ifdOffset);
    for (let i = 0; i < entryCount; i++) {
        const entry = ifdOffset + 2 + i * 12;
        if (entry + 12 > tiff.length)
            break;
        if (readUint16(entry) !== EXIF_ORIENTATION_TAG)
            continue;

        const value = readUint16(entry + 8);
        return value >= 1 && value <= 8 ? value : 1;
    }
    return 1;
}

export function getImageMimeType(format: ImageFormat): string {
    return MIME_TYPES[format];
}

/** Formats whose preview must come from a converted copy. Chromium cannot paint HEIF at all, so
 *  the conversion now runs on the wasm decode; it paints AVIF natively, kept here for older
 *  WebViews. WebKit reads both and its caller skips the conversion. */
export function needsPreviewConversion(format: ImageFormat): boolean {
    return format === 'heif' || format === 'avif';
}

// Private methods

function readJpegDimensions(bytes: Uint8Array): ImageSize | null {
    // Same marker walk as stripJpeg in metadata-stripper.ts
    let offset = 2;
    while (offset + 4 <= bytes.length) {
        if (bytes[offset] !== 0xFF)
            return null;

        const marker = bytes[offset + 1];
        if (marker === 0xDA || marker === 0xD9)
            return null;
        // Fill bytes, TEM and restart markers carry no length field
        if (marker === 0xFF || marker === 0x01 || (marker >= 0xD0 && marker <= 0xD7)) {
            offset += marker === 0xFF ? 1 : 2;
            continue;
        }

        const isStartOfFrame =
            marker >= 0xC0 && marker <= 0xCF && marker !== 0xC4 && marker !== 0xC8 && marker !== 0xCC;
        if (isStartOfFrame)
            return offset + 9 <= bytes.length
                ? { width: readUint16BE(bytes, offset + 7), height: readUint16BE(bytes, offset + 5) }
                : null;

        offset += 2 + readUint16BE(bytes, offset + 2);
    }
    return null;
}

function readWebpDimensions(bytes: Uint8Array): ImageSize | null {
    const chunk = readAscii(bytes, 12, 4);
    if (chunk === 'VP8X' && bytes.length >= 30)
        return { width: 1 + readUint24LE(bytes, 24), height: 1 + readUint24LE(bytes, 27) };
    if (chunk === 'VP8 ' && bytes.length >= 30)
        return { width: readUint16LE(bytes, 26) & 0x3FFF, height: readUint16LE(bytes, 28) & 0x3FFF };
    if (chunk === 'VP8L' && bytes.length >= 25) {
        const bits = readUint32LE(bytes, 21);
        return { width: (bits & 0x3FFF) + 1, height: ((bits >>> 14) & 0x3FFF) + 1 };
    }

    return null;
}

function readLargestIspeDimensions(bytes: Uint8Array): ImageSize | null {
    // Every image item - grid tiles, thumbnails, the primary image - has an 'ispe'; the largest is the full image
    let result: ImageSize | null = null;
    for (let offset = 4; offset + 16 <= bytes.length; offset++) {
        if (bytes[offset] !== 0x69 || readAscii(bytes, offset, 4) !== 'ispe')
            continue;

        const width = readUint32BE(bytes, offset + 8);
        const height = readUint32BE(bytes, offset + 12);
        if (!result || width * height > result.width * result.height)
            result = { width, height };
    }
    return result;
}

function readUint24LE(bytes: Uint8Array, offset: number): number {
    return bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);
}

function getIsoBaseMediaFormat(bytes: Uint8Array): ImageFormat {
    const boxEnd = Math.min(readUint32BE(bytes, 0), bytes.length);
    const majorBrand = readAscii(bytes, 8, 4);
    if (majorBrand === 'avif' || majorBrand === 'avis')
        return 'avif';

    // Compatible brands start after the major brand and minor version
    for (let offset = 16; offset + 4 <= boxEnd; offset += 4) {
        if (readAscii(bytes, offset, 4) === 'avif')
            return 'avif';
    }
    return HEIF_BRANDS.has(majorBrand) ? 'heif' : 'unknown';
}

function hasPngChunkBeforeImageData(bytes: Uint8Array, type: string): boolean {
    let offset = 8;
    while (offset + 8 <= bytes.length) {
        const chunkType = readAscii(bytes, offset + 4, 4);
        if (chunkType === type)
            return true;
        if (chunkType === 'IDAT')
            return false;

        offset += 12 + readUint32BE(bytes, offset);
    }
    return false;
}

function looksLikeSvg(bytes: Uint8Array): boolean {
    const head = readAscii(bytes, 0, Math.min(bytes.length, 1024)).replace(/^\xEF\xBB\xBF/, '').trimStart();
    return head.startsWith('<svg') || (head.startsWith('<?xml') && head.includes('<svg'));
}
