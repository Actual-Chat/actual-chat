import { describe, it, expect } from 'vitest';
import { fitWithinBudget } from 'image-processing/image-geometry';

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
