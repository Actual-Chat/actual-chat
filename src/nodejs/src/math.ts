import Denque from 'denque';

export class Vector2D {
    public static readonly zero = new Vector2D(0, 0);
    public static readonly unitX = new Vector2D(1, 0);
    public static readonly unitY = new Vector2D(0, 1);

    constructor(public x: number, public y: number) { }

    public add(other: Vector2D): Vector2D {
        return new Vector2D(this.x + other.x, this.y + other.y);
    }

    public sub(other: Vector2D): Vector2D {
        return new Vector2D(this.x - other.x, this.y - other.y);
    }

    public mul(multiplier: number): Vector2D {
        return new Vector2D(this.x * multiplier, this.y * multiplier);
    }

    public dotProduct(other: Vector2D): number {
        return this.x * other.x + this.y * other.y;
    }

    public get length(): number {
        return Math.sqrt(this.squareLength);
    }

    public get squareLength(): number {
        return this.x * this.x + this.y * this.y;
    }

    public isHorizontal(minRatio = 1) {
        return Math.abs(this.x) > minRatio * Math.abs(this.y);
    }

    public isVertical(minRatio = 1) {
        return Math.abs(this.y) > minRatio * Math.abs(this.x);
    }
}

export function clamp(n: number, min: number, max: number) {
    return Math.max(min, Math.min(max, n));
}

export function lerp(x: number, y: number, alpha: number) {
    return x + alpha*(y - x);
}

export interface RunningCounter {
    readonly sampleCount: number;
    readonly value: number;

    reset(): void;
    appendSample(value: number): void;
}

export class RunningAverage implements RunningCounter {
    private _sampleCount = 0;
    private _sum = 0;

    constructor(private readonly defaultValue: number)
    { }

    public get sampleCount(): number {
        return this._sampleCount;
    }

    public get value(): number {
        return this._sampleCount <= 0
            ? this.defaultValue
            : this._sum / this.sampleCount;
    }

    public reset(): void {
        this._sampleCount = 0;
        this._sum = 0;
    }

    public appendSample(value: number): void {
        this._sum += value;
        this._sampleCount++;
    }

    public removeSample(value: number): void {
        if (this._sampleCount <= 0)
            return;

        this._sum -= value;
        this._sampleCount--;
    }
}

export class RunningUnitMedian implements RunningCounter {
    private readonly _halfBucketSize: number;
    private readonly _buckets: Int32Array;
    private _sampleCount = 0;
    private _value: number | null = null;

    constructor(
        bucketCount = 100,
        private readonly defaultValue = 0.5
    ) {
        this._buckets = new Int32Array(bucketCount).fill(0);
        this._halfBucketSize = 0.5 / bucketCount;
    }

    public get sampleCount(): number {
        return this._sampleCount;
    }

    public get value(): number {
        if (this._value !== null)
            return this._value;

        const halfSampleCount = this._sampleCount / 2;
        let runningCount = 0;
        for (let i = 0; i < this._buckets.length; i++) {
            runningCount += this._buckets[i];
            if (runningCount >= halfSampleCount) {
                // Ideally we should distribute the weight here
                return this._value = i / this._buckets.length + this._halfBucketSize;
            }
        }
        return this._value = this.defaultValue;
    }

    public reset(): void {
        this._buckets.fill(0);
        this._sampleCount = 0;
        this._value = null;
    }

    public appendSample(value: number): void {
        value = clamp(value, 0, 1);
        this._buckets[Math.floor(value * this._buckets.length)]++;
        this._sampleCount++;
        this._value = null;
    }
}

export class RunningMA implements RunningCounter {
    private readonly _samples = new Denque<number>();
    private readonly _average: RunningAverage;

    constructor(
        private readonly windowSize: number,
        defaultValue: number,
    ) {
        this._average = new RunningAverage(defaultValue);
    }

    public get sampleCount(): number {
        return this._average.sampleCount;
    }

    public get value(): number {
        return this._average.value;
    }

    public reset(): void {
        this._samples.clear();
        this._average.reset();
    }

    public appendSample(value: number): void {
        const samples = this._samples;
        const average = this._average;
        samples.push(value);
        average.appendSample(value);
        if (average.sampleCount > this.windowSize)
            average.removeSample(samples.shift())
    }
}

export class RunningEMA implements RunningCounter {
    private _sampleCount = 0;
    private _average: RunningAverage;
    private _value: number | null = null;

    constructor(
        defaultValue: number,
        private readonly minSampleCount: number, // Uses RunningMA unless sampleCount > minSampleCount
        private readonly smoothingFactor: number = null,
    ) {
        if (smoothingFactor === null)
            this.smoothingFactor = 2 / (minSampleCount + 1);
        this._average = new RunningAverage(defaultValue);
    }

    public get sampleCount(): number {
        return this._sampleCount;
    }

    public get value(): number {
        return this._value ?? this._average.value;
    }

    public reset(): void {
        this._sampleCount = 0;
        this._average.reset();
        this._value = null;
    }

    public appendSample(value: number): void {
        this._sampleCount++;
        const last = this._value;
        if (last === null) {
            this._average.appendSample(value);
            if (this._sampleCount >= this.minSampleCount)
                this._value = this._average.value;
        } else {
            this._value =  last + this.smoothingFactor * (value - last);
        }
    }
}
