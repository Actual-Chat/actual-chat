// Per-tick "deficit" tracker for a codec (encoder or decoder).
// deficit = 1 - min(1, outputDelta / inputDelta), EMA-smoothed.
// 0 = codec keeps pace with source; 1 = codec emits nothing.
// Queue depth is irrelevant: a steady non-empty pipeline with matched
// throughput reports 0. Ticks with inputDelta <= 0 are skipped (no signal).
export class ThroughputDeficitTicker {
    private _value = 0;

    constructor(private readonly alpha: number) {}

    public get value(): number {
        return this._value;
    }

    public tick(outputDelta: number, inputDelta: number): number {
        if (inputDelta <= 0)
            return this._value;
        const ratio = Math.min(1, Math.max(0, outputDelta) / inputDelta);
        const deficit = 1 - ratio;
        this._value = this.alpha * deficit + (1 - this.alpha) * this._value;
        return this._value;
    }

    public reset(): void {
        this._value = 0;
    }
}
