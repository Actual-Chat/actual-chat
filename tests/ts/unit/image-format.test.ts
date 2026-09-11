import { describe, it, expect } from 'vitest';
import {
    getImageMimeType,
    isAnimatedImage,
    readImageDimensions,
    sniffImageFormat,
} from 'image-processing/image-format';

const ascii = (text: string): number[] => Array.from(text, c => c.charCodeAt(0));
const bytesOf = (...parts: number[][]): Uint8Array => new Uint8Array(parts.flat());
const u32be = (value: number): number[] => [value >>> 24, (value >>> 16) & 0xFF, (value >>> 8) & 0xFF, value & 0xFF];
const pngChunk = (type: string, data: number[] = []): number[] =>
    [...u32be(data.length), ...ascii(type), ...data, 0, 0, 0, 0];
const zeros = (count: number): number[] => new Array<number>(count).fill(0);
const PNG_SIGNATURE = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

describe('sniffImageFormat', () => {
    it('should detect common raster formats', () => {
        expect(sniffImageFormat(bytesOf([0xFF, 0xD8, 0xFF, 0xE0]))).toBe('jpeg');
        expect(sniffImageFormat(bytesOf(PNG_SIGNATURE))).toBe('png');
        expect(sniffImageFormat(bytesOf(ascii('GIF89a')))).toBe('gif');
        expect(sniffImageFormat(bytesOf(ascii('RIFF'), [0, 0, 0, 0], ascii('WEBP')))).toBe('webp');
        expect(sniffImageFormat(bytesOf(ascii('BM'), [0, 0, 0, 0]))).toBe('bmp');
    });

    it('should detect HEIF and AVIF by ftyp brands', () => {
        expect(sniffImageFormat(bytesOf(u32be(16), ascii('ftypheic'), [0, 0, 0, 0]))).toBe('heif');
        expect(sniffImageFormat(bytesOf(u32be(16), ascii('ftypavif'), [0, 0, 0, 0]))).toBe('avif');
        expect(sniffImageFormat(bytesOf(u32be(24), ascii('ftypmif1'), [0, 0, 0, 0], ascii('miafavif')))).toBe('avif');
    });

    it('should detect SVG and fall back to unknown', () => {
        expect(sniffImageFormat(bytesOf(ascii('<?xml version="1.0"?>\n<svg xmlns="x"/>')))).toBe('svg');
        expect(sniffImageFormat(bytesOf(ascii('hello world')))).toBe('unknown');
    });
});

describe('isAnimatedImage', () => {
    it('should detect APNG by acTL before IDAT', () => {
        const apng = bytesOf(PNG_SIGNATURE,
            pngChunk('IHDR', zeros(13)), pngChunk('acTL', zeros(8)), pngChunk('IDAT'));
        const png = bytesOf(PNG_SIGNATURE, pngChunk('IHDR', zeros(13)), pngChunk('IDAT'), pngChunk('acTL'));

        expect(isAnimatedImage(apng, 'png')).toBe(true);
        expect(isAnimatedImage(png, 'png')).toBe(false);
    });

    it('should detect animated WebP by the VP8X animation flag', () => {
        const webp = (flags: number): Uint8Array =>
            bytesOf(ascii('RIFF'), [0, 0, 0, 0], ascii('WEBPVP8X'), [10, 0, 0, 0], [flags], zeros(9));

        expect(isAnimatedImage(webp(0x02), 'webp')).toBe(true);
        expect(isAnimatedImage(webp(0x10), 'webp')).toBe(false);
    });
});

describe('readImageDimensions', () => {
    it('should read JPEG dimensions from the SOF segment', () => {
        const jpeg = bytesOf([0xFF, 0xD8], [0xFF, 0xE0, 0x00, 0x04, 0, 0],
            [0xFF, 0xC2, 0x00, 0x0B, 8, 0x0B, 0xB8, 0x0F, 0xA0, 3, 0, 0, 0]);

        expect(readImageDimensions(jpeg, 'jpeg')).toEqual({ width: 4000, height: 3000 });
    });

    it('should skip fill bytes before the JPEG SOF segment', () => {
        const jpeg = bytesOf([0xFF, 0xD8], [0xFF, 0xE0, 0x00, 0x04, 0, 0], [0xFF, 0xFF],
            [0xFF, 0xC2, 0x00, 0x0B, 8, 0x0B, 0xB8, 0x0F, 0xA0, 3, 0, 0, 0]);

        expect(readImageDimensions(jpeg, 'jpeg')).toEqual({ width: 4000, height: 3000 });
    });

    it('should read PNG dimensions from IHDR', () => {
        const png = bytesOf(PNG_SIGNATURE, pngChunk('IHDR', [...u32be(8192), ...u32be(6144), 8, 6, 0, 0, 0]));

        expect(readImageDimensions(png, 'png')).toEqual({ width: 8192, height: 6144 });
    });

    it('should read extended WebP dimensions from VP8X', () => {
        const width = 9999;
        const height = 4999;
        const webp = bytesOf(ascii('RIFF'), [0, 0, 0, 0], ascii('WEBPVP8X'), [10, 0, 0, 0], [0, 0, 0, 0],
            [width & 0xFF, (width >> 8) & 0xFF, width >> 16], [height & 0xFF, (height >> 8) & 0xFF, height >> 16]);

        expect(readImageDimensions(webp, 'webp')).toEqual({ width: 10000, height: 5000 });
    });

    it('should read the largest HEIF ispe box', () => {
        const ispe = (w: number, h: number): number[] =>
            [...u32be(20), ...ascii('ispe'), 0, 0, 0, 0, ...u32be(w), ...u32be(h)];
        const heif = bytesOf(u32be(16), ascii('ftypheic'), [0, 0, 0, 0], ispe(512, 512), ispe(16320, 12240));

        expect(readImageDimensions(heif, 'heif')).toEqual({ width: 16320, height: 12240 });
    });

    it('should return null for formats it does not read', () => {
        expect(readImageDimensions(bytesOf(ascii('GIF89a')), 'gif')).toBeNull();
    });
});

describe('getImageMimeType', () => {
    it('should map formats to MIME types', () => {
        expect(getImageMimeType('jpeg')).toBe('image/jpeg');
        expect(getImageMimeType('heif')).toBe('image/heif');
        expect(getImageMimeType('unknown')).toBe('application/octet-stream');
    });
});
