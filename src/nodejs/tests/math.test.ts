import { describe, it, expect } from 'vitest';
import {
    Vector2D,
    clamp,
    lerp,
    RunningAverage,
    RunningUnitMedian,
    RunningMA,
    RunningEMA,
    RunningMax,
    translate,
    average,
    approximateGain,
} from 'math';

// --- Vector2D ---

describe('Vector2D', () => {
    it('should add vectors', () => {
        const a = new Vector2D(1, 2);
        const b = new Vector2D(3, 4);
        const r = a.add(b);
        expect(r.x).toBe(4);
        expect(r.y).toBe(6);
    });

    it('should subtract vectors', () => {
        const r = new Vector2D(5, 7).sub(new Vector2D(2, 3));
        expect(r.x).toBe(3);
        expect(r.y).toBe(4);
    });

    it('should multiply by scalar', () => {
        const r = new Vector2D(3, 4).mul(2);
        expect(r.x).toBe(6);
        expect(r.y).toBe(8);
    });

    it('should compute dot product', () => {
        expect(new Vector2D(1, 0).dotProduct(new Vector2D(0, 1))).toBe(0); // perpendicular
        expect(new Vector2D(2, 3).dotProduct(new Vector2D(4, 5))).toBe(23);
    });

    it('should compute length', () => {
        expect(new Vector2D(3, 4).length).toBe(5);
        expect(Vector2D.zero.length).toBe(0);
    });

    it('should compute squareLength', () => {
        expect(new Vector2D(3, 4).squareLength).toBe(25);
    });

    it('should detect horizontal direction', () => {
        expect(new Vector2D(10, 1).isHorizontal()).toBe(true);
        expect(new Vector2D(1, 10).isHorizontal()).toBe(false);
    });

    it('should detect vertical direction', () => {
        expect(new Vector2D(1, 10).isVertical()).toBe(true);
        expect(new Vector2D(10, 1).isVertical()).toBe(false);
    });
});

// --- Scalar utilities ---

describe('clamp', () => {
    it('should clamp within range', () => {
        expect(clamp(5, 0, 10)).toBe(5);
        expect(clamp(-1, 0, 10)).toBe(0);
        expect(clamp(11, 0, 10)).toBe(10);
    });
});

describe('lerp', () => {
    it('should interpolate linearly', () => {
        expect(lerp(0, 10, 0)).toBe(0);
        expect(lerp(0, 10, 1)).toBe(10);
        expect(lerp(0, 10, 0.5)).toBe(5);
    });
});

describe('translate', () => {
    it('should map value between ranges', () => {
        expect(translate(5, [0, 10], [0, 100])).toBe(50);
        expect(translate(0, [0, 10], [100, 200])).toBe(100);
        expect(translate(10, [0, 10], [100, 200])).toBe(200);
    });

    it('should return outMin for degenerate input range', () => {
        expect(translate(5, [5, 5], [0, 100])).toBe(0); // NaN → outMin
    });
});

describe('average', () => {
    it('should compute arithmetic mean', () => {
        expect(average([2, 4, 6])).toBe(4);
        expect(average(new Float32Array([1, 2, 3, 4]))).toBe(2.5);
    });
});

describe('approximateGain', () => {
    it('should compute RMS-like gain', () => {
        const silence = new Float32Array(100).fill(0);
        expect(approximateGain(silence)).toBe(0);

        const loud = new Float32Array(100).fill(1);
        expect(approximateGain(loud)).toBeCloseTo(1, 5);
    });
});

// --- Running counters ---

describe('RunningAverage', () => {
    it('should return default when empty', () => {
        const avg = new RunningAverage(42);
        expect(avg.value).toBe(42);
        expect(avg.sampleCount).toBe(0);
    });

    it('should compute average of samples', () => {
        const avg = new RunningAverage(0);
        avg.appendSample(10);
        avg.appendSample(20);
        avg.appendSample(30);
        expect(avg.value).toBe(20);
        expect(avg.sampleCount).toBe(3);
    });

    it('should support removeSample', () => {
        const avg = new RunningAverage(0);
        avg.appendSample(10);
        avg.appendSample(20);
        avg.removeSample(10);
        expect(avg.value).toBe(20);
        expect(avg.sampleCount).toBe(1);
    });

    it('should reset', () => {
        const avg = new RunningAverage(0);
        avg.appendSample(100);
        avg.reset();
        expect(avg.sampleCount).toBe(0);
        expect(avg.value).toBe(0);
    });
});

describe('RunningUnitMedian', () => {
    it('should return default when empty', () => {
        const median = new RunningUnitMedian(100, 0.5);
        expect(median.value).toBe(0.5);
    });

    it('should approximate median for uniform samples', () => {
        const median = new RunningUnitMedian(100);
        // Add many samples near 0.3
        for (let i = 0; i < 100; i++) median.appendSample(0.3);
        expect(median.value).toBeCloseTo(0.3, 1);
    });

    it('should reset', () => {
        const median = new RunningUnitMedian(100, 0.5);
        median.appendSample(0.1);
        median.reset();
        expect(median.sampleCount).toBe(0);
        expect(median.value).toBe(0.5);
    });
});

describe('RunningMA', () => {
    it('should compute moving average within window', () => {
        const ma = new RunningMA(3, 0);
        ma.appendSample(10);
        ma.appendSample(20);
        ma.appendSample(30);
        expect(ma.value).toBe(20); // (10+20+30)/3

        ma.appendSample(40);
        expect(ma.value).toBe(30); // (20+30+40)/3, window=3
        expect(ma.sampleCount).toBe(3);
    });

    it('should reset', () => {
        const ma = new RunningMA(3, 99);
        ma.appendSample(1);
        ma.reset();
        expect(ma.value).toBe(99);
    });
});

describe('RunningEMA', () => {
    it('should use running average until minSampleCount reached', () => {
        const ema = new RunningEMA(0, 3);
        ema.appendSample(10);
        ema.appendSample(20);
        // Only 2 samples, still using RunningAverage
        expect(ema.value).toBe(15);
    });

    it('should switch to EMA after minSampleCount', () => {
        const ema = new RunningEMA(0, 2);
        ema.appendSample(10);
        ema.appendSample(20);
        // Now at minSampleCount, value = average = 15
        const v2 = ema.value;
        ema.appendSample(30);
        // EMA: prev + factor * (new - prev)
        expect(ema.value).not.toBe(v2);
        expect(ema.sampleCount).toBe(3);
    });

    it('should reset', () => {
        const ema = new RunningEMA(42, 2);
        ema.appendSample(10);
        ema.reset();
        expect(ema.value).toBe(42);
    });
});

describe('RunningMax', () => {
    it('should track max within window', () => {
        const rm = new RunningMax(3, 0);
        rm.appendSample(5);
        rm.appendSample(10);
        rm.appendSample(3);
        expect(rm.value).toBe(10);
    });

    it('should recompute max when max leaves window', () => {
        const rm = new RunningMax(3, 0);
        rm.appendSample(5);
        rm.appendSample(10);
        rm.appendSample(3);
        rm.appendSample(7); // window: [10, 3, 7]
        expect(rm.value).toBe(10);
        rm.appendSample(2); // window: [3, 7, 2] — 10 evicted
        expect(rm.value).toBe(7);
    });

    it('should return default when empty', () => {
        const rm = new RunningMax(3, -1);
        expect(rm.value).toBe(-1);
    });

    it('should reset', () => {
        const rm = new RunningMax(3, 0);
        rm.appendSample(99);
        rm.reset();
        expect(rm.value).toBe(0);
        expect(rm.sampleCount).toBe(0);
    });
});
