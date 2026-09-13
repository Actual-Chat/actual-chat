import { describe, it, expect } from 'vitest';
import { canHaveAlpha, chooseEncoding, tryKeepSource } from 'image-processing/image-encoding-policy';
import type { ImageOutputSpec } from 'image-processing/image-processing-contracts';

const ascii = (text: string): number[] => Array.from(text, c => c.charCodeAt(0));
const u32be = (value: number): number[] => [value >>> 24, (value >>> 16) & 0xFF, (value >>> 8) & 0xFF, value & 0xFF];
const zeros = (count: number): number[] => new Array<number>(count).fill(0);
const pngChunk = (type: string, data: number[] = []): number[] =>
    [...u32be(data.length), ...ascii(type), ...data, 1, 2, 3, 4];
const PNG_SIGNATURE = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
const SPEC: ImageOutputSpec = {
    kind: 'main',
    maxPixels: null,
    maxLongSide: 3840,
    codec: 'auto',
    stripMetadata: true,
    maxPassthroughPixels: null,
};
const SIZE = { width: 800, height: 600 };

describe('chooseEncoding', () => {
    it('should pass through when requested or animated', () => {
        expect(chooseEncoding('jpeg', false, 'passthrough', false)).toBe('passthrough');
        expect(chooseEncoding('webp', true, 'auto', false)).toBe('passthrough');
    });

    it('should re-encode an oversized passthrough image unless it must never be re-encoded', () => {
        expect(chooseEncoding('heif', false, 'passthrough', true)).toBe('reencode');
        expect(chooseEncoding('webp', true, 'passthrough', true)).toBe('passthrough');
        expect(chooseEncoding('gif', false, 'passthrough', true)).toBe('passthrough');
    });

    it('should pass through formats that must not be re-encoded', () => {
        for (const format of ['gif', 'svg', 'unknown'] as const)
            expect(chooseEncoding(format, false, 'auto', false)).toBe('passthrough');
    });

    it('should re-encode decodable still images', () => {
        for (const format of ['jpeg', 'png', 'webp', 'bmp', 'heif', 'avif'] as const)
            expect(chooseEncoding(format, false, 'auto', false)).toBe('reencode');
    });
});

describe('canHaveAlpha', () => {
    it('should exclude only JPEG', () => {
        // act & assert
        expect(canHaveAlpha('jpeg')).toBe(false);
        // A 32-bit BMP (BITMAPV4/V5 header) carries alpha, so it must still be scanned
        for (const format of ['png', 'webp', 'bmp', 'heif', 'avif', 'unknown'] as const)
            expect(canHaveAlpha(format)).toBe(true);
    });
});

describe('tryKeepSource', () => {
    const ihdr = pngChunk('IHDR', zeros(13));
    const idat = pngChunk('IDAT', [7, 7]);
    const iend = pngChunk('IEND');
    const clean = new Uint8Array([...PNG_SIGNATURE, ...ihdr, ...idat, ...iend]);
    const withText = new Uint8Array([
        ...PNG_SIGNATURE, ...ihdr, ...pngChunk('tEXt', ascii('Author\0me')), ...idat, ...iend,
    ]);

    it('should return the stripped bytes when the re-encoded image is not smaller', async () => {
        // arrange
        const source = new Blob([withText], { type: 'image/png' });

        // act
        const result = tryKeepSource(source, withText, 'png', SPEC, SIZE, withText.length);

        // assert
        expect(result).not.toBeNull();
        expect(result!.isSource).toBe(false);
        expect(result!.mimeType).toBe('image/png');
        expect(result!.kind).toBe('main');
        expect(result!.width).toBe(800);
        expect(result!.height).toBe(600);
        expect(new Uint8Array(await result!.blob.arrayBuffer())).toEqual(clean);
    });

    it('should return null when the re-encoded image is smaller than the stripped source', () => {
        // arrange
        const source = new Blob([withText], { type: 'image/png' });

        // act & assert
        expect(tryKeepSource(source, withText, 'png', SPEC, SIZE, clean.length - 1)).toBeNull();
    });

    it('should return the source blob itself when there is nothing to strip', () => {
        // arrange
        const source = new Blob([clean], { type: 'image/png' });

        // act
        const result = tryKeepSource(source, clean, 'png', SPEC, SIZE, clean.length);

        // assert
        expect(result).not.toBeNull();
        expect(result!.isSource).toBe(true);
        expect(result!.blob).toBe(source);
    });
});
