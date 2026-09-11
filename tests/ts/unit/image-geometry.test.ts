import { describe, it, expect } from 'vitest';
import { fitWithin } from 'image-processing/image-geometry';

describe('fitWithin', () => {
    it('should scale the long side down to maxSize', () => {
        expect(fitWithin(4000, 3000, 3840)).toEqual({ width: 3840, height: 2880 });
        expect(fitWithin(3000, 4000, 1920)).toEqual({ width: 1440, height: 1920 });
    });

    it('should keep images that already fit', () => {
        expect(fitWithin(1000, 800, 1920)).toEqual({ width: 1000, height: 800 });
        expect(fitWithin(5000, 4000, null)).toEqual({ width: 5000, height: 4000 });
    });

    it('should never produce a zero dimension', () => {
        expect(fitWithin(10000, 1, 1920)).toEqual({ width: 1920, height: 1 });
    });
});
