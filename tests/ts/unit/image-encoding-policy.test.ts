import { describe, it, expect } from 'vitest';
import { chooseEncoding } from 'image-processing/image-encoding-policy';

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
