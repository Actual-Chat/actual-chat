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

// Encoder-side deficit. Same ratio as above, but silence before the encoder's
// first output is startup rather than a deficit: an encoder that has emitted
// nothing scores 1.0, and two such ticks are enough to latch the sender-health
// classifier Bad, which then takes ten clean ticks to clear while QC is free to
// demote a layer. The monitor starts when the worker is asked to start, so its
// opening ticks span camera attach, worker spin-up and encoder configuration -
// frames are already being offered and nothing has come back yet. This is not a
// codec buffering frames: even Firefox emits its first VP9 or AV1 chunk after a
// single frame.
//
// The decoder side gets this for free by subtracting `decoderQueueSize` from
// arrived chunks, so its in-flight frames never count as missing; the encoder
// has no equivalent exact counter, hence the explicit startup handling.
//
// The grace period is bounded: an encoder that never produces anything must
// still read as broken rather than perfect.
export class EncodeDeficitTicker {
    private readonly inner: ThroughputDeficitTicker;
    private started = false;
    private silentTicks = 0;

    constructor(alpha: number, private readonly graceTicks = 3) {
        this.inner = new ThroughputDeficitTicker(alpha);
    }

    public get value(): number {
        return this.inner.value;
    }

    public tick(outputTotal: number, outputDelta: number, inputDelta: number): number {
        // No frames offered means no signal, so such a tick must neither charge
        // a deficit nor spend the grace period. The health monitor starts when
        // the worker is asked to start, not when frames begin to flow, so on a
        // slow camera attach these come first and would otherwise exhaust the
        // grace before the encoder had ever been handed anything.
        if (inputDelta <= 0)
            return this.inner.value;
        if (!this.started && outputTotal <= 0 && ++this.silentTicks <= this.graceTicks)
            return this.inner.value;
        if (!this.started && outputTotal > 0) {
            // The first window with output in it also spans the startup gap,
            // so it sets the baseline instead of smoothing a partial window in.
            this.started = true;
            return this.inner.value;
        }

        return this.inner.tick(outputDelta, inputDelta);
    }

    public reset(): void {
        this.inner.reset();
        this.started = false;
        this.silentTicks = 0;
    }
}
