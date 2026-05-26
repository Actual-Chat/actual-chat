import { getLogs } from 'logging';

const { infoLog } = getLogs('BgBlur');

const REPORT_EVERY = 100;

// Rolling-window percentile tracker for bg-blur render duration.
// Used identically on the WebGL main-thread painter and the WebGPU
// worker renderer so a single console grep on `[BgBlur]` lines up
// p50/p95/max side-by-side for the two paths.
//
// Wall-clock only; for the WebGPU path the work runs asynchronously on
// GPU, so `sample()` measures the encode+submit cost (the host-side
// portion you'd see on the main thread if the path were on main).
export class BgBlurPerfTracker {
    private samples: number[] = [];

    constructor(private readonly tag: string) {}

    sample(durationMs: number): void {
        this.samples.push(durationMs);
        if (this.samples.length >= REPORT_EVERY)
            this.flush();
    }

    private flush(): void {
        const sorted = this.samples.slice().sort((a, b) => a - b);
        const n = sorted.length;
        const p50 = sorted[Math.floor(n * 0.50)];
        const p95 = sorted[Math.floor(n * 0.95)];
        const max = sorted[n - 1];
        infoLog?.log(
            `${this.tag} render n=${n} `
            + `p50=${p50.toFixed(2)}ms p95=${p95.toFixed(2)}ms max=${max.toFixed(2)}ms`);
        this.samples = [];
    }
}
