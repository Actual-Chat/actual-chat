import { describe, it, expect } from 'vitest';
import { stripImageMetadata } from 'image-processing/metadata-stripper';

const ascii = (text: string): number[] => Array.from(text, c => c.charCodeAt(0));
const u32be = (value: number): number[] => [value >>> 24, (value >>> 16) & 0xFF, (value >>> 8) & 0xFF, value & 0xFF];
const u32le = (value: number): number[] => [value & 0xFF, (value >>> 8) & 0xFF, (value >>> 16) & 0xFF, value >>> 24];
const contains = (bytes: Uint8Array, text: string): boolean => Buffer.from(bytes).includes(Buffer.from(text, 'latin1'));

const jpegSegment = (marker: number, payload: number[]): number[] =>
    [0xFF, marker, (payload.length + 2) >> 8, (payload.length + 2) & 0xFF, ...payload];
const exifPayload = (orientation: number): number[] => [
    ...ascii('Exif\0\0'),
    0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
    0x02, 0x00,
    0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, orientation, 0x00, 0x00, 0x00,
    0x25, 0x88, 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 0x2A, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00,
    ...ascii('GPSSECRET'),
];

const SOI = [0xFF, 0xD8];
const ICC = jpegSegment(0xE2, [...ascii('ICC_PROFILE\0'), 1, 1, 0xAA, 0xBB]);
const MPF = jpegSegment(0xE2, [...ascii('MPF\0'), 0x4D, 0x4D, 0x00, 0x2A]);
const XMP = jpegSegment(0xE1, [...ascii('http://ns.adobe.com/xap/1.0/\0'), ...ascii('<x:xmpmeta>creator</x:xmpmeta>')]);
const XMP_HDR = jpegSegment(0xE1, [...ascii('http://ns.adobe.com/xap/1.0/\0'), ...ascii('<x hdrgm:Version="1.0"/>')]);
const COM = jpegSegment(0xFE, ascii('secret comment'));
const IPTC = jpegSegment(0xED, ascii('Photoshop 3.0\0'));
const DQT = jpegSegment(0xDB, [0x00, ...new Array<number>(64).fill(1)]);
const SCAN = [0xFF, 0xDA, 0x00, 0x08, 1, 1, 0, 0, 63, 0, 0x12, 0x34, 0xFF, 0xD9];
const GAIN_MAP = [0xFF, 0xD8, 0x55, 0xFF, 0xD9];

describe('stripImageMetadata: JPEG', () => {
    it('should drop EXIF, XMP, IPTC and comments and keep ICC, image data and trailer', () => {
        const input = new Uint8Array([
            ...SOI, ...jpegSegment(0xE1, exifPayload(1)), ...XMP, ...IPTC, ...COM, ...ICC, ...DQT, ...SCAN, ...GAIN_MAP,
        ]);

        const result = stripImageMetadata(input, 'jpeg');

        expect(Array.from(result)).toEqual([...SOI, ...ICC, ...DQT, ...SCAN, ...GAIN_MAP]);
    });

    it('should keep only the Orientation tag when it is not 1', () => {
        const input = new Uint8Array([...SOI, ...jpegSegment(0xE1, exifPayload(6)), ...DQT, ...SCAN]);

        const result = stripImageMetadata(input, 'jpeg');

        expect(contains(result, 'GPSSECRET')).toBe(false);
        expect(Array.from(result.subarray(0, 4))).toEqual([0xFF, 0xD8, 0xFF, 0xE1]);
        expect(Array.from(result.subarray(4, 6))).toEqual([0x00, 0x22]);
        expect(result[31]).toBe(6);
        expect(Array.from(result.subarray(38))).toEqual([...DQT, ...SCAN]);
    });

    it('should return the same instance when re-stripping an already-minimal Orientation segment', () => {
        const input = new Uint8Array([...SOI, ...jpegSegment(0xE1, exifPayload(6)), ...DQT, ...SCAN]);
        const stripped = stripImageMetadata(input, 'jpeg');

        const result = stripImageMetadata(stripped, 'jpeg');

        expect(result).toBe(stripped);
    });

    it('should keep Ultra HDR XMP', () => {
        const input = new Uint8Array([...SOI, ...XMP_HDR, ...COM, ...DQT, ...SCAN]);

        const result = stripImageMetadata(input, 'jpeg');

        expect(Array.from(result)).toEqual([...SOI, ...XMP_HDR, ...DQT, ...SCAN]);
    });

    it('should not move anything that follows the MPF segment', () => {
        const input = new Uint8Array([...SOI, ...COM, ...MPF, ...COM, ...DQT, ...SCAN]);

        const result = stripImageMetadata(input, 'jpeg');

        expect(Array.from(result)).toEqual([...SOI, ...MPF, ...COM, ...DQT, ...SCAN]);
    });

    it('should return the input instance when there is nothing to strip or the file is malformed', () => {
        const clean = new Uint8Array([...SOI, ...DQT, ...SCAN]);
        const truncated = new Uint8Array([...SOI, 0xFF, 0xE1, 0x10, 0x00, 0x01]);

        expect(stripImageMetadata(clean, 'jpeg')).toBe(clean);
        expect(stripImageMetadata(truncated, 'jpeg')).toBe(truncated);
    });
});

describe('stripImageMetadata: PNG', () => {
    const chunk = (type: string, data: number[] = []): number[] =>
        [...u32be(data.length), ...ascii(type), ...data, 1, 2, 3, 4];
    const SIGNATURE = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    it('should drop text, time and EXIF chunks', () => {
        const ihdr = chunk('IHDR', new Array<number>(13).fill(0));
        const iccp = chunk('iCCP', [...ascii('icc\0'), 0, 9]);
        const idat = chunk('IDAT', [7, 7]);
        const iend = chunk('IEND');
        const input = new Uint8Array([
            ...SIGNATURE, ...ihdr, ...chunk('tEXt', ascii('Author\0me')), ...chunk('eXIf', [1, 2]),
            ...chunk('iTXt', [0]), ...chunk('zTXt', [0]), ...chunk('tIME', new Array<number>(7).fill(0)),
            ...iccp, ...idat, ...iend,
        ]);

        const result = stripImageMetadata(input, 'png');

        expect(Array.from(result)).toEqual([...SIGNATURE, ...ihdr, ...iccp, ...idat, ...iend]);
    });
});

describe('stripImageMetadata: WebP', () => {
    const chunk = (fourCC: string, data: number[]): number[] =>
        [...ascii(fourCC), ...u32le(data.length), ...data, ...(data.length % 2 === 1 ? [0] : [])];

    it('should drop EXIF and XMP chunks, fix the RIFF size and clear VP8X flags', () => {
        const vp8x = chunk('VP8X', [0x0C | 0x10, 0, 0, 0, 1, 0, 0, 1, 0, 0]);
        const iccp = chunk('ICCP', [9, 9]);
        const vp8 = chunk('VP8 ', [5, 5, 5]);
        const body = [
            ...ascii('WEBP'), ...vp8x, ...iccp, ...vp8, ...chunk('EXIF', ascii('GPSSECRET')), ...chunk('XMP ', [1, 2]),
        ];
        const input = new Uint8Array([...ascii('RIFF'), ...u32le(body.length), ...body]);

        const result = stripImageMetadata(input, 'webp');

        const expectedBody = [...ascii('WEBP'), ...vp8x, ...iccp, ...vp8];
        expectedBody[12] = 0x10;
        expect(Array.from(result)).toEqual([...ascii('RIFF'), ...u32le(expectedBody.length), ...expectedBody]);
    });
});
