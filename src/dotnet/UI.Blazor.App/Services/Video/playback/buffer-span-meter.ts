// Smoothed buffer-span estimate for the present-stage catch-up/skip decision.
// A transient HIGH span spike must NOT trip skip-mode (skip drops frames), so we
// take a windowed MINIMUM: only a buffer that stays deep across the whole window
// raises the estimate. Two windows blended — a fast 300 ms one for responsiveness
// and a slow 1500 ms floor for stability — mirrors nimio's buffer meter.
// Each window is the monotonic-min deque from `operators/downlink-tap.ts`.

const DEFAULT_SHORT_WINDOW_MS = 300;
const DEFAULT_LONG_WINDOW_MS = 1500;
const DEFAULT_SHORT_WEIGHT = 0.6;
const DEFAULT_LONG_WEIGHT = 0.4;

export interface BufferSpanMeterOptions {
    shortWindowMs?: number;
    longWindowMs?: number;
    shortWeight?: number;
    longWeight?: number;
}

export class BufferSpanMeter {
    private readonly shortWindowMs: number;
    private readonly longWindowMs: number;
    private readonly shortWeight: number;
    private readonly longWeight: number;
    // [wallMs, spanMs] kept ascending by spanMs so front is the window minimum.
    private readonly shortDeque: [number, number][] = [];
    private readonly longDeque: [number, number][] = [];

    constructor(opts: BufferSpanMeterOptions = {}) {
        this.shortWindowMs = opts.shortWindowMs ?? DEFAULT_SHORT_WINDOW_MS;
        this.longWindowMs = opts.longWindowMs ?? DEFAULT_LONG_WINDOW_MS;
        this.shortWeight = opts.shortWeight ?? DEFAULT_SHORT_WEIGHT;
        this.longWeight = opts.longWeight ?? DEFAULT_LONG_WEIGHT;
    }

    sample(spanMs: number, nowMs: number): number {
        const shortMin = pushAndMin(this.shortDeque, spanMs, nowMs, this.shortWindowMs);
        const longMin = pushAndMin(this.longDeque, spanMs, nowMs, this.longWindowMs);
        return this.shortWeight * shortMin + this.longWeight * longMin;
    }

    reset(): void {
        this.shortDeque.length = 0;
        this.longDeque.length = 0;
    }
}

function pushAndMin(
    deque: [number, number][], spanMs: number, nowMs: number, windowMs: number,
): number {
    while (deque.length > 0 && deque[deque.length - 1][1] >= spanMs)
        deque.pop();
    deque.push([nowMs, spanMs]);
    while (deque.length > 0 && nowMs - deque[0][0] > windowMs)
        deque.shift();
    return deque[0][1];
}
