import type { ImageFormat } from './image-processing-contracts';
import {
    concatBytes,
    indexOfAscii,
    readAscii,
    readUint16BE,
    readUint16LE,
    readUint32BE,
    readUint32LE,
    startsWith,
    writeUint32LE,
} from './image-bytes';

const EXIF_HEADER = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00];
const XMP_HEADER = 'http://ns.adobe.com/xap/1.0/\0';
const EXIF_ORIENTATION_TAG = 0x0112;
const PNG_DROPPED_CHUNKS = new Set(['eXIf', 'tEXt', 'iTXt', 'zTXt', 'tIME']);
const WEBP_DROPPED_CHUNKS = new Set(['EXIF', 'XMP ']);
const WEBP_METADATA_FLAGS = 0x08 | 0x04;

/** Removes EXIF, XMP, IPTC and text metadata without touching image data. Returns the input
 *  instance when nothing changes, the format isn't JPEG/PNG/WebP, or the file is malformed. */
export function stripImageMetadata(bytes: Uint8Array, format: ImageFormat): Uint8Array {
    switch (format) {
    case 'jpeg':
        return stripJpeg(bytes);
    case 'png':
        return stripPng(bytes);
    case 'webp':
        return stripWebp(bytes);
    default:
        return bytes;
    }
}

// Private methods

function stripJpeg(bytes: Uint8Array): Uint8Array {
    const parts: Uint8Array[] = [bytes.subarray(0, 2)];
    let isChanged = false;
    let isAfterMpf = false;
    let offset = 2;
    while (offset + 4 <= bytes.length) {
        if (bytes[offset] !== 0xFF)
            return bytes;

        const marker = bytes[offset + 1];
        if (marker === 0xDA || marker === 0xD9) {
            parts.push(bytes.subarray(offset));
            return isChanged ? concatBytes(parts) : bytes;
        }
        if (marker === 0xFF || marker === 0x01 || (marker >= 0xD0 && marker <= 0xD7)) {
            const length = marker === 0xFF ? 1 : 2;
            parts.push(bytes.subarray(offset, offset + length));
            offset += length;
            continue;
        }

        const end = offset + 2 + readUint16BE(bytes, offset + 2);
        if (end > bytes.length)
            return bytes;

        const segment = bytes.subarray(offset, end);
        const payload = segment.subarray(4);
        // MPF stores offsets to the images after it, so no segment behind MPF may move
        const replacement = isAfterMpf ? segment : filterJpegSegment(marker, segment, payload);
        if (marker === 0xE2 && readAscii(payload, 0, 4) === 'MPF\0')
            isAfterMpf = true;
        if (replacement !== segment)
            isChanged = true;
        if (replacement)
            parts.push(replacement);
        offset = end;
    }
    return bytes;
}

function filterJpegSegment(marker: number, segment: Uint8Array, payload: Uint8Array): Uint8Array | null {
    if (marker === 0xED || marker === 0xFE)
        return null;
    if (marker !== 0xE1)
        return segment;
    if (startsWith(payload, 0, EXIF_HEADER)) {
        const orientation = readExifOrientation(payload.subarray(EXIF_HEADER.length));
        if (orientation <= 1)
            return null;

        const minimal = createOrientationExifSegment(orientation);
        return segment.length === minimal.length && startsWith(segment, 0, minimal) ? segment : minimal;
    }

    const isHdrXmp = readAscii(payload, 0, XMP_HEADER.length) === XMP_HEADER && indexOfAscii(payload, 'hdrgm') >= 0;
    return isHdrXmp ? segment : null;
}

function readExifOrientation(tiff: Uint8Array): number {
    if (tiff.length < 8)
        return 1;

    const isLittleEndian = tiff[0] === 0x49 && tiff[1] === 0x49;
    if (!isLittleEndian && !(tiff[0] === 0x4D && tiff[1] === 0x4D))
        return 1;

    const readUint16 = (offset: number): number => isLittleEndian ? readUint16LE(tiff, offset) : readUint16BE(tiff, offset);
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

function createOrientationExifSegment(orientation: number): Uint8Array {
    // Big-endian TIFF, IFD0 with one entry: Orientation, SHORT, count 1
    return new Uint8Array([
        0xFF, 0xE1, 0x00, 0x22,
        ...EXIF_HEADER,
        0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
        0x00, 0x01,
        0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, orientation, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
    ]);
}

function stripPng(bytes: Uint8Array): Uint8Array {
    const parts: Uint8Array[] = [bytes.subarray(0, 8)];
    let isChanged = false;
    let offset = 8;
    while (offset + 12 <= bytes.length) {
        const end = offset + 12 + readUint32BE(bytes, offset);
        if (end > bytes.length)
            return bytes;

        const type = readAscii(bytes, offset + 4, 4);
        if (PNG_DROPPED_CHUNKS.has(type))
            isChanged = true;
        else
            parts.push(bytes.subarray(offset, end));
        offset = end;
        if (type === 'IEND')
            break;
    }
    return isChanged ? concatBytes(parts) : bytes;
}

function stripWebp(bytes: Uint8Array): Uint8Array {
    const parts: Uint8Array[] = [bytes.subarray(0, 12)];
    let isChanged = false;
    let offset = 12;
    while (offset + 8 <= bytes.length) {
        const size = readUint32LE(bytes, offset + 4);
        const end = offset + 8 + size + (size & 1);
        if (end > bytes.length)
            return bytes;

        if (WEBP_DROPPED_CHUNKS.has(readAscii(bytes, offset, 4)))
            isChanged = true;
        else
            parts.push(bytes.subarray(offset, end));
        offset = end;
    }
    if (!isChanged)
        return bytes;

    const result = concatBytes(parts);
    writeUint32LE(result, 4, result.length - 8);
    if (readAscii(result, 12, 4) === 'VP8X')
        result[20] &= ~WEBP_METADATA_FLAGS;
    return result;
}
