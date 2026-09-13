import { describe, it, expect } from 'vitest';
import { fitWithin, fitWithinBudget } from 'image-processing/image-geometry';

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

describe('fitWithinBudget', () => {
    it('should leave an image inside both limits untouched', () => {
        const size = fitWithinBudget(4032, 3024, 12582912, 6144);
        expect(size).toEqual({ width: 4032, height: 3024 });
    });

    it('should scale down to the pixel budget', () => {
        const size = fitWithinBudget(16320, 12240, 50331648, 12288);
        expect(size.width * size.height).toBeLessThanOrEqual(50331648);
        expect(Math.abs(size.width / size.height - 16320 / 12240)).toBeLessThan(0.01);
    });

    it('should let a wide image spend its budget on width up to the long-side cap', () => {
        const size = fitWithinBudget(30000, 5000, 50331648, 12288);
        expect(size.width).toBe(12288);
        expect(size.height).toBe(2048);
    });

    it('should apply the long-side cap even when the pixel budget is not reached', () => {
        const size = fitWithinBudget(20000, 1000, 50331648, 12288);
        expect(size.width).toBe(12288);
    });

    it('should never upscale', () => {
        const size = fitWithinBudget(800, 600, 50331648, 12288);
        expect(size).toEqual({ width: 800, height: 600 });
    });

    it('should treat null limits as unbounded', () => {
        const size = fitWithinBudget(9000, 9000, null, null);
        expect(size).toEqual({ width: 9000, height: 9000 });
    });
});
