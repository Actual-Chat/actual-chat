export class ExponentialMovingAverage {
    private readonly _window: number;

    private _lastMovingAverage: number;

    constructor(window: number) {
        this._window = window;
        this._lastMovingAverage = NaN;

        if (window < 1 || window > 2000) {
            throw new Error("window should be in range [1;2000]");
        }
    }

    public get lastAverage() : number {
        return this._lastMovingAverage;
    }

    public appendChunk(values: Float32Array): Float32Array {
        if (!values || values.length < this._window) {
            throw new Error("values should not be null, undefined, empty or less than configured window");
        }
        
        const window = this._window;
        const length = values.length;
        const smoothingFactor = 2 / (window + 1);

        const exponentialMovingAverages = new Float32Array(length);
        let startIndex = window;
        if (isNaN(this._lastMovingAverage)) {
            const sma = simpleMovingAverage(values, window);
            exponentialMovingAverages.set(sma, 0);
        }
        else {
            exponentialMovingAverages[0] = this._lastMovingAverage;
            startIndex = 1;
        }
        
        for (let index = startIndex; index < values.length; index++) {
            const value = values[index];
            const previousEma = exponentialMovingAverages[index - 1];
            const currentEma = (value - previousEma) * smoothingFactor + previousEma;
            exponentialMovingAverages[index] = currentEma;
        }
        this._lastMovingAverage = exponentialMovingAverages[exponentialMovingAverages.length - 1];

        return exponentialMovingAverages;
    }
}

function simpleMovingAverage(values: Float32Array, window = 5, n = Infinity): Float32Array {
    if (!values || values.length < window) {
        throw new Error("values should not be null, undefined, empty or less than configured window");
    }

    const movingAverages = new Float32Array(values.length);
    for (let index = 0; index < values.length; index++) {
        if (index < window) {
            movingAverages[index] = values.subarray(0, index + 1).reduce((prev,curr) => prev + curr, 0) / (index + 1)
        }
        else {
            movingAverages[index] = movingAverages[index - 1] - values[index - window]/window + values[index]/window;
        }
    }
  
    return movingAverages;
}
