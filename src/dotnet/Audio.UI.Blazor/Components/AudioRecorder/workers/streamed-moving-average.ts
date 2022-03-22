export class ExponentialMovingAverage {
    private readonly window: number;

    private lastMovingAverage: number;
    private simpleSum = 0;
    private simpleCount = 0;

    constructor(window: number) {
        this.window = window;
        this.lastMovingAverage = NaN;

        if (window < 1 || window > 2000) {
            throw new Error('window should be in [1;2000] range');
        }
    }

    public get lastAverage(): number {
        return this.lastMovingAverage;
    }

    public append(value: number): number {
        const { window, lastMovingAverage } = this;
        const smoothingFactor = 2 / (window + 1);

        if (isNaN(lastMovingAverage)) {
            this.simpleSum += value;
            this.simpleCount++;
            const average = this.simpleSum / this.simpleCount;
            if (this.simpleCount >= window) {
                this.lastMovingAverage = average;
            }
            return average;
        }

        const previousEma = lastMovingAverage;
        const currentEma = (
            value - previousEma) * smoothingFactor + previousEma;
        this.lastMovingAverage = currentEma;

        return currentEma;
    }

    public appendChunk(values: Float32Array): Float32Array {
        if (!values || values.length < this.window) {
            throw new Error('values should not be null, undefined, empty or less than configured window');
        }

        const window = this.window;
        const length = values.length;
        const smoothingFactor = 2 / (
            window + 1);

        const exponentialMovingAverages = new Float32Array(length);
        let startIndex = window;
        if (isNaN(this.lastMovingAverage)) {
            const sma = simpleMovingAverage(values, window);
            exponentialMovingAverages.set(sma, 0);
        } else {
            exponentialMovingAverages[0] = this.lastMovingAverage;
            startIndex = 1;
        }

        for (let index = startIndex; index < values.length; index++) {
            const value = values[index];
            const previousEma = exponentialMovingAverages[index - 1];
            exponentialMovingAverages[index] = (value - previousEma) * smoothingFactor + previousEma;
        }
        this.lastMovingAverage = exponentialMovingAverages[exponentialMovingAverages.length - 1];

        return exponentialMovingAverages;
    }
}

function simpleMovingAverage(values: Float32Array, window = 5, n = Infinity): Float32Array {
    if (!values || values.length < window) {
        throw new Error('values should not be null, undefined, empty or less than configured window');
    }

    const movingAverages = new Float32Array(values.length);
    for (let index = 0; index < values.length; index++) {
        if (index < window) {
            movingAverages[index] = values.subarray(0, index + 1).reduce((prev, curr) => prev + curr, 0) / (index + 1);
        } else {
            movingAverages[index] = movingAverages[index - 1] - values[index - window] / window + values[index] / window;
        }
    }

    return movingAverages;
}
